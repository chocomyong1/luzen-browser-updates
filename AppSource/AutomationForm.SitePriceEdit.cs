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
    private string _lastSitePriceEditFailure = "";

    private static bool TrySitePrice(string? text, out long value) =>
        long.TryParse((text ?? "").Replace(",", "", StringComparison.Ordinal).Trim(), out value) && value >= 1000;

    // A local price Save is deliberately never hooked to this operation:
    // SaveSelectedJob(false) is also called by selection changes and imports.
    // Only the dedicated button may initiate a live site edit/re-registration.
    private async Task ApplySelectedSitePriceAsync() => await ApplySitePriceAsync(null, false);

    // A batch uses the same independently verified single-listing transaction.
    // Never parallelize submissions: the WebView2 and server quota are shared.
    private async Task ApplySelectedSitePricesAsync()
    {
        MessageBox.Show(this,
            "실제 사이트의 '수정하기'가 캐릭터명 저장 버튼과 혼동될 수 있어 일괄 가격 변경을 일시 중단했습니다.\n\n" +
            "현재 글의 '사이트 가격 버튼 진단'만 사용할 수 있으며 가격 입력·제출은 전혀 하지 않습니다.",
            "사이트 일괄 가격 변경 안전 중단", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
#pragma warning disable CS0162 // Keep the batch workflow for re-enabling only after control verification.
        if (_myListingsImportRunning || _busy)
        {
            MessageBox.Show(this, "진행 중인 조회·등록 작업이 끝난 뒤 실행하세요.", "사이트 일괄 가격 반영");
            return;
        }
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        SaveSelectedJob(false);
        var targets = _filteredJobs.Where(j => j.SelectedForBulk).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "사이트 가격을 변경할 물품의 왼쪽 체크박스를 선택해 주세요.", "선택한 물품 없음");
            return;
        }
        if (targets.Count > 20)
        {
            MessageBox.Show(this, "안전을 위해 한 번에 최대 20개만 사이트에 반영할 수 있습니다. 조회 조건을 좁혀 나누어 실행하세요.",
                "일괄 수정 수량 제한");
            return;
        }
        var invalid = targets.Where(j => !j.ImportedFromSite || !ListingIdentity.HasNumber(j.ListingNumber) ||
            !MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(j)) ||
            !TrySitePrice(j.Price, out var p) || p % 10 != 0).ToList();
        if (invalid.Count > 0)
        {
            MessageBox.Show(this, $"선택 물품 중 {invalid.Count}건은 본인 등록번호·구매/판매 방향·가격이 확인되지 않았습니다.\n" +
                "내 등록글 불러오기에서 확인된 물품만 선택한 뒤 다시 시도하세요.", "사이트 일괄 가격 반영 중단");
            return;
        }
        var preview = string.Join("\n", targets.Take(12).Select(j =>
            $"{j.Server} · {j.ListingNumber} → {j.Price}원"));
        if (targets.Count > 12) preview += $"\n외 {targets.Count - 12}건";
        if (MessageBox.Show(this,
            $"선택한 {targets.Count}건의 가격을 아이템매니아 실제 등록글에 순서대로 반영할까요?\n\n{preview}\n\n" +
            "각 물품의 등록번호·제목·현재 사이트 가격을 다시 확인하며, 한 건이라도 실패하거나 결과가 불확실하면 즉시 중단합니다.\n" +
            "재등록 횟수·서버 간격 제한이 적용됩니다.",
            "실제 사이트 가격 일괄 변경", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (_schedulerRunning) PauseScheduler();
        int finished = 0;
        string stoppedAt = "";
        foreach (var job in targets)
        {
            if (!await ApplySitePriceAsync(job, true))
            {
                stoppedAt = $"\n실패 물품: {job.Server} · {job.ListingNumber}\n" +
                    (_lastSitePriceEditFailure.Length > 0 ? _lastSitePriceEditFailure : "원인 미확인 (오류 로그 확인)");
                break;
            }
            finished++;
        }
        MessageBox.Show(this, $"선택 {targets.Count}건 중 {finished}건 확인 완료.\n" +
            (finished == targets.Count ? "모든 물품을 확인했습니다." :
                "확인하지 못한 물품이 있어 중단했습니다. 남은 글은 제출하지 않았습니다." + stoppedAt),
            "사이트 일괄 가격 반영 결과", MessageBoxButtons.OK,
            finished == targets.Count ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
#pragma warning restore CS0162
    }

    private async Task<bool> ApplySitePriceAsync(ListingJob? explicitJob, bool bulkApproval)
    {
        _lastSitePriceEditFailure = "";
        if (_myListingsImportRunning || _busy)
        {
            _lastSitePriceEditFailure = "다른 등록·조회 작업이 진행 중입니다";
            if (!bulkApproval) MessageBox.Show(this, "등록글 조회·재등록 작업이 끝난 뒤 사이트 가격을 수정할 수 있습니다.", "사이트 가격 변경 대기");
            return false;
        }
        if (!bulkApproval) SaveSelectedJob(false);
        var job = explicitJob ?? _selected;
        if (job is null || !job.ImportedFromSite || !ListingIdentity.HasNumber(job.ListingNumber) ||
            !MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job)))
        {
            _lastSitePriceEditFailure = "본인 등록글의 등록번호·거래 방향을 확인할 수 없습니다";
            if (!bulkApproval) MessageBox.Show(this, "내 등록글 불러오기로 정확한 등록번호와 구매/판매 방향이 연결된 기존 물품만 사이트 가격을 변경할 수 있습니다.",
                "수정 대상 확인");
            return false;
        }
        if (!TrySitePrice(job.Price, out var desired) || desired % 10 != 0)
        {
            _lastSitePriceEditFailure = "변경 가격은 1,000원 이상, 10원 단위여야 합니다";
            if (!bulkApproval) MessageBox.Show(this, "변경할 단가는 1,000원 이상, 10원 단위로 입력해 주세요.", "가격 확인");
            return false;
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
                if (!bulkApproval) MessageBox.Show(this, $"{job.Server} · {job.ListingNumber}\n현재 아이템매니아 가격도 {desired:N0}원입니다.\n재등록하지 않았습니다.",
                    "이미 반영된 가격");
                return true;
            }
            if (job.OriginalTradeSide != "구매")
                throw new InvalidOperationException("현재는 실제 수정 버튼이 확인된 구매글만 1건씩 가격 반영할 수 있습니다. 판매글은 제출하지 않았습니다.");
            if (!CanRunNow(job, out var quotaReason))
                throw new InvalidOperationException("가격 변경 시 등록 작업 간격을 확인해야 합니다. " + quotaReason);
            var ask = $"아이템매니아 구매 등록글 1건의 가격을 변경할까요?\n\n" +
                $"서버: {job.Server}\n등록번호: {job.ListingNumber}\n제목: {job.Title}\n\n" +
                $"사이트 현재 가격: {current:N0}원\n변경할 가격: {desired:N0}원\n\n" +
                "사이트의 구매 재등록 화면(frmBuy)에서 단가를 바꾼 뒤 '재등록' 버튼을 사용합니다.\n" +
                "등록번호가 바뀔 수 있어 결과가 불확실하면 자동 실행을 보류합니다.\n" +
                "재등록 횟수·서버 간격 제한에 포함되며 중복 제출하지 않습니다.";
            if (!bulkApproval && MessageBox.Show(this, ask, "실제 사이트 구매글 가격 변경", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;

            var record = records[0];
            bool hasDetailUrl = MyListingsImport.IsSafeDetailUrl(record.ListingUrl, job.ListingNumber);
            bool hasEditUrl = MyListingsImport.IsSafeDetailUrl(record.EditUrl, job.ListingNumber);
            if (hasDetailUrl || !hasEditUrl)
            {
                AppendLog("실제 수정 화면 경로: 본인 등록글의 제목을 열고 상세 화면에서 수정하기 탐색 · " + job.ListingNumber);
                var titleLink = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildOwnedTitleScript(job.ListingNumber, job.Title, false)));
                if (titleLink.Status == "ready")
                {
                    // My Room may render clickable titles without an href.
                    // Resolve the exact row by ID+title and click only its title.
                    try
                    {
                        titleLink = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                            SitePriceEdit.BuildOwnedTitleScript(job.ListingNumber, job.Title, true)));
                        if (titleLink.Status != "opened") throw new InvalidOperationException(titleLink.Message);
                    }
                    catch (Exception ex) when (IsAllowedCurrentPage() &&
                        !MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri))
                    {
                        AppendLog("제목 클릭 후 상세 페이지 이동 중 응답 종료: " + ex.GetType().Name);
                    }
                    // A site popup can be transferred into our WebView2 a
                    // moment later. If it did not navigate, use only the
                    // already verified exact-ID detail URL as fallback.
                    await Task.Delay(950, token);
                    if (MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri) && hasDetailUrl)
                    {
                        AppendLog("제목 클릭 뒤 목록 유지: 등록번호가 포함된 상세 URL로 한 번 이동");
                        await NavigateOwnedListAsync(record.ListingUrl, token);
                    }
                }
                else if (hasDetailUrl)
                {
                    AppendLog("제목 링크 직접 클릭 불가: 등록번호 포함 상세 URL로 이동 · " + titleLink.Message);
                    await NavigateOwnedListAsync(record.ListingUrl, token);
                }
                else throw new InvalidOperationException("목록 제목을 열지 못했고 정확한 상세 URL도 없습니다: " + titleLink.Message);
                SitePriceEditResult detail = new();
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 1100 : 650, token);
                    if (!IsAllowedCurrentPage())
                        throw new InvalidOperationException("상세 화면이 아이템매니아 공식 사이트를 벗어났습니다");
                    detail = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.BuildDetailEditScript(job.ListingNumber, job.Title, false)));
                    if (detail.Status == "ready") break;
                }
                if (detail.Status != "ready")
                    throw new InvalidOperationException("등록글 상세 화면에서 수정하기를 확인하지 못했습니다 (현재 경로: " +
                        (_web.Source?.AbsolutePath ?? "없음") + "): " + detail.Message);
                try
                {
                    detail = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.BuildDetailEditScript(job.ListingNumber, job.Title, true)));
                    if (detail.Status != "opened") throw new InvalidOperationException(detail.Message);
                }
                catch (Exception ex) when (IsAllowedCurrentPage() &&
                    !string.Equals(_web.Source?.AbsoluteUri, record.ListingUrl, StringComparison.OrdinalIgnoreCase) &&
                    !MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri))
                {
                    AppendLog("상세 화면 수정하기 전환 중 응답 종료: " + ex.GetType().Name + " (수정 폼 별도 검증)");
                }
            }
            else if (hasEditUrl)
            {
                AppendLog("정확한 등록번호가 포함된 사이트 수정 링크 열기: " + job.ListingNumber);
                await NavigateOwnedListAsync(record.EditUrl, token);
            }
            else throw new InvalidOperationException("본인 등록글 제목에 연결된 정확한 상세 URL이 없습니다. 안전한 경로를 확인하지 못해 수정하지 않았습니다.");

            SitePriceEditResult form = new();
            for (int attempt = 0; attempt < 9; attempt++)
            {
                await Task.Delay(attempt == 0 ? 1100 : 650, token);
                if (!IsAllowedCurrentPage()) throw new InvalidOperationException("수정 화면이 아이템매니아 공식 사이트를 벗어났습니다.");
                form = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), false)));
                if (form.Status is "ready" or "diagnostic") break;
            }
            if (form.Status == "diagnostic")
            {
                var diagnosis = $"[사이트 가격 버튼 진단]\n등록번호: {job.ListingNumber}\n" + form.Message;
                AppendLog("사이트 가격 버튼 진단 완료 (제출 없음): " + job.ListingNumber + "\n" + form.Message);
                _lastSitePriceEditFailure = "읽기 전용 가격 버튼 진단 완료 (가격 미제출)";
                if (!bulkApproval && MessageBox.Show(this,
                    $"읽기 전용 진단을 완료했습니다. 버튼 {form.Candidates}개를 조사했으며 " +
                    "가격·캐릭터명은 변경하지 않았습니다.\n\n" +
                    "가격 수정 기능은 안전 점검이 끝날 때까지 잠겨 있습니다.\n\n" +
                    "진단 내용을 클립보드에 복사할까요?",
                    "사이트 가격 버튼 진단 완료", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                {
                    try { Clipboard.SetText(diagnosis); }
                    catch (Exception ex)
                    {
                        AppendLog("사이트 가격 버튼 진단 복사 실패: " + ex.GetType().Name);
                        MessageBox.Show(this, "클립보드에 복사하지 못했습니다. 오류 기록 복사 기능을 사용해 주세요.",
                            "진단 복사 실패");
                    }
                }
                return false;
            }
            if (form.Status != "ready")
                throw new InvalidOperationException("수정 페이지를 안전하게 확인하지 못했습니다: " + form.Message);

            // Commit a crash-safe review checkpoint BEFORE touching the site.
            // A navigation can abort ExecuteScriptAsync after the site accepts;
            // never retry a submission when its result is uncertain.
            if (!CanRunNow(job, out quotaReason)) throw new InvalidOperationException(quotaReason);
            var submittedAt = DateTime.UtcNow;
            HourlyServerQuota.MarkAttempt(HourlyServerQuota.Current(_config.ServerHourlyCounters, job, submittedAt));
            _config.LastServerAttemptUtc[HourlyServerQuota.Key(job)] = submittedAt;
            job.RequiresReview = true;
            job.Enabled = false;
            job.LastStatus = "구매글 가격 재등록 제출 중: 결과 확인 필요";
            job.NextDueUtc = DateTime.MaxValue;
            _config.Jobs = _jobs.ToList();
            _config.Save();
            _armedSitePriceEditNumber = job.ListingNumber;
            _sitePriceEditDialogWarning = "";
            _submissionVerificationPending = true;
            submitted = true;
            try
            {
                var result = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), true)));
                if (result.Status != "submitted")
                    AppendLog("구매글 가격 수정 제출 결과 불확실 (자동 재시도 금지): " + result.Message);
            }
            catch (Exception ex)
            {
                AppendLog("구매글 가격 수정 뒤 화면 전환: " + ex.GetType().Name + " (중복 제출 금지)");
            }

            _state.Text = "구매글 가격 변경 결과 확인 중";
            if (!await ConfirmChangedSitePriceAsync(job, desired, token))
                throw new InvalidOperationException("구매글 재등록 후 변경된 사이트 단가를 확인하지 못했습니다. " +
                    "새 등록번호가 생겼을 수도 있으므로 이 글은 확인 대기 상태로 두고 자동으로 다시 제출하지 않습니다." +
                    (string.IsNullOrWhiteSpace(_sitePriceEditDialogWarning) ? "" :
                        " 사이트 메시지: " + _sitePriceEditDialogWarning));

            job.RequiresReview = previousReview;
            job.Enabled = previousEnabled && !previousReview;
            job.LastStatus = "사이트 구매글 단가 변경 확인: " + desired.ToString("N0") + "원";
            job.LastSucceededUtc = DateTime.UtcNow;
            job.InitialCompleted = true;
            job.NextDueUtc = previousDue == DateTime.MaxValue ? previousDue :
                new[] { previousDue, DateTime.UtcNow.AddMinutes(job.IntervalMinutes) }.Max();
            _config.Jobs = _jobs.ToList();
            _config.Save();
            AppendLog($"✓ 사이트 구매글 단가 확인: {job.Server} · {job.ListingNumber} · {current:N0}원 → {desired:N0}원");
            if (!bulkApproval) MessageBox.Show(this,
                $"사이트에서 구매글 단가 변경을 확인했습니다.\n\n{job.Server} · {job.ListingNumber}\n" +
                $"{current:N0}원 → {desired:N0}원", "사이트 가격 반영 완료");
            return true;
        }
        catch (OperationCanceledException)
        {
            _lastSitePriceEditFailure = submitted ? "제출 후 변경 여부를 확인하지 못했습니다. 중복 제출 금지" :
                "수정 화면 확인 시간 초과 (제출 전 중단)";
            AppendLog("사이트 가격 수정 시간 초과. 제출 여부는 실행 기록에서 확인하세요.");
            if (!bulkApproval) MessageBox.Show(this, submitted ? "제출 결과를 확인하지 못했습니다. 해당 글은 확인 대기 상태입니다. 자동으로 다시 제출하지 않습니다." :
                "수정 화면 확인 시간이 초과되어 제출하지 않았습니다.", "사이트 가격 확인");
            return false;
        }
        catch (Exception ex)
        {
            _lastSitePriceEditFailure = ex.Message;
            AppendLog("사이트 가격 수정 중단: " + job.Server + " · " + job.ListingNumber + " · " + ex.Message);
            if (!bulkApproval) MessageBox.Show(this, ex.Message, submitted ? "가격 변경 결과 확인 필요" : "사이트 가격 수정 중단",
                MessageBoxButtons.OK, submitted ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            return false;
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
            message.Contains("물품 수정", StringComparison.Ordinal) ||
            message.Contains("수정", StringComparison.Ordinal);
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
