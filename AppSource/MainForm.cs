using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

// V8: main program is an item-list editor, not a visible Internet browser.
// Keep Windows' native sizable frame so every edge/corner supports drag-resize.
internal sealed partial class MainForm : Form
{
    private readonly AutomationForm _moduleA;
    private readonly AutomationForm _moduleB;
    private readonly AutomationForm _moduleC;
    private readonly Panel _workspaceBody = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
    private AutomationForm _module => _activeSystem switch
    {
        "B" => _moduleB, "C" => _moduleC, _ => _moduleA
    };
    private string _activeSystem = "A";
    private Button? _systemPicker;
    private bool _checkingProxyConnection;
    private int _systemSelectionSequence;
    private readonly Label _accountInfo = new() { Dock = DockStyle.Right, Width = 184, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.Muted };
    private bool _systemBShown;
    private bool _systemCShown;
    private AppUpdateSettings _updateSettings = AppUpdateService.LoadSettings();
    private AppUpdateManifest? _pendingUpdate;
    private string? _downloadedUpdate;
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 15000 };
    private bool _checkingUpdate;
    private bool _installingUpdate;
    private DateTime _nextUpdateRetryUtc = DateTime.MinValue;
    private DateTime _lastUpdateCheckUtc = DateTime.MinValue;
    private bool _manualUpdateRequested;
    private readonly Label _status = new()
    {
        AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        Text = "  로컬 목록 준비 · 사이트 페이지는 화면에 표시하지 않습니다."
    };

    public MainForm(ServerInfo server, string username)
    {
        Text = "VANTA | 물품 · 자동등록 관리";
        Icon = new Icon(Settings.Asset("VANTA.ico"));
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 640);
        Size = new Size(1540, 860);
        FormBorderStyle = FormBorderStyle.Sizable; // native corner and edge dragging
        MaximizeBox = true;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Canvas;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 9.5F);

        _moduleA = new AutomationForm(username, "A")
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            MinimumSize = Size.Empty,
            MaximumSize = Size.Empty,
            Dock = DockStyle.Fill
        };
        _moduleB = new AutomationForm(username, "B")
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            MinimumSize = Size.Empty,
            MaximumSize = Size.Empty,
            Dock = DockStyle.Fill
        };
        _moduleC = new AutomationForm(username, "C")
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            MinimumSize = Size.Empty,
            MaximumSize = Size.Empty,
            Dock = DockStyle.Fill
        };
        _moduleB.ShareSiteQuotaWith(_moduleA);
        _moduleC.ShareSiteQuotaWith(_moduleA);
        InitializeMyListingsProgressBanner();

        var top = BuildToolbar();
        var body = _workspaceBody;
        body.Controls.Add(_moduleA);
        body.Controls.Add(_moduleB);
        body.Controls.Add(_moduleC);
        _moduleA.RequestSingleViewFromSplit += ExitSplitForEditor;
        _moduleB.RequestSingleViewFromSplit += ExitSplitForEditor;
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = UiTheme.SurfaceAlt };
        footer.Controls.Add(_status);
        footer.Controls.Add(_accountInfo);
        Controls.Add(body);
        Controls.Add(footer);
        AddSitePriceBatchProgressBanner(_moduleA, "A");
        AddSitePriceBatchProgressBanner(_moduleB, "B");
        AddSitePriceBatchProgressBanner(_moduleC, "C");
        Controls.Add(_importProgressBanner);
        Controls.Add(top);
        StartMarketBridge();

        Shown += (_, _) =>
        {
            _moduleA.Show();
            _moduleA.BringToFront();
            _moduleA.ShowAutomationView();
            RefreshSystemLabel();
            _status.Text = "  자동등록 현황 · 물품등록 탭은 별도 편집 화면으로 전환됩니다.";
            try
            {
                _moduleA.SaveListings(); _moduleB.SaveListings(); _moduleC.SaveListings();
                UserSettingsSnapshots.CreateSnapshot("daily", daily: true);
            }
            catch (Exception ex) { _status.Text = "  일일 설정 백업 보류: " + ex.GetType().Name; }
            AppUpdateService.ReportStartupReady();
            _updateTimer.Start();
            StartTelegramStatusPolling();
            _ = InitializeWindowsBootAsync();
        };
        _updateTimer.Tick += async (_, _) =>
        {
            if (_installingUpdate || _checkingUpdate || IsDisposed || Disposing) return;
            if (_pendingUpdate is not null)
            {
                if ((_updateSettings.Automatic || _manualUpdateRequested) && CanInstallUpdateAcrossSystems &&
                    DateTime.UtcNow >= _nextUpdateRetryUtc)
                    await InstallPendingUpdateAsync(automatic: true);
            }
            else if (_updateSettings.Automatic &&
                !string.IsNullOrWhiteSpace(_updateSettings.FeedUrl) &&
                DateTime.UtcNow - _lastUpdateCheckUtc >= TimeSpan.FromHours(6))
                await CheckForUpdateAsync(automatic: true);
        };
        FormClosing += (_, _) => { CancelBootBRetry(); StopTelegramStatusPolling(); _marketStatusTimer.Stop(); _marketBridge?.Dispose(); _integratedRefreshTimer.Stop(); _splitStatusTimer.Stop(); _importProgressDismiss.Stop(); _importProgressClock.Stop(); _updateTimer.Stop(); _moduleA.StopAndSave(); _moduleB.StopAndSave(); _moduleC.StopAndSave(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Also reapply when Windows recreates this handle after DPI/style
        // changes; the original native resize and caption buttons remain.
        NativeTitleBarTheme.Apply(Handle);
    }

    private bool CanInstallUpdateAcrossSystems =>
        _moduleA.CanInstallSoftwareUpdate && _moduleB.CanInstallSoftwareUpdate && _moduleC.CanInstallSoftwareUpdate;

    private void RefreshSystemLabel()
    {
        if (_systemPicker is not null)
            _systemPicker.Text = _activeSystem == "A" ? "A 기본 ▾" : $"{_activeSystem} VPN ▾";
        _accountInfo.Text = _marketVisible ? "시세 분석 · 읽기 전용   " :
            _integratedVisible ? "A/B/C 통합 물품 편집   " :
            _splitActive ? $"A+B 동시 현황 · 선택 {_activeSystem}   " :
            $"시스템 {_activeSystem} · 계정별 분리   ";
    }

    private async Task SelectSystemAsync(string system)
    {
        if (system is not ("A" or "B" or "C")) return;
        if (system != "A" && _checkingProxyConnection) return;
        int selection = ++_systemSelectionSequence;
        if (system != "A" && !DualSystemNetwork.IsRouteVerified(system))
        {
            // The verified route is process-local; retry silently after updates.
            if (string.IsNullOrWhiteSpace(DualSystemNetwork.Load(system).ProxyUrl))
            {
                OpenSystemNetworkSettings(system);
            }
            else
            {
                _checkingProxyConnection = true;
                _status.Text = $"  시스템 {system} 연결 자동 확인 중 · 기존 A 인터넷은 유지됩니다.";
                try
                {
                    if (system == "B") await DualSystemNetwork.VerifyBRouteAsync();
                    else await DualSystemNetwork.VerifyCRouteAsync();
                }
                catch (Exception ex)
                {
                    if (selection != _systemSelectionSequence || IsDisposed || Disposing) return;
                    _status.Text = $"  {system} 연결 확인 실패 · 다른 시스템은 계속 사용할 수 있습니다.";
                    if (MessageBox.Show(this,
                        $"저장된 {system} 프록시 연결을 확인하지 못했습니다.\n" +
                        (system == "B" ? "Cloudflare WARP 로컬 프록시 연결을 확인하세요.\n" : "C 전용 프록시 서비스가 실행 중인지 확인하세요.\n") +
                        ex.Message + $"\n\n{system} 연결 설정창을 열까요?",
                        $"{system} 연결 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        OpenSystemNetworkSettings(system);
                }
                finally
                {
                    _checkingProxyConnection = false;
                }
            }
            if (selection != _systemSelectionSequence || IsDisposed || Disposing) return;
            if (!DualSystemNetwork.IsRouteVerified(system)) return;
        }
        if (system == "B" && !_systemBShown)
        {
            _moduleB.Show();
            _moduleB.ShowAutomationView();
            _systemBShown = true;
        }
        if (system == "C" && !_systemCShown)
        {
            _moduleC.Show();
            _moduleC.ShowAutomationView();
            _systemCShown = true;
        }
        if (_integratedVisible) DisableIntegratedEditor();
        if (_marketVisible) DisableMarketView();
        if (_splitActive && system is ("A" or "B"))
        {
            SelectSplitSystem(system);
            return;
        }
        if (_splitActive && system == "C") DisableSplitView();
        _activeSystem = system;
        _module.BringToFront();
        RefreshSystemLabel();
        if (_splitActive) RefreshSplitHeaders();
        _status.Text = $"  시스템 {system} 작업 화면 · 다른 시스템의 자동등록은 계속 실행됩니다.";
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 62, AutoScroll = true, WrapContents = false,
            BackColor = UiTheme.Surface, Padding = new Padding(15, 11, 9, 8)
        };
        using var brandIcon = new Icon(Settings.Asset("VANTA.ico"), 32, 32);
        var logo = new PictureBox
        {
            Image = brandIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(32, 32), BackColor = UiTheme.Surface,
            Margin = new Padding(0, 3, 9, 0)
        };
        bar.Controls.Add(logo);
        bar.Controls.Add(new Label
        {
            Text = "VANTA",
            ForeColor = UiTheme.Text, Size = new Size(92, 36),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Padding = new Padding(1, 6, 0, 0), Margin = new Padding(0, 0, 6, 0)
        });
        var systemMenu = new ContextMenuStrip();
        systemMenu.Items.Add("A 기본", null, (_, _) => _ = SelectSystemAsync("A"));
        systemMenu.Items.Add("B VPN", null, (_, _) => _ = SelectSystemAsync("B"));
        systemMenu.Items.Add("C VPN", null, (_, _) => _ = SelectSystemAsync("C"));
        _systemPicker = AddToolButton(bar, "A 기본 ▾",
            () => systemMenu.Show(_systemPicker!, 0, _systemPicker!.Height), 105);
        AddToolbarDivider(bar);
        AddToolButton(bar, "자동 시작", () => _ = StartSelectedSystemsAsync(), 92, UiTheme.ButtonTone.Success);
        AddToolButton(bar, "일시정지", PauseSelectedSystems, 85);
        AddToolButton(bar, "즉시 중지", StopSelectedSystems, 93, UiTheme.ButtonTone.Danger);
        _splitButton = AddToolButton(bar, "스플릿", () => _ = ToggleSplitViewAsync(), 88);
        _integratedButton = AddToolButton(bar, "통합 편집", ToggleIntegratedEditor, 105);
        _marketButton = AddToolButton(bar, "시세 분석", ToggleMarketView, 96);
        var moreMenu = new ContextMenuStrip();
        moreMenu.Items.Add("내 등록글 불러오기", null, async (_, _) =>
        {
            try { await _module.ImportMyRegisteredListingsFromHeaderAsync(); }
            catch (Exception ex)
            {
                UpdateMyListingsProgress(_activeSystem, "조회 시작 실패 · " + ex.GetType().Name,
                    MyListingsImportPhase.Failed);
            }
        });
        moreMenu.Items.Add("체크한 구매글 가격 일괄 반영", null,
            async (_, _) => await _module.ApplyCheckedSitePricesFromHeaderAsync());
        moreMenu.Items.Add("선택 1건 가격 즉시 수정", null,
            async (_, _) => await _module.ApplySelectedSitePriceFromHeaderAsync());
        moreMenu.Items.Add("가격 수정 예약 현황", null, (_, _) => ShowPriceReservationsDialog());
        moreMenu.Items.Add(new ToolStripSeparator());
        var listingMenu = new ToolStripMenuItem("물품 관리");
        listingMenu.DropDownItems.Add("판매글 추가", null, (_, _) => AddNewListing("판매등록"));
        listingMenu.DropDownItems.Add("구매글 추가", null, (_, _) => AddNewListing("구매등록"));
        listingMenu.DropDownItems.Add("재등록 추가", null, (_, _) => AddNewListing("재등록"));
        listingMenu.DropDownItems.Add(new ToolStripSeparator());
        listingMenu.DropDownItems.Add("물품 복제", null, (_, _) => _module.DuplicateListing());
        listingMenu.DropDownItems.Add("VANTA 목록에서만 삭제", null, (_, _) => _module.RemoveListing());
        listingMenu.DropDownItems.Add("아이템매니아 등록글 삭제", null,
            async (_, _) => await _module.DeleteSiteListingAsync());
        listingMenu.DropDownItems.Add("설정 저장", null, (_, _) => _module.SaveListings());
        listingMenu.DropDownItems.Add("로컬 목록 새로고침", null, (_, _) => _module.RefreshListings());
        moreMenu.Items.Add(listingMenu);

        var settingsMenu = new ToolStripMenuItem("설정");
        settingsMenu.DropDownItems.Add("아이템매니아 계정", null, (_, _) => _module.OpenAccountSettings());
        settingsMenu.DropDownItems.Add("시간당 등록 횟수 설정", null, (_, _) => _module.OpenHourlyLimitSettings());
        settingsMenu.DropDownItems.Add("고급 설정", null, (_, _) => _module.ShowWorkflowSettings());
        settingsMenu.DropDownItems.Add("텔레그램 알림 설정", null, (_, _) => _module.OpenTelegramSettings());
        settingsMenu.DropDownItems.Add("B VPN 연결 설정", null, (_, _) => OpenSystemNetworkSettings("B"));
        settingsMenu.DropDownItems.Add("C VPN 연결 설정", null, (_, _) => OpenSystemNetworkSettings("C"));
        settingsMenu.DropDownItems.Add(new ToolStripSeparator());
        settingsMenu.DropDownItems.Add(BuildWindowsStartupMenu());
        settingsMenu.DropDownItems.Add(BuildAutoStartTargetsMenu());
        settingsMenu.DropDownItems.Add("업데이트 주소 · 자동설치 설정", null, (_, _) => OpenUpdateSettingsDialog());
        settingsMenu.DropDownItems.Add("설정 백업·복원", null, (_, _) => OpenSettingsBackupDialog());
        moreMenu.Items.Add(settingsMenu);

        var diagnosticsMenu = new ToolStripMenuItem("진단·도움말");
        diagnosticsMenu.DropDownItems.Add("선택 1건 조회시험", null,
            async (_, _) => await _module.ProbeSelectedRepostFromHeaderAsync());
        diagnosticsMenu.DropDownItems.Add("신규글 입력시험 (미제출)", null,
            async (_, _) => await _module.TestSelectedNewRegistrationFromHeaderAsync());
        diagnosticsMenu.DropDownItems.Add("사이트 가격 수정폼 분석·복사 (제출 없음)", null,
            async (_, _) => await _module.InspectSelectedPriceSubmitFromHeaderAsync());
        var traceMenuItem = new ToolStripMenuItem("수정 요청 기록");
        traceMenuItem.Click += async (_, _) => await _module.ToggleSiteTraceFromHeaderAsync(traceMenuItem);
        diagnosticsMenu.DropDownItems.Add(traceMenuItem);
        moreMenu.Opening += (_, _) => traceMenuItem.Text = _module.SiteTraceIsActive ? "기록 종료·복사" : "수정 요청 기록";
        diagnosticsMenu.DropDownItems.Add("도움말", null, (_, _) => MessageBox.Show(this,
            "모서리·테두리를 끌어서 크기를 자유롭게 바꿀 수 있습니다.\n" +
            "사이트 화면은 표시하지 않으며 내부 브라우저에서만 작업합니다.\n" +
            "자동등록 목록, 물품등록 편집, 일괄 CSV 관리, 고급 작업 설정을 사용할 수 있습니다.\n" +
            "보안 확인이나 추가 인증이 필요한 경우 자동 작업은 완료되지 않을 수 있습니다.\n" +
            "실제 사이트 등록 성공 여부는 Windows에서 별도 검증이 필요합니다.", "사용 안내"));
        moreMenu.Items.Add(diagnosticsMenu);
        moreMenu.Items.Add("업데이트 확인 · 설치", null,
            (_, _) => _ = CheckForUpdateAsync(automatic: false, installImmediately: true));
        Button? moreButton = null;
        moreButton = AddToolButton(bar, "관리 ▾", () => moreMenu.Show(moreButton!, 0, moreButton!.Height), 85);
        RefreshSystemLabel();
        return bar;
    }

    private void AddNewListing(string operation)
    {
        if (_marketVisible) DisableMarketView();
        _module.SelectRegistrationMode(operation);
        _module.AddListing();
        _status.Text = $"  시스템 {_activeSystem} · 새 {operation} 물품 편집";
    }

    private static void AddToolbarDivider(FlowLayoutPanel bar) => bar.Controls.Add(new Panel
    {
        Width = 1, Height = 25, BackColor = UiTheme.Border,
        Margin = new Padding(11, 6, 11, 0)
    });

    private void OpenUpdateSettingsDialog()
    {
        using var dialog = new Form
        {
            Text = "프로그램 업데이트 설정",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(700, 300),
            MinimumSize = new Size(560, 290),
            FormBorderStyle = FormBorderStyle.Sizable,
            MaximizeBox = false,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            Font = Font
        };
        var layout = new TableLayoutPanel
        { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label
        { Text = $"현재 버전 {AppUpdateService.CurrentVersion} · 새 버전이 있으면 프로그램 안에서 설치합니다.",
          Dock = DockStyle.Fill, ForeColor = UiTheme.Text, AutoSize = false };
        var feed = new TextBox
        { Text = _updateSettings.FeedUrl, Dock = DockStyle.Fill,
          PlaceholderText = "https://.../latest.json (업데이트 배포 주소)" };
        var automatic = new CheckBox
        { Checked = _updateSettings.Automatic, Dock = DockStyle.Fill,
          Text = "프로그램 실행 시 자동 검사 및 작업이 끝나면 자동 업데이트" };
        var hint = new Label
        { Text = "공식 업데이트 주소가 자동 연결됩니다. 새 버전 배포 후 설치할 수 있습니다.",
          Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, AutoSize = false };
        var buttons = new FlowLayoutPanel
        { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", Width = 100, Height = 32, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "저장하고 확인", Width = 125, Height = 32, DialogResult = DialogResult.OK };
        UiTheme.StyleButton(save, UiTheme.ButtonTone.Primary);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(feed, 0, 1);
        layout.Controls.Add(automatic, 0, 2);
        layout.Controls.Add(hint, 0, 3);
        layout.Controls.Add(buttons, 0, 4);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string url = feed.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) url = AppUpdateService.DefaultFeedUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { AppUpdateService.ValidateFeedUrl(url); }
            catch (Exception ex)
            { MessageBox.Show(this, ex.Message, "업데이트 주소 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        _updateSettings = new AppUpdateSettings { FeedUrl = url, Automatic = automatic.Checked };
        AppUpdateService.SaveSettings(_updateSettings);
        _manualUpdateRequested = false;
        _pendingUpdate = null;
        _downloadedUpdate = null;
        _nextUpdateRetryUtc = DateTime.MinValue;
        _lastUpdateCheckUtc = DateTime.MinValue;
        if (!string.IsNullOrWhiteSpace(url)) _ = CheckForUpdateAsync(automatic: false);
        else MessageBox.Show(this, "주소가 설정되지 않았습니다. 업데이트 배포 서버 연결 후 자동으로 사용할 수 있습니다.", "업데이트 설정");
    }

    private async Task CheckForUpdateAsync(bool automatic, bool installImmediately = false)
    {
        if (_checkingUpdate || _installingUpdate || string.IsNullOrWhiteSpace(_updateSettings.FeedUrl)) return;
        _checkingUpdate = true;
        _lastUpdateCheckUtc = DateTime.UtcNow;
        try
        {
            _status.Text = "  새 버전 확인 중...";
            var candidate = await AppUpdateService.CheckAsync(_updateSettings.FeedUrl, CancellationToken.None);
            if (IsDisposed || Disposing) return;
            if (candidate is null)
            {
                _pendingUpdate = null;
                _status.Text = "  최신 버전 사용 중: " + AppUpdateService.CurrentVersion;
                if (!automatic) MessageBox.Show(this, "이미 최신 버전입니다.", "업데이트 확인");
                return;
            }
            if (!string.Equals(_pendingUpdate?.Sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                _downloadedUpdate = null;
            _pendingUpdate = candidate;
            _status.Text = "  새 버전 " + candidate.Version + " 발견 · " +
                (CanInstallUpdateAcrossSystems ? "업데이트 준비" : "진행 중인 등록 작업 완료 대기");
            if (installImmediately)
            {
                _manualUpdateRequested = true;
                await InstallPendingUpdateAsync(automatic: true);
            }
            else if (automatic && _updateSettings.Automatic)
            {
                if (CanInstallUpdateAcrossSystems) await InstallPendingUpdateAsync(automatic: true);
            }
            else if (MessageBox.Show(this,
                $"새 버전 {candidate.Version}이 있습니다. 지금 설치할까요?\n\n" +
                (candidate.ReleaseNotes ?? "")[..Math.Min(candidate.ReleaseNotes?.Length ?? 0, 1200)] +
                "\n\n기존 물품 목록과 로그인 설정은 유지합니다.",
                "새 버전 설치", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                await InstallPendingUpdateAsync(automatic: false);
            else _nextUpdateRetryUtc = DateTime.UtcNow.AddHours(6);
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing) return;
            _status.Text = "  업데이트 확인 실패 · 기존 프로그램은 계속 사용할 수 있습니다.";
            if (!automatic) MessageBox.Show(this, ex.Message, "업데이트 확인 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _checkingUpdate = false; }
    }

    private async Task InstallPendingUpdateAsync(bool automatic)
    {
        if (_installingUpdate || _pendingUpdate is null) return;
        if (!CanInstallUpdateAcrossSystems)
        {
            _status.Text = "  업데이트 예약됨 · 자동등록을 일시정지하면 안전하게 설치합니다.";
            if (!automatic)
                MessageBox.Show(this, "실행 중인 등록/조회 작업이 종료되거나 자동 실행을 일시정지하면 업데이트합니다.", "업데이트 대기");
            return;
        }
        _installingUpdate = true;
        try
        {
            var manifest = _pendingUpdate;
            var archive = _downloadedUpdate;
            if (string.IsNullOrWhiteSpace(archive) || !System.IO.File.Exists(archive))
            {
                var progress = new Progress<int>(percent =>
                { if (!IsDisposed && !Disposing) _status.Text = $"  업데이트 다운로드 {percent}%"; });
                archive = await AppUpdateService.DownloadVerifiedAsync(manifest, progress, CancellationToken.None);
                _downloadedUpdate = archive;
            }
            if (IsDisposed || Disposing) return;
            if (!CanInstallUpdateAcrossSystems)
            {
                _status.Text = "  업데이트 다운로드 완료 · 진행 중인 작업 종료 대기";
                return;
            }
            _status.Text = "  새 버전 설치 준비 · 프로그램을 안전하게 종료합니다.";
            // A/B/C are idle here. Save any local edits and preserve a
            // separate recovery point before changing application binaries.
            _moduleA.SaveListings(); _moduleB.SaveListings(); _moduleC.SaveListings();
            UserSettingsSnapshots.CreateSnapshot("pre-update");
            AppUpdateService.StartExternalInstaller(archive, manifest);
            _updateTimer.Stop();
            Close();
        }
        catch (Exception ex)
        {
            _nextUpdateRetryUtc = DateTime.UtcNow.AddMinutes(10);
            if (!IsDisposed && !Disposing)
            {
                _status.Text = "  업데이트 실패 · 기존 프로그램 유지";
                if (!automatic)
                    MessageBox.Show(this, ex.Message, "업데이트 중단", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally { _installingUpdate = false; }
    }

    private static Button AddToolButton(FlowLayoutPanel parent, string label, Action action, int width = 72,
        UiTheme.ButtonTone tone = UiTheme.ButtonTone.Neutral)
    {
        var button = new Button
        {
            Text = label, Size = new Size(width, 34), Margin = new Padding(2, 3, 4, 2)
        };
        UiTheme.StyleButton(button, tone);
        // Korean button labels were visibly cut off after the larger font and
        // round corners were applied. Size to actual rendered text + insets.
        button.Width = Math.Max(width,
            TextRenderer.MeasureText(label, button.Font).Width + button.Padding.Horizontal + 24);
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        return button;
    }

}
