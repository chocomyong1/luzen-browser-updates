using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

// Style the native scrollbar windows without replacing WinForms scrolling.
// This preserves wheel, keyboard, drag, and grid row-position behavior.
internal static class DarkScrollbarTheme
{
    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    public static void Attach(Control control)
    {
        if (Attached.TryGetValue(control, out _)) return;
        Attached.Add(control, new object());

        control.HandleCreated += (_, _) =>
        {
            Apply(control);
            if (control is DataGridView grid) AttachGridScrollbars(grid);
        };
        control.SystemColorsChanged += (_, _) => Apply(control);

        if (control is DataGridView dataGrid)
        {
            // DataGridView owns separate VScrollBar/HScrollBar child windows.
            // Their handles may be created or recreated after grid initialization.
            dataGrid.ControlAdded += (_, e) =>
            {
                if (e.Control is ScrollBar) Attach(e.Control);
            };
            AttachGridScrollbars(dataGrid);
        }

        Apply(control);
    }

    private static void AttachGridScrollbars(DataGridView grid)
    {
        foreach (Control child in grid.Controls)
        {
            if (child is ScrollBar) Attach(child);
        }
    }

    private static void Apply(Control control)
    {
        if (!control.IsHandleCreated || control.IsDisposed) return;

        try
        {
            // Default system colors take precedence in accessibility/high-contrast mode.
            SetWindowTheme(control.Handle,
                SystemInformation.HighContrast ? null : "DarkMode_Explorer", null);
        }
        catch (DllNotFoundException) { } // Non-supported Windows configurations.
        catch (EntryPointNotFoundException) { }
    }
}
