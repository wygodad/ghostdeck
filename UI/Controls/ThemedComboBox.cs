using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>
/// DropDownList combo that honours the app theme (the stock ComboBox keeps a white field/button/list in
/// dark mode). Items are owner-drawn; the drop button, chevron and border are repainted after the default
/// paint so nothing stays light. Read theme colours at paint time so a light/dark switch just needs Invalidate.
/// </summary>
public sealed class ThemedComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? app, string? idList);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore; public int fIncUpdate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    public ThemedComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 30;   // roomy rows in the drop-down list
    }

    // Disable visual styles so the drop-down list scrollbar/border follow the flat dark look too.
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetWindowTheme(Handle, "", ""); }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hWnd, ref COMBOBOXINFO info);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int LB_GETTOPINDEX = 0x018E, LB_SETTOPINDEX = 0x0197;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int cbSize; public RECT rcItem, rcButton; public int stateButton;
        public IntPtr hwndCombo, hwndItem, hwndList;
    }

    // Windows routes the wheel to the control under the cursor, and a stock combo responds by
    // CHANGING ITS VALUE - so scrolling Settings past the language combo switched the language
    // and the page stopped scrolling (discussion #9). Never let the wheel change the value:
    // with the list closed, hand the scroll to the nearest scrollable ancestor; with the list
    // open, scroll the drop-down listbox itself (LB_SETTOPINDEX on its window).
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs h) h.Handled = true;
        if (DroppedDown)
        {
            var info = new COMBOBOXINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<COMBOBOXINFO>() };
            if (GetComboBoxInfo(Handle, ref info) && info.hwndList != IntPtr.Zero)
            {
                int top = (int)SendMessage(info.hwndList, LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
                int next = Math.Max(0, top - Math.Sign(e.Delta) * SystemInformation.MouseWheelScrollLines);
                SendMessage(info.hwndList, LB_SETTOPINDEX, (IntPtr)next, IntPtr.Zero);
            }
            return;
        }
        for (Control? p = Parent; p != null; p = p.Parent)
            if (p is ScrollableControl sc && sc.AutoScroll)
            {
                sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, -sc.AutoScrollPosition.Y - e.Delta);
                break;
            }
    }

    // Drop-down LIST rows. The CLOSED field can also arrive here (DrawItemState.ComboBoxEdit),
    // e.g. when the combo merely receives focus - clicking an overlay checkbox pushed focus to
    // the Position combo and its field lit up solid blue (discussion #9). Paint the field like
    // the normal closed state instead: no selection highlight outside the drop-down list.
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) { e.DrawBackground(); return; }
        bool field = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool sel = !field && (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? Theme.AccentFill : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
        var fore = sel ? Color.White : Theme.Text;
        var rect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, rect, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // Fully own the closed-field paint: the stock combo flashes its button light on hover/press before an
    // overpaint can cover it. We validate the update region ourselves and never let the base class paint,
    // so there is no light frame at all.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PAINT)
        {
            BeginPaint(Handle, out var ps);
            try { using var g = Graphics.FromHdc(ps.hdc); PaintField(g); }
            finally { EndPaint(Handle, ref ps); }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    private void PaintField(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
        var textRect = new Rectangle(8, 0, Width - 30, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        int cx = Width - 13, cy = Height / 2;
        using (var pen = new Pen(Theme.Muted, 1.6f))
            g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
        using (var bp = new Pen(Theme.Border))
            g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
    }
}
