using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private readonly TelegramStatusReceiver _telegramStatusReceiver = new();
    private readonly System.Windows.Forms.Timer _telegramStatusTimer = new() { Interval = 15_000 };
    private bool _telegramStatusPolling;
    private bool _telegramStatusErrorShown;
    private bool _telegramStatusAttached;

    private void StartTelegramStatusPolling()
    {
        if (!_telegramStatusAttached)
        {
            _telegramStatusTimer.Tick += async (_, _) => await PollTelegramStatusAsync();
            _telegramStatusAttached = true;
        }
        _telegramStatusTimer.Start();
    }

    private void StopTelegramStatusPolling() => _telegramStatusTimer.Stop();

    private async Task PollTelegramStatusAsync()
    {
        if (_telegramStatusPolling || IsDisposed || Disposing || _installingUpdate) return;
        _telegramStatusPolling = true;
        try
        {
            var (token, chatId) = TelegramQuotaAlerts.Load();
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId)) return;
            int commands = await _telegramStatusReceiver.ReadStatusRequestsAsync(token, chatId);
            if (IsDisposed || Disposing) return;
            _telegramStatusErrorShown = false;
            // Read UI-owned job snapshots *after* await returns to WinForms
            // synchronization context. Never trigger a submit from Telegram.
            for (int i = 0; i < commands; i++)
            {
                string report = "[Luzen 자동등록 현황]\n" +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n" +
                    _moduleA.TelegramStatusLine + "\n\n" +
                    _moduleB.TelegramStatusLine + "\n\n" +
                    _moduleC.TelegramStatusLine;
                await TelegramStatusReceiver.SendStatusAsync(token, chatId, report);
                if (IsDisposed || Disposing) return;
            }
        }
        catch (Exception ex)
        {
            // Existing outbound error alerts remain unaffected by an incoming
            // getUpdates conflict or connection problem. Avoid repeating noise.
            if (!_telegramStatusErrorShown && !IsDisposed && !Disposing)
            {
                _telegramStatusErrorShown = true;
                _status.Text = "  텔레그램 현황 조회 대기 · " + ex.GetType().Name +
                    " (기존 오류 알림 전송에는 영향 없음)";
            }
        }
        finally { _telegramStatusPolling = false; }
    }
}
