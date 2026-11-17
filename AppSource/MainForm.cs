using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

// V8: main program is an item-list editor, not a visible Internet browser.
// Keep Windows' native sizable frame so every edge/corner supports drag-resize.
internal sealed class MainForm : Form
{
    private readonly AutomationForm _module;
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
        Text = "Luzen Browser | 물품 · 재등록 관리 - V22";
        Icon = new Icon(Settings.Asset("iBook1.ico"));
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 640);
        Size = new Size(1540, 860);
        FormBorderStyle = FormBorderStyle.Sizable; // native corner and edge dragging
        MaximizeBox = true;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Canvas;
        Font = new Font("맑은 고딕", 9.5F);

        _module = new AutomationForm(username)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            MinimumSize = Size.Empty,
            MaximumSize = Size.Empty,
            Dock = DockStyle.Fill
        };

        var top = BuildToolbar();
        var modes = BuildModeTabs();
        var body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        body.Controls.Add(_module);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 27, BackColor = UiTheme.SurfaceAlt };
        var info = new Label
        {
            Dock = DockStyle.Right, Width = 320, TextAlign = ContentAlignment.MiddleRight,
            Text = $"사이트: 아이템매니아   |   계정: {username}   ",
            ForeColor = UiTheme.Muted
        };
        footer.Controls.Add(_status);
        footer.Controls.Add(info);
        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(modes);
        Controls.Add(top);

        Shown += (_, _) =>
        {
            _module.Show();
            _module.ShowAutomationView();
            _status.Text = "  자동등록 현황 · 물품등록 탭은 별도 편집 화면으로 전환됩니다.";
            AppUpdateService.ReportStartupReady();
            _updateTimer.Start();
            if (_updateSettings.Automatic && !string.IsNullOrWhiteSpace(_updateSettings.FeedUrl))
                _ = CheckForUpdateAsync(automatic: true);
        };
        _updateTimer.Tick += async (_, _) =>
        {
            if (_installingUpdate || _checkingUpdate || IsDisposed || Disposing) return;
            if (_pendingUpdate is not null)
            {
                if ((_updateSettings.Automatic || _manualUpdateRequested) && _module.CanInstallSoftwareUpdate &&
                    DateTime.UtcNow >= _nextUpdateRetryUtc)
                    await InstallPendingUpdateAsync(automatic: true);
            }
            else if (_updateSettings.Automatic &&
                !string.IsNullOrWhiteSpace(_updateSettings.FeedUrl) &&
                DateTime.UtcNow - _lastUpdateCheckUtc >= TimeSpan.FromHours(6))
                await CheckForUpdateAsync(automatic: true);
        };
        FormClosing += (_, _) => { _updateTimer.Stop(); _module.StopAndSave(); };
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 63, AutoScroll = true, WrapContents = false,
            BackColor = UiTheme.Navy, Padding = new Padding(14, 10, 8, 7)
        };
        var logo = new PictureBox
        {
            Image = Image.FromFile(Settings.Asset("09_536c8.png")),
            Size = new Size(42, 42), SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 7, 0)
        };
        bar.Controls.Add(logo);
        bar.Controls.Add(new Label
        {
            Text = "LUZEN\nLISTING MANAGER",
            ForeColor = Color.White, Size = new Size(157, 42),
            Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
            Padding = new Padding(2, 2, 0, 0), Margin = new Padding(0, 0, 14, 0)
        });
        AddToolButton(bar, "조회", () => _module.RefreshListings());
        AddToolButton(bar, "+ 새 물품", () => _module.AddListing(), 98);
        AddToolButton(bar, "복제", () => _module.DuplicateListing());
        AddToolButton(bar, "삭제", () => _module.RemoveListing());
        AddToolButton(bar, "저장", () => _module.SaveListings());
        AddToolButton(bar, "매니아 계정", () => _module.OpenAccountSettings(), 112);
        AddToolButton(bar, "설정", () => _module.ShowWorkflowSettings());
        // One click checks and installs. Advanced feed settings remain available
        // from the button's context menu, without an extra confirmation dialog.
        var updateButton = AddToolButton(bar, "업데이트", () => _ = CheckForUpdateAsync(automatic: false, installImmediately: true), 91);
        var updateMenu = new ContextMenuStrip();
        updateMenu.Items.Add("업데이트 주소 · 자동설치 설정", null, (_, _) => OpenUpdateSettingsDialog());
        updateButton.ContextMenuStrip = updateMenu;
        AddToolButton(bar, "도움말", () => MessageBox.Show(this,
            "모서리·테두리를 끌어서 크기를 자유롭게 바꿀 수 있습니다.\n" +
            "사이트 화면은 표시하지 않으며 내부 브라우저에서만 작업합니다.\n" +
            "자동등록 목록, 물품등록 편집, 일괄 CSV 관리, 고급 작업 설정을 사용할 수 있습니다.\n" +
            "보안 확인이나 추가 인증이 필요한 경우 자동 작업은 완료되지 않을 수 있습니다.\n" +
            "실제 사이트 등록 성공 여부는 Windows에서 별도 검증이 필요합니다.", "사용 안내"));
        AddToolButton(bar, "닫기", Close);
        return bar;
    }

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
                (_module.CanInstallSoftwareUpdate ? "업데이트 준비" : "진행 중인 등록 작업 완료 대기");
            if (installImmediately)
            {
                _manualUpdateRequested = true;
                await InstallPendingUpdateAsync(automatic: true);
            }
            else if (automatic && _updateSettings.Automatic)
            {
                if (_module.CanInstallSoftwareUpdate) await InstallPendingUpdateAsync(automatic: true);
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
        if (!_module.CanInstallSoftwareUpdate)
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
            if (!_module.CanInstallSoftwareUpdate)
            {
                _status.Text = "  업데이트 다운로드 완료 · 진행 중인 작업 종료 대기";
                return;
            }
            _status.Text = "  새 버전 설치 준비 · 프로그램을 안전하게 종료합니다.";
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

    private static Button AddToolButton(FlowLayoutPanel parent, string label, Action action, int width = 72)
    {
        var button = new Button
        {
            Text = label, Size = new Size(width, 36), Margin = new Padding(2, 3, 4, 2)
        };
        UiTheme.StyleButton(button, label == "+ 새 물품" ? UiTheme.ButtonTone.Primary :
            label == "삭제" ? UiTheme.ButtonTone.Danger : UiTheme.ButtonTone.Dark);
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
        return button;
    }

    private Control BuildModeTabs()
    {
        var tabs = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 40, WrapContents = false,
            AutoScroll = true, BackColor = UiTheme.Surface,
            Padding = new Padding(12, 5, 0, 4)
        };
        var navigation = new (string label, Action action)[]
        {
            ("자동등록", () => _module.ShowAutomationView()),
            ("물품등록", () => _module.ShowItemRegistrationView()),
            ("구매글 추가", () => { _module.SelectRegistrationMode("구매등록"); _module.AddListing(); }),
            ("재등록 추가", () => { _module.SelectRegistrationMode("재등록"); _module.AddListing(); })
        };
        foreach (var (label, action) in navigation)
        {
            var button = new Button
            {
                Text = label, Width = 118, Height = 29, Margin = new Padding(2, 0, 6, 0)
            };
            UiTheme.StyleButton(button);
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) =>
            {
                action();
                _status.Text = "  현재 화면: " + label + " · 웹페이지 표시 없음";
                foreach (Control other in tabs.Controls)
                    if (other is Button nav)
                    {
                        bool active = nav == button;
                        nav.BackColor = active ? Color.FromArgb(230, 239, 253) : UiTheme.Surface;
                        nav.ForeColor = active ? UiTheme.Primary : UiTheme.Muted;
                        nav.Font = new Font(nav.Font, active ? FontStyle.Bold : FontStyle.Regular);
                    }
            };
            if (tabs.Controls.Count == 0)
            {
                button.BackColor = Color.FromArgb(230, 239, 253);
                button.ForeColor = UiTheme.Primary;
                button.Font = new Font(button.Font, FontStyle.Bold);
            }
            tabs.Controls.Add(button);
        }
        tabs.Controls.Add(new Label
        {
            AutoSize = true, Text = "  등록 목록 / 물품 편집 / 실행 로그 · 사이트 화면 숨김",
            ForeColor = UiTheme.Muted, Padding = new Padding(5, 8, 0, 0)
        });
        return tabs;
    }
}
