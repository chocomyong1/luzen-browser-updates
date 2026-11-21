using System;
using System.Collections.Generic;
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
    private string _sitePriceReadbackNote = "";
    // An in-flight site price target is deferred until its readback completes.
    // Other listings may replace their local reservation without site access.
    private ListingJob? _activeSitePriceJob;
    private bool _sitePriceBatchRunning;
    private CancellationTokenSource? _sitePriceBatchCancellation;

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
        await KeyAsync("keyDown", "a", "KeyA", 65, 2);
        await KeyAsync("keyUp", "a", "KeyA", 65, 2);
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

    // Only for one explicitly approved, exact-ID BUY form. A browser mouse
    // input activates the site's own click/submit handlers; it is NOT a
    // direct form POST, synthetic fetch, or bypass of site validation.
    private async Task DispatchVerifiedSitePriceClickAsync(double x, double y, CancellationToken token)
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
        ListingIdentity.HasNumber(job.ListingNumber) &&
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
        if (existing.Count > 0 && !SitePriceEdit.AutomaticPriceSubmissionEnabled)
        {
            problem = "제목 보호 모드에서는 기존 등록글의 가격 자동 재등록이 중지됩니다. 사이트에서 직접 수정해 주세요.";
            return false;
        }
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
            problem = "기존글의 사이트 계정이 다르거나 등록번호가 중복되어 가격 반영을 예약하지 않았습니다.";
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

    // Explicit, single-listing pilot. The site handles the native submission;
    // the scheduler, bulk selection and existing pending reservations remain
    // locked until the actual site behavior is independently verified.
    private async Task ApplySelectedSitePriceAsync()
    {
        if (_selected is null) return;
        if (_busy || _myListingsImportRunning || _sitePriceBatchRunning)
        {
            MessageBox.Show(this, "진행 중인 작업이 끝난 뒤 한 건씩 시험해 주세요.", "사이트 가격 변경");
            return;
        }
        if (_schedulerRunning) PauseScheduler();
        SaveSelectedJob(false);
        var job = _selected;
        if (job is null || !IsRegisteredOwnedBuyPriceTarget(job) || job.RequiresReview ||
            !string.IsNullOrWhiteSpace(job.PendingSitePriceTarget) ||
            _jobs.Any(other => !ReferenceEquals(other, job) &&
                string.Equals(other.SiteId, job.SiteId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(other.ListingNumber, job.ListingNumber, StringComparison.Ordinal)))
        {
            MessageBox.Show(this,
                "등록번호가 확인된 본인 구매글 중 확인 대기·기존 가격 예약이 없는 글만 한 건씩 시험할 수 있습니다.",
                "사이트 가격 변경 제한", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var dialog = new Form
        {
            Text = "사이트 구매글 가격 변경 · 단건 시험", Width = 450, Height = 220,
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false
        };
        var description = new Label
        {
            Text = $"{job.Server} · 등록번호 {job.ListingNumber}\n사이트의 기본 수정 버튼으로 1건만 처리합니다.",
            AutoSize = true, Left = 18, Top = 17
        };
        var amount = new NumericUpDown
        {
            Left = 18, Top = 77, Width = 390, Minimum = 1000, Maximum = 1000000000,
            Increment = 10, ThousandsSeparator = true, DecimalPlaces = 0
        };
        if (TrySitePrice(job.Price, out var currentAmount))
            amount.Value = Math.Clamp((decimal)currentAmount, amount.Minimum, amount.Maximum);
        var apply = new Button { Text = "1건 시험", DialogResult = DialogResult.OK,
            Left = 209, Top = 124, Width = 94, Height = 30 };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel,
            Left = 314, Top = 124, Width = 94, Height = 30 };
        dialog.Controls.AddRange(new Control[] { description, amount, apply, cancel });
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (amount.Value % 10 != 0)
        {
            MessageBox.Show(this, "가격은 10원 단위로 입력해 주세요.", "가격 확인");
            return;
        }
        await ApplySitePriceAsync(job, bulkApproval: false, nativeSingleApproval: true,
            manuallyApprovedPrice: (long)amount.Value);
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

    private async Task<bool> ApplySitePriceAsync(ListingJob? explicitJob, bool bulkApproval,
        bool fromScheduler = false, bool nativeSingleApproval = false, long? manuallyApprovedPrice = null,
        bool diagnosticOnly = false)
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
        if (nativeSingleApproval && (fromScheduler || bulkApproval || manuallyApprovedPrice is null))
        {
            _lastSitePriceEditFailure = "단건 사이트 가격 변경은 사용자가 직접 실행해야 합니다";
            return false;
        }
        if (nativeSingleApproval && (_system is "B" or "C") &&
            !DualSystemNetwork.IsRouteVerified(_system))
        {
            _lastSitePriceEditFailure = "B/C 독립 네트워크 연결을 확인할 수 없어 제출하지 않았습니다";
            MessageBox.Show(this, _lastSitePriceEditFailure, "사이트 가격 변경 중단");
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
        if (!bulkApproval && !diagnosticOnly) SaveSelectedJob(false);
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
                    "아직 실사이트 결과가 검증되지 않은 단건 시험 기능입니다. 제목 변형 위험이 남아 있습니다.\n" +
                    "먼저 1건만 시험하고 사이트에서 제목·가격을 직접 확인해 주세요.\n\n" : "") +
                "사이트의 구매 재등록 화면(frmBuy)에서 단가를 바꾼 뒤 사이트 기본 '재등록' 버튼을 사용합니다.\n" +
                "등록번호가 바뀔 수 있어 결과가 불확실하면 자동 실행을 보류합니다.\n" +
                "재등록 횟수·서버 간격 제한에 포함되며 중복 제출하지 않습니다.";
            if (!bulkApproval && !diagnosticOnly && MessageBox.Show(this, ask, "실제 사이트 구매글 가격 변경", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;

            var record = records[0];
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
                        records[0].MinQuantity, records[0].MaxQuantity)));
                if (form.Status is "ready" or "diagnostic" or "restore-needed") break;
            }
            if (diagnosticOnly)
            {
                var inspection = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildSubmitBindingsInspectionScript(job.ListingNumber)));
                if (inspection.Status != "inspected")
                    throw new InvalidOperationException("실제 수정폼 읽기 전용 분석 실패: " + inspection.Message);
                Clipboard.SetText(inspection.Message);
                AppendLog("본인 구매글 실제 수정폼 이벤트 연결 진단 복사 완료 (가격·제목 입력/제출 없음)");
                MessageBox.Show(this,
                    "실제 아이템매니아 구매 수정폼의 제출 이벤트 연결 정보를 복사했습니다.\n\n" +
                    "이 내용을 채팅에 붙여넣어 주세요. 등록번호·가격·제목·쿠키·입력값은 포함하지 않았고 " +
                    "사이트에 아무것도 제출하지 않았습니다.",
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
            bool titleRestoreApproved = false;
            if (form.Status == "restore-needed" && nativeSingleApproval)
            {
                // The official edit page turned the first 8/9/10-level token
                // into the exact, independently verified OWN-list split range.
                // Never silently submit that corrupted title or overwrite it:
                // make the one-item title-restoration risk explicit to the user.
                if (MessageBox.Show(this,
                    "사이트 수정 화면의 제목이 원본과 다르게 만들어졌습니다.\n\n" + form.Message +
                    "\n\n승인하면 단 한 건의 수정폼 제목을 관리목록에서 확인한 제목으로 되돌리고 가격을 변경합니다." +
                    "\n사이트가 제출 중 제목을 다시 바꿀 위험은 아직 실사이트에서 검증되지 않았습니다." +
                    "\n\n이 구매글 한 건만 시험할까요?",
                    "원본 제목 복구 후 단건 가격 변경 시험", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
                titleRestoreApproved = true;
            }
            if (form.Status != "ready" && !titleRestoreApproved)
                throw new InvalidOperationException("수정 페이지를 안전하게 확인하지 못했습니다: " + form.Message);

            // Subscribe BEFORE the click. Previously the first readback
            // navigation could interrupt the POST that the site initiated.
            // No automatic fallback or duplicate POST is ever attempted.
            using var submissionObserver = new SitePriceSubmitObserver(_web.CoreWebView2);
            await submissionObserver.StartAsync();

            // Commit a crash-safe review checkpoint BEFORE touching the site.
            // A navigation can abort ExecuteScriptAsync after the site accepts;
            // never retry a submission when its result is uncertain.
            if (fromScheduler && !_schedulerRunning)
                throw new OperationCanceledException("자동등록이 중지되어 가격 제출을 시작하지 않았습니다.");
            if (!CanRunNow(job, out quotaReason)) throw new InvalidOperationException(quotaReason);
            var submittedAt = DateTime.UtcNow;
            HourlyServerQuota.MarkAttempt(HourlyServerQuota.Current(_config.ServerHourlyCounters, job, submittedAt));
            _config.LastServerAttemptUtc[HourlyServerQuota.Key(job)] = submittedAt;
            job.RequiresReview = true;
            job.Enabled = false;
            job.LastStatus = "구매글 가격 재등록 제출 중: 결과 확인 필요";
            job.NextDueUtc = DateTime.MaxValue;
            // Persist the intended amount before the native click. When a
            // submit outcome is uncertain, the user can verify this exact
            // target later WITHOUT clicking the site's submit button again.
            if (nativeSingleApproval) job.PendingSitePriceTarget = desired.ToString();
            job.PriceEditSiteTitleSnapshot = siteTitleSnapshot;
            _config.Jobs = _jobs.ToList();
            _config.Save();
            _armedSitePriceEditNumber = job.ListingNumber;
            _sitePriceEditDialogWarning = "";
            _submissionVerificationPending = true;
            submitted = true;
            string submitScriptNote = "제출 스크립트 반환 전 페이지 이동";
            try
            {
                var result = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.BuildEditFormScript(job.ListingNumber, current.ToString(), desired.ToString(), true,
                        siteTitleSnapshot, nativeSingleApproval, records[0].MinQuantity, records[0].MaxQuantity,
                        titleRestoreApproved, trustedOneClick: true, nativeKeyboardPrice: true)));
                submitScriptNote = result.Message;
                if (result.Status != "input-ready")
                    throw new InvalidOperationException("사이트 가격 입력칸 검증 실패: " + result.Message);
                AppendLog("사이트 가격칸 실제 입력 준비: " + result.Message);
                await TypeVerifiedSitePriceAsync(result.X, result.Y, desired, token);
                var final = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                    SitePriceEdit.FinalizeNativePriceScript));
                if (final.Status == "input-mismatch")
                {
                    // No POST or submit click has happened. Try only ONE
                    // focused, fully selected input-text replacement. This
                    // handles site masks that intercept per-key editing.
                    AppendLog("가격 입력칸 자릿수 검증 불일치 (사이트 제출 전): " + final.Message);
                    await ReplaceVerifiedSitePriceTextOnceAsync(desired, token);
                    final = SitePriceEdit.Decode(await _web.CoreWebView2.ExecuteScriptAsync(
                        SitePriceEdit.FinalizeNativePriceScript));
                }
                submitScriptNote = final.Message;
                if (final.Status != "armed")
                    throw new InvalidOperationException("사이트 키보드 입력 후 폼 검증 실패: " + final.Message);
                AppendLog("사이트 키보드 입력 후 제출폼 검증: " + final.Message);
                // Exactly one native browser click, and only after the
                // uncertain-result review checkpoint was saved above.
                await DispatchVerifiedSitePriceClickAsync(final.X, final.Y, token);
                submitScriptNote = "가격칸 실제 키보드 입력 후 검증된 재등록 버튼 브라우저 클릭 1회 (서버 반영은 별도 확인)";
            }
            catch (Exception ex)
            {
                // A type-only log disguised a precise failed browser stage.
                // Keep the bounded local error message without request data.
                var diagnosis = ex.GetType().Name + ": " + ex.Message;
                diagnosis = diagnosis.Replace('\r', ' ').Replace('\n', ' ');
                if (diagnosis.Length > 300) diagnosis = diagnosis[..300];
                submitScriptNote += " · 브라우저 입력/화면 전환 결과 불확실: " + diagnosis;
                AppendLog("구매글 가격 수정 버튼/화면 전환 결과 불확실: " + diagnosis + " (중복 제출 금지)");
            }

            var observed = await submissionObserver.WaitAsync(token);
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
                }
                catch (Exception ex) { AppendLog("사이트 제출 이벤트 조회 실패: " + ex.GetType().Name); }
            }
            // A missing CDP POST event is NOT proof that the site rejected
            // the submission. Always read the signed-in OWN list, including
            // the no-POST case, before making a success/failure decision.
            // This is read-only and never retries the site's submit button.
            if (observed.NetworkFinished) await Task.Delay(950, token);

            _state.Text = "구매글 가격 변경 결과 확인 중";
            if (!await ConfirmChangedSitePriceAsync(job, desired, token, siteTitleSnapshot))
                throw new InvalidOperationException((observed.RequestObserved ?
                    "구매글 재등록 후 변경된 사이트 단가를 확인하지 못했습니다. " :
                    "수정 POST 요청을 감지하지 못했고 관리목록에서도 변경된 단가를 확인하지 못했습니다. ") +
                    "새 등록번호가 생겼거나 사이트가 제출을 막았을 수 있어 확인 대기 상태를 유지하며 재제출하지 않습니다." +
                    "\n사이트 폼 실행: " + submitScriptNote[..Math.Min(420, submitScriptNote.Length)] +
                    (string.IsNullOrWhiteSpace(_sitePriceReadbackNote) ? "" :
                        "\n읽기 전용 목록 검사: " + _sitePriceReadbackNote) +
                    (string.IsNullOrWhiteSpace(_sitePriceEditDialogWarning) ? "" :
                        " 사이트 메시지: " + _sitePriceEditDialogWarning));
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
            if (PriceReadbackTitle(matchingOld[0].Title) != expectedTitle ||
                (legacyTitleFallback && MyListingsImport.HasSuspiciousDuplicatedLevel(matchingOld[0].Title)))
                return new SitePriceReadback { ExactIdVerified = false,
                    Message = summary + "가격은 변경됐지만 제출 전 사이트 제목과 달라졌거나 이전 시도의 제목 기준을 확인할 수 없습니다. 자동 작업을 중지하고 제목을 확인하세요." };
            return new SitePriceReadback { ExactIdVerified = true,
                Message = summary + $"기존 등록번호 {job.ListingNumber}의 변경 단가 {target:N0}원 및 제출 전 제목 유지 확인." };
        }
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
        return new SitePriceReadback { Message = summary };
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
