using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private async Task<bool> CancelImportForManualRunAsync()
    {
        if (!_myListingsImportRunning) return true;
        AppendLog("수동 시험/실제 실행 요청: 진행 중인 읽기 전용 등록글 조회를 취소합니다.");
        var completion = _myListingsImportCompletion?.Task;
        _myListingsImportCancellation?.Cancel();
        // Stop only the read-only page navigation owned by the importer.
        // Do not interrupt a registration/submission navigation.
        try { _web.CoreWebView2?.Stop(); }
        catch (Exception ex) { AppendLog("등록글 조회 페이지 중단 대기: " + ex.GetType().Name); }
        if (completion is null) return false;
        try { await completion.WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException)
        {
            MessageBox.Show(this, "등록글 조회의 페이지 이동이 아직 끝나지 않았습니다. 잠시 후 다시 실행하세요. 조회 취소는 이미 요청했습니다.", "조회 중단 대기");
            return false;
        }
        if (_busy)
        {
            MessageBox.Show(this, "등록글 조회가 종료되는 중입니다. 잠시 후 다시 실행하세요.", "조회 중단 대기");
            return false;
        }
        return true;
    }

    // This import is read-only. It never invokes a registration or repost action.
    // Imported jobs remain disabled and require explicit review.
    // Requests are idempotent. A running submission owns the shared WebView2, so
    // do not navigate it to the My Room page until that submission finishes.
    // Pausing prevents NEW submissions; an in-flight submission is not interrupted.
    private void DispatchQueuedMyListingsImport()
    {
        if (!_pendingMyListingsImport || _busy || _schedulerRunning ||
            IsDisposed || Disposing || !IsHandleCreated) return;
        _pendingMyListingsImport = false;
        BeginInvoke((MethodInvoker)(() =>
        {
            if (IsDisposed || Disposing) return;
            _ = ImportMyRegisteredListingsAsync();
        }));
    }

    private async Task ImportMyRegisteredListingsAsync()
    {
        if (_myListingsImportRunning)
        {
            AppendLog("내 등록글 조회가 이미 진행 중입니다. 중복 실행하지 않습니다.");
            return;
        }
        // The button is also a safe pause-and-import request.
        if (_schedulerRunning) PauseScheduler();
        if (_busy)
        {
            if (!_pendingMyListingsImport)
            {
                _pendingMyListingsImport = true;
                _state.Text = "목록 조회 예약 (진행 작업 종료 대기)";
                _state.ForeColor = Color.DarkGoldenrod;
                AppendLog("내 등록글 자동 조회 예약: 진행 중인 등록/재등록 작업이 끝나면 자동으로 조회합니다. 다시 누를 필요 없습니다.");
            }
            else AppendLog("내 등록글 자동 조회는 이미 예약되어 있습니다.");
            return;
        }
        SaveSelectedJob(false);
        _myListingsImportRunning = true;
        _busy = true;
        var cancellation = new CancellationTokenSource();
        _myListingsImportCancellation = cancellation;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _myListingsImportCompletion = completion;
        _state.Text = "내 판매·구매 등록글 자동 조회 중";
        _state.ForeColor = Color.DarkBlue;
        try
        {
            AppendLog("내 등록글 자동 불러오기 시작: 아이템매니아의 판매·구매 관리 페이지를 직접 조회합니다.");
            var scan = await ReadMyListingsPagesAsync(cancellation.Token);
            await EnrichImportedListingDetailsAsync(scan, cancellation.Token);
            int reconciled = await ReconcilePendingRepostsAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var records = scan.Records;
            int incomplete = records.Count(r => string.IsNullOrWhiteSpace(r.Game) ||
                string.IsNullOrWhiteSpace(r.Server) || string.IsNullOrWhiteSpace(r.Quantity) ||
                string.IsNullOrWhiteSpace(r.Price));
            if (incomplete > 0)
                AppendLog($"참고: {incomplete}건은 게임·서버·전체수량·가격 중 일부를 사이트에서 표시하지 않습니다. 빈칸을 추측해 채우지 않으며, 내 글의 등록번호·가격이 확인된 경우에도 자동 실행은 꺼 둡니다.");
            if (records.Count == 0)
            {
                var reason = scan.LoginRequired
                    ? "아이템매니아 로그인 세션이 확인되지 않았습니다. '매니아 계정'에서 계정·자동 로그인을 확인한 뒤 다시 눌러주세요."
                    : "아이템매니아 관리 페이지에서 물품 행을 읽지 못했습니다. 사이트 구조가 달라졌거나 현재 계정에 등록글이 없을 수 있습니다.";
                AppendLog("내 등록글 자동 조회 실패: " + reason);
                MessageBox.Show(this,
                    reason + "\n\n판매·구매 관리 페이지 자동 조회: " + scan.PagesVisited + "페이지" +
                    "\n상세 결과는 아래 실행 로그에서 확인할 수 있습니다." +
                    "\n\nURL을 직접 입력할 필요는 없습니다. 물품을 새로 등록하거나 재등록하지 않았습니다.",
                    "내 등록글 자동 조회", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var account = ItemManiaAccountStore.Load();
            var plan = MyListingsImport.Prepare(records, _jobs, _defaultSubId, account.UserId);
            if (plan.NewJobs.Count == 0 && plan.Updates.Count == 0)
            {
                MessageBox.Show(this,
                    $"판매·구매 등록글 {records.Count}건 확인\n" +
                    $"이미 목록에 있음: {plan.Duplicates}건 / 정보 부족: {plan.Invalid}건\n\n" +
                    $"추가하거나 보완할 물품이 없습니다. 제출 결과 자동 확인: {reconciled}건\n" +
                    "사이트에서 확인되지 않은 값은 빈칸으로 유지됩니다. 기존 글의 일정이나 제출 보류 상태는 변경하지 않았습니다.",
                    "내 등록글 자동 조회");
                return;
            }
            string confirm = $"판매·구매 등록글 {records.Count}건 확인 ({scan.PagesVisited}페이지 검사)\n\n" +
                $"새로 가져오기: {plan.NewJobs.Count}건 / 기존 항목 빈칸 보완: {plan.Updates.Count}건\n" +
                $"이미 목록에 있음: {plan.Duplicates}건\n" +
                $"정보 부족: {plan.Invalid}건 / 일부 정보 미표시: {incomplete}건\n" +
                $"개별 상세 URL 없는 글: {plan.WithoutUrl}건 (관리 목록에서 대상 행 확인 후 재등록 시도)\n\n" +
                "가져온 글은 '기존 글 재등록' 대상으로 저장하며 자동실행은 꺼 둡니다.\n" +
                "등록번호·상세 URL·가격이 확인된 글은 불필요한 '확인 필요' 표시를 제거합니다.\n" +
                "사이트에서 확인되지 않은 글과 제출 결과가 불분명한 글의 보류는 유지합니다.\n" +
                "불러오기만으로 사이트에 등록 또는 재등록하지 않습니다.\n\n물품 목록에 추가할까요?";
            if (MessageBox.Show(this, confirm, "기존 등록글 가져오기", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var update in plan.Updates) update.Apply();
            foreach (var item in plan.NewJobs) _jobs.Add(item);
            // The editor may still contain the PRE-import empty values. Clear
            // its selection before refreshing so SaveSelectedJob cannot undo
            // the just-enriched fields during the refresh.
            _selected = null;
            _config.Jobs = _jobs.ToList();
            _config.Save();
            ResetFiltersToAll();
            RefreshFilterChoices();
            RefreshFilteredLists(plan.NewJobs.FirstOrDefault()?.Id ?? plan.Updates.First().Target.Id);
            ShowItemRegistrationView();
            AppendLog($"내 등록글 자동 조회 완료: {plan.NewJobs.Count}건 추가 · {plan.Updates.Count}건 정보 보완 · {plan.Duplicates}건 중복 제외 · 재등록 결과 확인 {reconciled}건");
            if (!scan.CompletedAllPages)
                AppendLog("주의: 일부 페이지를 읽지 못했거나 페이지 제한에 도달했습니다. 모든 등록글을 확인했다고 판단하지 마세요.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            AppendLog("내 등록글 조회 취소 완료. 진행 중이던 조회만 중단했으며 기존 물품 데이터는 유지됩니다.");
        }
        catch (Exception ex)
        {
            AppendLog("등록글 자동 조회 실패: " + ex.GetType().Name + " / " + ex.Message);
            MessageBox.Show(this,
                "등록글 자동 조회에 실패했습니다. 로그인된 아이템매니아 계정 또는 사이트 구조를 확인하세요.\n\n" + ex.Message,
                "내 등록글 자동 조회", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _myListingsImportRunning = false;
            _state.Text = "중지/대기";
            _state.ForeColor = Color.Maroon;
            if (ReferenceEquals(_myListingsImportCancellation, cancellation))
                _myListingsImportCancellation = null;
            if (ReferenceEquals(_myListingsImportCompletion, completion))
                _myListingsImportCompletion = null;
            cancellation.Dispose();
            completion.TrySetResult(true);
        }
    }

    // A post can succeed on the server even when the hidden WebView misses its
    // final alert. Never repost it again to find out. Revisit the exact owned
    // row and clear the hold only if its server-rendered timestamp changed
    // since the checkpoint saved BEFORE pressing the repost button.
    private async Task<int> ReconcilePendingRepostsAsync(CancellationToken token)
    {
        int reconciled = 0;
        var pending = _jobs.Where(j => j.RequiresReview && j.PendingRepostAttemptUtc.HasValue &&
            !string.IsNullOrWhiteSpace(j.PendingRepostBeforeStamp) &&
            ListingIdentity.HasNumber(j.ListingNumber) &&
            j.PendingRepostAttemptUtc.Value > DateTime.UtcNow.AddHours(-2))
            .Take(15).ToList();
        foreach (var job in pending)
        {
            token.ThrowIfCancellationRequested();
            var url = OwnedListUrlFor(job);
            if (!MyListingsImport.IsOwnedListUrl(url)) continue;
            try
            {
                await NavigateOwnedListAsync(url, token);
                var row = await LocateOwnedListRepostAsync(job, token);
                if (row.Status != "ready" || string.IsNullOrWhiteSpace(row.Stamp) ||
                    string.Equals(row.Stamp, job.PendingRepostBeforeStamp, StringComparison.Ordinal))
                    continue;
                job.RequiresReview = false;
                job.InitialCompleted = true;
                job.LastSucceededUtc = DateTime.UtcNow;
                job.NextDueUtc = job.Enabled ? DateTime.UtcNow.AddMinutes(Math.Max(1, job.IntervalMinutes)) : DateTime.MaxValue;
                job.LastStatus = "재등록 시각 변경 확인 · 보류 자동 해제";
                job.PendingRepostBeforeStamp = "";
                job.PendingRepostAttemptUtc = null;
                reconciled++;
                _config.Save();
                AppendLog("보류 자동 해제: 정확한 등록번호 " + job.ListingNumber + "의 사이트 재등록 시각 변경 확인");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { AppendLog("보류 재검증 실패: " + job.ListingNumber + " / " + ex.GetType().Name); }
        }
        if (reconciled > 0) { _jobsGrid.Refresh(); _autoGrid.Refresh(); RefreshAutoSummary(); }
        return reconciled;
    }

    private sealed class MyListingsScan
    {
        public List<ExistingListingRecord> Records { get; } = new();
        public bool LoginRequired { get; set; }
        public bool CompletedAllPages { get; set; } = true;
        public int PagesVisited { get; set; }
    }

    private async Task<MyListingsScan> ReadMyListingsPagesAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync());
        if (_web.CoreWebView2 is null) throw new InvalidOperationException("내부 아이템매니아 사이트 엔진을 시작할 수 없습니다.");

        var result = new MyListingsScan();
        // These URLs are the verified SELL/BUY links in the official homepage's
        // '마이룸' menu. No guessing, no public search page, no manual URL dialog.
        var urls = new Queue<string>(new[] { MyListingsImport.SellListingsUrl, MyListingsImport.BuyListingsUrl });
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retriedLogin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int maxPages = 60;
        while (urls.Count > 0 && result.PagesVisited < maxPages)
        {
            token.ThrowIfCancellationRequested();
            var url = urls.Dequeue();
            if (!MyListingsImport.IsOwnedListUrl(url) || !seen.Add(url)) continue;
            result.PagesVisited++;
            try
            {
                AppendLog("내 등록글 조회: " + (MyListingsImport.IsSellListUrl(url) ? "판매" : "구매") + " 목록 " + result.PagesVisited + "페이지");
                _state.Text = $"내 등록글 조회 중 · {result.PagesVisited}페이지";
                await NavigateOwnedListAsync(url, token);
                ExistingListingPage page = new();
                // Async rendering is common: give a list a few chances to appear.
                // Read only. Avoid clicking registration/deletion/confirmation buttons.
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 500 : 900, token);
                    if (_web.CoreWebView2 is null) throw new InvalidOperationException("사이트 연결이 종료됐습니다.");
                    string raw = await _web.CoreWebView2.ExecuteScriptAsync(MyListingsImport.ExtractPageScript);
                    page = MyListingsImport.DecodePage(raw);
                    if (page.Listings.Count > 0 || page.IsEmptyList || page.LoginRequired) break;
                }
                if (page.LoginRequired)
                {
                    result.LoginRequired = true;
                    result.CompletedAllPages = false;
                    AppendLog("관리 페이지에 로그인 필요 표시가 있습니다. 자동 로그인 상태를 한 번 더 확인합니다.");
                    if (retriedLogin.Add(url))
                    {
                        // Auto-login listens for page navigation; allow its
                        // first attempt to finish, then revisit this OWN page.
                        await Task.Delay(3000, token);
                        seen.Remove(url);
                        urls.Enqueue(url);
                    }
                    continue;
                }
                if (!MyListingsImport.IsOwnedListUrl(page.PageUrl))
                {
                    result.CompletedAllPages = false;
                    AppendLog("목록을 읽지 않음: 로그인 전환 또는 다른 경로로 이동함");
                    continue;
                }
                AppendLog($"조회 결과: {page.Listings.Count}건" +
                    (string.IsNullOrWhiteSpace(page.Error) ? "" : " / " + page.Error));
                foreach (var record in page.Listings.Take(1000))
                {
                    // Do not let two pages with the same record create duplicates.
                    var id = !string.IsNullOrWhiteSpace(record.ListingNumber)
                        ? record.ListingNumber.Trim()
                        : record.ListingUrl.Trim();
                    if (id.Length > 0 && ids.Add(id)) result.Records.Add(record);
                }
                foreach (var next in page.NextPageUrls.Take(30))
                    if (MyListingsImport.IsOwnedListUrl(next) && !seen.Contains(next)) urls.Enqueue(next);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                result.CompletedAllPages = false;
                AppendLog("내 등록글 페이지 조회 오류: " + ex.GetType().Name + " / " + ex.Message);
            }
        }
        if (urls.Count > 0)
        {
            result.CompletedAllPages = false;
            AppendLog($"목록 조회를 {maxPages}페이지에서 중단했습니다. 일부 페이지는 누락될 수 있습니다.");
        }
        return result;
    }


    // Optional read-only enrichment from confirmed detail links. A list may
    // contain only title+registration number while the actual unit price,
    // game, server, quantity, character or description are on the detail page.
    private async Task EnrichImportedListingDetailsAsync(MyListingsScan scan, CancellationToken token)
    {
        if (_web.CoreWebView2 is null) return;
        var needing = scan.Records.Where(r =>
            (string.IsNullOrWhiteSpace(r.Game) || string.IsNullOrWhiteSpace(r.Server) ||
             string.IsNullOrWhiteSpace(r.Quantity) || string.IsNullOrWhiteSpace(r.Price) ||
             string.IsNullOrWhiteSpace(r.ItemCategory) || string.IsNullOrWhiteSpace(r.TradingMethod) ||
             string.IsNullOrWhiteSpace(r.Character) || string.IsNullOrWhiteSpace(r.Description) ||
             string.IsNullOrWhiteSpace(r.MinQuantity) || string.IsNullOrWhiteSpace(r.MaxQuantity)) &&
            MyListingsImport.IsSafeDetailUrl(r.ListingUrl, r.ListingNumber)).ToList();
        const int maxDetailPages = 80;
        if (needing.Count == 0) return;
        // Do not fetch the same first 80 rows forever when the account has
        // hundreds of listings. Continue from the last checked offset.
        int start = Math.Max(0, _config.DetailEnrichmentCursor) % needing.Count;
        int batchSize = Math.Min(needing.Count, maxDetailPages);
        int visits = 0, updated = 0;
        for (int offset = 0; offset < batchSize; offset++)
        {
            var record = needing[(start + offset) % needing.Count];
            token.ThrowIfCancellationRequested();
            visits++;
            try
            {
                _state.Text = $"내 등록글 상세 확인 중 · {visits}/{Math.Min(needing.Count, maxDetailPages)}";
                await NavigateOwnedListAsync(record.ListingUrl, token);
                await Task.Delay(450, token);
                var raw = await _web.CoreWebView2.ExecuteScriptAsync(
                    MyListingsImport.BuildDetailScript(record.ListingNumber));
                var detail = MyListingsImport.DecodeDetail(raw);
                // Do not mistake another item's page for this listing.
                if (!string.Equals(detail.ListingNumber, record.ListingNumber, StringComparison.Ordinal))
                    continue;
                int before = new[] { record.Game, record.Server, record.Quantity, record.Price,
                    record.ItemCategory, record.TradingMethod, record.MinQuantity, record.MaxQuantity,
                    record.Character, record.Description }
                    .Count(string.IsNullOrWhiteSpace);
                if (string.IsNullOrWhiteSpace(record.Game)) record.Game = detail.Game;
                if (string.IsNullOrWhiteSpace(record.Server)) record.Server = detail.Server;
                if (string.IsNullOrWhiteSpace(record.Quantity)) record.Quantity = detail.Quantity;
                if (string.IsNullOrWhiteSpace(record.Price)) record.Price = detail.Price;
                if (string.IsNullOrWhiteSpace(record.ItemCategory)) record.ItemCategory = detail.ItemCategory;
                if (string.IsNullOrWhiteSpace(record.TradingMethod)) record.TradingMethod = detail.TradingMethod;
                if (string.IsNullOrWhiteSpace(record.MinQuantity)) record.MinQuantity = detail.MinQuantity;
                if (string.IsNullOrWhiteSpace(record.MaxQuantity)) record.MaxQuantity = detail.MaxQuantity;
                if (string.IsNullOrWhiteSpace(record.Character)) record.Character = detail.Character;
                if (string.IsNullOrWhiteSpace(record.Description)) record.Description = detail.Description;
                int after = new[] { record.Game, record.Server, record.Quantity, record.Price,
                    record.ItemCategory, record.TradingMethod, record.MinQuantity, record.MaxQuantity,
                    record.Character, record.Description }
                    .Count(string.IsNullOrWhiteSpace);
                if (after < before) updated++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                AppendLog("기존 글 상세정보 조회 건너뜀: 등록번호 " + record.ListingNumber +
                    " / " + ex.GetType().Name);
            }
        }
        _config.DetailEnrichmentCursor = (start + batchSize) % needing.Count;
        _config.Save();
        if (visits > 0) AppendLog($"등록글 상세 화면 {visits}건 읽음 · {updated}건 정보 보완");
        if (needing.Count > maxDetailPages)
            AppendLog($"상세정보 조회는 한번에 최대 {maxDetailPages}건까지 수행합니다. " +
                "나머지 등록글은 재등록 전에 직접 확인하세요.");
    }

    private async Task NavigateOwnedListAsync(string url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!MyListingsImport.IsOfficialUrl(url) || _web.CoreWebView2 is null)
            throw new InvalidOperationException("공식 아이템매니아 주소가 아닙니다.");
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, e) => done.TrySetResult(e.IsSuccess);
        _web.NavigationCompleted += handler;
        try
        {
            _web.CoreWebView2.Navigate(url);
            if (!await done.Task.WaitAsync(TimeSpan.FromSeconds(25), token))
                throw new InvalidOperationException("아이템매니아 페이지로 이동하지 못했습니다.");
            if (!MyListingsImport.IsOfficialUrl(_web.Source?.AbsoluteUri))
                throw new InvalidOperationException("조회 도중 공식 사이트를 벗어났습니다.");
        }
        finally { _web.NavigationCompleted -= handler; }
    }
}
