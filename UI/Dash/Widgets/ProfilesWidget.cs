using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Profiles: the Scenarios tiles in a row (4x1) or a 2 x 2 grid (2x2): icon, name, "ACTIVE"
/// pill or "SELECT". On a board with a fourth mode the Extreme tile carries the APEX switch,
/// as on Scenarios. The tiles are live (the active one follows the sample), the card is static.
///
/// Settings (1.45): which profiles the widget shows. With copies of the widget that makes small
/// ones, say one with Silent alone and one with Balanced alone. The sizes follow the count: one
/// profile fits a single cell, two fit two, three and four keep the sizes of 1.44.
/// </summary>
internal sealed class ProfilesWidget : DashWidget
{
    private string _painted = "";
    private readonly List<(Rectangle r, ProfileId id)> _tiles = new();
    private Rectangle _apexSwitch;

    private const string KShow = "show";

    public ProfilesWidget(DashWidgetDef def, DashOptions opt) : base(def, opt)
    {
        int n = Ids().Count;
        Sizes = n <= 1 ? Sz((1, 1), (2, 1)) : n == 2 ? Sz((2, 1), (2, 2)) : def.Sizes;
    }

    /// <summary>The profiles to show, in the order the user set for profiles; all of them unless some were picked.</summary>
    private IReadOnlyList<ProfileId> Ids() => Picked(KShow, Profiles.Shown, id => id.ToString());

    public override void PaintStatic(Graphics g, Rectangle r, DashContext c) => Frame(g, r);

    public override void PaintLive(Graphics g, Rectangle r, DashContext c)
    {
        var ids = Ids();
        _tiles.Clear();
        _apexSwitch = Rectangle.Empty;
        _painted = Signature(c);
        if (ids.Count == 0) { DashPaint.Note(g, Inner(r, c), c, Lang.T("dash_sel_none")); return; }
        // a lone tile gets more of the card: it is the whole widget
        var inner = ids.Count == 1 ? Rectangle.Inflate(r, -c.S(8), -c.S(8)) : Inner(r, c);
        int gap = c.S(10);
        bool grid = r.Height > r.Width * 0.75f;   // the square variants
        int cols = grid ? Math.Min(2, ids.Count) : ids.Count, rows = (ids.Count + cols - 1) / cols;
        int tileW = (inner.Width - gap * (cols - 1)) / Math.Max(1, cols);
        int tileH = (inner.Height - gap * (rows - 1)) / Math.Max(1, rows);
        bool writable = c.Writable;
        bool apexAvail = c.D.ApexAvailable();
        bool apexOn = apexAvail && c.D.ApexOn();
        for (int i = 0; i < ids.Count; i++)
        {
            var tile = new Rectangle(inner.Left + (i % cols) * (tileW + gap), inner.Top + (i / cols) * (tileH + gap), tileW, tileH);
            PaintTile(g, tile, ids[i], writable && c.Sample.Profile == ids[i], apexAvail && ids[i] == ProfileId.Extreme, apexOn, c);
            _tiles.Add((tile, ids[i]));
        }
    }

    private void PaintTile(Graphics g, Rectangle t, ProfileId id, bool active, bool apexRow, bool apexOn, DashContext c)
    {
        var def = Profiles.Get(id);
        var col = Theme.Profile(c.D.ColorOf(id));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundRect(t, c.Sf(8)))
        {
            using var b = new SolidBrush(active ? Theme.AccentSoft : Theme.Surface);
            g.FillPath(b, path);
            using var pen = new Pen(active ? Theme.Accent : Theme.Border, active ? 2f : 1f);
            g.DrawPath(pen, path);
        }
        var nameF = c.Font(10f, FontStyle.Bold);
        var footF = c.Font(9f, FontStyle.Bold);
        var pillF = c.Font(9.5f, FontStyle.Bold);
        var apexF = c.Font(8.5f, FontStyle.Bold);
        int padX = c.S(8), w = t.Width - padX * 2;
        int nameH = Math.Min(nameF.Height * 2, TextRenderer.MeasureText(def.Label, nameF, new Size(w, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
        int footH = pillF.Height + c.S(14);
        int apexH = apexRow ? Math.Max(c.S(16), apexF.Height) + c.S(8) : 0;
        int icon = Math.Max(c.S(24), Math.Min(c.S(56), (int)(t.Height * 0.32f)));
        int g1 = c.S(8), g2 = c.S(6);
        int total = icon + g1 + nameH + g2 + footH + apexH;
        if (total > t.Height - c.S(8)) { icon = Math.Max(c.S(20), icon - (total - (t.Height - c.S(8)))); total = icon + g1 + nameH + g2 + footH + apexH; }
        int top = t.Top + Math.Max(c.S(4), (t.Height - total) / 2);

        IconPainter.Scenario(g, id, new RectangleF(t.Left + (t.Width - icon) / 2f, top, icon, icon), col, Math.Max(2f, c.Sf(3f)));
        int y = top + icon + g1;
        TextRenderer.DrawText(g, def.Label, nameF, new Rectangle(t.Left + padX, y, w, nameH), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        y += nameH + g2;
        if (active)
        {
            string a = Lang.T("scen_active");
            int tw = TextRenderer.MeasureText(a, pillF).Width;
            Ui.Pill(g, a, new Point(t.Left + (t.Width - (tw + 32)) / 2, y), Theme.Accent);
        }
        else
            TextRenderer.DrawText(g, Lang.T("scen_select"), footF, new Rectangle(t.Left + padX, y + c.S(5), w, footF.Height), Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        y += footH;
        if (apexRow)
        {
            int swH = Math.Max(c.S(16), apexF.Height), swW = swH * 2;
            int textW = TextRenderer.MeasureText("APEX", apexF, Size.Empty, TextFormatFlags.NoPadding).Width;
            int totalW = swW + c.S(6) + textW;
            int x = t.Left + (t.Width - totalW) / 2;
            _apexSwitch = new Rectangle(x, y, swW, swH);
            DashPaint.Switch(g, _apexSwitch, apexOn);
            TextRenderer.DrawText(g, "APEX", apexF, new Rectangle(x + swW + c.S(6), y, textW + c.S(2), swH), apexOn ? Theme.Amber : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private string Signature(DashContext c)
    {
        bool apexAvail = c.D.ApexAvailable();
        return $"{c.Sample.Profile}|{c.Writable}|{apexAvail}|{(apexAvail && c.D.ApexOn())}|{string.Join(",", Ids())}";
    }

    public override IReadOnlyList<DashOptionDef> OptionDefs(DashContext c) => new[]
    {
        new DashOptionDef(KShow, DashOptKind.Checks, Lang.T("dash_opt_show")) { Items = Profiles.Shown.Select(id => (id.ToString(), Profiles.Get(id).Label)).ToArray() },
    };

    public override bool LiveChanged(DashContext c) => Signature(c) != _painted;

    public override void Click(Point p, Rectangle r, DashContext c)
    {
        if (!c.Writable) return;
        if (_apexSwitch.Contains(p)) { c.D.SetApex(!c.D.ApexOn()); return; }
        foreach (var (tr, id) in _tiles)
            if (tr.Contains(p)) { c.D.SetProfile(id); return; }
    }
}
