using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The help card of the Windows power card (#117), opened by the "?" next to CPU turbo boost.
/// Too much for a bubble: what the card changes (Windows, not the EC), the switch and the two
/// lists, every turbo mode this Windows offers in a table (value, Windows' own name, what it
/// does, whether it lowers power), the power mode and Auto, By profile, Restore and the
/// Power Options link. Five pages behind tabs (the icon legend has its own) keep the card short
/// enough for a 1080p screen at 125 % with text as large as on the Settings cards; the card takes
/// the height of its tallest page, so switching pages never moves the buttons. No text is ever
/// cut or ends in "...": tabs, cells and paragraphs wrap and their rows grow.
/// The mode names are read live (<see cref="PowerPlan.BoostModes"/>), and the sentence about
/// autonomous mode appears only when the active plan has it on for both power sources.
/// The left button opens the full guide (docs/WINDOWS-POWER.md) on GitHub.
/// </summary>
public sealed class WinPowerHelpForm : GhostCardForm
{
    private const string GuideUrl = "https://github.com/wygodad/ghostdeck/blob/main/docs/WINDOWS-POWER.md";
    private static readonly string[] TabKeys = { "pw_help_tab_turbo", "pw_help_tab_modes", "pw_lg_title", "pw_help_tab_profile", "pw_help_tab_restore" };

    private readonly List<PowerPlan.BoostMode> _modes = PowerPlan.BoostModes();
    private readonly bool _autonomous = PowerPlan.AutonomousOn();
    private int _page;
    private readonly List<(Rectangle R, int Zone)> _zones = new();   // window coordinates

    public WinPowerHelpForm()
        : base("//WIN-POWER", Lang.T("pw_grp"), "", Lang.T("gen_ok"), "", () => { })   // the intro opens the first page, so the tall modes page keeps its room
    {
    }

    protected override int CardWidth => 960;
    protected override string AuxLabel => Lang.T("pw_help_guide");
    protected override Color AuxColor => Fill;
    protected override float ButtonFontPx => 14.5f;
    protected override void AuxClick() => ModelsPage.OpenUrl(GuideUrl);

    // ---------------- one layout routine for measure and paint ----------------

    private enum Kind { Tab, Head, Para, Callout, Th, Cell, Line, Icon }
    private sealed record Item(Rectangle R, Kind K, string Text, int Zone = -1, bool On = false, Color? Tint = null);

    private sealed class Fonts : IDisposable
    {
        public readonly Font Tab, Head, Para, Cap, Cell;
        public Fonts(float k)
        {
            // the size of the text on the Settings cards (10 pt body = 13.3 px) and a step above
            Tab = Dpi.Font("Segoe UI", 14.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
            Head = Dpi.Font("Segoe UI Semibold", 16.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            Para = Dpi.Font("Segoe UI", 14.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            Cap = Dpi.Font("Segoe UI", 11.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
            Cell = Dpi.Font("Segoe UI", 14f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        public void Dispose() { Tab.Dispose(); Head.Dispose(); Para.Dispose(); Cap.Dispose(); Cell.Dispose(); }
    }

    /// <summary>Tab row height: one line, or two when a tab name has to wrap in a long language.</summary>
    private static int TabH(Graphics g, float k, int width, Fonts f)
    {
        int tabW = width / TabKeys.Length, h = (int)Math.Ceiling(42 * k);
        foreach (var key in TabKeys)
            h = Math.Max(h, (int)Math.Ceiling(g.MeasureString(Lang.T(key), f.Tab, Math.Max(10, tabW - (int)Math.Ceiling(14 * k))).Height + 14 * k));
        return h;
    }

    private int TabsHeight(Graphics g, float k, int width, Fonts f) => TabH(g, k, width, f) + (int)Math.Ceiling(16 * k);

    private List<Item> Tabs(Graphics g, float k, int width, Fonts f)
    {
        var items = new List<Item>();
        int tabH = TabH(g, k, width, f), tabW = width / TabKeys.Length;
        for (int i = 0; i < TabKeys.Length; i++)
            items.Add(new Item(new Rectangle(i * tabW, 0, i == TabKeys.Length - 1 ? width - i * tabW : tabW, tabH), Kind.Tab, Lang.T(TabKeys[i]), i, i == _page));
        return items;
    }

    /// <summary>The items of one page, laid out from <paramref name="y0"/>; returns the bottom.</summary>
    private int Page(Graphics g, float k, int width, Fonts f, int page, int y0, List<Item> items)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        int y = y0;
        int TextH(string s, Font font, int w) => Ce(g.MeasureString(s, font, Math.Max(10, w)).Height);
        void Head(string s) { int h = TextH(s, f.Head, width); items.Add(new Item(new Rectangle(0, y, width, h), Kind.Head, s)); y += h + Ce(4 * k); }
        void Para(string s) { int h = TextH(s, f.Para, width); items.Add(new Item(new Rectangle(0, y, width, h), Kind.Para, s)); y += h + Ce(14 * k); }
        void Callout(string s)
        {
            int pad = Ce(12 * k), h = TextH(s, f.Para, width - pad * 2 - Ce(4 * k)) + pad * 2;
            items.Add(new Item(new Rectangle(0, y, width, h), Kind.Callout, s));
            y += h + Ce(14 * k);
        }
        string off = PowerPlan.BoostName(0), on = PowerPlan.BoostName(1);

        switch (page)
        {
            case 0:
                Para(Lang.T("pw_help_intro"));
                Head(Lang.T("pw_help_switch_h"));
                Para(string.Format(Lang.T("pw_help_switch"), off));
                Head(Lang.T("pw_help_less_h"));
                Callout(string.Format(Lang.T("pw_help_less"), off));
                break;
            case 1:
            {
                // value | name in Windows (with the icon of its group) | what it does
                string[] th = { Lang.T("pw_help_col_value"), Lang.T("pw_help_col_name"), Lang.T("pw_help_col_desc") };
                // the value column is as wide as its caption, so the caption never breaks inside a word
                int cV = Math.Max(Ce(56 * k), Ce(g.MeasureString(Lang.Upper(th[0]), f.Cap).Width) + Ce(14 * k));
                int cN = Ce(236 * k), cD = width - cV - cN;
                int[] x = { 0, cV, cV + cN };
                int[] w = { cV - Ce(10 * k), cN - Ce(12 * k), cD };
                int ic = Ce(20 * k), icGap = Ce(8 * k);
                int thH = 0;
                for (int j = 0; j < 3; j++) thH = Math.Max(thH, TextH(Lang.Upper(th[j]), f.Cap, w[j]));
                for (int j = 0; j < 3; j++) items.Add(new Item(new Rectangle(x[j], y, w[j], thH), Kind.Th, Lang.Upper(th[j])));
                y += thH + Ce(6 * k);
                items.Add(new Item(new Rectangle(0, y, width, 1), Kind.Line, ""));
                foreach (var m in _modes)
                {
                    var grp = PowerPlan.GroupOf(m.Value, _autonomous);
                    string desc = PowerPlan.BoostDescription(m.Value);
                    int nameW = w[1] - ic - icGap;
                    int rh = Math.Max(ic, Math.Max(TextH(m.Name, f.Cell, nameW), TextH(desc, f.Cell, w[2])));
                    int top = y + Ce(5 * k);
                    items.Add(new Item(new Rectangle(x[0], top, w[0], rh), Kind.Cell, m.Value.ToString(), Tint: Muted));
                    items.Add(new Item(new Rectangle(x[1], top, ic, ic), Kind.Icon, "", (int)grp));
                    items.Add(new Item(new Rectangle(x[1] + ic + icGap, top, nameW, rh), Kind.Cell, m.Name, Tint: White));
                    items.Add(new Item(new Rectangle(x[2], top, w[2], rh), Kind.Cell, desc, Tint: Ink));
                    y = top + rh + Ce(5 * k);
                    items.Add(new Item(new Rectangle(0, y, width, 1), Kind.Line, ""));
                }
                y += Ce(12 * k);
                Para(Lang.T("pw_help_number"));   // what the Value column is
                break;
            }
            case 2:
            {
                // the legend: what each icon means and which modes it covers, two columns
                int ic = Ce(22 * k), icGap = Ce(10 * k);
                var legend = new (PowerPlan.BoostGroup G, string Modes, string Key)[]
                {
                    (PowerPlan.BoostGroup.Less, "0", "pw_lg_less"),
                    (PowerPlan.BoostGroup.Turbo, _autonomous ? "1-4" : "1, 3", "pw_lg_turbo"),
                    (PowerPlan.BoostGroup.TurboMax, "2, 4", "pw_lg_max"),
                    (PowerPlan.BoostGroup.AskMore, "5, 6", "pw_lg_more"),
                    (PowerPlan.BoostGroup.Unknown, Lang.T("pw_lg_other"), "pw_lg_unknown"),
                };
                int cols = 2, colGap = Ce(28 * k), colW = (width - colGap * (cols - 1)) / cols, textX = ic + icGap, textW = colW - textX;
                for (int i = 0; i < legend.Length; i += cols)
                {
                    int rowH = 0;
                    for (int c = 0; c < cols && i + c < legend.Length; c++)
                    {
                        var (grp, modes, key) = legend[i + c];
                        int lx = c * (colW + colGap);
                        string head = PowerPlan.GroupLabel(grp) + "  ·  " + modes;
                        int hh = TextH(head, f.Tab, textW), mh = TextH(Lang.T(key), f.Cell, textW);
                        items.Add(new Item(new Rectangle(lx, y, ic, ic), Kind.Icon, "", (int)grp));
                        items.Add(new Item(new Rectangle(lx + textX, y, textW, hh), Kind.Cell, head, Tint: PowerPlan.GroupColor(grp, onDarkCard: true), On: true));
                        items.Add(new Item(new Rectangle(lx + textX, y + hh, textW, mh), Kind.Cell, Lang.T(key), Tint: Muted));
                        rowH = Math.Max(rowH, Math.Max(ic, hh + mh));
                    }
                    y += rowH + Ce(16 * k);
                }
                if (_autonomous) Callout(string.Format(Lang.T("pw_help_autonomous"), on, off));
                else Para(Lang.T("pw_lg_note"));
                break;
            }
            case 3:
                Head(Lang.T("pw_grp_mode"));
                Para(string.Format(Lang.T("pw_help_mode"), Lang.T("pw_auto_seg")));
                Head(Lang.T("pw_grp_profile"));
                Para(string.Format(Lang.T("pw_help_profile"), Lang.T("pw_unchanged")));
                break;
            default:
                Head(Lang.T("pw_restore_btn"));
                Para(string.Format(Lang.T("pw_help_restore"), Lang.T("pw_restore_ack")));
                Head(Lang.T("pw_help_reveal_h"));
                Para(Lang.T("pw_help_reveal"));
                Para(Lang.T("pw_help_sources"));
                break;
        }
        return y;
    }

    protected override int MeasureContent(Graphics g, float k, int width)
    {
        using var f = new Fonts(k);
        int max = 0;
        for (int p = 0; p < TabKeys.Length; p++) max = Math.Max(max, Page(g, k, width, f, p, 0, new List<Item>()));
        return TabsHeight(g, k, width, f) + max;
    }

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        using var f = new Fonts(k);
        var items = Tabs(g, k, area.Width, f);
        Page(g, k, area.Width, f, _page, TabsHeight(g, k, area.Width, f), items);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None };
        using var whiteB = new SolidBrush(White);
        using var inkB = new SolidBrush(Ink);
        using var mutedB = new SolidBrush(Muted);
        using var hair = new Pen(Color.FromArgb(28, 243, 247, 255), 1f);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        _zones.Clear();
        foreach (var it in items)
        {
            var r = new Rectangle(it.R.X + area.X, it.R.Y + area.Y, it.R.Width, it.R.Height);
            switch (it.K)
            {
                case Kind.Tab:
                {
                    _zones.Add((new Rectangle(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height), it.Zone));
                    bool hot = HotZone == it.Zone;
                    if (it.On || hot)
                    {
                        using var hb = new SolidBrush(Color.FromArgb(it.On ? 30 : 16, 255, 255, 255));
                        using var hp = RoundPath(Rectangle.Inflate(r, -Ce(2 * k), 0), Ce(8 * k));
                        g.FillPath(hb, hp);
                    }
                    if (it.On) { using var ub = new SolidBrush(Cyan); g.FillRectangle(ub, r.X + Ce(12 * k), r.Bottom - Ce(3 * k), r.Width - Ce(24 * k), Ce(3 * k)); }
                    g.DrawString(it.Text, f.Tab, it.On || hot ? whiteB : inkB, Rectangle.Inflate(r, -Ce(7 * k), 0), center);
                    break;
                }
                case Kind.Head: g.DrawString(it.Text, f.Head, whiteB, r); break;
                case Kind.Para: g.DrawString(it.Text, f.Para, inkB, r); break;
                case Kind.Callout:
                {
                    int pad = Ce(12 * k);
                    using (var cb = new SolidBrush(Color.FromArgb(22, Amber))) g.FillRectangle(cb, r);
                    using (var rb = new SolidBrush(Amber)) g.FillRectangle(rb, r.X, r.Y, Ce(3 * k), r.Height);
                    g.DrawString(it.Text, f.Para, whiteB, new Rectangle(r.X + pad + Ce(4 * k), r.Y + pad, r.Width - pad * 2 - Ce(4 * k), r.Height - pad * 2 + Ce(4 * k)));
                    break;
                }
                case Kind.Th: g.DrawString(it.Text, f.Cap, mutedB, r); break;
                case Kind.Cell:
                {
                    using var tb = new SolidBrush(it.Tint ?? Ink);
                    g.DrawString(it.Text, it.On ? f.Tab : f.Cell, tb, new RectangleF(r.X, r.Y, r.Width, r.Height + Ce(2 * k)));
                    break;
                }
                case Kind.Icon:
                {
                    var grp = (PowerPlan.BoostGroup)it.Zone;
                    IconPainter.BoostGroup(g, grp, r, PowerPlan.GroupColor(grp, onDarkCard: true));
                    break;
                }
                case Kind.Line: g.DrawLine(hair, r.X, r.Y, r.Right, r.Y); break;
            }
        }
    }

    protected override int ContentHit(Point p)
    {
        foreach (var (r, z) in _zones) if (r.Contains(p)) return z;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        _page = zone;
        Rerender();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Left or Keys.Right)
        {
            _page = (_page + (keyData == Keys.Right ? 1 : TabKeys.Length - 1)) % TabKeys.Length;
            Rerender();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
