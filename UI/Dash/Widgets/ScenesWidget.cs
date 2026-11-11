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
///
/// Looks (1.45, "view"): the list (as in 1.44, the default), tiles (a large glyph over the name,
/// the scene's profile as a coloured line at the bottom) and buttons (compact pills with the
/// glyph and the name, the profile as a dot; twice as many scenes fit as in the list). In every
/// look a click on a scene runs it. Tiles and buttons also allow the wide 4x1 size.
/// </summary>
internal sealed class ScenesWidget : DashWidget
{
    private const string KShow = "show", KView = "view";
    private static readonly string[] Views = { "list", "tiles", "buttons" };

    // A scene's name is the user's own text and may be any length. In every look it takes two
    // lines at most; a name that needs more ends with three dots, so it never runs into what is
    // under it. The widget breaks the name itself (Fit): the system's own ellipsis cuts a wrapped
    // text after its first line. A name that fits its lines is drawn exactly as before.
    private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
    private const string Dots = "…";
    private readonly Dictionary<(string name, int width, int font, int lines), string> _fitted = new();

    /// <summary>The name as it is drawn in <paramref name="lines"/> lines (one or two) of this width; kept, a widget paints the same names again and again.</summary>
    private string Fitted(string name, Font f, int width, int lines)
    {
        var key = (name, width, f.Height, lines);
        if (_fitted.TryGetValue(key, out var done)) return done;
        if (_fitted.Count > 256) _fitted.Clear();
        return _fitted[key] = Fit(name, f, Math.Max(1, width), lines);
    }

    /// <summary>
    /// The height two lines of the name take as the system draws them. Twice the font's own
    /// height is a pixel or two less at some scales, and a box of that height cuts the second
    /// line's descenders.
    /// </summary>
    internal static int TwoLinesH(Font f) => Math.Max(f.Height * 2, TextRenderer.MeasureText("Ag\nAg", f, Size.Empty, TextFormatFlags.NoPadding).Height);

    internal static string Fit(string name, Font f, int width, int lines)
    {
        int W(string s) => TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        if (W(name) <= width) return name;
        // the longest head of a text that fits with the dots after it; never half of a surrogate pair
        int Head(string s, string tail)
        {
            int lo = 0, hi = s.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (W(s[..mid].TrimEnd() + tail) <= width) lo = mid; else hi = mid - 1;
            }
            return lo > 0 && char.IsHighSurrogate(s[lo - 1]) ? lo - 1 : lo;
        }
        string Cut(string s) => W(s) <= width ? s : s[..Head(s, Dots)].TrimEnd() + Dots;
        if (lines <= 1) return Cut(name);
        // two lines the way the system breaks them: nothing to do unless a third line or a word wider than the line is in the way
        var natural = TextRenderer.MeasureText(name, f, new Size(width, int.MaxValue), Wrap);
        if (natural.Height <= TwoLinesH(f) && natural.Width <= width) return name;
        // the first line: whole words while they fit; a first word wider than the line is broken where it fits
        int brk = -1;
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] != ' ') continue;
            if (W(name[..i]) > width) break;
            brk = i;
        }
        string first, rest;
        if (brk > 0) { first = name[..brk]; rest = name[(brk + 1)..].TrimStart(); }
        else { int n = Math.Max(1, Head(name, "")); first = name[..n]; rest = name[n..].TrimStart(); }
        return rest.Length == 0 ? first : first + "\n" + Cut(rest);
    }

    private readonly string _view;
    private string _painted = "";
    private readonly List<(Rectangle r, SceneDef scene)> _rows = new();

    public ScenesWidget(DashWidgetDef def, DashOptions opt) : base(def, opt)
    {
        _view = opt.Choice(KView, "list", Views);
        if (_view != "list") Sizes = Sz((2, 1), (2, 2), (4, 1), (1, 1));
    }

    /// <summary>The scenes to show, in the order of the Scenarios tab; all of them unless some were picked.</summary>
    private IReadOnlyList<SceneDef> Shown(DashContext c) => Picked(KShow, c.D.Settings.Scenes, s => s.Id);

    /// <summary>
    /// Scenes are picked by the id they have on the machine they were made on, so a pick that
    /// came in a file from another machine names scenes that are not here. Such a pick is taken
    /// off and the widget shows every scene. A pick with at least one scene of this machine
    /// stays (the file is the user's own), and so does one whose scenes were deleted later: that
    /// widget says there is nothing to show.
    /// </summary>
    internal static void DropForeignPick(DashItem it, AppSettings s)
    {
        if (it.Type != "core.scenes" || it.Opt == null) return;
        var o = DashOptions.Of(it);
        if (o.List(KShow) is not { } pick || pick.Any(id => s.Scenes.Any(sc => sc.Id == id))) return;
        o.Remove(KShow);
        it.Opt = o.ToMap();
    }

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
        if (_view == "tiles") { PaintTiles(g, area, scenes, nameF, c); return; }
        if (_view == "buttons") { PaintPills(g, area, scenes, nameF, c); return; }
        var chipF = c.Font(8.5f);
        using var glyphF = Dpi.Font("Segoe UI Emoji", 12f);   // scene glyphs are emoji, as on the scene cards
        int oneH = Math.Max(c.S(34), nameF.Height + c.S(12));
        int twoH = TwoLinesH(nameF) + c.S(12);   // a row whose name takes two lines
        int btn = Math.Max(c.S(26), nameF.Height + c.S(6));
        int y = area.Top;
        foreach (var s in scenes)
        {
            // what stands beside the name: the run button, the profile chip when there is room for one, the glyph
            int right = area.Right - btn - c.S(8);
            string? label = null; Color pc = default; int cw = 0;
            if (!string.IsNullOrEmpty(s.Profile) && Enum.TryParse<ProfileId>(s.Profile, out var pid))
            {
                label = Profiles.Get(pid).Label;
                pc = Theme.Profile(c.D.ColorOf(pid));
                cw = DashPaint.Chip(g, label, Point.Empty, pc, chipF, c.S(7), draw: false);
                if (cw < area.Width / 2) right -= cw + c.S(8); else label = null;
            }
            int gx = area.Left, gw = string.IsNullOrEmpty(s.Glyph) ? 0 : c.S(26);
            var name = new Rectangle(gx + gw + (gw > 0 ? c.S(6) : 0), y, Math.Max(10, right - gx - gw - c.S(6)), oneH);
            // A name too long for one line takes a second one and its row grows for it, so the
            // second line is whole instead of cut by the next row; never more than two lines.
            bool two = TextRenderer.MeasureText(g, s.Name, nameF, Size.Empty, TextFormatFlags.NoPadding).Width > name.Width;
            int rowH = two ? twoH : oneH;
            if (y + rowH > area.Bottom) break;
            var row = new Rectangle(area.Left, y, area.Width, rowH);
            var br = new Rectangle(row.Right - btn, y + (rowH - btn) / 2, btn, btn);
            RunMark(g, br, c);
            if (label != null) DashPaint.Chip(g, label, new Point(br.Left - c.S(8) - cw, y + (rowH - (chipF.Height + c.S(7))) / 2), pc, chipF, c.S(7));
            if (gw > 0)
                TextRenderer.DrawText(g, s.Glyph, glyphF, new Rectangle(gx, y, gw, rowH), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (two)
            {
                string text = Fitted(s.Name, nameF, name.Width, 2);
                int th = TextRenderer.MeasureText(g, text, nameF, new Size(name.Width, int.MaxValue), Wrap).Height;   // two lines at most (Fit)
                TextRenderer.DrawText(g, text, nameF, new Rectangle(name.X, y + (rowH - th) / 2, name.Width, th), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.Top | Wrap);
            }
            else
                TextRenderer.DrawText(g, s.Name, nameF, name, Theme.Text,
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
        string name = Fitted(s.Name, nameF, inner.Width, 2);
        int nameH = TextRenderer.MeasureText(name, nameF, new Size(inner.Width, int.MaxValue), Wrap).Height;   // two lines at most (Fit)
        int btn = Math.Max(c.S(26), nameF.Height + c.S(6));
        int g1 = glyphH > 0 ? c.S(6) : 0, g2 = c.S(9);
        int top = inner.Top + Math.Max(0, (inner.Height - (glyphH + g1 + nameH + g2 + btn)) / 2);
        if (glyphH > 0)
            TextRenderer.DrawText(g, s.Glyph, glyphF, new Rectangle(inner.Left, top, inner.Width, glyphH), Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, name, nameF, new Rectangle(inner.Left, top + glyphH + g1, inner.Width, nameH), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | Wrap);
        RunMark(g, new Rectangle(inner.Left + (inner.Width - btn) / 2, top + glyphH + g1 + nameH + g2, btn, btn), c);
        _rows.Add((inner, s));
    }

    /// <summary>The colour of a scene's profile, or null when the scene sets none.</summary>
    private static Color? ProfileColor(SceneDef s, DashContext c) =>
        !string.IsNullOrEmpty(s.Profile) && Enum.TryParse<ProfileId>(s.Profile, out var pid) ? Theme.Profile(c.D.ColorOf(pid)) : null;

    private static void Cell(Graphics g, Rectangle r, float radius)
    {
        using var path = Theme.RoundRect(r, radius);
        using var b = new SolidBrush(Theme.Surface);
        g.FillPath(b, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
    }

    /// <summary>
    /// Tiles: as many as fit at a size a glyph and a name need, in rows that share the width, so
    /// three scenes are three wide tiles and six are two rows of three. The profile is a thin
    /// coloured line at the tile's bottom.
    /// </summary>
    private void PaintTiles(Graphics g, Rectangle area, IReadOnlyList<SceneDef> scenes, Font nameF, DashContext c)
    {
        using var glyphF = Dpi.Font("Segoe UI Emoji", 17f);
        int gap = c.S(8), minW = c.S(74), minH = Math.Max(c.S(74), glyphF.Height + nameF.Height + c.S(26));
        int perMax = Math.Max(1, (area.Width + gap) / (minW + gap)), rowsMax = Math.Max(1, (area.Height + gap) / (minH + gap));
        int n = Math.Min(scenes.Count, perMax * rowsMax);
        // the number of rows that makes the tiles nearest to a little wider than tall: four scenes
        // in a square widget are two rows of two, not four tall slivers
        int rows = 1, per = n;
        double best = double.MaxValue;
        for (int r = Math.Max(1, (n + perMax - 1) / perMax); r <= Math.Min(rowsMax, n); r++)
        {
            int p = (n + r - 1) / r;
            double w = (area.Width - gap * (p - 1)) / (double)p, h = (area.Height - gap * (r - 1)) / (double)r;
            double off = Math.Abs(Math.Log(w / Math.Max(1, h) / 1.25));
            if (off < best - 0.001) { best = off; rows = r; per = p; }
        }
        int th = (area.Height - gap * (rows - 1)) / rows;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int i = 0; i < n; i++)
        {
            int row = i / per, col = i % per, inRow = Math.Min(per, n - row * per);
            int tw = (area.Width - gap * (inRow - 1)) / inRow;
            var t = new Rectangle(area.Left + col * (tw + gap), area.Top + row * (th + gap), tw, th);
            var s = scenes[i];
            Cell(g, t, c.Sf(9));
            if (ProfileColor(s, c) is { } pc)
            {
                using var lb = new SolidBrush(pc);
                using var lp = Theme.RoundRect(new RectangleF(t.Left + c.Sf(10), t.Bottom - c.Sf(7), Math.Max(c.Sf(6), t.Width - c.Sf(20)), c.Sf(3)), c.Sf(1.5f));
                g.FillPath(lb, lp);
            }
            var body = new Rectangle(t.Left + c.S(5), t.Top + c.S(6), t.Width - c.S(10), t.Height - c.S(6) - c.S(11));
            int glyphH = string.IsNullOrEmpty(s.Glyph) ? 0 : glyphF.Height, g1 = glyphH > 0 ? c.S(4) : 0;
            string name = Fitted(s.Name, nameF, body.Width, 2);
            int nameH = TextRenderer.MeasureText(name, nameF, new Size(body.Width, int.MaxValue), Wrap).Height;   // two lines at most (Fit)
            if (glyphH + g1 + nameH > body.Height)
            {
                // a low tile: one line of the name
                name = Fitted(s.Name, nameF, body.Width, 1);
                nameH = TextRenderer.MeasureText(name, nameF, new Size(body.Width, int.MaxValue), Wrap).Height;
            }
            int top = body.Top + Math.Max(0, (body.Height - (glyphH + g1 + nameH)) / 2);
            if (glyphH > 0)
                TextRenderer.DrawText(g, s.Glyph, glyphF, new Rectangle(body.Left, top, body.Width, glyphH), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, name, nameF, new Rectangle(body.Left, top + glyphH + g1, body.Width, nameH), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | Wrap);
            _rows.Add((t, s));
        }
    }

    /// <summary>
    /// Buttons: compact pills with the glyph and the name, the profile as a dot at the right end.
    /// One column while the scenes fit under one another, else as many columns as the width
    /// allows; a name too long for its pill takes a second line (every pill is then that tall).
    /// </summary>
    private void PaintPills(Graphics g, Rectangle area, IReadOnlyList<SceneDef> scenes, Font nameF, DashContext c)
    {
        using var glyphF = Dpi.Font("Segoe UI Emoji", 11f);
        int gap = c.S(8), glyphW = c.S(26), dotW = c.S(18), padL = c.S(8);
        int oneH = Math.Max(c.S(30), nameF.Height + c.S(12));
        int colsMax = Math.Max(1, (area.Width + gap) / (c.S(140) + gap));
        int rowsOne = Math.Max(1, (area.Height + gap) / (oneH + gap));
        int cols = Math.Min(colsMax, Math.Max(1, (scenes.Count + rowsOne - 1) / rowsOne));
        int pw = (area.Width - gap * (cols - 1)) / cols;
        int n = Math.Min(scenes.Count, cols * rowsOne);
        int TextW(SceneDef s) => pw - padL - (string.IsNullOrEmpty(s.Glyph) ? 0 : glyphW) - dotW;
        bool two = false;
        for (int i = 0; i < n && !two; i++)
            two = TextRenderer.MeasureText(scenes[i].Name, nameF, Size.Empty, TextFormatFlags.NoPadding).Width > TextW(scenes[i]);
        int ph = two ? Math.Max(oneH, TwoLinesH(nameF) + c.S(8)) : oneH;
        int rows = Math.Max(1, (area.Height + gap) / (ph + gap));
        n = Math.Min(n, cols * rows);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int i = 0; i < n; i++)
        {
            int row = i / cols, col = i % cols;
            var p = new Rectangle(area.Left + col * (pw + gap), area.Top + row * (ph + gap), pw, ph);
            var s = scenes[i];
            Cell(g, p, Math.Min(c.Sf(15), ph / 2f));
            int x = p.Left + padL;
            if (!string.IsNullOrEmpty(s.Glyph))
            {
                TextRenderer.DrawText(g, s.Glyph, glyphF, new Rectangle(x, p.Top, glyphW, ph), Theme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += glyphW;
            }
            int nameW = Math.Max(10, TextW(s) - c.S(2));
            string name = Fitted(s.Name, nameF, nameW, two ? 2 : 1);
            int nameH = TextRenderer.MeasureText(name, nameF, new Size(nameW, int.MaxValue), Wrap).Height;   // one line, or two at most (Fit)
            TextRenderer.DrawText(g, name, nameF, new Rectangle(x + c.S(2), p.Top + (ph - nameH) / 2, nameW, nameH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | Wrap);
            if (ProfileColor(s, c) is { } pc)
            {
                using var db = new SolidBrush(pc);
                float d = c.Sf(8);
                g.FillEllipse(db, p.Right - c.Sf(10) - d, p.Top + (ph - d) / 2f, d, d);
            }
            _rows.Add((p, s));
        }
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
        var list = new List<DashOptionDef>
        {
            new(KView, DashOptKind.Choice, Lang.T("dash_opt_view"))
            {
                Default = "list",
                Items = new[] { ("list", Lang.T("dash_opt_view_list")), ("tiles", Lang.T("dash_opt_view_tiles")), ("buttons", Lang.T("dash_opt_view_buttons")) },
            },
        };
        var scenes = c.D.Settings.Scenes;
        if (scenes.Count > 1)
            list.Add(new(KShow, DashOptKind.Checks, Lang.T("dash_opt_show")) { Items = scenes.Select(s => (s.Id, s.Name)).ToArray() });   // names alone: the card's font has no emoji for the glyphs
        list.Add(LabelDef(Theme.Text));
        return list;
    }
}
