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
