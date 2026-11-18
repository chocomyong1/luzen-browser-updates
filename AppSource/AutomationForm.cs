using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LuzenBrowserReplica;

// A standalone browser-based listing workflow runner. No credentials, private APIs,
// captcha solvers, anti-bot circumvention, or original LuzenSoft DLLs are included.
internal sealed partial class AutomationForm : Form
{
    private readonly AutomationConfiguration _config;
    private readonly string _system;
    private readonly BindingList<ListingJob> _jobs;
    private readonly string _defaultSubId;
    private List<ListingJob> _filteredJobs = new();
    private bool _filterReady;
    private bool _filterChanging;
    private readonly ComboBox _fSub = FilterDropdown(132);
    private readonly ComboBox _fSite = FilterDropdown(92);
    private readonly ComboBox _fSiteId = FilterDropdown(132);
    private readonly ComboBox _fOperation = FilterDropdown(128);
    private readonly ComboBox _fGame = FilterDropdown(150);
    private readonly ComboBox _fServer = FilterDropdown(139);
    private readonly ComboBox _fCategory = FilterDropdown(110);
    private readonly ComboBox _fPaid = FilterDropdown(99);
    private readonly TextBox _fName = new() { Width = 216, PlaceholderText = "물품 제목 검색" };
    private readonly Label _filterSummary = new() { AutoSize = true, ForeColor = UiTheme.Muted };
    // Retained internally for compatibility with saved jobs; no sub-ID editor.
    private readonly TextBox _subId = new(), _siteId = new();
    private readonly CheckBox _paidRegistration = new() { Text = "유료 등록", AutoSize = true };

    private static ComboBox FilterDropdown(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, FlatStyle = FlatStyle.Standard };

    private readonly BindingList<AutomationStep> _steps = new();
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White };
    private readonly DataGridView _stepsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White };
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private ItemManiaAutoLogin? _autoLogin;
    private readonly TextBox _browserUrl = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = UiTheme.Navy, ForeColor = Color.FromArgb(222, 232, 244), Font = new Font("Consolas", 9) };
    private readonly Label _logErrors = new() { Text = "현재 오류 0건", Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
        ForeColor = UiTheme.Muted, BackColor = UiTheme.SurfaceAlt };
    private readonly ToolTip _logErrorTip = new();
    private readonly List<string> _rawLogLines = new();
    private readonly List<(string Text, bool Error)> _displayLogLines = new();
    private int _errorLogCount;
    private bool _showOnlyErrors;
    private readonly Label _state = new() { AutoSize = true, Text = "중지됨", ForeColor = Color.Maroon };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly ComboBox _editMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly RadioButton _sellRadio = new() { Text = "판매", AutoSize = true, Checked = true };
    private readonly RadioButton _buyRadio = new() { Text = "구매", AutoSize = true };
    private readonly RadioButton _renewRadio = new() { Text = "재등록", AutoSize = true };
    private readonly ComboBox _itemCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _tradingMethod = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _tradingUnit = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _minQuantity = new(), _maxQuantity = new(), _characterClass = new(), _characterLevel = new();
    private readonly CheckBox _autoDecrease = new() { Text = "수량 자동감소", AutoSize = true };
    private readonly CheckBox _instantTransaction = new() { Text = "사용 (사이트 인증 필요)", AutoSize = true };
    private readonly ComboBox _macro = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _flowMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox _autoRepost = new() { Text = "첫 등록 후 자동재등록", AutoSize = true, Checked = true };
    private readonly CheckBox _jobEnabled = new() { Text = "자동 실행 포함", AutoSize = true };
    // Game/server must be selected from verified catalog entries, never free text.
    private readonly ComboBox _game = new() { DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = false, DropDownHeight = 210 };
    private readonly ComboBox _server = new() { DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = false, DropDownHeight = 260 };
    private readonly Label _importedListingInfo = new() { AutoSize = true, Text = "새로 등록할 물품", ForeColor = Color.FromArgb(68, 81, 96) };
    private readonly TextBox _title = new(), _quantity = new(), _price = new(), _unit = new(), _description = new(), _character = new(), _entry = new();
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 10080, Value = 30, Width = 90 };
    private readonly NumericUpDown _hourLimit = new() { Minimum = 1, Maximum = 1000, Width = 68, Value = 30 };
    private readonly TextBox _flowUrl = new() { Width = 400 };
    private readonly TextBox _successSelector = new() { Width = 270 };
    private readonly TextBox _repostLinkSelector = new() { Width = 270 };
    private readonly TextBox _successUrlPart = new() { Width = 270 };
    private ListingJob? _selected;
    private bool _selectingJob;
    private bool _selectingFlow;
    private bool _schedulerRunning;
    private bool _busy;
    // Bulk edits may be made while the scheduler is running. Edits to the
    // currently executing item are deferred until that submission has fully
    // completed; otherwise a mid-flight title/price change can invalidate
    // the exact-site-row identity verification.
    private ListingJob? _currentlyExecutingJob;
    private readonly List<Action> _deferredExecutingJobEdits = new();
    private bool _pendingMyListingsImport;
    private bool _myListingsImportRunning;
    private CancellationTokenSource? _myListingsImportCancellation;
    private TaskCompletionSource<bool>? _myListingsImportCompletion;
    private CancellationTokenSource? _currentRun;
    // Owned-list verification is read-only and must never overlap a submit.
    // Check one held item periodically while the scheduler is otherwise idle.
    private CancellationTokenSource? _pendingReviewRun;
    private DateTime _nextPendingReviewUtc = DateTime.MaxValue;
    private int _pendingReviewCursor;
    // A signed-out B/C profile can redirect every managed-list request to
    // login. Do not make dozens of jobs race the same background sign-in.
    private DateTime _loginRecoveryPauseUntilUtc = DateTime.MinValue;
    private DateTime _bConnectionRecoveryUtc = DateTime.MinValue;
    private bool _bConnectionRecheckRunning;
    // Once a live submit begins, do not cancel its independent result probe.
    // The scheduler stops immediately; the one in-flight result is verified.
    private bool _submissionVerificationPending;
    // Armed only after the target's exact own-list row has been verified.
    // JavaScript confirm dialogs stay invisible; only a repost-specific prompt
    // from the official own-list page may be accepted automatically.
    private string _armedOwnListRepost = "";
    private bool _repostSuccessAlertSeen;
    private string _repostDialogBlocked = "";
    private bool _repostServerCooldownRejected;
    private int? _siteReportedIdRemaining;
    private string _pickerMode = "";
    private readonly TabControl _editorTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _autoTab = new("등록 현황");
    private readonly TabPage _itemTab = new("물품 편집");
    private readonly TabPage _workflowTab = new("고급 설정");
    private readonly DataGridView _autoGrid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false,
        RowHeadersVisible = false, BackgroundColor = Color.White, ReadOnly = false
    };
    private readonly Label _autoSummary = new()
    {
        Dock = DockStyle.Top, Height = 39, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(19, 4, 0, 0), BackColor = UiTheme.Canvas,
        ForeColor = UiTheme.Muted
    };
    private Control? _segmentedNavigation;


    public AutomationForm(string defaultSubId = "", string system = "A")
    {
        _system = system;
        _config = AutomationConfiguration.Load(system);
        _defaultSubId = defaultSubId == "Local" ? "" : defaultSubId;
        _jobs = new BindingList<ListingJob>(_config.Jobs);
        var currentSiteId = ItemManiaAccountStore.Load(_system).UserId;
        foreach (var oldJob in _jobs)
        {
            // V12 did not have these fields; preserve any explicit existing values.
            if (string.IsNullOrWhiteSpace(oldJob.SubId)) oldJob.SubId = _defaultSubId;
            if (string.IsNullOrWhiteSpace(oldJob.SiteId)) oldJob.SiteId = currentSiteId;
            // Earlier imports accidentally included '[1개~9,995개]' in titles.
            // Repair only site-imported items; preserve manually authored titles.
            if (oldJob.ImportedFromSite)
            {
                var cleanTitle = MyListingsImport.CleanImportedTitle(oldJob.Title);
                if (cleanTitle.Length > 0) oldJob.Title = cleanTitle;
            }
        }
        Text = "Luzen Browser V22 - 자동 업데이트 지원";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(960, 600);
        Size = new Size(1500, 890);
        Font = new Font("Segoe UI", 9.5F);
        Icon = new Icon(Settings.Asset("iBook1.ico"));
        _hourLimit.Value = Math.Clamp(_config.MaxSubmissionsPerHour, 1, 1000);
        _editMode.Items.AddRange(new object[] { "판매등록", "구매등록", "재등록" });
        // Only these four categories are shown in the user's original ItemMania/Luzen registration screenshots.
        _itemCategory.Items.AddRange(ItemManiaFieldCatalog.Categories.Cast<object>().ToArray());
        _itemCategory.SelectedItem = "아이템";
        // '흥정' is not a confirmed generally available type; don't invent that option.
        _tradingMethod.Items.AddRange(ItemManiaFieldCatalog.TradingMethods.Cast<object>().ToArray());
        _tradingMethod.SelectedItem = "분할";
        // Currency unit options vary by game; no unverified currency-unit category is offered.
        _tradingUnit.Items.Add("없음");
        _tradingUnit.SelectedItem = "없음";
        _macro.Items.AddRange(new object[] { "직접설정" });
        _macro.SelectedItem = "직접설정";
        _itemCategory.SelectedIndexChanged += (_, _) => UpdateContextFields();
        _tradingMethod.SelectedIndexChanged += (_, _) => UpdateContextFields();
        _sellRadio.CheckedChanged += (_, _) => UpdateTradeMode();
        _buyRadio.CheckedChanged += (_, _) => UpdateTradeMode();
        _renewRadio.CheckedChanged += (_, _) => UpdateTradeMode();
        _flowMode.Items.AddRange(new object[] { "판매등록", "구매등록", "재등록" });
        _game.Items.AddRange(GameServerCatalog.Games.Cast<object>().ToArray());
        _game.SelectedIndexChanged += (_, _) => UpdateServerOptions();
        _game.SelectedItem = GameServerCatalog.LostArk;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, BackColor = UiTheme.Canvas };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(outer);
        outer.BringToFront();
        // Run WebView2 outside the visible client area. Never attach the site
        // to a user-facing pane; the business screens show only jobs and logs.
        // A real WinForms parent/handle is still available for WebView2 scripts.
        _web.Dock = DockStyle.None;
        _web.Bounds = new Rectangle(-20000, -20000, 1280, 900);
        _web.Visible = true;
        Controls.Add(_web);
        // The list/editor and log must be independently resizable. V18 used a
        // fixed 98px table row, which prevented dragging the log boundary.
        var contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 5,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Canvas
        };
        // The native Windows tab borders gave this screen its dated appearance.
        // Keep TabControl for existing application logic, but clip its native
        // headers and supply a separate, accessible segmented navigation.
        var tabViewport = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        _editorTabs.Dock = DockStyle.None;
        const int hiddenNativeHeader = 47;
        void FitEditorPages()
        {
            if (tabViewport.ClientSize.Width < 1 || tabViewport.ClientSize.Height < 1) return;
            _editorTabs.Location = new Point(-4, -hiddenNativeHeader);
            _editorTabs.Size = new Size(tabViewport.ClientSize.Width + 8,
                tabViewport.ClientSize.Height + hiddenNativeHeader + 4);
        }
        tabViewport.Controls.Add(_editorTabs);
        tabViewport.SizeChanged += (_, _) => FitEditorPages();
        bool logSplitInitialized = false;
        bool logExpanded = false;
        int expandedLogHeight = 155;
        contentSplit.SizeChanged += (_, _) =>
        {
            if (logSplitInitialized || contentSplit.Height < 370) return;
            contentSplit.Panel1MinSize = 200;
            contentSplit.Panel2MinSize = 39;
            // Listings are the primary surface. Logs remain a one-click drawer.
            contentSplit.SplitterDistance = contentSplit.Height - contentSplit.SplitterWidth - 43;
            logSplitInitialized = true;
        };
        contentSplit.Panel1.Controls.Add(tabViewport);
        outer.Controls.Add(contentSplit, 0, 0);
        var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        logPanel.Controls.Add(_log);
        var logHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 39, ColumnCount = 3, RowCount = 1,
            BackColor = UiTheme.Surface
        };
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        var logToggle = new Label
        {
            Text = "▸  실행 기록", Cursor = Cursors.Hand,
            Dock = DockStyle.Fill, Padding = new Padding(18, 9, 0, 0),
            ForeColor = UiTheme.Text, Font = new Font(Font, FontStyle.Bold)
        };
        void SetLogExpanded(bool expanded)
        {
            if (contentSplit.Height < 270) return;
            if (logExpanded && contentSplit.Panel2.Height > 65)
                expandedLogHeight = contentSplit.Panel2.Height;
            logExpanded = expanded;
            _log.Visible = expanded;
            logToggle.Text = expanded ? "▾  실행 기록" : "▸  실행 기록";
            int target = expanded ? Math.Max(125, expandedLogHeight) : 43;
            target = Math.Min(target, contentSplit.Height - contentSplit.Panel1MinSize - contentSplit.SplitterWidth);
            contentSplit.SplitterDistance = contentSplit.Height - contentSplit.SplitterWidth - target;
        }
        logToggle.Click += (_, _) => SetLogExpanded(!logExpanded);
        contentSplit.SplitterMoved += (_, _) =>
        {
            // Dragging the original resize divider still opens/closes the
            // drawer; the label and log visibility follow the actual height.
            logExpanded = contentSplit.Panel2.Height > 65;
            _log.Visible = logExpanded;
            logToggle.Text = logExpanded ? "▾  실행 기록" : "▸  실행 기록";
            if (logExpanded) expandedLogHeight = contentSplit.Panel2.Height;
        };
        logHeader.Controls.Add(logToggle, 0, 0);
        _logErrors.Click += (_, _) =>
        {
            _showOnlyErrors = !_showOnlyErrors;
            RefreshErrorCounter();
            RenderCompactLog();
        };
        _logErrorTip.SetToolTip(_logErrors,
            "오류 숫자는 이번 실행에서 발생한 오류만 셉니다. 확인 필요는 이전 실행에서 저장된 보류 상태일 수 있습니다.");
        logHeader.Controls.Add(_logErrors, 1, 0);
        var copyErrors = new Button
        {
            Text = "오류 로그 복사", Dock = DockStyle.Fill,
            Margin = new Padding(4, 3, 8, 3)
        };
        UiTheme.StyleButton(copyErrors);
        copyErrors.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(ErrorLogCopy.Build(_rawLogLines,
                    AppUpdateService.CurrentVersion, DateTime.Now, _system));
                copyErrors.Text = "복사 완료 ✓";
            }
            catch (Exception ex)
            {
                copyErrors.Text = "복사 실패";
                AppendLog("오류 로그 복사 실패: " + ex.GetType().Name);
            }
        };
        logHeader.Controls.Add(copyErrors, 2, 0);
        logPanel.Controls.Add(logHeader);
        contentSplit.Panel2.Controls.Add(logPanel);
        _log.Visible = false;

        BuildAutoRegistrationTab();
        BuildJobTab();
        BuildWorkflowTab();
        _segmentedNavigation = BuildSegmentedNavigation();
        contentSplit.Panel1.Controls.Add(_segmentedNavigation);
        ConfigureJobsGrid();
        ConfigureAutoGrid();
        InstallListingHeaderSorting();
        InitializeBatchContextMenus();
        ConfigureStepsGrid();
        _jobsGrid.EnableHeadersVisualStyles = false;
        _jobsGrid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.SurfaceAlt;
        _jobsGrid.AlternatingRowsDefaultCellStyle.BackColor = UiTheme.Canvas;
        _jobsGrid.ColumnHeadersHeight = 32;
        _jobsGrid.RowTemplate.Height = 27;
        InitializeModernGrids();
        InstallSelectAllHeaders();
        UiTheme.StyleTabs(_editorTabs);
        UiTheme.ApplyControlStyles(this);
        _stepsGrid.DataSource = _steps;
        InitializeFiltersAndRefresh();
        RefreshAutoSummary();
        _jobsGrid.SelectionChanged += (_, _) => SelectCurrentJob();
        _jobsGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) { RestoreEditorPanel(); SelectCurrentJob(); }
        };
        _flowMode.SelectedIndexChanged += (_, _) => LoadWorkflow();
        _flowMode.SelectedIndex = 0;
        if (_jobs.Count == 0) AddJob();
        else { if (_jobsGrid.Rows.Count > 0) _jobsGrid.Rows[0].Selected = true; SelectCurrentJob(); }

        _web.NavigationCompleted += (_, e) => { if (!IsDisposed && _web.CoreWebView2 is not null) _browserUrl.Text = _web.Source?.ToString() ?? ""; };
        _web.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess || _web.CoreWebView2 is null) return;
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _web.CoreWebView2.ScriptDialogOpening += OnItemManiaScriptDialog;
            _autoLogin = new ItemManiaAutoLogin(_web, message => AppendLog(message), _system);
            ItemManiaPopupHandler.InstallSilent(_web, message => AppendLog(message));
        };
        Shown += async (_, _) =>
        {
            try
            {
                await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
                if (_web.CoreWebView2 is not null)
                {
                    _web.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                    _web.CoreWebView2.Navigate("https://www.itemmania.com/");
                }
            }
            catch (Exception ex)
            {
                AppendLog("내부 사이트 엔진 시작 실패: " + ex.Message);
                NotifyAutomationIssue("내부 브라우저 시작 실패", ex.Message);
            }
            AppendLog("V22: 자동 업데이트 준비 · 자동 헤더 일괄선택 · 조회 취소 후 실행");
            int pending = _jobs.Count(j => j.RequiresReview);
            if (pending > 0)
            {
                AppendLog($"주의: 결과 확인 대기 {pending}건. 자동 시작 후 사이트 등록시각을 백그라운드에서 검사합니다. 결과가 불명확한 글만 보류됩니다.");
                foreach (var review in _jobs.Where(j => j.RequiresReview).Take(10))
                    AppendLog($"보류 사유: {review.Server} · {review.ListingNumber} · {review.LastStatus} (이전 실행 포함)");
                if (pending > 10)
                    AppendLog($"보류 사유: 나머지 {pending - 10}건은 물품 목록에서 확인하세요.");
                _state.Text = $"확인 필요 {pending}건";
                _state.ForeColor = Color.Firebrick;
            }
        };
        _timer.Tick += async (_, _) =>
        {
            try { await SchedulerTickAsync(); }
            catch (OperationCanceledException) when (!_schedulerRunning)
            {
                // User pressed pause/stop while an awaited operation was pending.
            }
            catch (Exception ex)
            {
                _schedulerRunning = false;
                _timer.Stop();
                _state.Text = "오류로 중지";
                _state.ForeColor = Color.Firebrick;
                RefreshAutoSummary();
                AppendLog("자동등록 예기치 못한 중단: " + ex.Message);
                NotifyAutomationIssue("자동등록 예기치 못한 중단", ex.Message);
            }
        };
        FormClosing += (_, _) =>
        {
            _sitePriceBatchCancellation?.Cancel();
            _siteRequestTraceActive = false;
            DetachSiteTrace();
            _timer.Stop(); _currentRun?.Cancel(); _pendingReviewRun?.Cancel();
            _schedulerRunning = false;
            try { SaveAll(); } catch { }
        };
    }

    // The host owns the single run toolbar. No secondary toolbar is rendered
    // above the tabs, so status and listing actions cannot be duplicated.
    public Task ImportMyRegisteredListingsFromHeaderAsync() => ImportMyRegisteredListingsAsync();
    public void StartAutomationFromHeader() => StartScheduler();
    public void PauseAutomationFromHeader() => PauseScheduler();
    public void StopAutomationFromHeader() => StopScheduler();
    public Task ProbeSelectedRepostFromHeaderAsync() => ProbeSelectedRepostReadOnlyAsync();
    public Task TestSelectedNewRegistrationFromHeaderAsync()
    {
        if (_schedulerRunning || _busy || _myListingsImportRunning)
        {
            MessageBox.Show(this, "자동등록·등록글 조회가 끝난 후 신규글 입력시험을 실행하세요. 시험 중에는 사이트에 제출하지 않습니다.",
                "신규글 입력시험 대기", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }
        if (_selected is null || _selected.Operation is not ("판매등록" or "구매등록") ||
            _selected.InitialCompleted || _selected.ImportedFromSite)
        {
            MessageBox.Show(this, "물품 편집에서 아직 등록하지 않은 신규 판매글 또는 구매글 1개를 선택해 주세요.",
                "신규글 입력시험 대상", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }
        if (_selected.PaidRegistration || _selected.InstantTransaction)
        {
            MessageBox.Show(this, "입력시험은 일반등록만 지원합니다. 유료등록·즉시구매 설정을 해제한 물품을 선택하세요.",
                "신규글 입력시험 제한", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }
        if (!_config.WorkflowFor(_selected.Operation).Steps.Any(s =>
            s.Action == "제출" && !string.IsNullOrWhiteSpace(s.Selector)))
        {
            MessageBox.Show(this, "신규 등록 작업에 최종 제출 버튼 단계가 없습니다. 관리 → 고급 설정에서 확인하세요.",
                "신규글 입력시험 설정", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }
        return RunSelectedAsync(dryRun: true);
    }
    public Task ToggleSiteTraceFromHeaderAsync(ToolStripItem menuItem) => ToggleSiteRequestTraceAsync(menuItem);
    public bool SiteTraceIsActive => _siteRequestTraceActive;

    public void OpenHourlyLimitSettings()
    {
        if (_busy || _schedulerRunning || _sitePriceBatchRunning)
        {
            MessageBox.Show(this, "자동 실행 또는 사이트 작업을 중지한 뒤 등록 횟수를 변경하세요.", "시간당 등록 설정");
            return;
        }
        using var dialog = new Form
        {
            Text = "ID·서버별 시간당 등록 횟수", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(395, 170)
        };
        var number = new NumericUpDown
        {
            Minimum = 1, Maximum = 1000, Value = _hourLimit.Value,
            Location = new Point(224, 32), Width = 118
        };
        dialog.Controls.Add(new Label
        {
            Text = "ID·서버별 시간당 최대 등록", AutoSize = true,
            Location = new Point(22, 37)
        });
        dialog.Controls.Add(number);
        dialog.Controls.Add(new Label
        {
            Text = "같은 서버는 최소 21초 간격을 유지합니다.", AutoSize = true,
            ForeColor = UiTheme.Muted, Location = new Point(23, 77)
        });
        var ok = new Button
        {
            Text = "저장", Width = 98, Height = 32, Location = new Point(244, 115),
            DialogResult = DialogResult.OK
        };
        UiTheme.StyleButton(ok, UiTheme.ButtonTone.Primary);
        dialog.Controls.Add(ok);
        dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _hourLimit.Value = number.Value;
        SaveAll();
        RefreshAutoSummary();
    }

    private static void AddInline(FlowLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label
        {
            Text = label, AutoSize = true, ForeColor = UiTheme.Muted,
            Padding = new Padding(11, 5, 3, 0)
        });
        control.Margin = new Padding(0, 2, 3, 2);
        panel.Controls.Add(control);
    }

    private Control BuildBrowser()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 3 };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        bar.Controls.Add(_browserUrl, 0, 0);
        _browserUrl.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenBrowserUrl(); e.SuppressKeyPress = true; } };
        var open = new Button { Text = "이동", Dock = DockStyle.Fill };
        open.Click += (_, _) => OpenBrowserUrl(); bar.Controls.Add(open, 1, 0);
        var picker = new Button { Text = "요소 선택", Dock = DockStyle.Fill };
        picker.Click += async (_, _) => await StartSelectorPickerAsync();
        bar.Controls.Add(picker, 2, 0);
        var explain = new Label { Text = "사이트 로그인은 직접 진행 · 요소 선택은 현재 페이지의 입력칸/버튼을 클릭", Dock = DockStyle.Top, Height = 28, ForeColor = Color.DimGray, Padding = new Padding(5, 5, 0, 0) };
        panel.Controls.Add(_web);
        panel.Controls.Add(explain);
        panel.Controls.Add(bar);
        return panel;
    }

    private static Label PinkLabel(string label) => new()
    {
        Text = label, AutoSize = false, Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, BackColor = UiTheme.SurfaceAlt,
        ForeColor = UiTheme.Muted, Margin = new Padding(0, 3, 6, 3),
        Padding = new Padding(10, 0, 0, 0)
    };

    private static FlowLayoutPanel Horizontal(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0)
        };
        foreach (var control in controls)
        {
            control.Margin = new Padding(2, 3, 7, 2);
            if (control is TextBox or ComboBox) control.Width = Math.Max(control.Width, 100);
            row.Controls.Add(control);
        }
        return row;
    }

    // Apple-inspired, progressive-disclosure filters: the everyday game/server/
    // title controls stay visible. Less-used account/type filters open on demand.
    // Search applies ONLY to local listing rows, never to site jobs or scheduling.
    private Control BuildSearchFilters()
    {
        var search = new Panel { Dock = DockStyle.Top, Height = 51, BackColor = UiTheme.Surface };
        var first = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 50, Padding = new Padding(15, 9, 0, 5),
            WrapContents = false, AutoScroll = true, BackColor = UiTheme.Surface
        };
        var advanced = new Panel { Dock = DockStyle.Top, Height = 88,
            BackColor = UiTheme.SurfaceAlt, Visible = false };
        var second = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 43, Padding = new Padding(15, 8, 0, 4),
            WrapContents = false, AutoScroll = true, BackColor = UiTheme.SurfaceAlt
        };
        var third = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 43, Padding = new Padding(15, 7, 0, 3),
            WrapContents = false, AutoScroll = true, BackColor = UiTheme.SurfaceAlt
        };
        static Label Tag(string title) => new()
        {
            Text = title, AutoSize = true, Height = 29, Padding = new Padding(4, 7, 5, 0),
            ForeColor = UiTheme.Muted, BackColor = Color.Transparent,
            Margin = new Padding(2, 1, 2, 1)
        };
        void Add(FlowLayoutPanel line, string caption, Control ctrl)
        {
            line.Controls.Add(Tag(caption));
            ctrl.Margin = new Padding(0, 2, 12, 2); line.Controls.Add(ctrl);
        }
        Add(first, "게임", _fGame);
        Add(first, "서버", _fServer);
        Add(first, "검색", _fName);
        Add(second, "서브 ID", _fSub);
        Add(second, "사이트", _fSite);
        Add(second, "사이트 ID", _fSiteId);
        Add(second, "거래 구분", _fOperation);
        Add(third, "물품구분", _fCategory);
        Add(third, "유료등록", _fPaid);
        var reset = new Button { Text = "필터 초기화", Width = 104, Height = 30,
            Margin = new Padding(9, 0, 0, 0) };
        UiTheme.StyleButton(reset);
        reset.Click += (_, _) => { ResetFiltersToAll(); RefreshFilteredLists(); };
        third.Controls.Add(reset);
        var toggle = new Button { Text = "세부 필터  ▾", Width = 118, Height = 31,
            Margin = new Padding(0, 0, 8, 0) };
        UiTheme.StyleButton(toggle);
        toggle.Click += (_, _) =>
        {
            bool expanded = !advanced.Visible;
            advanced.Visible = expanded;
            search.Height = expanded ? 139 : 51;
            toggle.Text = expanded ? "세부 필터  ▴" : "세부 필터  ▾";
        };
        first.Controls.Add(toggle);
        _filterSummary.Margin = new Padding(13, 7, 0, 0);
        first.Controls.Add(_filterSummary);
        // Last added Top control is highest; keep the compact search always on top.
        advanced.Controls.Add(third);
        advanced.Controls.Add(second);
        search.Controls.Add(advanced);
        search.Controls.Add(first);
        foreach (var combo in new[] { _fSub, _fSite, _fSiteId, _fOperation, _fGame, _fServer, _fCategory, _fPaid })
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (!_filterReady || _filterChanging) return;
                if (ReferenceEquals(combo, _fGame)) RefreshServerFilterChoices();
                RefreshFilteredLists();
            };
        _fName.TextChanged += (_, _) => { if (_filterReady && !_filterChanging) RefreshFilteredLists(); };
        return search;
    }

    private static string Value(ComboBox combo) => combo.SelectedItem?.ToString() ?? "전체";

    private static void SetOptions(ComboBox combo, IEnumerable<string> options)
    {
        var selected = Value(combo);
        combo.Items.Clear();
        combo.Items.Add("전체");
        foreach (var value in options.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(v => v, StringComparer.CurrentCulture))
            if (!string.Equals(value, "전체", StringComparison.OrdinalIgnoreCase)) combo.Items.Add(value);
        combo.SelectedItem = combo.Items.Contains(selected) ? selected : "전체";
    }

    private void InitializeFiltersAndRefresh()
    {
        RefreshFilterChoices();
        _filterReady = true;
        RefreshFilteredLists();
    }

    private void RefreshFilterChoices()
    {
        _filterChanging = true;
        try
        {
            SetOptions(_fSub, _jobs.Select(j => j.SubId));
            SetOptions(_fSite, new[] { "매니아" });
            SetOptions(_fSiteId, _jobs.Select(j => j.SiteId));
            SetOptions(_fOperation, new[] { "판매", "구매", "재등록" });
            SetOptions(_fGame, _jobs.Select(j => j.Game).Concat(GameServerCatalog.Games));
            SetOptions(_fCategory, ItemManiaFieldCatalog.Categories);
            SetOptions(_fPaid, new[] { "무료", "유료" });
            RefreshServerFilterChoicesCore();
        }
        finally { _filterChanging = false; }
    }

    private void RefreshServerFilterChoices()
    {
        _filterChanging = true;
        try { RefreshServerFilterChoicesCore(); }
        finally { _filterChanging = false; }
    }

    private void RefreshServerFilterChoicesCore()
    {
        string game = Value(_fGame);
        var servers = _jobs.Where(j => game == "전체" || j.Game == game).Select(j => j.Server);
        if (game != "전체") servers = servers.Concat(GameServerCatalog.Servers(game));
        SetOptions(_fServer, servers);
    }

    private bool MatchesFilter(ListingJob job)
    {
        static bool Match(string selected, string actual) => selected == "전체" ||
            string.Equals(selected, actual, StringComparison.OrdinalIgnoreCase);
        string op = job.Operation switch { "판매등록" => "판매", "구매등록" => "구매", _ => "재등록" };
        return Match(Value(_fSub), job.SubId) &&
               Match(Value(_fSite), "매니아") &&
               Match(Value(_fSiteId), job.SiteId) &&
               Match(Value(_fOperation), op) &&
               Match(Value(_fGame), job.Game) &&
               Match(Value(_fServer), job.Server) &&
               Match(Value(_fCategory), job.ItemCategory) &&
               Match(Value(_fPaid), job.PaidRegistration ? "유료" : "무료") &&
               (string.IsNullOrWhiteSpace(_fName.Text) || job.Title.Contains(_fName.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    private void ResetFiltersToAll()
    {
        _filterChanging = true;
        try
        {
            foreach (var combo in new[] { _fSub, _fSite, _fSiteId, _fOperation, _fGame, _fServer, _fCategory, _fPaid })
                if (combo.Items.Contains("전체")) combo.SelectedItem = "전체";
            _fName.Clear();
        }
        finally { _filterChanging = false; }
    }

    private void RefreshFilteredLists(string? focusId = null)
    {
        if (!_filterReady || _filterChanging) return;
        if (!_selectingJob) SaveSelectedJob(false);
        focusId ??= _selected?.Id;
        _filteredJobs = SortFilteredListingJobs(_jobs.Where(MatchesFilter));
        _selectingJob = true;
        try
        {
            _jobsGrid.DataSource = new BindingList<ListingJob>(_filteredJobs);
            _autoGrid.DataSource = new BindingList<ListingJob>(_filteredJobs.ToList());
            _selected = null;
            var target = _jobsGrid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => (r.DataBoundItem as ListingJob)?.Id == focusId)
                ?? _jobsGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            _jobsGrid.ClearSelection();
            if (target is not null)
            {
                _jobsGrid.CurrentCell = target.Cells[Math.Min(2, target.Cells.Count - 1)];
                target.Selected = true;
            }
        }
        finally { _selectingJob = false; }
        if (_jobsGrid.CurrentRow is not null) SelectCurrentJob();
        else ClearEditor();
        RefreshBatchSummary();
        RefreshAutoSummary();
        RefreshSelectAllHeaderVisuals();
        RefreshListingSortGlyphs();
    }

    private void RefreshBatchSummary()
    {
        if (!_filterReady) return;
        int marked = _filteredJobs.Count(j => j.SelectedForBulk);
        _filterSummary.Text = $"조회 {_filteredJobs.Count}/{_jobs.Count}개   ·   선택 {marked}개";
        if (_batchCountLabel is not null && !_batchCountLabel.IsDisposed)
            _batchCountLabel.Text = $"선택 {marked}건  /  조회 {_filteredJobs.Count}건";
    }

    private void SetVisibleBatchSelection(bool selected)
    {
        if (_sitePriceBatchRunning)
        {
            MessageBox.Show(this, "사이트 가격 순차 반영이 끝난 뒤 선택을 바꾸세요.", "가격 반영 중");
            return;
        }
        _jobsGrid.EndEdit(); _autoGrid.EndEdit();
        foreach (var job in _filteredJobs) job.SelectedForBulk = selected;
        _jobsGrid.Refresh(); _autoGrid.Refresh(); RefreshBatchSummary();
        RefreshSelectAllHeaderVisuals();
        AppendLog($"조회 결과 {_filteredJobs.Count}건 {(selected ? "전체 선택" : "선택 해제")}");
    }

    // Pick one row as the price reference. Never mix other site IDs, sales,
    // hidden rows or unverified registration IDs into a live price batch.
    private void SelectSamePriceListings()
    {
        if (_sitePriceBatchRunning)
        {
            MessageBox.Show(this, "사이트 가격 순차 반영이 끝난 뒤 같은 가격을 다시 선택하세요.", "가격 반영 중");
            return;
        }
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        var activeGrid = _editorTabs.SelectedTab == _autoTab ? _autoGrid : _jobsGrid;
        var reference = activeGrid.CurrentRow?.DataBoundItem as ListingJob;
        if (reference is null || !_filteredJobs.Contains(reference) ||
            !TrySitePrice(reference.Price, out var amount) ||
            !reference.ImportedFromSite || reference.OriginalTradeSide != "구매")
        {
            MessageBox.Show(this, "먼저 불러온 본인 구매글 중 가격을 기준으로 삼을 물품 한 줄을 클릭하세요.", "같은 가격 선택");
            return;
        }
        var matched = _filteredJobs.Where(job =>
            job.ImportedFromSite && job.OriginalTradeSide == "구매" &&
            !job.RequiresReview && ListingIdentity.HasNumber(job.ListingNumber) &&
            MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job)) &&
            string.Equals(job.SiteId, reference.SiteId, StringComparison.OrdinalIgnoreCase) &&
            TrySitePrice(job.Price, out var price) && price == amount).ToList();
        var batch = matched.Take(20).ToList();
        foreach (var job in _jobs) job.SelectedForBulk = batch.Contains(job);
        _jobsGrid.Refresh();
        _autoGrid.Refresh();
        RefreshBatchSummary();
        RefreshSelectAllHeaderVisuals();
        AppendLog($"같은 가격 선택: {reference.SiteId} · {amount:N0}원 · {matched.Count}건 중 {batch.Count}건 (본인 구매글만)");
        MessageBox.Show(this,
            $"{reference.SiteId} · {amount:N0}원인 본인 구매글 {matched.Count}건 중 {batch.Count}건을 체크했습니다.\n\n" +
            "[일괄수정]에서 새 금액을 적용하고 [예]를 누르면 다음 자동등록 순번에 예약됩니다.\n" +
            "확인 대기·판매글·다른 계정 물품은 제외됩니다. 한 번에 최대 20개씩 예약할 수 있습니다.\n" +
            (matched.Count > 20 ? "나머지는 첫 묶음을 예약한 뒤 조건을 다시 선택해 주세요." : ""),
            "같은 가격 선택 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SetBatchEnabled(bool enabled)
    {
        if (_sitePriceBatchRunning)
        {
            MessageBox.Show(this, "사이트 가격 순차 반영이 끝난 뒤 자동 ON/OFF를 변경하세요.", "가격 반영 중");
            return;
        }
        if (_busy) { MessageBox.Show(this, _myListingsImportRunning
            ? "등록글 조회 중입니다. '즉시 중지'로 조회를 중단하거나 조회 완료 후 다시 시도하세요."
            : "작업 실행 중에는 자동 사용 여부를 일괄 변경할 수 없습니다."); return; }
        // Never change jobs outside the CURRENT search result.
        var selected = _filteredJobs.Where(j => j.SelectedForBulk).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "조회 결과 목록에서 먼저 '선택' 체크박스를 선택하세요."); return; }
        int pending = selected.Count(j => j.RequiresReview);
        int approved = enabled && pending > 0 ? ApproveImportedReviewsForBulk(selected) : 0;
        pending = selected.Count(j => j.RequiresReview);
        if (enabled && pending == selected.Count)
        {
            MessageBox.Show(this,
                $"체크한 {selected.Count}건 모두 확인 필요 상태라 자동 ON을 적용하지 않았습니다.\n" +
                "사이트의 등록번호와 실제 가격을 확인한 물품만 자동실행할 수 있습니다.", "자동 ON 보류");
            return;
        }
        if (approved == 0 && MessageBox.Show(this, $"선택한 {selected.Count}건을 자동 {(enabled ? "ON" : "OFF")} 할까요?\n확인 필요로 제외: {(enabled ? pending : 0)}건",
            "선택 물품 일괄변경", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SaveSelectedJob(false);
        foreach (var job in selected)
        {
            job.Enabled = enabled && !job.RequiresReview;
            if (job.Enabled && job.NextDueUtc == DateTime.MaxValue)
                job.NextDueUtc = DateTime.UtcNow;
        }
        _config.Jobs = _jobs.ToList(); _config.Save();
        _selected = null;
        RefreshFilteredLists();
        AppendLog($"선택한 {selected.Count}건 자동 {(enabled ? "ON" : "OFF")} 적용 / 보류 {pending}건");
    }

    // V12: This is an INDEPENDENT automation view. V11's two top navigation
    // buttons both selected the same tab; therefore they appeared to do nothing.
    private Control BuildSegmentedNavigation()
    {
        var nav = new Panel { Dock = DockStyle.Top, Height = 61, BackColor = UiTheme.Canvas };
        nav.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border });
        var choices = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, Width = 328, Height = 59, WrapContents = false,
            Padding = new Padding(19, 12, 0, 5), BackColor = UiTheme.Canvas
        };
        // Advanced settings still exist and remain accessible from [관리].
        // They are intentionally absent from the everyday two-tab navigation.
        var pages = new[] { _autoTab, _itemTab };
        var labels = new[] { "등록 현황", "물품 편집" };
        var buttons = new List<Button>();
        for (int i = 0; i < pages.Length; i++)
        {
            TabPage page = pages[i];
            var button = new Button
            {
                Text = labels[i], Width = 140, Height = 34,
                Margin = new Padding(0, 0, 6, 0)
            };
            UiTheme.StyleButton(button);
            button.Click += (_, _) => _editorTabs.SelectedTab = page;
            choices.Controls.Add(button);
            buttons.Add(button);
        }
        void UpdateSelection()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool chosen = _editorTabs.SelectedTab == pages[i];
                var button = buttons[i];
                button.BackColor = chosen ? UiTheme.Surface : UiTheme.Canvas;
                button.ForeColor = chosen ? UiTheme.Text : UiTheme.Muted;
                button.FlatAppearance.BorderSize = chosen ? 1 : 0;
                button.FlatAppearance.BorderColor = UiTheme.Border;
                button.Font = new Font("Segoe UI", 9.5F,
                    chosen ? FontStyle.Bold : FontStyle.Regular);
            }
        }
        _editorTabs.SelectedIndexChanged += (_, _) => UpdateSelection();
        _autoSummary.Dock = DockStyle.Fill;
        _autoSummary.BackColor = UiTheme.Canvas;
        _autoSummary.AutoEllipsis = true;
        _autoSummary.TextAlign = ContentAlignment.MiddleRight;
        _autoSummary.Padding = new Padding(2, 0, 20, 0);
        nav.Controls.Add(_autoSummary);
        nav.Controls.Add(choices);
        UpdateSelection();
        return nav;
    }

    private void BuildAutoRegistrationTab()
    {
        _editorTabs.TabPages.Add(_autoTab);
        // This page is for monitoring. Creation and bulk editing belong to
        // the product-editing page; a double-click still opens that editor.
        _autoGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditAutoSelectedJob(); };
        _autoTab.Controls.Add(_autoGrid);
    }

    private void ConfigureAutoGrid()
    {
        _autoGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "BatchSelected", HeaderText = "선택", DataPropertyName = "SelectedForBulk", Width = 54
        });
        _autoGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled", HeaderText = "사용", DataPropertyName = "Enabled", Width = 54
        });
        void Add(string header, string field, int width)
        {
            _autoGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header, DataPropertyName = field, Width = width,
                ReadOnly = true
            });
        }
        Add("구분", "Operation", 85);
        Add("게임", "Game", 107);
        Add("서버", "Server", 105);
        Add("수량", "Quantity", 75);
        Add("가격", "Price", 95);
        _autoGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "물품 제목", DataPropertyName = "Title", MinimumWidth = 200,
            ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 390
        });
        Add("다음 등록", "NextDueLocal", 140);
        Add("상태", "LastStatus", 200);
        _autoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "최소", DataPropertyName = "MinQuantity", Width = 68, ReadOnly = true });
        _autoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "최대", DataPropertyName = "MaxQuantity", Width = 68, ReadOnly = true });
        _autoGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "재등록(분)", DataPropertyName = "IntervalMinutes", Width = 88, ReadOnly = true });
        _autoGrid.ColumnHeadersHeight = 34;
        _autoGrid.RowTemplate.Height = 27;
        _autoGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_autoGrid.IsCurrentCellDirty)
                _autoGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _autoGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _selectingJob) return;
            if (_autoGrid.Rows[e.RowIndex].DataBoundItem is not ListingJob changed) return;
            if (_autoGrid.Columns[e.ColumnIndex].Name == "BatchSelected")
            { _jobsGrid.Refresh(); RefreshBatchSummary(); return; }
            if (_autoGrid.Columns[e.ColumnIndex].Name != "Enabled") return;
            if (changed.Enabled && changed.RequiresReview)
            {
                changed.Enabled = false;
                MessageBox.Show(this, "이 물품은 '확인 필요' 상태입니다. 실제 등록글의 정보와 등록번호를 확인한 뒤 보류를 해제하세요.", "자동 실행 보류");
            }
            if (changed.Enabled && changed.NextDueUtc == DateTime.MaxValue)
                changed.NextDueUtc = DateTime.UtcNow;
            if (_selected?.Id == changed.Id) _jobEnabled.Checked = changed.Enabled;
            _config.Jobs = _jobs.ToList(); _config.Save();
            _jobsGrid.Refresh(); RefreshAutoSummary();
        };
        _autoGrid.DataError += (_, e) => { e.ThrowException = false; };
    }

    private void RefreshAutoSummary()
    {
        if (_autoSummary.IsDisposed) return;
        int enabled = _jobs.Count(x => x.Enabled);
        int pending = _jobs.Count(x => x.RequiresReview);
        int scheduledPrices = _jobs.Count(x => !string.IsNullOrWhiteSpace(x.PendingSitePriceTarget));
        string state = _system == "B" && _schedulerRunning && _bConnectionRecheckRunning
            ? "B VPN 연결 재확인 중" :
            _system == "B" && _schedulerRunning && _bConnectionRecoveryUtc > DateTime.UtcNow
            ? "B VPN 연결 대기" : _sitePriceBatchRunning ? _state.Text :
            _schedulerRunning ? "자동 실행 중" : "중지/대기";
        _autoSummary.Text = $"전체 {_jobs.Count}개     ·     자동 사용 {enabled}개     ·     가격 예약 {scheduledPrices}개     ·     확인 필요 {pending}개     ·     {state}";
    }

    private void EditAutoSelectedJob()
    {
        if (_autoGrid.CurrentRow?.DataBoundItem is not ListingJob job)
        {
            MessageBox.Show(this, "편집할 물품을 먼저 선택하세요.", "물품 선택");
            return;
        }
        if (_splitStatusMode) RequestSingleViewFromSplit?.Invoke(_system);
        ShowItemRegistrationView();
        RestoreEditorPanel();
        foreach (DataGridViewRow row in _jobsGrid.Rows)
        {
            if (row.DataBoundItem is not ListingJob candidate || candidate.Id != job.Id) continue;
            _jobsGrid.ClearSelection();
            row.Selected = true;
            if (row.Cells.Count > 1) _jobsGrid.CurrentCell = row.Cells[1];
            SelectCurrentJob();
            break;
        }
    }

    public void ShowAutomationView()
    {
        _editorTabs.SelectedTab = _autoTab;
        _autoGrid.Refresh();
        RefreshAutoSummary();
        AppendLog("화면 전환: 자동등록 현황");
    }

    public void ShowItemRegistrationView()
    {
        _editorTabs.SelectedTab = _itemTab;
        // The item tab starts hidden while the automation dashboard is open,
        // so its SplitContainer handle may not exist during the Form.Shown event.
        if (!_itemListFirstShown)
        {
            _itemListFirstShown = true;
            _jobsSplit?.CreateControl();
            if (!_wideGridEnabled) ToggleWideGrid();
        }
        AppendLog("화면 전환: 물품 목록 · 더블클릭으로 상세 편집");
    }

    private void BuildJobTab()
    {
        var page = _itemTab;
        _editorTabs.TabPages.Add(page);
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            BorderStyle = BorderStyle.None, SplitterWidth = 8, IsSplitterFixed = false,
            BackColor = UiTheme.Border
        };
        _jobsSplit = split;
        // Let the user drag the divider. Only set a sensible FIRST layout;
        // never reset the user's split distance on later resizes.
        bool initialized = false;
        split.SizeChanged += (_, _) =>
        {
            if (initialized || split.Width < 780) return;
            var desired = Math.Clamp((int)(split.Width * 0.68), 340, split.Width - 320 - split.SplitterWidth);
            split.SplitterDistance = desired;
            split.Panel1MinSize = 340;
            split.Panel2MinSize = 320;
            initialized = true;
        };
        split.Panel1.Controls.Add(_jobsGrid);
        // Keep everyday actions visible; less-used settings live in one dropdown.
        var toolsPanel = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = UiTheme.Surface };
        var primary = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 53, AutoScroll = true,
            WrapContents = false, Padding = new Padding(15, 10, 0, 6), BackColor = UiTheme.Surface };
        Button ActionButton(string caption, int width, Action run, bool primaryAction = false)
        {
            var button = new Button { Text = caption, Width = width, Height = 33,
                Margin = new Padding(2, 0, 9, 0) };
            UiTheme.StyleButton(button, primaryAction ? UiTheme.ButtonTone.Primary : UiTheme.ButtonTone.Neutral);
            void FitText() => button.Width = Math.Max(width,
                TextRenderer.MeasureText(caption, button.Font).Width + button.Padding.Horizontal + 26);
            button.FontChanged += (_, _) => FitText();
            FitText();
            button.Click += (_, _) => run();
            return button;
        }
        primary.Controls.Add(ActionButton("+ 새 물품", 107, AddListing));
        primary.Controls.Add(ActionButton("같은 가격 선택", 122, SelectSamePriceListings));
        primary.Controls.Add(ActionButton("일괄수정", 105, BulkEditSelected, true));
        primary.Controls.Add(ActionButton("가격 반영 예약", 138, () => _ = ApplySelectedSitePricesAsync()));
        var selectionTools = new ContextMenuStrip();
        selectionTools.Items.Add("조회 전체선택", null, (_, _) => SetVisibleBatchSelection(true));
        selectionTools.Items.Add("선택 해제", null, (_, _) => SetVisibleBatchSelection(false));
        selectionTools.Items.Add(new ToolStripSeparator());
        selectionTools.Items.Add("선택 물품 자동 ON", null, (_, _) => SetBatchEnabled(true));
        selectionTools.Items.Add("선택 물품 자동 OFF", null, (_, _) => SetBatchEnabled(false));
        selectionTools.Items.Add(new ToolStripSeparator());
        selectionTools.Items.Add("선택 물품 가격 변경 예약 (1건)", null, (_, _) => _ = ApplySelectedSitePriceAsync());
        selectionTools.Items.Add("표 넓게 보기/복원", null, (_, _) => ToggleWideGrid());
        selectionTools.Items.Add("열 너비 초기화", null, (_, _) => ResetGridColumns());
        selectionTools.Items.Add(new ToolStripSeparator());
        selectionTools.Items.Add("CSV 가져오기", null, (_, _) => ImportJobsCsv());
        selectionTools.Items.Add("CSV 내보내기", null, (_, _) => ExportJobsCsv());
        Button? selectionToolsButton = null;
        selectionToolsButton = ActionButton("더보기 ▾", 99,
            () => selectionTools.Show(selectionToolsButton!, 0, selectionToolsButton!.Height));
        primary.Controls.Add(selectionToolsButton);
        // The visible count lives in the filter/search strip. Repeating it on
        // the action toolbar made the screen look busier without adding value.
        toolsPanel.Controls.Add(primary);
        split.Panel1.Controls.Add(toolsPanel);
        split.Panel1.Controls.Add(BuildSearchFilters());

        var right = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new Padding(10, 8, 10, 10), BackColor = UiTheme.Surface
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        int row = 0;
        void Field(string label, Control c, int height = 33)
        {
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            editor.Controls.Add(PinkLabel(label), 0, row);
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, 3, 0, 3);
            editor.Controls.Add(c, 1, row++);
        }
        var radios = Horizontal(_sellRadio, _buyRadio, _renewRadio);
        Field("구분", radios, 34);
        Field("사이트 아이디", _siteId, 33);
        Field("유료 등록", _paidRegistration, 33);
        Field("사이트", new Label { Text = "아이템매니아 (매니아)", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 32);
        Field("게임", _game, 33);
        Field("서버", _server, 33);
        Field("물품구분", _itemCategory, 33);
        Field("거래유형", _tradingMethod, 33);
        // No fictitious common 'item info' or arbitrary money-unit dropdown.
        _quantity.Width = 110; _unit.Width = 92;
        _unit.PlaceholderText = "수량 단위";
        Field("수량 / 단위", Horizontal(_quantity, _unit), 33);
        Field("금액 (원)", _price, 33);
        _minQuantity.Width = 92; _maxQuantity.Width = 92;
        Field("최소/최대수량", Horizontal(
            new Label { Text = "최소", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _minQuantity,
            new Label { Text = "최대", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _maxQuantity), 36);
        // Local automation-only controls and game-dependent character attributes are
        // intentionally NOT presented as if the site universally requires them.
        Field("캐릭터명", _character, 33);
        Field("물품제목", _title, 33);
        _description.Multiline = true; _description.ScrollBars = ScrollBars.Vertical;
        Field("상세설명", _description, 95);
        Field("재등록 주기", Horizontal(_interval,
            new Label { Text = "분", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }), 35);
        Field("연속 재등록", _autoRepost, 33);
        Field("등록여부", _jobEnabled, 34);
        _entry.PlaceholderText = "첫 등록 후 재등록할 내 물품 상세/관리 URL";
        Field("기존 등록글", _importedListingInfo, 33);
        Field("재등록 대상 URL", _entry, 33);
        scroll.Controls.Add(editor);
        // Do not present guessed classifications. Actual game-specific subcategories
        // require a verified live form and are not pre-populated here.
        UpdateContextFields();

        // The right editor can be only ~320 px wide. A fixed 80px footer used
        // to clip its third/fourth line of buttons when Windows scaling changed.
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 51,
            AutoScroll = false, WrapContents = true, Padding = new Padding(5, 5, 5, 5),
            BackColor = Color.White };
        void FitBottomHeight()
        {
            int available = bottom.ClientSize.Width - bottom.Padding.Horizontal;
            if (available < 120) return;
            int used = 0, lines = 1;
            foreach (Control item in bottom.Controls)
            {
                int needed = item.Width + item.Margin.Horizontal;
                if (used > 0 && used + needed > available) { lines++; used = 0; }
                used += needed;
            }
            int height = bottom.Padding.Vertical + lines * 37 + 4;
            if (bottom.Height != height) bottom.Height = height;
        }
        bottom.SizeChanged += (_, _) => FitBottomHeight();
        Button FooterButton(string text, int width, Action action, bool primaryAction = false)
        {
            var b = new Button { Text = text, Width = width, Height = 33,
                TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(3) };
            void FitText()
            {
                b.Width = Math.Max(width, TextRenderer.MeasureText(text, b.Font).Width +
                    b.Padding.Horizontal + 28);
                FitBottomHeight();
            }
            b.FontChanged += (_, _) => FitText();
            b.Click += (_, _) => action(); bottom.Controls.Add(b);
            UiTheme.StyleButton(b, primaryAction ? UiTheme.ButtonTone.Primary : UiTheme.ButtonTone.Neutral);
            FitText();
            return b;
        }
        FooterButton("저장", 86, () => { SaveSelectedJob(); RefreshFilterChoices(); RefreshFilteredLists(); }, true);
        FooterButton("가격 변경 예약", 131, () => _ = ApplySelectedSitePriceAsync());
        var extraActions = new ContextMenuStrip();
        extraActions.Items.Add("서버별 복제", null, (_, _) => CopySelectedToOtherServers());
        extraActions.Items.Add("완료 표시 초기화", null, (_, _) =>
        {
            if (_selected is null) return;
            if (_selected.RequiresReview)
            {
                MessageBox.Show(this, "확인 대기 중인 글은 완료 표시 초기화로 보류를 해제할 수 없습니다. 아이템매니아에서 실제 상태를 확인한 뒤 '사이트 확인 후 보류 해제'를 이용하세요.", "중복 등록 방지");
                return;
            }
            _selected.InitialCompleted = false; _selected.RequiresReview = false;
            _selected.NextDueUtc = DateTime.MinValue; _selected.LastStatus = "대기";
            SaveAll(); _jobsGrid.Refresh();
        });
        extraActions.Items.Add("사이트 확인 후 보류 해제", null, (_, _) =>
        {
            if (_selected is null || !_selected.RequiresReview) return;
            if (MessageBox.Show(this, "아이템매니아에서 실제 등록 여부를 먼저 확인했습니까? 중복 등록이 없는 것을 확인한 경우에만 해제하세요.",
                "보류 해제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (_selected.InitialCompleted && _selected.AutoRepost &&
                string.IsNullOrWhiteSpace(_selected.EntryUrl) && string.IsNullOrWhiteSpace(OwnedListUrlFor(_selected)))
            {
                MessageBox.Show(this, "먼저 오른쪽 '재등록 대상 URL'에 해당 물품의 상세/관리 페이지 주소를 입력하고 저장하세요.");
                return;
            }
            _selected.RequiresReview = false;
            _selected.Enabled = false; _jobEnabled.Checked = false;
            _selected.LastStatus = "사이트 확인 완료 (자동 실행 꺼짐)";
            _selected.NextDueUtc = DateTime.MaxValue;
            SaveAll(); _jobsGrid.Refresh(); RefreshAutoSummary();
        });
        Button? extraButton = null;
        extraButton = FooterButton("추가 작업  ▾", 112,
            () => extraActions.Show(extraButton!, 0, extraButton!.Height));
        right.SizeChanged += (_, _) => FitBottomHeight();
        bottom.HandleCreated += (_, _) => FitBottomHeight();
        right.Controls.Add(scroll);
        right.Controls.Add(bottom);
        right.Controls.Add(new Label { Text = "물품 상세 설정", Dock = DockStyle.Top, Height = 35,
            Padding = new Padding(16, 9, 0, 0), BackColor = UiTheme.Canvas,
            ForeColor = UiTheme.Text, Font = new Font(this.Font, FontStyle.Bold) });
        split.Panel2.Controls.Add(right);
        page.Controls.Add(split);
    }

    private void UpdateTradeMode()
    {
        if (_selectingJob) return;
        var op = _sellRadio.Checked ? "판매등록" : _buyRadio.Checked ? "구매등록" : "재등록";
        _editMode.SelectedItem = op;
        _newJobMode = op;
        UpdateContextFields();
    }

    private void UpdateContextFields()
    {
        // Keep shared form inputs simple: split-specific quantity bounds are optional
        // unless the selected transaction is explicitly a split transaction.
        bool split = string.Equals(_tradingMethod.SelectedItem?.ToString(), "분할", StringComparison.Ordinal);
        _minQuantity.Enabled = split;
        _maxQuantity.Enabled = split;
        _instantTransaction.Enabled = _buyRadio.Checked;
        if (!_buyRadio.Checked) _instantTransaction.Checked = false;
    }

    private void SetTradeMode(string? operation)
    {
        _sellRadio.Checked = operation == "판매등록";
        _buyRadio.Checked = operation == "구매등록";
        _renewRadio.Checked = operation == "재등록";
        _editMode.SelectedItem = operation;
    }

    private void BuildWorkflowTab()
    {
        var page = _workflowTab;
        _editorTabs.TabPages.Add(page);
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, Padding = new Padding(6, 6, 0, 0) };
        AddInline(header, "설정할 작업", _flowMode);
        var help = new Button { Text = "단계 설명", Width = 93, Height = 28 };
        help.Click += (_, _) => MessageBox.Show(this,
            "이동: URL 열기 ({EntryUrl}은 물품별 URL 우선)\n입력: CSS 선택자의 입력칸에 값 입력\n클릭: 버튼 클릭\n선택: select 태그 또는 지정한 목록 내부에서 실제 옵션 선택\n체크: 체크박스 선택\n대기: 요소가 나타날 때까지 대기\n멈춤: 밀리초 대기\n제출: 최종 등록 버튼 클릭\n\n아이템매니아 웹페이지는 표시하지 않습니다. 기본 필드는 라벨 기반 자동 탐색(@auto:)이며, 일치하는 항목이 없거나 여러 개면 등록을 중단합니다.\n\n{Game}, {Server}, {Title}, {Quantity}, {Unit}, {Price}, {Character}, {Description}, {EntryUrl} 변수 지원.\n\n사이트 구조 변화나 추가 인증이 필요하면 실제 제출이 실패할 수 있습니다.",
            "사이트 작업 설정 방법", MessageBoxButtons.OK, MessageBoxIcon.Information);
        header.Controls.Add(help);
        var diagnostic = new Button { Text = "사이트 입력칸 진단 (개발용)", Width = 196, Height = 28 };
        diagnostic.Click += async (_, _) =>
        {
            if (MessageBox.Show(this,
                "사이트의 입력칸 이름과 버튼 정보를 JSON으로 저장합니다. 물품을 등록하지는 않습니다.\n" +
                "일반 사용에는 필요 없는 개발·오류 분석 기능이며, 파일을 공유하기 전 내용 확인이 필요합니다.\n\n실행할까요?",
                "사이트 구조 진단", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                await ExportSiteDiagnosticsAsync();
        };
        header.Controls.Add(diagnostic);
        var props = new TableLayoutPanel { Dock = DockStyle.Top, Height = 131, ColumnCount = 2, RowCount = 4, Padding = new Padding(8) };
        props.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); props.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(int i, string name, Control c) { props.Controls.Add(new Label { Text = name, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, i); c.Dock = DockStyle.Fill; props.Controls.Add(c, 1, i); }
        Row(0, "기본 URL", _flowUrl); Row(1, "성공 CSS", _successSelector);
        Row(2, "성공 URL 단서", _successUrlPart); Row(3, "등록글 링크 CSS", _repostLinkSelector);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(7, 5, 0, 0) };
        void Btn(string t, Action callback, int w) { var b = new Button { Text = t, Width = w, Height = 29 }; b.Click += (_, _) => callback(); bottom.Controls.Add(b); }
        Btn("단계 추가", AddStep, 86); Btn("단계 삭제", DeleteStep, 86); Btn("▲", () => MoveStep(-1), 42); Btn("▼", () => MoveStep(1), 42);
        Btn("백그라운드 URL 열기", () => NavigateWorkflowUrl(), 148);
        Btn("입력칸 자동 탐색", async () => await DiscoverFormFieldsAsync(), 142);
        Btn("단계 저장", SaveWorkflow, 94);
        page.Controls.Add(_stepsGrid);
        page.Controls.Add(bottom);
        page.Controls.Add(props);
        page.Controls.Add(header);
    }

    private void OpenItemManiaAccountSettings()
    {
        using var dialog = new ItemManiaAccountForm(_system);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (_web.CoreWebView2 is not null) _web.CoreWebView2.Navigate("https://www.itemmania.com/");
        AppendLog("아이템매니아 계정 저장 완료. 백그라운드에서 로그인을 시도합니다.");
    }

    private void ConfigureJobsGrid()
    {
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "BatchSelected", HeaderText = "선택", DataPropertyName = "SelectedForBulk", Width = 47 });
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "자동", DataPropertyName = "Enabled", Width = 45 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "구분", DataPropertyName = "Operation", Width = 83, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "사이트 ID", DataPropertyName = "SiteId", Width = 92, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "등록번호", DataPropertyName = "ListingNumber", Width = 129, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "원래 거래", DataPropertyName = "OriginalTradeSide", Width = 69, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "게임", DataPropertyName = "Game", Width = 100, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "서버", DataPropertyName = "Server", Width = 100, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "물품구분", DataPropertyName = "ItemCategory", Width = 92, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "수량", DataPropertyName = "QuantityDisplay", Width = 78, ReadOnly = true,
            ToolTipText = "확인된 수량과 단위를 합쳐 표시합니다. 미수집은 사이트에서 확인되지 않은 수량입니다." });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "가격(원)", DataPropertyName = "Price", Width = 112, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "거래유형", DataPropertyName = "TradingMethod", Width = 83, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "최소", DataPropertyName = "MinQuantity", Width = 70, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "최대", DataPropertyName = "MaxQuantity", Width = 70, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "재등록(분)", DataPropertyName = "IntervalMinutes", Width = 86, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상품/제목", DataPropertyName = "Title", MinimumWidth = 140, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 385 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "캐릭터", DataPropertyName = "CharacterDisplay", Width = 98, ReadOnly = true,
            ToolTipText = "미수집: 사이트의 목록·상세 화면에서 확인되지 않은 값입니다. 자동으로 임의 입력하지 않습니다." });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상세설명", DataPropertyName = "DescriptionDisplay", Width = 250, ReadOnly = true,
            ToolTipText = "미수집: 사이트의 목록·상세 화면에서 확인되지 않은 값입니다. 자동으로 임의 입력하지 않습니다." });
        _jobsGrid.CurrentCellDirtyStateChanged += (_, _) => { if (_jobsGrid.IsCurrentCellDirty) _jobsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _jobsGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _selectingJob) return;
            if (_jobsGrid.Rows[e.RowIndex].DataBoundItem is not ListingJob changed) return;
            if (_jobsGrid.Columns[e.ColumnIndex].Name == "BatchSelected")
            { _autoGrid.Refresh(); RefreshBatchSummary(); return; }
            if (_jobsGrid.Columns[e.ColumnIndex].Name != "Enabled") return;
            if (changed.Enabled && changed.RequiresReview)
            {
                changed.Enabled = false;
                MessageBox.Show(this, "이 물품은 '확인 필요' 상태입니다. 실제 등록글의 정보와 등록번호를 확인한 뒤 보류를 해제하세요.", "자동 실행 보류");
            }
            if (changed.Enabled && changed.NextDueUtc == DateTime.MaxValue)
                changed.NextDueUtc = DateTime.UtcNow;
            if (_selected?.Id == changed.Id) _jobEnabled.Checked = changed.Enabled;
            _config.Jobs = _jobs.ToList(); _config.Save(); _jobsGrid.Refresh(); RefreshAutoSummary();
        };
    }

    private void ConfigureStepsGrid()
    {
        var type = new DataGridViewComboBoxColumn { HeaderText = "종류", DataPropertyName = "Action", Width = 72 };
        type.Items.AddRange("이동", "입력", "클릭", "선택", "체크", "대기", "멈춤", "제출");
        _stepsGrid.Columns.Add(type);
        _stepsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CSS 선택자", DataPropertyName = "Selector", Width = 160, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _stepsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "입력값 / 변수", DataPropertyName = "Value", Width = 170 });
        _stepsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "대기(초)", DataPropertyName = "TimeoutSeconds", Width = 72 });
        _stepsGrid.DataError += (_, e) => { e.ThrowException = false; };
    }

    // Called by the host browser's left navigation. Workflow mode is selected
    // without mutating an existing listing; the next new listing uses the mode.
    private string _newJobMode = "판매등록";
    public void SelectRegistrationMode(string operation)
    {
        if (_flowMode.Items.Contains(operation))
        {
            // Select the mode for NEW jobs; never silently change a saved job.
            _newJobMode = operation;
            _flowMode.SelectedItem = operation;
            ShowItemRegistrationView();
        }
    }

    // Top-level original-style toolbar delegates. All actions operate on local job
    // data and never open a visible browser.
    public void AddListing() { AddJob(); ShowItemRegistrationView(); RestoreEditorPanel(); }
    public void DuplicateListing() => CloneJob();
    public void RemoveListing() => DeleteJob();
    public void SaveListings() { SaveAll(); RefreshFilterChoices(); RefreshFilteredLists(); }

    // Updates must never interrupt an in-flight site action or queued import.
    public bool CanInstallSoftwareUpdate =>
        !_busy && !_schedulerRunning && !_myListingsImportRunning && !_pendingMyListingsImport;
    // Each network keeps its own job list and login, but the SAME ItemMania
    // account/server quota must be shared. Otherwise A and B could both spend
    // the same remaining ID allocation in the same hour.
    public void ShareSiteQuotaWith(AutomationForm primary)
    {
        foreach (var (key, b) in _config.ServerHourlyCounters)
        {
            if (!primary._config.ServerHourlyCounters.TryGetValue(key, out var a) || a is null || a.HourKey != b.HourKey)
            {
                if (a is null || string.CompareOrdinal(a.HourKey, b.HourKey) < 0)
                    primary._config.ServerHourlyCounters[key] = b;
                continue;
            }
            a.Attempts = Math.Max(a.Attempts, b.Attempts);
            if (b.SiteRemaining.HasValue)
                a.SiteRemaining = a.SiteRemaining.HasValue
                    ? Math.Min(a.SiteRemaining.Value, b.SiteRemaining.Value) : b.SiteRemaining;
        }
        foreach (var (key, time) in _config.LastServerAttemptUtc)
            if (!primary._config.LastServerAttemptUtc.TryGetValue(key, out var existing) || time > existing)
                primary._config.LastServerAttemptUtc[key] = time;
        _config.ServerHourlyCounters = primary._config.ServerHourlyCounters;
        _config.LastServerAttemptUtc = primary._config.LastServerAttemptUtc;
    }
    public void RefreshListings() { RefreshFilterChoices(); RefreshFilteredLists(); AppendLog("로컬 물품 목록 새로고침"); }
    public void OpenAccountSettings() => OpenItemManiaAccountSettings();
    public void ShowWorkflowSettings() => _editorTabs.SelectedTab = _workflowTab;
    public void StopAndSave()
    {
        _timer.Stop(); _currentRun?.Cancel(); _schedulerRunning = false;
        try { SaveAll(); } catch { }
    }

    private void NavigateWorkflowUrl()
    {
        var url = _flowUrl.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !ItemManiaAccountStore.IsOfficialSite(uri))
        { MessageBox.Show(this, "아이템매니아 공식 HTTPS 주소만 사용할 수 있습니다."); return; }
        if (_web.CoreWebView2 is null)
        { AppendLog("내부 사이트 엔진 준비 중입니다."); return; }
        _web.CoreWebView2.Navigate(uri.AbsoluteUri);
        AppendLog("백그라운드 사이트 이동: " + uri.Host + uri.AbsolutePath);
    }

    private void UpdateServerOptions(string? requested = null)
    {
        // Called on game changes. Do not silently select the first server:
        // choosing a wrong server would create an incorrect listing.
        string game = _game.SelectedItem?.ToString() ?? "";
        string previous = requested ?? _server.SelectedItem?.ToString() ?? "";
        _server.BeginUpdate();
        try
        {
            _server.Items.Clear();
            foreach (string name in GameServerCatalog.Servers(game)) _server.Items.Add(name);
            // Preserve old config values without making them valid for new posts.
            if (!string.IsNullOrWhiteSpace(previous) && !_server.Items.Contains(previous))
                _server.Items.Add(previous);
            _server.SelectedItem = _server.Items.Contains(previous) ? previous : null;
        }
        finally { _server.EndUpdate(); }
    }

    private void SetJobCatalogSelection(ListingJob job)
    {
        // Old saved jobs may use games not yet in the catalog: display their
        // original values, but ValidateJob will prohibit unmapped posting.
        if (!string.IsNullOrWhiteSpace(job.Game) && !_game.Items.Contains(job.Game))
            _game.Items.Add(job.Game);
        _game.SelectedItem = _game.Items.Contains(job.Game) ? job.Game : null;
        UpdateServerOptions(job.Server);
    }

    private void CopySelectedToOtherServers()
    {
        if (_busy) { MessageBox.Show(this, "작업 중에는 복제할 수 없습니다."); return; }
        SaveSelectedJob(false);
        if (_selected is null) return;
        var servers = GameServerCatalog.Servers(_selected.Game);
        if (servers.Count == 0) { MessageBox.Show(this, "아직 지원되지 않는 게임입니다."); return; }
        if (_selected.Operation == "재등록")
        { MessageBox.Show(this, "재등록용 원본 글 링크는 서버별로 다릅니다. 신규 등록 물품만 서버별 복제할 수 있습니다."); return; }
        if (string.IsNullOrWhiteSpace(_selected.Title) || _selected.Title == "신규 물품")
        { MessageBox.Show(this, "먼저 물품 제목을 입력하고 저장하세요."); return; }
        var existing = _jobs.Select(j => (j.Operation, j.Game, j.Server, j.Title)).ToHashSet();
        var missing = servers.Where(server => !existing.Contains((_selected.Operation, _selected.Game, server, _selected.Title))).ToList();
        if (missing.Count == 0) { MessageBox.Show(this, "같은 제목·구분의 물품이 이미 모든 서버에 있습니다."); return; }

        using var choose = new Form
        {
            Text = "복제할 서버 선택", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(310, 365),
            MaximizeBox = false, MinimizeBox = false, Font = Font
        };
        var checkList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None };
        foreach (var server in missing) checkList.Items.Add(server);
        var note = new Label { Dock = DockStyle.Top, Text = "원하는 서버만 체크하세요 (자동 실행은 꺼짐)", Height = 28, Padding = new Padding(8, 5, 0, 0) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 47, Padding = new Padding(8, 8, 0, 0) };
        var all = new Button { Text = "전체 선택", Width = 86 };
        all.Click += (_, _) => { for (int i = 0; i < checkList.Items.Count; i++) checkList.SetItemChecked(i, true); };
        var none = new Button { Text = "해제", Width = 58 };
        none.Click += (_, _) => { for (int i = 0; i < checkList.Items.Count; i++) checkList.SetItemChecked(i, false); };
        var okay = new Button { Text = "복제", Width = 66, DialogResult = DialogResult.OK };
        buttons.Controls.Add(all); buttons.Controls.Add(none); buttons.Controls.Add(okay);
        choose.Controls.Add(checkList); choose.Controls.Add(note); choose.Controls.Add(buttons);
        choose.AcceptButton = okay;
        if (choose.ShowDialog(this) != DialogResult.OK) return;
        var targets = checkList.CheckedItems.Cast<string>().ToArray();
        if (targets.Length == 0) { MessageBox.Show(this, "복제할 서버를 하나 이상 체크하세요."); return; }
        if (MessageBox.Show(this, $"{targets.Length}개 서버 물품을 추가할까요?\n실제 등록은 실행하지 않습니다.",
            "복제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (string server in targets)
        {
            var copy = JsonSerializer.Deserialize<ListingJob>(JsonSerializer.Serialize(_selected))!;
            copy.Id = Guid.NewGuid().ToString("N"); copy.Server = server;
            // Site-specific deep links must not be reused for another server.
            copy.EntryUrl = ""; copy.SourceListUrl = ""; copy.ListingNumber = ""; copy.OriginalTradeSide = "";
            copy.ImportedFromSite = false; copy.ImportedUtc = null;
            copy.Enabled = false; copy.InitialCompleted = false; copy.RequiresReview = false;
            copy.NextDueUtc = DateTime.MinValue; copy.LastSucceededUtc = null;
            copy.LastStatus = "복제됨 (자동 꺼짐)";
            _jobs.Add(copy);
        }
        _config.Jobs = _jobs.ToList(); _config.Save();
        RefreshFilterChoices(); RefreshFilteredLists();
        AppendLog($"서버별 복제 완료: {string.Join(", ", targets)} (자동 실행 꺼짐)");
    }

    private void AddJob()
    {
        SaveSelectedJob(false);
        var accountId = ItemManiaAccountStore.Load(_system).UserId;
        var job = new ListingJob { Operation = _newJobMode, Game = GameServerCatalog.LostArk, Server = "", Title = "신규 물품", Enabled = false, SubId = _defaultSubId, SiteId = accountId };
        _jobs.Add(job);
        _config.Jobs = _jobs.ToList();
        _config.Save();
        ResetFiltersToAll();
        RefreshFilteredLists(job.Id);
    }

    private void CloneJob()
    {
        SaveSelectedJob();
        if (_selected is null) return;
        var job = JsonSerializer.Deserialize<ListingJob>(JsonSerializer.Serialize(_selected))!;
        job.Id = Guid.NewGuid().ToString("N"); job.Title += " (복사)";
        // Cloning an existing account listing must NEVER re-use its listing ID or URL.
        job.ListingNumber = ""; job.SourceListUrl = ""; job.OriginalTradeSide = ""; job.ImportedFromSite = false;
        job.ImportedUtc = null; job.EntryUrl = "";
        job.InitialCompleted = false; job.RequiresReview = false;
        job.NextDueUtc = DateTime.MinValue; job.LastSucceededUtc = null; job.LastStatus = "대기"; job.Enabled = false;
        _jobs.Add(job); _config.Jobs = _jobs.ToList(); _config.Save();
        RefreshFilterChoices(); RefreshFilteredLists(job.Id);
    }

    private void ImportJobsCsv()
    {
        if (_busy) { MessageBox.Show(this, "실행 중에는 가져올 수 없습니다."); return; }
        using var dialog = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv", Title = "물품 CSV 가져오기" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SaveSelectedJob(false);
            var imported = CsvJobStorage.Import(dialog.FileName);
            // Site listing ID is the primary identity. Two posts can share
            // the same server/title but must not be merged as if identical.
            static string Identity(ListingJob job)
            {
                string owner = (job.SiteId ?? "").Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(job.ListingNumber))
                    return "ID|" + owner + "|" + job.ListingNumber.Trim();
                if (MyListingsImport.IsOfficialUrl(job.EntryUrl))
                    return "URL|" + owner + "|" + job.EntryUrl.Trim();
                return "LOCAL|" + owner + "|" + job.Operation + "|" + job.Game + "|" + job.Server + "|" + job.Title;
            }
            var existing = _jobs.Select(Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var distinct = new List<ListingJob>();
            int skipped = 0;
            foreach (var item in imported)
            {
                if (!existing.Add(Identity(item))) { skipped++; continue; }
                distinct.Add(item);
            }
            if (distinct.Count == 0)
            { MessageBox.Show(this, $"추가할 새 물품이 없습니다. (중복 {skipped}건)"); return; }
            if (MessageBox.Show(this, $"{distinct.Count}건을 물품 목록에 추가할까요? (중복 {skipped}건 제외)\n자동 등록은 모두 꺼진 상태로 가져옵니다.", "CSV 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var job in distinct) _jobs.Add(job);
            _config.Jobs = _jobs.ToList(); _config.Save();
            RefreshFilterChoices(); RefreshFilteredLists();
            AppendLog($"CSV 가져오기: {distinct.Count}건 추가, {skipped}건 중복 제외");
        }
        catch (Exception ex) { MessageBox.Show(this, "CSV 가져오기 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BulkEditSelected() => ShowModernBatchEditor();

    private void ExportJobsCsv()
    {
        try
        {
            SaveSelectedJob(false);
            using var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "아이템매니아_물품목록.csv", Title = "물품 CSV 내보내기" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            CsvJobStorage.Export(dialog.FileName, _jobs);
            AppendLog($"CSV 내보내기: {_jobs.Count}건");
            MessageBox.Show(this, "물품 목록을 저장했습니다.\n파일에는 물품 제목/설명 등이 포함되므로 공유할 때 주의하세요.", "내보내기 완료");
        }
        catch (Exception ex) { MessageBox.Show(this, "CSV 내보내기 실패: " + ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DeleteJob()
    {
        if (_busy || _schedulerRunning)
        {
            MessageBox.Show(this, "자동 작업 중에는 삭제할 수 없습니다. 먼저 작업을 중지하세요.", "물품 삭제");
            return;
        }
        // Commit the actual checkbox values; DataGridView.SelectedRows is only
        // keyboard/row focus and is NOT the '선택' check column.
        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        var marked = _filteredJobs.Where(j => j.SelectedForBulk).ToList();
        // When nothing is checked, preserve the familiar single-row delete.
        var active = _editorTabs.SelectedTab == _autoTab
            ? _autoGrid.CurrentRow?.DataBoundItem as ListingJob
            : _jobsGrid.CurrentRow?.DataBoundItem as ListingJob;
        var targets = marked.Count > 0 ? marked
            : active is not null && _filteredJobs.Contains(active) ? new List<ListingJob> { active }
            : _selected is not null && _filteredJobs.Contains(_selected) ? new List<ListingJob> { _selected }
            : new List<ListingJob>();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "삭제할 물품을 선택하거나 '선택' 열에 체크하세요.", "물품 삭제");
            return;
        }
        string question = marked.Count > 0
            ? $"체크한 {targets.Count}건의 물품 설정을 한 번에 삭제할까요?"
            : "현재 물품 설정 1건을 삭제할까요?";
        if (MessageBox.Show(this, question + "\n\n프로그램 내부 목록에서만 삭제되며, 아이템매니아 사이트의 등록글은 삭제되지 않습니다.",
            "물품 일괄 삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        // Save the selected editor before building the remaining snapshot;
        // commit to disk FIRST so a failed save never removes visible rows.
        SaveSelectedJob(false);
        var ids = new HashSet<string>(targets.Select(j => j.Id), StringComparer.Ordinal);
        var remaining = _jobs.Where(j => !ids.Contains(j.Id)).ToList();
        var original = _config.Jobs;
        try
        {
            _config.Jobs = remaining;
            _config.Save();
        }
        catch (Exception ex)
        {
            _config.Jobs = original;
            MessageBox.Show(this, "삭제 내용을 저장하지 못했습니다. 목록은 그대로 유지됩니다.\n" + ex.Message,
                "물품 삭제 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        foreach (var job in targets) _jobs.Remove(job);
        _selected = null;
        RefreshFilterChoices();
        RefreshFilteredLists();
        AppendLog($"선택 물품 {targets.Count}건 삭제 완료 (프로그램 내부 목록만 변경)");
    }

    private void SelectCurrentJob()
    {
        if (_selectingJob) return;
        var job = _jobsGrid.CurrentRow?.DataBoundItem as ListingJob;
        if (job is null || ReferenceEquals(job, _selected)) return;
        if (_selected is not null) SaveSelectedJob(false);
        _selectingJob = true;
        try
        {
            _selected = job;
            _subId.Text = job.SubId;
            _siteId.Text = job.SiteId;
            _paidRegistration.Checked = job.PaidRegistration;
            SetTradeMode(job.Operation);
            SetJobCatalogSelection(job); _title.Text = job.Title;
            // Never silently turn an unknown/legacy category into '아이템' or '분할'.
            var category = ItemManiaFieldCatalog.CanonicalCategory(job.ItemCategory);
            _itemCategory.SelectedItem = category is not null && _itemCategory.Items.Contains(category) ? category : null;
            _tradingMethod.SelectedItem = _tradingMethod.Items.Contains(job.TradingMethod) ? job.TradingMethod : null;
            _tradingUnit.SelectedItem = "없음";
            _minQuantity.Text = job.MinQuantity; _maxQuantity.Text = job.MaxQuantity;
            _autoDecrease.Checked = job.AutoDecrease;
            _instantTransaction.Checked = job.InstantTransaction;
            _characterClass.Text = job.CharacterClass; _characterLevel.Text = job.CharacterLevel;
            _macro.SelectedItem = _macro.Items.Contains(job.ListingMacro) ? job.ListingMacro : "직접설정";
            _quantity.Text = job.Quantity; _unit.Text = job.Unit; _price.Text = job.Price;
            _character.Text = job.Character; _description.Text = job.Description; _entry.Text = job.EntryUrl;
            _importedListingInfo.Text = job.ImportedFromSite
                ? $"기존글 {job.ListingNumber} · {job.OriginalTradeSide} · 확인 전 자동 OFF"
                : "신규 등록 물품";
            _interval.Value = Math.Clamp(job.IntervalMinutes, 1, 10080); _autoRepost.Checked = job.AutoRepost;
            _jobEnabled.Checked = job.Enabled;
            UpdateContextFields();
        }
        finally { _selectingJob = false; }
    }

    private void ClearEditor()
    {
        foreach (var box in new[] { _title, _quantity, _price, _character, _description, _entry, _subId, _siteId }) box.Clear();
        _paidRegistration.Checked = false;
        _importedListingInfo.Text = "신규 등록 물품";
        _game.SelectedItem = GameServerCatalog.LostArk;
        _server.SelectedIndex = -1;
        SetTradeMode("판매등록");
        _jobEnabled.Checked = false; _autoRepost.Checked = true;
    }

    private void SaveSelectedJob(bool notify = true)
    {
        if (_selected is null || _selectingJob) return;
        var newOperation = _sellRadio.Checked ? "판매등록" : _buyRadio.Checked ? "구매등록" : "재등록";
        if (_selected.ImportedFromSite && newOperation != "재등록")
        {
            // An imported existing post must not become a NEW sale/buy post.
            _selectingJob = true;
            try { SetTradeMode("재등록"); }
            finally { _selectingJob = false; }
            if (notify) MessageBox.Show(this,
                "기존 등록글은 재등록만 할 수 있습니다. 신규 판매/구매 글을 만들려면 새 물품을 추가하세요.");
            return;
        }
        // A previously submitted listing cannot silently become a buy/sell job.
        if (_selected.Operation != newOperation && (_selected.InitialCompleted || _selected.LastSucceededUtc.HasValue))
        {
            _selected.InitialCompleted = false;
            _selected.RequiresReview = true;
            _selected.Enabled = false;
            _selected.LastStatus = "거래구분 변경: 등록 내역 확인 필요";
        }
        _selected.Operation = newOperation;
        _selected.SubId = _subId.Text.Trim();
        _selected.SiteId = _siteId.Text.Trim();
        _selected.PaidRegistration = _paidRegistration.Checked;
        // Invalid legacy data remains pending manual correction; never auto-convert.
        string? chosenCategory = _itemCategory.SelectedItem?.ToString();
        string? chosenMethod = _tradingMethod.SelectedItem?.ToString();
        // Existing account listings are being REPOSTED, not created anew.
        // Their old category/trade method might not appear on a MyRoom list;
        // never force a guessed value or overwrite an existing listing.
        bool repostOnly = newOperation == "재등록";
        if (!repostOnly && (!ItemManiaFieldCatalog.IsCategory(chosenCategory) || !ItemManiaFieldCatalog.IsTradingMethod(chosenMethod)))
        {
            _selected.Enabled = false;
            _selected.RequiresReview = true;
            _selected.LastStatus = "미확인 분류: 사이트 기준으로 다시 선택 필요";
            _config.Jobs = _jobs.ToList(); _config.Save();
            _jobsGrid.Refresh();
            if (notify) MessageBox.Show(this, "물품구분과 거래유형을 확인된 목록에서 모두 선택하세요. 미확인 분류는 자동 등록하지 않습니다.");
            return;
        }
        if (ItemManiaFieldCatalog.IsCategory(chosenCategory)) _selected.ItemCategory = chosenCategory!;
        if (ItemManiaFieldCatalog.IsTradingMethod(chosenMethod)) _selected.TradingMethod = chosenMethod!;
        // Only clear the category-specific review that this editor itself created.
        // Uncertain submissions, recovery, or other review statuses must stay blocked.
        if (_selected.RequiresReview && _selected.LastStatus == "미확인 분류: 사이트 기준으로 다시 선택 필요")
        {
            _selected.RequiresReview = false;
            _selected.LastStatus = "분류 수정됨 (자동 실행 꺼짐)";
        }
        _selected.TradingUnit = "없음"; // kept only for legacy JSON compatibility, never guessed

        _selected.MinQuantity = _minQuantity.Text.Trim();
        _selected.MaxQuantity = _maxQuantity.Text.Trim();
        _selected.AutoDecrease = _autoDecrease.Checked;
        _selected.InstantTransaction = _instantTransaction.Checked;
        _selected.CharacterClass = _characterClass.Text.Trim();
        _selected.CharacterLevel = _characterLevel.Text.Trim();
        _selected.ListingMacro = _macro.SelectedItem?.ToString() ?? "직접설정";
        // A site import can populate the model while the editor's combo boxes
        // still have no selected item. Saving/switching tabs must not erase the
        // imported identity or overwrite a valid listing with empty strings.
        _selected.Game = KeepExistingWhenEmpty(_game.SelectedItem?.ToString(), _selected.Game);
        _selected.Server = KeepExistingWhenEmpty(_server.SelectedItem?.ToString(), _selected.Server);
        _selected.Title = KeepExistingWhenEmpty(_title.Text, _selected.Title);
        _selected.Quantity = KeepExistingWhenEmpty(_quantity.Text, _selected.Quantity);
        _selected.Unit = KeepExistingWhenEmpty(_unit.Text, _selected.Unit);
        _selected.Price = KeepExistingWhenEmpty(_price.Text, _selected.Price);
        _selected.Character = KeepExistingWhenEmpty(_character.Text, _selected.Character);
        _selected.Description = KeepExistingWhenEmpty(_description.Text, _selected.Description);
        _selected.EntryUrl = KeepExistingWhenEmpty(_entry.Text, _selected.EntryUrl);
        _selected.IntervalMinutes = (int)_interval.Value;
        _selected.AutoRepost = _autoRepost.Checked;
        _selected.Enabled = _jobEnabled.Checked && !_selected.RequiresReview;
        _config.Jobs = _jobs.ToList(); _config.Save(); _jobsGrid.Refresh();
        if (notify) AppendLog("물품 설정 저장: " + _selected.Title);
    }

    private static string KeepExistingWhenEmpty(string? editedValue, string? savedValue) =>
        string.IsNullOrWhiteSpace(editedValue) ? savedValue ?? "" : editedValue.Trim();

    private void LoadWorkflow()
    {
        if (_selectingFlow || _flowMode.SelectedItem is null) return;
        _selectingFlow = true;
        try
        {
            var w = _config.WorkflowFor(_flowMode.SelectedItem.ToString()!);
            _flowUrl.Text = w.EntryUrl;
            _successSelector.Text = w.SuccessSelector;
            _successUrlPart.Text = w.SuccessUrlContains;
            _repostLinkSelector.Text = w.RepostLinkSelector;
            _steps.Clear();
            foreach (var step in w.Steps) _steps.Add(new AutomationStep { Action = step.Action, Selector = step.Selector, Value = step.Value, TimeoutSeconds = step.TimeoutSeconds });
        }
        finally { _selectingFlow = false; }
    }

    private void SaveWorkflow()
    {
        if (_flowMode.SelectedItem is null) return;
        _stepsGrid.EndEdit();
        var w = _config.WorkflowFor(_flowMode.SelectedItem.ToString()!);
        w.EntryUrl = _flowUrl.Text.Trim();
        w.SuccessSelector = _successSelector.Text.Trim(); w.SuccessUrlContains = _successUrlPart.Text.Trim();
        w.RepostLinkSelector = _repostLinkSelector.Text.Trim();
        w.Steps = _steps.Select(x => new AutomationStep { Action = x.Action ?? "", Selector = x.Selector?.Trim() ?? "", Value = x.Value ?? "", TimeoutSeconds = Math.Clamp(x.TimeoutSeconds, 0, 120) }).ToList();
        _config.Save();
        AppendLog("작업 단계 저장: " + w.Operation + " (" + w.Steps.Count + "단계)");
    }

    private void AddStep()
    {
        _stepsGrid.EndEdit();
        int idx = _stepsGrid.CurrentRow?.Index ?? _steps.Count - 1;
        _steps.Insert(Math.Clamp(idx + 1, 0, _steps.Count), new AutomationStep { Action = "클릭" });
    }
    private void DeleteStep() { if (_stepsGrid.CurrentRow is { Index: var i } && i >= 0 && i < _steps.Count) _steps.RemoveAt(i); }
    private void MoveStep(int offset)
    {
        _stepsGrid.EndEdit();
        int i = _stepsGrid.CurrentRow?.Index ?? -1;
        int j = i + offset;
        if (i < 0 || j < 0 || j >= _steps.Count) return;
        var step = _steps[i]; _steps.RemoveAt(i); _steps.Insert(j, step);
        _stepsGrid.CurrentCell = _stepsGrid.Rows[j].Cells[0];
    }

    private void SaveAll()
    {
        SaveSelectedJob(false);
        if (_flowMode.SelectedItem is not null) SaveWorkflow();
        _config.MaxSubmissionsPerHour = (int)_hourLimit.Value;
        // Keep the separate per-server listing-count safeguard, but use the
        // same user-selected number as the hourly ID/server attempt limit.
        _config.MaxListingsPerServer = (int)_hourLimit.Value;
        // ItemMania's same-server minimum interval is fixed by this app.
        _config.MinimumServerSeconds = 21;
        // Legacy global pacing is no longer used: different servers can follow
        // immediately, while each ID/server retains its own 21-second floor.
        _config.MinimumGlobalSeconds = 21;
        _config.AllowedHost = "www.itemmania.com";
        _config.Jobs = _jobs.ToList();
        _config.Save();
        AppendLog("전체 설정 저장 완료");
    }

    private void OpenBrowserUrl()
    {
        if (_web.CoreWebView2 is null) { AppendLog("내부 사이트 엔진 초기화 중입니다."); return; }
        string value = _browserUrl.Text.Trim();
        if (!value.Contains("://")) value = "https://" + value;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) _web.CoreWebView2.Navigate(uri.AbsoluteUri);
        else MessageBox.Show(this, "HTTP/HTTPS 주소만 사용할 수 있습니다.");
    }

    private async Task StartSelectorPickerAsync(string target = "step")
    {
        if (_web.CoreWebView2 is null) { AppendLog("브라우저 준비가 필요합니다."); return; }
        if (_editorTabs.SelectedTab != _workflowTab) _editorTabs.SelectedTab = _workflowTab;
        if (target == "step" && _stepsGrid.CurrentRow is null) { AppendLog("작업 단계에서 선택할 행을 지정하세요."); return; }
        if (!IsAllowedCurrentPage()) { AppendLog("허용 호스트의 페이지에서만 요소를 선택할 수 있습니다."); return; }
        _pickerMode = target;
        const string pickerJs = """
        (() => {
          const old=document.getElementById('__luzen_selector_style'); if(old)old.remove();
          const st=document.createElement('style');st.id='__luzen_selector_style';
          st.textContent='.__luzen_pick_target{outline:3px solid #fd7e14!important;cursor:crosshair!important}';document.head.appendChild(st);
          let hovered=null;
          function cssPath(el){
            if(el.id){const id='#'+CSS.escape(el.id);if(document.querySelectorAll(id).length===1)return id;}
            const parts=[];
            for(let e=el;e&&e.nodeType===1&&parts.length<8;e=e.parentElement){
              let p=e.tagName.toLowerCase();
              if(e.id){p+='#'+CSS.escape(e.id);parts.unshift(p);break;}
              if(e.getAttribute('name')){p+='[name="'+CSS.escape(e.getAttribute('name'))+'"]';}
              else if(e.parentElement){let k=1,s=e;while((s=s.previousElementSibling)!=null)if(s.tagName===e.tagName)k++;p+=':nth-of-type('+k+')';}
              parts.unshift(p);
            }
            return parts.join(' > ');
          }
          function move(ev){if(hovered)hovered.classList.remove('__luzen_pick_target');hovered=ev.target;hovered.classList.add('__luzen_pick_target');}
          function click(ev){ev.preventDefault();ev.stopPropagation();ev.stopImmediatePropagation();
            document.removeEventListener('mouseover',move,true);document.removeEventListener('click',click,true);
            if(hovered)hovered.classList.remove('__luzen_pick_target');st.remove();
            const el=ev.target;
            chrome.webview.postMessage({kind:'luzen_selector',selector:cssPath(el),tag:el.tagName.toLowerCase(),type:el.getAttribute('type')||''});
          }
          document.addEventListener('mouseover',move,true);document.addEventListener('click',click,true);
        })();
        """;
        try { await _web.CoreWebView2.ExecuteScriptAsync(pickerJs); AppendLog("선택 모드: 오른쪽 사이트에서 입력칸이나 버튼을 한 번 클릭하세요. (iframe 내부는 선택 불가)"); }
        catch (Exception ex) { AppendLog("선택 모드 실패: " + ex.Message); }
    }

    // Best-effort form inspection: discovers ordinary visible input fields on the
    // CURRENT page. Does not collect passwords, field contents or submit forms.
    // Game/server autocomplete and iframe widgets must still be configured by hand.
    private async Task DiscoverFormFieldsAsync()
    {
        if (_busy) { MessageBox.Show(this, "작업 실행 중에는 탐색하지 않습니다."); return; }
        if (_web.CoreWebView2 is null || !IsAllowedCurrentPage())
        { MessageBox.Show(this, "내부 사이트에 아이템매니아 등록 페이지를 먼저 열어 주세요."); return; }
        const string script = """
        (() => {
          function path(el){
            if(el.id){const s='#'+CSS.escape(el.id);if(document.querySelectorAll(s).length===1)return s;}
            if(el.name){const s=el.tagName.toLowerCase()+'[name="'+CSS.escape(el.name)+'"]';if(document.querySelectorAll(s).length===1)return s;}
            const parts=[];
            for(let e=el;e&&e.nodeType===1&&parts.length<9;e=e.parentElement){
              let p=e.tagName.toLowerCase();
              if(e.id){p+='#'+CSS.escape(e.id);parts.unshift(p);break;}
              if(e.parentElement){let n=1,t=e;while((t=t.previousElementSibling))if(t.tagName===e.tagName)n++;p+=':nth-of-type('+n+')';}
              parts.unshift(p);
            }
            return parts.join(' > ');
          }
          return Array.from(document.querySelectorAll('input, textarea, select'))
            .filter(el => !['password','hidden','file','email','tel','search'].includes((el.type||'').toLowerCase())
                && !el.disabled && el.getClientRects().length && getComputedStyle(el).visibility!=='hidden')
            .slice(0,120).map(el=>({
              selector:path(el),tag:el.tagName.toLowerCase(),type:(el.type||'').toLowerCase(),
              name:el.getAttribute('name')||'',id:el.id||'',
              placeholder:el.getAttribute('placeholder')||'',
              label:(Array.from(el.labels||[]).map(n=>n.innerText||n.textContent||'').join(' ')||
                el.getAttribute('aria-label')||'').trim().slice(0,120)
            }));
        })()
        """;
        try
        {
            _stepsGrid.EndEdit();
            string json = await _web.CoreWebView2.ExecuteScriptAsync(script);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("입력칸 목록을 가져오지 못했습니다.");
            var fields = doc.RootElement.EnumerateArray().Select(e => new
            {
                Selector = e.GetProperty("selector").GetString() ?? "",
                Tag = e.GetProperty("tag").GetString() ?? "",
                Type = e.GetProperty("type").GetString() ?? "",
                Text = string.Join(" ", new[] { "label", "placeholder", "name", "id" }.Select(k => e.GetProperty(k).GetString() ?? ""))
            }).ToList();
            var patterns = new (string Variable, string[] Keywords)[]
            {
                ("{Title}", new[] { "상품제목", "물품제목", "판매제목", "구매제목" }),
                ("{Quantity}", new[] { "판매수량", "구매수량", "상품수량", "수량" }),
                ("{Price}", new[] { "판매금액", "구매금액", "거래금액", "판매가격", "구매가격" }),
                ("{Unit}", new[] { "수량단위", "단위" }),
                ("{Character}", new[] { "캐릭터명", "캐릭터이름", "캐릭터" }),
                ("{Description}", new[] { "상세설명", "상품설명", "물품설명" })
            };
            var added = new List<string>();
            foreach (var mapping in patterns)
            {
                var matches = fields.Where(f => f.Tag is "input" or "textarea" && f.Type is not ("checkbox" or "radio" or "button" or "submit" or "reset") &&
                    mapping.Keywords.Any(k => f.Text.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matches.Count != 1) continue; // ambiguity: do not guess
                var field = matches[0];
                if (string.IsNullOrWhiteSpace(field.Selector)) continue;
                var step = _steps.FirstOrDefault(s => s.Action == "입력" && s.Value == mapping.Variable && string.IsNullOrWhiteSpace(s.Selector));
                if (step is not null) step.Selector = field.Selector;
                else if (!_steps.Any(s => s.Action == "입력" && s.Value == mapping.Variable))
                {
                    var newStep = new AutomationStep { Action = "입력", Selector = field.Selector, Value = mapping.Variable };
                    int submitIndex = _steps.ToList().FindIndex(s => s.Action == "제출");
                    _steps.Insert(submitIndex < 0 ? _steps.Count : submitIndex, newStep);
                }
                else continue;
                added.Add(mapping.Variable + " → " + field.Selector);
            }
            _stepsGrid.Refresh();
            if (added.Count > 0) SaveWorkflow();
            string message = added.Count == 0 ? "확신할 수 있는 입력칸을 찾지 못했습니다. 요소 선택 기능으로 직접 연결하세요." :
                "자동으로 연결한 입력칸:\n" + string.Join("\n", added) + "\n\n자동 시작 전 등록할 물품과 금액을 확인해 주세요.";
            message += "\n게임/서버 표시는 목록 선택으로 바뀌었습니다. 사이트 옵션 연결과 성공 판정은 별도 검증이 필요합니다.";
            MessageBox.Show(this, message, "현재 페이지 입력칸 탐색", MessageBoxButtons.OK, MessageBoxIcon.Information);
            AppendLog($"현재 페이지 입력칸 탐색: 후보 {fields.Count}개, 자동 연결 {added.Count}개");
        }
        catch (Exception ex) { MessageBox.Show(this, "입력칸 탐색 실패: " + ex.Message); }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (root.GetProperty("kind").GetString() != "luzen_selector" || _pickerMode is not ("step" or "success")) return;
            if (!IsAllowedUrl(e.Source)) return;
            string target = _pickerMode;
            _pickerMode = "";
            string css = root.GetProperty("selector").GetString() ?? "";
            if (target == "success")
            {
                _successSelector.Text = css;
                SaveWorkflow();
                AppendLog("완료 표시 CSS 저장: " + css);
                return;
            }
            if (_stepsGrid.CurrentRow?.DataBoundItem is AutomationStep step)
            {
                step.Selector = css;
                string tag = root.GetProperty("tag").GetString() ?? "";
                if (tag is "input" or "textarea") step.Action = "입력";
                else if (tag == "select") step.Action = "선택";
                else if (step.Action == "입력") step.Action = "클릭";
                _stepsGrid.Refresh();
                AppendLog("선택자 입력 완료: " + css);
            }
        }
        catch (Exception ex) { AppendLog("선택자 메시지 처리 오류: " + ex.Message); }
    }

    private bool IsAllowedCurrentPage()
    {
        try { return _web.Source is Uri uri && IsAllowedUrl(uri.AbsoluteUri); }
        catch { return false; }
    }

    private bool IsAllowedUrl(string input)
    {
        var host = _config.AllowedHost.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host) || host.Contains('/') || host.Contains(':')) return false;
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
             (host == "www.itemmania.com" &&
               (uri.Host.Equals("itemmania.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("trade.itemmania.com", StringComparison.OrdinalIgnoreCase))));
    }

    private string Expand(string text, ListingJob job, AutomationWorkflow flow)
    {
        string entry = EntryFor(job, flow);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Game"] = job.Game, ["Server"] = job.Server, ["Title"] = job.Title,
            ["Quantity"] = job.Quantity, ["Unit"] = job.Unit, ["Price"] = job.Price,
            ["Character"] = job.Character, ["Description"] = job.Description,
            ["ItemCategory"] = job.ItemCategory, ["TradingMethod"] = job.TradingMethod,
            ["TradingUnit"] = job.TradingUnit, ["MinQuantity"] = job.MinQuantity,
            ["MaxQuantity"] = job.MaxQuantity, ["CharacterClass"] = job.CharacterClass,
            ["CharacterLevel"] = job.CharacterLevel, ["AutoDecrease"] = job.AutoDecrease ? "Y" : "N",
            ["InstantTransaction"] = job.InstantTransaction ? "Y" : "N",
            ["EntryUrl"] = entry
        };
        foreach (var entryPair in values) text = text.Replace("{" + entryPair.Key + "}", entryPair.Value, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static string EffectiveOperation(ListingJob job) =>
        job.InitialCompleted && job.AutoRepost ? "재등록" : job.Operation;

    private static bool WantsWork(ListingJob job) =>
        job.Enabled && !job.RequiresReview && (!job.InitialCompleted || job.Operation == "재등록" || job.AutoRepost);

    // Repost through the authenticated OWN registration-list page whenever
    // the listing ID and original buy/sell direction are known.
    private static string OwnedListUrlFor(ListingJob job)
    {
        if (!job.ImportedFromSite && !job.InitialCompleted) return "";
        if (!ListingIdentity.HasNumber(job.ListingNumber)) return "";
        bool selling = job.OriginalTradeSide == "판매" ||
            (string.IsNullOrWhiteSpace(job.OriginalTradeSide) && job.Operation == "판매등록");
        bool buying = job.OriginalTradeSide == "구매" ||
            (string.IsNullOrWhiteSpace(job.OriginalTradeSide) && job.Operation == "구매등록");
        if (!selling && !buying) return "";
        if (MyListingsImport.IsOwnedListUrl(job.SourceListUrl) &&
            MyListingsImport.IsSellListUrl(job.SourceListUrl) == selling)
            return job.SourceListUrl.Trim();
        return selling ? MyListingsImport.SellListingsUrl : MyListingsImport.BuyListingsUrl;
    }

    private static string EntryFor(ListingJob job, AutomationWorkflow flow) =>
        flow.Operation != "재등록" ? flow.EntryUrl :
        !string.IsNullOrWhiteSpace(OwnedListUrlFor(job)) ? OwnedListUrlFor(job) :
        MyListingsImport.IsSafeDetailUrl(job.EntryUrl, job.ListingNumber) ? job.EntryUrl.Trim() :
        (job.EntryUrl ?? "").Trim();

    private bool ValidateJob(ListingJob job, bool dryRun, out string message, bool importedReviewConfirmed = false)
    {
        var effective = EffectiveOperation(job);
        var flow = _config.WorkflowFor(effective);
        if (effective == "재등록" && string.IsNullOrWhiteSpace(EntryFor(job, flow)))
        { message = "재등록 대상 상세 URL과 내 등록글 목록을 모두 확인할 수 없습니다. 해당 글을 다시 불러오세요."; return false; }
        if (effective == "재등록" && !ListingIdentity.HasNumber(job.ListingNumber))
        { message = "재등록은 고유 등록번호가 확인된 물품만 가능합니다. 내 등록글 불러오기로 등록번호를 연결하세요."; return false; }
        if (string.IsNullOrWhiteSpace(job.Title) || string.IsNullOrWhiteSpace(job.Server)) { message = "제목을 입력하고 서버를 목록에서 선택하세요."; return false; }
        if (!GameServerCatalog.IsSupported(job.Game, job.Server))
        { message = $"지원하지 않는 게임/서버입니다: {job.Game} / {job.Server}.\n목록에서 다시 선택하세요."; return false; }
        if (job.Operation is not ("판매등록" or "구매등록" or "재등록")) { message = "지원하지 않는 작업 유형입니다."; return false; }
        // Reposting an existing listing uses its management URL, NOT the new
        // listing form. The old category/trading type may be absent from the
        // account list; do not invent values just to allow reposting.
        if (effective != "재등록" && !ItemManiaFieldCatalog.IsCategory(job.ItemCategory))
        { message = "아이템매니아에서 확인된 물품구분(머니/아이템/계정/기타)만 사용할 수 있습니다."; return false; }
        if (effective != "재등록" && !ItemManiaFieldCatalog.IsTradingMethod(job.TradingMethod))
        { message = "거래유형은 확인된 일반 또는 분할 중 하나여야 합니다. 과거 '흥정' 데이터는 다시 선택하세요."; return false; }
        if (!dryRun && job.PaidRegistration)
        { message = "유료등록 결제 설정은 아직 실제 사이트와 연결이 확인되지 않아 자동 제출할 수 없습니다. 일반등록으로 전환하거나 별도로 검증해야 합니다."; return false; }
        if (!dryRun && job.InstantTransaction)
        { message = "즉시구매는 실사이트 인증 및 설정 연동이 확인되지 않아 자동 제출할 수 없습니다."; return false; }
        if ((job.Operation is "판매등록" or "구매등록") &&
            (!int.TryParse(job.Price.Replace(",", ""), out int amount) || amount <= 0))
        { message = "등록할 금액을 숫자로 입력하세요."; return false; }
        if ((job.Operation is "판매등록" or "구매등록") && !int.TryParse(job.Quantity.Replace(",", ""), out int q))
        { message = "수량은 숫자로 입력하세요."; return false; }
        if (job.Operation is "판매등록" or "구매등록")
        {
            if (int.TryParse(job.Quantity.Replace(",", ""), out var quantity) && quantity < 1)
            { message = "수량은 1 이상이어야 합니다."; return false; }
            if (job.TradingMethod == "분할" &&
                (!int.TryParse(job.MinQuantity.Replace(",", ""), out int min) || min < 1 ||
                 !int.TryParse(job.MaxQuantity.Replace(",", ""), out int max) || max < min))
            { message = "분할 거래의 최소 수량과 최대 수량을 확인하세요."; return false; }
        }
        if (job.IntervalMinutes < 1) { message = "재등록 주기는 1분 이상이어야 합니다."; return false; }
        string url = EntryFor(job, flow);
        if (!IsAllowedUrl(url)) { message = "진입 URL은 허용된 사이트의 HTTPS 주소여야 합니다."; return false; }
        if (flow.Steps.Count == 0) { message = "작업 단계가 없습니다."; return false; }
        // Removed: earlier versions invented non-site item-info categories. Legacy
        // JSON fields are kept for backwards compatibility, but are never submitted.
        // Require explicit workflow cleanup rather than posting literal placeholders.
        if (flow.Steps.Any(step => (step.Value ?? "").Contains("{ItemInfoGroup}", StringComparison.OrdinalIgnoreCase) ||
                                   (step.Value ?? "").Contains("{ItemInfoDetail}", StringComparison.OrdinalIgnoreCase)) ||
            (flow.EntryUrl ?? "").Contains("{ItemInfoGroup}", StringComparison.OrdinalIgnoreCase) ||
            (flow.EntryUrl ?? "").Contains("{ItemInfoDetail}", StringComparison.OrdinalIgnoreCase))
        { message = "이전 버전의 임의 아이템정보 변수를 참조하는 작업 단계가 있습니다. 고급 설정에서 제거하세요."; return false; }
        if (!dryRun && job.RequiresReview && !importedReviewConfirmed)
        {
            message = job.ImportedFromSite
                ? "불러온 등록글의 현재 상태를 확인해야 합니다. 물품등록 · 편집 화면에서 '사이트 확인 후 보류 해제'를 이용하세요."
                : "이전 제출의 성공 여부가 불명확합니다. 사이트에서 물품이 등록됐는지 확인하고 보류를 해제하세요.";
            return false;
        }
        if (!dryRun)
        {
            if (!flow.Steps.Any(s => s.Action == "제출" && !string.IsNullOrWhiteSpace(s.Selector))) { message = "제출 버튼의 CSS 선택자를 설정하세요."; return false; }
            if (flow.Steps.Any(s => (s.Action is "입력" or "클릭" or "선택" or "체크" or "대기") && string.IsNullOrWhiteSpace(s.Selector)))
            { message = "자동 탐색(@auto:) 또는 사이트 CSS 선택자가 비어 있는 단계가 있습니다."; return false; }
            if (string.IsNullOrWhiteSpace(flow.SuccessSelector) && string.IsNullOrWhiteSpace(flow.SuccessUrlContains)) { message = "실제 등록 성공 여부를 확인할 성공 CSS 또는 URL 단서를 설정하세요."; return false; }
            if (string.IsNullOrWhiteSpace(flow.SuccessSelector) && !string.IsNullOrWhiteSpace(flow.SuccessUrlContains) && url.Contains(flow.SuccessUrlContains, StringComparison.OrdinalIgnoreCase))
            { message = "성공 URL 단서가 제출 전 페이지 URL에도 들어 있습니다. 완료 페이지만 구분되는 값을 설정하세요."; return false; }
            if (job.Operation != "재등록" && !job.InitialCompleted)
            {
                int others = _jobs.Count(x => x.Id != job.Id && x.Operation != "재등록" && x.Server.Equals(job.Server, StringComparison.OrdinalIgnoreCase) && x.Enabled);
                if (others >= _config.MaxListingsPerServer) { message = $"{job.Server} 서버의 작업 수가 제한 {_config.MaxListingsPerServer}개에 도달했습니다."; return false; }
            }
        }
        message = ""; return true;
    }

    // Read-only: checks one row's number, title and unique repost control.
    // It never submits, never removes a review hold, and never starts scheduling.
    private async Task ProbeSelectedRepostReadOnlyAsync()
    {
        if (_schedulerRunning || _busy || _myListingsImportRunning)
        {
            MessageBox.Show(this, "자동 작업을 중지한 후 조회시험을 실행하세요.", "조회시험 대기");
            return;
        }
        var target = _selected;
        if (target is null || EffectiveOperation(target) != "재등록" ||
            !MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(target)))
        {
            MessageBox.Show(this, "재등록 물품 1개를 먼저 선택해 주세요. 확인 가능한 내 등록목록 주소가 필요합니다.", "조회시험 대상 선택");
            return;
        }
        if ((_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system))
        {
            MessageBox.Show(this, "해당 시스템의 별도 IP 연결 확인이 필요합니다.", "연결 확인 필요");
            return;
        }
        _busy = true;
        _state.Text = "선택 1건 조회시험 중 (미제출)";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        AppendLog("[미제출 조회시험 시작] " + target.Server + " / " + target.ListingNumber);
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(timeout.Token);
            var result = await LocateOwnedListFallbackAsync(target, timeout.Token);
            string summary = result.Status == "ready"
                ? "등록번호·제목·재등록 버튼 1개를 확인했습니다. 실제 제출하지 않았습니다."
                : "조회시험 실패: " + result.Message + ". 실제 제출하지 않았습니다.";
            AppendLog("[미제출 조회시험] " + target.Server + " / " + target.ListingNumber +
                " / 상태 " + result.Status + " / " + summary);
            MessageBox.Show(this, summary, "미제출 조회시험 결과");
        }
        catch (OperationCanceledException)
        {
            AppendLog("[미제출 조회시험] 90초 제한으로 검사 종료. 제출 없음.");
            MessageBox.Show(this, "90초 안에 검사를 완료하지 못했습니다. 실제 제출은 없었습니다.", "조회시험 시간 초과");
        }
        catch (Exception ex)
        {
            AppendLog("[미제출 조회시험] " + ex.GetType().Name + ": " + ex.Message + " / 제출 없음");
            MessageBox.Show(this, "조회시험 오류: " + ex.Message + "\n실제 제출은 없었습니다.", "조회시험 오류");
        }
        finally
        {
            _busy = false;
            _state.Text = "중지/대기";
        }
    }

    private async Task RunSelectedAsync(bool dryRun)
    {
        // A read-only My Room scan uses the same WebView2 as registration.
        // Explicit manual execution takes priority: cancel the scan and wait
        // until its navigation handlers have fully released the browser.
        if (_myListingsImportRunning)
        {
            if (!await CancelImportForManualRunAsync()) return;
        }
        _pendingMyListingsImport = false;
        if (_busy)
        {
            MessageBox.Show(this, "실제 등록/재등록 작업 1건이 이미 진행 중입니다. 완료되거나 중지된 후 다시 실행하세요.", "작업 진행 중");
            return;
        }
        SaveAll();
        if (_selected is null) return;
        // Imported listings were all marked RequiresReview even when they had
        // never been submitted by this program. Ask for an explicit, per-item
        // acknowledgement; NEVER auto-clear an ambiguous previous submission.
        bool importedReviewConfirmed = !dryRun && _selected.RequiresReview &&
            CanConfirmImportedReviewForSingleRun(_selected);
        if (importedReviewConfirmed && !ConfirmImportedReviewForSingleRun(_selected)) return;
        if (!dryRun && !importedReviewConfirmed && MessageBox.Show(this,
            $"사이트에 물품을 실제로 제출합니다.\n\n구분: {_selected.Operation}\n서버: {_selected.Server}\n제목: {_selected.Title}\n금액: {_selected.Price}원\n\n진행할까요? (사이트 이용약관, 등록료 및 제한 확인 필요)",
            "실제 물품 등록 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (!ValidateJob(_selected, dryRun, out var message, importedReviewConfirmed)) { MessageBox.Show(this, message, "작업 설정 확인"); return; }
        if (!dryRun && !CanRunNow(_selected, out var delayMessage)) { MessageBox.Show(this, delayMessage, "등록 간격 제한"); return; }
        if (importedReviewConfirmed)
        {
            _selected.RequiresReview = false;
            // One-off execution must not silently start unattended reposting.
            _selected.Enabled = false;
            _jobEnabled.Checked = false;
            _selected.LastStatus = "본인 등록글 확인 후 1건 실행 준비";
            _config.Jobs = _jobs.ToList();
            _config.Save();
            _jobsGrid.Refresh();
            RefreshAutoSummary();
            AppendLog("선택한 기존 등록글 1건의 사이트 확인을 기록했습니다. 자동 반복은 꺼진 상태로 1건만 실행합니다.");
        }
        await ExecuteJobAsync(_selected, dryRun);
    }

    private void StartScheduler()
    {
        if (_sitePriceBatchRunning)
        {
            MessageBox.Show(this, "선택 물품 가격을 순서대로 반영하고 있습니다. 완료 또는 [즉시 중지] 후 자동 시작하세요.", "가격 반영 중");
            return;
        }
        if ((_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system))
        {
            MessageBox.Show(this, $"시스템 {_system} 연결 설정에서 독립적인 외부 IP를 먼저 확인하세요. 확인 전에는 {_system}가 현재 IP로 등록하지 않습니다.",
                $"{_system} 연결 확인 필요");
            return;
        }
        SaveAll();
        if (_schedulerRunning) return;
        if (_busy || _pendingMyListingsImport)
        {
            MessageBox.Show(this, "현재 작업 또는 기존 등록글 불러오기가 완료된 후 자동 시작할 수 있습니다.", "자동등록 대기");
            return;
        }
        var enabled = _jobs.Where(WantsWork).ToList();
        bool canCheckHeld = _jobs.Any(j => j.RequiresReview && j.PendingRepostAttemptUtc.HasValue &&
            !string.IsNullOrWhiteSpace(j.PendingRepostBeforeStamp) &&
            ListingIdentity.HasNumber(j.ListingNumber) && MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(j)));
        if (enabled.Count == 0 && !canCheckHeld)
        { MessageBox.Show(this, "자동 실행할 물품이나 사이트에서 재검증할 수 있는 보류 물품이 없습니다."); return; }
        foreach (var job in enabled)
            if (!ValidateJob(job, false, out var error)) { MessageBox.Show(this, job.Title + ": " + error, "자동 시작 불가"); return; }
        _schedulerRunning = true; _timer.Start();
        _nextPendingReviewUtc = DateTime.UtcNow.AddSeconds(15);
        // Old builds scheduled each item at a fixed 30-minute interval. Hourly
        // per-server allocation now controls the pace; remove old future times.
        foreach (var job in enabled)
            if (job.NextDueUtc != DateTime.MaxValue) job.NextDueUtc = DateTime.UtcNow;
        _config.Save();
        _state.Text = "자동 실행 중"; _state.ForeColor = Color.ForestGreen; RefreshAutoSummary();
        AppendLog("스케줄러 시작: " + enabled.Count + "건, ID·서버별 시간당 " + _config.MaxSubmissionsPerHour + "회 제한" +
            (canCheckHeld ? " · 보류 물품은 별도 안전 조회" : ""));
    }

    private void PauseScheduler()
    {
        _schedulerRunning = false; _timer.Stop();
        _pendingReviewRun?.Cancel();
        _state.Text = "일시정지"; _state.ForeColor = Color.DarkGoldenrod; RefreshAutoSummary();
        AppendLog(_myListingsImportRunning ? "자동등록은 일시정지했습니다. '내 등록글 불러오기'는 계속 조회 중입니다. 중단하려면 [즉시 중지]를 누르세요."
            : _busy ? "일시정지. 진행 중인 작업 1건은 종료까지 기다립니다. 목록 불러오기를 누르면 종료 후 자동 조회합니다."
            : "일시정지. 진행 중인 작업은 없습니다. 기존 등록글을 불러올 수 있습니다.");
    }

    private void StopScheduler()
    {
        _sitePriceBatchCancellation?.Cancel();
        _schedulerRunning = false; _timer.Stop();
        _pendingReviewRun?.Cancel();
        if (!_submissionVerificationPending) _currentRun?.Cancel();
        _pendingMyListingsImport = false;
        if (_myListingsImportRunning)
        {
            _myListingsImportCancellation?.Cancel();
            try { _web.CoreWebView2?.Stop(); }
            catch (Exception ex) { AppendLog("조회 페이지 중단 대기: " + ex.GetType().Name); }
            AppendLog("내 등록글 조회 취소 요청: 현재 페이지 이동이 끝나는 대로 중단합니다.");
        }
        _state.Text = _submissionVerificationPending ? "중지 요청 · 마지막 제출 결과 확인 중" : "중지됨";
        _state.ForeColor = Color.Maroon; RefreshAutoSummary();
        AppendLog(_submissionVerificationPending
            ? "새 작업은 중지했습니다. 방금 제출한 1건은 등록번호·등록시각 검증을 마친 후 중지합니다."
            : "중지 요청. 실제 제출 중이었다면 사이트에서 등록 여부를 확인하세요.");
    }

    private bool CanRunNow(ListingJob job, out string reason)
    {
        var now = DateTime.UtcNow;
        var counter = HourlyServerQuota.Current(_config.ServerHourlyCounters, job, now);
        if (HourlyServerQuota.Remaining(counter, _config.MaxSubmissionsPerHour) <= 0)
        {
            reason = $"{job.Server} 서버의 이번 시간 등록 가능 횟수를 모두 사용했습니다."; return false;
        }
        string key = HourlyServerQuota.Key(job);
        // Preserve the previous build's server-only timestamp on upgrade.
        DateTime lastServer = _config.LastServerAttemptUtc.TryGetValue(key, out var recent) ? recent :
            _config.LastServerAttemptUtc.TryGetValue(job.Server, out var legacy) ? legacy : DateTime.MinValue;
        DateTime allowedAt = HourlyServerQuota.NextAllowedUtc(lastServer, now, counter,
            _config.MaxSubmissionsPerHour, _config.MinimumServerSeconds);
        if (now < allowedAt)
        {
            reason = $"{job.Server} 서버 배분 간격: {Math.Ceiling((allowedAt - now).TotalSeconds)}초 남음"; return false;
        }
        reason = ""; return true;
    }

    private async Task SchedulerTickAsync()
    {
        if (_busy || !_schedulerRunning) return;
        if (DateTime.UtcNow < _loginRecoveryPauseUntilUtc) return;
        if (_system == "B" && _bConnectionRecoveryUtc != DateTime.MinValue)
        {
            if (_bConnectionRecheckRunning || DateTime.UtcNow < _bConnectionRecoveryUtc) return;
            _bConnectionRecheckRunning = true;
            // Hold B while an external IP check is in flight. A is independent.
            _bConnectionRecoveryUtc = DateTime.UtcNow.AddMinutes(2);
            try
            {
                await DualSystemNetwork.VerifyBRouteAsync();
                _bConnectionRecoveryUtc = DateTime.MinValue;
                AppendLog("B VPN 독립 외부 IP 재확인 완료. 관리목록 재접속은 다음 작업 순번에 재시도합니다.");
            }
            catch (Exception ex)
            {
                AppendLog("B VPN 연결 재확인 실패: " + ex.GetType().Name +
                    ". B 작업은 2분간 대기하며 A 작업에는 영향을 주지 않습니다.");
                NotifyAutomationIssue("B VPN 연결 장애", "독립 IP 재확인 실패: " + ex.GetType().Name);
            }
            finally { _bConnectionRecheckRunning = false; }
            return;
        }
        // Normal posts take priority. A pending read-only check must never
        // delay a different server that is already eligible to register.
        var jobs = _jobs.Where(x => WantsWork(x) && x.NextDueUtc <= DateTime.UtcNow)
                        .OrderBy(x => x.LastSucceededUtc ?? DateTime.MinValue)
                        .ThenBy(x => x.NextDueUtc).ToList();
        foreach (var job in jobs)
        {
            if (!CanRunNow(job, out _)) continue;
            if (!string.IsNullOrWhiteSpace(job.PendingSitePriceTarget))
            {
                // A requested price change replaces THIS job's regular repost
                // turn; it is never an extra batch and shares CanRunNow/quota.
                // Local-only edits after the reservation invalidate the old
                // amount instead of changing what the user approved.
                if (!TrySitePrice(job.PendingSitePriceTarget, out long reserved) ||
                    !TrySitePrice(job.Price, out long local) || reserved != local)
                {
                    job.PendingSitePriceTarget = "";
                    _config.Save();
                    AppendLog($"가격 예약 취소: {job.Server} · {job.ListingNumber} · 예약 후 로컬 금액 변경");
                }
                else
                {
                    bool updated = await ApplySitePriceAsync(job, bulkApproval: true, fromScheduler: true);
                    if (updated)
                    {
                        job.PendingSitePriceTarget = "";
                        // If the actual site price was already identical, no
                        // POST was needed but this due slot still concludes.
                        if (job.NextDueUtc <= DateTime.UtcNow)
                            job.NextDueUtc = DateTime.UtcNow.AddMinutes(job.IntervalMinutes);
                        _config.Save();
                        AppendLog($"자동등록 순번 가격 반영 완료: {job.Server} · {job.ListingNumber}");
                    }
                    else if (_schedulerRunning && !job.RequiresReview)
                    {
                        // A pre-submit verification failure must not spin on
                        // every timer tick or block healthy listings.
                        job.RequiresReview = true;
                        job.Enabled = false;
                        job.NextDueUtc = DateTime.MaxValue;
                        job.LastStatus = "예약 가격 변경 확인 실패 (자동 재시도 금지)";
                        _config.Save();
                        AppendLog($"예약 가격 수정 보류: {job.Server} · {job.ListingNumber} · " +
                            _lastSitePriceEditFailure + " · 다른 물품은 계속 등록합니다.");
                        NotifyAutomationIssue("예약 가격 수정 보류", _lastSitePriceEditFailure, job);
                    }
                    _jobsGrid.Refresh(); _autoGrid.Refresh(); RefreshAutoSummary();
                    return;
                }
            }
            await ExecuteJobAsync(job, false);
            _jobsGrid.Refresh();
            return;
        }
        if (DateTime.UtcNow >= _nextPendingReviewUtc)
        {
            // A read-only check cannot race an active registration. Rotate
            // candidates to avoid starving one of several pending listings.
            _nextPendingReviewUtc = DateTime.UtcNow.AddMinutes(2);
            var held = _jobs.Where(j => j.RequiresReview && j.PendingRepostAttemptUtc.HasValue &&
                !string.IsNullOrWhiteSpace(j.PendingRepostBeforeStamp) &&
                ListingIdentity.HasNumber(j.ListingNumber) &&
                MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(j)))
                .OrderBy(j => j.PendingRepostAttemptUtc).ToList();
            if (held.Count > 0)
            {
                var candidate = held[_pendingReviewCursor++ % held.Count];
                await VerifyOneHeldListingInBackgroundAsync(candidate);
                return; // Resume ordinary registrations at the next tick.
            }
        }
        _jobsGrid.Refresh();
    }

    private async Task VerifyOneHeldListingInBackgroundAsync(ListingJob candidate)
    {
        if (_busy || !_schedulerRunning || _myListingsImportRunning || _pendingMyListingsImport) return;
        if ((_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system)) return;
        _busy = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _pendingReviewRun = cancellation;
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            if (_autoLogin is not null)
                await _autoLogin.WaitForLoginNavigationAsync(cancellation.Token);
            AppendLog($"보류 자동 확인: {candidate.Server} · {candidate.ListingNumber} (조회만, 재등록 없음)");
            await ReconcilePendingRepostsAsync(cancellation.Token, candidate);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            AppendLog("보류 자동 확인: 조회 시간이 초과되었거나 사용자가 작업을 중지했습니다. 다음 주기에 재검사합니다.");
        }
        catch (Exception ex)
        {
            AppendLog("보류 자동 확인: " + candidate.Server + " · 조회 실패(" + ex.GetType().Name + "). 다른 등록 작업은 계속합니다.");
        }
        finally
        {
            if (ReferenceEquals(_pendingReviewRun, cancellation)) _pendingReviewRun = null;
            _busy = false;
            _jobsGrid.Refresh();
            RefreshAutoSummary();
            DispatchQueuedMyListingsImport();
        }
    }

    private async Task ExecuteJobAsync(ListingJob job, bool dryRun)
    {
        if ((_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system))
        {
            AppendLog($"{_system} 연결 확인 필요: 별도 IP 확인 전에는 등록/재등록을 실행하지 않습니다.");
            if (_schedulerRunning) NotifyAutomationIssue($"{_system} VPN 연결 확인 필요",
                "독립 외부 IP를 검증하지 못해 해당 시스템의 등록 작업을 실행하지 않았습니다.");
            return;
        }
        if (_busy) { AppendLog("이미 다른 물품 작업이 진행 중입니다."); return; }
        if (!ValidateJob(job, dryRun, out var error))
        {
            AppendLog("실행 거절: " + error);
            if (_schedulerRunning && !dryRun) NotifyAutomationIssue("자동등록 설정 오류", error, job);
            return;
        }
        if (!dryRun && !CanRunNow(job, out var pacingReason))
        { AppendLog("실행 대기: " + pacingReason); return; }
        _busy = true;
        _currentlyExecutingJob = job;
        var executionClock = Stopwatch.StartNew();
        long submitStartMilliseconds = -1;
        _currentRun?.Dispose(); _currentRun = new CancellationTokenSource();
        var token = _currentRun.Token;
        bool submitted = false;
        string preSubmitUrl = "";
        bool successSelectorBefore = false;
        string preSubmitListingNumber = "";
        bool repostFromOwnedList = false;
        bool exactOwnedRepostSubmitted = false;
        string preRepostStamp = "";
        string preRepostListUrl = "";
        _armedOwnListRepost = "";
        _repostDialogBlocked = "";
        _repostServerCooldownRejected = false;
        _repostSuccessAlertSeen = false;
        _siteReportedIdRemaining = null;
        _submissionVerificationPending = false;
        job.LastStatus = "실행 중"; _jobsGrid.Refresh();
        _state.Text = dryRun ? "시험 실행 중" : "등록 실행 중"; _state.ForeColor = Color.DarkBlue;
        var flow = _config.WorkflowFor(EffectiveOperation(job));
        AppendLog($"작업 시작: [{flow.Operation}] {job.Server} / {job.Title}" + (dryRun ? " (미제출 테스트)" : ""));
        if (flow.Operation == "재등록" && MyListingsImport.IsOwnedListUrl(EntryFor(job, flow)))
            AppendLog("재등록 방식: 본인 관리목록에 바로 접속하여 정확한 등록번호·제목 행을 검증합니다.");
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            // The login form can still be submitting/redirecting just after
            // scheduling starts. Wait until its browser navigation settles;
            // otherwise the first repost's management-page navigation may be
            // canceled by that unrelated login redirect.
            if (_autoLogin is not null)
            {
                try { await _autoLogin.WaitForLoginNavigationAsync(token); }
                catch (InvalidOperationException ex)
                {
                    throw new OwnedListLoginPendingException(
                        "로그인 전환 중에는 등록 작업을 시작하지 않습니다. " + ex.Message);
                }
            }
            if (!IsAllowedUrl(Expand("{EntryUrl}", job, flow))) throw new InvalidOperationException("허용 사이트가 아닙니다.");
            for (int i = 0; i < flow.Steps.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var step = flow.Steps[i];
                if (string.IsNullOrWhiteSpace(step.Selector) && step.Action == "제출") continue; // optional confirmation
                if (step.Action == "제출")
                {
                    if (dryRun)
                    {
                        if (flow.Operation == "재등록")
                        {
                            if (MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri))
                            {
                                var result = await LocateOwnedListRepostAsync(job, token);
                                if (result.Status != "ready")
                                    throw new InvalidOperationException("[시험] 재등록 대상 행 확인 실패: " + result.Message);
                                AppendLog("[시험] 실제 제출 없이 해당 물품 행·재등록 버튼 확인: " + job.ListingNumber);
                            }
                            else
                            {
                                var existing = await InspectCurrentIdentityAsync(job, token);
                                if (!ListingIdentity.IsConfirmedExisting(existing, job))
                                {
                                    var fallback = await LocateOwnedListFallbackAsync(job, token);
                                    if (fallback.Status != "ready")
                                        throw new InvalidOperationException("[시험] 상세 화면 확인 실패 및 본인 등록목록 재탐색 실패: " +
                                            existing.Error + " / " + fallback.Message + ". 제출하지 않았습니다.");
                                    AppendLog("[시험] 본인 등록목록에서 정확한 등록번호·제목·재등록 버튼 확인: " + job.ListingNumber);
                                }
                                else AppendLog("[시험] 실제 제출 없이 상세 등록번호 확인: " + job.ListingNumber);
                            }
                        }
                        else
                        {
                            // A new-listing dry run must test the final button
                            // too, but it must never invoke that button.
                            string submitSelector = step.Selector;
                            if (submitSelector.StartsWith("@auto:", StringComparison.OrdinalIgnoreCase))
                                submitSelector = await ResolveAutoSelectorAsync(submitSelector[6..],
                                    Math.Max(1, step.TimeoutSeconds), token);
                            if (!await WaitForElementAsync(submitSelector, Math.Max(1, step.TimeoutSeconds), token))
                                throw new InvalidOperationException("[시험] 신규글 최종 등록 버튼을 찾지 못했습니다: " + step.Selector);
                            AppendLog("[시험] " + (flow.Operation == "판매등록" ? "판매" : "구매") +
                                "글 입력칸과 최종 등록 버튼 확인 완료 · 실제 제출 없음");
                        }
                        AppendLog("[시험] 최종 제출 버튼은 누르지 않음: " + (i + 1));
                        break;
                    }
                    // A repost button must belong to this EXACT existing listing;
                    // a generic '재등록' button on a list of other items is unsafe.
                    if (flow.Operation == "재등록")
                    {
                        repostFromOwnedList = MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri);
                        if (repostFromOwnedList)
                        {
                            var ownRow = await LocateOwnedListRepostAsync(job, token);
                            if (ownRow.Status != "ready")
                                throw new InvalidOperationException("내 등록목록 재등록 차단: " + ownRow.Message +
                                    ". 등록번호와 제목이 일치하는 물품 행의 버튼을 찾지 못했습니다. 제출하지 않았습니다.");
                            preRepostStamp = ownRow.Stamp;
                            preRepostListUrl = _web.Source?.AbsoluteUri ?? "";
                            AppendLog("본인 등록글 행 확인: " + job.ListingNumber + " / " + job.Title);
                        }
                        else
                        {
                            var beforeRepost = await InspectCurrentIdentityAsync(job, token);
                            if (!ListingIdentity.IsConfirmedExisting(beforeRepost, job))
                            {
                                var fallback = await LocateOwnedListFallbackAsync(job, token);
                                if (fallback.Status != "ready")
                                    throw new InvalidOperationException(
                                        "재등록 차단: 상세 화면과 본인 관리목록 모두에서 대상 글을 검증하지 못했습니다. " +
                                        $"번호 일치={string.Equals(beforeRepost.Number, job.ListingNumber, StringComparison.Ordinal)}, " +
                                        $"제목 확인={beforeRepost.TitlePresent}, 상세 화면={beforeRepost.DetailPage}. " +
                                        "목록 검사: " + fallback.Message + ". 제출하지 않았습니다.");
                                repostFromOwnedList = true;
                                preRepostStamp = fallback.Stamp;
                                preRepostListUrl = _web.Source?.AbsoluteUri ?? "";
                                AppendLog("본인 관리목록에서 재등록 대상 행 재확인: " + job.ListingNumber + " / " + job.Title);
                            }
                        }
                    }
                    if (!submitted)
                    {
                        if (!CanRunNow(job, out var wait)) throw new InvalidOperationException(wait);
                        var now = DateTime.UtcNow;
                        // A success marker already on screen cannot prove a NEW submission.
                        preSubmitUrl = _web.Source?.AbsoluteUri ?? "";
                        // A repost from a verified own-list row already has an
                        // exact ID. Rechecking the whole list here only delays
                        // the next server and cannot add identity evidence.
                        preSubmitListingNumber = repostFromOwnedList ? job.ListingNumber :
                            (await InspectCurrentIdentityAsync(job, token)).Number;
                        successSelectorBefore = await SuccessSelectorVisibleAsync(flow.SuccessSelector);
                        bool urlAlreadyMatches = !string.IsNullOrWhiteSpace(flow.SuccessUrlContains) &&
                            preSubmitUrl.Contains(flow.SuccessUrlContains, StringComparison.OrdinalIgnoreCase);
                        if (urlAlreadyMatches && (string.IsNullOrWhiteSpace(flow.SuccessSelector) || successSelectorBefore))
                            throw new InvalidOperationException("제출 전에 성공 확인 조건이 이미 충족되어 있습니다. 등록 완료 화면 전용 CSS 또는 URL을 설정하세요.");
                        HourlyServerQuota.MarkAttempt(HourlyServerQuota.Current(_config.ServerHourlyCounters, job, now));
                        _config.LastServerAttemptUtc[HourlyServerQuota.Key(job)] = now;
                        // IMPORTANT: checkpoint to disk BEFORE touching the submit button.
                        // Process termination during a live submission must never silently
                        // retry the listing on the next launch.
                        job.RequiresReview = true;
                        job.PendingRepostBeforeStamp = flow.Operation == "재등록" ? preRepostStamp : "";
                        job.PendingRepostAttemptUtc = flow.Operation == "재등록" ? now : null;
                        job.LastStatus = "제출 중: 결과 확인 필요";
                        job.NextDueUtc = DateTime.MaxValue;
                        _config.Jobs = _jobs.ToList();
                        _config.Save();
                        submitted = true;
                        submitStartMilliseconds = executionClock.ElapsedMilliseconds;
                        _submissionVerificationPending = true;
                    }
                }
                AppendLog($"  {i + 1}/{flow.Steps.Count} {step.Action} " + (step.Action == "입력" ? "(내용 입력)" : step.Selector));
                if (step.Action == "제출" && repostFromOwnedList)
                {
                    _armedOwnListRepost = job.ListingNumber;
                    var result = await InspectOwnedListRepostAsync(job, true, token);
                    if (result.Status != "clicked")
                        throw new InvalidOperationException("해당 등록번호의 행 재등록 클릭 실패: " + result.Message);
                    exactOwnedRepostSubmitted = true;
                    AppendLog("본인 물품 재등록 명령 전달 (" + result.Method + "): " + job.ListingNumber);
                    await Task.Delay(650, token);
                    if (!string.IsNullOrEmpty(_repostDialogBlocked))
                        throw new InvalidOperationException(_repostDialogBlocked);
                }
                else await ExecuteStepAsync(step, job, flow, token);
                if (step.Action == "제출" && !dryRun)
                    await ConfirmExactRegistrationDialogAsync(token);
                if (step.Action == "제출" && !string.IsNullOrEmpty(_repostDialogBlocked))
                    throw new InvalidOperationException(_repostDialogBlocked);
            }
            if (dryRun)
            {
                job.LastStatus = "시험 완료 (미제출)";
                AppendLog("시험 완료. 실제 제출 여부는 사이트 폼을 확인하세요.");
                AppendLog($"시험 완료 요약: {job.Server} · {job.ListingNumber} · 제출 없음");
            }
            else
            {
                if (!submitted) throw new InvalidOperationException("제출 버튼 단계가 실행되지 않았습니다.");
                bool verified = await VerifySuccessAsync(flow, job, preSubmitListingNumber, preSubmitUrl,
                    successSelectorBefore, preRepostStamp, preRepostListUrl, token);
                if (!verified) throw new InvalidOperationException("제출 이후 성공 확인 조건을 충족하지 못했습니다. 중복 등록을 피하려면 사이트에서 먼저 물품 상태를 확인하세요.");
                // Repost is not a NEW listing. Its original number was verified
                // on the exact owned-list row BEFORE invoking that row's native
                // repost action. After a separate verified SITE success signal,
                // a generic completion page need not expose the old ID again.
                // Requiring a NEW page-bound ID here caused a false failure
                // even after ItemMania had reported a successful repost.
                bool exactOwnedRepostConfirmed = flow.Operation == "재등록" &&
                    repostFromOwnedList && exactOwnedRepostSubmitted &&
                    ListingIdentity.HasNumber(job.ListingNumber) &&
                    MyListingsImport.IsOwnedListUrl(preRepostListUrl);
                var identity = new ListingIdentityEvidence();
                bool identityOk = exactOwnedRepostConfirmed;
                if (exactOwnedRepostConfirmed)
                    AppendLog("재등록 성공 확인: 본인 등록목록의 정확한 등록번호·제목 행에서 제출했고, 사이트 성공 응답을 확인했습니다: " + job.ListingNumber);
                else
                {
                    // NEW registrations must still establish a newly created
                    // listing identity. Legacy repost paths need current-page
                    // identity evidence because their target may be ambiguous.
                    identity = await InspectCurrentIdentityAsync(job, token);
                    identityOk = flow.Operation == "재등록" ?
                        ListingIdentity.IsConfirmedExisting(identity, job) :
                        ListingIdentity.IsConfirmedNew(identity, job);
                }
                if (!identityOk)
                    throw new InvalidOperationException("등록 완료 표시를 찾았지만 해당 글의 등록번호를 확정하지 못했습니다. " +
                        identity.Error + " / 중복 등록을 막기 위해 사이트 확인 전까지 자동 실행을 보류합니다.");
                if (flow.Operation != "재등록") job.ListingNumber = identity.Number;
                job.InitialCompleted = true; job.RequiresReview = false;
                job.PendingRepostBeforeStamp = "";
                job.PendingRepostAttemptUtc = null;
                if (flow.Operation is "판매등록" or "구매등록")
                {
                    job.OriginalTradeSide = flow.Operation == "판매등록" ? "판매" : "구매";
                    // Retain a safe own-list navigation fallback if the success
                    // page did not expose a listing-specific repost URL.
                    if (string.IsNullOrWhiteSpace(job.SourceListUrl))
                        job.SourceListUrl = job.OriginalTradeSide == "판매"
                            ? MyListingsImport.SellListingsUrl : MyListingsImport.BuyListingsUrl;
                }
                job.LastSucceededUtc = DateTime.UtcNow;
                AppendLog("등록번호 확인: " + job.ListingNumber);
                if (_siteReportedIdRemaining is int reportedRemaining)
                    ReconcileSiteQuota(job, reportedRemaining);
                if (flow.Operation != "재등록" && job.AutoRepost && string.IsNullOrWhiteSpace(job.EntryUrl))
                {
                    job.EntryUrl = await CaptureRepostUrlAsync(flow, job, token, identity);
                }
                if (job.AutoRepost || job.Operation == "재등록")
                {
                    string repostTarget = EntryFor(job, _config.WorkflowFor("재등록"));
                    if (string.IsNullOrWhiteSpace(repostTarget) || !IsAllowedUrl(repostTarget))
                    {
                        job.NextDueUtc = DateTime.MaxValue;
                        job.RequiresReview = true;
                        job.LastStatus = "등록 성공 / 재등록 물품 URL 확인 필요";
                        AppendLog("등록 확인. 자동재등록 보류: 해당 물품의 고유 URL을 확인할 수 없습니다. 다른 물품을 재등록하지 않습니다.");
                    }
                    else
                    {
                        if (job.Enabled)
                        {
                            // The server quota clock distributes registered posts
                            // fairly. Fixed per-item intervals used to waste most
                            // of the permitted registrations.
                            job.NextDueUtc = DateTime.UtcNow;
                            job.LastStatus = "성공 확인 / 재등록 예약";
                            AppendLog("등록 확인 / 다음 재등록: " + job.Title + " / " + job.NextDueLocal);
                        }
                        else
                        {
                            job.NextDueUtc = DateTime.MaxValue;
                            job.LastStatus = "등록 성공 (자동 반복 꺼짐)";
                            AppendLog("등록 확인: " + job.Title + " / 자동 반복은 꺼져 있습니다.");
                        }
                    }
                }
                else
                {
                    job.NextDueUtc = DateTime.MaxValue;
                    job.LastStatus = "등록 성공 (재등록 꺼짐)";
                    AppendLog("등록 확인: " + job.Title);
                }
                string remaining = _siteReportedIdRemaining is int left ? $" · 사이트 잔여 {left}회" : "";
                AppendLog($"{(flow.Operation == "재등록" ? "재등록 완료 요약:" : "등록 완료 요약:")} " +
                    $"{job.Server} · {job.ListingNumber}{remaining}" +
                    (job.RequiresReview ? " · 추가 확인 필요" : ""));
            }
        }
        catch (OwnedListLoginPendingException ex) when (!submitted)
        {
            // This failure happened during read-only navigation, before any
            // submit click. It is neither a posting failure nor an ambiguous
            // result requiring review; retry after login has time to settle.
            _loginRecoveryPauseUntilUtc = DateTime.UtcNow.AddSeconds(30);
            job.LastStatus = "로그인 전환 대기 (미제출)";
            job.NextDueUtc = _loginRecoveryPauseUntilUtc;
            AppendLog($"{_system} 로그인 복구 대기: {job.Server} · {job.ListingNumber} · " +
                "등록 작업은 제출되지 않았습니다. 30초 후 자동 재시도합니다.");
            AppendLog("안내: 사이트 로그인이 완료되지 않으면 내 계정의 로그인 상태를 확인하세요. " + ex.Message);
            if (_schedulerRunning) NotifyAutomationIssue("사이트 로그인 복구 대기", ex.Message, job);
        }
        catch (OwnedListConnectivityPendingException ex) when (!submitted && _system == "B")
        {
            // HTTP 0 or an unrendered own-list is not a failed submission.
            // Pause *all B jobs* before doing a read-only VPN/IP recheck.
            _bConnectionRecoveryUtc = DateTime.UtcNow.AddSeconds(90);
            job.NextDueUtc = _bConnectionRecoveryUtc;
            job.LastStatus = "B VPN·관리목록 연결 재확인 대기 (미제출)";
            AppendLog($"B VPN 접속 대기: {job.Server} · {job.ListingNumber} · " + ex.Message);
            AppendLog("B 자동등록 전체를 90초 대기한 뒤 VPN의 독립 IP를 재확인합니다. " +
                "실제 재등록 요청은 하지 않았으며 A는 계속 실행됩니다.");
            if (_schedulerRunning) NotifyAutomationIssue("B VPN 접속 대기", ex.Message, job);
        }
        catch (OperationCanceledException)
        {
            job.LastStatus = submitted ? "중지됨 (사이트 확인 필요)" : "중지됨";
            if (submitted) job.RequiresReview = true;
            job.NextDueUtc = submitted ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(10);
            AppendLog("작업 중지: " + job.Title + (submitted ? " / 제출이 진행되었을 수 있으니 중복 등록 전에 반드시 확인" : ""));
            AppendLog($"작업 중지 요약: {job.Server} · {job.ListingNumber} · " +
                (submitted ? "결과 확인 필요" : "미제출"));
        }
        catch (Exception ex) when (submitted && _repostServerCooldownRejected)
        {
            // An explicit site alert that this server cannot post yet is a
            // definitive rejection, not an unknown submission outcome. Keep
            // other servers running; retry this item after a conservative gap.
            job.RequiresReview = false;
            job.PendingRepostBeforeStamp = "";
            job.PendingRepostAttemptUtc = null;
            job.NextDueUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, _config.MinimumServerSeconds));
            job.LastStatus = "사이트 서버 재등록 간격 대기";
            AppendLog("사이트가 이 서버의 재등록을 명확히 거절했습니다. 다른 서버는 계속 진행하며 " +
                $"해당 글은 {job.NextDueLocal} 이후 재시도합니다. " + ex.Message);
            AppendLog($"재등록 대기 요약: {job.Server} · {job.ListingNumber} · 사이트 간격 제한 · " +
                $"{job.NextDueLocal} 재시도");
        }
        catch (Exception ex)
        {
            job.LastStatus = submitted ? "제출 결과 확인 필요" : "오류 (미제출)";
            if (submitted) job.RequiresReview = true;
            job.NextDueUtc = submitted ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(10);
            AppendLog("작업 오류: " + ex.Message);
            AppendLog($"{(flow.Operation == "재등록" ? "재등록 실패 요약:" : "등록 실패 요약:")} " +
                $"{job.Server} · {job.ListingNumber} · {ex.Message}");
            if (!dryRun) NotifyAutomationIssue(submitted ? "제출 결과 확인 필요" : "등록 작업 오류", ex.Message, job);
            if (_schedulerRunning && submitted)
                AppendLog("결과 확인이 필요한 물품 1건만 보류했습니다. 다른 정상 물품은 계속 등록하며 " +
                    "해당 물품은 사이트의 정확한 등록번호·재등록 시각을 백그라운드에서 검사합니다.");
        }
        finally
        {
            executionClock.Stop();
            if (submitStartMilliseconds >= 0)
                AppendLog($"처리 시간 요약: {job.Server} · {job.ListingNumber} · 총 {executionClock.ElapsedMilliseconds / 1000.0:F1}초 · " +
                    $"제출 전 {submitStartMilliseconds / 1000.0:F1}초 · 제출 후 {(executionClock.ElapsedMilliseconds - submitStartMilliseconds) / 1000.0:F1}초");
            _armedOwnListRepost = "";
            _repostDialogBlocked = "";
            _repostServerCooldownRejected = false;
            _repostSuccessAlertSeen = false;
            _siteReportedIdRemaining = null;
            _submissionVerificationPending = false;
            int appliedDeferredEdits = _deferredExecutingJobEdits.Count;
            foreach (var deferredEdit in _deferredExecutingJobEdits) deferredEdit();
            _deferredExecutingJobEdits.Clear();
            _currentlyExecutingJob = null;
            _busy = false;
            _jobsGrid.Refresh();
            _config.Save();
            if (appliedDeferredEdits > 0)
                AppendLog($"진행 중이던 물품의 일괄수정 {appliedDeferredEdits}개 항목을 작업 종료 후 적용했습니다.");
            _state.Text = _schedulerRunning ? "자동 실행 중" : "중지/대기";
            _state.ForeColor = _schedulerRunning ? Color.ForestGreen : Color.Maroon;
            // A paused scheduler must not block a requested read-only import.
            // Keep the current submission flow intact; import after it releases WebView2.
            DispatchQueuedMyListingsImport();
        }
    }

    // A previously saved detail URL can be stale or display a shared management
    // page without the title. Re-enter the account's own list, then verify one
    // exact row and one row-scoped repost button. No broad/global button fallback.
    private async Task<OwnedListRepostResult> LocateOwnedListFallbackAsync(ListingJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string ownListUrl = OwnedListUrlFor(job);
        if (!MyListingsImport.IsOwnedListUrl(ownListUrl) || !IsAllowedUrl(ownListUrl))
            return new OwnedListRepostResult { Status = "error", Message = "본인 등록목록 주소 또는 거래 방향을 검증할 수 없습니다" };
        AppendLog("상세 URL에서 글 확인 실패 → 본인 등록목록에서 등록번호·제목을 재검사합니다: " + job.ListingNumber);
        if (!MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri) ||
            MyListingsImport.IsSellListUrl(_web.Source?.AbsoluteUri) != MyListingsImport.IsSellListUrl(ownListUrl))
            await NavigateOwnedListAsync(ownListUrl, token);
        return await LocateOwnedListRepostAsync(job, token);
    }

    private async Task<OwnedListRepostResult> LocateOwnedListRepostAsync(ListingJob job, CancellationToken token,
        bool requireRepostButton = true)
    {
        string current = _web.Source?.AbsoluteUri ?? "";
        string expectedList = OwnedListUrlFor(job);
        if (MyListingsImport.IsOwnedListUrl(expectedList) &&
            (!MyListingsImport.IsOwnedListUrl(current) ||
             MyListingsImport.IsSellListUrl(current) != MyListingsImport.IsSellListUrl(expectedList)))
        {
            AppendLog("관리 목록 경로 복구: 현재 화면 대신 해당 거래 방향의 본인 목록을 다시 엽니다.");
            await NavigateOwnedListAsync(expectedList, token);
            current = _web.Source?.AbsoluteUri ?? "";
        }
        if (!MyListingsImport.IsOwnedListUrl(current))
            return new OwnedListRepostResult { Status = "error", Message = "등록글 관리 목록을 열지 못했습니다" };
        bool selling = MyListingsImport.IsSellListUrl(current);
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(current);
        string baseUrl = selling ? MyListingsImport.SellListingsUrl : MyListingsImport.BuyListingsUrl;
        if (!current.Equals(baseUrl, StringComparison.OrdinalIgnoreCase)) queue.Enqueue(baseUrl);
        // The site may show only the first ten pagination links even when a
        // later page exists. An earlier successful OWN-list import already
        // recorded the actual page URL for each registered listing. Include
        // those verified URLs, without guessing page numbers or endpoints.
        var knownPages = _jobs.Select(j => j.SourceListUrl)
            .Where(u => MyListingsImport.IsOwnedListUrl(u) &&
                MyListingsImport.IsSellListUrl(u) == selling)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (string knownPage in knownPages) queue.Enqueue(knownPage);
        if (knownPages.Count > 0)
            AppendLog($"본인 등록글에서 확인해 저장한 {knownPages.Count}개 관리 목록 페이지도 재등록 검색에 포함합니다.");
        const int maxPages = 25;
        int loadedPages = 0, unrenderedPages = 0, visibleListings = 0;
        int numberRows = 0, titleRows = 0;
        while (queue.Count > 0 && visited.Count < maxPages)
        {
            token.ThrowIfCancellationRequested();
            string next = queue.Dequeue();
            if (!MyListingsImport.IsOwnedListUrl(next) || MyListingsImport.IsSellListUrl(next) != selling ||
                !visited.Add(next)) continue;
            if (!string.Equals(_web.Source?.AbsoluteUri, next, StringComparison.OrdinalIgnoreCase))
                await NavigateOwnedListAsync(next, token);
            var (row, page) = await InspectOwnedListPageAsync(job, token, requireRepostButton);
            // ItemMania sometimes completes navigation before its SPA content
            // settles, or briefly redirects during account login. Recover via
            // read-only navigation and reinspection, never a speculative click.
            for (int retry = 1; retry <= 2 && IsTransientOwnedListPageError(row); retry++)
            {
                AppendLog($"관리 목록 일시 이동 오류: {retry}/2회 안전 재조회 ({job.Server})");
                await Task.Delay(900 * retry, token);
                await NavigateOwnedListAsync(next, token);
                (row, page) = await InspectOwnedListPageAsync(job, token, requireRepostButton);
            }
            if (row.Status == "ready" || (!requireRepostButton && row.Status == "found"))
            {
                if (!string.Equals(job.SourceListUrl, next, StringComparison.OrdinalIgnoreCase))
                {
                    job.SourceListUrl = next;
                    _config.Save();
                }
                if (visited.Count > 1) AppendLog($"재등록 물품을 관리 목록 {visited.Count}페이지 검사 후 찾았습니다: " + job.ListingNumber);
                return row;
            }
            if (row.Status != "not-found")
            {
                AppendLog($"재등록 검색 진단: 관리 목록 {visited.Count}페이지 검사 중 등록번호 해당 행 발견 " +
                    $"{row.NumberRows}건, 제목 일치 행 {row.TitleRows}건. {row.Message}");
                if (_system == "B" && IsTransientOwnedListPageError(row))
                    throw new OwnedListConnectivityPendingException(
                        "내 등록글 관리목록이 열리지 않았습니다. 로그인 또는 B VPN 연결 상태를 재확인합니다. " + row.Message);
                return row; // Never click on an unverified or mismatched row.
            }
            if (page.Listings.Count > 0 || page.IsEmptyList) loadedPages++;
            else unrenderedPages++;
            visibleListings += page.Listings.Count;
            numberRows += row.NumberRows;
            titleRows += row.TitleRows;
            foreach (string link in page.NextPageUrls.Take(30))
                if (MyListingsImport.IsOwnedListUrl(link) && MyListingsImport.IsSellListUrl(link) == selling &&
                    !visited.Contains(link)) queue.Enqueue(link);
        }
        // Another program using the same account can repost an item while we
        // are paging through this list. That moves its row to the newest page,
        // which we may have inspected *before* it moved. Recheck the official
        // first page exactly once. This is read-only; never submit an unverified
        // row or widen the listing ID/title matching rules.
        token.ThrowIfCancellationRequested();
        AppendLog($"관리 목록 {visited.Count}페이지 검사: 표시 확인 {loadedPages}페이지, " +
            $"물품 {visibleListings}건, 미표시 {unrenderedPages}페이지, " +
            $"등록번호 일치 행 {numberRows}건, 제목 일치 행 {titleRows}건. " +
            $"첫 페이지 최신 목록 재확인: {job.ListingNumber}");
        try
        {
            await NavigateOwnedListAsync(baseUrl, token);
            var (updated, _) = await InspectOwnedListPageAsync(job, token, requireRepostButton);
            if (updated.Status == "ready" || (!requireRepostButton && updated.Status == "found"))
            {
                if (!string.Equals(job.SourceListUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    job.SourceListUrl = baseUrl;
                    _config.Save();
                }
                AppendLog("최신 관리 목록 첫 페이지에서 정확한 등록번호·제목 재확인 성공: " + job.ListingNumber);
                return updated;
            }
            if (updated.Status != "not-found") return updated;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { AppendLog("최신 첫 페이지 재확인 실패: " + ex.GetType().Name); }
        return new OwnedListRepostResult { Status = "not-found", Message =
            $"관리 목록 {visited.Count}페이지에서 등록번호 {job.ListingNumber}를 찾지 못했습니다 " +
            $"(읽힌 물품 {visibleListings}건, 미표시 {unrenderedPages}페이지). " +
            "원본 글이 다른 상태/페이지로 이동했거나 저장된 등록번호가 변경됐을 수 있습니다" };
    }

    private static bool IsTransientOwnedListPageError(OwnedListRepostResult result) =>
        result.Status == "error" &&
        (result.Message.Contains("현재 화면이 내 판매·구매 등록글 목록이 아닙니다", StringComparison.Ordinal) ||
         result.Message.Contains("내 등록글 목록 화면이 아닙니다", StringComparison.Ordinal));

    // The site renders its management rows asynchronously. A navigation-complete
    // event alone does not prove the listing table is ready to inspect.
    // Always remain read-only here; a mismatch is never permission to repost.
    private async Task<(OwnedListRepostResult Row, ExistingListingPage Page)> InspectOwnedListPageAsync(
        ListingJob job, CancellationToken token, bool requireRepostButton = true)
    {
        var row = await InspectOwnedListRepostAsync(job, requireRepostButton ? false : null, token);
        var page = new ExistingListingPage();
        if (row.Status != "not-found") return (row, page);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string json = await _web.CoreWebView2.ExecuteScriptAsync(MyListingsImport.ExtractPageScript);
                page = MyListingsImport.DecodePage(json);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                page = new ExistingListingPage { Error = "목록 읽기 오류: " + ex.GetType().Name };
            }
            if (page.LoginRequired)
                return (new OwnedListRepostResult { Status = "error", Message = "관리 목록에 로그인해야 합니다. 재등록하지 않았습니다" }, page);

            bool idInParsedList = page.Listings.Any(x =>
                string.Equals(x.ListingNumber, job.ListingNumber, StringComparison.Ordinal));
            bool pageNotReady = page.Listings.Count == 0 && !page.IsEmptyList;
            if ((!pageNotReady && !idInParsedList) || attempt == 2) break;

            await Task.Delay(750, token);
            row = await InspectOwnedListRepostAsync(job, requireRepostButton ? false : null, token);
            if (row.Status != "not-found") return (row, page);
        }

        // The import parser and the repost parser intentionally use independent
        // selectors. If one sees the ID but the other cannot verify its button,
        // report that difference instead of pretending the listing is absent.
        var matches = page.Listings.Where(x =>
            string.Equals(x.ListingNumber, job.ListingNumber, StringComparison.Ordinal)).ToList();
        if (matches.Count > 0)
        {
            bool titleMatches = matches.Any(x =>
                string.Equals(string.Concat((x.Title ?? "").Where(c => !char.IsWhiteSpace(c))),
                    string.Concat((job.Title ?? "").Where(c => !char.IsWhiteSpace(c))),
                    StringComparison.OrdinalIgnoreCase));
            return (new OwnedListRepostResult
            {
                Status = "error",
                Message = titleMatches
                    ? "목록 추출에서는 등록번호·제목을 확인했지만 재등록 버튼을 연결하지 못했습니다. 페이지 구조를 점검해야 합니다"
                    : "목록 추출에서 등록번호는 확인했지만 저장된 제목이 현재 사이트와 다릅니다. 제목을 다시 불러오세요",
                ScannedRows = row.ScannedRows,
                NumberRows = Math.Max(row.NumberRows, matches.Count),
                TitleRows = row.TitleRows
            }, page);
        }
        if (page.Listings.Count == 0 && !page.IsEmptyList)
            return (new OwnedListRepostResult { Status = "error", Message =
                "관리 목록의 물품 행이 끝내 표시되지 않았습니다. 비어 있음을 확인할 수 없어 재등록하지 않았습니다. " + page.Error }, page);
        return (row, page);
    }

    private async Task<OwnedListRepostResult> InspectOwnedListRepostAsync(ListingJob job, bool? click,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri) || _web.CoreWebView2 is null)
            return new OwnedListRepostResult { Status = "error", Message = "내 등록글 목록 화면이 아닙니다" };
        try
        {
            string js = OwnedListRepost.BuildScript(job.ListingNumber, job.Title, click);
            string raw = await _web.CoreWebView2.ExecuteScriptAsync(js);
            return OwnedListRepost.Decode(raw);
        }
        catch (Exception ex)
        {
            return new OwnedListRepostResult { Status = "error", Message = "등록글 행 분석 오류: " + ex.GetType().Name };
        }
    }

    private async Task<ListingIdentityEvidence> InspectCurrentIdentityAsync(ListingJob job, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAllowedCurrentPage() || _web.CoreWebView2 is null)
            return new ListingIdentityEvidence { Error = "아이템매니아 공식 페이지가 아닙니다" };
        try
        {
            string script = ListingIdentity.BrowserScript.Replace("__ARG__", JsonSerializer.Serialize(new
            {
                title = job.Title,
                number = job.ListingNumber,
                entryUrl = job.EntryUrl
            }));
            ListingIdentityEvidence result = new();
            // The site can render the detail asynchronously after navigation.
            // Retry a few times before concluding that the page is unrelated.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (!IsAllowedCurrentPage()) return new ListingIdentityEvidence { Error = "확인 도중 공식 사이트를 벗어났습니다" };
                var json = await _web.CoreWebView2.ExecuteScriptAsync(script);
                result = ListingIdentity.Decode(json);
                if (ListingIdentity.IsConfirmedExisting(result, job) ||
                    (!ListingIdentity.HasNumber(job.ListingNumber) && ListingIdentity.IsConfirmedNew(result, job)))
                    break;
                if (attempt < 3) await Task.Delay(400, token);
            }
            return result;
        }
        catch (Exception ex) { return new ListingIdentityEvidence { Error = "등록번호 읽기 오류: " + ex.GetType().Name }; }
    }

    // Hidden WebView2 does not display JavaScript confirm/alert dialogs. If a
    // site reInsert() handler uses window.confirm(), disabling default dialogs
    // without this event handler silently cancels the repost. Never approve a
    // generic confirmation, payments, deletion, account checks or other pages.
    private void OnItemManiaScriptDialog(object? sender, CoreWebView2ScriptDialogOpeningEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_armedSitePriceEditNumber))
        {
            HandleSitePriceEditScriptDialog(e);
            return;
        }
        if (string.IsNullOrWhiteSpace(_armedOwnListRepost)) return;
        var currentPage = _web.Source?.AbsoluteUri ?? "";
        bool officialDialog = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && ItemManiaAccountStore.IsOfficialSite(uri);
        bool ownListContext = MyListingsImport.IsOwnedListUrl(currentPage);
        var message = (e.Message ?? "").Trim();
        var compactMessage = string.Concat(message.Where(c => !char.IsWhiteSpace(c)));
        bool officialCooldownAlert = officialDialog && e.Kind == CoreWebView2ScriptDialogKind.Alert &&
            compactMessage.Contains("해당서버에아직등록하실수없습니다", StringComparison.Ordinal);
        // The first confirm originates from the owned-list page. A subsequent
        // success alert may be dispatched by another official ItemMania page
        // after the repost handler navigates. That alert is safe to DISMISS,
        // but an unrelated second CONFIRM must never be approved automatically.
        bool officialRepostAlert = officialDialog && e.Kind == CoreWebView2ScriptDialogKind.Alert &&
            (message.Contains("재등록") || message.Contains("다시 등록"));
        if (!officialDialog || (!ownListContext && !officialRepostAlert && !officialCooldownAlert))
        {
            _repostDialogBlocked = "재등록 대상 목록 외부에서 확인창이 발생했습니다";
            AppendLog("재등록 확인창 출처 차단: 종류=" + e.Kind +
                " / 페이지=" + (_web.Source?.AbsolutePath ?? "없음") +
                " / 발생경로=" + (uri?.AbsolutePath ?? "알 수 없음"));
            return;
        }
        var text = message.Length > 120 ? message[..120] + "…" : message;
        var unsafeWords = new[] { "결제", "유료", "포인트", "마일리지", "프리미엄", "수수료", "삭제", "숨김",
            "비밀번호", "인증", "차단 해제", "보안", "입금", "충전" };
        var isUnsafe = unsafeWords.Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (e.Kind == CoreWebView2ScriptDialogKind.Alert)
        {
            // The site's explicit server cooldown message proves no repost was
            // accepted. Other alerts remain ambiguous and still require review.
            if (officialCooldownAlert)
                _repostServerCooldownRejected = true;
            // Informational alerts need to be closed before site JS can resume.
            // Only a positive, repost-specific server message is a success signal.
            var negative = new[] { "실패", "오류", "불가", "제한", "초과", "취소", "않", "못", "이미" };
            _repostSuccessAlertSeen = !isUnsafe &&
                (message.Contains("재등록") || message.Contains("다시 등록")) &&
                new[] { "완료", "성공", "되었습니다", "처리되었습니다" }.Any(message.Contains) &&
                !negative.Any(message.Contains);
            if (_repostSuccessAlertSeen)
                _siteReportedIdRemaining = HourlyServerQuota.ParseIdRemaining(message);
            if (!_repostSuccessAlertSeen)
                _repostDialogBlocked = "사이트의 재등록 안내를 확인해야 합니다: " + text;
            AppendLog((_repostSuccessAlertSeen ? "재등록 성공 안내: " : "사이트 안내(자동등록 보류): ") + text);
            e.Accept();
            return;
        }
        if (e.Kind == CoreWebView2ScriptDialogKind.Confirm && !isUnsafe &&
            (message.Contains("재등록") || message.Contains("다시 등록")))
        {
            e.Accept();
            AppendLog("본인 물품 재등록 확인창 처리: " + _armedOwnListRepost);
            return;
        }
        _repostDialogBlocked = "자동 승인할 수 없는 확인창: " + text;
        AppendLog("안전을 위해 확인창을 승인하지 않았습니다: " + text);
    }

    // Only the precise no-fee product-registration confirmation is eligible.
    // It never clicks payment, paid promotion, login, human verification, or
    // arbitrary '확인' buttons from the rest of the page.
    private async Task ConfirmExactRegistrationDialogAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAllowedCurrentPage() || _web.CoreWebView2 is null) return;
        // A site JavaScript alert is the authoritative response to an owned-row
        // repost. Do not stall another three seconds looking for an HTML modal.
        if (!string.IsNullOrWhiteSpace(_armedOwnListRepost) &&
            (_repostSuccessAlertSeen || _repostServerCooldownRejected || !string.IsNullOrWhiteSpace(_repostDialogBlocked))) return;
        const string script = """
        (() => {
          const visible=e=>e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden';
          const dialogs=[...document.querySelectorAll('[role="dialog"],.modal.show,.modal[style*="display: block"],.layer_pop')]
            .filter(visible).filter(el=>/등록하시려는\s*물품이\s*위와\s*같습니까|해당\s*물품을\s*등록하시겠습니까/.test(el.innerText||''));
          if(dialogs.length!==1)return {status:'not-confirmed',count:dialogs.length};
          const dialog=dialogs[0];
          if(/결제\s*금액\s*[:：]?\s*[1-9]|유료\s*등록|프리미엄\s*등록/.test(dialog.innerText||''))
            return {status:'payment-dialog'};
          const buttons=[...dialog.querySelectorAll('button,input[type="button"],input[type="submit"]')]
            .filter(visible).filter(e=>(e.innerText||e.value||'').trim()==='확인');
          if(buttons.length!==1)return {status:'ambiguous-confirmation'};
          buttons[0].click(); return {status:'confirmed'};
        })()
        """;
        // Some sites show their confirmation dialog asynchronously. Poll the
        // ONE exact free confirmation briefly; never search for arbitrary OK.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        do
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(_armedOwnListRepost) &&
                (_repostSuccessAlertSeen || _repostServerCooldownRejected || !string.IsNullOrWhiteSpace(_repostDialogBlocked))) return;
            if (!IsAllowedCurrentPage()) return;
            string json;
            try { json = await _web.CoreWebView2.ExecuteScriptAsync(script); }
            catch (Exception) { await Task.Delay(250, token); continue; } // navigation in progress
            using var result = JsonDocument.Parse(json);
            var status = result.RootElement.GetProperty("status").GetString() ?? "";
            if (status == "payment-dialog") throw new InvalidOperationException("결제/유료 등록 창이 감지되어 자동 진행을 중단했습니다.");
            if (status == "ambiguous-confirmation") throw new InvalidOperationException("등록 확인 버튼을 하나로 특정할 수 없어 중단했습니다.");
            if (status == "confirmed") { AppendLog("사이트의 무료 등록 확인창 처리"); await Task.Delay(800, token); return; }
            await Task.Delay(250, token);
        } while (DateTime.UtcNow < deadline);
    }

    private async Task<string> CaptureRepostUrlAsync(AutomationWorkflow flow, ListingJob job,
        CancellationToken token, ListingIdentityEvidence identity)
    {
        token.ThrowIfCancellationRequested();
        // Prefer a link obtained from the same page as a verified listing ID.
        if (MyListingsImport.IsSafeDetailUrl(identity.DetailUrl, job.ListingNumber) && IsAllowedUrl(identity.DetailUrl))
            return identity.DetailUrl;
        if (string.IsNullOrWhiteSpace(flow.RepostLinkSelector) || !IsAllowedCurrentPage()) return "";
        try
        {
            string script = """
            (() => {
              const arg=__ARG__;
              const links=Array.from(document.querySelectorAll(arg.selector))
                .filter(x=>x.matches('a[href]'));
              if(links.length!==1)return '';
              const a=links[0];
              // A link for a DIFFERENT listing must never be auto-selected.
              if(a.getAttribute('data-listing-title') && a.getAttribute('data-listing-title')!==arg.title)return '';
              return a.href;
            })()
            """.Replace("__ARG__", JsonSerializer.Serialize(new { selector = flow.RepostLinkSelector, title = job.Title }));
            string raw = await _web.CoreWebView2.ExecuteScriptAsync(script);
            string? link = JsonSerializer.Deserialize<string>(raw);
            if (MyListingsImport.IsSafeDetailUrl(link, job.ListingNumber) && IsAllowedUrl(link))
                return link;
        }
        catch (Exception ex) { AppendLog("재등록 대상 링크 탐색 실패: " + ex.Message); }
        return "";
    }

    // Labels/IDs in real ItemMania markup have NOT been verified. Exact-match
    // semantic resolver works when markup is conventional; ambiguity fails closed.
    private async Task<string> ResolveAutoSelectorAsync(string key, int timeoutSeconds, CancellationToken token)
    {
        string js = """
        (() => {
          const key=__KEY__;
          const dict={
            Game:['게임','게임명'], Server:['서버','서버명'],
            ItemCategory:['물품구분','물품종류','카테고리'],
            TradingMethod:['거래유형','매매유형'],
            Quantity:['수량','판매수량','구매수량','수량/분할수량'],
            MinQuantity:['최소수량','최소판매수량','최소구매수량'], MaxQuantity:['최대수량','최대판매수량','최대구매수량'],
            AutoDecrease:['수량자동감소','자동감소'],
            Price:['가격','금액','판매금액','구매금액','가격(원)','금액(원)'],
            Character:['캐릭터명','캐릭터이름','게임캐릭터'],
            Title:['제목','물품제목','상품제목','물품명','상품명'],
            Description:['상세설명','상세내용','내용','상품설명','물품설명']
          };
          const visible=e=>!!e&&e.getClientRects().length>0&&!e.disabled&&getComputedStyle(e).visibility!=='hidden';
          const norm=x=>String(x||'').replace(/[\s:*：]/g,'').trim();
          let out=[];
          if(key==='submit'||key==='submitSell'||key==='submitBuy'||key==='repost'){
            // A generic '등록하기' button is safe to consider only on the
            // corresponding new-listing editor, not on an account or list page.
            if(key==='submitSell'||key==='submitBuy'){
              const side=key==='submitSell'?'sell':'buy';
              if(!new RegExp('^/(?:trade/)?'+side+'(?:/|$)','i').test(location.pathname||''))
                return JSON.stringify({error:'신규 '+side+' 등록 화면이 아닙니다'});
            }
            const words=key==='repost'?['재등록','다시등록']:
              key==='submitSell'?['판매등록','물품등록하기','등록하기','등록']:
              key==='submitBuy'?['구매등록','물품등록하기','등록하기','등록']:
              ['판매등록','구매등록','물품등록하기','등록하기'];
            // Repost is commonly a normal anchor, not necessarily an a[role=button].
            // The caller has already verified the exact existing listing ID.
            out=Array.from(document.querySelectorAll('button,input[type="submit"],input[type="button"],a[href],a[role="button"]'))
              .filter(visible).filter(e=>words.includes(norm(e.value||e.innerText||e.textContent)));
          }else{
            const names=dict[key]; if(!names)return JSON.stringify({error:'지원하지 않는 자동필드: '+key});
            const wanted=names.map(norm);
            const all=Array.from(document.querySelectorAll('input,select,textarea,[role="combobox"],[contenteditable="true"]'))
              .filter(e=>visible(e)&&!['hidden','password','submit','button'].includes(e.type));
            out=all.filter(e=>{
              const labels=[e.getAttribute('data-field'),e.getAttribute('aria-label'),e.getAttribute('placeholder'),e.getAttribute('name'),e.id];
              if(e.labels)labels.push(...Array.from(e.labels).map(x=>x.textContent));
              if(e.id){const l=document.querySelector('label[for="'+CSS.escape(e.id)+'"]'); if(l)labels.push(l.textContent);}
              const td=e.closest('td');
              if(td){const th=td.previousElementSibling;if(th&&th.matches('th'))labels.push(th.textContent);}
              const dd=e.closest('dd');
              if(dd){const dt=dd.previousElementSibling;if(dt&&dt.matches('dt'))labels.push(dt.textContent);}
              return labels.some(x=>wanted.includes(norm(x)));
            });
            if(out.length>1 && out.every(e=>e.tagName==='INPUT'&&e.type==='radio'&&e.name&&e.name===out[0].name))
              out=[out[0]];
          }
          if(out.length!==1)return JSON.stringify({error:out.length?'후보 중복 ('+out.length+'개): '+key:'자동 매칭 실패: '+key});
          const el=out[0];
          if(el.id){const s='#'+CSS.escape(el.id);if(document.querySelectorAll(s).length===1)return JSON.stringify({selector:s});}
          if(el.name){const s=el.tagName.toLowerCase()+'[name="'+CSS.escape(el.name)+'"]';
            if(document.querySelectorAll(s).length===1)return JSON.stringify({selector:s});}
          const parts=[];
          for(let n=el;n&&n.nodeType===1;n=n.parentElement){
            if(n.id){parts.unshift('#'+CSS.escape(n.id));break;}
            let t=n.tagName.toLowerCase();let k=1;let p=n;
            while((p=p.previousElementSibling)!=null)if(p.tagName===n.tagName)k++;
            parts.unshift(t+':nth-of-type('+k+')');
          }
          const selector=parts.join(' > ');
          return JSON.stringify(document.querySelectorAll(selector).length===1?{selector}:{error:'유일하지 않은 필드: '+key});
        })()
        """.Replace("__KEY__", JsonSerializer.Serialize(key));
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        string lastError = "";
        do
        {
            token.ThrowIfCancellationRequested();
            if (!IsAllowedCurrentPage()) throw new InvalidOperationException("사이트가 허용된 주소를 벗어났습니다.");
            string raw = await _web.CoreWebView2.ExecuteScriptAsync(js);
            string? inner = JsonSerializer.Deserialize<string>(raw);
            if (!string.IsNullOrWhiteSpace(inner))
            {
                using var result = JsonDocument.Parse(inner);
                if (result.RootElement.TryGetProperty("selector", out var s) && !string.IsNullOrWhiteSpace(s.GetString()))
                    return s.GetString()!;
                lastError = result.RootElement.TryGetProperty("error", out var e) ? (e.GetString() ?? "") : "";
                if (lastError.StartsWith("후보 중복", StringComparison.Ordinal)) break; // never guess
            }
            await Task.Delay(350, token);
        } while (DateTime.UtcNow < deadline);
        throw new InvalidOperationException("자동 입력칸 연결 실패: " + key + " / " + lastError + ". 사이트 구조에 맞는 CSS 연결이 필요합니다.");
    }

    private async Task ExecuteStepAsync(AutomationStep step, ListingJob job, AutomationWorkflow flow, CancellationToken token)
    {
        string value = Expand(step.Value, job, flow);
        // Split-trade only fields must not interfere with ordinary listings.
        if (new[] { "{MinQuantity}", "{MaxQuantity}", "{AutoDecrease}" }.Contains(step.Value) && job.TradingMethod != "분할") return;
        if (string.IsNullOrWhiteSpace(value) &&
            (step.Value is "{Character}" or "{Description}") && step.Action == "입력") return;
        if (step.Action == "멈춤")
        {
            if (!int.TryParse(value, out int ms)) throw new InvalidOperationException("멈춤 단계에는 대기 시간(밀리초)을 숫자로 지정하세요.");
            await Task.Delay(Math.Clamp(ms, 0, 120000), token); return;
        }
        if (step.Action == "이동")
        {
            string url = string.IsNullOrWhiteSpace(value) ? Expand("{EntryUrl}", job, flow) : value;
            if (!IsAllowedUrl(url)) throw new InvalidOperationException("사이트 이동 거부: 허용되지 않은 URL");
            // Repost/import pages have their own NavigationId-correlated navigation.
            // A simultaneous background login redirect must not complete or abort
            // the first repost step by mistake.
            if (MyListingsImport.IsOwnedListUrl(url))
            {
                bool alreadyHere = flow.Operation == "재등록" &&
                    string.Equals(_web.Source?.AbsoluteUri, url, StringComparison.OrdinalIgnoreCase) &&
                    _web.CoreWebView2 is not null &&
                    await _web.CoreWebView2.ExecuteScriptAsync("document.readyState") == "\"complete\"";
                if (!alreadyHere) await NavigateOwnedListAsync(url, token);
            }
            else await NavigateAsync(url, token);
            return;
        }
        if (string.IsNullOrWhiteSpace(step.Selector)) throw new InvalidOperationException(step.Action + " 단계에 CSS 선택자가 없습니다.");
        if (!IsAllowedCurrentPage()) throw new InvalidOperationException("현재 페이지가 허용 사이트가 아닙니다. 로그인 화면/외부 사이트 여부를 확인하세요.");
        if (await ContainsHumanVerificationAsync()) throw new InvalidOperationException("CAPTCHA/봇 확인 화면이 감지되었습니다. 수동으로 해결한 뒤 진행하세요.");
        string selector = step.Selector;
        if (selector.StartsWith("@auto:", StringComparison.OrdinalIgnoreCase))
            selector = await ResolveAutoSelectorAsync(selector[6..], Math.Max(1, step.TimeoutSeconds), token);
        bool found = await WaitForElementAsync(selector, Math.Max(1, step.TimeoutSeconds), token);
        if (!found) throw new InvalidOperationException("요소를 찾지 못했습니다: " + step.Selector);
        if (step.Action == "대기") return;
        // Searchable game/server inputs (not native SELECT): type a query first,
        // then click only ONE visible exact-matching suggestion. Never assume
        // filling the search text alone selects the game's internal category ID.
        if (step.Action == "선택")
        {
            string tagJson = await _web.CoreWebView2.ExecuteScriptAsync(
                "(() => {const e=document.querySelector(" + JsonSerializer.Serialize(selector) + ");return e?.tagName || '';})()");
            if (JsonSerializer.Deserialize<string>(tagJson) == "INPUT")
            {
                await ExecuteStepAsync(new AutomationStep { Action = "입력", Selector = selector, Value = step.Value }, job, flow, token);
                await Task.Delay(450, token);
            }
        }
        string script = """
        (() => {
          const arg = __ARG__;
          try {
            const el=document.querySelector(arg.selector);
            if(!el)return {ok:false,error:'요소 없음'};
            el.scrollIntoView({block:'center',behavior:'instant'});
            if(arg.action==='입력'){
              if(el.isContentEditable){el.textContent=arg.value;el.dispatchEvent(new InputEvent('input',{bubbles:true,inputType:'insertText',data:arg.value}));}
              else if(el.tagName==='SELECT'){
                const opts=Array.from(el.options).filter(o=>o.value===arg.value||o.text.trim()===arg.value);
                if(opts.length!==1)return {ok:false,error:'드롭다운 옵션 없음 또는 중복: '+arg.value};
                el.value=opts[0].value;
                el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));
              }else if('value' in el){
                const proto=el.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;
                const desc=Object.getOwnPropertyDescriptor(proto,'value');
                if(desc&&desc.set)desc.set.call(el,arg.value);else el.value=arg.value;
                el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));
              }else return {ok:false,error:'입력할 수 없는 요소'};
            } else if(arg.action==='선택'){
              if(el.tagName==='SELECT'){
                const opts=Array.from(el.options).filter(o=>o.value===arg.value||o.text.trim()===arg.value);
                if(opts.length!==1)return {ok:false,error:'선택 옵션 없음 또는 중복: '+arg.value};
                el.value=opts[0].value;el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));
              }else if(el.tagName==='INPUT' && el.type==='radio'){
                const targets=Array.from(document.querySelectorAll('input[type="radio"]'))
                  .filter(x=>x.name===el.name && (x.value===arg.value || (x.labels&&Array.from(x.labels).some(l=>l.textContent.trim()===arg.value))));
                if(targets.length!==1)return {ok:false,error:'라디오 옵션을 찾지 못함: '+arg.value};
                targets[0].click();
              }else if(el.tagName==='INPUT'){
                // Searchable combobox options are often rendered as a sibling
                // popup instead of inside the input element.
                const opts=Array.from(document.querySelectorAll('[role="option"], [role="listbox"] li, .ui-autocomplete li, .autocomplete li, [data-option]'));
                const matches=opts.filter(x=>x.textContent.trim()===arg.value && x.getClientRects().length);
                if(matches.length!==1)return {ok:false,error:'검색 제안에서 정확한 옵션을 1개 찾지 못함: '+arg.value};
                matches[0].click();
              }else{
                // Custom list root: require exactly one visible, exact-name item.
                const items=Array.from(el.querySelectorAll('[role="option"],button,li,a,[role="menuitem"]'));
                const targets=items.filter(x=>x.textContent.trim()===arg.value && x.getClientRects().length);
                if(targets.length!==1)return {ok:false,error:'목록 안에서 정확히 일치하는 옵션을 1개 찾지 못함: '+arg.value};
                targets[0].click();
              }
            } else if(arg.action==='체크'){
              if(!('checked' in el))return {ok:false,error:'체크박스가 아닙니다'};
              const want=arg.value==='Y'||arg.value==='true';if(el.checked!==want)el.click();
            } else if(arg.action==='클릭'||arg.action==='제출') el.click();
            else return {ok:false,error:'지원하지 않는 단계: '+arg.action};
            return {ok:true};
          }catch(err){return {ok:false,error:String(err.message||err)};}
        })()
        """.Replace("__ARG__", JsonSerializer.Serialize(new { selector, action = step.Action, value }));
        string json = await _web.CoreWebView2.ExecuteScriptAsync(script);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new InvalidOperationException("사이트 조작 실패: " + (doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() : json));
        // Let async site handlers/rendering settle. Submissions may change URL or open a confirmation modal.
        await Task.Delay(step.Action is "클릭" or "제출" ? 650 : 200, token);
    }

    private async Task NavigateAsync(string url, CancellationToken token)
    {
        if (_web.CoreWebView2 is null) throw new InvalidOperationException("브라우저가 초기화되지 않았습니다.");
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? requestedNavigationId = null;
        EventHandler<CoreWebView2NavigationStartingEventArgs>? starting = null;
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? completed = null;
        starting = (_, args) =>
        {
            if (requestedNavigationId is null && string.Equals(args.Uri, url, StringComparison.OrdinalIgnoreCase))
                requestedNavigationId = args.NavigationId;
        };
        completed = (_, result) =>
        {
            if (requestedNavigationId.HasValue && result.NavigationId == requestedNavigationId.Value)
                completion.TrySetResult(result);
        };
        _web.NavigationStarting += starting;
        _web.NavigationCompleted += completed;
        try
        {
            _web.CoreWebView2.Navigate(url);
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(35), token);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"페이지 이동에 실패했습니다: {url} ({result.WebErrorStatus}, HTTP {result.HttpStatusCode})");
            await Task.Delay(600, token);
            if (!IsAllowedCurrentPage()) throw new InvalidOperationException("로그인/이동 후 다른 호스트로 이동했습니다. 해당 사이트에서 수동으로 인증하세요.");
        }
        finally
        {
            _web.NavigationStarting -= starting;
            _web.NavigationCompleted -= completed;
        }
    }

    private async Task<bool> WaitForElementAsync(string selector, int seconds, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        string arg = JsonSerializer.Serialize(selector);
        do
        {
            token.ThrowIfCancellationRequested();
            if (!IsAllowedCurrentPage()) throw new InvalidOperationException("허용된 사이트를 벗어났습니다.");
            try
            {
                string json = await _web.CoreWebView2.ExecuteScriptAsync("(() => {try {const e=document.querySelector(" + arg + ");return !!(e&&e.isConnected);}catch{return false;}})()");
                if (json == "true") return true;
            }
            catch (InvalidOperationException) { throw; }
            catch { /* navigation in progress */ }
            await Task.Delay(400, token);
        } while (DateTime.UtcNow < until);
        return false;
    }

    private async Task<bool> ContainsHumanVerificationAsync()
    {
        try
        {
            string json = await _web.CoreWebView2.ExecuteScriptAsync("""
            (() => !!document.querySelector('iframe[src*="recaptcha"], iframe[src*="hcaptcha"], .g-recaptcha, .h-captcha, [data-sitekey]'))()
            """);
            return json == "true";
        }
        catch { return false; }
    }

    private async Task<bool> SuccessSelectorVisibleAsync(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector) || _web.CoreWebView2 is null) return false;
        try
        {
            if (selector.Equals("@auto:success", StringComparison.OrdinalIgnoreCase))
            {
                string autoJs = """
                (()=>{
                  const texts=['등록완료','등록성공','등록되었습니다','등록이완료되었습니다','재등록완료'];
                  const visible=e=>e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden';
                  const nodes=Array.from(document.querySelectorAll('[role="status"],[role="alert"],.alert-success,.toast-success,.success-message'))
                    .filter(visible).filter(e=>texts.some(t=>(e.textContent||'').replace(/\s/g,'').includes(t)));
                  return nodes.length===1;
                })()
                """;
                return await _web.CoreWebView2.ExecuteScriptAsync(autoJs) == "true";
            }
            string js = "(() => {try{const e=document.querySelector(" + JsonSerializer.Serialize(selector) +
                ");return !!(e&&e.getClientRects().length&&getComputedStyle(e).visibility!==\"hidden\");}catch{return false;}})()";
            return await _web.CoreWebView2.ExecuteScriptAsync(js) == "true";
        }
        catch { return false; }
    }

    private async Task<bool> VerifySuccessAsync(AutomationWorkflow flow, ListingJob job, string beforeListingNumber,
        string beforeUrl, bool selectorBefore, string beforeRepostStamp, string beforeOwnListUrl, CancellationToken token)
    {
        // The site's repost handler can be asynchronous and may not show a
        // success toast. Re-query the official list repeatedly, rather than
        // treating a single 4-second snapshot as the final result.
        var until = DateTime.UtcNow.AddSeconds(flow.Operation == "재등록" ? 30 : 20);
        var nextListRecheck = DateTime.UtcNow.AddSeconds(3);
        int listRecheckCount = 0;
        do
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(_repostDialogBlocked))
                throw new InvalidOperationException(_repostDialogBlocked);
            if (!IsAllowedCurrentPage()) return false;
            if (flow.Operation == "재등록" && _repostSuccessAlertSeen)
            {
                AppendLog("사이트에서 재등록 성공 안내를 확인했습니다.");
                return true;
            }
            string currentUrl = _web.Source?.AbsoluteUri ?? "";
            bool changedToSuccessUrl = !string.IsNullOrWhiteSpace(flow.SuccessUrlContains) &&
                !string.Equals(beforeUrl, currentUrl, StringComparison.OrdinalIgnoreCase) &&
                currentUrl.Contains(flow.SuccessUrlContains, StringComparison.OrdinalIgnoreCase);
            bool newSuccessMarker = !selectorBefore && await SuccessSelectorVisibleAsync(flow.SuccessSelector);
            if (changedToSuccessUrl || newSuccessMarker) return true;
            // Some result pages have no toast. A NEW and unique listing number,
            // explicitly tied to this job title, is stronger evidence.
            if (flow.Operation != "재등록")
            {
                var evidence = await InspectCurrentIdentityAsync(job, token);
                if (ListingIdentity.IsConfirmedNew(evidence, job) &&
                    !string.Equals(evidence.Number, beforeListingNumber, StringComparison.Ordinal))
                    return true;
            }
            // The authenticated My Room page may repost by AJAX without URL
            // change or toast. Multiple fresh reads of the exact owned row
            // detect delayed server updates; a button click alone proves none.
            if (flow.Operation == "재등록" && DateTime.UtcNow >= nextListRecheck &&
                !string.IsNullOrWhiteSpace(beforeRepostStamp) &&
                MyListingsImport.IsOwnedListUrl(beforeOwnListUrl))
            {
                listRecheckCount++;
                nextListRecheck = DateTime.UtcNow.AddSeconds(4);
                try
                {
                    await NavigateAsync(beforeOwnListUrl, token);
                    var row = await InspectOwnedListRepostAsync(job, null, token);
                    if (row.Status == "not-found")
                    {
                        // After successful repost the item may move to page 1.
                        string firstPage = MyListingsImport.IsSellListUrl(beforeOwnListUrl) ?
                            MyListingsImport.SellListingsUrl : MyListingsImport.BuyListingsUrl;
                        if (!string.Equals(firstPage, beforeOwnListUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            await NavigateAsync(firstPage, token);
                            row = await InspectOwnedListRepostAsync(job, null, token);
                        }
                    }
                    if (row.Status == "found" && !string.IsNullOrWhiteSpace(row.Stamp) &&
                        !string.Equals(row.Stamp, beforeRepostStamp, StringComparison.Ordinal))
                    {
                        AppendLog("아이템매니아에서 해당 물품 재등록 시각 변경 확인: " + beforeRepostStamp + " → " + row.Stamp);
                        return true;
                    }
                    if (listRecheckCount == 1 || listRecheckCount % 3 == 0)
                        AppendLog("재등록 결과 재확인 " + listRecheckCount + "회: 대상 글의 등록시각 변경 대기 중");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppendLog("등록시각 재확인 실패: " + ex.GetType().Name); }
            }
            await Task.Delay(500, token);
        } while (DateTime.UtcNow < until);
        return false;
    }

    private void AppendLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(message))); return; }
        var when = DateTime.Now;
        string raw = $"[{when:HH:mm:ss}] {message}";
        _rawLogLines.Add(raw);
        if (_rawLogLines.Count > 2500) _rawLogLines.RemoveRange(0, 500);

        // Everyday view: one compact result per listing. Keep every original
        // step in the raw log and on-disk history for later troubleshooting.
        var display = LogPresentation.Format(message);
        if (display.Visible)
        {
            _displayLogLines.Add(($"[{when:HH:mm:ss}] {display.Text}", display.IsError));
            if (_displayLogLines.Count > 1200) _displayLogLines.RemoveRange(0, 200);
            if (display.IsError)
            {
                _errorLogCount++;
                RefreshErrorCounter();
            }
            if (!_showOnlyErrors || display.IsError)
            {
                if (_showOnlyErrors || _log.TextLength > 45000) RenderCompactLog();
                else
                {
                    _log.AppendText($"[{when:HH:mm:ss}] {display.Text}{Environment.NewLine}");
                    _log.SelectionStart = _log.TextLength;
                    _log.ScrollToCaret();
                }
            }
        }
        try
        {
            string directory = System.IO.Path.Combine(Settings.AutomationDirectory(_system), "logs");
            System.IO.Directory.CreateDirectory(directory);
            string file = System.IO.Path.Combine(directory, "automation_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            System.IO.File.AppendAllText(file, $"[{when:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* Never let log I/O interrupt a run. */ }
    }

    private void RefreshErrorCounter()
    {
        _logErrors.Text = _showOnlyErrors ? $"현재 오류 {_errorLogCount}건 · 보기" : $"현재 오류 {_errorLogCount}건";
        _logErrors.ForeColor = _errorLogCount > 0 ? Color.FromArgb(164, 38, 38) : UiTheme.Muted;
        _logErrors.BackColor = _showOnlyErrors ? Color.FromArgb(254, 235, 234) : UiTheme.SurfaceAlt;
    }

    private void RenderCompactLog()
    {
        var rows = _displayLogLines.Where(x => !_showOnlyErrors || x.Error)
            .TakeLast(450).Select(x => x.Text).ToArray();
        _log.Text = rows.Length > 0 ? string.Join(Environment.NewLine, rows) + Environment.NewLine :
            (_showOnlyErrors ? "오류가 없습니다. '오류 0건'을 다시 누르면 전체 기록으로 돌아갑니다." : "");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
