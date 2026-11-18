using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace LuzenBrowserReplica;

internal sealed class WindowsAutoStartPreferences
{
    // The user requested startup with Windows and automatic registration.
    // One toggle controls both; C stays an explicit, independent choice.
    public bool Enabled { get; set; } = true;
}

internal static class WindowsAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "LuzenBrowserAutoRegister";
    private static string PreferencesPath => Path.Combine(Settings.UserDirectory, "windows-auto-start.json");

    public static WindowsAutoStartPreferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<WindowsAutoStartPreferences>(File.ReadAllText(PreferencesPath)) ?? new();
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        // A damaged/blocked preference must not silently opt back into
        // unattended submissions. Missing preference is the first-run ON case.
        catch (JsonException) { return new WindowsAutoStartPreferences { Enabled = false }; }
        catch (IOException) { return new WindowsAutoStartPreferences { Enabled = false }; }
        catch (UnauthorizedAccessException) { return new WindowsAutoStartPreferences { Enabled = false }; }
    }

    public static void SetEnabled(bool enabled)
    {
        // HKCU Run starts an interactive WinForms/WebView2 app only AFTER the
        // Windows user signs in. It does not install a service or require admin.
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Windows 사용자 시작 프로그램 설정에 접근할 수 없습니다.");
        if (enabled)
        {
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 확인하지 못했습니다.");
            if (!Path.GetFileName(exe).Equals("LuzenBrowserReplica.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("LuzenBrowserReplica.exe를 직접 실행한 상태에서 설정하세요.");
            key.SetValue(EntryName, $"\"{Path.GetFullPath(exe)}\" --windows-startup", RegistryValueKind.String);
        }
        else key.DeleteValue(EntryName, throwOnMissingValue: false);

        Directory.CreateDirectory(Settings.UserDirectory);
        string temp = PreferencesPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new WindowsAutoStartPreferences { Enabled = enabled }));
        File.Move(temp, PreferencesPath, overwrite: true);
    }
}
