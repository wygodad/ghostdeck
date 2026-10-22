using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Editor for what GhostDeck sets in Windows on a profile switch (Settings → Power → Windows
/// power → By profile), as a GhostDeck card: a profile picker at the top (the scenario-tile
/// icon in the profile colour plus the name), then for the picked profile three rows of chips -
/// the Windows power mode used by "Auto: profile" (#36) and the CPU turbo mode plugged in and
/// on battery (#117), each turbo row starting with "no change". One profile at a time keeps the
/// card short although Windows offers up to seven turbo modes with long names; the chips wrap
/// instead of being cut. A layered card cannot host child controls, so everything is painted
/// and hit-tested here. Save commits into <see cref="AppSettings.PowerModeMap"/> (only rows that
/// differ from the defaults) and <see cref="AppSettings.TurboMap"/> (only profiles that set a
/// turbo mode); Cancel, Esc and ✕ leave the settings untouched.
/// Keyboard: ←/→ pick the profile, Enter saves.
/// </summary>
public sealed class PowerMapForm : GhostCardForm
{
    private static readonly (ProfileId Id, string Name)[] Rows =
    {
        (ProfileId.SuperBattery, "Super Battery"), (ProfileId.Silent, "Silent"),
        (ProfileId.Balanced, "Balanced"), (ProfileId.Extreme, "Extreme"),
    };
    private static readonly string[] ModeKeys = { "pw_seg_eff", "pw_seg_bal", "pw_seg_perf" };

    // hit zones: profile tab = i; mode chip = 100 + j; turbo chip = 200 / 300 + index (0 = no change)

    /// <summary>The turbo mode a chip stands for, or -1 (a profile tab, a power-mode chip, "no change").</summary>
    private int ModeOfZone(int zone) => zone >= ZAc && zone % 100 > 0 && zone % 100 - 1 < _modes.Count ? (int)_modes[zone % 100 - 1].Value : -1;
    private static int ChipIcon(float k) => (int)Math.Ceiling(17 * k);
    private const int ZMode = 100, ZAc = 200, ZDc = 300;

    private readonly AppSettings _settings;
    private readonly Func<ProfileId, Color> _colorOf;
    private readonly Action _onSaved;
    private readonly List<PowerPlan.BoostMode> _modes = PowerPlan.BoostModes();
    private readonly bool _autonomous = PowerPlan.AutonomousOn();   // the groups of modes 2 and 4 follow it
    private readonly int[] _mode = new int[Rows.Length], _ac = new int[Rows.Length], _dc = new int[Rows.Length];
    private int _p;                                              // the profile being edited
    private readonly List<(Rectangle R, int Zone)> _zones = new();   // window coordinates

    public PowerMapForm(AppSettings settings, Func<ProfileId, Color> colorOf, Action onSaved, ProfileId start)
        : base("//WIN-POWER", Lang.T("pw_grp_profile"), string.Format(Lang.T("pw_map_body2"), Lang.T("pw_auto_seg")),
               Lang.T("set_save"), Lang.T("gen_cancel"), () => { })
    {
        _settings = settings;
        _colorOf = colorOf;
        _onSaved = onSaved;
        for (int i = 0; i < Rows.Length; i++)
        {
            _mode[i] = Math.Clamp(PowerPlan.ModeGroupFor(settings, Rows[i].Id), 0, 2);
            (_ac[i], _dc[i]) = PowerPlan.TurboFor(settings, Rows[i].Id);
            if (Rows[i].Id == start) _p = i;
        }
    }

    protected override int CardWidth => 620;

    protected override bool Acknowledge()
    {
        var map = _settings.PowerModeMap;
        map.Clear();
        _settings.TurboMap.Clear();
        for (int i = 0; i < Rows.Length; i++)
        {
            if (_mode[i] != PowerPlan.DefaultModeGroup(Rows[i].Id)) map[Rows[i].Id.ToString()] = _mode[i];
            if (_ac[i] >= 0 || _dc[i] >= 0) _settings.TurboMap[Rows[i].Id.ToString()] = new[] { _ac[i], _dc[i] };
        }
        _settings.Save();
        _onSaved();
        return true;
    }

    // ---------------- one layout routine for measure and paint ----------------

    private sealed record Item(Rectangle R, int Zone, string Text, bool On, int Kind);   // Kind: 0 tab, 1 chip, 2 label, 3 description

    private List<Item> Arrange(Graphics g, float k, int width, out int height)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        using var tabF = Dpi.Font("Segoe UI", 13f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var chipF = Dpi.Font("Segoe UI", 12.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labF = Dpi.Font("Segoe UI", 11f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var descF = Dpi.Font("Segoe UI", 12.5f * k, GraphicsUnit.Pixel);
        var items = new List<Item>();
        int y = 0;

        // profile tabs: equal widths across the card, icon + name; a name too long for its tab
        // wraps and every tab grows with it (no "..." anywhere in this card)
        int tabW = width / Rows.Length, tabTextW = tabW - Ce(18 * k) - Ce(8 * k) - Ce(12 * k);
        int tabH = Ce(40 * k);
        foreach (var r in Rows) tabH = Math.Max(tabH, Ce(g.MeasureString(r.Name, tabF, Math.Max(10, tabTextW)).Height) + Ce(14 * k));
        for (int i = 0; i < Rows.Length; i++)
            items.Add(new Item(new Rectangle(i * tabW, y, i == Rows.Length - 1 ? width - i * tabW : tabW, tabH), i, Rows[i].Name, i == _p, 0));
        y += tabH + Ce(16 * k);

        int chipH = Ce(30 * k), gap = Ce(6 * k), padX = Ce(12 * k);
        void Label(string text)
        {
            int h = Ce(g.MeasureString(text.ToUpperInvariant(), labF, width).Height) + Ce(2 * k);
            items.Add(new Item(new Rectangle(0, y, width, h), -1, text.ToUpperInvariant(), false, 2));
            y += h + Ce(6 * k);
        }
        void Chips(IEnumerable<(string Text, int Zone, bool On)> chips)
        {
            int x = 0, rowH = chipH;
            foreach (var (text, zone, on) in chips)
            {
                // a turbo-mode chip carries the icon of its group in front of the name; a name
                // wider than the card wraps inside a full-width chip that grows, never "..."
                int iconW = ModeOfZone(zone) >= 0 ? ChipIcon(k) + Ce(7 * k) : 0;
                int avail = width - padX * 2 - iconW;
                int tw = Ce(g.MeasureString(text, chipF).Width);
                int w = tw <= avail ? tw + padX * 2 + iconW : width;
                int h = tw <= avail ? chipH : Math.Max(chipH, Ce(g.MeasureString(text, chipF, avail).Height) + Ce(12 * k));
                if (x > 0 && x + w > width) { x = 0; y += rowH + gap; rowH = chipH; }
                items.Add(new Item(new Rectangle(x, y, w, h), zone, text, on, 1));
                rowH = Math.Max(rowH, h);
                x += w + gap;
            }
            y += rowH + Ce(8 * k);
        }
        void Desc(int value)
        {
            if (value < 0) { y += Ce(6 * k); return; }
            string d = PowerPlan.BoostDescription((uint)value);
            int h = Ce(g.MeasureString(d, descF, width).Height);
            items.Add(new Item(new Rectangle(0, y, width, h), -1, d, false, 3));
            y += h + Ce(12 * k);
        }

        Label(Lang.T("pw_mode_now"));
        Chips(ModeKeys.Select((key, j) => (Lang.T(key), ZMode + j, _mode[_p] == j)));
        y += Ce(6 * k);
        foreach (var (title, zbase, sel) in new[] { (Lang.T("pw_map_turbo_ac"), ZAc, _ac[_p]), (Lang.T("pw_map_turbo_dc"), ZDc, _dc[_p]) })
        {
            Label(title);
            var chips = new List<(string, int, bool)> { (Lang.T("pw_unchanged"), zbase, sel < 0) };
            for (int j = 0; j < _modes.Count; j++) chips.Add((PowerPlan.BoostName(_modes[j].Value), zbase + 1 + j, sel == (int)_modes[j].Value));
            Chips(chips);
            Desc(sel);
        }
        height = y;
        return items;
    }

    protected override int MeasureContent(Graphics g, float k, int width)
    {
        Arrange(g, k, width, out int h);
        return h;
    }

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        var items = Arrange(g, k, area.Width, out _);
        using var tabF = Dpi.Font("Segoe UI", 13f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var chipF = Dpi.Font("Segoe UI", 12.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labF = Dpi.Font("Segoe UI", 11f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var descF = Dpi.Font("Segoe UI", 12.5f * k, GraphicsUnit.Pixel);
        // wrapping formats without trimming: a text that does not fit on one line takes the next
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None };
        using var left = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None };
        using var whiteB = new SolidBrush(White);
        using var inkB = new SolidBrush(Ink);
        using var mutedB = new SolidBrush(Muted);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        _zones.Clear();
        foreach (var it in items)
        {
            var r = new Rectangle(it.R.X + area.X, it.R.Y + area.Y, it.R.Width, it.R.Height);
            if (it.Zone >= 0) _zones.Add((new Rectangle(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height), it.Zone));
            bool hot = it.Zone >= 0 && HotZone == it.Zone;
            switch (it.Kind)
            {
                case 0:   // profile tab: underline in the profile colour when picked
                {
                    var id = Rows[it.Zone].Id;
                    if (it.On || hot)
                    {
                        using var hb = new SolidBrush(Color.FromArgb(it.On ? 30 : 16, 255, 255, 255));
                        using var hp = RoundPath(Rectangle.Inflate(r, -Ce(2 * k), 0), Ce(8 * k));
                        g.FillPath(hb, hp);
                    }
                    if (it.On) { using var ub = new SolidBrush(_colorOf(id)); g.FillRectangle(ub, r.X + Ce(10 * k), r.Bottom - Ce(3 * k), r.Width - Ce(20 * k), Ce(3 * k)); }
                    int icon = Ce(18 * k);
                    float tw = Math.Min(g.MeasureString(it.Text, tabF).Width, r.Width - icon - Ce(8 * k) - Ce(12 * k));
                    float total = Math.Min(r.Width - Ce(8 * k), icon + Ce(8 * k) + tw);
                    float x0 = r.X + (r.Width - total) / 2f;
                    IconPainter.Scenario(g, id, new RectangleF(x0, r.Y + (r.Height - icon) / 2f, icon, icon), _colorOf(id), 1.6f * k);
                    g.DrawString(it.Text, tabF, it.On || hot ? whiteB : inkB,
                        new RectangleF(x0 + icon + Ce(8 * k), r.Y, Math.Max(10, r.Right - (x0 + icon + Ce(8 * k)) - Ce(4 * k)), r.Height), left);
                    break;
                }
                case 1:   // chip
                {
                    using var cp = RoundPath(r, r.Height / 2);
                    using (var cb = new SolidBrush(it.On ? Fill : hot ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(10, 255, 255, 255))) g.FillPath(cb, cp);
                    if (!it.On) { using var pen = new Pen(Color.FromArgb(40, 243, 247, 255), 1f); g.DrawPath(pen, cp); }
                    int mode = ModeOfZone(it.Zone);
                    if (mode < 0) { g.DrawString(it.Text, chipF, it.On || hot ? whiteB : inkB, r, center); break; }
                    int ic = ChipIcon(k), gapI = Ce(7 * k), padC = Ce(12 * k);
                    float tw = Math.Min(g.MeasureString(it.Text, chipF).Width, r.Width - padC * 2 - ic - gapI);
                    float x0 = r.X + Math.Max(padC, (r.Width - (ic + gapI + tw)) / 2f);
                    var grp = PowerPlan.GroupOf((uint)mode, _autonomous);
                    IconPainter.BoostGroup(g, grp, new RectangleF(x0, r.Y + (r.Height - ic) / 2f, ic, ic), PowerPlan.GroupColor(grp, onDarkCard: true), it.On ? White : null);
                    g.DrawString(it.Text, chipF, it.On || hot ? whiteB : inkB, new RectangleF(x0 + ic + gapI, r.Y, Math.Max(10, r.Right - (x0 + ic + gapI) - padC + 2), r.Height), left);
                    break;
                }
                case 2: g.DrawString(it.Text, labF, mutedB, new RectangleF(r.X, r.Y, r.Width + 2, r.Height), left); break;
                case 3: g.DrawString(it.Text, descF, mutedB, r); break;
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
        if (zone < Rows.Length) _p = zone;
        else if (zone < ZAc) _mode[_p] = zone - ZMode;
        else
        {
            int idx = zone % 100, v = idx == 0 ? -1 : (int)_modes[idx - 1].Value;
            if (zone < ZDc) _ac[_p] = v; else _dc[_p] = v;
        }
        Rerender();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Left or Keys.Right)
        {
            _p = (_p + (keyData == Keys.Right ? 1 : Rows.Length - 1)) % Rows.Length;
            Rerender();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
