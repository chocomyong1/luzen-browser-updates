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
                MessageBox.Show("Luzen이 이미 실행 중입니다. 기존 창을 사용하세요.", "Luzen 실행 중");
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
