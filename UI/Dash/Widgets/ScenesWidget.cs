namespace GhostDeck;

/// <summary>
/// Scenes: the title and one row per scene (glyph, name, the profile chip, a run button), as
/// many as the card has room for. A click on a row runs the scene (<see cref="MainDeps.RunScene"/>).
/// The rows are live, because the scene list can change on the Scenarios tab at any time.
///
/// Settings (1.45): the title and its colour, and which scenes the widget shows, by the scene's
/// lasting id, so renaming a scene changes nothing here. In a single cell there is no room for
/// rows: the widget is one button that runs the first of its scenes, which with one scene picked
/// makes a "run this scene" button.
/// </summary>
internal sealed class ScenesWidget : DashWidget
{
    private const string KShow = "show";

    private string _painted = "";
    private readonly List<(Rectangle r, SceneDef scene)> _rows = new();

    public ScenesWidget(DashWidgetDef def, DashOptions opt) : base(def, opt) { }

    /// <summary>The scenes to show, in the order of the Scenarios tab; all of them unless some were picked.</summary>
    private IReadOnlyList<SceneDef> Shown(DashContext c) => Picked(KShow, c.D.Settings.Scenes, s => s.Id);

    /// <summary>No room for the title and a row with its chip and button: the single-cell size.</summary>
    private static bool Button(Rectangle r, DashContext c) => r.Width < c.S(230);

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c)
    {
        Frame(g, r);
        if (!Button(r, c)) DashPaint.Title(g, Inner(r, c), c, Caption, Tint("labelc", Theme.Text));
    }

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        bool button = Button(r, c);
        var inner = button ? Rectangle.Inflate(r, -c.S(10), -c.S(10)) : Inner(r, c);
        int titleH = c.Font(11f, FontStyle.Bold).Height;
        var area = button ? inner : new Rectangle(inner.Left, inner.Top + titleH + c.S(8), inner.Width, inner.Height - titleH - c.S(8));
        var scenes = Shown(c);
        _rows.Clear();
        _painted = Signature(c);
        if (scenes.Count == 0)
        {
            // no scenes at all, or the picked ones are gone
            DashPaint.Note(g, area, c, Lang.T(c.D.Settings.Scenes.Count == 0 ? "scene_empty" : "dash_sel_none"));
            return;
        }
        var nameF = c.Font(10.5f, FontStyle.Bold);
        if (button) { PaintButton(g, inner, scenes[0], nameF, c); return; }
        var chipF = c.Font(8.5f);
        using var glyphF = Dpi.Font("Segoe UI Emoji", 12f);   // scene glyphs are emoji, as on the scene cards
        int rowH = Math.Max(c.S(34), nameF.Height + c.S(12));
        int btn = Math.Max(c.S(26), nameF.Height + c.S(6));
        int y = area.Top;
        foreach (var s in scenes)
        {
            if (y + rowH > area.Bottom) break;
            var row = new Rectangle(area.Left, y, area.Width, rowH);
            // run button on the right
            var br = new Rectangle(row.Right - btn, y + (rowH - btn) / 2, btn, btn);
            RunMark(g, br, c);
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
    }

    private static void RunMark(Graphics g, Rectangle br, DashContext c)
    {
        using (var path = Theme.RoundRect(br, c.Sf(6)))
        {
            using var b = new SolidBrush(Theme.Surface);
            g.FillPath(b, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, "▶", c.Font(9f), br, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>The single-cell look: the scene's glyph, its name and the run mark one under another; the whole card runs it.</summary>
    private void PaintButton(Graphics g, Rectangle inner, SceneDef s, Font nameF, DashContext c)
    {
        using var glyphF = Dpi.Font("Segoe UI Emoji", 17f);
        int glyphH = string.IsNullOrEmpty(s.Glyph) ? 0 : glyphF.Height;
        int nameH = Math.Min(nameF.Height * 2, TextRenderer.MeasureText(s.Name, nameF, new Size(inner.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
        int btn = Math.Max(c.S(26), nameF.Height + c.S(6));
        int g1 = glyphH > 0 ? c.S(6) : 0, g2 = c.S(9);
        int top = inner.Top + Math.Max(0, (inner.Height - (glyphH + g1 + nameH + g2 + btn)) / 2);
        if (glyphH > 0)
            TextRenderer.DrawText(g, s.Glyph, glyphF, new Rectangle(inner.Left, top, inner.Width, glyphH), Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, s.Name, nameF, new Rectangle(inner.Left, top + glyphH + g1, inner.Width, nameH), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        RunMark(g, new Rectangle(inner.Left + (inner.Width - btn) / 2, top + glyphH + g1 + nameH + g2, btn, btn), c);
        _rows.Add((inner, s));
    }

    private string Signature(DashContext c)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(c.D.Settings.Scenes.Count).Append('#');
        foreach (var s in Shown(c)) sb.Append(s.Id).Append('|').Append(s.Name).Append('|').Append(s.Glyph).Append('|').Append(s.Profile).Append(';');
        return sb.ToString();
    }

    public override bool LiveChanged(DashContext c) => Signature(c) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c)
    {
        foreach (var (rr, scene) in _rows)
            if (rr.Contains(p)) { c.D.RunScene(scene); return; }
    }

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c)
    {
        var list = new List<DashOptionDef>();
        var scenes = c.D.Settings.Scenes;
        if (scenes.Count > 1)
            list.Add(new(KShow, DashOptKind.Checks, Lang.T("dash_opt_show")) { Items = scenes.Select(s => (s.Id, s.Name)).ToArray() });   // names alone: the card's font has no emoji for the glyphs
        list.Add(LabelDef(Theme.Text));
        return list;
    }
}
