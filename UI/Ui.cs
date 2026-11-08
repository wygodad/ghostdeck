using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>Shared themed widgets / drawing helpers for the tab pages.</summary>
internal static class Ui
{
    /// <summary>
    /// #172: 40 px holds the label up to about 150 % scaling; above that the button grows with
    /// its text so the bottom of the letters is never cut off.
    /// </summary>
    public static int ButtonHeight(Font f) => Math.Max(40, TextRenderer.MeasureText("Ag", f).Height + 12);

    public static void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Theme.AccentFill;
        b.ForeColor = Color.White;
        b.Font = Dpi.Font("Segoe UI", 10.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.Height = ButtonHeight(b.Font);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- text on a page that scrolls ----
    // TextRenderer hands its string to GDI through a raw HDC, so by default it honours NEITHER
    // Graphics.TranslateTransform NOR Graphics.Clip - the two flags below are the only mechanism
    // that makes it read them (measured, see docs/RENDERING.md §5.1). A page that scrolls by
    // ThemedPage.ApplyScroll therefore has to draw every label through these wrappers: without
    // them the cards, lines and dots move with the scroll while every caption stays put, and no
    // clip can stop content from painting over the page header.
    // Adding the flags is safe on an unscrolled Graphics too: the transform is then the identity
    // and honouring the paint clip is what a painter should do anyway.
    public const TextFormatFlags Scrolled =
        TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;

    public static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color, TextFormatFlags flags)
        => TextRenderer.DrawText(g, text, font, bounds, color, flags | Scrolled);

    public static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color)
        => TextRenderer.DrawText(g, text, font, bounds, color, Scrolled);

    public static void DrawText(Graphics g, string text, Font font, Point pt, Color color, TextFormatFlags flags)
        => TextRenderer.DrawText(g, text, font, pt, color, flags | Scrolled);

    public static void DrawText(Graphics g, string text, Font font, Point pt, Color color)
        => TextRenderer.DrawText(g, text, font, pt, color, Scrolled);

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
        // A batch inside a batch (three cards rebuilt in one go, each through RebuildCard) must
        // not switch painting back on halfway: the outermost call owns the single repaint.
        if (!c.IsHandleCreated || !Batching.Add(c)) { work(); return; }
        SendMessage(c.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        c.SuspendLayout();
        try { work(); }
        finally
        {
            c.ResumeLayout(true);
            SendMessage(c.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            c.Invalidate(true);
            Batching.Remove(c);
        }
    }

    private static readonly HashSet<Control> Batching = new();

    public static void StyleGhost(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Theme.BorderStrong;
        b.FlatAppearance.BorderSize = 1;
        // Without explicit hover/press colors WinForms derives them from BackColor, which on the
        // dark surface is imperceptible - the button looked dead under the cursor (user report).
        b.FlatAppearance.MouseOverBackColor = Theme.AccentSoft;
        b.FlatAppearance.MouseDownBackColor = Theme.RowAlt;
        b.BackColor = Theme.Surface;
        b.ForeColor = Theme.Text;
        b.Font = Dpi.Font("Segoe UI", 10.5f);
        b.Cursor = Cursors.Hand;
        b.Height = ButtonHeight(b.Font);
    }

    // Pixel-centre a single glyph inside a rect by its measured ink box. Layout-rect centring
    // (StringFormat/TextRenderer VerticalCenter) leaves glyphs looking off because it centres the
    // font line box, not the glyph; measuring with GenericTypographic and offsetting fixes it.
    private static readonly Dictionary<(string glyph, string family, float size), (PointF shift, bool ok)> InkShift = new();

    /// <summary>
    /// Like <see cref="CenterGlyph"/>, but centred on the glyph's INK, not its layout box: symbol
    /// fonts leave uneven side bearings and a baseline gap, so a layout-centred ❄ or ▦ sits a
    /// pixel or three low and left in a small square. The ink box is measured once per glyph and
    /// font (a tiny offscreen render) and the shift is cached.
    /// </summary>
    public static void CenterGlyphInk(Graphics g, string glyph, Font font, Color color, RectangleF rect)
    {
        var key = (glyph, font.FontFamily.Name, font.Size);
        if (!InkShift.TryGetValue(key, out var ink))
        {
            ink = (PointF.Empty, false);
            try
            {
                int side = (int)Math.Ceiling(font.Size * 3) + 8;
                using var bmp = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var mg = Graphics.FromImage(bmp))
                {
                    mg.Clear(Color.Transparent);
                    CenterGlyph(mg, glyph, font, Color.White, new RectangleF(0, 0, side, side));
                }
                int minX = side, minY = side, maxX = -1, maxY = -1;
                var data = bmp.LockBits(new Rectangle(0, 0, side, side), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        for (int y = 0; y < side; y++)
                        {
                            byte* row = (byte*)data.Scan0 + y * data.Stride;
                            for (int x = 0; x < side; x++)
                                if (row[x * 4 + 3] > 40) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                        }
                    }
                }
                finally { bmp.UnlockBits(data); }
                if (maxX >= minX)
                {
                    // where the ink's centre landed against the square's centre: draw shifted by the opposite
                    float cx = (minX + maxX + 1) / 2f, cy = (minY + maxY + 1) / 2f;
                    ink = (new PointF(side / 2f - cx, side / 2f - cy), true);
                }
            }
            catch { }
            InkShift[key] = ink;
        }
        CenterGlyph(g, glyph, font, color, ink.ok ? new RectangleF(rect.X + ink.shift.X, rect.Y + ink.shift.Y, rect.Width, rect.Height) : rect);
    }

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
        var font = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
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

    /// <summary>
    /// Put text on the clipboard and say whether it got there. <c>Clipboard.SetText</c> throws
    /// outright whenever another process has the clipboard open, which is common and momentary, and
    /// every report flow used to swallow that in an empty catch. The file was still written, but the
    /// clipboard kept the PREVIOUS report, so a Ctrl+V pasted the wrong run into an issue and
    /// nothing on screen said otherwise. SetDataObject's retry loop is the documented answer to
    /// that error; copy: true also flushes the data so it survives the app closing.
    /// </summary>
    public static bool CopyText(string text)
    {
        // SetText REFUSES an empty string, so the old code was a guaranteed no-op there.
        // SetDataObject accepts it and would hand the shell an empty payload, wiping whatever the
        // user had copied. Nothing to copy means leave the clipboard alone.
        if (string.IsNullOrEmpty(text)) return false;

        try { Clipboard.SetDataObject(text, true, 10, 120); }
        catch { return false; }

        // Best effort confirmation, and deliberately asymmetric. Reading straight back after the
        // flush often returns an empty string even though the write worked, so an empty or
        // unreadable clipboard is treated as no evidence. Only text that is demonstrably
        // SOMETHING ELSE proves the report did not land. Reading through GetText rather than
        // ContainsText matters: a clobber with non-text data leaves ContainsText false, which
        // would have short-circuited a genuine failure into a reported success.
        try
        {
            string back = Clipboard.GetText();
            if (back.Length > 0 && back != text) return false;
        }
        catch { }
        return true;
    }
}
