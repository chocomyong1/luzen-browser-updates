using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    // The importer lives inside a hidden WebView module. Keep its progress
    // visible even while the combined editor or another system is selected.
    private readonly Panel _importProgressBanner = new()
    {
        Dock = DockStyle.Top, Height = 44, Visible = false,
        BackColor = UiTheme.SurfaceAlt
    };
    private readonly Label _importProgressText = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = UiTheme.Text
    };
    private readonly ProgressBar _importProgressBar = new()
    {
        Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25
    };
    private readonly Button _importProgressAction = new()
    {
        Dock = DockStyle.Fill, Text = "조회 취소", FlatStyle = FlatStyle.Flat,
        BackColor = UiTheme.Surface, ForeColor = UiTheme.Primary
    };
    private readonly System.Windows.Forms.Timer _importProgressDismiss = new() { Interval = 25000 };
    private readonly System.Windows.Forms.Timer _importProgressClock = new() { Interval = 1000 };
    private string _importProgressSystem = "A";
    private string _importProgressMessage = "";
    private DateTime _importProgressStartedUtc = DateTime.UtcNow;
    private MyListingsImportPhase _importProgressPhase = MyListingsImportPhase.Completed;

    private void InitializeMyListingsProgressBanner()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Color.Transparent, Padding = new Padding(16, 6, 16, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_importProgressText, 0, 0);
        layout.Controls.Add(_importProgressBar, 1, 0);
        layout.Controls.Add(_importProgressAction, 2, 0);
        _importProgressBanner.Controls.Add(layout);
        _importProgressAction.Click += (_, _) =>
        {
            if (_importProgressPhase is MyListingsImportPhase.Running or MyListingsImportPhase.Queued)
            {
                ModuleForImport(_importProgressSystem).CancelMyListingsImportFromHeader();
            }
            else _importProgressBanner.Visible = false;
        };
        _importProgressDismiss.Tick += (_, _) =>
        {
            _importProgressDismiss.Stop();
            _importProgressBanner.Visible = false;
        };
        _importProgressClock.Tick += (_, _) => RenderMyListingsProgressText();
        _moduleA.MyListingsImportProgress += (message, phase) => UpdateMyListingsProgress("A", message, phase);
        _moduleB.MyListingsImportProgress += (message, phase) => UpdateMyListingsProgress("B", message, phase);
        _moduleC.MyListingsImportProgress += (message, phase) => UpdateMyListingsProgress("C", message, phase);
    }

    private AutomationForm ModuleForImport(string system) => system switch
    {
        "B" => _moduleB, "C" => _moduleC, _ => _moduleA
    };

    private void UpdateMyListingsProgress(string system, string message, MyListingsImportPhase phase)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            BeginInvoke((MethodInvoker)(() => UpdateMyListingsProgress(system, message, phase)));
            return;
        }
        _importProgressDismiss.Stop();
        if (_importProgressPhase is not (MyListingsImportPhase.Running or MyListingsImportPhase.Queued))
            _importProgressStartedUtc = DateTime.UtcNow;
        _importProgressSystem = system;
        _importProgressPhase = phase;
        _importProgressMessage = message;
        bool pending = phase is MyListingsImportPhase.Running or MyListingsImportPhase.Queued;
        if (pending) _importProgressClock.Start();
        else _importProgressClock.Stop();
        RenderMyListingsProgressText();
        _importProgressBar.Visible = pending;
        _importProgressAction.Text = pending ? "조회 취소" : "닫기";
        _importProgressBanner.BackColor = phase switch
        {
            MyListingsImportPhase.Failed => Color.FromArgb(67, 39, 44),
            MyListingsImportPhase.Completed => Color.FromArgb(28, 65, 57),
            MyListingsImportPhase.Cancelled => UiTheme.SurfaceAlt,
            _ => UiTheme.SurfaceAlt
        };
        _importProgressBanner.Visible = true;
        _status.Text = $"  {system} 내 등록글 · {message}";
        if (!pending && phase != MyListingsImportPhase.Failed)
            _importProgressDismiss.Start();
    }

    private void RenderMyListingsProgressText()
    {
        bool pending = _importProgressPhase is MyListingsImportPhase.Running or MyListingsImportPhase.Queued;
        TimeSpan elapsed = DateTime.UtcNow - _importProgressStartedUtc;
        string duration = pending ? $" · 경과 {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}" : "";
        _importProgressText.Text = $"  {_importProgressSystem} · 내 등록글 불러오기  |  {_importProgressMessage}{duration}";
    }
}
