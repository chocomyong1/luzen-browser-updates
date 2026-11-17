using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LuzenBrowserReplica;

internal sealed class SystemBNetworkSettings
{
    // SOCKS5 or HTTP proxy provided by a VPN client/service. Never store a
    // password in the browser command line, process arguments or this file.
    public string ProxyUrl { get; set; } = "";
}

internal static class DualSystemNetwork
{
    private static string ConfigPath => Path.Combine(Settings.UserDirectory, "system-b-network.json");
    private static string _verifiedProxy = "";
    private static string _verifiedExternalIp = "";

    public static bool IsBRouteVerified => _verifiedProxy.Length > 0 &&
        string.Equals(_verifiedProxy, Load().ProxyUrl, StringComparison.OrdinalIgnoreCase);
    public static string VerifiedExternalIp => IsBRouteVerified ? _verifiedExternalIp : "";

    public static SystemBNetworkSettings Load()
    {
        try { return JsonSerializer.Deserialize<SystemBNetworkSettings>(File.ReadAllText(ConfigPath)) ?? new(); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public static bool TryNormalizeProxy(string value, out string normalized)
    {
        normalized = "";
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "socks5") ||
            !string.IsNullOrWhiteSpace(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" || uri.Port <= 0 || uri.Port > 65535 ||
            uri.Host.Length == 0 || !Regex.IsMatch(uri.Host, @"^[a-zA-Z0-9.:-]+$")) return false;
        // Generate the browser flag from URI components, never concatenate raw
        // user input into command-line arguments.
        string host = uri.Host.Contains(':') ? "[" + uri.Host + "]" : uri.Host;
        normalized = $"{uri.Scheme}://{host}:{uri.Port}";
        return true;
    }

    public static void SaveProxy(string proxy)
    {
        if (!TryNormalizeProxy(proxy, out var normalized))
            throw new ArgumentException("VPN에서 제공하는 SOCKS5 또는 HTTP 프록시 주소와 포트를 입력하세요. 예: socks5://127.0.0.1:1080");
        string temp = ConfigPath + ".tmp";
        Directory.CreateDirectory(Settings.UserDirectory);
        File.WriteAllText(temp, JsonSerializer.Serialize(new SystemBNetworkSettings { ProxyUrl = normalized }));
        File.Move(temp, ConfigPath, true);
        _verifiedProxy = "";
        _verifiedExternalIp = "";
    }

    public static CoreWebView2EnvironmentOptions BrowserOptionsForB()
    {
        string address = Load().ProxyUrl;
        if (!TryNormalizeProxy(address, out var normalized))
            throw new InvalidOperationException("시스템 B 연결이 설정되지 않았습니다. VPN 프록시 설정과 연결 테스트를 완료하세요.");
        // WebView2 uses a separate user-data folder/browser process for B.
        // A failed proxy must cause a connection error, never direct fallback.
        return new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--proxy-server=" + normalized
        };
    }

    public static async Task<(string DirectIp, string BIp)> VerifyBRouteAsync(CancellationToken token = default)
    {
        var configured = Load().ProxyUrl;
        if (!TryNormalizeProxy(configured, out var proxy))
            throw new InvalidOperationException("시스템 B의 프록시 주소를 먼저 저장하세요.");
        // IP lookup is performed only when the user explicitly presses
        // '연결 테스트'. No account information or credentials are transmitted.
        static async Task<string> GetIpAsync(HttpClientHandler handler, CancellationToken token)
        {
            using (handler)
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) })
            {
                string value = (await client.GetStringAsync("https://api.ipify.org", token)).Trim();
                if (!IPAddress.TryParse(value, out _)) throw new InvalidOperationException("외부 IP 확인 응답이 올바르지 않습니다.");
                return value;
            }
        }
        string direct = await GetIpAsync(new HttpClientHandler { UseProxy = false }, token);
        string b = await GetIpAsync(new HttpClientHandler { UseProxy = true, Proxy = new WebProxy(proxy) }, token);
        if (string.Equals(direct, b, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("시스템 A와 B의 외부 IP가 같습니다. B 자동등록은 시작하지 않습니다.");
        _verifiedProxy = proxy;
        _verifiedExternalIp = b;
        return (direct, b);
    }
}
