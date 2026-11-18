using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private string _armedSitePriceEditNumber = "";
    private string _sitePriceEditDialogWarning = "";

    private static bool TrySitePrice(string? text, out long value) =>
        long.TryParse((text ?? "").Replace(",", "", StringComparison.Ordinal).Trim(), out value) && value >= 1000;

    // A local price Save is deliberately never hooked to this operation:
    // SaveSelectedJob(false) is also called by selection changes and imports.
    // Only the dedicated button may initiate a live site edit/re-registration.
    private async Task ApplySelectedSitePriceAsync()
    {
        if (_myListingsImportRunning || _busy)
        {
            MessageBox.Show(this, "등록글 조회·재등록 작업이 끝난 뒤 사이트 가격을 수정할 수 있습니다.", "사이트 가격 변경 대기");
            return;
        }
        SaveSelectedJob(false);
        var job = _selected;
        if (job is null || !job.ImportedFromSite || !ListingIdentity.HasNumber(job.ListingNumber) ||
            !MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job)))
        {
            MessageBox.Show(this, "내 등록글 불러오기로 정확한 등록번호와 구매/판매 방향이 연결된 기존 물품만 사이트 가격을 변경할 수 있습니다.",
                "수정 대상 확인");
            return;
        }
        if (!TrySitePrice(job.Price, out var desired) || desired % 10 != 0)
        {
            MessageBox.Show(this, "변경할 단가는 1,000원 이상, 10원 단위로 입력해 주세요.", "가격 확인");
            return;
        }
        if (_schedulerRunning) PauseScheduler();
        _busy = true;
        _state.Text = "사이트 가격 수정 대상 확인 중";
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = cancellation.Token;
        bool submitted = false;
        bool previousReview = job.RequiresReview;
        bool previousEnabled = job.Enabled;
        DateTime previousDue = job.NextDueUtc;
        string previousStatus = job.LastStatus;
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_web.CoreWebView2 is null) throw new InvalidOperationException("아이템매니아 브라우저를 시작하지 못했습니다.");
            if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(token);
            var url = OwnedListUrlFor(job);
            await NavigateOwnedListAsync(url, token);
            var matched = await LocateOwnedListRepostAsync(job, token, requireRepostButton: false);
            if (matched.Status != "found")
                throw new InvalidOperationException("등록번호·제목이 일치하는 본인 물품 행을 확인할 수 없습니다: " + matched.Message);
            var page = MyListingsImport.DecodePage(await _web.CoreWebView2.ExecuteScriptAsync(MyListingsImport.ExtractPageScript));
            var records = page.Listings.Where(x => x.ListingNumber == job.ListingNumber &&
                x.TradeSide == job.OriginalTradeSide &&
                MyListingsImport.IsOwnedListUrl(x.SourceListUrl)).ToList();
            if (records.Count != 1 || !TrySitePrice(records[0].Price, out var current))
                throw new InvalidOperationException("해당 등록번호의 현재 사이트 단가를 정확히 읽지 못했습니다. 수정하지 않았습니다.");
            if (current == desired)
            {
                AppendLog($"사이트 가격이 이미 동일합니다: {job.Server} · {job.ListingNumber} · {desired:N0}원 (제출 없음)");
                MessageBox.Show(this, $"{job.Server} · {job.ListingNumber}\n현재 아이템매니아 가격도 {desired:N0}원입니다.\n재등록하지 않았습니다.",
                    "이미 반영된 가격");
                return;
            }
            if (!CanRunNow(job, out var quotaReason))
                throw new InvalidOperationException("가격 변경 시 사이트 재등록이 필요합니다. " + quotaReason);
            var ask = $"아이템매니아의 실제 등록글 가격을 수정할까요?\n\n" +
                $"서버: {job.Server}\n등록번호: {job.ListingNumber}\n제목: {job.Title}\n\n" +
                $"사이트 현재 가격: {current:N0}원\n변경할 가격: {desired:N0}원\n\n" +
                "사이트의 '재등록' 버튼을 사용하므로 등록 횟수·서버별 간격 제한에 포함됩니다.\n" +
                "확인된 이 글 하나에만 반영합니다.";
            if (MessageBox.Show(this, ask, "실제 사이트 가격 수정", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            var record = records[0];
            var edit = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                SitePriceEdit.BuildOwnedRowScript(job.ListingNumber, job.Title, false)));
            if (edit.Status == "ready")
            {
                AppendLog("실제 수정화면 열기: 검증된 본인 물품 행 · " + job.ListingNumber);
                try
                {
                    edit = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.BuildOwnedRowScript(job.ListingNumber, job.Title, true)));
                    if (edit.Status != "opened")
                        throw new InvalidOperationException(edit.Message);
                }
                catch (Exception ex) when (IsAllowedCurrentPage() &&
                    !MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri))
                {
                    // WebView2 may cancel ExecuteScriptAsync as the verified
                    // row's edit button navigates. The form is checked below.
                    AppendLog("수정 페이지 이동 중 응답 종료: " + ex.GetType().Name + " (페이지 등록번호 재검증 예정)");
                }
            }
            else if (MyListingsImport.IsSafeDetailUrl(record.EditUrl, job.ListingNumber))
            {
                AppendLog("정확한 등록번호가 포함된 사이트 수정 링크 열기: " + job.ListingNumber);
                await NavigateOwnedListAsync(record.EditUrl, token);
            }
            else throw new InvalidOperationException("이 물품에 연결된 '수정' 버튼이나 정확한 수정 URL이 없습니다: " + edit.Message);

            SitePriceEditResult form = new();
            for (int attempt = 0; attempt < 9; attempt++)
            {
                await Task.Delay(attempt == 0 ? 1100 : 650, token);
                if (!IsAllowedCurrentPage()) throw new InvalidOperationException("수정 화면이 아이템매니아 공식 사이트를 벗어났습니다.");
                form = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), false)));
                if (form.Status == "ready") break;
            }
            if (form.Status != "ready")
                throw new InvalidOperationException("수정 페이지를 안전하게 확인하지 못했습니다: " + form.Message);

            // Crash-safe checkpoint immediately before the site submission.
            if (!CanRunNow(job, out quotaReason)) throw new InvalidOperationException(quotaReason);
            var submittedAt = DateTime.UtcNow;
            HourlyServerQuota.MarkAttempt(HourlyServerQuota.Current(_config.ServerHourlyCounters, job, submittedAt));
            _config.LastServerAttemptUtc[HourlyServerQuota.Key(job)] = submittedAt;
            job.RequiresReview = true;
            job.Enabled = false;
            job.LastStatus = "사이트 가격 변경 제출 중: 결과 확인 필요";
            job.NextDueUtc = DateTime.MaxValue;
            _config.Jobs = _jobs.ToList();
            _config.Save();
            _armedSitePriceEditNumber = job.ListingNumber;
            _sitePriceEditDialogWarning = "";
            _submissionVerificationPending = true;
            // At this point the site may accept the submission even if its
            // navigation interrupts ExecuteScriptAsync. Never retry a click.
            submitted = true;
            try
            {
                var submit = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), true)));
                if (submit.Status != "submitted")
                    AppendLog("사이트 가격 수정 제출 응답 불분명: " + submit.Message);
            }
            catch (Exception ex) { AppendLog("사이트 가격 수정 후 화면 전환: " + ex.GetType().Name + " (중복 제출하지 않음)"); }

            _state.Text = "사이트 가격 변경 결과 확인 중";
            var verified = await ConfirmChangedSitePriceAsync(job, desired, token);
            if (!verified)
                throw new InvalidOperationException("사이트에서 변경 단가를 확인하지 못했습니다. 중복 제출하지 않으며 해당 글만 확인 대기 상태로 둡니다." +
                    (string.IsNullOrWhiteSpace(_sitePriceEditDialogWarning) ? "" : " 사이트 메시지: " + _sitePriceEditDialogWarning));
            job.RequiresReview = previousReview;
            job.Enabled = previousEnabled && !previousReview;
            job.LastStatus = "사이트 가격 변경 확인: " + desired.ToString("N0") + "원";
            job.LastSucceededUtc = DateTime.UtcNow;
            job.InitialCompleted = true;
            job.NextDueUtc = previousDue == DateTime.MaxValue ? previousDue :
                new[] { previousDue, DateTime.UtcNow.AddMinutes(job.IntervalMinutes) }.Max();
            _config.Jobs = _jobs.ToList();
            _config.Save();
            AppendLog($"✓ 사이트 단가 변경 확인: {job.Server} · {job.ListingNumber} · {current:N0}원 → {desired:N0}원");
            MessageBox.Show(this, $"아이템매니아에서 가격 변경을 확인했습니다.\n\n" +
                $"{job.Server} · {job.ListingNumber}\n{current:N0}원 → {desired:N0}원", "사이트 가격 반영 완료");
        }
        catch (OperationCanceledException)
        {
            AppendLog("사이트 가격 수정 시간 초과. 제출 여부는 실행 기록에서 확인하세요.");
            MessageBox.Show(this, submitted ? "제출 결과를 확인하지 못했습니다. 해당 글은 확인 대기 상태입니다. 자동으로 다시 제출하지 않습니다." :
                "수정 화면 확인 시간이 초과되어 제출하지 않았습니다.", "사이트 가격 확인");
        }
        catch (Exception ex)
        {
            AppendLog("사이트 가격 수정 중단: " + job.Server + " · " + job.ListingNumber + " · " + ex.Message);
            MessageBox.Show(this, ex.Message, submitted ? "가격 변경 결과 확인 필요" : "사이트 가격 수정 중단",
                MessageBoxButtons.OK, submitted ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        finally
        {
            _armedSitePriceEditNumber = "";
            _submissionVerificationPending = false;
            if (!submitted)
            {
                job.RequiresReview = previousReview;
                job.Enabled = previousEnabled;
                job.NextDueUtc = previousDue;
                job.LastStatus = previousStatus;
                _config.Jobs = _jobs.ToList();
                _config.Save();
            }
            _busy = false;
            _state.Text = _schedulerRunning ? "자동 실행 중" : "중지/대기";
            _jobsGrid.Refresh();
            RefreshAutoSummary();
            cancellation.Dispose();
            DispatchQueuedMyListingsImport();
        }
    }

    private async Task<bool> ConfirmChangedSitePriceAsync(ListingJob job, long target, CancellationToken token)
    {
        string firstPage = job.OriginalTradeSide == "판매"
            ? MyListingsImport.SellListingsUrl : MyListingsImport.BuyListingsUrl;
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(i == 0 ? 1600 : 1100, token);
            await NavigateOwnedListAsync(firstPage, token);
            var found = await LocateOwnedListRepostAsync(job, token, requireRepostButton: false);
            if (found.Status != "found") continue;
            var page = MyListingsImport.DecodePage(await _web.CoreWebView2!.ExecuteScriptAsync(MyListingsImport.ExtractPageScript));
            var match = page.Listings.Where(r => r.ListingNumber == job.ListingNumber &&
                r.TradeSide == job.OriginalTradeSide).ToList();
            if (match.Count == 1 && TrySitePrice(match[0].Price, out var observed) && observed == target)
                return true;
        }
        return false;
    }

    private void HandleSitePriceEditScriptDialog(Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogOpeningEventArgs e)
    {
        bool official = Uri.TryCreate(e.Uri, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && ItemManiaAccountStore.IsOfficialSite(url);
        string message = (e.Message ?? "").Trim();
        bool unsafeMessage = new[] { "결제", "유료", "마일리지", "삭제", "비밀번호", "인증", "충전", "입금" }
            .Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
        bool priceRepost = message.Contains("재등록", StringComparison.Ordinal) ||
            message.Contains("가격 수정", StringComparison.Ordinal) ||
            message.Contains("물품 수정", StringComparison.Ordinal);
        if (!official || unsafeMessage || !priceRepost)
        {
            _sitePriceEditDialogWarning = "확인할 수 없는 사이트 안내: " + message[..Math.Min(80, message.Length)];
            AppendLog("사이트 가격 수정 확인창 차단: " + _sitePriceEditDialogWarning);
            return;
        }
        if (e.Kind is Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Alert or
            Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Confirm)
        {
            if (new[] { "실패", "오류", "불가", "제한", "초과", "취소", "못", "않" }.Any(message.Contains))
                _sitePriceEditDialogWarning = message[..Math.Min(100, message.Length)];
            e.Accept();
            AppendLog("사이트 가격 수정 재등록 안내 확인 (실제 결과는 관리목록 가격 재검증): " +
                message[..Math.Min(100, message.Length)]);
        }
    }
}
