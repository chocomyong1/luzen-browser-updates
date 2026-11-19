using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // An accidental second launch (including a Windows startup entry plus
        // a manual shortcut) must not create TWO independent site submitters.
        using var instance = new Mutex(true, @"Local\LuzenBrowserReplica.SingleInstance", out bool first);
        if (!first)
        {
            if (!args.Contains("--windows-startup", StringComparer.OrdinalIgnoreCase))
                MessageBox.Show("VANTA가 이미 실행 중입니다. 기존 창을 사용하세요.", "VANTA 실행 중");
            return;
        }

        // The previous running process has already closed and saved its own
        // state. Only now may a user-requested snapshot replace local config.
        try
        {
            if (UserSettingsSnapshots.ApplyPendingRestoreOnStartup())
                MessageBox.Show("설정 백업 복원이 끝났습니다.\n중복 등록 방지를 위해 자동등록은 꺼진 상태입니다. 사이트의 등록글을 확인한 뒤 다시 시작하세요.",
                    "설정 복원 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("설정 복원을 완료하지 못해 안전하게 실행을 중단했습니다. 기존 백업은 유지됩니다.\n" + ex.Message,
                "설정 복원 확인 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Single-user/local mode: the replica's own sign-in has no server-side
        // authentication, so open the actual main window immediately.
        // Keep previous service and display-name preferences where available.
        var preferences = Settings.LoadPreferences();
        var servers = Settings.LoadServers();
        var server = servers.FirstOrDefault(s =>
            string.Equals(s.Name, preferences.ServerName, StringComparison.OrdinalIgnoreCase))
            ?? servers.FirstOrDefault()
            ?? new ServerInfo();
        var displayName = string.IsNullOrWhiteSpace(preferences.UserId)
            ? "Local"
            : preferences.UserId.Trim();

        // ItemMania sign-in is separate. AutomationForm keeps the official site
        // in a background WebView2 control without displaying a browser tab.
        Application.Run(new MainForm(server, displayName));
    }
}
