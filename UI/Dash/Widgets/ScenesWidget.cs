namespace GhostDeck;

/// <summary>
/// Scenes: the title and one row per scene (glyph, name, the profile chip, a run button), as
/// many as the card has room for. A click on a row runs the scene (<see cref="MainDeps.RunScene"/>).
/// The rows are live, because the scene list can change on the Scenarios tab at any time.
/// </summary>
internal sealed class ScenesWidget : DashWidget
{
    private string _painted = "";
    private readonly List<(Rectangle r, SceneDef scene)> _rows = new();

    public ScenesWidget(DashWidgetDef def, DashOptions opt) : base(def, opt) { }

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        DashPaint.Title(g, Inner(r, c), c, Lang.T(NameKey));
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var inner = Inner(r, c);
        int titleH = c.Font(11f, FontStyle.Bold).Height;
        var area = new Rectangle(inner.Left, inner.Top + titleH + c.S(8), inner.Width, inner.Height - titleH - c.S(8));
        var scenes = c.D.Settings.Scenes;
        _rows.Clear();
        if (scenes.Count == 0)
        {
            DashPaint.Note(g, area, c, Lang.T("scene_empty"));
            _painted = Signature(c);
            return;
        }
        var nameF = c.Font(10.5f, FontStyle.Bold);
        var chipF = c.Font(8.5f);
        var glyphF = Dpi.Font("Segoe UI Emoji", 12f);   // scene glyphs are emoji, as on the scene cards
        int rowH = Math.Max(c.S(34), nameF.Height + c.S(12));
        int btn = Math.Max(c.S(26), nameF.Height + c.S(6));
        int y = area.Top;
        foreach (var s in scenes)
        {
            if (y + rowH > area.Bottom) break;
            var row = new Rectangle(area.Left, y, area.Width, rowH);
            // run button on the right
            var br = new Rectangle(row.Right - btn, y + (rowH - btn) / 2, btn, btn);
            using (var path = Theme.RoundRect(br, c.Sf(6)))
            {
                using var b = new SolidBrush(Theme.Surface);
                g.FillPath(b, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, "▶", c.Font(9f), br, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int right = br.Left - c.S(8);
            // profile chip left of the button
            if (!string.IsNullOrEmpty(s.Profile) && Enum.TryParse<ProfileId>(s.Profile, out var pid))
            {
                string label = Profiles.Get(pid).Label;
                var pc = Theme.Profile(c.D.ColorOf(pid));
                int cw = DashPaint.Chip(g, label, Point.Empty, pc, chipF, c.S(7), draw: false);
                int ch = chipF.Height + c.S(7);
                if (cw < area.Width / 2)
                {
                    DashPaint.Chip(g, label, new Point(right - cw, y + (rowH - ch) / 2), pc, chipF, c.S(7));
                    right -= cw + c.S(8);
                }
            }
            // glyph and name
            int gx = row.Left, gw = string.IsNullOrEmpty(s.Glyph) ? 0 : c.S(26);
            if (gw > 0)
                TextRenderer.DrawText(g, s.Glyph, glyphF, new Rectangle(gx, y, gw, rowH), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, s.Name, nameF, new Rectangle(gx + gw + (gw > 0 ? c.S(6) : 0), y, Math.Max(10, right - gx - gw - c.S(6)), rowH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
            _rows.Add((row, s));
            y += rowH;
        }
        glyphF.Dispose();
        _painted = Signature(c);
    }

    private static string Signature(DashContext c)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in c.D.Settings.Scenes) sb.Append(s.Name).Append('|').Append(s.Glyph).Append('|').Append(s.Profile).Append(';');
        return sb.ToString();
    }

    public override bool LiveChanged(DashContext c) => Signature(c) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c)
    {
        foreach (var (rr, scene) in _rows)
            if (rr.Contains(p)) { c.D.RunScene(scene); return; }
    }
}
