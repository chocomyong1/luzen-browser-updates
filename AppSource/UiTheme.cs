using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

// A compact, high-contrast business UI. Styling only: this class never
// changes listing data, job selection, or any browser automation behavior.
internal static class UiTheme
{
    public static readonly Color Canvas = Color.FromArgb(247, 249, 252);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(240, 244, 249);
    public static readonly Color Border = Color.FromArgb(222, 230, 239);
    public static readonly Color Text = Color.FromArgb(31, 46, 68);
    public static readonly Color Muted = Color.FromArgb(99, 116, 139);
    public static readonly Color Navy = Color.FromArgb(23, 38, 59);
    public static readonly Color Primary = Color.FromArgb(35, 104, 211);
    public static readonly Color PrimaryHover = Color.FromArgb(28, 87, 180);
    public static readonly Color Success = Color.FromArgb(13, 128, 98);
    public static readonly Color Danger = Color.FromArgb(190, 65, 72);
    public static readonly Color Warning = Color.FromArgb(161, 100, 27);
    public static readonly Color WarningSurface = Color.FromArgb(255, 247, 228);

    public enum ButtonTone { Neutral, Primary, Success, Danger, Dark }

    public static void StyleButton(Button button, ButtonTone tone = ButtonTone.Neutral)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(6, 0, 6, 0);
        button.Font = new Font("맑은 고딕", 9F,
            tone == ButtonTone.Primary ? FontStyle.Bold : FontStyle.Regular);
        button.FlatAppearance.BorderSize = tone == ButtonTone.Neutral ? 1 : 0;
        button.FlatAppearance.BorderColor = Border;
        switch (tone)
        {
            case ButtonTone.Primary:
                button.BackColor = Primary;
                button.ForeColor = Color.White;
                button.FlatAppearance.MouseOverBackColor = PrimaryHover;
                break;
            case ButtonTone.Success:
                button.BackColor = Success;
                button.ForeColor = Color.White;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(9, 109, 84);
                break;
            case ButtonTone.Danger:
                button.BackColor = Danger;
                button.ForeColor = Color.White;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(165, 52, 58);
                break;
            case ButtonTone.Dark:
                button.BackColor = Color.FromArgb(44, 61, 83);
                button.ForeColor = Color.White;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 81, 110);
                break;
            default:
                button.BackColor = Surface;
                button.ForeColor = Text;
                button.FlatAppearance.MouseOverBackColor = SurfaceAlt;
                break;
        }
        button.FlatAppearance.MouseDownBackColor = tone == ButtonTone.Neutral
            ? Color.FromArgb(224, 232, 243) : button.FlatAppearance.MouseOverBackColor;
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
        foreach (Control child in parent.Controls)
        {
            switch (child)
            {
                case Button button:
                    StyleButton(button, ToneFor(button.Text));
                    break;
                case TextBox input:
                    input.BorderStyle = BorderStyle.FixedSingle;
                    if (!input.ReadOnly) input.BackColor = Surface;
                    input.ForeColor = Text;
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
        tabs.ItemSize = new Size(174, 34);
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            var background = selected ? Surface : SurfaceAlt;
            using var brush = new SolidBrush(background);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text,
                new Font("맑은 고딕", 9F, selected ? FontStyle.Bold : FontStyle.Regular),
                e.Bounds, selected ? Primary : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis);
            if (selected)
                using (var accent = new SolidBrush(Primary))
                    e.Graphics.FillRectangle(accent,
                        new Rectangle(e.Bounds.X + 11, e.Bounds.Bottom - 3,
                            Math.Max(0, e.Bounds.Width - 22), 3));
        };
    }
}
