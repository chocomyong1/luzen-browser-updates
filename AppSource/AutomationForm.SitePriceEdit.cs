using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private string _armedSitePriceEditNumber = "";
    private bool _unattendedSitePriceEdit;
    private Form? _visibleSitePriceEditWindow;
    private string _sitePriceEditDialogWarning = "";
    private string _lastSitePriceEditFailure = "";

    // The official BUY editor's FormChecker can reject a submit before any
    // network request. Keep the actual site visible for this single manual
    // operation so its native validation messages can be corrected in place.
    private DialogResult ShowVisibleSinglePriceEditor(ListingJob job, long current, long desired,
        SitePriceSubmitObserver observer)
    {
        var originalParent = _web.Parent ?? throw new InvalidOperationException("사이트 브라우저의 원래 창을 찾지 못했습니다.");
        var originalBounds = _web.Bounds;
        var originalDock = _web.Dock;
        using var window = new Form
        {
            Text = "등록번호 " + job.ListingNumber + " · 사이트 가격 수정",
            Width = 1250, Height = 880, MinimumSize = new Size(800, 600),
            StartPosition = FormStartPosition.CenterParent
        };
        var instructions = new Label
        {
            Dock = DockStyle.Top, Height = 65, Padding = new Padding(15, 10, 15, 4),
            Text = $"등록번호 {job.ListingNumber}  |  {current:N0}원 → {desired:N0}원\n" +
                "사이트의 [재등록]을 직접 누르세요. 사이트 안내가 뜨면 확인한 뒤 아래 [결과 확인]을 누르세요."
        };
        var controls = new Panel { Dock = DockStyle.Bottom, Height = 57, Padding = new Padding(8) };
        var verify = new Button { Text = "사이트 제출 후 결과 확인", Width = 220, Height = 36,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, Left = 770, Top = 9 };
        var close = new Button { Text = "창 닫기", Width = 112, Height = 36,
            Anchor = AnchorStyles.Top | AnchorStyles.Right, Left = 1000, Top = 9 };
        controls.Resize += (_, _) =>
        {
            close.Left = controls.ClientSize.Width - close.Width - 12;
            verify.Left = close.Left - verify.Width - 10;
        };
        verify.Click += async (_, _) =>
        {
            verify.Enabled = false;
            try
            {
                // Contact/category AJAX is asynchronous. A canceled DOM0
                // submit is not itself proof that the site has finished.
                if (!observer.FinalPostStarted) await Task.Delay(1600);
                if (!observer.FinalPostStarted)
                {
                    MessageBox.Show(window,
                        "사이트의 최종 가격 수정 요청이 아직 확인되지 않았습니다.\n\n" +
                        "수정 화면에 오류나 확인 안내가 있는지 살펴보고 사이트 [재등록] 버튼을 눌러 주세요.\n" +
                        "이 창에서는 제출 버튼을 대신 누르지 않습니다.",
                        "사이트 제출 대기", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                window.DialogResult = DialogResult.OK;
                window.Close();
            }
            finally { if (!window.IsDisposed) verify.Enabled = true; }
        };
        close.Click += (_, _) => { window.DialogResult = DialogResult.Cancel; window.Close(); };
        controls.Controls.AddRange(new Control[] { verify, close });
        var browserHost = new Panel { Dock = DockStyle.Fill };
        window.Controls.Add(browserHost);
        window.Controls.Add(controls);
        window.Controls.Add(instructions);
        try
        {
            originalParent.Controls.Remove(_web);
            _web.Dock = DockStyle.Fill;
            browserHost.Controls.Add(_web);
            _visibleSitePriceEditWindow = window;
            return window.ShowDialog(this);
        }
        finally
        {
            _visibleSitePriceEditWindow = null;
            browserHost.Controls.Remove(_web);
            _web.Dock = originalDock;
            originalParent.Controls.Add(_web);
            _web.Bounds = originalBounds;
            _web.Visible = true;
        }
    }

    private void HandleVisibleSitePriceDialog(Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogOpeningEventArgs e)
    {
        if (_visibleSitePriceEditWindow is not Form window) return;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !ItemManiaAccountStore.IsOfficialSite(uri)) return;
        var message = (e.Message ?? "").Trim();
        if (message.Length > 1000) message = message[..1000] + "…";
        if (e.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Alert)
        {
            MessageBox.Show(window, message, "아이템매니아 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Accept();
        }
        else if (e.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Confirm)
        {
            if (MessageBox.Show(window, message, "아이템매니아 확인", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) e.Accept();
        }
    }

    private string _sitePriceReadbackNote = "";
    private SitePriceReadback? _sitePriceLastReadback;
    // An in-flight site price target is deferred until its readback completes.
    // Other listings may replace their local reservation without site access.
    private ListingJob? _activeSitePriceJob;
    private bool _sitePriceBatchRunning;
    private CancellationTokenSource? _sitePriceBatchCancellation;
    private Action<string>? _reportSitePriceBatchStage;
    public event Action<string, bool, string?>? SitePriceBatchProgress;

    internal void RequestStopSitePriceBatch()
    {
        var cancellation = _sitePriceBatchCancellation;
        if (cancellation is null || cancellation.IsCancellationRequested) return;
        cancellation.Cancel();
        _reportSitePriceBatchStage?.Invoke("중지 요청 · 현재 건의 결과 확인 후 종료");
    }

    private static bool TrySitePrice(string? text, out long value) =>
        long.TryParse((text ?? "").Replace(",", "", StringComparison.Ordinal).Trim(), out value) && value >= 1000;

    // Type as a user would: focus the audited price control, select its
    // existing value, deliver browser key-down/up events, then blur. Site
    // validators bound to real keyboard/change events can now run normally.
    // This method never presses the re-registration button.
    private async Task TypeVerifiedSitePriceAsync(double x, double y, long desired, CancellationToken token)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x <= 0 || y <= 0 ||
            desired < 1000 || desired % 10 != 0)
            throw new InvalidOperationException("사이트 가격 입력 조건을 확인하지 못했습니다");
        var core = _web.CoreWebView2 ?? throw new InvalidOperationException("사이트 브라우저 연결 없음");
        async Task MouseAsync(string type, string button, int buttons, int clicks) =>
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                System.Text.Json.JsonSerializer.Serialize(new { type, x, y, button, buttons, clickCount = clicks }));
        async Task KeyAsync(string type, string key, string code, int vk, int modifiers = 0,
            string text = "") =>
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                System.Text.Json.JsonSerializer.Serialize(new
                { type, key, code, windowsVirtualKeyCode = vk, modifiers, text, unmodifiedText = text }));

        token.ThrowIfCancellationRequested();
        await MouseAsync("mouseMoved", "none", 0, 0);
        if (!string.Equals(await core.ExecuteScriptAsync(SitePriceEdit.VerifyNativePriceInputScript),
                "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("가격 입력 직전 대상 입력칸 검증 실패. 제출하지 않았습니다");
        await MouseAsync("mousePressed", "left", 1, 1);
        await MouseAsync("mouseReleased", "left", 0, 1);
        if (!string.Equals(await core.ExecuteScriptAsync(SitePriceEdit.VerifyNativePriceFocusScript),
                "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("사이트 가격 입력칸에 키보드 포커스를 확인하지 못해 제출하지 않았습니다");
        var selection = SitePriceEdit.Decode(await core.ExecuteScriptAsync(
            SitePriceEdit.SelectNativePriceForReplacementScript));
        if (selection.Status != "retry-ready")
            throw new InvalidOperationException("사이트 가격칸의 기존 금액을 모두 선택하지 못했습니다: " + selection.Message);
        foreach (char digit in desired.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            token.ThrowIfCancellationRequested();
            string key = digit.ToString();
            await KeyAsync("keyDown", key, "Digit" + key, 48 + (digit - '0'), text: key);
            await KeyAsync("keyUp", key, "Digit" + key, 48 + (digit - '0'));
        }
        await KeyAsync("keyDown", "Tab", "Tab", 9);
        await KeyAsync("keyUp", "Tab", "Tab", 9);
    }

    private async Task ReplaceVerifiedSitePriceTextOnceAsync(long desired, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var core = _web.CoreWebView2 ?? throw new InvalidOperationException("사이트 브라우저 연결 없음");
        var selection = SitePriceEdit.Decode(await core.ExecuteScriptAsync(
            SitePriceEdit.SelectNativePriceForReplacementScript));
        if (selection.Status != "retry-ready")
            throw new InvalidOperationException("사이트 가격칸 안전한 전체 선택 실패: " + selection.Message);
        // Input.insertText replaces a proven selected range through the
        // browser's input path; it never bypasses a site's submit handler.
        await core.CallDevToolsProtocolMethodAsync("Input.insertText",
            System.Text.Json.JsonSerializer.Serialize(new
            { text = desired.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        token.ThrowIfCancellationRequested();
        await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
            "{\"type\":\"keyDown\",\"key\":\"Tab\",\"code\":\"Tab\",\"windowsVirtualKeyCode\":9}");
        await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
            "{\"type\":\"keyUp\",\"key\":\"Tab\",\"code\":\"Tab\",\"windowsVirtualKeyCode\":9}");
    }

    // The original BUY form may pause at a first-party premium/ordinary
    // registration layer after the official contact check answers S. This is
    // a separate DOM/UI route, NOT a replay of the registration button and
    // NEVER an independent POST. Only an unambiguous no-fee site action can
    // receive one native click; paid/security/unclear choices stay untouched.
    private async Task TryAdvanceOfficialFreeRegistrationAsync(
        SitePriceSubmitObserver observer, Task<SitePriceSubmitObservation> observing,
        CancellationToken token)
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        bool searched = false;
        while (DateTime.UtcNow < deadline && !observing.IsCompleted &&
               !observer.FinalPostStarted && !observer.CategoryCheckStarted)
        {
            token.ThrowIfCancellationRequested();
            if (!IsAllowedCurrentPage() ||
                !string.Equals(_web.Source?.AbsolutePath, "/myroom/buy/buy_re_reg.html",
                    StringComparison.OrdinalIgnoreCase)) return;
            if (!observer.ContactApproved)
            {
                await Task.Delay(250, token);
                continue;
            }
            searched = true;
            SitePriceEditResult option;
            try
            {
                option = SitePriceEdit.Decode(await core.ExecuteScriptAsync(
                    SitePriceEdit.FindOfficialFreeContinuationScript));
            }
            catch (Exception) { return; } // uncertain page state: no click
            if (option.Status is "paid" or "ambiguous" or "blocked" or "not-applicable")
            {
                AppendLog("사이트 무료 등록 분기 진행 불가: " + option.Message);
                return;
            }
            if (option.Status == "free-ready")
            {
                // Browser pointer events preserve the site's authentic
                // handler; never invoke form.submit(), fetch() or bypass
                // premiumLayerOpen(). Validate again just before mouse-down.
                double x = option.X, y = option.Y;
                if (!double.IsFinite(x) || !double.IsFinite(y) || x <= 0 || y <= 0) return;
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    System.Text.Json.JsonSerializer.Serialize(new
                    { type = "mouseMoved", x, y, button = "none", buttons = 0, clickCount = 0 }));
                if (observer.FinalPostStarted || observer.CategoryCheckStarted ||
                    !observer.ContactApproved ||
                    !string.Equals(await core.ExecuteScriptAsync(
                        SitePriceEdit.VerifyOfficialFreeContinuationScript), "true",
                        StringComparison.OrdinalIgnoreCase)) return;
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    System.Text.Json.JsonSerializer.Serialize(new
                    { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 }));
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    System.Text.Json.JsonSerializer.Serialize(new
                    { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 }));
                AppendLog("사이트 공식 무료·일반 등록 후속 버튼 1회 클릭 (유료 선택·독립 POST 없음)");
                return; // No second click. Observe the site's own next step.
            }
            await Task.Delay(300, token);
        }
        if (searched && !observer.CategoryCheckStarted && !observer.FinalPostStarted &&
            !observing.IsCompleted)
            AppendLog("사이트 무료·일반 등록 후속 버튼 없음 · 추가 제출하지 않음");
    }

    private async Task<bool> WaitForOfficialPremiumPromptAsync(SitePriceSubmitObserver observer,
        CancellationToken token)
    {
        var core = _web.CoreWebView2;
        if (core is null) return false;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !observer.CategoryCheckStarted &&
               !observer.FinalPostStarted && string.IsNullOrEmpty(_sitePriceEditDialogWarning) &&
               string.Equals(_web.Source?.AbsolutePath, "/myroom/buy/buy_re_reg.html",
                   StringComparison.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (observer.ContactApproved && string.Equals(
                await core.ExecuteScriptAsync(SitePriceEdit.OfficialPremiumConfirmationScript),
                "true", StringComparison.OrdinalIgnoreCase))
            {
                _state.Text = "아이템매니아 프리미엄 설정 확인 대기";
                AppendLog("아이템매니아 공식 추가 확인 화면 표시 · 기존 설정 유지 확인 필요");
                return true;
            }
            await Task.Delay(200, token);
        }
        return false;
    }

    private async Task<bool> ConfirmZeroCostPremiumForBatchAsync(SitePriceSubmitObserver observer,
        CancellationToken token)
    {
        var core = _web.CoreWebView2!;
        async Task<SitePriceEditResult> VerifyAsync() => SitePriceEdit.Decode(
            await core.ExecuteScriptAsync(SitePriceEdit.ZeroCostPremiumConfirmationScript));

        async Task MouseAsync(string type, string button, int buttons, int clickCount, SitePriceEditResult point) =>
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                System.Text.Json.JsonSerializer.Serialize(new
                { type, button, buttons, clickCount, x = point.X, y = point.Y }));

        var first = await VerifyAsync();
        if (first.Status != "ready")
        {
            _sitePriceEditDialogWarning = first.Message;
            return false;
        }
        await MouseAsync("mouseMoved", "none", 0, 0, first);
        token.ThrowIfCancellationRequested();
        var final = await VerifyAsync();
        if (final.Status != "ready" || first.X != final.X || first.Y != final.Y ||
            !observer.ContactApproved || observer.CategoryCheckStarted || observer.FinalPostStarted)
        {
            _sitePriceEditDialogWarning = "사이트 0원 확인 상태가 바뀌어 일괄 수정을 중단했습니다";
            return false;
        }
        await MouseAsync("mousePressed", "left", 1, 1, final);
        await MouseAsync("mouseReleased", "left", 0, 1, final);
        AppendLog("일괄 승인으로 아이템매니아의 공식 확인 버튼 1회 입력 · 결제 0원 · 프리미엄 0시간");
        return true;
    }

    // Only for one explicitly approved, exact-ID BUY form. A browser mouse
    // input activates the site's own click/submit handlers; it is NOT a
    // direct form POST, synthetic fetch, or bypass of site validation.
    private async Task DispatchVerifiedSitePriceClickAsync(double x, double y, CancellationToken token,
        Action checkpointBeforeMousePress)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x <= 0 || y <= 0)
            throw new InvalidOperationException("사이트 재등록 버튼 좌표를 검증하지 못했습니다");
        var core = _web.CoreWebView2 ?? throw new InvalidOperationException("사이트 브라우저 연결 없음");
        string stage = "제출 버튼 마우스 이동";
        async Task MouseAsync(string type, string button, int buttons, int clickCount,
            double mouseX, double mouseY) =>
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                System.Text.Json.JsonSerializer.Serialize(new
                { type, x = mouseX, y = mouseY, button, buttons, clickCount }));
        try
        {
            token.ThrowIfCancellationRequested();
            await MouseAsync("mouseMoved", "none", 0, 0, x, y);
            stage = "마우스 이동 후 등록번호·제목·가격·버튼 재검증";
            var refreshed = SitePriceEdit.Decode(await core.ExecuteScriptAsync(
                SitePriceEdit.RefreshNativeClickTargetScript));
            if (refreshed.Status != "click-ready")
                throw new InvalidOperationException(refreshed.Message);
            if (Math.Abs(refreshed.X - x) > 1 || Math.Abs(refreshed.Y - y) > 1)
            {
                // The footer moved after hovering. Never click the old
                // coordinates; move once more to the same audited button.
                x = refreshed.X;
                y = refreshed.Y;
                stage = "재등록 버튼 위치 변화 후 마우스 재이동";
                await MouseAsync("mouseMoved", "none", 0, 0, x, y);
                stage = "재이동 후 최종 버튼 재검증";
                refreshed = SitePriceEdit.Decode(await core.ExecuteScriptAsync(
                    SitePriceEdit.RefreshNativeClickTargetScript));
                if (refreshed.Status != "click-ready" ||
                    Math.Abs(refreshed.X - x) > 1 || Math.Abs(refreshed.Y - y) > 1)
                    throw new InvalidOperationException("재등록 버튼 위치가 계속 변동됨: " +
                        refreshed.Message);
            }
            stage = "마우스 누름 직전 최종 안전 검증";
            if (!string.Equals(await core.ExecuteScriptAsync(SitePriceEdit.VerifyNativeClickScript),
                    "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("등록번호·제목·가격·버튼 최종 검증 실패");
            token.ThrowIfCancellationRequested();
            // The site has not received a submit attempt yet. Persist the
            // uncertain-outcome checkpoint only at the last verified moment,
            // immediately before the first irreversible mouse press.
            stage = "클릭 직전 결과 확인 대기 상태 저장";
            checkpointBeforeMousePress();
            stage = "재등록 버튼 마우스 누름";
            await MouseAsync("mousePressed", "left", 1, 1, x, y);
            stage = "재등록 버튼 마우스 놓음";
            await MouseAsync("mouseReleased", "left", 0, 1, x, y);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("브라우저 클릭 단계 [" + stage + "] 실패: " +
                ex.GetType().Name + " · " + ex.Message, ex);
        }
    }

    // A newly drafted listing uses its edited price on its first ordinary post.
    // Imported BUY posts and BUY posts successfully created by Luzen can queue a price edit.
    // A saved local draft alone never becomes an owned listing.
    private static bool IsUnsubmittedNewListing(ListingJob job) =>
        !job.ImportedFromSite && !ListingIdentity.HasNumber(job.ListingNumber) &&
        !job.InitialCompleted && !job.LastSucceededUtc.HasValue &&
        !job.PendingRepostAttemptUtc.HasValue && job.Operation is "구매등록" or "판매등록";

    // Luzen-created posts receive a confirmed number only after the site
    // success + listing identity checks. Do not require a redundant import.
    // The actual edit still re-reads the signed-in OWN list and matches ID/title.
    private static bool IsRegisteredOwnedBuyPriceTarget(ListingJob job) =>
        (job.ImportedFromSite ||
         (job.Operation == "구매등록" && job.InitialCompleted && job.LastSucceededUtc.HasValue)) &&
        job.OriginalTradeSide == "구매" &&
        job.ListingNumber is { Length: 16 } && job.ListingNumber.All(char.IsAsciiDigit) &&
        MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job));

    // Plan first, mutate only after EVERY selected job passes validation. The
    // pending value is bound to the verified listing, never an independent task.
    private bool TryPlanAutomaticPriceApply(IReadOnlyList<ListingJob> selected, string amount,
        out Dictionary<ListingJob, string> pendingByJob, out string problem)
    {
        pendingByJob = new Dictionary<ListingJob, string>();
        problem = "";
        if (!TrySitePrice(amount, out long amountValue) || amountValue % 10 != 0)
        {
            problem = "가격은 1,000원 이상, 10원 단위로 입력하세요.";
            return false;
        }
        if (selected.Count == 0 || selected.Distinct().Count() != selected.Count ||
            selected.Any(job => !_jobs.Contains(job)))
        {
            problem = "선택한 물품이 현재 목록에 없습니다.";
            return false;
        }
        var changed = selected.Where(job =>
            !TrySitePrice(job.Price, out long oldPrice) || oldPrice != amountValue).ToList();
        var existing = changed.Where(job => !IsUnsubmittedNewListing(job)).ToList();
        // Updating the saved target is local-only. It never authorizes a
        // site POST: the scheduler is locked, and only a later explicit
        // one-item or checked-list batch confirmation may use this price.
        if (existing.Any(job =>
            !IsRegisteredOwnedBuyPriceTarget(job) || job.RequiresReview ||
            (job.LastStatus.Contains("가격", StringComparison.Ordinal) &&
             (job.LastStatus.Contains("제출 중", StringComparison.Ordinal) ||
              job.LastStatus.Contains("결과 확인 필요", StringComparison.Ordinal))) ||
            !ListingIdentity.HasNumber(job.ListingNumber) ||
            !MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job))))
        {
            problem = "기존글 가격 반영은 등록번호·관리목록이 확인된 본인 구매글만 가능합니다. " +
                "판매글·확인 대기 중인 글은 변경하지 않았습니다.";
            return false;
        }
        string accountId = ItemManiaAccountStore.Load(_system).UserId;
        if (existing.Select(job => job.SiteId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 ||
            (!string.IsNullOrWhiteSpace(accountId) && existing.Any(job =>
                !string.Equals(job.SiteId, accountId, StringComparison.OrdinalIgnoreCase))) ||
            existing.Any(job => _jobs.Any(other => !ReferenceEquals(other, job) &&
                string.Equals(other.SiteId, job.SiteId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(other.ListingNumber, job.ListingNumber, StringComparison.Ordinal))))
        {
            problem = "기존글의 사이트 계정이 다르거나 등록번호가 중복되어 가격 변경을 저장하지 않았습니다.";
            return false;
        }
        foreach (var job in selected)
        {
            if (!changed.Contains(job))
                pendingByJob[job] = job.PendingSitePriceTarget;
            else
                pendingByJob[job] = IsUnsubmittedNewListing(job) ? "" : amountValue.ToString();
        }
        return true;
    }

    // Immediate, explicit one-listing operation. It never starts the scheduler
    // and never submits another selected/checked row. A queued local target may
    // be applied now, but the existing reservation is retained on cancellation.
    public Task ApplySelectedSitePriceFromHeaderAsync() => ApplySelectedSitePriceAsync();

    private async Task ApplySelectedSitePriceAsync(ListingJob? rowTarget = null)
    {
        var job = rowTarget ?? (_editorTabs.SelectedTab == _autoTab
            ? _autoGrid.CurrentRow?.DataBoundItem as ListingJob : _selected);
        if (job is null)
        {
            MessageBox.Show(this, "가격을 수정할 물품 한 줄을 먼저 선택해 주세요.", "1건 즉시 가격 수정");
            return;
        }
        if (_busy || _sitePriceBatchRunning || _siteRequestTraceActive)
        {
            MessageBox.Show(this, "진행 중인 사이트 작업이 완료된 뒤 선택한 1건을 수정할 수 있습니다.", "1건 즉시 가격 수정");
            return;
        }
        if (_myListingsImportRunning && !await CancelImportForManualRunAsync("1건 즉시 가격 수정")) return;
        if (!_jobs.Contains(job) || !IsRegisteredOwnedBuyPriceTarget(job) ||
            _jobs.Any(other => !ReferenceEquals(other, job) &&
                string.Equals(other.SiteId, job.SiteId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(other.ListingNumber, job.ListingNumber, StringComparison.Ordinal)))
        {
            MessageBox.Show(this,
                "등록번호와 계정이 확인된 본인 구매글 한 건만 즉시 가격 수정할 수 있습니다.",
                "1건 즉시 가격 수정 제한", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (job.RequiresReview)
        {
            if (!await ReconcileHeldPriceBeforeImmediateEditAsync(job)) return;
        }
        using var dialog = new Form
        {
            Text = "선택한 1건 사이트 가격 즉시 수정", Width = 480, Height = 238,
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false
        };
        var description = new Label
        {
            Text = $"{job.Server} · 등록번호 {job.ListingNumber}\n사이트 수정 화면에서 이 등록번호의 가격을 적용합니다.",
            AutoSize = true, Left = 18, Top = 17
        };
        var amount = new NumericUpDown
        {
            Left = 18, Top = 77, Width = 390, Minimum = 1000, Maximum = 1000000000,
            Increment = 10, ThousandsSeparator = true, DecimalPlaces = 0
        };
        string preset = string.IsNullOrWhiteSpace(job.PendingSitePriceTarget)
            ? job.Price : job.PendingSitePriceTarget;
        if (TrySitePrice(preset, out var currentAmount))
            amount.Value = Math.Clamp((decimal)currentAmount, amount.Minimum, amount.Maximum);
        var apply = new Button { Text = "이 1건 가격 수정", DialogResult = DialogResult.OK,
            Left = 192, Top = 132, Width = 135, Height = 32 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel,
            Left = 338, Top = 132, Width = 94, Height = 32 };
        dialog.Controls.AddRange(new Control[] { description, amount, apply, cancel });
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (amount.Value % 10 != 0)
        {
            MessageBox.Show(this, "가격은 10원 단위로 입력해 주세요.", "가격 확인");
            return;
        }
        if (_schedulerRunning) PauseScheduler();
        await ApplySitePriceAsync(job, bulkApproval: false, nativeSingleApproval: true,
            manuallyApprovedPrice: (long)amount.Value, backgroundApproved: true);
    }

    // The same per-row action first reconciles an old uncertain submission.
    // If the exact listing already has the intended price, commit the verified
    // result WITHOUT posting. A fresh attempt is possible only when the
    // original ID/title/old price are all proven unchanged, the full owned
    // list scan found no replacement, and the user independently confirms
    // no other listing was created. No automatic hold release is inferred
    // from a missing network event alone.
    private async Task<bool> ReconcileHeldPriceBeforeImmediateEditAsync(ListingJob job)
    {
        if (!job.LastStatus.Contains("가격", StringComparison.Ordinal) ||
            !TrySitePrice(job.PendingSitePriceTarget, out var attemptedTarget))
        {
            MessageBox.Show(this, "확인 대기 원인이나 이전 요청 금액을 확인할 수 없습니다.\n" +
                "사이트의 중복 등록 여부를 직접 확인한 뒤 보류를 해제해 주세요.",
                "중복 제출 방지", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if ((_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system))
        {
            MessageBox.Show(this, "독립 VPN 연결이 확인되지 않아 사이트를 조회하지 않습니다.", "연결 확인");
            return false;
        }
        var account = ItemManiaAccountStore.Load(_system).UserId;
        if (string.IsNullOrWhiteSpace(account) ||
            !string.Equals(account, job.SiteId, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "현재 사이트 계정과 이 등록글의 계정이 일치하지 않습니다.", "계정 확인");
            return false;
        }
        if (_schedulerRunning) PauseScheduler();
        if (_busy) return false;
        _busy = true;
        _state.Text = "이전 가격 수정 결과 재조회 중 (재제출 없음)";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_web.CoreWebView2 is null) throw new InvalidOperationException("사이트 브라우저 연결 없음");
            if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(cancellation.Token);
            var readback = await ReadSitePriceAfterRepostAsync(job, attemptedTarget, cancellation.Token);
            AppendLog("1건 즉시 수정 전 확인 대기 재조회 (제출 없음): " + job.ListingNumber + " · " + readback.Message);
            if (readback.ExactIdVerified)
            {
                job.Price = attemptedTarget.ToString("N0");
                job.VerifiedSitePrice = attemptedTarget.ToString();
                job.VerifiedSitePriceUtc = DateTime.UtcNow;
                job.PendingSitePriceTarget = "";
                job.PriceEditSiteTitleSnapshot = "";
                job.RequiresReview = false;
                job.Enabled = false; // A single manual check never restarts the scheduler.
                job.NextDueUtc = DateTime.MaxValue;
                job.LastStatus = "가격 수정 결과 확인: " + attemptedTarget.ToString("N0") + "원 (자동 OFF)";
                _config.Jobs = _jobs.ToList();
                _config.Save();
                MessageBox.Show(this, "이전 가격 수정이 실제 사이트에 반영되어 있습니다.\n\n" +
                    readback.Message + "\n\n중복 제출 없이 확인 대기만 해제했습니다. 자동 실행은 꺼져 있습니다.",
                    "기존 수정 반영 확인 · 추가 제출 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (!readback.OriginalUnchangedNoReplacement)
            {
                MessageBox.Show(this, "사이트에서 기존 수정 결과를 안전하게 확정하지 못했습니다.\n\n" +
                    readback.Message + "\n\n추가 제출하지 않았고 확인 대기를 유지합니다. " +
                    "아이템매니아에서 중복 글과 등록번호를 확인해 주세요.",
                    "기존 수정 결과 불확실 · 중복 제출 방지", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (MessageBox.Show(this,
                "이전 가격 수정 이후에도 원래 등록번호·제목·기존 금액이 그대로이고, " +
                "검색한 본인 관리목록에 새 등록글 후보가 없음을 확인했습니다.\n\n" +
                readback.Message + "\n\n" +
                "아이템매니아 사이트에서도 새 글이나 중복 글이 생기지 않았음을 직접 확인했습니까?\n" +
                "[예]를 누르면 이 1건의 확인 대기를 해제하고 새 가격 수정 화면으로 진행합니다. " +
                "아직 사이트에 재제출하지 않았습니다.",
                "기존 가격 수정 결과 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
            job.RequiresReview = false;
            job.Enabled = false;
            job.NextDueUtc = DateTime.MaxValue;
            job.LastStatus = "사이트 기존 가격 유지 확인 · 사용자 확인 후 1건 수정 준비 (자동 OFF)";
            _config.Jobs = _jobs.ToList();
            _config.Save();
            AppendLog("사이트 원본 글 유지 검증·사용자 중복 부재 확인 · 단건 수정 재진입: " + job.ListingNumber);
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("기존 수정 결과 재조회 실패: " + ex.GetType().Name + " · " + ex.Message);
            MessageBox.Show(this, "사이트 확인 중 오류가 발생했습니다. 가격을 제출하지 않았습니다.\n" + ex.Message,
                "가격 결과 재조회 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally
        {
            _busy = false;
            _state.Text = _schedulerRunning ? "자동 실행 중" : "중지/대기";
            _jobsGrid.Refresh();
            _autoGrid.Refresh();
            RefreshAutoSummary();
            DispatchQueuedMyListingsImport();
        }
    }

    // Never edits a field or submits a listing. This uses the same exact-ID
    // management-list/detail/edit-page navigation as the approved price pilot,
    // so that the diagnostics come from the REAL account form, not a mock.
    private async Task InspectSelectedPriceSubmitBindingsAsync()
    {
        if (_selected is null) return;
        await ApplySitePriceAsync(_selected, bulkApproval: false, diagnosticOnly: true);
    }

    // Recovery path for a previously attempted edit. It NEVER revisits the
    // edit form or clicks its submit button; only the signed-in OWN list is
    // consulted. A new registration number is never inferred or rebound.
    private async Task RecheckSelectedPriceWithoutSubmitAsync()
    {
        if (_selected is null) return;
        if (_busy || _myListingsImportRunning || _sitePriceBatchRunning || _siteRequestTraceActive)
        {
            MessageBox.Show(this, "다른 사이트 작업이 끝난 뒤 결과를 조회해 주세요.", "가격 결과 재조회");
            return;
        }
        var job = _selected;
        if (!IsRegisteredOwnedBuyPriceTarget(job) || !job.RequiresReview ||
            !job.LastStatus.Contains("가격", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "가격 변경 후 확인 대기 중인 본인 구매글만 제출 없이 재조회할 수 있습니다.",
                "가격 결과 재조회", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var account = ItemManiaAccountStore.Load(_system).UserId;
        if (string.IsNullOrWhiteSpace(account) ||
            !string.Equals(account, job.SiteId, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "현재 로그인 계정과 저장된 물품 계정이 일치하지 않아 조회하지 않습니다.",
                "계정 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_schedulerRunning) PauseScheduler();
        using var dialog = new Form
        {
            Text = "가격 변경 결과 재조회 · 제출 없음", Width = 460, Height = 225,
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false
        };
        var description = new Label
        {
            Text = $"{job.Server} · 등록번호 {job.ListingNumber}\n확인할 변경 목표 금액을 입력하세요. 사이트에 다시 제출하지 않습니다.",
            AutoSize = true, Left = 18, Top = 17
        };
        var amount = new NumericUpDown
        {
            Left = 18, Top = 83, Width = 400, Minimum = 1000, Maximum = 1000000000,
            Increment = 10, ThousandsSeparator = true, DecimalPlaces = 0
        };
        string proposed = string.IsNullOrWhiteSpace(job.PendingSitePriceTarget)
            ? job.Price : job.PendingSitePriceTarget;
        if (TrySitePrice(proposed, out var proposedAmount))
            amount.Value = Math.Clamp((decimal)proposedAmount, amount.Minimum, amount.Maximum);
        var verify = new Button { Text = "읽기 전용 조회", DialogResult = DialogResult.OK,
            Left = 195, Top = 132, Width = 117, Height = 30 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel,
            Left = 322, Top = 132, Width = 96, Height = 30 };
        dialog.Controls.AddRange(new Control[] { description, amount, verify, cancel });
        dialog.AcceptButton = verify;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (amount.Value % 10 != 0)
        {
            MessageBox.Show(this, "금액은 10원 단위로 입력해 주세요.", "가격 확인");
            return;
        }
        await InspectPendingSitePriceOnlyAsync(job, (long)amount.Value, confirmLocalOnExact: true);
    }

    // Reservation only: no WebView navigation, no POST, no forced scheduler
    // pause and no additional hourly quota usage at the time of this click.
    private async Task ApplySelectedSitePricesAsync(IReadOnlyList<ListingJob>? explicitTargets = null,
        bool sitePriceConsentConfirmed = false)
    {
        if (_myListingsImportRunning && !await CancelImportForManualRunAsync("가격 변경 예약")) return;
        if (_myListingsImportRunning || (_busy && _currentlyExecutingJob is null) || _sitePriceBatchRunning)
        {
            MessageBox.Show(this, "사이트 제출·조회가 끝난 뒤 가격 변경을 예약하세요.", "가격 변경 예약");
            return;
        }
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        if (explicitTargets is null && !_busy) SaveSelectedJob(false);
        var targets = (explicitTargets ?? _filteredJobs.Where(j => j.SelectedForBulk).ToList()).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "가격을 변경할 물품의 왼쪽 체크박스를 선택해 주세요.", "선택한 물품 없음");
            return;
        }
        if (!SitePriceEdit.AutomaticPriceSubmissionEnabled)
        {
            MessageBox.Show(this, "제목 보호 모드에서는 기존 글의 가격 자동 변경 예약을 받지 않습니다.\n" +
                "사이트에서 직접 재등록하면 기존 제목의 추가 변형을 방지할 수 있습니다.", "제목 보호 모드",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var invalid = targets.Where(j => !_jobs.Contains(j) ||
            !IsRegisteredOwnedBuyPriceTarget(j) || j.RequiresReview ||
            (j.LastStatus.Contains("가격", StringComparison.Ordinal) &&
             (j.LastStatus.Contains("제출 중", StringComparison.Ordinal) ||
              j.LastStatus.Contains("결과 확인 필요", StringComparison.Ordinal))) ||
            !ListingIdentity.HasNumber(j.ListingNumber) ||
            !MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(j)) ||
            !TrySitePrice(j.Price, out var p) || p % 10 != 0).ToList();
        string currentAccount = ItemManiaAccountStore.Load(_system).UserId;
        bool accountMismatch = !string.IsNullOrWhiteSpace(currentAccount) &&
            targets.Any(j => !string.Equals(j.SiteId, currentAccount, StringComparison.OrdinalIgnoreCase));
        bool mixedAccounts = targets.Select(j => j.SiteId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1;
        bool duplicateIds = targets.GroupBy(j => ((j.SiteId ?? "").Trim().ToLowerInvariant(), j.ListingNumber))
            .Any(group => group.Count() != 1);
        if (invalid.Count > 0 || duplicateIds || accountMismatch || mixedAccounts)
        {
            MessageBox.Show(this, $"선택 물품 중 {invalid.Count}건은 본인 구매글·등록번호·가격 또는 확인 대기 상태가 유효하지 않습니다.\n" +
                (duplicateIds ? "같은 사이트 ID의 등록번호가 중복된 항목도 있습니다.\n" : "") +
                (accountMismatch || mixedAccounts ? "서로 다른 사이트 아이디가 섞였거나 현재 계정과 일치하지 않습니다.\n" : "") +
                "안전한 구매글만 다시 선택해 주세요.", "가격 변경 예약 중단");
            return;
        }
        var preview = string.Join("\n", targets.Take(12).Select(j =>
            $"{j.Server} · {j.ListingNumber} → {j.Price}원"));
        if (targets.Count > 12) preview += $"\n외 {targets.Count - 12}건";
        if ((!sitePriceConsentConfirmed || explicitTargets is null) && MessageBox.Show(this,
            $"{targets.Count}개 글의 가격 변경을 예약할까요?\n\n{preview}\n\n" +
            "지금은 사이트에 제출하지 않습니다. 각 글의 기존 자동등록 차례가 오면 가격을 수정합니다.\n" +
            "자동등록은 멈추지 않으며 시간당 횟수·서버 간격 제한을 그대로 공유합니다.\n" +
            "자동 OFF인 물품은 다시 ON할 때까지 대기합니다.",
            "다음 자동등록 순번에 가격 변경", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        // Every reservation is tied to the exact saved listing object. There
        // is intentionally no independent background batch or timer.
        var before = targets.Select(j => (Job: j, j.PendingSitePriceTarget)).ToArray();
        foreach (var job in targets)
            job.PendingSitePriceTarget = job.Price.Replace(",", "", StringComparison.Ordinal).Trim();
        try
        {
            _config.Jobs = _jobs.ToList();
            _config.Save();
        }
        catch (Exception ex)
        {
            foreach (var (job, pending) in before) job.PendingSitePriceTarget = pending;
            MessageBox.Show(this, "예약 저장에 실패했습니다. 변경을 취소했습니다.\n" + ex.Message,
                "가격 변경 예약 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _jobsGrid.Refresh(); _autoGrid.Refresh(); RefreshAutoSummary();
        AppendLog($"가격 변경 예약: {targets.Count}개 · 기존 자동등록 순번에 처리 (추가 제출 없음)");
    }

    public Task ApplyCheckedSitePricesFromHeaderAsync() => ApplyCheckedSitePricesNowAsync();

    // Deliberate, one-time user approval for ALL checked BUY listings. Unlike
    // the old reservation path, this executes now, without opening the site
    // window or asking the user to approve each native site submit. Stop on
    // the first uncertain result; never issue a second POST for that ID.
    private async Task ApplyCheckedSitePricesNowAsync()
    {
        if (_myListingsImportRunning && !await CancelImportForManualRunAsync("선택 구매글 가격 일괄 수정")) return;
        if (_busy || _myListingsImportRunning || _sitePriceBatchRunning || _siteRequestTraceActive)
        {
            MessageBox.Show(this, "다른 사이트 작업이 끝나면 일괄 가격 수정을 시작할 수 있습니다.",
                "가격 일괄 수정 대기");
            return;
        }
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        var targets = _filteredJobs.Where(j => j.SelectedForBulk).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "가격을 수정할 구매글의 왼쪽 체크박스를 선택해 주세요.", "선택한 물품 없음");
            return;
        }
        string currentAccount = ItemManiaAccountStore.Load(_system).UserId;
        bool invalid = string.IsNullOrWhiteSpace(currentAccount) ||
            targets.Any(j => !_jobs.Contains(j) || !IsRegisteredOwnedBuyPriceTarget(j) ||
                j.RequiresReview || !string.Equals(j.SiteId, currentAccount, StringComparison.OrdinalIgnoreCase) ||
                !TrySitePrice(j.Price, out var price) || price % 10 != 0 ||
                j.LastStatus.Contains("결과 확인 필요", StringComparison.Ordinal));
        bool duplicateIds = targets.GroupBy(j => ((j.SiteId ?? "").Trim().ToLowerInvariant(), j.ListingNumber))
            .Any(g => g.Count() > 1) ||
            targets.Any(j => _jobs.Any(other => !ReferenceEquals(other, j) &&
                string.Equals(other.SiteId, j.SiteId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(other.ListingNumber, j.ListingNumber, StringComparison.Ordinal)));
        if (invalid || duplicateIds || ((_system is "B" or "C") &&
            !DualSystemNetwork.IsRouteVerified(_system)))
        {
            MessageBox.Show(this,
                "본인 구매글·등록번호·현재 계정·가격(1,000원 이상/10원 단위) 또는 독립 연결을 확인할 수 없습니다.\n" +
                "확인 대기나 중복 등록번호가 있는 경우에도 일괄 수정하지 않습니다.",
                "가격 일괄 수정 대상 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string preview = string.Join("\n", targets.Take(12).Select(j =>
            $"{j.Server} · {j.ListingNumber} → {j.Price}원"));
        if (targets.Count > 12) preview += $"\n외 {targets.Count - 12}건";
        if (MessageBox.Show(this,
            $"{targets.Count}개 본인 구매글의 사이트 가격을 지금 수정할까요?\n\n{preview}\n\n" +
            "각 행에 저장된 가격을 사용합니다. 대상은 등록번호로 확인합니다.\n" +
            "사이트 재등록 버튼은 한 건씩 자동으로 누르고, 관리목록에서 실제 가격을 확인한 후 다음 글로 넘어갑니다.\n" +
            "총 결제금액 0원·프리미엄 0시간인 사이트 확인도 이번 일괄 승인에 포함됩니다.\n" +
            "사이트 수정 폼의 제목이 다르면 같은 등록번호의 관리목록 제목을 복원합니다.\n" +
            "오류·결과 불확실·추가 승인 요청 시 남은 건은 제출하지 않습니다.\n" +
            "서버별 등록 간격과 시간당 제한을 지킵니다.",
            "선택 구매글 사이트 가격 일괄 수정", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        if (_schedulerRunning) PauseScheduler();
        _sitePriceBatchCancellation = new CancellationTokenSource();
        _sitePriceBatchRunning = true;
        int verified = 0;
        string stopReason = "";
        SitePriceBatchProgress?.Invoke($"0/{targets.Count}건 확인 · 일괄 수정 시작", true, null);
        AppendLog($"선택 구매글 가격 일괄 수정 시작: {targets.Count}건");
        try
        {
            foreach (var job in targets)
            {
                var cancel = _sitePriceBatchCancellation.Token;
                cancel.ThrowIfCancellationRequested();
                _reportSitePriceBatchStage = stage => SitePriceBatchProgress?.Invoke(
                    $"{verified}/{targets.Count}건 확인 · {job.Server} · {job.ListingNumber} · " +
                    (cancel.IsCancellationRequested ? "중지 요청 · 현재 건의 결과 확인 후 종료" : stage), true, null);
                // Only normal server-spacing delays are waited through.
                // Exhausted hourly quotas require a later explicit batch.
                while (!CanRunNow(job, out var pacingReason))
                {
                    if (pacingReason.Contains("이번 시간 등록 가능 횟수", StringComparison.Ordinal))
                    {
                        stopReason = pacingReason;
                        break;
                    }
                    _state.Text = $"가격 일괄 수정 간격 대기 {verified}/{targets.Count} · {pacingReason}";
                    _reportSitePriceBatchStage?.Invoke("서버별 등록 간격 대기");
                    await Task.Delay(1000, cancel);
                }
                if (!string.IsNullOrWhiteSpace(stopReason)) break;
                if (!TrySitePrice(job.Price, out long desired))
                {
                    stopReason = "선택 글의 저장 가격을 다시 확인하지 못했습니다";
                    break;
                }
                _state.Text = $"가격 일괄 수정 {verified + 1}/{targets.Count} · 등록번호 {job.ListingNumber}";
                _reportSitePriceBatchStage?.Invoke("기존 등록글 조회 중");
                if (!await ApplySitePriceAsync(job, bulkApproval: true, nativeSingleApproval: true,
                        manuallyApprovedPrice: desired, backgroundApproved: true))
                {
                    stopReason = job.ListingNumber + " · " + _lastSitePriceEditFailure;
                    break;
                }
                verified++;
            }
        }
        catch (OperationCanceledException)
        {
            stopReason = "사용자가 중지를 요청했습니다. 이미 제출한 한 건의 결과는 별도 확인이 필요할 수 있습니다.";
        }
        catch (Exception ex)
        {
            stopReason = ex.Message;
        }
        finally
        {
            _sitePriceBatchRunning = false;
            _reportSitePriceBatchStage = null;
            _sitePriceBatchCancellation.Dispose();
            _sitePriceBatchCancellation = null;
            _state.Text = _schedulerRunning ? "자동 실행 중" : "중지/대기";
            _jobsGrid.Refresh();
            _autoGrid.Refresh();
            RefreshAutoSummary();
        }
        AppendLog($"선택 구매글 가격 일괄 수정 종료: {verified}/{targets.Count}건 확인" +
            (string.IsNullOrWhiteSpace(stopReason) ? "" : " · 중단: " + stopReason));
        if (SitePriceBatchProgress is { } reportProgress)
        {
            if (string.IsNullOrWhiteSpace(stopReason))
                reportProgress($"완료 · {verified}/{targets.Count}건의 사이트 가격 확인", false, null);
            else
                reportProgress($"중단 · {verified}/{targets.Count}건의 사이트 가격 확인 · {stopReason}",
                    false, stopReason);
            return;
        }
        MessageBox.Show(this, $"{targets.Count}건 중 {verified}건의 사이트 가격 확인을 완료했습니다." +
            (string.IsNullOrWhiteSpace(stopReason) ? "" : "\n\n나머지 글은 제출하지 않았습니다.\n" + stopReason),
            "가격 일괄 수정 결과", MessageBoxButtons.OK,
            string.IsNullOrWhiteSpace(stopReason) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task<bool> ApplySitePriceAsync(ListingJob? explicitJob, bool bulkApproval,
        bool fromScheduler = false, bool nativeSingleApproval = false, long? manuallyApprovedPrice = null,
        bool diagnosticOnly = false, bool backgroundApproved = false)
    {
        _lastSitePriceEditFailure = "";
        if (diagnosticOnly && (bulkApproval || fromScheduler || nativeSingleApproval || manuallyApprovedPrice.HasValue))
            throw new InvalidOperationException("읽기 전용 분석은 가격 제출 작업과 동시에 사용할 수 없습니다");
        if (!SitePriceEdit.AutomaticPriceSubmissionEnabled && !nativeSingleApproval && !diagnosticOnly)
        {
            _lastSitePriceEditFailure = "제목 보호 모드: 사이트 가격 자동 제출 중지 (기존 예약도 제출하지 않음)";
            if (!bulkApproval) MessageBox.Show(this, _lastSitePriceEditFailure + "\n사이트에서 직접 수정해 주세요.",
                "제목 보호 모드", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (nativeSingleApproval && (fromScheduler || manuallyApprovedPrice is null ||
            (bulkApproval && !backgroundApproved)))
        {
            _lastSitePriceEditFailure = "단건 사이트 가격 변경은 사용자가 직접 실행해야 합니다";
            return false;
        }
        if (nativeSingleApproval && (_system is "B" or "C") &&
            !DualSystemNetwork.IsRouteVerified(_system))
        {
            _lastSitePriceEditFailure = "B/C 독립 네트워크 연결을 확인할 수 없어 제출하지 않았습니다";
            if (!bulkApproval) MessageBox.Show(this, _lastSitePriceEditFailure, "사이트 가격 변경 중단");
            return false;
        }
        if (fromScheduler && !_schedulerRunning) return false;
        if (_sitePriceBatchRunning && !bulkApproval)
        {
            MessageBox.Show(this, "선택 물품의 순차 가격 반영이 끝난 뒤 단건 수정할 수 있습니다.", "가격 변경 진행 중");
            return false;
        }
        if (_myListingsImportRunning || _busy)
        {
            _lastSitePriceEditFailure = "다른 등록·조회 작업이 진행 중입니다";
            if (!bulkApproval) MessageBox.Show(this, "등록글 조회·재등록 작업이 끝난 뒤 사이트 가격을 수정할 수 있습니다.", "사이트 가격 변경 대기");
            return false;
        }
        if (_siteRequestTraceActive)
        {
            _lastSitePriceEditFailure = "사이트 수정 요청을 기록하는 중입니다";
            if (!bulkApproval) MessageBox.Show(this,
                "수정 요청 기록을 종료한 뒤 가격을 적용하세요. 수동 사이트 작업과 자동 제출을 동시에 실행하지 않습니다.",
                "사이트 작업 중복 방지", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        // A direct row action must NOT commit unrelated editor fields or
        // overwrite the selected listing with stale local form values.
        if (!bulkApproval && !diagnosticOnly && !nativeSingleApproval) SaveSelectedJob(false);
        var job = explicitJob ?? _selected;
        if (job is null || !IsRegisteredOwnedBuyPriceTarget(job))
        {
            _lastSitePriceEditFailure = "본인 등록글의 등록번호·거래 방향을 확인할 수 없습니다";
            if (!bulkApproval) MessageBox.Show(this, "본인 구매글의 등록번호와 거래 방향이 확인되어야 사이트 가격을 변경할 수 있습니다.",
                "수정 대상 확인");
            return false;
        }
        string activeAccount = ItemManiaAccountStore.Load(_system).UserId;
        if ((nativeSingleApproval && string.IsNullOrWhiteSpace(activeAccount)) ||
            (!string.IsNullOrWhiteSpace(activeAccount) &&
            !string.Equals(job.SiteId, activeAccount, StringComparison.OrdinalIgnoreCase)))
        {
            _lastSitePriceEditFailure = "현재 로그인 계정과 저장된 물품 계정이 일치하지 않습니다";
            if (!bulkApproval) MessageBox.Show(this, _lastSitePriceEditFailure, "가격 변경 계정 확인");
            return false;
        }
        long desired = manuallyApprovedPrice ?? 0;
        if ((!manuallyApprovedPrice.HasValue && !TrySitePrice(job.Price, out desired)) ||
            desired < 1000 || desired % 10 != 0)
        {
            _lastSitePriceEditFailure = "변경 가격은 1,000원 이상, 10원 단위여야 합니다";
            if (!bulkApproval) MessageBox.Show(this, "변경할 단가는 1,000원 이상, 10원 단위로 입력해 주세요.", "가격 확인");
            return false;
        }
        // A previous price submission may already have re-registered this
        // listing under a new ID. NEVER submit it again while on hold.
        // This dedicated path only reads the signed-in OWN list.
        bool unresolvedPriceAttempt = job.LastStatus.Contains("가격", StringComparison.Ordinal) &&
            (job.LastStatus.Contains("제출 중", StringComparison.Ordinal) ||
             job.LastStatus.Contains("결과 확인 필요", StringComparison.Ordinal));
        if (!diagnosticOnly && (job.RequiresReview || unresolvedPriceAttempt))
        {
            if (job.LastStatus.Contains("가격", StringComparison.Ordinal))
                await InspectPendingSitePriceOnlyAsync(job, desired);
            else if (!bulkApproval)
                MessageBox.Show(this, "이 물품은 확인 대기 상태입니다. 확인이 끝나기 전에는 사이트에 다시 제출하지 않습니다.",
                    "중복 가격 제출 차단", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _lastSitePriceEditFailure = "확인 대기 물품: 사이트 가격 재제출 차단";
            return false;
        }
        // The user has now captured an actual manual edit: the site's own
        // POST goes to /myroom/buy/buy_re_reg_ok.php. We can observe that
        // *exact* first-party submission before navigating to the readback
        // list; the previous trial could cancel the POST by navigating early.
        if (_schedulerRunning && diagnosticOnly && MessageBox.Show(this,
                "수정폼을 안전하게 읽는 동안 자동등록을 일시정지합니다. 검사 후 자동 시작을 다시 눌러 주세요.\n\n" +
                "가격·제목은 입력하지 않으며 사이트에 제출하지 않습니다. 계속할까요?",
                "읽기 전용 수정폼 검사", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
        if (_schedulerRunning && !fromScheduler) PauseScheduler();
        _busy = true;
        _activeSitePriceJob = job;
        _state.Text = "사이트 가격 수정 대상 확인 중";
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = cancellation.Token;
        _sitePriceLastReadback = null;
        bool submitted = false;
        bool previousReview = job.RequiresReview;
        bool previousEnabled = job.Enabled;
        DateTime previousDue = job.NextDueUtc;
        string previousStatus = job.LastStatus;
        string previousPendingPrice = job.PendingSitePriceTarget;
        bool showBrowser = backgroundApproved && !bulkApproval;
        var originalBrowserBounds = _web.Bounds;
        var originalBrowserDock = _web.Dock;
        try
        {
            if (showBrowser)
                ShowSiteBrowserInWorkspace();
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_web.CoreWebView2 is null) throw new InvalidOperationException("아이템매니아 브라우저를 시작하지 못했습니다.");
            if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(token);
            var url = OwnedListUrlFor(job);
            await NavigateOwnedListAsync(url, token);
            var matched = await LocateOwnedListRepostAsync(job, token, requireRepostButton: false);
            if (matched.Status != "found")
                throw new InvalidOperationException("등록번호에 직접 연결된 본인 물품 행을 확인할 수 없습니다: " + matched.Message);
            var page = MyListingsImport.DecodePage(await _web.CoreWebView2.ExecuteScriptAsync(MyListingsImport.ExtractPageScript));
            var records = page.Listings.Where(x => x.ListingNumber == job.ListingNumber &&
                x.TradeSide == job.OriginalTradeSide &&
                MyListingsImport.IsOwnedListUrl(x.SourceListUrl)).ToList();
            if (records.Count != 1 || !TrySitePrice(records[0].Price, out var current))
                throw new InvalidOperationException("해당 등록번호의 현재 사이트 단가를 정확히 읽지 못했습니다. 수정하지 않았습니다.");
            // Only the exact signed-in OWN-list number, side and server identify
            // this target. A historical VANTA title may differ from the site.
            // Capture the CURRENT site title solely to prevent the whole-form
            // price edit from silently rewriting it; never copy job.Title into
            // the edit form or use it to veto an otherwise verified listing.
            string siteTitleSnapshot = records[0].Title;
            if (string.IsNullOrWhiteSpace(siteTitleSnapshot))
                throw new InvalidOperationException("해당 등록번호의 현재 사이트 제목을 읽지 못해 전체 수정폼 제출을 중단했습니다.");
            if (!string.IsNullOrWhiteSpace(records[0].Server) &&
                !string.Equals(records[0].Server.Trim(), job.Server.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("등록번호는 일치하지만 사이트 서버와 저장 서버가 달라 수정하지 않았습니다.");
            if (!diagnosticOnly)
            {
                job.VerifiedSitePrice = current.ToString();
                job.VerifiedSitePriceUtc = DateTime.UtcNow;
                _config.Save();
            }
            if (!diagnosticOnly && current == desired)
            {
                if (nativeSingleApproval)
                {
                    job.Price = desired.ToString("N0");
                    _config.Save();
                }
                AppendLog($"사이트 가격이 이미 동일합니다: {job.Server} · {job.ListingNumber} · {desired:N0}원 (제출 없음)");
                if (!bulkApproval) MessageBox.Show(this, $"{job.Server} · {job.ListingNumber}\n현재 아이템매니아 가격도 {desired:N0}원입니다.\n재등록하지 않았습니다.",
                    "이미 반영된 가격");
                return true;
            }
            if (job.OriginalTradeSide != "구매")
                throw new InvalidOperationException("현재는 실제 수정 버튼이 확인된 구매글만 1건씩 가격 반영할 수 있습니다. 판매글은 제출하지 않았습니다.");
            if (!diagnosticOnly && !CanRunNow(job, out var quotaReason))
                throw new InvalidOperationException("가격 변경 시 등록 작업 간격을 확인해야 합니다. " + quotaReason);
            var ask = $"아이템매니아 구매 등록글 1건의 가격을 변경할까요?\n\n" +
                $"서버: {job.Server}\n등록번호: {job.ListingNumber}\n사이트 현재 제목: {siteTitleSnapshot}\n\n" +
                $"사이트 현재 가격: {current:N0}원\n변경할 가격: {desired:N0}원\n\n" +
                (nativeSingleApproval ?
                    (backgroundApproved
                        ? "사이트 수정 화면에서 공식 재등록 버튼을 확인한 뒤 한 번 누릅니다.\n"
                        : "사이트 수정 창에서 '재등록' 버튼을 직접 눌러야 합니다.\n") +
                    "사이트가 최종 제출을 거부하거나 결과가 불확실하면 자동 재시도하지 않습니다.\n\n" : "") +
                "사이트 구매 수정 화면(frmBuy)의 단가만 입력하고 다른 내용은 유지합니다.\n" +
                "등록번호가 바뀔 수 있어 결과가 불확실하면 자동 실행을 보류합니다.\n" +
                "재등록 횟수·서버 간격 제한에 포함되며 중복 제출하지 않습니다.";
            if (!bulkApproval && !diagnosticOnly && !backgroundApproved && MessageBox.Show(this, ask, "실제 사이트 구매글 가격 변경", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;

            using var submissionObserver = new SitePriceSubmitObserver(_web.CoreWebView2);
            if (!diagnosticOnly)
                await submissionObserver.StartAsync();
            var record = records[0];
            if (bulkApproval) _reportSitePriceBatchStage?.Invoke("사이트 수정폼 확인 중");
            bool hasDetailUrl = MyListingsImport.IsSafeDetailUrl(record.ListingUrl, job.ListingNumber);
            bool hasEditUrl = MyListingsImport.IsSafeDetailUrl(record.EditUrl, job.ListingNumber);
            if (hasDetailUrl || !hasEditUrl)
            {
                AppendLog("실제 수정 화면 경로: 본인 등록글의 제목을 열고 상세 화면에서 수정하기 탐색 · " + job.ListingNumber);
                var titleLink = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildOwnedTitleScript(job.ListingNumber, siteTitleSnapshot, false,
                        job.MinQuantity, job.MaxQuantity, job.Server, job.OriginalTradeSide)));
                if (titleLink.Status == "ready")
                {
                    // My Room may render clickable titles without an href.
                    // Resolve the exact row by ID+title and click only its title.
                    try
                    {
                        titleLink = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                            SitePriceEdit.BuildOwnedTitleScript(job.ListingNumber, siteTitleSnapshot, true,
                                job.MinQuantity, job.MaxQuantity, job.Server, job.OriginalTradeSide)));
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
                        SitePriceEdit.BuildDetailEditScript(job.ListingNumber, siteTitleSnapshot, false)));
                    if (detail.Status == "ready") break;
                }
                if (detail.Status != "ready")
                    throw new InvalidOperationException("등록글 상세 화면에서 수정하기를 확인하지 못했습니다 (현재 경로: " +
                        (_web.Source?.AbsolutePath ?? "없음") + "): " + detail.Message);
                try
                {
                    detail = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.BuildDetailEditScript(job.ListingNumber, siteTitleSnapshot, true)));
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
                    SitePriceEdit.BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), false,
                        siteTitleSnapshot, nativeSingleApproval || diagnosticOnly,
                        restoreCurrentTitle: !diagnosticOnly)));
                if (form.Status is "ready" or "diagnostic") break;
            }
            if (diagnosticOnly)
            {
                var inspection = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildSubmitBindingsInspectionScript(job.ListingNumber)));
                if (inspection.Status != "inspected")
                    throw new InvalidOperationException("실제 수정폼 읽기 전용 분석 실패: " + inspection.Message);
                // The previous v22.3.13 error proves an unmodified DOM0
                // onsubmit canceled the submission, but not WHY. Inspect
                // first-party static validator code without re-submitting.
                // No requests other than safe JS asset GETs are permitted.
                var validator = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildSubmitValidatorSourceScript(job.ListingNumber)));
                if (validator.Status == "started")
                {
                    var deadline = DateTime.UtcNow.AddSeconds(12);
                    validator = new SitePriceEditResult { Status = "error", Message = "공식 JS 응답 대기 시간 초과 (12초)" };
                    while (DateTime.UtcNow < deadline)
                    {
                        token.ThrowIfCancellationRequested();
                        await Task.Delay(300, token);
                        if (!IsAllowedCurrentPage() || !string.Equals(_web.Source?.AbsolutePath,
                                "/myroom/buy/buy_re_reg.html", StringComparison.OrdinalIgnoreCase))
                        {
                            validator = new SitePriceEditResult { Status = "error", Message = "분석 중 사이트 수정폼을 벗어났습니다" };
                            break;
                        }
                        var polled = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                            SitePriceEdit.SubmitValidatorSourceStatusScript));
                        if (polled.Status is "inspected" or "error")
                        {
                            validator = polled;
                            break;
                        }
                    }
                }
                string sourceNote = validator.Status == "inspected" ? validator.Message :
                    "사이트 정적 JS 분석 불가: " + (string.IsNullOrWhiteSpace(validator.Message)
                        ? "브라우저 스크립트가 결과를 반환하지 않았습니다 (WebView2 null 응답)" : validator.Message);
                Clipboard.SetText(inspection.Message + "\n\n" + sourceNote);
                foreach (var line in sourceNote.Split('\n').Where(line =>
                    line.StartsWith("추가 확인 함수:", StringComparison.Ordinal) ||
                    line.StartsWith("정적 선택자:", StringComparison.Ordinal)))
                    AppendLog(line);
                AppendLog("구매글 수정폼 검증 스크립트 읽기 전용 분석 복사 완료 (가격·제목 입력/제출 없음)");
                MessageBox.Show(this,
                    "아이템매니아 구매 수정폼의 이벤트 연결과 공식 검증 JS 분석을 복사했습니다.\n\n" +
                    "복사된 내용을 채팅에 붙여넣어 주세요. 등록번호·가격·제목·쿠키·입력값은 제외했고 " +
                    "사이트의 정적 JS 파일을 읽는 GET 외에 아무것도 제출하지 않았습니다.",
                    "가격 수정폼 분석 완료 — 제출 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
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

            // The site's own FormChecker cancels the initial submit event
            // while contact, category and editor checks run. Ensure that
            // genuine asynchronous submission chain is initialized before
            // any price is typed or any review checkpoint is persisted.
            SitePriceEditResult siteChain = new();
            for (int attempt = 0; attempt < 8; attempt++)
            {
                token.ThrowIfCancellationRequested();
                siteChain = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.OfficialPriceSubmitReadinessScript));
                if (siteChain.Status == "ready") break;
                await Task.Delay(350, token);
            }
            if (siteChain.Status != "ready")
                throw new InvalidOperationException("사이트 원본 비동기 제출 경로가 준비되지 않았습니다: " + siteChain.Message);
            // The edit page binds FormChecker before its three initialization
            // requests finish. In particular the free-pass response initializes
            // the premium continuation used by the contact approval callback.
            await submissionObserver.WaitForFormLoadAsync(token);
            if (bulkApproval) _sitePriceBatchCancellation?.Token.ThrowIfCancellationRequested();
            AppendLog("사이트 수정폼 초기 조회 완료 (안심번호·파워 등록권·무료 이용권)");
            AppendLog("구매글 사이트 비동기 제출 경로 준비 확인 (연락처·카테고리·에디터·최종 POST)");

            // Subscribe BEFORE the click. Previously the first readback
            // navigation could interrupt the POST that the site initiated.
            // No automatic fallback or duplicate POST is ever attempted.
            // Observe actual site-side JavaScript failures without changing
            // its original validators, network requests or confirmation UI.
            await _web.CoreWebView2.ExecuteScriptAsync(SitePriceEdit.InstallSiteSubmitErrorProbeScript);

            // Verify quota and scheduler state before touching the site.
            // Input/DOM checks may fail; only a genuine submit click needs
            // an uncertain-outcome checkpoint or quota attempt.
            if (fromScheduler && !_schedulerRunning)
                throw new OperationCanceledException("자동등록이 중지되어 가격 제출을 시작하지 않았습니다.");
            if (!CanRunNow(job, out quotaReason)) throw new InvalidOperationException(quotaReason);
            _armedSitePriceEditNumber = job.ListingNumber;
            _sitePriceEditDialogWarning = "";
            string submitScriptNote = "제출 버튼 클릭 전 입력 검증 중";
            string blockerNote = "";
            try
            {
                var result = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), true,
                        siteTitleSnapshot, nativeSingleApproval, trustedOneClick: true, nativeKeyboardPrice: true,
                        restoreCurrentTitle: true)));
                submitScriptNote = result.Message;
                if (result.Status != "input-ready")
                    throw new InvalidOperationException("사이트 가격 입력칸 검증 실패: " + result.Message);
                AppendLog("사이트 가격칸 실제 입력 준비: " + result.Message);
                var preflight = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.OfficialPricePreflightScript));
                if (preflight.Status != "ready")
                    throw new InvalidOperationException("사이트 수정폼 입력 검증: " + preflight.Message);
                AppendLog("사이트 구매글 제출 전 검증: " + preflight.Message);
                await TypeVerifiedSitePriceAsync(result.X, result.Y, desired, token);
                // The native Tab/blur can trigger price validation. Recheck
                // the first-party FormChecker conditions at the NEW price
                // before saving a checkpoint or clicking the submit button.
                var typedPreflight = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.OfficialPricePreflightScript));
                if (typedPreflight.Status != "ready")
                    throw new InvalidOperationException("변경 단가에 대한 사이트 제출 조건 미충족 (클릭 전 중단): " +
                        typedPreflight.Message);
                var final = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.FinalizeNativePriceScript));
                if (final.Status == "input-mismatch")
                {
                    // No POST or submit click has happened. Try only ONE
                    // focused, fully selected input-text replacement. This
                    // handles site masks that intercept per-key editing.
                    AppendLog("가격 입력칸 자릿수 검증 불일치 (사이트 제출 전): " + final.Message);
                    await ReplaceVerifiedSitePriceTextOnceAsync(desired, token);
                    typedPreflight = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.OfficialPricePreflightScript));
                    if (typedPreflight.Status != "ready")
                        throw new InvalidOperationException("단가 재입력 후 사이트 제출 조건 미충족 (클릭 전 중단): " +
                            typedPreflight.Message);
                    final = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.FinalizeNativePriceScript));
                }
                submitScriptNote = final.Message;
                if (final.Status != "armed")
                    throw new InvalidOperationException("사이트 키보드 입력 후 폼 검증 실패: " + final.Message);
                AppendLog("사이트 키보드 입력 후 제출폼 검증: " + final.Message);
                if (bulkApproval) _reportSitePriceBatchStage?.Invoke("사이트 가격 저장 요청 중");
                if (nativeSingleApproval && !backgroundApproved)
                {
                    // User-driven price editing is a separate, visible path.
                    // The site's FormChecker can cancel its initial submit
                    // with an alert, without ever issuing a POST. The user
                    // must see that native form and its validation messages.
                    // Save the uncertain-outcome checkpoint BEFORE exposing
                    // the actual site submit button, not after a possible POST.
                    job.RequiresReview = true;
                    job.Enabled = false;
                    job.LastStatus = "구매글 가격 재등록 제출 중: 결과 확인 필요";
                    job.NextDueUtc = DateTime.MaxValue;
                    if (nativeSingleApproval) job.PendingSitePriceTarget = desired.ToString();
                    job.PriceEditSiteTitleSnapshot = siteTitleSnapshot;
                    _config.Jobs = _jobs.ToList();
                    _config.Save();
                    _submissionVerificationPending = true;
                    submitted = true;
                    AppendLog("구매글 가격만 입력 완료 · 공식 사이트 수정 화면 표시 (재등록 버튼은 사용자가 직접 누름)");
                    var windowResult = ShowVisibleSinglePriceEditor(job, current, desired, submissionObserver);
                    if (!submissionObserver.FinalPostStarted)
                        await Task.Delay(1600, token);
                    bool siteButtonClicked = submissionObserver.FinalPostStarted ||
                        string.Equals(await _web.CoreWebView2.ExecuteScriptAsync(
                            SitePriceEdit.NativePriceButtonClickedScript), "true", StringComparison.OrdinalIgnoreCase);
                    if (!siteButtonClicked && !submissionObserver.FinalPostStarted)
                    {
                        // Neither an official POST nor a site submit click
                        // occurred. Restore the previous listing state.
                        submitted = false;
                        job.PendingSitePriceTarget = previousPendingPrice;
                        AppendLog("가격 수정 창을 닫았습니다. 사이트 제출이 없어 이 등록번호는 변경하지 않았습니다.");
                        return false;
                    }
                    // Opening or canceling a price window is not a listing
                    // attempt. Count the quota only after a genuine site
                    // click or first-party POST has been observed.
                    var submittedAt = DateTime.UtcNow;
                    HourlyServerQuota.MarkAttempt(HourlyServerQuota.Current(_config.ServerHourlyCounters, job, submittedAt));
                    _config.LastServerAttemptUtc[HourlyServerQuota.Key(job)] = submittedAt;
                    _config.Save();
                    submitScriptNote = windowResult == DialogResult.OK
                        ? "사용자가 공식 사이트 재등록 버튼을 눌러 제출 후 결과 확인을 요청함"
                        : "사용자가 수정 창을 닫았으나 사이트 재등록 클릭/POST 흔적이 있어 결과 확인 중";
                }
                else
                {
                    // Explicit single-item or user-approved batch only.
                    // The audited browser pointer triggers the site's own
                    // FormChecker; no synthetic fetch/form.submit is used.
                    _unattendedSitePriceEdit = true;
                    await DispatchVerifiedSitePriceClickAsync(final.X, final.Y, token, () =>
                    {
                        if (bulkApproval) _sitePriceBatchCancellation?.Token.ThrowIfCancellationRequested();
                        if (fromScheduler && !_schedulerRunning)
                            throw new OperationCanceledException("자동등록 중지로 사이트 제출 전 취소됐습니다.");
                        if (!CanRunNow(job, out var clickQuotaReason))
                            throw new InvalidOperationException(clickQuotaReason);
                        var submittedAt = DateTime.UtcNow;
                        HourlyServerQuota.MarkAttempt(HourlyServerQuota.Current(_config.ServerHourlyCounters, job, submittedAt));
                        _config.LastServerAttemptUtc[HourlyServerQuota.Key(job)] = submittedAt;
                        job.RequiresReview = true;
                        job.Enabled = false;
                        job.LastStatus = "구매글 가격 재등록 제출 중: 결과 확인 필요";
                        job.NextDueUtc = DateTime.MaxValue;
                        if (nativeSingleApproval) job.PendingSitePriceTarget = desired.ToString();
                        job.PriceEditSiteTitleSnapshot = siteTitleSnapshot;
                        _config.Jobs = _jobs.ToList();
                        _config.Save();
                        _submissionVerificationPending = true;
                        submitted = true;
                    });
                    submitScriptNote = "가격칸 실제 키보드 입력 후 재등록 버튼 브라우저 클릭 1회";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var diagnosis = ex.GetType().Name + ": " + ex.Message;
                diagnosis = diagnosis.Replace('\r', ' ').Replace('\n', ' ');
                if (diagnosis.Length > 300) diagnosis = diagnosis[..300];
                // No mouse press was attempted: the site may have accepted
                // input events, but VANTA did not request a submission.
                // Do not strand this item in review or wait 35 s for a POST.
                if (!submitted)
                    throw new InvalidOperationException("가격 입력·버튼 최종 확인 단계에서 제출 전 중단: " + diagnosis, ex);
                submitScriptNote += " · 브라우저 입력/화면 전환 결과 불확실: " + diagnosis;
                AppendLog("구매글 가격 수정 버튼/화면 전환 결과 불확실: " + diagnosis + " (중복 제출 금지)");
            }

            // Follow a clearly free, official on-page continuation if the
            // contact check approved but the site has not started category
            // validation. This alternative never re-clicks the original
            // re-registration button or sends a synthetic form POST.
            bool requiresSiteConfirmation = _unattendedSitePriceEdit &&
                await WaitForOfficialPremiumPromptAsync(submissionObserver, token);
            bool approvedBatchConfirmation = requiresSiteConfirmation && bulkApproval && backgroundApproved;
            if (approvedBatchConfirmation)
                await ConfirmZeroCostPremiumForBatchAsync(submissionObserver, token);
            var observing = requiresSiteConfirmation && !approvedBatchConfirmation
                ? submissionObserver.WaitAsync(token, TimeSpan.FromMinutes(2))
                : submissionObserver.WaitAsync(token);
            if (_unattendedSitePriceEdit && !bulkApproval && !requiresSiteConfirmation &&
                string.IsNullOrEmpty(_sitePriceEditDialogWarning))
                await TryAdvanceOfficialFreeRegistrationAsync(submissionObserver, observing, token);
            var observed = await observing;
            AppendLog("구매글 실제 사이트 수정 POST 확인: " +
                (observed.RequestObserved ? SitePriceSubmitObserver.BuyReRegistrationPostPath : "요청 없음") +
                " · HTTP " + (observed.HttpStatus?.ToString() ?? "미확인") + " · " + observed.Detail +
                (string.IsNullOrWhiteSpace(observed.OtherBuyPostPath) ? "" :
                    " · 다른 구매글 POST 경로 감지(성공 근거 아님): " + observed.OtherBuyPostPath));
            if (!observed.RequestObserved &&
                string.Equals(_web.Source?.AbsolutePath, "/myroom/buy/buy_re_reg.html", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var probeJson = await _web.CoreWebView2!.ExecuteScriptAsync(SitePriceEdit.SubmitProbeScript);
                    string? probe = System.Text.Json.JsonSerializer.Deserialize<string>(probeJson);
                    if (!string.IsNullOrWhiteSpace(probe))
                    {
                        submitScriptNote += " · " + probe;
                        AppendLog("구매글 사이트 기본 제출 진단 (조회만): " + probe);
                    }
                    var barrierJson = await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.PostContactBlockerProbeScript);
                    string? barrier = System.Text.Json.JsonSerializer.Deserialize<string>(barrierJson);
                    if (!string.IsNullOrWhiteSpace(barrier))
                    {
                        blockerNote = barrier;
                        AppendLog("구매글 공식 수정폼 후속 분기 진단 (조회만): " + barrier);
                    }
                }
                catch (Exception ex) { AppendLog("사이트 제출 이벤트 조회 실패: " + ex.GetType().Name); }
            }
            // A missing CDP POST event is NOT proof that the site rejected
            // the submission. Always read the signed-in OWN list, including
            // the no-POST case, before making a success/failure decision.
            // This is read-only and never retries the site's submit button.
            if (observed.NetworkFinished) await Task.Delay(950, token);

            _state.Text = "구매글 가격 변경 결과 확인 중";
            if (bulkApproval) _reportSitePriceBatchStage?.Invoke("같은 등록번호의 변경 가격 확인 중");
            if (!await ConfirmChangedSitePriceAsync(job, desired, token, siteTitleSnapshot))
            {
                // Unlike a missing CDP event, an actual non-S response from
                // the site's mandatory contact check proves the first-party
                // submit chain was rejected. Clear a NEW uncertainty hold
                // ONLY after a full authenticated scan independently proves
                // the old ID and price unchanged, with no replacement post.
                // Never retry automatically or mistake HTTP 200 for approval.
                if (observed.ContactCheckRejected && !observed.RequestObserved &&
                    string.IsNullOrWhiteSpace(observed.OtherBuyPostPath) &&
                    _sitePriceLastReadback?.OriginalUnchangedNoReplacement == true && !previousReview)
                {
                    job.RequiresReview = false;
                    job.Enabled = false;
                    job.NextDueUtc = DateTime.MaxValue;
                    job.PendingSitePriceTarget = previousPendingPrice;
                    job.PriceEditSiteTitleSnapshot = "";
                    job.LastStatus = "사이트 연락처 검사 거절 · 기존 등록번호·가격 유지 (자동 OFF)";
                    _config.Jobs = _jobs.ToList();
                    _config.Save();
                    _lastSitePriceEditFailure = "연락처 검사에서 사이트가 변경을 승인하지 않았습니다. " +
                        "기존 등록번호·가격 유지 확인 · 재시도 없음 · " + observed.Detail;
                    AppendLog("사이트 연락처 검사 거절 확인 · 원래 글 유지 · 가격 미적용: " +
                        job.ListingNumber + " · " + observed.Detail);
                    if (!bulkApproval) MessageBox.Show(this, _lastSitePriceEditFailure,
                        "사이트가 가격 수정을 거절함", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                throw new InvalidOperationException((observed.RequestObserved ?
                    "구매글 재등록 후 변경된 사이트 단가를 확인하지 못했습니다. " :
                    "수정 POST 요청을 감지하지 못했고 관리목록에서도 변경된 단가를 확인하지 못했습니다. ") +
                    "새 등록번호가 생겼거나 사이트가 제출을 막았을 수 있어 확인 대기 상태를 유지하며 재제출하지 않습니다." +
                    "\n사이트 단계별 검증: " + observed.Detail[..Math.Min(420, observed.Detail.Length)] +
                    "\n사이트 폼 실행: " + submitScriptNote[..Math.Min(420, submitScriptNote.Length)] +
                    (string.IsNullOrWhiteSpace(blockerNote) ? "" : "\n사이트 후속 분기: " + blockerNote) +
                    (string.IsNullOrWhiteSpace(_sitePriceReadbackNote) ? "" :
                        "\n읽기 전용 목록 검사: " + _sitePriceReadbackNote) +
                    (string.IsNullOrWhiteSpace(_sitePriceEditDialogWarning) ? "" :
                        " 사이트 메시지: " + _sitePriceEditDialogWarning));
            }
            if (!observed.RequestObserved)
                AppendLog("요청 감지 누락: 본인 관리목록의 등록번호·가격·제목이 일치하여 변경을 확인했습니다 (재제출 없음).");

            job.RequiresReview = previousReview;
            job.Enabled = previousEnabled && !previousReview;
            job.LastStatus = "사이트 구매글 단가 변경 확인: " + desired.ToString("N0") + "원";
            job.VerifiedSitePrice = desired.ToString();
            job.VerifiedSitePriceUtc = DateTime.UtcNow;
            if (nativeSingleApproval)
            {
                job.Price = desired.ToString("N0");
                job.PendingSitePriceTarget = "";
            }
            job.PriceEditSiteTitleSnapshot = "";
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
            if (showBrowser && !_web.IsDisposed)
            {
                _web.Dock = originalBrowserDock;
                _web.Bounds = originalBrowserBounds;
            }
            _unattendedSitePriceEdit = false;
            _armedSitePriceEditNumber = "";
            _submissionVerificationPending = false;
            if (!submitted)
            {
                job.RequiresReview = previousReview;
                job.Enabled = previousEnabled;
                job.NextDueUtc = previousDue;
                job.LastStatus = previousStatus;
                job.PendingSitePriceTarget = previousPendingPrice;
                _config.Jobs = _jobs.ToList();
                _config.Save();
            }
            // Finish the current verified/held site operation before applying
            // any later local price the user requested for this same listing.
            // SchedulerTick must compare its original reservation before
            // clearing it, or this newer queued target would be lost.
            int deferredPriceChanges = _deferredExecutingJobEdits.Count;
            foreach (var deferredEdit in _deferredExecutingJobEdits) deferredEdit();
            _deferredExecutingJobEdits.Clear();
            if (deferredPriceChanges > 0)
            {
                _config.Jobs = _jobs.ToList();
                _config.Save();
                AppendLog($"사이트 가격 확인 완료 · 대기 중이던 로컬 수정 {deferredPriceChanges}건 반영");
            }
            _activeSitePriceJob = null;
            _busy = false;
            _state.Text = _schedulerRunning ? "자동 실행 중" : "중지/대기";
            _jobsGrid.Refresh();
            RefreshAutoSummary();
            cancellation.Dispose();
            DispatchQueuedMyListingsImport();
        }
    }

    private sealed class SitePriceReadback
    {
        public bool ExactIdVerified { get; set; }
        public bool OriginalUnchangedNoReplacement { get; set; }
        public string Message { get; set; } = "";
    }

    private async Task InspectPendingSitePriceOnlyAsync(ListingJob job, long target,
        bool confirmLocalOnExact = false)
    {
        _busy = true;
        _state.Text = "확인 대기 구매글 사이트 가격 읽는 중 (제출 없음)";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_web.CoreWebView2 is null) throw new InvalidOperationException("아이템매니아 브라우저를 열지 못했습니다.");
            if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(cancellation.Token);
            var readback = await ReadSitePriceAfterRepostAsync(job, target, cancellation.Token);
            AppendLog("가격 확인 대기 글 재조회 (제출 없음): " + job.ListingNumber + " · " + readback.Message);
            if (confirmLocalOnExact && readback.ExactIdVerified)
            {
                if (MessageBox.Show(this,
                    "사이트 관리목록에서 기존 등록번호·제출 전 확인한 제목·변경 단가가 모두 일치합니다.\n\n" +
                    readback.Message + "\n\n프로그램의 저장 금액을 이 가격으로 맞출까요? " +
                    "확인 대기와 자동등록 중지는 그대로 유지합니다. 사이트 재제출은 하지 않습니다.",
                    "읽기 전용 검증 완료", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    job.Price = target.ToString("N0");
                    job.VerifiedSitePrice = target.ToString();
                    job.VerifiedSitePriceUtc = DateTime.UtcNow;
                    job.PendingSitePriceTarget = "";
                    job.PriceEditSiteTitleSnapshot = "";
                    job.RequiresReview = true;
                    job.Enabled = false;
                    job.NextDueUtc = DateTime.MaxValue;
                    job.LastStatus = "사이트 단가 재조회 확인 · 보류 유지: " + target.ToString("N0") + "원";
                    _config.Jobs = _jobs.ToList();
                    _config.Save();
                    AppendLog($"✓ 재제출 없이 로컬 금액 동기화 · {job.ListingNumber} · {target:N0}원 · 보류 유지");
                }
            }
            else MessageBox.Show(this,
                "사이트 관리 목록을 읽기 전용으로 확인했습니다. 가격을 다시 제출하지 않았습니다.\n\n" +
                readback.Message + "\n\n등록번호가 바뀐 경우 자동 연결하거나 재등록하지 않습니다.",
                "가격 변경 결과 재확인", MessageBoxButtons.OK,
                readback.ExactIdVerified ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            AppendLog("가격 확인 대기 글 읽기 전용 조회 실패: " + ex.Message);
            MessageBox.Show(this, "사이트 가격을 확인하지 못했습니다. 다시 제출하지 않았습니다.\n" + ex.Message,
                "가격 결과 조회 중단", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
            _state.Text = _schedulerRunning ? "자동 실행 중" : "중지/대기";
            _jobsGrid.Refresh();
            DispatchQueuedMyListingsImport();
        }
    }

    private async Task<bool> ConfirmChangedSitePriceAsync(ListingJob job, long target, CancellationToken token,
        string siteTitleSnapshot)
    {
        var readback = await ReadSitePriceAfterRepostAsync(job, target, token, siteTitleSnapshot);
        _sitePriceLastReadback = readback;
        _sitePriceReadbackNote = readback.Message;
        AppendLog("구매글 재등록 후 사이트 가격 읽기: " + readback.Message);
        return readback.ExactIdVerified;
    }

    private static string PriceReadbackTitle(string? title) =>
        Regex.Replace(MyListingsImport.CleanImportedTitle(title), @"\s+", "")
            .Trim().ToLowerInvariant();

    // Scan the authenticated OWN list, never public search results. New IDs
    // are only reported as candidates; identity is not silently reassigned.
    private async Task<SitePriceReadback> ReadSitePriceAfterRepostAsync(ListingJob job, long target,
        CancellationToken token, string? titleSnapshot = null)
    {
        string firstPage = job.OriginalTradeSide == "판매"
            ? MyListingsImport.SellListingsUrl : MyListingsImport.BuyListingsUrl;
        var queue = new Queue<string>();
        queue.Enqueue(firstPage);
        if (MyListingsImport.IsOwnedListUrl(job.SourceListUrl)) queue.Enqueue(job.SourceListUrl);
        foreach (var url in _jobs.Where(j => j.SiteId == job.SiteId &&
                j.OriginalTradeSide == job.OriginalTradeSide).Select(j => j.SourceListUrl)
            .Where(MyListingsImport.IsOwnedListUrl).Distinct(StringComparer.OrdinalIgnoreCase))
            queue.Enqueue(url);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchingOld = new List<ExistingListingRecord>();
        var newCandidates = new Dictionary<string, ExistingListingRecord>(StringComparer.Ordinal);
        int totalRows = 0;
        const int maxPages = 25;
        // For a new/held price submission, use the saved CURRENT SITE snapshot,
        // never the historical VANTA title. Older pending attempts without a
        // snapshot retain the conservative legacy check rather than guessing.
        string titleBeforeSubmit = !string.IsNullOrWhiteSpace(titleSnapshot) ? titleSnapshot :
            !string.IsNullOrWhiteSpace(job.PriceEditSiteTitleSnapshot) ? job.PriceEditSiteTitleSnapshot : job.Title;
        string expectedTitle = PriceReadbackTitle(titleBeforeSubmit);
        bool legacyTitleFallback = string.IsNullOrWhiteSpace(titleSnapshot) &&
            string.IsNullOrWhiteSpace(job.PriceEditSiteTitleSnapshot);
        string expectedServer = Regex.Replace(job.Server ?? "", @"\s+", "").ToLowerInvariant();
        while (queue.Count > 0 && visited.Count < maxPages)
        {
            token.ThrowIfCancellationRequested();
            var url = queue.Dequeue();
            if (!MyListingsImport.IsOwnedListUrl(url) ||
                MyListingsImport.IsSellListUrl(url) != (job.OriginalTradeSide == "판매") ||
                !visited.Add(url)) continue;
            await NavigateOwnedListAsync(url, token);
            var page = MyListingsImport.DecodePage(await _web.CoreWebView2!.ExecuteScriptAsync(MyListingsImport.ExtractPageScript));
            // If a transient page rendered empty, do not infer deletion.
            if (page.Listings.Count == 0 && !page.IsEmptyList)
            {
                await Task.Delay(750, token);
                page = MyListingsImport.DecodePage(await _web.CoreWebView2.ExecuteScriptAsync(MyListingsImport.ExtractPageScript));
            }
            totalRows += page.Listings.Count;
            foreach (var row in page.Listings)
            {
                if (row.TradeSide != job.OriginalTradeSide ||
                    !ListingIdentity.HasNumber(row.ListingNumber)) continue;
                if (row.ListingNumber == job.ListingNumber)
                {
                    matchingOld.Add(row);
                    continue;
                }
                if (!TrySitePrice(row.Price, out var price) || price != target ||
                    PriceReadbackTitle(row.Title) != expectedTitle ||
                    Regex.Replace(row.Server ?? "", @"\s+", "").ToLowerInvariant() != expectedServer)
                    continue;
                if (!newCandidates.ContainsKey(row.ListingNumber))
                    newCandidates.Add(row.ListingNumber, row);
            }
            foreach (var next in page.NextPageUrls)
                if (MyListingsImport.IsOwnedListUrl(next) && !visited.Contains(next) &&
                    MyListingsImport.IsSellListUrl(next) == (job.OriginalTradeSide == "판매"))
                    queue.Enqueue(next);
            if (matchingOld.Count == 1 && TrySitePrice(matchingOld[0].Price, out var exactPrice) &&
                exactPrice == target) break;
        }
        string summary = $"관리목록 {visited.Count}페이지 / {totalRows}건 검사. ";
        var oldPrices = matchingOld.Select(r => r.Price).Distinct().ToList();
        if (matchingOld.Count == 1 && TrySitePrice(matchingOld[0].Price, out var current) && current == target)
        {
            // One authenticated OWN-list row with this exact registration ID
            // and the requested site price proves the result. A historical
            // title difference never negates that proof or reassigns the ID.
            bool titleDifferent = expectedTitle.Length > 0 &&
                PriceReadbackTitle(matchingOld[0].Title) != expectedTitle;
            return new SitePriceReadback { ExactIdVerified = true,
                Message = summary + $"기존 등록번호 {job.ListingNumber}의 변경 단가 {target:N0}원 확인." +
                    (titleDifferent ? " 사이트 표시 제목은 저장된 제목과 다르지만 가격 결과 판별에는 사용하지 않았습니다." : "") };
        }
        bool originalUnchangedNoReplacement = matchingOld.Count == 1 &&
            TrySitePrice(matchingOld[0].Price, out var currentOld) &&
            TrySitePrice(job.VerifiedSitePrice, out var previouslyVerifiedOld) &&
            currentOld == previouslyVerifiedOld && currentOld != target &&
            !legacyTitleFallback && expectedTitle.Length > 0 &&
            PriceReadbackTitle(matchingOld[0].Title) == expectedTitle &&
            Regex.Replace(matchingOld[0].Server ?? "", @"\s+", "").ToLowerInvariant() == expectedServer &&
            newCandidates.Count == 0 && queue.Count == 0 && visited.Count < maxPages;
        if (oldPrices.Count > 0)
            summary += $"기존 등록번호 {job.ListingNumber} 조회 단가: {string.Join(", ", oldPrices)}원. ";
        else summary += $"기존 등록번호 {job.ListingNumber}는 검사한 목록에서 찾지 못함. ";
        if (newCandidates.Count == 1)
            summary += $"새 등록번호 후보 1건: {newCandidates.Keys.Single()} ({target:N0}원). " +
                "제목·서버·가격은 일치하지만 동일 물품인지는 사이트에서 확인 필요.";
        else if (newCandidates.Count > 1)
            summary += $"새 등록번호 후보 {newCandidates.Count}건: " +
                string.Join(", ", newCandidates.Keys.Take(4)) + ". 동일 물품을 특정할 수 없음.";
        else summary += "변경 가격과 제목·서버가 모두 일치하는 새 등록번호 후보 없음.";
        return new SitePriceReadback { OriginalUnchangedNoReplacement = originalUnchangedNoReplacement,
            Message = summary };
    }

    private void HandleSitePriceEditScriptDialog(Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogOpeningEventArgs e)
    {
        bool official = Uri.TryCreate(e.Uri, UriKind.Absolute, out var url) &&
            url.Scheme == Uri.UriSchemeHttps && ItemManiaAccountStore.IsOfficialSite(url);
        string message = (e.Message ?? "").Trim();
        string compact = message[..Math.Min(message.Length, 120)];
        bool unsafeMessage = new[] { "결제", "유료", "마일리지", "삭제", "비밀번호", "인증", "충전", "입금" }
            .Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
        bool priceRepost = message.Contains("재등록", StringComparison.Ordinal) ||
            message.Contains("가격 수정", StringComparison.Ordinal) ||
            message.Contains("물품 수정", StringComparison.Ordinal) ||
            message.Contains("수정", StringComparison.Ordinal);
        if (!official)
        {
            _sitePriceEditDialogWarning = "외부 사이트 확인창 자동 처리 차단";
            AppendLog("사이트 가격 수정 중 외부 확인창 차단");
            return;
        }
        // The official FormChecker validation uses alert() before its AJAX
        // chain, and WebView2 intentionally hides native JS dialogs. An
        // unacknowledged ALERT can stall the entire submit with no final
        // POST. Dismiss alerts only; they can NEVER approve a transaction.
        if (e.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Alert)
        {
            _sitePriceEditDialogWarning = compact;
            e.Accept();
            AppendLog("사이트 가격 수정 FormChecker 안내 (알림 닫음·제출 승인 아님): " + compact);
            return;
        }
        // Only a genuine, same-page re-registration confirmation is eligible
        // for the earlier one-item user approval. Category changes require a
        // SECOND explicit decision; never auto-approve payments/security.
        if (unsafeMessage || e.Kind != Microsoft.Web.WebView2.Core.CoreWebView2ScriptDialogKind.Confirm ||
            !string.Equals(_web.Source?.AbsolutePath, "/myroom/buy/buy_re_reg.html", StringComparison.OrdinalIgnoreCase))
        {
            _sitePriceEditDialogWarning = "사이트 추가 승인 차단: " + compact;
            AppendLog("사이트 가격 수정 추가 확인 차단: " + compact);
            return;
        }
        if (message.Contains("카테고리", StringComparison.Ordinal) &&
            message.Contains("변경", StringComparison.Ordinal))
        {
            if (_unattendedSitePriceEdit)
            {
                _sitePriceEditDialogWarning = "사이트가 카테고리 변경 추가 승인을 요청하여 무인 가격 수정을 중단했습니다";
                AppendLog(_sitePriceEditDialogWarning);
                return;
            }
            if (MessageBox.Show(this, "아이템매니아에서 카테고리 변경을 요청했습니다.\n\n" + compact +
                    "\n\n이 한 건의 변경을 계속할까요?", "사이트 카테고리 변경 확인",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                e.Accept();
                AppendLog("사이트 카테고리 변경 확인: 사용자 별도 승인");
            }
            else
            {
                _sitePriceEditDialogWarning = "사이트 카테고리 변경을 사용자가 승인하지 않았습니다";
                AppendLog(_sitePriceEditDialogWarning);
            }
            return;
        }
        if (priceRepost)
        {
            if (_unattendedSitePriceEdit)
            {
                _sitePriceEditDialogWarning = "사이트가 직접 승인을 요청하여 일괄 가격 수정을 중단했습니다";
                AppendLog(_sitePriceEditDialogWarning);
                return;
            }
            e.Accept();
            AppendLog("사이트 원본 재등록 확인 처리 (실제 성공은 관리목록 재검증): " + compact);
            return;
        }
        _sitePriceEditDialogWarning = "사이트 예상 밖 확인창 차단: " + compact;
        AppendLog(_sitePriceEditDialogWarning);
    }
}
