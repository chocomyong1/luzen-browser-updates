using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LuzenBrowserReplica;

string secret = new('a', 64);
using var bridge = new MarketBridgeServer(secret, port: 0);
var received = new TaskCompletionSource<MarketBridgeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
bridge.SnapshotReceived += snapshot => received.TrySetResult(snapshot);
bridge.Start();
if (bridge.ListeningPort <= 0) throw new Exception("Ephemeral loopback listener did not start.");
using var handler = new HttpClientHandler { UseProxy = false };
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
string url = $"http://127.0.0.1:{bridge.ListeningPort}/market";

string payload = JsonSerializer.Serialize(new
{
    schema = 1,
    observedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    eligiblePosts = 27,
    ownPosts = 2,
    rows = new[]
    {
        new { server = "카마인", level = "10", highest = 97000, median = 93000,
            normalMax = 96000, myPrice = 94000, distinct = 12, total = 19,
            highCount = 1, basis = "자동" }
    }
});

async Task<HttpStatusCode> SendAsync(string body, string token)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
    request.Headers.Add("X-Vanta-Token", token);
    using var response = await http.SendAsync(request);
    return response.StatusCode;
}

if (await SendAsync(payload, new string('0', 64)) != HttpStatusCode.Unauthorized)
    throw new Exception("Unauthorized browser data was not rejected.");
if (received.Task.IsCompleted) throw new Exception("Unauthorized payload reached UI.");

if (await SendAsync("{malformed", secret) != HttpStatusCode.UnprocessableEntity)
    throw new Exception("Malformed JSON was not rejected.");
if (await SendAsync(payload.Replace("\"level\":\"10\"", "\"level\":\"42\""), secret) != HttpStatusCode.UnprocessableEntity)
    throw new Exception("Invalid gem level was not rejected.");

if (await SendAsync(payload, secret) != HttpStatusCode.NoContent)
    throw new Exception("Verified local market observation was not accepted.");
MarketBridgeSnapshot actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
if (actual.Rows.Count != 1 || actual.Rows[0].Server != "카마인" ||
    actual.Rows[0].Highest != 97000 || actual.Rows[0].MyPrice != 94000)
    throw new Exception("Aggregated market fields changed in transit.");

Console.WriteLine("PASS VANTA localhost-only authenticated market bridge and read-only aggregate payload");
