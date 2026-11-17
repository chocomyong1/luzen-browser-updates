using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace LuzenBrowserReplica;

internal sealed class ServerInfo
{
    public string Name { get; set; } = "AutoItem1";
    public string Domain { get; set; } = "http://item1.luzensoft.com";
    public string VirtualPath { get; set; } = "/Luzen.ITEM.WebService/";
    public override string ToString() => Name;
}

internal sealed class UserPreferences
{
    public string UserId { get; set; } = "";
    public string ServerName { get; set; } = "AutoItem1";
    public bool SavePassword { get; set; }
    public bool AutoLogin { get; set; }
    public string EncryptedPassword { get; set; } = "";
}

internal sealed class BrowserMenu
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Group { get; set; } = "기본 메뉴";
}

internal static class Settings
{
    public static string UserDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuzenBrowserReplica");
    // Keep system A on the old path so existing registrations and credentials
    // survive unchanged. System B never reads or writes A's account/jobs.
    public static string AutomationDirectory(string system) =>
        string.Equals(system, "B", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(UserDirectory, "SystemB") : UserDirectory;
    public static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);
    public static string Config(string name) => Path.Combine(AppContext.BaseDirectory, "Config", name);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    static Settings() => Directory.CreateDirectory(UserDirectory);

    public static IReadOnlyList<ServerInfo> LoadServers()
    {
        try
        {
            XDocument xml = XDocument.Load(Config("Server.cfg"));
            var servers = xml.Descendants("server")
                .Select(e => new ServerInfo
                {
                    Name = e.Element("name")?.Value ?? "AutoItem1",
                    Domain = e.Element("domain")?.Value ?? "http://item1.luzensoft.com",
                    VirtualPath = e.Element("vitualpath")?.Value ?? "/Luzen.ITEM.WebService/"
                })
                .ToList();
            if (servers.Count > 0) return servers;
        }
        catch { /* Fall back to the packaged server. */ }
        return new List<ServerInfo> { new() };
    }

    public static UserPreferences LoadPreferences() => Load("preferences.json", new UserPreferences());
    public static void SavePreferences(UserPreferences preferences) => Save("preferences.json", preferences);

    public static List<BrowserMenu> LoadMenus()
    {
        string packaged = Config("Menus.json");
        string custom = Path.Combine(UserDirectory, "menus.json");
        try
        {
            if (File.Exists(custom)) return JsonSerializer.Deserialize<List<BrowserMenu>>(File.ReadAllText(custom), JsonOptions) ?? new();
            if (File.Exists(packaged)) return JsonSerializer.Deserialize<List<BrowserMenu>>(File.ReadAllText(packaged), JsonOptions) ?? new();
        }
        catch { }
        return new List<BrowserMenu>();
    }
    public static void SaveMenus(List<BrowserMenu> menus) => Save("menus.json", menus);
    public static Dictionary<string, string> LoadNotes() => Load("schedule.json", new Dictionary<string, string>());
    public static void SaveNotes(Dictionary<string, string> notes) => Save("schedule.json", notes);

    public static HashSet<string> LoadAutoStartMenuIds()
    {
        try
        {
            string custom = Path.Combine(UserDirectory, "AutoStart.cfg");
            XDocument xml = XDocument.Load(File.Exists(custom) ? custom : Config("AutoStart.cfg"));
            return xml.Descendants("MenuID").Select(x => x.Value).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }
    public static void SaveAutoStartMenuIds(HashSet<string> ids)
    {
        var xml = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("AutoStartConfig", ids.Select(id => new XElement("Table1", new XElement("MenuID", id)))));
        xml.Save(Path.Combine(UserDirectory, "AutoStart.cfg"));
    }

    private static T Load<T>(string file, T fallback)
    {
        try
        {
            string path = Path.Combine(UserDirectory, file);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback : fallback;
        }
        catch { return fallback; }
    }
    private static void Save<T>(string file, T value)
    {
        Directory.CreateDirectory(UserDirectory);
        File.WriteAllText(Path.Combine(UserDirectory, file), JsonSerializer.Serialize(value, JsonOptions));
    }
}

// Windows DPAPI: saved passwords are encrypted for the current Windows account.
internal static class PasswordVault
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private static byte[] Apply(byte[] bytes, bool protect)
    {
        IntPtr unmanaged = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
            var input = new DataBlob { Size = bytes.Length, Data = unmanaged };
            DataBlob output;
            bool ok = protect
                ? CryptProtectData(ref input, "LuzenBrowserReplica", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(unmanaged); }
    }

    public static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(Apply(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value)
    {
        try { return string.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(Apply(Convert.FromBase64String(value), false)); }
        catch { return ""; }
    }
}
