using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>Vector icons drawn with GDI+ (no icon font dependency) for tiles and gauges.</summary>
internal static class IconPainter
{
    private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void Scenario(Graphics g, ProfileId id, RectangleF box, Color c, float stroke)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        float s = Math.Min(box.Width, box.Height) / 2f;
        using var pen = new Pen(c, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(c);

        switch (id)
        {
            case ProfileId.Silent:   // quill feather (whisper quiet): rounded nib + shaft + barb divider
            {
                float u = s / 16f;   // 32-unit design grid mapped onto the icon box
                PointF P(float gx, float gy) => new(cx + (gx - 16f) * u, cy + (gy - 16f) * u);
                using var p = new GraphicsPath();
                p.AddLine(P(6.7f, 14f), P(6.7f, 25.3f));                              // left edge
                p.AddLine(P(6.7f, 25.3f), P(18f, 25.3f));                             // bottom edge
                p.AddLine(P(18f, 25.3f), P(27f, 16.3f));                              // lower diagonal
                p.AddArc(P(13.36f, 2.64f).X, P(13.36f, 2.64f).Y, 16f * u, 16f * u, 45f, -180f); // rounded tip
                p.CloseFigure();
                g.DrawPath(pen, p);
                g.DrawLine(pen, P(21.3f, 10.7f), P(2.7f, 29.3f));                     // shaft down to the quill
                g.DrawLine(pen, P(23.3f, 20f), P(12f, 20f));                          // barb divider
                break;
            }
            case ProfileId.Balanced: // balance scale with triangle pans
            {
                float top = cy - s * 0.70f, baseY = cy + s * 0.72f;
                g.DrawLine(pen, cx, top - s * 0.08f, cx, baseY);                    // post
                g.DrawLine(pen, cx - s * 0.40f, baseY, cx + s * 0.40f, baseY);      // base
                g.DrawLine(pen, cx - s * 0.74f, top, cx + s * 0.74f, top);          // beam
                foreach (float ex in new[] { cx - s * 0.74f, cx + s * 0.74f })
                {
                    float pan = s * 0.32f, py = top + s * 0.48f;
                    g.DrawLine(pen, ex, top, ex - pan, py);                          // hanger, left
                    g.DrawLine(pen, ex, top, ex + pan, py);                          // hanger, right
                    g.DrawArc(pen, ex - pan, py - pan * 0.9f, pan * 2, pan * 1.8f, 0, 180); // bowl
                }
                break;
            }
            case ProfileId.Extreme:  // lightning bolt
            {
                using var p = new GraphicsPath();
                p.AddPolygon(new[]
                {
                    new PointF(cx + s * 0.20f, cy - s * 0.92f),
                    new PointF(cx - s * 0.50f, cy + s * 0.10f),
                    new PointF(cx - s * 0.06f, cy + s * 0.10f),
                    new PointF(cx - s * 0.20f, cy + s * 0.92f),
                    new PointF(cx + s * 0.50f, cy - s * 0.10f),
                    new PointF(cx + s * 0.06f, cy - s * 0.10f),
                });
                g.DrawPath(pen, p);
                break;
            }
            case ProfileId.SuperBattery: // battery with charge bars
            {
                float w = s * 1.05f, h = s * 1.45f;
                var body = new RectangleF(cx - w / 2, cy - h / 2 + s * 0.14f, w, h);
                using (var p = Theme.RoundRect(body, s * 0.16f)) g.DrawPath(pen, p);
                g.DrawLine(pen, cx - s * 0.20f, body.Y - s * 0.16f, cx + s * 0.20f, body.Y - s * 0.16f); // terminal
                float barW = w - s * 0.42f, barH = s * 0.16f, bx = cx - barW / 2;
                for (int i = 0; i < 3; i++)
                {
                    float by = body.Bottom - s * 0.24f - barH - i * (barH + s * 0.14f);
                    using var rp = Theme.RoundRect(new RectangleF(bx, by, barW, barH), barH / 2);
                    g.FillPath(brush, rp);
                }
                break;
            }
        }
    }

    /// <summary>Ring gauge with a centred value; fraction 0..1 lights the tick segments.</summary>
    public static void Ring(Graphics g, RectangleF box, float fraction, Color color,
                            string value, string unit, string label, string? sub = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float thick = Math.Max(8f, box.Width * 0.11f);
        var r = new RectangleF(box.X + thick / 2, box.Y + thick / 2, box.Width - thick, box.Width - thick);
        // segmented gauge (ghostdeck.dev mockup): ticks clockwise from the top, lit part fades
        // from the base colour to a lighter tint, the rest stays a dim track
        const int segs = 40;
        const float step = 360f / segs;
        const float segGap = step * 0.38f;
        int lit = fraction > 0.001f ? (int)Math.Round(Math.Clamp(fraction, 0, 1) * segs) : 0;
        for (int i = 0; i < segs; i++)
        {
            var cc = i < lit ? Mix(color, Color.White, 0.55f * i / (segs - 1)) : Theme.Border;
            using var p = new Pen(cc, thick);
            g.DrawArc(p, r, -90 + i * step + segGap / 2, step - segGap);
        }

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
        int labelY = (int)(box.Bottom + 12);
        TextRenderer.DrawText(g, label, labelFont,
            new Rectangle((int)box.X - 16, labelY, (int)box.Width + 32, labelFont.Height + 4),
            Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrEmpty(sub))
        {
            var subFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, sub, subFont,
                new Rectangle((int)box.X - 16, labelY + labelFont.Height + 4, (int)box.Width + 32, subFont.Height + 4),
                color, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
    }
}
