using System.Drawing.Drawing2D;

namespace MSIProfileSwitcher;

/// <summary>Vector icons drawn with GDI+ (no icon font dependency) for tiles and gauges.</summary>
internal static class IconPainter
{
    public static void Scenario(Graphics g, ProfileId id, RectangleF box, Color c, float stroke)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        float s = Math.Min(box.Width, box.Height) / 2f;
        using var pen = new Pen(c, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(c);

        switch (id)
        {
            case ProfileId.Silent:   // crescent moon (even-odd fill)
            {
                var outer = new RectangleF(cx - s, cy - s, s * 2, s * 2);
                var cut = new RectangleF(cx - s * 0.55f, cy - s * 1.05f, s * 1.8f, s * 1.8f);
                using var p = new GraphicsPath { FillMode = FillMode.Alternate };
                p.AddEllipse(outer);
                p.AddEllipse(cut);
                g.FillPath(brush, p);
                break;
            }
            case ProfileId.Balanced: // balance scale
            {
                float top = cy - s * 0.75f;
                g.DrawLine(pen, cx, top, cx, cy + s * 0.7f);                 // post
                g.DrawLine(pen, cx - s * 0.5f, cy + s * 0.7f, cx + s * 0.5f, cy + s * 0.7f); // base
                g.DrawLine(pen, cx - s * 0.8f, top, cx + s * 0.8f, top);     // beam
                float pan = s * 0.42f;
                g.DrawArc(pen, cx - s * 0.8f - pan, top + s * 0.15f, pan * 2, pan * 2, 20, 140); // left pan
                g.DrawArc(pen, cx + s * 0.8f - pan, top + s * 0.15f, pan * 2, pan * 2, 20, 140); // right pan
                break;
            }
            case ProfileId.Extreme:  // speedometer gauge
            {
                var arc = new RectangleF(cx - s * 0.85f, cy - s * 0.55f, s * 1.7f, s * 1.7f);
                g.DrawArc(pen, arc, 180, 180);
                g.DrawLine(pen, cx, cy + s * 0.3f, cx + s * 0.5f, cy - s * 0.35f); // needle
                using (var b2 = new SolidBrush(c)) g.FillEllipse(b2, cx - stroke, cy + s * 0.3f - stroke, stroke * 2, stroke * 2);
                break;
            }
            case ProfileId.SuperBattery: // leaf
            {
                using var p = new GraphicsPath();
                var a = new PointF(cx - s * 0.55f, cy + s * 0.6f);
                var b = new PointF(cx + s * 0.6f, cy - s * 0.6f);
                p.AddBezier(a, new PointF(cx - s * 0.7f, cy - s * 0.4f), new PointF(cx + s * 0.1f, cy - s * 0.8f), b);
                p.AddBezier(b, new PointF(cx + s * 0.1f, cy + s * 0.2f), new PointF(cx - s * 0.2f, cy + s * 0.5f), a);
                g.DrawPath(pen, p);
                g.DrawLine(pen, a.X + s * 0.1f, a.Y - s * 0.1f, b.X - s * 0.2f, b.Y + s * 0.2f); // midrib
                break;
            }
        }
    }

    /// <summary>Ring gauge with a centred value; fraction 0..1 fills the accent arc.</summary>
    public static void Ring(Graphics g, RectangleF box, float fraction, Color color,
                            string value, string unit, string label)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float thick = Math.Max(8f, box.Width * 0.11f);
        var r = new RectangleF(box.X + thick / 2, box.Y + thick / 2, box.Width - thick, box.Width - thick);
        using (var track = new Pen(Theme.Border, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(track, r, 0, 360);
        if (fraction > 0.001f)
            using (var arc = new Pen(color, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, r, -90, Math.Clamp(fraction, 0, 1) * 360);

        // Fonts sized in PIXELS (GraphicsUnit.Pixel) so DPI is not applied twice
        // (box.Width is already in device pixels). Value sits above centre, unit below,
        // both measured from their own height so they never clip or overlap.
        using var valFont = new Font("Segoe UI", box.Width * 0.16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var unitFont = new Font("Segoe UI", box.Width * 0.085f, FontStyle.Regular, GraphicsUnit.Pixel);
        int valH = valFont.Height, unitH = unitFont.Height, gap = (int)(box.Width * 0.02f);
        int blockTop = (int)(box.Y + (box.Height - (valH + gap + unitH)) / 2f);
        TextRenderer.DrawText(g, value, valFont,
            new Rectangle((int)box.X, blockTop, (int)box.Width, valH),
            Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, unit, unitFont,
            new Rectangle((int)box.X, blockTop + valH + gap, (int)box.Width, unitH),
            Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        var labelFont = new Font("Segoe UI", 10f);
        TextRenderer.DrawText(g, label, labelFont,
            new Rectangle((int)box.X - 16, (int)(box.Bottom + 12), (int)box.Width + 32, labelFont.Height + 4),
            Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }
}
