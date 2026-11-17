using System;
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

    public static void Save(string token, string chatId)
    {
        if (!Regex.IsMatch(token, @"^\d+:[A-Za-z0-9_-]+$") ||
            !Regex.IsMatch(chatId, @"^(?:-?\d+|@[A-Za-z0-9_]+)$"))
            throw new ArgumentException("봇 토큰 또는 채팅 ID의 형식이 올바르지 않습니다.");
        Directory.CreateDirectory(Settings.UserDirectory);
        var settings = new TelegramAlertPreferences
        {
            ProtectedToken = PasswordVault.Protect(token),
            ProtectedChatId = PasswordVault.Protect(chatId)
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

    private void ReconcileSiteQuota(ListingJob job, int siteRemaining)
    {
        var state = HourlyServerQuota.Current(_config.ServerHourlyCounters, job, DateTime.UtcNow);
        bool firstSiteReportThisHour = !state.SiteRemaining.HasValue;
        var difference = HourlyServerQuota.Reconcile(state, _config.MaxSubmissionsPerHour, siteRemaining);
        AppendLog(firstSiteReportThisHour
            ? $"ID·서버별 첫 사이트 잔여 횟수 동기화(텔레그램 알림 없음): {job.SiteId} / {job.Server} / 사이트 {siteRemaining}회, 이번 시간 프로그램 시도 {state.Attempts}회"
            : $"ID·서버별 잔여 횟수: {job.SiteId} / {job.Server} / 사이트 {siteRemaining}회, 이번 시간 프로그램 시도 {state.Attempts}회");
        if (difference is not { } change) return;
        string message = $"[Luzen 자동등록 횟수 불일치]\n" +
            $"아이디: {job.SiteId}\n서버: {job.Server}\n" +
            $"프로그램 예상: {change.Expected}회\n" +
            $"사이트 실제: {change.Reported}회\n" +
            $"자동 보정 후 사용 가능: {change.Effective}회\n" +
            $"기준 시간: {HourlyServerQuota.HourKey(DateTime.UtcNow)}시 (한국)\n" +
            "전체 자동등록은 중지하지 않고 다른 서버도 계속 진행합니다.";
        AppendLog($"등록 횟수 차이 자동 보정: {job.Server} / 예상 {change.Expected} → 실제 {change.Reported} / 텔레그램 알림 예약");
        _ = SendQuotaAlertAsync(message);
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

    private async Task OpenTelegramSettingsAsync()
    {
        var (savedToken, savedChatId) = TelegramQuotaAlerts.Load();
        using var form = new Form
        {
            Text = "텔레그램 알림 연결", Width = 495, Height = 304,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new Font("맑은 고딕", 9)
        };
        form.Controls.Add(new Label
        {
            Text = "기존 보석 시세 추적기와 동일한 봇 토큰·채팅 ID를 입력하세요.\n토큰은 현재 Windows 사용자 계정으로 암호화해 저장합니다.",
            Left = 19, Top = 12, Width = 444, Height = 45
        });
        form.Controls.Add(new Label { Text = "봇 토큰", Left = 19, Top = 76, Width = 88, Height = 25 });
        var tokenInput = new TextBox
        {
            Left = 117, Top = 72, Width = 337, UseSystemPasswordChar = true,
            PlaceholderText = savedToken.Length > 0 ? "저장됨 · 변경 시에만 입력" : "기존 봇 토큰을 입력"
        };
        form.Controls.Add(tokenInput);
        form.Controls.Add(new Label { Text = "채팅 ID", Left = 19, Top = 118, Width = 88, Height = 25 });
        var chatInput = new TextBox { Left = 117, Top = 114, Width = 337, Text = savedChatId };
        form.Controls.Add(chatInput);
        var status = new Label { Text = "연결 테스트 후 저장할 수 있습니다.", Left = 19, Top = 155, Width = 444, Height = 26 };
        form.Controls.Add(status);
        var test = new Button { Text = "테스트 전송", Left = 117, Top = 200, Width = 123, Height = 33 };
        test.Click += async (_, _) =>
        {
            test.Enabled = false; status.Text = "테스트 알림 전송 중...";
            try
            {
                await TelegramQuotaAlerts.SendAsync(tokenInput.Text.Trim().Length > 0 ? tokenInput.Text.Trim() : savedToken,
                    chatInput.Text.Trim(), "[Luzen 자동등록] 텔레그램 연결 테스트 성공");
                status.Text = "전송 성공. 기존 봇에서 메시지를 확인하세요.";
            }
            catch (Exception ex) { status.Text = "전송 실패: " + ex.Message; }
            finally { test.Enabled = true; }
        };
        form.Controls.Add(test);
        var save = new Button { Text = "저장", Left = 249, Top = 200, Width = 100, Height = 33 };
        save.Click += (_, _) =>
        {
            try
            {
                TelegramQuotaAlerts.Save(tokenInput.Text.Trim().Length > 0 ? tokenInput.Text.Trim() : savedToken,
                    chatInput.Text.Trim());
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            catch (Exception ex) { status.Text = "저장 실패: " + ex.Message; }
        };
        form.Controls.Add(save);
        form.Controls.Add(new Button
        {
            Text = "취소", Left = 358, Top = 200, Width = 96, Height = 33,
            DialogResult = DialogResult.Cancel
        });
        form.AcceptButton = save;
        form.ShowDialog(this);
        await Task.CompletedTask;
    }
}
