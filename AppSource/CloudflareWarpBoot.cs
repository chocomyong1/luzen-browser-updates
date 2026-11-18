using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LuzenBrowserReplica;

// Called ONLY for Windows-login B startup. Cloudflare WARP must already be
// configured for the local proxy; this never changes modes, installs software,
// requires admin privileges, or alters A's system-wide network settings.
internal static class CloudflareWarpBoot
{
    internal readonly record struct Attempt(bool Attempted);

    private static bool IsLocalProxy(string? raw, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(raw) ||
            !DualSystemNetwork.TryNormalizeProxy(raw, out string normalized) ||
            !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return false;
        if (uri.Host is not ("127.0.0.1" or "localhost" or "::1")) return false;
        port = uri.Port;
        return port is > 0 and <= 65535;
    }

    // The installed vendor CLI is a fixed, trusted file under Program Files.
    // Do not invoke a user-specified EXE/path, scripts, powershell, or a shell.
    private static string VendorCli()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles)) return "";
        string path = Path.Combine(programFiles, "Cloudflare", "Cloudflare WARP", "warp-cli.exe");
        return File.Exists(path) ? path : "";
    }

    private static async Task<(int Code, string Output)> RunVendorCliAsync(
        string exe, string argument, CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = argument,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start()) throw new InvalidOperationException("WARP CLI 실행 실패");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            throw;
        }
        return (process.ExitCode, (await stdout) + "\n" + (await stderr));
    }

    internal static async Task<Attempt> EnsureLocalProxyConnectedAsync(
        string? bProxy, CancellationToken token = default)
    {
        if (!IsLocalProxy(bProxy, out int port)) return new Attempt(false);
        string cli = VendorCli();
        if (cli.Length == 0) return new Attempt(false);

        // settings is read-only; do NOT run 'warp-cli mode ...'.
        var settings = await RunVendorCliAsync(cli, "settings", token);
        if (settings.Code != 0) return new Attempt(false);
        var mode = Regex.Matches(settings.Output,
            @"\bWarpProxy\s+on\s+port\s+(\d{1,5})\b", RegexOptions.IgnoreCase);
        if (mode.Count != 1 || !int.TryParse(mode[0].Groups[1].Value, out int configuredPort) ||
            configuredPort != port)
            return new Attempt(false); // Full tunnel, DNS-only, wrong local port etc.

        var status = await RunVendorCliAsync(cli, "status", token);
        if (status.Code == 0 && Regex.IsMatch(status.Output,
            @"\bStatus\s+update\s*:\s*Connected\b", RegexOptions.IgnoreCase))
            return new Attempt(false); // Already connected: never restart it.

        // Safe now: verified local-proxy mode + same B port. 'connect' never
        // changes mode; B still needs a different externally verified IP.
        var connect = await RunVendorCliAsync(cli, "connect", token);
        return new Attempt(connect.Code == 0);
    }
}
