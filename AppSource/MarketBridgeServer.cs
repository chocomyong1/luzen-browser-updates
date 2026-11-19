using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LuzenBrowserReplica;

// Only aggregated, read-only market observations cross this boundary.
// ItemMania credentials, Telegram tokens, listing IDs and submission actions
// never enter the bridge. The HTTP listener binds to 127.0.0.1 only.
internal sealed class MarketBridgeSnapshot
{
    public int Schema { get; set; }
    public long ObservedAt { get; set; }
    public int EligiblePosts { get; set; }
    public int OwnPosts { get; set; }
    public List<MarketBridgeRow> Rows { get; set; } = new();
}

internal sealed class MarketBridgeRow
{
    public string Server { get; set; } = "";
    public string Level { get; set; } = "";
    public long Highest { get; set; }
    public long Median { get; set; }
    public long? NormalMax { get; set; }
    public long? MyPrice { get; set; }
    public int Distinct { get; set; }
    public int Total { get; set; }
    public int HighCount { get; set; }
    public string Basis { get; set; } = "";
}

internal sealed class MarketBridgeServer : IDisposable
{
    public const int DefaultPort = 47831;
    private const int MaxHeaderBytes = 8192;
    private const int MaxBodyBytes = 65536;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _token;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private Task? _acceptTask;
    public int ListeningPort { get; private set; }
    public event Action<MarketBridgeSnapshot>? SnapshotReceived;

    public MarketBridgeServer(string token, int port = DefaultPort)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new ArgumentException("유효한 연결 코드를 입력해 주세요.", nameof(token));
        _token = token;
        _port = port;
    }

    public static string LoadOrCreateToken(string path)
    {
        if (File.Exists(path))
        {
            string previous = File.ReadAllText(path).Trim();
            if (previous.Length == 64 && previous.All(Uri.IsHexDigit)) return previous;
        }
        string value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
        return value;
    }

    public void Start()
    {
        if (_listener is not null) return;
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start(8);
        _listener = listener;
        ListeningPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        _acceptTask = AcceptLoopAsync(listener, _stop.Token);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stop);
                _ = ServeAsync(client, stop); // Never block the UI or the accept loop.
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (stop.IsCancellationRequested) { break; }
            catch (SocketException) when (stop.IsCancellationRequested) { break; }
            catch (SocketException)
            {
                try { await Task.Delay(500, stop); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[MaxHeaderBytes + MaxBodyBytes + 4];
                int count = 0;
                int headerEnd = -1;
                while (headerEnd < 0 && count <= MaxHeaderBytes)
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(count, buffer.Length - count), deadline.Token);
                    if (n == 0) return;
                    count += n;
                    headerEnd = FindHeaderEnd(buffer, count);
                }
                if (headerEnd < 0 || headerEnd > MaxHeaderBytes)
                { await RespondAsync(stream, 431, "Headers Too Large", deadline.Token); return; }

                string[] lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
                if (lines.Length == 0 || lines[0] != "POST /market HTTP/1.1")
                { await RespondAsync(stream, 404, "Not Found", deadline.Token); return; }
                string? lengthHeader = Header(lines, "Content-Length");
                if (!int.TryParse(lengthHeader, out int length) || length < 2 || length > MaxBodyBytes)
                { await RespondAsync(stream, 413, "Invalid Length", deadline.Token); return; }
                string? supplied = Header(lines, "X-Vanta-Token");
                if (!ValidToken(supplied))
                { await RespondAsync(stream, 401, "Unauthorized", deadline.Token); return; }

                int bodyStart = headerEnd + 4;
                while (count - bodyStart < length)
                {
                    int remaining = length - (count - bodyStart);
                    int n = await stream.ReadAsync(buffer.AsMemory(count, remaining), deadline.Token);
                    if (n == 0) return;
                    count += n;
                }
                MarketBridgeSnapshot? snapshot;
                try
                {
                    snapshot = JsonSerializer.Deserialize<MarketBridgeSnapshot>(
                        buffer.AsSpan(bodyStart, length), JsonOptions);
                }
                catch (JsonException) { snapshot = null; }
                if (!ValidSnapshot(snapshot))
                { await RespondAsync(stream, 422, "Invalid Market Snapshot", deadline.Token); return; }

                SnapshotReceived?.Invoke(snapshot!);
                await RespondAsync(stream, 204, "No Content", deadline.Token);
            }
            catch (OperationCanceledException) { } // Closed, slow or incomplete local request.
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (Exception) { } // Malformed localhost traffic must never stop registration.
        }
    }

    private bool ValidToken(string? supplied)
    {
        if (supplied is null || supplied.Length != _token.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(_token));
    }

    private static bool ValidSnapshot(MarketBridgeSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Schema != 1 || snapshot.Rows is null ||
            snapshot.Rows.Count > 300 || snapshot.EligiblePosts is < 0 or > 100000 ||
            snapshot.OwnPosts is < 0 or > 100000) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (snapshot.ObservedAt < now - 5 * 60_000 || snapshot.ObservedAt > now + 2 * 60_000) return false;
        foreach (var row in snapshot.Rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Server) || row.Server.Length > 40 ||
                row.Level is not ("8" or "9" or "10") ||
                row.Highest is <= 0 or > 1_000_000_000 ||
                row.Median is < 0 or > 1_000_000_000 ||
                row.NormalMax is < 0 or > 1_000_000_000 ||
                row.MyPrice is < 0 or > 1_000_000_000 ||
                row.Distinct is < 0 or > 100000 || row.Total is < 0 or > 100000 ||
                row.HighCount is < 0 or > 100000 || row.Basis is null || row.Basis.Length > 40) return false;
        }
        return true;
    }

    private static string? Header(string[] lines, string name)
    {
        foreach (string line in lines.Skip(1))
        {
            int split = line.IndexOf(':');
            if (split > 0 && line[..split].Equals(name, StringComparison.OrdinalIgnoreCase))
                return line[(split + 1)..].Trim();
        }
        return null;
    }

    private static int FindHeaderEnd(byte[] data, int count)
    {
        for (int i = 0; i < count - 3; i++)
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                return i;
        return -1;
    }

    private static Task RespondAsync(NetworkStream stream, int code, string reason, CancellationToken token)
    {
        byte[] bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Length: 0\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        return stream.WriteAsync(bytes.AsMemory(), token).AsTask();
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        _listener?.Stop();
        _stop.Dispose();
    }
}
