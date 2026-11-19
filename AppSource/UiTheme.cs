using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

// VANTA's dark trading-workstation palette. Styling only: this class never
// changes listing data, job selection, or any browser automation behavior.
internal static class UiTheme
{
    public static readonly Color Canvas = Color.FromArgb(16, 24, 32);
    public static readonly Color Surface = Color.FromArgb(30, 41, 51);
    public static readonly Color SurfaceAlt = Color.FromArgb(37, 52, 64);
    public static readonly Color Border = Color.FromArgb(53, 70, 83);
    public static readonly Color Text = Color.FromArgb(241, 245, 249);
    public static readonly Color Muted = Color.FromArgb(145, 164, 174);
    public static readonly Color Navy = Color.FromArgb(10, 17, 24);
    public static readonly Color Primary = Color.FromArgb(87, 217, 173);
    public static readonly Color PrimaryHover = Color.FromArgb(112, 233, 192);
    public static readonly Color Success = Color.FromArgb(50, 205, 153);
    public static readonly Color Danger = Color.FromArgb(240, 107, 112);
    public static readonly Color Warning = Color.FromArgb(245, 183, 92);
    public static readonly Color WarningSurface = Color.FromArgb(66, 50, 38);

    public enum ButtonTone { Neutral, Primary, Success, Danger, Dark }

    private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void RoundButton(Button button)
    {
        if (button.Width < 4 || button.Height < 4) return;
        using var path = RoundedPath(new Rectangle(0, 0, button.Width, button.Height), 8);
        var previous = button.Region;
        button.Region = new Region(path);
        previous?.Dispose();
    }

    public static void StyleButton(Button button, ButtonTone tone = ButtonTone.Neutral)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(11, 0, 11, 0);
        button.Font = new Font("Segoe UI", 9.5F,
            tone == ButtonTone.Primary ? FontStyle.Bold : FontStyle.Regular);
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = Border;
        switch (tone)
        {
            case ButtonTone.Primary:
                button.BackColor = Primary;
                button.ForeColor = Canvas;
                button.FlatAppearance.MouseOverBackColor = PrimaryHover;
                break;
            case ButtonTone.Success:
                button.BackColor = Success;
                button.ForeColor = Canvas;
                button.FlatAppearance.MouseOverBackColor = PrimaryHover;
                break;
            case ButtonTone.Danger:
                button.BackColor = Danger;
                button.ForeColor = Canvas;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 137, 139);
                break;
            case ButtonTone.Dark:
                button.BackColor = Color.FromArgb(45, 65, 77);
                button.ForeColor = Text;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(59, 82, 93);
                break;
            default:
                button.BackColor = SurfaceAlt;
                button.ForeColor = Text;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 73, 85);
                break;
        }
        button.FlatAppearance.MouseDownBackColor = tone == ButtonTone.Neutral
            ? Color.FromArgb(64, 86, 97) : button.FlatAppearance.MouseOverBackColor;
        RoundButton(button);
        // SizeChanged is essential: most form buttons are resized by their parent
        // after theme initialization, including on DPI changes.
        button.SizeChanged -= ButtonSizeChanged;
        button.SizeChanged += ButtonSizeChanged;
    }

    private static void ButtonSizeChanged(object? sender, EventArgs args)
    {
        if (sender is Button button) RoundButton(button);
    }

    private static ButtonTone ToneFor(string? label)
    {
        string text = label ?? "";
        if (text.Contains("즉시 중지") || text == "삭제") return ButtonTone.Danger;
        if (text.Contains("자동 시작") || text.Contains("자동등록 시작")) return ButtonTone.Success;
        if (text.Contains("일괄수정") || text.Contains("불러오기") ||
            text.Contains("실제 실행")) return ButtonTone.Primary;
        return ButtonTone.Neutral;
    }

    public static void ApplyControlStyles(Control parent)
    {
        // Preserve explicitly colored status labels, but ensure unstyled
        // WinForms controls inherit readable colors on the dark canvas.
        if (parent is Form form)
        {
            form.BackColor = Canvas;
            form.ForeColor = Text;
        }
        foreach (Control child in parent.Controls)
        {
            if (child is DataGridView or RichTextBox or ScrollBar ||
                child is ScrollableControl { AutoScroll: true } ||
                child is TextBox { Multiline: true, ScrollBars: not ScrollBars.None })
            {
                DarkScrollbarTheme.Attach(child);
            }
            switch (child)
            {
                case Panel panel when panel.BackColor == Color.White:
                    panel.BackColor = Surface;
                    break;
                case Label label when label.ForeColor == Color.Black ||
                                      label.ForeColor == SystemColors.ControlText:
                    label.ForeColor = Text;
                    break;
                case CheckBox check:
                    check.ForeColor = Text;
                    break;
                case RadioButton radio:
                    radio.ForeColor = Text;
                    break;
                case Button button:
                    StyleButton(button, ToneFor(button.Text));
                    break;
                case TextBox input:
                    input.BorderStyle = BorderStyle.FixedSingle;
                    input.BackColor = input.ReadOnly ? SurfaceAlt : Surface;
                    input.ForeColor = Text;
                    break;
                case DataGridView grid when grid.BackgroundColor == Color.White:
                    grid.BackgroundColor = Surface;
                    break;
                case ComboBox combo:
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = Surface;
                    combo.ForeColor = Text;
                    break;
                case NumericUpDown number:
                    number.BorderStyle = BorderStyle.FixedSingle;
                    number.BackColor = Surface;
                    number.ForeColor = Text;
                    break;
            }
            if (child.HasChildren) ApplyControlStyles(child);
        }
    }

    public static void StyleTabs(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(153, 40);
        tabs.BackColor = Canvas;
        foreach (TabPage page in tabs.TabPages) page.BackColor = Surface;
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var canvas = new SolidBrush(Canvas);
            e.Graphics.FillRectangle(canvas, e.Bounds);
            var segment = Rectangle.Inflate(e.Bounds, -4, -4);
            if (selected && segment.Width > 8 && segment.Height > 8)
            {
                using var rounded = RoundedPath(segment, 9);
                using var fill = new SolidBrush(Surface);
                using var border = new Pen(Border, 1f);
                e.Graphics.FillPath(fill, rounded);
                e.Graphics.DrawPath(border, rounded);
            }
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
                new Font("Segoe UI", 9.5F, selected ? FontStyle.Bold : FontStyle.Regular),
                e.Bounds, selected ? Text : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
        };
    }
}
