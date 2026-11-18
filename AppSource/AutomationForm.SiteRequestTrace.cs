using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    // Passive and local-only. Never stores cookies, headers, post body values,
    // passwords, authentication tokens or private account identifiers.
    private bool _siteRequestTraceActive;
    private CoreWebView2DevToolsProtocolEventReceiver? _siteTraceRequests;
    private CoreWebView2DevToolsProtocolEventReceiver? _siteTraceResponses;
    private readonly List<SiteRequestTraceEntry> _siteTraceEntries = new();
    private readonly Dictionary<string, SiteRequestTraceEntry> _siteTraceById = new();

    private sealed class SiteRequestTraceEntry
    {
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public string FieldNames { get; set; } = "";
        public int? ResponseStatus { get; set; }
    }

    private async Task ToggleSiteRequestTraceAsync(ToolStripItem button)
    {
        if (_siteRequestTraceActive)
        {
            StopSiteRequestTrace(button);
            return;
        }
        if (_busy || _myListingsImportRunning)
        {
            MessageBox.Show(this, "진행 중인 자동 조회·제출 작업이 끝난 후 시작하세요.", "수정 요청 기록 대기");
            return;
        }
        if (_schedulerRunning) PauseScheduler();
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            var core = _web.CoreWebView2;
            if (core is null) throw new InvalidOperationException("사이트 브라우저를 시작하지 못했습니다.");
            await core.CallDevToolsProtocolMethodAsync("Network.enable", "{\"maxPostDataSize\":65536}");
            _siteTraceEntries.Clear();
            _siteTraceById.Clear();
            _siteTraceRequests = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            _siteTraceResponses = core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
            _siteTraceRequests.DevToolsProtocolEventReceived += OnSiteTraceRequest;
            _siteTraceResponses.DevToolsProtocolEventReceived += OnSiteTraceResponse;
            _siteRequestTraceActive = true;
            SizeChanged += ResizeSiteTraceBrowser;
            ResizeSiteTraceBrowser(this, EventArgs.Empty);
            button.Text = "기록 종료·복사";
            if (!MyListingsImport.IsOfficialUrl(_web.Source?.AbsoluteUri))
                core.Navigate(MyListingsImport.BuyListingsUrl);
            AppendLog("사이트 수정 요청 기록 시작 (공식 사이트의 요청 경로·필드 이름만 기록)");
            MessageBox.Show(this,
                "기록을 시작하고 사이트 화면을 열었습니다. 직접 본인 물품 1건을 수정해 주세요.\n\n" +
                "사이트에서 수정 결과를 확인한 뒤 '기록 종료·복사'를 누르세요.\n" +
                "기록 기능은 클릭·제출을 대신하지 않습니다. 계정 정보와 실제 요청값은 저장하지 않습니다.",
                "사이트 수정 요청 분석", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            DetachSiteTrace();
            _siteRequestTraceActive = false;
            SizeChanged -= ResizeSiteTraceBrowser;
            HideSiteTraceBrowser();
            AppendLog("수정 요청 기록 시작 실패: " + ex.GetType().Name);
            MessageBox.Show(this, "요청 기록을 시작하지 못했습니다: " + ex.Message,
                "사이트 요청 기록", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StopSiteRequestTrace(ToolStripItem button)
    {
        DetachSiteTrace();
        _siteRequestTraceActive = false;
        SizeChanged -= ResizeSiteTraceBrowser;
        HideSiteTraceBrowser();
        button.Text = "수정 요청 기록";
        var report = new StringBuilder();
        report.AppendLine("[Luzen 사이트 수정 요청 경로 분석 — 값·쿠키·토큰 제외]");
        report.AppendLine("공식 사이트 변경 요청과 재등록 관련 GET 경로만 기록. HTTP 응답만으로 수정 성공을 단정할 수 없습니다.");
        int count = 0;
        foreach (var item in _siteTraceEntries.Take(35))
        {
            count++;
            report.AppendLine($"#{count}: {item.Method} {item.Path} / HTTP {item.ResponseStatus?.ToString() ?? "미확인"}");
            report.AppendLine("필드 이름: " + item.FieldNames);
        }
        if (count == 0) report.AppendLine("관련 요청이 기록되지 않음 (이 브라우저에서 수정 요청을 감지하지 못함).");
        report.AppendLine("원본 요청 데이터·비밀번호·쿠키·인증값은 수집 또는 전송하지 않았습니다.");
        try
        {
            Clipboard.SetText(report.ToString());
            MessageBox.Show(this,
                $"기록 종료: 공식 사이트 변경 요청 {count}건을 확인했습니다.\n" +
                "요청 경로·필드 이름·응답 상태만 클립보드에 복사했습니다. 여기 붙여넣어 주세요.",
                "수정 요청 경로 기록", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("사이트 수정 요청 진단 복사 실패: " + ex.GetType().Name);
            MessageBox.Show(this, "클립보드 복사에 실패했습니다. 저장된 계정 정보나 원본 요청값은 없습니다.",
                "진단 복사 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        AppendLog($"사이트 수정 요청 기록 종료: 관련 요청 {count}건 (개인정보 없는 필드명만)");
    }

    private void DetachSiteTrace()
    {
        if (_siteTraceRequests is not null)
            _siteTraceRequests.DevToolsProtocolEventReceived -= OnSiteTraceRequest;
        if (_siteTraceResponses is not null)
            _siteTraceResponses.DevToolsProtocolEventReceived -= OnSiteTraceResponse;
        _siteTraceRequests = null;
        _siteTraceResponses = null;
    }

    private void ResizeSiteTraceBrowser(object? sender, EventArgs e)
    {
        if (!_siteRequestTraceActive || _web.IsDisposed) return;
        // The ordinary automation browser is deliberately hidden offscreen.
        // Show it temporarily only while the user records a manual action.
        _web.Dock = DockStyle.None;
        _web.Bounds = new Rectangle(12, 49,
            Math.Max(640, ClientSize.Width - 24), Math.Max(370, ClientSize.Height - 61));
        _web.BringToFront();
    }

    private void HideSiteTraceBrowser()
    {
        if (_web.IsDisposed) return;
        _web.Dock = DockStyle.None;
        _web.Bounds = new Rectangle(-20000, -20000, 1280, 900);
    }

    private static string SafeTraceText(string value, int max = 65)
    {
        var cleaned = Regex.Replace(value, @"[\r\n\t]", " ");
        return cleaned[..Math.Min(max, cleaned.Length)];
    }

    private static string TraceFieldNames(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "전송 필드 정보 없음";
        var fields = new List<string>();
        // The real buy_re_reg_ok.php submission uses multipart/form-data.
        // Its boundaries must NEVER be interpreted as URL-encoded field names.
        // Read Content-Disposition NAMES only, not any part values or files.
        bool multipart = raw.StartsWith("--", StringComparison.Ordinal) &&
            raw.Contains("Content-Disposition:", StringComparison.OrdinalIgnoreCase);
        if (multipart)
        {
            foreach (Match match in Regex.Matches(raw,
                @"(?im)^Content-Disposition:[^\r\n]*\bname\s*=\s*""([^""\r\n]+)""").Cast<Match>().Take(75))
                fields.Add(match.Groups[1].Value);
        }
        else
        {
            try
            {
                using var json = JsonDocument.Parse(raw);
                if (json.RootElement.ValueKind == JsonValueKind.Object)
                    fields.AddRange(json.RootElement.EnumerateObject().Select(p => p.Name));
            }
            catch (JsonException)
            {
                foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries).Take(75))
                {
                    var equal = pair.IndexOf('=');
                    if (equal < 1) continue;
                    try { fields.Add(Uri.UnescapeDataString(pair[..equal].Replace('+', ' '))); }
                    catch (UriFormatException) { /* malformed input name: skip */ }
                }
            }
        }
        var safeNames = fields.Where(x => x.Length is > 0 and < 90)
            .Select(x => Regex.IsMatch(x, "pass|cookie|token|secret|auth|csrf|session", RegexOptions.IgnoreCase)
                ? "[보안필드]" : SafeTraceText(x, 50))
            .Distinct(StringComparer.Ordinal).Take(45).ToList();
        return safeNames.Count == 0
            ? (multipart ? "멀티파트 필드 이름 미확인" : "인코딩된 필드 이름 미확인")
            : string.Join(", ", safeNames);
    }

    private static string GetTraceString(JsonElement node, string property) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private async void OnSiteTraceRequest(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (!_siteRequestTraceActive || _siteTraceEntries.Count >= 40) return;
        try
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = json.RootElement;
            if (!root.TryGetProperty("request", out var req)) return;
            var method = GetTraceString(req, "method").ToUpperInvariant();
            if (method is not ("GET" or "POST" or "PUT" or "PATCH")) return;
            if (!Uri.TryCreate(GetTraceString(req, "url"), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || !ItemManiaAccountStore.IsOfficialSite(uri)) return;
            if (Regex.IsMatch(uri.AbsolutePath, @"login|password|account|auth|payment|checkout", RegexOptions.IgnoreCase)) return;
            if (method == "GET" && !Regex.IsMatch(uri.AbsolutePath,
                @"/myroom/(buy|sell)/.*(re_reg|modify|update|edit)", RegexOptions.IgnoreCase)) return;
            var requestId = GetTraceString(root, "requestId");
            var cleanPath = Regex.Replace(uri.AbsolutePath, @"\d{8,20}", "[등록번호]");
            var entry = new SiteRequestTraceEntry
            {
                Method = method,
                Path = SafeTraceText(cleanPath, 110),
                FieldNames = TraceFieldNames(method == "GET" ? uri.Query.TrimStart('?') :
                    GetTraceString(req, "postData"))
            };
            _siteTraceEntries.Add(entry);
            if (requestId.Length > 0) _siteTraceById[requestId] = entry;
            // Some Chromium versions omit request.postData in the event.
            // Read it transiently from DevTools, retaining field NAMES only.
            if (method != "GET" && entry.FieldNames == "전송 필드 정보 없음" && requestId.Length > 0 &&
                req.TryGetProperty("hasPostData", out var hasData) && hasData.ValueKind == JsonValueKind.True &&
                _web.CoreWebView2 is not null)
            {
                try
                {
                    var reply = await _web.CoreWebView2.CallDevToolsProtocolMethodAsync(
                        "Network.getRequestPostData", JsonSerializer.Serialize(new { requestId }));
                    using var parsed = JsonDocument.Parse(reply);
                    if (_siteRequestTraceActive)
                        entry.FieldNames = TraceFieldNames(GetTraceString(parsed.RootElement, "postData"));
                }
                catch (Exception) { /* request body already unavailable: omit it */ }
            }
        }
        catch (Exception) { /* unrecognized developer-tool event: ignore */ }
    }

    private void OnSiteTraceResponse(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (!_siteRequestTraceActive) return;
        try
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = json.RootElement;
            if (!_siteTraceById.TryGetValue(GetTraceString(root, "requestId"), out var entry) ||
                !root.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.Number) return;
            entry.ResponseStatus = status.GetInt32();
        }
        catch (Exception) { /* incomplete response events must never abort browser navigation */ }
    }
}
