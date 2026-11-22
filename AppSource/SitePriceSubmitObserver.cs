using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LuzenBrowserReplica;

// Watches the real first-party POST after the site's own submit button is
// clicked. The CDP request event is not evidence of success: the response must
// finish, and the signed-in OWN listing is independently read back afterward.
// No headers, cookies, request values or query strings are read. The contact
// endpoint's response is parsed ONLY in memory to classify its first result
// token as accepted / rejected / unreadable. The response is never retained.
internal sealed class SitePriceSubmitObservation
{
    public bool RequestObserved { get; init; }
    public bool NetworkFinished { get; init; }
    public int? HttpStatus { get; init; }
    public bool ContactCheckRejected { get; init; }
    public string OtherBuyPostPath { get; init; } = "";
    public string Detail { get; init; } = "";
}

internal sealed class SitePriceSubmitObserver : IDisposable
{
    // Confirmed by a user's *manual* purchase-listing edit on 2026-11-18.
    public const string BuyReRegistrationPostPath = "/myroom/buy/buy_re_reg_ok.php";

    private readonly CoreWebView2 _core;
    private readonly TaskCompletionSource<SitePriceSubmitObservation> _done =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<ContactDecision> _contactDecision =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed record ContactDecision(bool? Approved, string Summary);
    private CoreWebView2DevToolsProtocolEventReceiver? _requests;
    private CoreWebView2DevToolsProtocolEventReceiver? _responses;
    private CoreWebView2DevToolsProtocolEventReceiver? _finished;
    private CoreWebView2DevToolsProtocolEventReceiver? _failed;
    private string _requestId = "";
    private string _otherBuyPostPath = "";
    // The official page's FormChecker intentionally cancels the first
    // submit event while it performs these asynchronous checks. The only
    // response content interpreted is the contact-check's initial result
    // token; the actual text, contact details, and request data are not logged.
    private readonly Dictionary<string, string> _validationRequests = new();
    private readonly List<string> _validationStages = new();
    private bool _contactStarted;
    private bool _contactFinished;
    private bool _categoryStarted;
    private bool _categoryFinished;
    private int? _contactStatus;
    private int? _categoryStatus;
    private int? _httpStatus;
    private bool _disposed;

    // Used by the visible one-listing price editor to keep the official page
    // open when FormChecker has not sent its final request yet.
    public bool FinalPostStarted => _requestId.Length > 0;

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

    private static string? ValidationStage(string path) => path.ToLowerInvariant() switch
    {
        "/_include/_user_contact_restrict.php" => "연락처 검사",
        "/_include/_ajax_trade_auto_class_check.php" => "제목·카테고리 검사",
        "/_include/_ajax_image_upload.php" => "에디터 이미지 처리",
        _ => null
    };

    private void AppendStage(string stage)
    {
        if (_validationStages.Count < 18) _validationStages.Add(stage);
    }

    private string StageSummary() => _validationStages.Count == 0
        ? "사전 검증 AJAX 미감지 (폼 검증 또는 사이트 스크립트 실행 단계 확인 필요)"
        : "사이트 비동기 검증: " + string.Join(" → ", _validationStages) +
          (_contactDecision.Task.IsCompletedSuccessfully
            ? " · 연락처 검사 판단: " + _contactDecision.Task.Result.Summary : "");

    // Site buy_re_reg.js explicitly tests res.split('|')[0] == "S" before
    // calling createLayerContent(). HTTP 200 alone cannot prove that branch
    // ran. NEVER return the raw body, error text, user ID, or phone values.
    private async Task InspectContactDecisionAsync(string requestId)
    {
        ContactDecision decision;
        try
        {
            string json = await _core.CallDevToolsProtocolMethodAsync("Network.getResponseBody",
                JsonSerializer.Serialize(new { requestId })).WaitAsync(TimeSpan.FromSeconds(4));
            using var doc = JsonDocument.Parse(json);
            string body = Str(doc.RootElement, "body");
            bool encoded = doc.RootElement.TryGetProperty("base64Encoded", out var flag) &&
                flag.ValueKind == JsonValueKind.True;
            if (encoded) body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            // Compare EXACTLY like the site's own switch (no Trim):
            // whitespace around S would also make the site reject it.
            string token = body.Split('|', 2)[0];
            decision = token switch
            {
                "S" => new ContactDecision(true, "승인(S) — 다음 사이트 단계 진행 가능"),
                "" => new ContactDecision(null, "응답이 비어 있어 판정 불가"),
                _ => new ContactDecision(false, "서버가 승인(S)하지 않음 — 원본 사이트 검사에서 제출 중단")
            };
        }
        catch (Exception)
        {
            // CDP inspection may be unavailable on an older WebView2 runtime.
            // An inspection failure is NOT a server rejection or a success.
            decision = new ContactDecision(null, "응답 판독 불가 — 서버 승인 여부 미확인");
        }
        _contactDecision.TrySetResult(decision);
    }

    // HTTP 200 does not establish that the site's response said "S".
    // Report the safe verdict, never the response text or form values.
    private string StalledBeforeFinalPost()
    {
        if (!_contactStarted)
            return "중단 위치: 연락처 검사 요청 이전 (FormChecker 필수값·가격 조건·사이트 알림 확인)";
        if (!_contactFinished)
            return "중단 위치: 연락처 검사 요청 후 응답 처리 완료 이전";
        if (_contactStatus is < 200 or >= 300)
            return "중단 위치: 연락처 검사 HTTP 오류 (서버 상태 확인 필요)";
        if (_contactDecision.Task.IsCompletedSuccessfully &&
            _contactDecision.Task.Result.Approved == false)
            return "중단 위치: 연락처 검사 서버 거절 (사이트에서 승인하지 않은 수정)";
        if (!_categoryStarted)
            return "중단 위치: 연락처 검사 이후 분류 검사 이전 (프리미엄 추가 절차·사이트 스크립트 확인)";
        if (!_categoryFinished)
            return "중단 위치: 분류 검사 요청 후 응답 처리 완료 이전";
        if (_categoryStatus is < 200 or >= 300)
            return "중단 위치: 분류 검사 HTTP 오류 (서버 상태 확인 필요)";
        return "중단 위치: 분류 검사 완료 이후 최종 제출 이전 (카테고리 확인·에디터·시세 제한 확인)";
    }

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
                !ItemManiaAccountStore.IsOfficialSite(uri)) return;
            var validation = ValidationStage(uri.AbsolutePath);
            if (validation is not null)
            {
                if (validation == "연락처 검사") _contactStarted = true;
                if (validation == "제목·카테고리 검사") _categoryStarted = true;
                _validationRequests[requestId] = validation;
                AppendStage(validation + " 요청");
                return;
            }
            if (!uri.AbsolutePath.Equals(BuyReRegistrationPostPath, StringComparison.OrdinalIgnoreCase))
            {
                // A site revision may submit the same edit to another BUY
                // endpoint. This path is only a diagnostic, never proof of
                // a successful price edit or authorization to retry.
                if (_otherBuyPostPath.Length == 0 &&
                    uri.AbsolutePath.StartsWith("/myroom/buy/", StringComparison.OrdinalIgnoreCase))
                    _otherBuyPostPath = uri.AbsolutePath[..Math.Min(uri.AbsolutePath.Length, 120)];
                return;
            }
            _requestId = requestId;
        }
        catch (Exception) { /* Unrelated DevTools events do not affect the edit. */ }
    }

    private void OnResponse(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = doc.RootElement;
            string requestId = Str(root, "requestId");
            if (!root.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("status", out var status) || !status.TryGetInt32(out var code)) return;
            if (_validationRequests.TryGetValue(requestId, out var stage))
            {
                if (stage == "연락처 검사") _contactStatus = code;
                if (stage == "제목·카테고리 검사") _categoryStatus = code;
                AppendStage(stage + " HTTP " + code);
                return;
            }
            if (requestId != _requestId) return;
            _httpStatus = code;
        }
        catch (Exception) { /* No response status is safer than a guessed one. */ }
    }

    private void OnFinished(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var requestId = Str(doc.RootElement, "requestId");
            if (_validationRequests.Remove(requestId, out var stage))
            {
                if (stage == "연락처 검사")
                {
                    _contactFinished = true;
                    _ = InspectContactDecisionAsync(requestId);
                }
                if (stage == "제목·카테고리 검사") _categoryFinished = true;
                AppendStage(stage + " 완료");
                return;
            }
            if (requestId != _requestId) return;
            _done.TrySetResult(new SitePriceSubmitObservation
            {
                RequestObserved = true, NetworkFinished = true, HttpStatus = _httpStatus,
                OtherBuyPostPath = _otherBuyPostPath,
                Detail = "사이트 제출 요청의 네트워크 전송 완료 · " + StageSummary()
            });
        }
        catch (Exception) { /* Observe only the matching request. */ }
    }

    private void OnFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var requestId = Str(doc.RootElement, "requestId");
            if (_validationRequests.Remove(requestId, out var stage))
            {
                AppendStage(stage + " 전송 실패");
                return;
            }
            if (requestId != _requestId) return;
            _done.TrySetResult(new SitePriceSubmitObservation
            {
                RequestObserved = true, HttpStatus = _httpStatus,
                OtherBuyPostPath = _otherBuyPostPath,
                Detail = "사이트 제출 요청이 네트워크 단계에서 끝나지 못함 · " + StageSummary()
            });
        }
        catch (Exception) { /* Preserve an uncertain result. */ }
    }

    public async Task<SitePriceSubmitObservation> WaitAsync(CancellationToken token)
    {
        // A conclusive contact-check rejection cannot lead to the official
        // category/final POST chain. Stop waiting early, then perform the
        // existing read-only, exact-ID price check; NEVER resubmit.
        var timeout = Task.Delay(TimeSpan.FromSeconds(35), token);
        Task first = await Task.WhenAny(_done.Task, _contactDecision.Task, timeout);
        token.ThrowIfCancellationRequested();
        if (first == _contactDecision.Task)
        {
            if (_contactDecision.Task.Result.Approved == false && _requestId.Length == 0)
                return new SitePriceSubmitObservation
                {
                    ContactCheckRejected = true,
                    OtherBuyPostPath = _otherBuyPostPath,
                    Detail = "사이트 연락처 검사에서 승인되지 않아 최종 제출 없음 · " +
                        StageSummary() + " · " + StalledBeforeFinalPost()
                };
            first = await Task.WhenAny(_done.Task, timeout);
            token.ThrowIfCancellationRequested();
        }
        if (first == _done.Task) return await _done.Task;
        // Even when the response inspection completed late, its safe status
        // is included in the stage summary for the user's actual error.
        if (_contactDecision.Task.IsCompletedSuccessfully &&
            _contactDecision.Task.Result.Approved == false && _requestId.Length == 0)
            return new SitePriceSubmitObservation
            {
                ContactCheckRejected = true,
                OtherBuyPostPath = _otherBuyPostPath,
                Detail = "사이트 연락처 검사에서 승인되지 않아 최종 제출 없음 · " +
                    StageSummary() + " · " + StalledBeforeFinalPost()
            };
        else
        {
            return new SitePriceSubmitObservation
            {
                RequestObserved = _requestId.Length > 0, HttpStatus = _httpStatus,
                OtherBuyPostPath = _otherBuyPostPath,
                Detail = (_requestId.Length > 0
                    ? "요청은 확인했으나 완료 신호가 없어 결과 확인 필요"
                    : "사이트 최종 재등록 POST 요청 미감지") + " · " + StageSummary() +
                    (_requestId.Length > 0 ? "" : " · " + StalledBeforeFinalPost())
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
