using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private readonly WindowsAutoStartPreferences _windowsBoot = WindowsAutoStart.Load();
    private readonly System.Windows.Forms.Timer _bootBRetryTimer = new() { Interval = 60_000 };
    private bool _bootBRetryAttached;
    private bool _bootBRetryChecking;
    private bool _bootCancelledByUser;
    private DateTime _nextBootBWarpConnectUtc = DateTime.MinValue;

    private async Task TryBootBLocalProxyConnectAsync()
    {
        if (!_windowsBoot.Enabled || _bootCancelledByUser || !_startTargets.B ||
            IsDisposed || Disposing || _nextBootBWarpConnectUtc > DateTime.UtcNow) return;
        // Throttle CLI checks across the 60-second B retry loop. A remains
        // active, while only B's proxy and external IP are revalidated.
        _nextBootBWarpConnectUtc = DateTime.UtcNow.AddMinutes(2);
        try
        {
            using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
            var outcome = await CloudflareWarpBoot.EnsureLocalProxyConnectedAsync(
                DualSystemNetwork.Load("B").ProxyUrl, deadline.Token);
            if (!IsDisposed && !Disposing && outcome.Attempted)
                _status.Text = "  B WARP 로컬 프록시 연결 요청 · 독립 IP 확인 후에만 B 자동등록 시작";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!IsDisposed && !Disposing)
                _status.Text = "  B WARP 준비 지연 (" + ex.GetType().Name +
                    ") · A는 계속 실행하고 B는 60초 뒤 재확인";
        }
    }

    private ToolStripMenuItem BuildWindowsStartupMenu()
    {
        var item = new ToolStripMenuItem("Windows 로그인 시 자동 실행 · 자동등록")
        { Checked = _windowsBoot.Enabled, CheckOnClick = false };
        item.Click += (_, _) =>
        {
            bool next = !_windowsBoot.Enabled;
            try
            {
                WindowsAutoStart.SetEnabled(next);
                _windowsBoot.Enabled = next;
                item.Checked = next;
                if (!next) CancelBootBRetry();
                _status.Text = next
                    ? "  설정 완료 · 다음 Windows 로그인부터 프로그램 실행 및 A/B 자동등록"
                    : "  Windows 자동 시작 해제 · 현재 실행 중인 자동등록에는 영향 없음";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Windows 시작 프로그램 설정에 실패했습니다.\n" + ex.Message,
                    "자동 실행 설정", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        return item;
    }

    private async Task InitializeWindowsBootAsync()
    {
        try
        {
            // Register the CURRENT executable on first launch and refresh its
            // path after an automatic binary update. HKCU requires no admin.
            WindowsAutoStart.SetEnabled(_windowsBoot.Enabled);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
                _status.Text = "  Windows 자동 실행 등록 실패 · 관리 메뉴에서 재설정하세요: " + ex.GetType().Name;
        }

        // Let an already available automatic update install BEFORE launching
        // background submitters; otherwise the updater would wait indefinitely.
        if (_updateSettings.Automatic && !string.IsNullOrWhiteSpace(_updateSettings.FeedUrl))
            await CheckForUpdateAsync(automatic: true);

        if (!_windowsBoot.Enabled || _bootCancelledByUser || IsDisposed || Disposing || _installingUpdate)
            return;
        // The first A WebView2 and sign-in navigation initialize asynchronously
        // after the host form is shown. Actual submits still verify login/IDs.
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (_bootCancelledByUser || IsDisposed || Disposing || _installingUpdate) return;
        try
        {
            await StartSelectedSystemsAsync(silentStartup: true);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
                _status.Text = "  자동등록 시작 실패 · 기존 등록글은 제출하지 않았습니다: " + ex.GetType().Name;
        }
    }

    private void ScheduleBootBRetry()
    {
        if (!_windowsBoot.Enabled || _bootCancelledByUser) return;
        if (!_bootBRetryAttached)
        {
            _bootBRetryTimer.Tick += async (_, _) => await TryBootBRetryAsync();
            _bootBRetryAttached = true;
        }
        _bootBRetryTimer.Start();
    }

    private async Task TryBootBRetryAsync()
    {
        if (_bootBRetryChecking || _startingBoth || _bootCancelledByUser || !_windowsBoot.Enabled ||
            IsDisposed || Disposing)
            return;
        _bootBRetryChecking = true;
        try
        {
            if (!await EnsureBReadyAsync(silentStartup: true)) return;
            if (_bootCancelledByUser || IsDisposed || Disposing) return;
            _moduleB.StartAutomationOnBoot();
            CancelBootBRetry();
            _status.Text = _moduleB.IsAutomationRunning
                ? "  B VPN 독립 IP 확인 완료 · B 자동등록 시작 · A는 기존대로 실행"
                : "  B VPN 연결 확인 완료 · B에 자동 실행할 물품이 없어 시작 생략";
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
                _status.Text = "  B VPN 자동 시작 준비 대기 · 60초 후 재확인: " + ex.GetType().Name;
        }
        finally { _bootBRetryChecking = false; }
    }

    private void CancelBootBRetry() => _bootBRetryTimer.Stop();
}
