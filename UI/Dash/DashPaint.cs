using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Drawing shared by the built-in widgets, in the look of the other tabs: the segmented ring of
/// the Status page (IconPainter.Ring, split into its track and its lit part so the track can
/// live in the static layer), the Status bars, the ToggleSwitch look and small chips.
/// </summary>
internal static class DashPaint
{
    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static readonly Color CpuUseColor = Color.FromArgb(0x3C, 0x7D, 0xFF);   // blue: CPU side of the palette (fans = cyan/violet), as on Status

    public static Color TempColor(int t) => t <= 0 ? Theme.Muted : t < 70 ? Theme.Green : t < 85 ? Theme.Amber : Theme.Red;

    // ---- ring (IconPainter.Ring geometry: 40 ticks clockwise from the top) ----------------
    private const int RingSegs = 40;
    private const float RingStep = 360f / RingSegs;
    private const float RingGap = RingStep * 0.38f;

    private static RectangleF RingRect(RectangleF box, out float thick)
    {
        thick = Math.Max(8f, box.Width * 0.11f);
        return new RectangleF(box.X + thick / 2, box.Y + thick / 2, box.Width - thick, box.Width - thick);
    }

    /// <summary>The dim track: all ticks in the border colour (static layer).</summary>
    public static void RingTrack(Graphics g, RectangleF box)
    {
        var r = RingRect(box, out float thick);
        using var p = new Pen(Theme.Border, thick);
        for (int i = 0; i < RingSegs; i++) g.DrawArc(p, r, -90 + i * RingStep + RingGap / 2, RingStep - RingGap);
    }

    /// <summary>The lit ticks for a fraction 0..1, fading from the colour toward white (live layer, over the track).</summary>
    public static void RingLit(Graphics g, RectangleF box, float fraction, Color color)
    {
        int lit = fraction > 0.001f ? (int)Math.Round(Math.Clamp(fraction, 0, 1) * RingSegs) : 0;
        if (lit == 0) return;
        var r = RingRect(box, out float thick);
        for (int i = 0; i < lit; i++)
        {
            using var p = new Pen(Mix(color, Color.White, 0.55f * i / (RingSegs - 1)), thick);
            g.DrawArc(p, r, -90 + i * RingStep + RingGap / 2, RingStep - RingGap);
        }
    }

    /// <summary>The continuous dial's track: one closed ring in the border colour (static layer).</summary>
    public static void RingTrackArc(Graphics g, RectangleF box)
    {
        var r = RingRect(box, out float thick);
        using var p = new Pen(Theme.Border, thick);
        g.DrawEllipse(p, r);
    }

    /// <summary>The continuous dial's lit part for a fraction 0..1, with the same fade toward white as the ticks (live layer).</summary>
    public static void RingLitArc(Graphics g, RectangleF box, float fraction, Color color)
    {
        float sweep = Math.Clamp(fraction, 0, 1) * 360f;
        if (sweep < 1f) return;
        var r = RingRect(box, out float thick);
        const float step = 6f;
        for (float a = 0; a < sweep; a += step)
        {
            float len = Math.Min(step, sweep - a);
            using var p = new Pen(Mix(color, Color.White, 0.55f * a / 360f), thick);
            g.DrawArc(p, r, -90 + a, len + (a + len < sweep ? 0.6f : 0f));   // a hair of overlap, so no seam shows between the steps
        }
    }

    /// <summary>
    /// A widget painted into a bitmap, for a card that shows its picture. The painting goes through
    /// a GDI buffer, as on the canvas: a widget's text is GDI text, and GDI text drawn straight onto
    /// a GDI+ bitmap blends against black whatever is under it, which leaves dark fringes around
    /// every letter (plain to see in the light theme).
    /// </summary>
    public static Bitmap Snapshot(Control owner, int w, int h, Color back, Action<Graphics, Rectangle> paint)
    {
        var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
        using var og = owner.CreateGraphics();
        using var ctx = new BufferedGraphicsContext();   // its own context: the canvas's buffer size stays as it is
        using var buf = ctx.Allocate(og, r);
        buf.Graphics.Clear(back);
        buf.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        try { paint(buf.Graphics, r); } catch { }
        using var bg = Graphics.FromImage(bmp);
        buf.Render(bg);
        return bmp;
    }

    // ---- the palette a widget's colour setting offers (1.45) ----
    // Stored by id, never as a colour code, so a picked colour follows the light and the dark theme.
    // In the order of the colour wheel, warm first (the colours a warning usually wants), the
    // theme's own text colour last.
    public static readonly string[] PaletteIds = { "red", "orange", "amber", "yellow", "lime", "green", "teal", "cyan", "blue", "violet", "pink", "mono" };

    /// <summary>The values a colour setting may hold: "auto" (what the widget paints by itself), then the palette.</summary>
    public static readonly string[] PaletteChoices = new[] { "auto" }.Concat(PaletteIds).ToArray();

    public static Color PaletteColor(string id) => id switch
    {
        "red"    => Theme.Dark ? Color.FromArgb(0xFF, 0x52, 0x47) : Color.FromArgb(0xD9, 0x30, 0x25),
        "orange" => Theme.Dark ? Color.FromArgb(0xFF, 0x8A, 0x3D) : Color.FromArgb(0xD9, 0x64, 0x0F),
        "amber"  => Theme.Amber,
        "yellow" => Theme.Dark ? Color.FromArgb(0xFF, 0xE0, 0x4D) : Color.FromArgb(0xA8, 0x8A, 0x00),
        "lime"   => Theme.Dark ? Color.FromArgb(0xB4, 0xF0, 0x5A) : Color.FromArgb(0x5E, 0x9E, 0x0C),
        "green"  => Theme.Green,
        "teal"   => Theme.Dark ? Color.FromArgb(0x2E, 0xD3, 0xB7) : Color.FromArgb(0x0E, 0x93, 0x84),
        "cyan"   => Theme.Dark ? Color.FromArgb(0x3D, 0xE3, 0xFF) : Color.FromArgb(0x0E, 0x9C, 0xC0),
        "blue"   => Color.FromArgb(0x3C, 0x7D, 0xFF),
        "violet" => Theme.Violet,
        "pink"   => Theme.Red,
        "mono"   => Theme.Text,
        _        => Theme.Accent,
    };

    /// <summary>A colour setting's value as a colour: the palette colour, or <paramref name="own"/> for "auto".</summary>
    public static Color Pick(string id, Color own) => id == "auto" ? own : PaletteColor(id);

    /// <summary>Value above the centre, unit below, sized from the ring (fonts in pixels, like IconPainter.Ring).</summary>
    public static void RingValue(Graphics g, RectangleF box, string value, string unit, DashContext c)
    {
        // a touch larger than the Status rings (0.16 / 0.085): a 1x1 widget ring is 110 px, not 150-240
        var valFont = c.PxFont(box.Width * 0.21f, FontStyle.Bold);
        var unitFont = c.PxFont(box.Width * 0.095f);
        int valH = valFont.Height, unitH = unitFont.Height, gap = (int)(box.Width * 0.02f);
        int blockTop = (int)(box.Y + (box.Height - (valH + gap + unitH)) / 2f);
        TextRenderer.DrawText(g, value, valFont, new Rectangle((int)box.X, blockTop, (int)box.Width, valH),
            Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, unit, unitFont, new Rectangle((int)box.X, blockTop + valH + gap, (int)box.Width, unitH),
            Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
    }

    // ---- bars and switches ---------------------------------------------------------------
    /// <summary>A rounded bar with its filled part (the Status RAM/VRAM/disk bars).</summary>
    public static void Bar(Graphics g, RectangleF r, float frac, Color color)
    {
        frac = Math.Clamp(frac, 0, 1);
        float rad = r.Height / 2f;
        using (var p = Theme.RoundRect(r, rad)) using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, p);
        if (frac > 0)
        {
            var fr = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * frac), r.Height);
            using var p = Theme.RoundRect(fr, rad);
            using var b = new SolidBrush(color);
            g.FillPath(b, p);
        }
    }

    /// <summary>The ToggleSwitch look: accent track with a white knob when on, strong border with a muted knob when off.</summary>
    public static void Switch(Graphics g, Rectangle r, bool on, bool enabled = true)
    {
        var track = !enabled ? Theme.Border : on ? Theme.AccentFill : Theme.BorderStrong;
        var knob = enabled ? Color.White : Theme.Muted;
        using (var path = Theme.RoundRect(r, r.Height / 2f)) using (var b = new SolidBrush(track)) g.FillPath(b, path);
        float pad = 4 * r.Height / 28f, d = r.Height - 2 * pad, kx = on ? r.Right - d - pad : r.X + pad;
        using var kb = new SolidBrush(knob);
        g.FillEllipse(kb, kx, r.Y + pad, d, d);
    }

    /// <summary>A small outlined chip (the scene cards' profile chip): tinted fill, coloured border and text. Returns its width.</summary>
    public static int Chip(Graphics g, string text, Point at, Color fg, Font font, int padX, bool draw = true)
    {
        var sz = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        int w = sz.Width + padX * 2, h = font.Height + padX;
        if (draw)
        {
            var r = new RectangleF(at.X, at.Y, w, h);
            using (var path = Theme.RoundRect(r, h / 2f))
            {
                using (var b = new SolidBrush(Color.FromArgb(36, fg))) g.FillPath(b, path);
                using var pen = new Pen(fg);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, text, font, Rectangle.Round(r), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return w;
    }

    /// <summary>Widget title at the top-left of the inner area; returns the height it took.</summary>
    public static int Title(Graphics g, Rectangle inner, DashContext c, string text, Color? color = null)
    {
        var f = c.Font(11f, FontStyle.Bold);
        TextRenderer.DrawText(g, text, f, new Rectangle(inner.Left, inner.Top, inner.Width, f.Height), color ?? Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        return f.Height;
    }

    /// <summary>Centred muted note (unavailable on this model, no reading yet), wrapped to the area.</summary>
    public static void Note(Graphics g, Rectangle area, DashContext c, string text)
    {
        var f = c.Font(9.5f);
        TextRenderer.DrawText(g, text, f, area, Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }
}
