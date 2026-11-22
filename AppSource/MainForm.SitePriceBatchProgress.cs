using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private void AddSitePriceBatchProgressBanner(AutomationForm module, string system)
    {
        var banner = new Panel { Dock = DockStyle.Top, Height = 44, Visible = false };
        var text = new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 8, 0), ForeColor = UiTheme.Text
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 188, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(4)
        };
        var close = new Button { Text = "닫기", Width = 80, Height = 30 };
        var stop = new Button { Text = "남은 건 중지", Width = 105, Height = 30 };
        var details = new Button { Text = "중단 이유", Width = 84, Height = 30, Visible = false };
        UiTheme.StyleButton(close);
        UiTheme.StyleButton(stop);
        UiTheme.StyleButton(details);
        string? failure = null;
        bool wasRunning = false;
        close.Click += (_, _) => banner.Hide();
        stop.Click += (_, _) =>
        {
            stop.Enabled = false;
            module.RequestStopSitePriceBatch();
        };
        details.Click += (_, _) => MessageBox.Show(this, failure ?? "",
            $"{system} 일괄 가격 수정 중단", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        actions.Controls.Add(close);
        actions.Controls.Add(stop);
        actions.Controls.Add(details);
        banner.Controls.Add(text);
        banner.Controls.Add(actions);
        Controls.Add(banner);

        module.SitePriceBatchProgress += (message, running, reason) =>
        {
            if (IsDisposed || Disposing) return;
            failure = reason;
            text.Text = $"{system} 시스템 · 가격 일괄 수정  |  {message}";
            banner.BackColor = running ? UiTheme.SurfaceAlt :
                reason is null ? Color.FromArgb(28, 65, 57) : Color.FromArgb(67, 39, 44);
            close.Visible = !running;
            if (running && !wasRunning) stop.Enabled = true;
            wasRunning = running;
            stop.Visible = running;
            details.Visible = !running && reason is not null;
            banner.Show();
        };
    }
}
