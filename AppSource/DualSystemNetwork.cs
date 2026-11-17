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
    private static string ConfigPath(string system) => Path.Combine(Settings.UserDirectory,
        system == "B" ? "system-b-network.json" : "system-c-network.json");
    private static readonly System.Collections.Generic.Dictionary<string, (string Proxy, string Ip)> Verified = new();

    private static string CheckSystem(string system) => system.ToUpperInvariant() switch
    {
        "B" => "B", "C" => "C",
        _ => throw new ArgumentException("별도 프록시는 B 또는 C 시스템에만 설정할 수 있습니다.", nameof(system))
    };

    public static bool IsBRouteVerified => IsRouteVerified("B");
    public static bool IsCRouteVerified => IsRouteVerified("C");
    public static bool IsRouteVerified(string system)
    {
        system = CheckSystem(system);
        return Verified.TryGetValue(system, out var route) && route.Proxy.Length > 0 &&
            string.Equals(route.Proxy, Load(system).ProxyUrl, StringComparison.OrdinalIgnoreCase);
    }
    public static string VerifiedExternalIp => VerifiedExternalIpFor("B");
    public static string VerifiedExternalIpFor(string system) => IsRouteVerified(system) ? Verified[CheckSystem(system)].Ip : "";

    public static SystemBNetworkSettings Load() => Load("B");
    public static SystemBNetworkSettings Load(string system)
    {
        try { return JsonSerializer.Deserialize<SystemBNetworkSettings>(File.ReadAllText(ConfigPath(CheckSystem(system)))) ?? new(); }
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

    public static void SaveProxy(string proxy) => SaveProxy("B", proxy);
    public static void SaveProxy(string system, string proxy)
    {
        system = CheckSystem(system);
        if (!TryNormalizeProxy(proxy, out var normalized))
            throw new ArgumentException("VPN에서 제공하는 SOCKS5 또는 HTTP 프록시 주소와 포트를 입력하세요. 예: socks5://127.0.0.1:1080");
        string target = ConfigPath(system);
        string temp = target + ".tmp";
        Directory.CreateDirectory(Settings.UserDirectory);
        File.WriteAllText(temp, JsonSerializer.Serialize(new SystemBNetworkSettings { ProxyUrl = normalized }));
        File.Move(temp, target, true);
        Verified.Remove(system);
        // C's address might still be valid, but its comparison with B is not.
        if (system == "B") Verified.Remove("C");
    }

    public static CoreWebView2EnvironmentOptions BrowserOptionsForB() => BrowserOptionsFor("B");
    public static CoreWebView2EnvironmentOptions BrowserOptionsFor(string system)
    {
        system = CheckSystem(system);
        string address = Load(system).ProxyUrl;
        if (!TryNormalizeProxy(address, out var normalized))
            throw new InvalidOperationException($"시스템 {system} 연결이 설정되지 않았습니다. VPN 프록시 설정과 연결 테스트를 완료하세요.");
        // WebView2 uses a separate user-data folder/browser process for B/C.
        // A failed proxy must cause a connection error, never direct fallback.
        return new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--proxy-server=" + normalized
        };
    }

    private static async Task<string> GetIpAsync(HttpClientHandler handler, CancellationToken token)
    {
        using (handler)
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) })
        {
            string value = (await client.GetStringAsync("https://api.ipify.org", token)).Trim();
            if (!IPAddress.TryParse(value, out _)) throw new InvalidOperationException("외부 IP 확인 응답이 올바르지 않습니다.");
            return value;
        }
    }

    private static async Task<(string DirectIp, string RoutedIp)> VerifyRouteAsync(string system, CancellationToken token)
    {
        system = CheckSystem(system);
        var configured = Load(system).ProxyUrl;
        if (!TryNormalizeProxy(configured, out var proxy))
            throw new InvalidOperationException($"시스템 {system}의 프록시 주소를 먼저 저장하세요.");
        // IP lookup never submits account information or credentials.
        string direct = await GetIpAsync(new HttpClientHandler { UseProxy = false }, token);
        string routed = await GetIpAsync(new HttpClientHandler { UseProxy = true, Proxy = new WebProxy(proxy) }, token);
        if (string.Equals(direct, routed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"시스템 A와 {system}의 외부 IP가 같습니다. {system} 자동등록은 시작하지 않습니다.");
        return (direct, routed);
    }

    public static async Task<(string DirectIp, string BIp)> VerifyBRouteAsync(CancellationToken token = default)
    {
        var (direct, b) = await VerifyRouteAsync("B", token);
        if (IsCRouteVerified && string.Equals(b, VerifiedExternalIpFor("C"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("시스템 B와 C의 외부 IP가 같습니다. B 자동등록은 시작하지 않습니다.");
        _ = Verified.Remove("B");
        Verified["B"] = (Load("B").ProxyUrl, b);
        return (direct, b);
    }

    public static async Task<(string DirectIp, string BIp, string CIp)> VerifyCRouteAsync(CancellationToken token = default)
    {
        var (direct, c) = await VerifyRouteAsync("C", token);
        string bProxy = Load("B").ProxyUrl;
        if (!TryNormalizeProxy(bProxy, out var normalizedB))
            throw new InvalidOperationException("C를 독립 IP로 확인하려면 B의 프록시도 먼저 설정하세요.");
        // Refuse a duplicate endpoint even if exit IP appears to rotate between tests.
        if (string.Equals(normalizedB, Load("C").ProxyUrl, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("B와 C에 같은 프록시가 지정돼 있습니다. C는 다른 VPN/프록시 연결이 필요합니다.");
        string b;
        try { b = await GetIpAsync(new HttpClientHandler { UseProxy = true, Proxy = new WebProxy(normalizedB) }, token); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("B의 외부 IP를 확인할 수 없어 C의 독립 연결을 검증하지 못했습니다. B도 연결한 뒤 다시 시도하세요.", ex);
        }
        if (string.Equals(b, c, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("시스템 B와 C의 외부 IP가 같습니다. C는 다른 VPN/프록시가 필요합니다.");
        Verified["C"] = (Load("C").ProxyUrl, c);
        return (direct, b, c);
    }
}
