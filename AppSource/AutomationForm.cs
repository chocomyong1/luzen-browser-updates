using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private readonly AutomationConfiguration _config = AutomationConfiguration.Load();
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
    private readonly TextBox _fName = new() { Width = 210, PlaceholderText = "제목 검색" };
    private readonly Label _filterSummary = new() { AutoSize = true, ForeColor = UiTheme.Muted };
    private readonly TextBox _subId = new(), _siteId = new();
    private readonly CheckBox _paidRegistration = new() { Text = "유료 등록", AutoSize = true };

    private static ComboBox FilterDropdown(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, FlatStyle = FlatStyle.Standard };

    private readonly BindingList<AutomationStep> _steps = new();
    private readonly DataGridView _jobsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White };
    private readonly DataGridView _stepsGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White };
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly TextBox _browserUrl = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = UiTheme.Navy, ForeColor = Color.FromArgb(222, 232, 244), Font = new Font("Consolas", 9) };
    private readonly Label _state = new() { AutoSize = true, Text = "중지됨", ForeColor = Color.Maroon };
    private readonly CheckBox _livePosting = new() { Text = "실제 제출 허용", AutoSize = true };
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
    private readonly CheckBox _instantTransaction = new() { Text = "즉시거래 (사이트 인증 필요)", AutoSize = true };
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
    private readonly NumericUpDown _serverLimit = new() { Minimum = 1, Maximum = 500, Width = 68, Value = 30 };
    private readonly NumericUpDown _serverDelay = new() { Minimum = 21, Maximum = 3600, Width = 68, Value = 21 };
    private readonly TextBox _host = new() { Width = 160 };
    private readonly TextBox _flowUrl = new() { Width = 400 };
    private readonly TextBox _successSelector = new() { Width = 270 };
    private readonly TextBox _repostLinkSelector = new() { Width = 270 };
    private readonly TextBox _successUrlPart = new() { Width = 270 };
    private ListingJob? _selected;
    private bool _selectingJob;
    private bool _selectingFlow;
    private bool _schedulerRunning;
    private bool _busy;
    private bool _pendingMyListingsImport;
    private bool _myListingsImportRunning;
    private CancellationTokenSource? _myListingsImportCancellation;
    private TaskCompletionSource<bool>? _myListingsImportCompletion;
    private CancellationTokenSource? _currentRun;
    private DateTime _nextGlobalAllowedUtc = DateTime.MinValue;
    private string _pickerMode = "";
    private readonly TabControl _editorTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _autoTab = new("자동등록 현황");
    private readonly TabPage _itemTab = new("물품등록 · 편집");
    private readonly TabPage _workflowTab = new("고급 설정 · 사이트 연동");
    private readonly DataGridView _autoGrid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false,
        RowHeadersVisible = false, BackgroundColor = Color.White, ReadOnly = false
    };
    private readonly Label _autoSummary = new()
    {
        Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 5, 0, 0)
    };


    public AutomationForm(string defaultSubId = "")
    {
        _defaultSubId = defaultSubId == "Local" ? "" : defaultSubId;
        _jobs = new BindingList<ListingJob>(_config.Jobs);
        var currentSiteId = ItemManiaAccountStore.Load().UserId;
        foreach (var oldJob in _jobs)
        {
            // V12 did not have these fields; preserve any explicit existing values.
            if (string.IsNullOrWhiteSpace(oldJob.SubId)) oldJob.SubId = _defaultSubId;
            if (string.IsNullOrWhiteSpace(oldJob.SiteId)) oldJob.SiteId = currentSiteId;
        }
        Text = "Luzen Browser V22 - 자동 업데이트 지원";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(960, 600);
        Size = new Size(1500, 890);
        Font = new Font("맑은 고딕", 9.5F);
        Icon = new Icon(Settings.Asset("iBook1.ico"));
        _hourLimit.Value = Math.Clamp(_config.MaxSubmissionsPerHour, 1, 1000);
        _serverLimit.Value = Math.Clamp(_config.MaxListingsPerServer, 1, 500);
        _serverDelay.Value = Math.Clamp(_config.MinimumServerSeconds, 21, 3600);
        _host.Text = _config.AllowedHost;
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

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiTheme.Canvas };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(outer);
        outer.BringToFront();
        outer.Controls.Add(BuildToolbar(), 0, 0);
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
            SplitterWidth = 8,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Border
        };
        bool logSplitInitialized = false;
        contentSplit.SizeChanged += (_, _) =>
        {
            if (logSplitInitialized || contentSplit.Height < 370) return;
            // Apply minimum sizes only after layout allocated enough space.
            contentSplit.SplitterDistance = contentSplit.Height - contentSplit.SplitterWidth - 155;
            contentSplit.Panel1MinSize = 200;
            contentSplit.Panel2MinSize = 70;
            logSplitInitialized = true;
        };
        contentSplit.Panel1.Controls.Add(_editorTabs);
        outer.Controls.Add(contentSplit, 0, 1);
        var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        logPanel.Controls.Add(_log);
        logPanel.Controls.Add(new Label
        {
            Text = "실행 기록   ·   위 경계선을 드래그하여 높이 조절",
            Dock = DockStyle.Top, Height = 27, Padding = new Padding(12, 5, 0, 0),
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Muted
        });
        contentSplit.Panel2.Controls.Add(logPanel);

        BuildAutoRegistrationTab();
        BuildJobTab();
        BuildWorkflowTab();
        ConfigureJobsGrid();
        ConfigureAutoGrid();
        InitializeBatchContextMenus();
        ConfigureStepsGrid();
        _jobsGrid.EnableHeadersVisualStyles = false;
        _jobsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(219, 225, 232);
        _jobsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(244, 246, 250);
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
            _ = new ItemManiaAutoLogin(_web, message => AppendLog(message));
            ItemManiaPopupHandler.InstallSilent(_web, message => AppendLog(message));
        };
        Shown += async (_, _) =>
        {
            try
            {
                await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync());
                if (_web.CoreWebView2 is not null)
                {
                    _web.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                    _web.CoreWebView2.Navigate("https://www.itemmania.com/");
                }
            }
            catch (Exception ex) { AppendLog("내부 사이트 엔진 시작 실패: " + ex.Message); }
            AppendLog("V22: 자동 업데이트 준비 · 자동 헤더 일괄선택 · 조회 취소 후 실행");
            int pending = _jobs.Count(j => j.RequiresReview);
            if (pending > 0)
            {
                AppendLog($"주의: 결과 확인 대기 {pending}건. 사이트에서 실제 등록 여부 확인 후 보류 해제하세요.");
                _state.Text = $"확인 필요 {pending}건";
                _state.ForeColor = Color.Firebrick;
            }
        };
        _timer.Tick += async (_, _) => await SchedulerTickAsync();
        FormClosing += (_, _) => { _timer.Stop(); _currentRun?.Cancel(); _schedulerRunning = false; try { SaveAll(); } catch { } };
    }

    private Control BuildToolbar()
    {
        var top = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 46, WrapContents = false, AutoScroll = true,
            Padding = new Padding(11, 7, 4, 2), BackColor = UiTheme.Surface
        };
        void Btn(string label, EventHandler action, int width,
            UiTheme.ButtonTone tone = UiTheme.ButtonTone.Neutral)
        {
            var b = new Button
            {
                Text = label, Width = width, Height = 31,
                Margin = new Padding(2, 0, 5, 0)
            };
            UiTheme.StyleButton(b, tone);
            b.Click += action;
            actions.Controls.Add(b);
        }
        Btn("내 등록글 불러오기", async (_, _) => await ImportMyRegisteredListingsAsync(), 158, UiTheme.ButtonTone.Primary);
        Btn("1건 실제 실행", async (_, _) => await RunSelectedAsync(false), 125, UiTheme.ButtonTone.Primary);
        Btn("시험 실행", async (_, _) => await RunSelectedAsync(true), 96);
        actions.Controls.Add(new Panel
        {
            Width = 1, Height = 23, BackColor = UiTheme.Border,
            Margin = new Padding(10, 4, 10, 0)
        });
        Btn("자동 시작", (_, _) => StartScheduler(), 95, UiTheme.ButtonTone.Success);
        Btn("일시정지", (_, _) => PauseScheduler(), 91);
        Btn("즉시 중지", (_, _) => StopScheduler(), 93, UiTheme.ButtonTone.Danger);
        actions.Controls.Add(new Label
        {
            Text = "현재 상태", AutoSize = true, ForeColor = UiTheme.Muted,
            Padding = new Padding(13, 8, 4, 0)
        });
        _state.BackColor = UiTheme.WarningSurface;
        _state.Padding = new Padding(9, 7, 9, 6);
        actions.Controls.Add(_state);
        top.Controls.Add(actions);

        var limits = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 42, WrapContents = false, AutoScroll = true,
            Padding = new Padding(11, 6, 0, 0), BackColor = UiTheme.SurfaceAlt
        };
        limits.Controls.Add(new Label
        {
            Text = "실행 조건", AutoSize = true,
            Font = new Font(Font, FontStyle.Bold), ForeColor = UiTheme.Text,
            Padding = new Padding(2, 5, 12, 0)
        });
        limits.Controls.Add(_livePosting);
        AddInline(limits, "시간당", _hourLimit);
        AddInline(limits, "서버당", _serverLimit);
        AddInline(limits, "서버 간격(초)", _serverDelay);
        AddInline(limits, "사이트", _host);
        var save = new Button { Text = "조건 저장", Width = 101, Height = 28, Margin = new Padding(8, 0, 4, 0) };
        UiTheme.StyleButton(save);
        save.Click += (_, _) => SaveAll();
        limits.Controls.Add(save);
        top.Controls.Add(limits);
        return top;
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

    // V13: original-style search bar. Search applies ONLY to local listing rows,
    // not to browser navigation, stored credentials, or the scheduler queue.
    private Control BuildSearchFilters()
    {
        var search = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = UiTheme.SurfaceAlt };
        var first = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(10, 4, 0, 1), WrapContents = false, AutoScroll = true };
        var second = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(10, 4, 0, 1), WrapContents = false, AutoScroll = true };
        var third = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 33, Padding = new Padding(12, 3, 0, 0), WrapContents = false, AutoScroll = true };
        static Label Tag(string title) => new()
        {
            Text = title, AutoSize = true, Height = 27, Padding = new Padding(5, 5, 4, 3),
            ForeColor = UiTheme.Muted, BackColor = UiTheme.SurfaceAlt,
            Margin = new Padding(2, 2, 2, 2)
        };
        void Add(FlowLayoutPanel line, string caption, Control ctrl)
        {
            line.Controls.Add(Tag(caption));
            ctrl.Margin = new Padding(0, 2, 6, 2); line.Controls.Add(ctrl);
        }
        Add(first, "서브 ID", _fSub);
        Add(first, "사이트", _fSite);
        Add(first, "사이트 ID", _fSiteId);
        Add(first, "판매/구매", _fOperation);
        Add(second, "게임", _fGame);
        Add(second, "서버", _fServer);
        Add(second, "물품구분", _fCategory);
        Add(second, "물품명", _fName);
        Add(second, "유료등록", _fPaid);
        var reset = new Button { Text = "조건 초기화", Width = 102, Height = 27 };
        UiTheme.StyleButton(reset);
        reset.Click += (_, _) => { ResetFiltersToAll(); RefreshFilteredLists(); };
        third.Controls.Add(new Label
        {
            Text = "검색·필터", AutoSize = true, Font = new Font(Font, FontStyle.Bold),
            ForeColor = UiTheme.Text, Padding = new Padding(2, 5, 11, 0)
        });
        third.Controls.Add(reset);
        _filterSummary.Margin = new Padding(13, 6, 0, 0);
        third.Controls.Add(_filterSummary);
        // Dock ordering: last-added Top control is highest.
        search.Controls.Add(third);
        search.Controls.Add(second);
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
        _filteredJobs = _jobs.Where(MatchesFilter).ToList();
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
    }

    private void RefreshBatchSummary()
    {
        if (!_filterReady) return;
        int marked = _filteredJobs.Count(j => j.SelectedForBulk);
        _filterSummary.Text = $"조회 {_filteredJobs.Count}/{_jobs.Count}건  |  체크 선택 {marked}건";
        if (_batchCountLabel is not null && !_batchCountLabel.IsDisposed)
            _batchCountLabel.Text = $"선택 {marked}건  /  조회 {_filteredJobs.Count}건";
    }

    private void SetVisibleBatchSelection(bool selected)
    {
        _jobsGrid.EndEdit(); _autoGrid.EndEdit();
        foreach (var job in _filteredJobs) job.SelectedForBulk = selected;
        _jobsGrid.Refresh(); _autoGrid.Refresh(); RefreshBatchSummary();
        RefreshSelectAllHeaderVisuals();
        AppendLog($"조회 결과 {_filteredJobs.Count}건 {(selected ? "전체 선택" : "선택 해제")}");
    }

    private void SetBatchEnabled(bool enabled)
    {
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
    private void BuildAutoRegistrationTab()
    {
        _editorTabs.TabPages.Add(_autoTab);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 45, WrapContents = false, AutoScroll = true,
            BackColor = UiTheme.Surface, Padding = new Padding(10, 7, 0, 0)
        };
        void MakeButton(string name, int width, Action click)
        {
            var b = new Button
            {
                Text = name, Width = width, Height = 30, Margin = new Padding(4, 0, 4, 0),
                Cursor = Cursors.Hand
            };
            b.Click += (_, _) => click();
            buttons.Controls.Add(b);
        }
        MakeButton("자동등록 시작", 123, StartScheduler);
        MakeButton("일시정지", 88, PauseScheduler);
        MakeButton("중지", 72, StopScheduler);
        MakeButton("선택 물품 편집", 125, EditAutoSelectedJob);
        MakeButton("물품 추가", 92, () => { AddJob(); ShowItemRegistrationView(); RestoreEditorPanel(); });
        MakeButton("조회 전체선택", 120, () => SetVisibleBatchSelection(true));
        MakeButton("체크 물품 자동 ON", 148, () => SetBatchEnabled(true));
        MakeButton("체크 물품 자동 OFF", 149, () => SetBatchEnabled(false));
        MakeButton("선택 해제", 91, () => SetVisibleBatchSelection(false));
        MakeButton("선택 물품 일괄수정", 150, BulkEditSelected);
        MakeButton("조회 조건", 91, ShowItemRegistrationView);
        MakeButton("설정 저장", 93, SaveAll);
        var help = new Label
        {
            Text = "표 맨 위 [☐ 전체] 클릭 → 조회된 물품 일괄선택/해제  |  체크 물품 자동 ON/OFF  |  실제 사이트 제출은 별도 허용이 필요합니다.",
            Dock = DockStyle.Top, Height = 29,
            Padding = new Padding(12, 6, 0, 0), ForeColor = UiTheme.Muted,
            BackColor = UiTheme.SurfaceAlt
        };
        _autoGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditAutoSelectedJob(); };
        _autoTab.Controls.Add(_autoGrid);
        _autoTab.Controls.Add(help);
        _autoTab.Controls.Add(_autoSummary);
        _autoTab.Controls.Add(buttons);
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
        _autoSummary.Text = $"총 {_jobs.Count}개   |   자동 실행 {enabled}개   |   확인 필요 {pending}개   |   작업 상태: {(_schedulerRunning ? "진행 중" : "중지/대기")}";
    }

    private void EditAutoSelectedJob()
    {
        if (_autoGrid.CurrentRow?.DataBoundItem is not ListingJob job)
        {
            MessageBox.Show(this, "편집할 물품을 먼저 선택하세요.", "물품 선택");
            return;
        }
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
        // Permanent two-row toolbar. The main batch edit command must not be hidden
        // off-screen by a single long, horizontally scrolling toolbar.
        var toolsPanel = new Panel { Dock = DockStyle.Top, Height = 79, BackColor = UiTheme.Surface };
        var primary = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, AutoScroll = true,
            WrapContents = false, Padding = new Padding(10, 5, 0, 0), BackColor = UiTheme.Surface };
        var secondary = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, AutoScroll = true,
            WrapContents = false, Padding = new Padding(10, 3, 0, 0), BackColor = UiTheme.Surface };
        Button ActionButton(string caption, int width, Action run, bool primaryAction = false)
        {
            var button = new Button { Text = caption, Width = width, Height = 30,
                Margin = new Padding(2, 0, 7, 0) };
            UiTheme.StyleButton(button, primaryAction ? UiTheme.ButtonTone.Primary : UiTheme.ButtonTone.Neutral);
            button.Click += (_, _) => run();
            return button;
        }
        primary.Controls.Add(ActionButton("선택 물품 일괄수정", 160, BulkEditSelected, true));
        primary.Controls.Add(ActionButton("조회 전체선택", 116, () => SetVisibleBatchSelection(true)));
        primary.Controls.Add(ActionButton("선택 해제", 90, () => SetVisibleBatchSelection(false)));
        primary.Controls.Add(ActionButton("표 넓게 보기", 116, ToggleWideGrid));
        _batchCountLabel = new Label { AutoSize = true, ForeColor = UiTheme.Primary,
            Font = new Font(Font, FontStyle.Bold), Padding = new Padding(7, 8, 0, 0) };
        primary.Controls.Add(_batchCountLabel);
        secondary.Controls.Add(ActionButton("내 등록글 불러오기", 145, () => _ = ImportMyRegisteredListingsAsync()));
        secondary.Controls.Add(ActionButton("자동 ON", 90, () => SetBatchEnabled(true)));
        secondary.Controls.Add(ActionButton("자동 OFF", 90, () => SetBatchEnabled(false)));
        secondary.Controls.Add(ActionButton("열 너비 초기화", 114, ResetGridColumns));
        secondary.Controls.Add(ActionButton("CSV 가져오기", 104, ImportJobsCsv));
        secondary.Controls.Add(ActionButton("CSV 내보내기", 104, ExportJobsCsv));
        toolsPanel.Controls.Add(secondary);
        toolsPanel.Controls.Add(primary);
        split.Panel1.Controls.Add(toolsPanel);
        split.Panel1.Controls.Add(BuildSearchFilters());
        split.Panel1.Controls.Add(new Label
        {
            Text = "조회한 물품은 표 맨 위 [전체]에서 한 번에 선택  ·  여러 물품의 가격/주기 변경은 [선택 물품 일괄수정]",
            Dock = DockStyle.Top, Height = 28, ForeColor = UiTheme.Muted,
            BackColor = UiTheme.Surface, Padding = new Padding(12, 6, 0, 0)
        });

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
        Field("서브 ID", _subId, 33);
        Field("사이트 아이디", _siteId, 33);
        Field("유료 등록", _paidRegistration, 33);
        Field("사이트", new Label { Text = "아이템매니아 (매니아)", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 32);
        Field("게임", _game, 33);
        Field("서버", _server, 33);
        Field("물품구분", _itemCategory, 33);
        Field("거래유형", _tradingMethod, 33);
        // No fictitious common 'item info' or arbitrary money-unit dropdown.
        // Instant buying is optional and only applies to buy listings.
        Field("즉시구매(선택)", _instantTransaction, 33);
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

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 80, AutoScroll = true, WrapContents = true, Padding = new Padding(5, 5, 0, 0), BackColor = Color.White };
        void Button(string text, int width, Action action)
        {
            var b = new Button { Text = text, Width = width, Height = 29, Margin = new Padding(3) };
            b.Click += (_, _) => action(); bottom.Controls.Add(b);
        }
        Button("물품 저장", 95, () => { SaveSelectedJob(); RefreshFilterChoices(); RefreshFilteredLists(); });
        Button("서버별 복제", 103, CopySelectedToOtherServers);
        Button("완료 표시 초기화", 126, () =>
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
        Button("사이트 확인 후 보류 해제", 167, () =>
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
        right.Controls.Add(scroll);
        right.Controls.Add(bottom);
        right.Controls.Add(new Label { Text = "물품 설정  ·  원본 양식의 주요 선택 항목", Dock = DockStyle.Top, Height = 27,
            Padding = new Padding(12, 6, 0, 0), BackColor = UiTheme.SurfaceAlt,
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
            "이동: URL 열기 ({EntryUrl}은 물품별 URL 우선)\n입력: CSS 선택자의 입력칸에 값 입력\n클릭: 버튼 클릭\n선택: select 태그 또는 지정한 목록 내부에서 실제 옵션 선택\n체크: 체크박스 선택\n대기: 요소가 나타날 때까지 대기\n멈춤: 밀리초 대기\n제출: 최종 등록 버튼 클릭 (시험 실행에서는 건너뜀)\n\n아이템매니아 웹페이지는 표시하지 않습니다. 기본 필드는 라벨 기반 자동 탐색(@auto:)이며, 일치하는 항목이 없거나 여러 개면 등록을 중단합니다. 실사이트 연결은 별도 확인이 필요합니다.\n\n{Game}, {Server}, {Title}, {Quantity}, {Unit}, {Price}, {Character}, {Description}, {EntryUrl} 변수 지원.\n\n사이트 구조 변화나 추가 인증이 필요하면 실제 제출이 실패할 수 있습니다.",
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
        using var dialog = new ItemManiaAccountForm();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (_web.CoreWebView2 is not null) _web.CoreWebView2.Navigate("https://www.itemmania.com/");
        AppendLog("아이템매니아 계정 저장 완료. 백그라운드에서 로그인을 시도합니다.");
    }

    private void ConfigureJobsGrid()
    {
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "BatchSelected", HeaderText = "선택", DataPropertyName = "SelectedForBulk", Width = 47 });
        _jobsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "자동", DataPropertyName = "Enabled", Width = 45 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "구분", DataPropertyName = "Operation", Width = 83, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "서브 ID", DataPropertyName = "SubId", Width = 95, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "사이트 ID", DataPropertyName = "SiteId", Width = 92, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "등록번호", DataPropertyName = "ListingNumber", Width = 129, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "원래 거래", DataPropertyName = "OriginalTradeSide", Width = 69, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "게임", DataPropertyName = "Game", Width = 100, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "서버", DataPropertyName = "Server", Width = 100, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "물품구분", DataPropertyName = "ItemCategory", Width = 92, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "수량", DataPropertyName = "Quantity", Width = 68, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "단위", DataPropertyName = "Unit", Width = 65, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "가격(원)", DataPropertyName = "Price", Width = 112, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "거래유형", DataPropertyName = "TradingMethod", Width = 83, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "최소", DataPropertyName = "MinQuantity", Width = 70, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "최대", DataPropertyName = "MaxQuantity", Width = 70, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "재등록(분)", DataPropertyName = "IntervalMinutes", Width = 86, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상품/제목", DataPropertyName = "Title", MinimumWidth = 140, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = 385 });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "캐릭터", DataPropertyName = "Character", Width = 98, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상세설명", DataPropertyName = "Description", Width = 250, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "다음 예정", DataPropertyName = "NextDueLocal", Width = 115, ReadOnly = true });
        _jobsGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상태", DataPropertyName = "LastStatus", Width = 90, ReadOnly = true });
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
        var accountId = ItemManiaAccountStore.Load().UserId;
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
        _selected.Game = _game.SelectedItem?.ToString() ?? "";
        _selected.Server = _server.SelectedItem?.ToString() ?? "";
        _selected.Title = _title.Text.Trim(); _selected.Quantity = _quantity.Text.Trim(); _selected.Unit = _unit.Text.Trim();
        _selected.Price = _price.Text.Trim(); _selected.Character = _character.Text.Trim();
        _selected.Description = _description.Text; _selected.EntryUrl = _entry.Text.Trim();
        _selected.IntervalMinutes = (int)_interval.Value;
        _selected.AutoRepost = _autoRepost.Checked;
        _selected.Enabled = _jobEnabled.Checked && !_selected.RequiresReview;
        _config.Jobs = _jobs.ToList(); _config.Save(); _jobsGrid.Refresh();
        if (notify) AppendLog("물품 설정 저장: " + _selected.Title);
    }

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
        _config.MaxListingsPerServer = (int)_serverLimit.Value;
        _config.MinimumServerSeconds = (int)_serverDelay.Value;
        _config.MinimumGlobalSeconds = 21;
        _config.AllowedHost = _host.Text.Trim().ToLowerInvariant();
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
                "자동으로 연결한 입력칸:\n" + string.Join("\n", added) + "\n\n실제 동작을 시험 실행으로 확인해 주세요.";
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

    // A repost may target either a listing-specific detail URL or its verified
    // OWN registration-list page. A shared list is never treated like a detail.
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
        MyListingsImport.IsSafeDetailUrl(job.EntryUrl, job.ListingNumber) ? job.EntryUrl.Trim() :
        !string.IsNullOrWhiteSpace(OwnedListUrlFor(job)) ? OwnedListUrlFor(job) :
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
            if (!_livePosting.Checked) { message = "실제 제출 허용에 체크해야 합니다."; return false; }
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
        if (!dryRun && MessageBox.Show(this,
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
        SaveAll();
        if (_schedulerRunning) return;
        if (_busy || _pendingMyListingsImport)
        {
            MessageBox.Show(this, "현재 작업 또는 기존 등록글 불러오기가 완료된 후 자동 시작할 수 있습니다.", "자동등록 대기");
            return;
        }
        if (!_livePosting.Checked) { MessageBox.Show(this, "자동 등록은 '실제 제출 허용'을 체크해야 시작할 수 있습니다."); return; }
        var enabled = _jobs.Where(WantsWork).ToList();
        if (enabled.Count == 0) { MessageBox.Show(this, "자동 실행이 체크된 미완료 물품 또는 재등록 물품이 없습니다."); return; }
        foreach (var job in enabled)
            if (!ValidateJob(job, false, out var error)) { MessageBox.Show(this, job.Title + ": " + error, "자동 시작 불가"); return; }
        if (MessageBox.Show(this, $"{enabled.Count}개의 등록/재등록 작업을 자동 제출합니다.\n" +
            $"시간당 최대 {_config.MaxSubmissionsPerHour}회, 같은 서버 최소 {_config.MinimumServerSeconds}초\n\n" +
            "사이트 이용약관과 게시 제한을 준수해야 하며 CAPTCHA/본인인증/유료 결제는 자동 처리하지 않습니다.\n자동 실행을 시작할까요?",
            "자동 등록 시작", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _schedulerRunning = true; _timer.Start();
        _state.Text = "자동 실행 중"; _state.ForeColor = Color.ForestGreen; RefreshAutoSummary();
        AppendLog("스케줄러 시작: " + enabled.Count + "건, 시간당 " + _config.MaxSubmissionsPerHour + "회 제한");
    }

    private void PauseScheduler()
    {
        _schedulerRunning = false; _timer.Stop();
        _state.Text = "일시정지"; _state.ForeColor = Color.DarkGoldenrod; RefreshAutoSummary();
        AppendLog(_myListingsImportRunning ? "자동등록은 일시정지했습니다. '내 등록글 불러오기'는 계속 조회 중입니다. 중단하려면 [즉시 중지]를 누르세요."
            : _busy ? "일시정지. 진행 중인 작업 1건은 종료까지 기다립니다. 목록 불러오기를 누르면 종료 후 자동 조회합니다."
            : "일시정지. 진행 중인 작업은 없습니다. 기존 등록글을 불러올 수 있습니다.");
    }

    private void StopScheduler()
    {
        _schedulerRunning = false; _timer.Stop(); _currentRun?.Cancel();
        _pendingMyListingsImport = false;
        if (_myListingsImportRunning)
        {
            _myListingsImportCancellation?.Cancel();
            try { _web.CoreWebView2?.Stop(); }
            catch (Exception ex) { AppendLog("조회 페이지 중단 대기: " + ex.GetType().Name); }
            AppendLog("내 등록글 조회 취소 요청: 현재 페이지 이동이 끝나는 대로 중단합니다.");
        }
        _state.Text = "중지됨"; _state.ForeColor = Color.Maroon; RefreshAutoSummary();
        AppendLog("중지 요청. 실제 제출 중이었다면 사이트에서 등록 여부를 확인하세요.");
    }

    private bool CanRunNow(ListingJob job, out string reason)
    {
        var now = DateTime.UtcNow;
        _config.SubmissionAttemptsUtc.RemoveAll(x => x < now.AddHours(-1));
        int hourlyLimit = Math.Clamp(_config.MaxSubmissionsPerHour, 1, 1000);
        int serverDelay = Math.Max(21, _config.MinimumServerSeconds);
        if (_config.SubmissionAttemptsUtc.Count >= hourlyLimit)
        {
            reason = "최근 1시간 등록 시도 횟수가 상한에 도달했습니다."; return false;
        }
        var last = _config.SubmissionAttemptsUtc.Count == 0 ? DateTime.MinValue : _config.SubmissionAttemptsUtc.Max();
        int pace = Math.Max(Math.Max(21, _config.MinimumGlobalSeconds), (int)Math.Ceiling(3600.0 / hourlyLimit));
        var globalAllowed = last == DateTime.MinValue ? DateTime.MinValue : last.AddSeconds(pace);
        globalAllowed = globalAllowed > _nextGlobalAllowedUtc ? globalAllowed : _nextGlobalAllowedUtc;
        if (now < globalAllowed)
        {
            reason = $"전체 등록 간격 제한: {Math.Ceiling((globalAllowed - now).TotalSeconds)}초 남음"; return false;
        }
        if (_config.LastServerAttemptUtc.TryGetValue(job.Server, out var lastServer) && now < lastServer.AddSeconds(serverDelay))
        {
            reason = $"{job.Server} 서버 최소 간격: {Math.Ceiling((lastServer.AddSeconds(serverDelay) - now).TotalSeconds)}초 남음"; return false;
        }
        reason = ""; return true;
    }

    private async Task SchedulerTickAsync()
    {
        if (_busy || !_schedulerRunning) return;
        var jobs = _jobs.Where(x => WantsWork(x) && x.NextDueUtc <= DateTime.UtcNow)
                        .OrderBy(x => x.NextDueUtc).ThenBy(x => x.LastSucceededUtc).ToList();
        foreach (var job in jobs)
        {
            if (!CanRunNow(job, out _)) continue;
            await ExecuteJobAsync(job, false);
            break;
        }
        _jobsGrid.Refresh();
    }

    private async Task ExecuteJobAsync(ListingJob job, bool dryRun)
    {
        if (_busy) { AppendLog("이미 다른 물품 작업이 진행 중입니다."); return; }
        if (!ValidateJob(job, dryRun, out var error)) { AppendLog("실행 거절: " + error); return; }
        _busy = true;
        _currentRun?.Dispose(); _currentRun = new CancellationTokenSource();
        var token = _currentRun.Token;
        bool submitted = false;
        string preSubmitUrl = "";
        bool successSelectorBefore = false;
        string preSubmitListingNumber = "";
        bool repostFromOwnedList = false;
        job.LastStatus = "실행 중"; _jobsGrid.Refresh();
        _state.Text = dryRun ? "시험 실행 중" : "등록 실행 중"; _state.ForeColor = Color.DarkBlue;
        var flow = _config.WorkflowFor(EffectiveOperation(job));
        AppendLog($"작업 시작: [{flow.Operation}] {job.Server} / {job.Title}" + (dryRun ? " (미제출 테스트)" : ""));
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync());
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
                        preSubmitListingNumber = (await InspectCurrentIdentityAsync(job, token)).Number;
                        successSelectorBefore = await SuccessSelectorVisibleAsync(flow.SuccessSelector);
                        bool urlAlreadyMatches = !string.IsNullOrWhiteSpace(flow.SuccessUrlContains) &&
                            preSubmitUrl.Contains(flow.SuccessUrlContains, StringComparison.OrdinalIgnoreCase);
                        if (urlAlreadyMatches && (string.IsNullOrWhiteSpace(flow.SuccessSelector) || successSelectorBefore))
                            throw new InvalidOperationException("제출 전에 성공 확인 조건이 이미 충족되어 있습니다. 등록 완료 화면 전용 CSS 또는 URL을 설정하세요.");
                        _config.SubmissionAttemptsUtc.Add(now);
                        _config.LastServerAttemptUtc[job.Server] = now;
                        int pace = Math.Max(Math.Max(21, _config.MinimumGlobalSeconds),
                            (int)Math.Ceiling(3600.0 / Math.Clamp(_config.MaxSubmissionsPerHour, 1, 1000)));
                        _nextGlobalAllowedUtc = now.AddSeconds(pace);
                        // IMPORTANT: checkpoint to disk BEFORE touching the submit button.
                        // Process termination during a live submission must never silently
                        // retry the listing on the next launch.
                        job.RequiresReview = true;
                        job.LastStatus = "제출 중: 결과 확인 필요";
                        job.NextDueUtc = DateTime.MaxValue;
                        _config.Jobs = _jobs.ToList();
                        _config.Save();
                        submitted = true;
                    }
                }
                AppendLog($"  {i + 1}/{flow.Steps.Count} {step.Action} " + (step.Action == "입력" ? "(내용 입력)" : step.Selector));
                if (step.Action == "제출" && repostFromOwnedList)
                {
                    var result = await InspectOwnedListRepostAsync(job, true, token);
                    if (result.Status != "clicked")
                        throw new InvalidOperationException("해당 등록번호의 행 재등록 클릭 실패: " + result.Message);
                    AppendLog("본인 물품 행의 재등록 버튼 클릭: " + job.ListingNumber);
                    await Task.Delay(650, token);
                }
                else await ExecuteStepAsync(step, job, flow, token);
                if (step.Action == "제출" && !dryRun)
                    await ConfirmExactRegistrationDialogAsync(token);
            }
            if (dryRun)
            {
                job.LastStatus = "시험 완료 (미제출)";
                AppendLog("시험 완료. 실제 제출 여부는 사이트 폼을 확인하세요.");
            }
            else
            {
                if (!submitted) throw new InvalidOperationException("제출 버튼 단계가 실행되지 않았습니다.");
                bool verified = await VerifySuccessAsync(flow, job, preSubmitListingNumber, preSubmitUrl, successSelectorBefore, token);
                if (!verified) throw new InvalidOperationException("제출 이후 성공 확인 조건을 충족하지 못했습니다. 중복 등록을 피하려면 사이트에서 먼저 물품 상태를 확인하세요.");
                // An apparent success message alone is not enough to establish
                // which item was posted. Read a unique, site-owned listing ID.
                var identity = await InspectCurrentIdentityAsync(job, token);
                bool identityOk = flow.Operation == "재등록" ?
                    ListingIdentity.IsConfirmedExisting(identity, job) :
                    ListingIdentity.IsConfirmedNew(identity, job);
                // A My Room LIST intentionally contains many registration IDs.
                // It is not a detail page, so confirm the exact row again only
                // AFTER a new success signal was verified above.
                if (!identityOk && repostFromOwnedList &&
                    MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri))
                {
                    var ownRowAfter = await InspectOwnedListRepostAsync(job, null, token);
                    identityOk = ownRowAfter.Status == "found";
                    if (identityOk) AppendLog("재등록 성공 신호 및 대상 등록번호 행 재확인: " + job.ListingNumber);
                }
                if (!identityOk)
                    throw new InvalidOperationException("등록 완료 표시를 찾았지만 해당 글의 등록번호를 확정하지 못했습니다. " +
                        identity.Error + " / 중복 등록을 막기 위해 사이트 확인 전까지 자동 실행을 보류합니다.");
                if (flow.Operation != "재등록") job.ListingNumber = identity.Number;
                job.InitialCompleted = true; job.RequiresReview = false;
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
                            job.NextDueUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, job.IntervalMinutes));
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
            }
        }
        catch (OperationCanceledException)
        {
            job.LastStatus = submitted ? "중지됨 (사이트 확인 필요)" : "중지됨";
            if (submitted) job.RequiresReview = true;
            job.NextDueUtc = DateTime.UtcNow.AddMinutes(10);
            AppendLog("작업 중지: " + job.Title + (submitted ? " / 제출이 진행되었을 수 있으니 중복 등록 전에 반드시 확인" : ""));
        }
        catch (Exception ex)
        {
            job.LastStatus = submitted ? "제출 결과 확인 필요" : "오류 (미제출)";
            if (submitted) job.RequiresReview = true;
            job.NextDueUtc = DateTime.UtcNow.AddMinutes(10);
            AppendLog("작업 오류: " + ex.Message);
            if (_schedulerRunning && submitted)
            {
                PauseScheduler();
                MessageBox.Show(this, "사이트 제출 후 결과가 불명확합니다. 중복 게시를 막기 위해 자동 실행을 일시정지했습니다.\n\n" + ex.Message, "결과 확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _busy = false;
            _jobsGrid.Refresh();
            _config.Save();
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
            await NavigateAsync(ownListUrl, token);
        return await LocateOwnedListRepostAsync(job, token);
    }

    private async Task<OwnedListRepostResult> LocateOwnedListRepostAsync(ListingJob job, CancellationToken token)
    {
        string current = _web.Source?.AbsoluteUri ?? "";
        if (!MyListingsImport.IsOwnedListUrl(current))
            return new OwnedListRepostResult { Status = "error", Message = "등록글 관리 목록을 열지 못했습니다" };
        bool selling = MyListingsImport.IsSellListUrl(current);
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(current);
        string baseUrl = selling ? MyListingsImport.SellListingsUrl : MyListingsImport.BuyListingsUrl;
        if (!current.Equals(baseUrl, StringComparison.OrdinalIgnoreCase)) queue.Enqueue(baseUrl);
        const int maxPages = 25;
        while (queue.Count > 0 && visited.Count < maxPages)
        {
            token.ThrowIfCancellationRequested();
            string next = queue.Dequeue();
            if (!MyListingsImport.IsOwnedListUrl(next) || MyListingsImport.IsSellListUrl(next) != selling ||
                !visited.Add(next)) continue;
            if (!string.Equals(_web.Source?.AbsoluteUri, next, StringComparison.OrdinalIgnoreCase))
                await NavigateAsync(next, token);
            var row = await InspectOwnedListRepostAsync(job, false, token);
            if (row.Status == "ready")
            {
                if (!string.Equals(job.SourceListUrl, next, StringComparison.OrdinalIgnoreCase))
                {
                    job.SourceListUrl = next;
                    _config.Save();
                }
                if (visited.Count > 1) AppendLog($"재등록 물품을 관리 목록 {visited.Count}페이지 검사 후 찾았습니다: " + job.ListingNumber);
                return row;
            }
            if (row.Status != "not-found") return row; // Ambiguous button/ID must never trigger a different row.
            try
            {
                string json = await _web.CoreWebView2.ExecuteScriptAsync(MyListingsImport.ExtractPageScript);
                var page = MyListingsImport.DecodePage(json);
                foreach (string link in page.NextPageUrls.Take(30))
                    if (MyListingsImport.IsOwnedListUrl(link) && MyListingsImport.IsSellListUrl(link) == selling &&
                        !visited.Contains(link)) queue.Enqueue(link);
            }
            catch (Exception ex) { AppendLog("관리 목록 페이지 탐색 오류: " + ex.GetType().Name); }
        }
        return new OwnedListRepostResult { Status = "not-found", Message =
            $"해당 등록번호의 행을 관리 목록 {visited.Count}페이지에서 찾지 못했습니다. 글 상태/페이지 구성을 확인하세요" };
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

    // Only the precise no-fee product-registration confirmation is eligible.
    // It never clicks payment, paid promotion, login, human verification, or
    // arbitrary '확인' buttons from the rest of the page.
    private async Task ConfirmExactRegistrationDialogAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAllowedCurrentPage() || _web.CoreWebView2 is null) return;
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
            const words=key==='repost'?['재등록','다시등록']:
              key==='submitSell'?['판매등록']:
              key==='submitBuy'?['구매등록']:
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
            await NavigateAsync(url, token); return;
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
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, result) => completion.TrySetResult(result.IsSuccess);
        _web.NavigationCompleted += handler;
        try
        {
            _web.CoreWebView2.Navigate(url);
            bool success = await completion.Task.WaitAsync(TimeSpan.FromSeconds(35), token);
            if (!success) throw new InvalidOperationException("페이지 이동에 실패했습니다: " + url);
            await Task.Delay(600, token);
            if (!IsAllowedCurrentPage()) throw new InvalidOperationException("로그인/이동 후 다른 호스트로 이동했습니다. 해당 사이트에서 수동으로 인증하세요.");
        }
        finally { _web.NavigationCompleted -= handler; }
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
        string beforeUrl, bool selectorBefore, CancellationToken token)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        do
        {
            token.ThrowIfCancellationRequested();
            if (!IsAllowedCurrentPage()) return false;
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
            await Task.Delay(500, token);
        } while (DateTime.UtcNow < until);
        return false;
    }

    private void AppendLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(message))); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        try
        {
            string directory = System.IO.Path.Combine(Settings.UserDirectory, "logs");
            System.IO.Directory.CreateDirectory(directory);
            string file = System.IO.Path.Combine(directory, "automation_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            System.IO.File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* Never let log I/O interrupt a run. */ }
        if (_log.TextLength > 75000) _log.Text = _log.Text[^50000..];
        _log.SelectionStart = _log.TextLength; _log.ScrollToCaret();
    }
}
