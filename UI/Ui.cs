using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>Shared themed widgets / drawing helpers for the tab pages.</summary>
internal static class Ui
{
    public static void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Theme.AccentFill;
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.Height = 40;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Faint background grid (ghostdeck.dev texture), drawn under the page content.</summary>
    public static void DrawGrid(Graphics g, Rectangle r)
    {
        const int pitch = 48;
        using var pen = new Pen(Theme.GridLine);
        for (int x = r.Left + pitch; x < r.Right; x += pitch) g.DrawLine(pen, x, r.Top, x, r.Bottom);
        for (int y = r.Top + pitch; y < r.Bottom; y += pitch) g.DrawLine(pen, r.Left, y, r.Right, y);
    }

    /// <summary>
    /// Runs a bulk control rebuild with painting + layout suspended (WM_SETREDRAW), then repaints
    /// once. Without this a rebuild (e.g. Settings after a language change) visibly blanks the
    /// page and repaints control-by-control.
    /// </summary>
    public static void BatchRedraw(Control c, Action work)
    {
        const int WM_SETREDRAW = 0x000B;
        if (!c.IsHandleCreated) { work(); return; }
        SendMessage(c.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        c.SuspendLayout();
        try { work(); }
        finally
        {
            c.ResumeLayout(true);
            SendMessage(c.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            c.Invalidate(true);
        }
    }

    public static void StyleGhost(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Theme.BorderStrong;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = Theme.Surface;
        b.ForeColor = Theme.Text;
        b.Font = new Font("Segoe UI", 10.5f);
        b.Cursor = Cursors.Hand;
        b.Height = 40;
    }

    // Pixel-centre a single glyph inside a rect by its measured ink box. Layout-rect centring
    // (StringFormat/TextRenderer VerticalCenter) leaves glyphs looking off because it centres the
    // font line box, not the glyph; measuring with GenericTypographic and offsetting fixes it.
    public static void CenterGlyph(Graphics g, string glyph, Font font, Color color, RectangleF rect)
    {
        var oldHint = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        var sz = g.MeasureString(glyph, font, rect.Size, fmt);
        var centred = new RectangleF(
            rect.X + (rect.Width - sz.Width) / 2f,
            rect.Y + (rect.Height - sz.Height) / 2f,
            sz.Width, sz.Height);
        using var br = new SolidBrush(color);
        g.DrawString(glyph, font, br, centred, fmt);
        g.TextRenderingHint = oldHint;
    }

    // Word-wrap plain text to at most maxChars per line (WinForms ToolTip does not wrap by itself,
    // so a long hint would run off-screen). Breaks on spaces; overlong words are kept whole.
    public static string Wrap(string text, int maxChars)
    {
        var words = text.Split(' ');
        var sb = new System.Text.StringBuilder();
        int lineLen = 0;
        foreach (var w in words)
        {
            if (lineLen > 0 && lineLen + 1 + w.Length > maxChars) { sb.Append('\n'); lineLen = 0; }
            else if (lineLen > 0) { sb.Append(' '); lineLen++; }
            sb.Append(w);
            lineLen += w.Length;
        }
        return sb.ToString();
    }

    public static void FillCard(Graphics g, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(r, 12);
        using var b = new SolidBrush(Theme.Card);
        g.FillPath(b, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
    }

    public static void Pill(Graphics g, string text, Point at, Color fg)
    {
        var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        var sz = TextRenderer.MeasureText(text, font);
        int w = sz.Width + 32, h = sz.Height + 14;       // bigger padding
        var r = new RectangleF(at.X, at.Y, w, h);
        // outlined badge like the ghostdeck.dev status chips: soft tint + 1px border in the badge colour
        using (var path = Theme.RoundRect(r, 4))
        {
            using (var b = new SolidBrush(Color.FromArgb(26, fg))) g.FillPath(b, path);
            using var pen = new Pen(Color.FromArgb(170, fg));
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, text, font, Rectangle.Round(r), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
