using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LuzenBrowserReplica;

// Keep the standard Windows frame (drag, snap, maximize, resize, system menu)
// while matching VANTA's dark caption to its existing toolbar. No owner-drawn
// title bar or custom window buttons are needed.
internal static class NativeTitleBarTheme
{
    private const int DarkMode = 20;
    private const int LegacyDarkMode = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int CaptionTextColor = 36;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int valueSize);

    public static void Apply(IntPtr window)
    {
        if (!OperatingSystem.IsWindows() || window == IntPtr.Zero) return;

        try
        {
            // Supported systems paint their native minimize/maximize/close
            // controls in the dark style. Older Windows 10 revisions used 19.
            int enabled = 1;
            if (DwmSetWindowAttribute(window, DarkMode, ref enabled, sizeof(int)) < 0)
                DwmSetWindowAttribute(window, LegacyDarkMode, ref enabled, sizeof(int));

            // Windows 11 supports explicit colors even when Windows itself is
            // in light mode. Windows 10 keeps the OS-managed caption colors.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int caption = ColorTranslator.ToWin32(UiTheme.Surface);
                int foreground = ColorTranslator.ToWin32(UiTheme.Text);
                int border = ColorTranslator.ToWin32(UiTheme.Border);
                DwmSetWindowAttribute(window, CaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(window, CaptionTextColor, ref foreground, sizeof(int));
                DwmSetWindowAttribute(window, BorderColor, ref border, sizeof(int));
            }
        }
        catch (DllNotFoundException) { /* Non-DWM installations retain the OS frame. */ }
        catch (EntryPointNotFoundException) { /* Older Windows retains the OS frame. */ }
    }
}
