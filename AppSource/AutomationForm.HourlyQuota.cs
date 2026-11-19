using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed class TelegramAlertPreferences
{
    public string ProtectedToken { get; set; } = "";
    public string ProtectedChatId { get; set; } = "";
    // Optional private recipient for read-only /status. The alert destination
    // may be a channel (-100...) and must never authorize remote commands.
    public string ProtectedStatusChatId { get; set; } = "";
}

internal static class TelegramQuotaAlerts
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static string PreferencesFile => Path.Combine(Settings.UserDirectory, "telegram-alerts.json");

    private static TelegramAlertPreferences Read()
    {
        try { return JsonSerializer.Deserialize<TelegramAlertPreferences>(File.ReadAllText(PreferencesFile)) ?? new(); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        // Do not silently discard broken/encrypted credentials by overwriting them.
    }

    public static (string Token, string ChatId) Load()
    {
        var settings = Read();
        return (PasswordVault.Unprotect(settings.ProtectedToken), PasswordVault.Unprotect(settings.ProtectedChatId));
    }

    public static bool IsConfigured()
    {
        var (token, chatId) = Load();
        return token.Length > 0 && chatId.Length > 0;
    }

    public static string LoadStatusChatId()
    {
        var settings = Read();
        string statusId = PasswordVault.Unprotect(settings.ProtectedStatusChatId);
        if (long.TryParse(statusId, out long id) && id > 0) return statusId;
        // Existing private-chat users keep /status working without migration.
        string oldDestination = PasswordVault.Unprotect(settings.ProtectedChatId);
        return long.TryParse(oldDestination, out id) && id > 0 ? oldDestination : "";
    }

    public static void Save(string token, string chatId, string statusChatId = "")
    {
        if (!Regex.IsMatch(token, @"^\d+:[A-Za-z0-9_-]+$") ||
            !Regex.IsMatch(chatId, @"^(?:-?\d+|@[A-Za-z0-9_]+)$") ||
            (statusChatId.Length > 0 &&
             (!long.TryParse(statusChatId, out long statusId) || statusId <= 0)))
            throw new ArgumentException("봇 토큰 또는 채팅 ID의 형식이 올바르지 않습니다.");
        Directory.CreateDirectory(Settings.UserDirectory);
        var settings = new TelegramAlertPreferences
        {
            ProtectedToken = PasswordVault.Protect(token),
            ProtectedChatId = PasswordVault.Protect(chatId),
            ProtectedStatusChatId = statusChatId.Length == 0 ? "" : PasswordVault.Protect(statusChatId)
        };
        File.WriteAllText(PreferencesFile, JsonSerializer.Serialize(settings));
    }

    public static async Task SendAsync(string token, string chatId, string text)
    {
        if (!Regex.IsMatch(token, @"^\d+:[A-Za-z0-9_-]+$") ||
            string.IsNullOrWhiteSpace(chatId)) throw new InvalidOperationException("텔레그램 봇 연결이 필요합니다.");
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId, ["text"] = text, ["disable_web_page_preview"] = "true"
        });
        using var response = await Client.PostAsync("https://api.telegram.org/bot" + token + "/sendMessage", body);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("텔레그램 서버가 전송을 거절했습니다. 연결 설정을 확인하세요.");
    }
}

internal sealed partial class AutomationForm
{
    private bool _telegramMissingWarned;
    private readonly Dictionary<string, DateTime> _lastTelegramIssueUtc = new(StringComparer.Ordinal);
    // A connection hiccup is not actionable until repeated read-only recovery
    // checks fail. There is only one active recovery lane per system.
    private string _activeConnectionRecovery = "";
    private int _failedConnectionRecoveryProbes;

    private void BeginConnectionRecovery(string kind)
    {
        if (_activeConnectionRecovery == kind) return;
        _activeConnectionRecovery = kind;
        _failedConnectionRecoveryProbes = 0;
    }

    private void ConnectionRecoverySucceeded()
    {
        _activeConnectionRecovery = "";
        _failedConnectionRecoveryProbes = 0;
    }

    private void ConnectionRecoveryProbeFailed(string kind, string category, string details)
    {
        BeginConnectionRecovery(kind);
        _failedConnectionRecoveryProbes++;
        AppendLog($"{_system} 연결 복구 재검사 {_failedConnectionRecoveryProbes}회 연속 실패 · 실제 제출 없음");
        // Recovery is still in progress. Two failed probes, not one failed
        // navigation, establish a sustained outage. NotifyAutomationIssue
        // also groups all connection categories into a 15-minute incident.
        if (_schedulerRunning && _failedConnectionRecoveryProbes >= 2)
            NotifyAutomationIssue(category, details);
    }

    // Only actionable failures should be sent. Normal manual pause/stop and
    // hourly quotas never use this path; a repeated site outage is throttled.
    private void NotifyAutomationIssue(string category, string details, ListingJob? job = null)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(() => NotifyAutomationIssue(category, details, job)));
            return;
        }
        var now = DateTime.UtcNow;
        // Login, VPN and management-page failures are one connectivity incident,
        // not three independent alerts if the site changes its error wording.
        string key = IsTransientConnectionIssue(category) ? "연결 장애" : category;
        if (_lastTelegramIssueUtc.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(15))
            return;
        _lastTelegramIssueUtc[key] = now;
        string listing = job is null ? "" :
            $"\n서버: {SafeTelegramIssueText(job.Server, 36)}" +
            (ListingIdentity.HasNumber(job.ListingNumber) ? $"\n등록번호: {job.ListingNumber}" : "");
        bool connection = IsTransientConnectionIssue(category);
        string message = $"⚠️ Luzen · {_system}\n문제: {ShortTelegramIssue(category)}" + listing +
            (connection
                ? "\n상태: 복구 확인 2회 이상 실패 · 자동등록 대기" +
                  "\n제출: 없음 · 2분마다 자동 재확인" +
                  "\n조치: 계속되면 해당 시스템 로그인·VPN 확인"
                : category == "제출 결과 확인 필요"
                ? "\n상태: 해당 물품만 재제출 보류 · 사이트 등록 결과 확인 필요"
                : "\n상태: 실행 기록 확인 필요") +
            (connection ? "" : $"\n원인: {SafeTelegramIssueText(details, 115)}") +
            $"\n시간: {DateTime.Now:MM-dd HH:mm:ss}";
        _ = SendAutomationIssueAsync(message);
    }

    private static bool IsTransientConnectionIssue(string category) => category is
        "사이트 로그인 복구 대기" or "관리목록 표시 복구 대기" or
        "관리목록 복구 대기" or "B VPN 접속 대기" or "B VPN 연결 장애";

    private static string ShortTelegramIssue(string category) => category switch
    {
        "사이트 로그인 복구 대기" => "사이트 로그인 복구 지연",
        "관리목록 표시 복구 대기" or "관리목록 복구 대기" => "내 등록목록 표시 지연",
        "B VPN 접속 대기" or "B VPN 연결 장애" => "B VPN·사이트 접속 지연",
        _ => category
    };

    private static string SafeTelegramIssueText(string value, int maxLength)
    {
        string safe = Regex.Replace(value ?? "", @"https?://\S+", "[주소 생략]", RegexOptions.IgnoreCase);
        safe = Regex.Replace(safe,
            @"\b(password|token|cookie|authorization|session)\s*[:=]\s*[^\s;,]+",
            "$1=[비공개]", RegexOptions.IgnoreCase);
        safe = safe.Replace('\r', ' ').Replace('\n', ' ');
        return safe.Length > maxLength ? safe[..maxLength] + "…" : safe;
    }

    private async Task SendAutomationIssueAsync(string message)
    {
        try
        {
            var (token, chatId) = TelegramQuotaAlerts.Load();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
            {
                if (!_telegramMissingWarned)
                {
                    _telegramMissingWarned = true;
                    AppendLog("오류 알림 미전송: 관리 → 텔레그램 알림 설정에서 봇을 연결하세요.");
                }
                return;
            }
            await TelegramQuotaAlerts.SendAsync(token, chatId, message);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => AppendLog("텔레그램 오류 알림 전송 완료")));
        }
        catch (Exception)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => AppendLog("텔레그램 오류 알림 전송 실패: 연결 설정과 인터넷 상태를 확인하세요.")));
        }
    }

    private void ReconcileSiteQuota(ListingJob job, int siteRemaining)
    {
        var state = HourlyServerQuota.Current(_config.ServerHourlyCounters, job, DateTime.UtcNow);
        bool firstSiteReportThisHour = !state.SiteRemaining.HasValue;
        var difference = HourlyServerQuota.Reconcile(state, _config.MaxSubmissionsPerHour, siteRemaining);
        AppendLog(firstSiteReportThisHour
            ? $"ID·서버별 첫 사이트 잔여 횟수 동기화(텔레그램 알림 없음): {job.SiteId} / {job.Server} / 사이트 {siteRemaining}회, 이번 시간 프로그램 시도 {state.Attempts}회"
            : $"ID·서버별 잔여 횟수: {job.SiteId} / {job.Server} / 사이트 {siteRemaining}회, 이번 시간 프로그램 시도 {state.Attempts}회");
        if (difference is not { } change) return;
        // The user's original registration program may also be posting under
        // this ID/server. That makes a different remaining count expected;
        // continue to use the authoritative site count without Telegram spam.
        AppendLog($"등록 횟수 차이 자동 보정(텔레그램 알림 OFF): {job.Server} / 예상 {change.Expected} → 사이트 실제 {change.Reported} / 사용 가능 {change.Effective}회");
    }

    private async Task SendQuotaAlertAsync(string message)
    {
        try
        {
            var (token, chatId) = TelegramQuotaAlerts.Load();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
            {
                if (!_telegramMissingWarned)
                {
                    AppendLog("횟수 차이 감지: 텔레그램 미연결. 상단 '텔레그램 연결'에서 기존 봇을 한 번 연결하세요.");
                    _telegramMissingWarned = true;
                }
                return;
            }
            await TelegramQuotaAlerts.SendAsync(token, chatId, message);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => AppendLog("등록 횟수 불일치 텔레그램 알림 발송 완료")));
        }
        catch (Exception)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(new Action(() => AppendLog("텔레그램 전송 실패: 봇 연결과 인터넷 상태를 확인하세요.")));
        }
    }

    public void OpenTelegramSettings() => _ = OpenTelegramSettingsAsync();

    private async Task OpenTelegramSettingsAsync()
    {
        var (savedToken, savedChatId) = TelegramQuotaAlerts.Load();
        string savedStatusChatId = TelegramQuotaAlerts.LoadStatusChatId();
        using var form = new Form
        {
            Text = "텔레그램 알림·현황 연결", Width = 510, Height = 355,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new Font("맑은 고딕", 9)
        };
        form.Controls.Add(new Label
        {
            Text = "오류 알림은 채널로, /status 현황 조회는 개인 채팅으로 분리합니다.\n기존 봇 토큰을 사용하며 Windows 계정으로 암호화해 저장합니다.",
            Left = 19, Top = 12, Width = 456, Height = 45
        });
        form.Controls.Add(new Label { Text = "봇 토큰", Left = 19, Top = 76, Width = 88, Height = 25 });
        var tokenInput = new TextBox
        {
            Left = 117, Top = 72, Width = 337, UseSystemPasswordChar = true,
            PlaceholderText = savedToken.Length > 0 ? "저장됨 · 변경 시에만 입력" : "기존 봇 토큰을 입력"
        };
        form.Controls.Add(tokenInput);
        form.Controls.Add(new Label { Text = "알림 채널 ID", Left = 19, Top = 118, Width = 98, Height = 25 });
        var chatInput = new TextBox { Left = 117, Top = 114, Width = 337, Text = savedChatId };
        form.Controls.Add(chatInput);
        form.Controls.Add(new Label { Text = "개인 채팅 ID", Left = 19, Top = 155, Width = 98, Height = 25 });
        var statusChatInput = new TextBox { Left = 117, Top = 151, Width = 337, Text = savedStatusChatId,
            PlaceholderText = "선택 사항 · /status를 보낼 본인의 숫자 ID" };
        form.Controls.Add(statusChatInput);
        form.Controls.Add(new Label { Text = "개인 채팅 ID는 양수만 가능 · 비워두면 채널 알림만 사용합니다.",
            Left = 117, Top = 183, Width = 355, Height = 20, ForeColor = Color.DimGray });
        var status = new Label { Text = "채널 및 개인 채팅을 각각 테스트할 수 있습니다.", Left = 19, Top = 208, Width = 460, Height = 26 };
        form.Controls.Add(status);
        var test = new Button { Text = "알림 테스트", Left = 19, Top = 260, Width = 109, Height = 33 };
        test.Click += async (_, _) =>
        {
            test.Enabled = false; status.Text = "테스트 알림 전송 중...";
            try
            {
                await TelegramQuotaAlerts.SendAsync(tokenInput.Text.Trim().Length > 0 ? tokenInput.Text.Trim() : savedToken,
                    chatInput.Text.Trim(), "[Luzen 자동등록] 텔레그램 연결 테스트 성공");
                status.Text = "알림 전송 성공. 채널에서 메시지를 확인하세요.";
            }
            catch (Exception ex) { status.Text = "전송 실패: " + ex.Message; }
            finally { test.Enabled = true; }
        };
        form.Controls.Add(test);
        var testStatus = new Button { Text = "현황 테스트", Left = 136, Top = 260, Width = 110, Height = 33 };
        testStatus.Click += async (_, _) =>
        {
            if (!long.TryParse(statusChatInput.Text.Trim(), out long id) || id <= 0)
            {
                status.Text = "본인의 개인 채팅 숫자 ID를 입력하세요.";
                return;
            }
            testStatus.Enabled = false; status.Text = "개인 채팅 연결 확인 중...";
            try
            {
                await TelegramQuotaAlerts.SendAsync(tokenInput.Text.Trim().Length > 0 ? tokenInput.Text.Trim() : savedToken,
                    statusChatInput.Text.Trim(), "[Luzen] 개인 현황 조회 채팅 연결 완료 · /status 또는 현황을 보내세요.");
                status.Text = "개인 채팅 테스트 성공. 봇에게 /status를 보내세요.";
            }
            catch (Exception ex) { status.Text = "개인 채팅 전송 실패: " + ex.Message; }
            finally { testStatus.Enabled = true; }
        };
        form.Controls.Add(testStatus);
        var save = new Button { Text = "저장", Left = 254, Top = 260, Width = 98, Height = 33 };
        save.Click += (_, _) =>
        {
            try
            {
                TelegramQuotaAlerts.Save(tokenInput.Text.Trim().Length > 0 ? tokenInput.Text.Trim() : savedToken,
                    chatInput.Text.Trim(), statusChatInput.Text.Trim());
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            catch (Exception ex) { status.Text = "저장 실패: " + ex.Message; }
        };
        form.Controls.Add(save);
        form.Controls.Add(new Button
        {
            Text = "취소", Left = 360, Top = 260, Width = 94, Height = 33,
            DialogResult = DialogResult.Cancel
        });
        form.AcceptButton = save;
        form.ShowDialog(this);
        await Task.CompletedTask;
    }
}
