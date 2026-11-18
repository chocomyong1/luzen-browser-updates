using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LuzenBrowserReplica;

// Separate, read-only Telegram command receiver. Only the saved user's
// PRIVATE chat may request status. It never exposes posting controls.
internal sealed class TelegramStatusReceiver
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private string _lastToken = "";
    private string _lastChat = "";
    private long _nextOffset = -1;
    private long _sinceEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static bool IsAllowedPrivateChat(string chatId) =>
        long.TryParse(chatId, out var id) && id > 0;

    // We intentionally consume no commands for group/channel destinations,
    // even if such chat IDs can receive ordinary outgoing alerts.
    public async Task<int> ReadStatusRequestsAsync(string token, string chatId, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(token ?? "", @"^\d+:[A-Za-z0-9_-]+$") || !IsAllowedPrivateChat(chatId))
            return 0;
        if (!string.Equals(token, _lastToken, StringComparison.Ordinal) ||
            !string.Equals(chatId, _lastChat, StringComparison.Ordinal))
        {
            _lastToken = token; _lastChat = chatId;
            // On first poll use only Telegram's most recent pending update,
            // not the entire historical chat backlog.
            _nextOffset = -1;
            _sinceEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3;
        }

        string url = "https://api.telegram.org/bot" + token +
            "/getUpdates?timeout=0&limit=25&allowed_updates=%5B%22message%22%5D&offset=" + _nextOffset;
        using var response = await Client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("텔레그램 명령 수신 실패 (서버 응답 오류)");
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = data.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("텔레그램 명령 수신 형식 오류");

        int requests = 0;
        foreach (var update in results.EnumerateArray())
        {
            if (!update.TryGetProperty("update_id", out var updateId) ||
                !updateId.TryGetInt64(out long id)) continue;
            _nextOffset = Math.Max(_nextOffset, id + 1);
            if (!update.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object) continue;
            if (!message.TryGetProperty("chat", out var chat) ||
                !chat.TryGetProperty("id", out var senderChat) ||
                senderChat.ToString() != chatId) continue;
            if (!chat.TryGetProperty("type", out var chatType) ||
                chatType.GetString() != "private") continue;
            if (!message.TryGetProperty("from", out var sender) ||
                !sender.TryGetProperty("id", out var senderId) ||
                senderId.ToString() != chatId ||
                (sender.TryGetProperty("is_bot", out var bot) && bot.ValueKind == JsonValueKind.True))
                continue;
            if (!message.TryGetProperty("date", out var date) ||
                !date.TryGetInt64(out var unix) || unix < _sinceEpoch) continue;
            if (!message.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String) continue;
            string request = (text.GetString() ?? "").Trim();
            if (request == "/status" || request == "현황")
                requests = Math.Min(2, requests + 1);
        }
        return requests;
    }

    public static async Task SendStatusAsync(string token, string chatId, string status, CancellationToken ct = default)
    {
        if (!IsAllowedPrivateChat(chatId)) return;
        // Reply keyboard appears after the first /status; tapping 현황 sends
        // a fresh message. It does not offer mutating remote operations.
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = status,
            ["disable_web_page_preview"] = "true",
            ["reply_markup"] = "{\"keyboard\":[[{\"text\":\"현황\"}]],\"resize_keyboard\":true,\"is_persistent\":true}"
        });
        using var response = await Client.PostAsync(
            "https://api.telegram.org/bot" + token + "/sendMessage", body, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("텔레그램 현황 응답 전송 실패");
    }
}
