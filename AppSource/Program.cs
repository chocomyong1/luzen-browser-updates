using System;
using System.Linq;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

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
