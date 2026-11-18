using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private sealed class AutomaticStartTargets
    {
        public AutomaticStartTargets() { }
        public bool A { get; set; } = true;
        public bool B { get; set; } = true;
    }

    private static string StartTargetsPath => Path.Combine(Settings.UserDirectory, "automatic-start-targets.json");
    private static AutomaticStartTargets LoadStartTargets()
    {
        try
        {
            if (File.Exists(StartTargetsPath))
            {
                var stored = JsonSerializer.Deserialize<AutomaticStartTargets>(File.ReadAllText(StartTargetsPath));
                if (stored is not null && (stored.A || stored.B)) return stored;
            }
        }
        catch { /* Keep both A+B selected when preferences are unreadable. */ }
        return new AutomaticStartTargets();
    }

    private readonly AutomaticStartTargets _startTargets = LoadStartTargets();
    private readonly System.Windows.Forms.Timer _splitStatusTimer = new() { Interval = 1200 };
    private SplitContainer? _splitContainer;
    private Button? _splitButton;
    private Label? _splitAHeader;
    private Label? _splitBHeader;
    private bool _splitActive;
    private bool _splitTimerAttached;
    private bool _togglingSplit;
    private bool _startingBoth;

    private ToolStripMenuItem BuildAutoStartTargetsMenu()
    {
        var menu = new ToolStripMenuItem("자동 시작 대상");
        var a = new ToolStripMenuItem("A 기본") { CheckOnClick = true, Checked = _startTargets.A };
        var b = new ToolStripMenuItem("B VPN") { CheckOnClick = true, Checked = _startTargets.B };
        void SaveSelection()
        {
            if (!a.Checked && !b.Checked)
            {
                MessageBox.Show(this, "자동 시작 대상은 A 또는 B 중 하나 이상 선택해야 합니다.", "자동 시작 대상");
                a.Checked = _startTargets.A;
                b.Checked = _startTargets.B;
                return;
            }
            _startTargets.A = a.Checked;
            _startTargets.B = b.Checked;
            try
            {
                Directory.CreateDirectory(Settings.UserDirectory);
                var temp = StartTargetsPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_startTargets));
                File.Move(temp, StartTargetsPath, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "시작 대상 설정을 저장하지 못했습니다: " + ex.Message,
                    "설정 저장", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        a.Click += (_, _) => SaveSelection();
        b.Click += (_, _) => SaveSelection();
        menu.DropDownItems.Add(a);
        menu.DropDownItems.Add(b);
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripMenuItem("기본값: A와 B 동시 시작") { Enabled = false });
        return menu;
    }

    // Never start or display B's browser with the A route. A separate, verified
    // proxy is mandatory, including when A is already running in the background.
    private async Task<bool> EnsureBReadyAsync(bool silentStartup = false)
    {
        if (_checkingProxyConnection)
        {
            _status.Text = "  B 연결 확인이 진행 중입니다. 완료한 뒤 다시 시도하세요.";
            return false;
        }
        if (!DualSystemNetwork.IsRouteVerified("B"))
        {
            if (string.IsNullOrWhiteSpace(DualSystemNetwork.Load("B").ProxyUrl))
            {
                _status.Text = "  B VPN 연결이 아직 설정되지 않았습니다. B 연결 설정을 완료하세요.";
                if (!silentStartup) OpenSystemNetworkSettings("B");
                return false;
            }
            _checkingProxyConnection = true;
            _status.Text = "  B VPN의 A와 다른 외부 IP 확인 중…";
            try
            {
                // Windows login: A has already started independently. Only
                // prepare Cloudflare's LOCAL proxy when B is requested and
                // its actual CLI settings match this port. Never enable a
                // full-device WARP tunnel, which could reroute A as well.
                if (silentStartup && _windowsBoot.Enabled && !_bootCancelledByUser && _startTargets.B)
                    await TryBootBLocalProxyConnectAsync();
                if (_bootCancelledByUser) return false;
                await DualSystemNetwork.VerifyBRouteAsync();
            }
            catch (Exception ex)
            {
                _status.Text = "  B VPN 연결 확인 실패 · B 자동등록을 시작하지 않았습니다.";
                if (!silentStartup)
                    MessageBox.Show(this, "B VPN의 독립 외부 IP를 확인하지 못했습니다.\n" + ex.Message +
                        "\n\nA 자동등록은 별개로 유지됩니다.", "B VPN 연결 확인",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally { _checkingProxyConnection = false; }
        }
        if (!DualSystemNetwork.IsRouteVerified("B") || IsDisposed || Disposing) return false;
        if (!_systemBShown)
        {
            _moduleB.Show();
            _moduleB.ShowAutomationView();
            _systemBShown = true;
            if (!_splitActive) _module.BringToFront();
        }
        return true;
    }

    private async Task StartSelectedSystemsAsync(bool silentStartup = false)
    {
        if (_startingBoth) return;
        if (silentStartup && _bootCancelledByUser) return;
        if (!silentStartup)
        {
            _bootCancelledByUser = false;
            CancelBootBRetry();
        }
        _startingBoth = true;
        try
        {
            // C remains opt-in and independent; never include it in A+B starts.
            if (!silentStartup && _activeSystem == "C" && !_splitActive)
            {
                _moduleC.StartAutomationFromHeader();
                return;
            }
            if (_startTargets.A)
            {
                if (silentStartup) _moduleA.StartAutomationOnBoot();
                else _moduleA.StartAutomationFromHeader();
            }
            bool bReady = !_startTargets.B || await EnsureBReadyAsync(silentStartup);
            // An explicit Pause/Stop during an awaited VPN check wins.
            if (_bootCancelledByUser) return;
            if (bReady && _startTargets.B)
            {
                if (silentStartup) _moduleB.StartAutomationOnBoot();
                else _moduleB.StartAutomationFromHeader();
            }
            else if (!bReady && silentStartup && !_bootCancelledByUser)
                ScheduleBootBRetry();
            if (!bReady)
                _status.Text = silentStartup
                    ? "  A 동작은 유지 · B VPN 준비를 기다리며 자동 재확인합니다 (B 제출 없음)."
                    : "  A 동작은 유지 · B VPN 설정/연결 확인 전에는 B 자동등록을 실행하지 않습니다.";
            else
                _status.Text = $"  자동 시작 요청 · A {(_moduleA.IsAutomationRunning ? "실행" : "대기")} / " +
                    $"B {(_moduleB.IsAutomationRunning ? "실행" : "대기")}";
            RefreshSplitHeaders();
        }
        finally { _startingBoth = false; }
    }

    private void PauseSelectedSystems()
    {
        if (!(_activeSystem == "C" && !_splitActive))
        {
            _bootCancelledByUser = true;
            CancelBootBRetry();
        }
        if (_activeSystem == "C" && !_splitActive) _moduleC.PauseAutomationFromHeader();
        else
        {
            _moduleA.PauseAutomationFromHeader();
            if (_systemBShown) _moduleB.PauseAutomationFromHeader();
        }
        RefreshSplitHeaders();
    }

    private void StopSelectedSystems()
    {
        if (!(_activeSystem == "C" && !_splitActive))
        {
            _bootCancelledByUser = true;
            CancelBootBRetry();
        }
        if (_activeSystem == "C" && !_splitActive) _moduleC.StopAutomationFromHeader();
        else
        {
            _moduleA.StopAutomationFromHeader();
            if (_systemBShown) _moduleB.StopAutomationFromHeader();
        }
        RefreshSplitHeaders();
    }

    private async Task ToggleSplitViewAsync()
    {
        if (_integratedVisible) DisableIntegratedEditor();
        if (_togglingSplit) return;
        if (_splitActive) { DisableSplitView(); return; }
        _togglingSplit = true;
        try
        {
            if (!await EnsureBReadyAsync()) return;
            if (_activeSystem == "C") _activeSystem = "A";
            EnableSplitView();
        }
        finally { _togglingSplit = false; }
    }

    private Panel CreateSplitHeader(string system, out Label summary)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top, Height = 52, BackColor = UiTheme.Surface,
            Padding = new Padding(14, 8, 10, 7)
        };
        var edit = new Button { Text = "편집 ↗", Dock = DockStyle.Right, Width = 74 };
        UiTheme.StyleButton(edit);
        edit.Click += (_, _) => ExitSplitForEditor(system);
        summary = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        summary.Click += (_, _) => SelectSplitSystem(system);
        header.Controls.Add(summary);
        header.Controls.Add(edit);
        return header;
    }

    private void SelectSplitSystem(string system)
    {
        if (!_splitActive) return;
        _activeSystem = system;
        RefreshSystemLabel();
        RefreshSplitHeaders();
        _status.Text = $"  시스템 {system} 선택 · 두 자동등록은 각각 독립적으로 실행됩니다.";
    }

    private void EnableSplitView()
    {
        if (_splitActive) return;
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            BackColor = UiTheme.Border, SplitterWidth = 7
        };
        split.Panel1.BackColor = UiTheme.Canvas;
        split.Panel2.BackColor = UiTheme.Canvas;
        split.Panel1.Controls.Add(_moduleA);
        split.Panel1.Controls.Add(CreateSplitHeader("A", out var headerA));
        split.Panel2.Controls.Add(_moduleB);
        split.Panel2.Controls.Add(CreateSplitHeader("B", out var headerB));
        _splitAHeader = headerA;
        _splitBHeader = headerB;
        _splitContainer = split;
        _workspaceBody.Controls.Add(split);
        split.BringToFront();
        // Set divider only once. User can resize A/B widths independently.
        if (split.ClientSize.Width > 500)
        {
            split.SplitterDistance = (split.ClientSize.Width - split.SplitterWidth) / 2;
            split.Panel1MinSize = 200;
            split.Panel2MinSize = 200;
        }
        _moduleA.SetSplitStatusMode(true);
        _moduleB.SetSplitStatusMode(true);
        _splitActive = true;
        if (_splitButton is not null) _splitButton.Text = "단일 보기";
        if (!_splitTimerAttached)
        {
            _splitStatusTimer.Tick += (_, _) => RefreshSplitHeaders();
            _splitTimerAttached = true;
        }
        _splitStatusTimer.Start();
        RefreshSplitHeaders();
        RefreshSystemLabel();
        _status.Text = "  A/B 등록 현황 동시 표시 · 가운데 선을 드래그해 크기 조절 · 편집을 누르면 단일 보기";
    }

    private void DisableSplitView()
    {
        if (!_splitActive || _splitContainer is null) return;
        _splitStatusTimer.Stop();
        _moduleA.SetSplitStatusMode(false);
        _moduleB.SetSplitStatusMode(false);
        // Reparent forms before disposing the split container: disposing a
        // parent with its WebView2 child would destroy a running scheduler.
        _splitContainer.Panel1.Controls.Remove(_moduleA);
        _splitContainer.Panel2.Controls.Remove(_moduleB);
        _workspaceBody.Controls.Add(_moduleA);
        _workspaceBody.Controls.Add(_moduleB);
        _workspaceBody.Controls.Remove(_splitContainer);
        _splitContainer.Dispose();
        _splitContainer = null;
        _splitAHeader = null;
        _splitBHeader = null;
        _splitActive = false;
        if (_splitButton is not null) _splitButton.Text = "스플릿";
        _module.BringToFront();
        RefreshSystemLabel();
        _status.Text = $"  시스템 {_activeSystem} 단일 보기 · 다른 시스템의 자동등록은 계속 실행됩니다.";
    }

    private void ExitSplitForEditor(string system)
    {
        if (_splitActive) DisableSplitView();
        _activeSystem = system;
        _module.BringToFront();
        RefreshSystemLabel();
        _status.Text = $"  시스템 {system} · 물품 편집 화면";
    }

    private void RefreshSplitHeaders()
    {
        if (!_splitActive) return;
        if (_splitAHeader is not null)
        {
            _splitAHeader.Text = "A 기본  ·  " + _moduleA.SplitStatusSummary;
            _splitAHeader.ForeColor = _activeSystem == "A" ? UiTheme.Primary : UiTheme.Text;
        }
        if (_splitBHeader is not null)
        {
            _splitBHeader.Text = "B VPN  ·  " + _moduleB.SplitStatusSummary;
            _splitBHeader.ForeColor = _activeSystem == "B" ? UiTheme.Primary : UiTheme.Text;
        }
    }
}
