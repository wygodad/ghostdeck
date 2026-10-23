using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>Vector icons drawn with GDI+ (no icon font dependency) for tiles and gauges.</summary>
internal static class IconPainter
{
    private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>
    /// The icon of a turbo-mode group (#117): a rounded tile in the group colour at low alpha with
    /// the symbol on top - a leaf (drawn, Segoe MDL2 has none) for less power, MDL2 glyphs for the
    /// rest (LightningBolt E945, the flame ECAD, Up E74A, a circled question mark E9CE).
    /// <paramref name="ink"/> overrides the symbol colour (white on a highlighted list row).
    /// </summary>
    // One font per pixel size, kept: a list or a table draws a dozen icons per repaint, and
    // a font per icon is avoidable work (harness: one icon 0.04 ms with the cached font).
    private static readonly Dictionary<int, Font> _glyphFonts = new();
    private static Font GlyphFont(float px)
    {
        int key = (int)Math.Round(px * 4);
        lock (_glyphFonts)
        {
            if (!_glyphFonts.TryGetValue(key, out var f)) _glyphFonts[key] = f = Dpi.Font("Segoe MDL2 Assets", key / 4f, GraphicsUnit.Pixel);
            return f;
        }
    }

    public static void BoostGroup(Graphics g, PowerPlan.BoostGroup grp, RectangleF box, Color c, Color? ink = null)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float r = Math.Max(2f, box.Width * 0.24f);
        using (var tile = new GraphicsPath())
        {
            tile.AddArc(box.X, box.Y, r * 2, r * 2, 180, 90);
            tile.AddArc(box.Right - r * 2, box.Y, r * 2, r * 2, 270, 90);
            tile.AddArc(box.Right - r * 2, box.Bottom - r * 2, r * 2, r * 2, 0, 90);
            tile.AddArc(box.X, box.Bottom - r * 2, r * 2, r * 2, 90, 90);
            tile.CloseFigure();
            using var tb = new SolidBrush(Color.FromArgb(ink != null ? 60 : 46, c));
            g.FillPath(tb, tile);
        }
        Color fg = ink ?? c;
        float s = Math.Min(box.Width, box.Height);
        if (grp == PowerPlan.BoostGroup.Less)
        {
            // a leaf on a 32-unit grid: two arcs meeting at the tip, a midrib and a short stem
            float u = s / 32f;
            PointF P(float gx, float gy) => new(box.X + gx * u, box.Y + gy * u);
            using var leaf = new GraphicsPath();
            leaf.AddBezier(P(8f, 24f), P(6f, 13f), P(14f, 7f), P(25f, 7f));
            leaf.AddBezier(P(25f, 7f), P(26f, 18f), P(19f, 26f), P(8f, 24f));
            leaf.CloseFigure();
            using var lb = new SolidBrush(fg);
            g.FillPath(lb, leaf);
            using var rib = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1f, 1.4f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(rib, P(10.5f, 21.5f), P(21f, 11f));
            using var stem = new Pen(fg, Math.Max(1f, 2f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(stem, P(8.5f, 23.5f), P(5.5f, 26.5f));
        }
        else
        {
            string glyph = grp switch
            {
                PowerPlan.BoostGroup.Turbo => "",
                PowerPlan.BoostGroup.TurboMax => "",
                PowerPlan.BoostGroup.AskMore => "",
                _ => "",
            };
            var f = GlyphFont(s * 0.66f);
            using var gb = new SolidBrush(fg);
            using var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var oldHint = g.TextRenderingHint;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(glyph, f, gb, new RectangleF(box.X, box.Y + s * 0.02f, box.Width, box.Height), sf);
            g.TextRenderingHint = oldHint;
        }
        g.SmoothingMode = old;
    }

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
                            string value, string unit, string label, string? sub = null, float labelWidth = 0)
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
        using var valFont = Dpi.Font("Segoe UI", box.Width * 0.16f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var unitFont = Dpi.Font("Segoe UI", box.Width * 0.085f, FontStyle.Regular, GraphicsUnit.Pixel);
        int valH = valFont.Height, unitH = unitFont.Height, gap = (int)(box.Width * 0.02f);
        int blockTop = (int)(box.Y + (box.Height - (valH + gap + unitH)) / 2f);
        TextRenderer.DrawText(g, value, valFont,
            new Rectangle((int)box.X, blockTop, (int)box.Width, valH),
            Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, unit, unitFont,
            new Rectangle((int)box.X, blockTop + valH + gap, (int)box.Width, unitH),
            Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        var labelFont = Dpi.Font("Segoe UI", 10f);
        int labelY = (int)(box.Bottom + 12);
        // the label may use more than the ring's width (its column, when the ring sits centred in a wider one)
        int lw = (int)Math.Max(box.Width + 32, labelWidth), lx = (int)(box.X + box.Width / 2 - lw / 2f);
        TextRenderer.DrawText(g, label, labelFont,
            new Rectangle(lx, labelY, lw, labelFont.Height + 4),
            Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);   // dpi-ok: ring label under a gauge sized with the page; last resort for a very long translation
        if (!string.IsNullOrEmpty(sub))
        {
            var subFont = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, sub, subFont,
                new Rectangle(lx, labelY + labelFont.Height + 4, lw, subFont.Height + 4),
                color, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);   // dpi-ok: ring label under a gauge sized with the page; last resort for a very long translation
        }
    }
}
