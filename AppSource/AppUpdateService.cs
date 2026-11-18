using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LuzenBrowserReplica;

internal sealed class AppUpdateSettings
{
    public string FeedUrl { get; set; } = AppUpdateService.DefaultFeedUrl;
    public bool Automatic { get; set; } = true;
}

internal sealed class AppUpdateManifest
{
    public int Schema { get; set; } = 1;
    public string Version { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Format { get; set; } = "source";
    public string ReleaseNotes { get; set; } = "";
}

internal static class AppUpdateService
{
    public const string CurrentVersion = "22.2.65";
    public const string DefaultFeedUrl =
        "https://raw.githubusercontent.com/chocomyong1/luzen-browser-updates/main/latest.json";
    public static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LuzenBrowserPortable", "ManagedInstall");
    private static string SettingsPath => Path.Combine(Settings.UserDirectory, "app-updates.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static AppUpdateSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppUpdateSettings>(
                    File.ReadAllText(SettingsPath), JsonOptions) ?? new AppUpdateSettings();
                // Migrate the pre-connected V22 preference without asking the
                // user to paste the public update URL. Keep their auto toggle.
                if (string.IsNullOrWhiteSpace(settings.FeedUrl))
                    settings.FeedUrl = DefaultFeedUrl;
                return settings;
            }
        }
        catch { /* A corrupt preference cannot prevent launching the program. */ }
        return new AppUpdateSettings();
    }

    public static void SaveSettings(AppUpdateSettings value)
    {
        Directory.CreateDirectory(Settings.UserDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(value, JsonOptions));
    }

    // The detached updater waits for this marker before considering the new
    // installation healthy. The marker is never written for another folder.
    public static void ReportStartupReady()
    {
        try
        {
            string current = Path.GetFullPath(Path.Combine(InstallRoot, "Current"));
            string running = Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            if (current.Equals(running, StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(Path.Combine(current, ".startup-ready"), CurrentVersion);
        }
        catch { /* Startup must work even if a marker cannot be written. */ }
    }

    private static Uri RequiredHttps(string text, string name)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || uri.Host.Length == 0)
            throw new InvalidOperationException(name + "은(는) HTTPS 주소여야 합니다.");
        return uri;
    }

    public static void ValidateFeedUrl(string url) => _ = RequiredHttps(url, "업데이트 서버 주소");

    public static async Task<AppUpdateManifest?> CheckAsync(string feedUrl, CancellationToken token)
    {
        Uri feed = RequiredHttps(feedUrl, "업데이트 서버 주소");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LuzenBrowser-Updater/22.0");
        using var response = await client.GetAsync(feed, HttpCompletionOption.ResponseHeadersRead, token);
        // The public channel can exist before its first release manifest is
        // published. An absent manifest is not an installation failure.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 65_536)
            throw new InvalidDataException("업데이트 안내 파일 크기가 비정상적으로 큽니다.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var limited = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (limited.Length + read > 65_536)
                throw new InvalidDataException("업데이트 안내 파일 크기가 초과됐습니다.");
            limited.Write(buffer, 0, read);
        }
        var manifest = JsonSerializer.Deserialize<AppUpdateManifest>(limited.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("업데이트 안내 파일을 읽을 수 없습니다.");
        if (manifest.Schema != 1 || !Version.TryParse(manifest.Version, out var incoming) ||
            !Version.TryParse(CurrentVersion, out var current) || incoming.Major < 1)
            throw new InvalidDataException("업데이트 버전 정보가 잘못됐습니다.");
        if (incoming.CompareTo(current) <= 0) return null;
        RequiredHttps(manifest.PackageUrl, "업데이트 파일 주소");
        if (!Regex.IsMatch(manifest.Sha256 ?? "", "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("업데이트 파일의 SHA-256 검증값이 없습니다.");
        if (manifest.Format is not ("source" or "binary"))
            throw new InvalidDataException("지원하지 않는 업데이트 파일 형식입니다.");
        return manifest;
    }

    public static async Task<string> DownloadVerifiedAsync(
        AppUpdateManifest manifest, IProgress<int>? progress, CancellationToken token)
    {
        const long maxBytes = 100L * 1024 * 1024;
        var url = RequiredHttps(manifest.PackageUrl, "업데이트 파일 주소");
        string root = Path.Combine(InstallRoot, "Downloads");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "incoming-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LuzenBrowser-Updater/22.0");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > maxBytes)
                throw new InvalidDataException("업데이트 파일이 허용된 크기(100MB)를 초과합니다.");
            await using var incoming = await response.Content.ReadAsStreamAsync(token);
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 64 * 1024, useAsync: true);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long count = 0;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await incoming.ReadAsync(buffer, token)) != 0)
            {
                count += read;
                if (count > maxBytes) throw new InvalidDataException("업데이트 파일 크기 초과");
                sha.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), token);
                if (response.Content.Headers.ContentLength is > 0)
                    progress?.Report((int)Math.Min(100, count * 100 / response.Content.Headers.ContentLength.Value));
            }
            await file.FlushAsync(token);
            var actual = Convert.ToHexString(sha.GetHashAndReset());
            if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("파일 검증값이 일치하지 않아 업데이트를 중단했습니다.");
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch { }
            throw;
        }
    }

    // A separate updater waits for the GUI to close; running files are never
    // overwritten, and all user's saved data lives outside the installation.
    public static void StartExternalInstaller(string archive, AppUpdateManifest manifest)
    {
        string updater = Path.Combine(InstallRoot, "Updater", "LuzenUpdater.exe");
        if (!File.Exists(updater))
            throw new FileNotFoundException("자동 업데이트 구성요소가 없습니다. V22 시작 파일로 실행하세요.", updater);
        var start = new ProcessStartInfo(updater)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = InstallRoot
        };
        foreach (var argument in new[] { "--root", InstallRoot, "--archive", archive,
            "--version", manifest.Version, "--format", manifest.Format,
            "--sha256", manifest.Sha256, "--pid", Environment.ProcessId.ToString() })
            start.ArgumentList.Add(argument);
        if (Process.Start(start) is null)
            throw new InvalidOperationException("업데이트 설치 프로그램을 시작할 수 없습니다.");
    }
}
