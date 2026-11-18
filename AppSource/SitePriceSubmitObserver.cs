using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LuzenBrowserReplica;

// Watches the real first-party POST after the site's own submit button is
// clicked. The CDP request event is not evidence of success: the response must
// finish, and the signed-in OWN listing is independently read back afterward.
// No headers, cookies, form values, response bodies or query strings are read.
internal sealed class SitePriceSubmitObservation
{
    public bool RequestObserved { get; init; }
    public bool NetworkFinished { get; init; }
    public int? HttpStatus { get; init; }
    public string Detail { get; init; } = "";
}

internal sealed class SitePriceSubmitObserver : IDisposable
{
    // Confirmed by a user's *manual* purchase-listing edit on 2026-11-18.
    public const string BuyReRegistrationPostPath = "/myroom/buy/buy_re_reg_ok.php";

    private readonly CoreWebView2 _core;
    private readonly TaskCompletionSource<SitePriceSubmitObservation> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CoreWebView2DevToolsProtocolEventReceiver? _requests;
    private CoreWebView2DevToolsProtocolEventReceiver? _responses;
    private CoreWebView2DevToolsProtocolEventReceiver? _finished;
    private CoreWebView2DevToolsProtocolEventReceiver? _failed;
    private string _requestId = "";
    private int? _httpStatus;
    private bool _disposed;

    public SitePriceSubmitObserver(CoreWebView2 core) => _core = core;

    public async Task StartAsync()
    {
        await _core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        _requests = _core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        _responses = _core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        _finished = _core.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
        _failed = _core.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
        _requests.DevToolsProtocolEventReceived += OnRequest;
        _responses.DevToolsProtocolEventReceived += OnResponse;
        _finished.DevToolsProtocolEventReceived += OnFinished;
        _failed.DevToolsProtocolEventReceived += OnFailed;
    }

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";

    private void OnRequest(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("request", out var req)) return;
            string requestId = Str(root, "requestId");
            // Redirect follow-ups can reuse the request ID. Preserve the
            // first POST and its redirect status without recording its data.
            if (_requestId.Length > 0)
            {
                if (requestId == _requestId && root.TryGetProperty("redirectResponse", out var redirect) &&
                    redirect.TryGetProperty("status", out var status) && status.TryGetInt32(out var code))
                    _httpStatus = code;
                return;
            }
            if (Str(req, "method") != "POST" || requestId.Length == 0 ||
                !Uri.TryCreate(Str(req, "url"), UriKind.Absolute, out var uri) ||
                !ItemManiaAccountStore.IsOfficialSite(uri) ||
                !uri.AbsolutePath.Equals(BuyReRegistrationPostPath, StringComparison.OrdinalIgnoreCase)) return;
            _requestId = requestId;
        }
        catch (Exception) { /* Unrelated DevTools events do not affect the edit. */ }
    }

    private void OnResponse(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed || _requestId.Length == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = doc.RootElement;
            if (Str(root, "requestId") != _requestId ||
                !root.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("status", out var status) || !status.TryGetInt32(out var code)) return;
            _httpStatus = code;
        }
        catch (Exception) { /* No response status is safer than a guessed one. */ }
    }

    private void OnFinished(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed || _requestId.Length == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (Str(doc.RootElement, "requestId") != _requestId) return;
            _done.TrySetResult(new SitePriceSubmitObservation
            {
                RequestObserved = true, NetworkFinished = true, HttpStatus = _httpStatus,
                Detail = "사이트 제출 요청의 네트워크 전송 완료"
            });
        }
        catch (Exception) { /* Observe only the matching request. */ }
    }

    private void OnFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed || _requestId.Length == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (Str(doc.RootElement, "requestId") != _requestId) return;
            _done.TrySetResult(new SitePriceSubmitObservation
            {
                RequestObserved = true, HttpStatus = _httpStatus,
                Detail = "사이트 제출 요청이 네트워크 단계에서 끝나지 못함"
            });
        }
        catch (Exception) { /* Preserve an uncertain result. */ }
    }

    public async Task<SitePriceSubmitObservation> WaitAsync(CancellationToken token)
    {
        try { return await _done.Task.WaitAsync(TimeSpan.FromSeconds(35), token); }
        catch (TimeoutException)
        {
            return new SitePriceSubmitObservation
            {
                RequestObserved = _requestId.Length > 0, HttpStatus = _httpStatus,
                Detail = _requestId.Length > 0
                    ? "요청은 확인했으나 완료 신호가 없어 결과 확인 필요"
                    : "사이트의 구매글 재등록 POST 요청이 감지되지 않음"
            };
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_requests is not null) _requests.DevToolsProtocolEventReceived -= OnRequest;
        if (_responses is not null) _responses.DevToolsProtocolEventReceived -= OnResponse;
        if (_finished is not null) _finished.DevToolsProtocolEventReceived -= OnFinished;
        if (_failed is not null) _failed.DevToolsProtocolEventReceived -= OnFailed;
    }
}
