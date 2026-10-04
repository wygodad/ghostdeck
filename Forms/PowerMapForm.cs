using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Editor for the profile → Windows power mode mapping used by "Auto: profile" (Settings →
/// Power → Windows power), as a GhostDeck card: one row per profile - the scenario-tile icon
/// in the profile colour, the name, and a three-way picker with the Windows modes. A layered
/// card cannot host child controls, so the pickers are painted and hit-tested here; every
/// choice is visible at once and changing one takes a single click. Save commits into
/// <see cref="AppSettings.PowerModeMap"/>, storing only the rows that differ from the defaults
/// (an empty map = the defaults); Cancel, Esc and ✕ leave the settings untouched.
/// Keyboard: ↑/↓ pick a row, ←/→ change its mode, Enter saves.
/// </summary>
public sealed class PowerMapForm : GhostCardForm
{
    private static readonly (ProfileId Id, string Name)[] Rows =
    {
        (ProfileId.SuperBattery, "Super Battery"), (ProfileId.Silent, "Silent"),
        (ProfileId.Balanced, "Balanced"), (ProfileId.Extreme, "Extreme"),
    };
    private static readonly string[] ModeKeys = { "pw_seg_eff", "pw_seg_bal", "pw_seg_perf" };

    private static readonly Color White = Color.FromArgb(0xF3, 0xF7, 0xFF);
    private static readonly Color Ink = Color.FromArgb(0xC9, 0xD4, 0xE8);
    private static readonly Color Cyan = Color.FromArgb(0x3D, 0xE3, 0xFF);
    private static readonly Color Fill = Color.FromArgb(0x3C, 0x7D, 0xFF);   // = Theme.AccentFill: a filled control with white text

    private readonly AppSettings _settings;
    private readonly Func<ProfileId, Color> _colorOf;
    private readonly Action _onSaved;
    private readonly int[] _sel = new int[Rows.Length];
    private readonly Rectangle[] _cell = new Rectangle[Rows.Length * 3];   // window coordinates
    private int _kbRow = -1;   // keyboard row; its marker appears only once the arrows are used

    public PowerMapForm(AppSettings settings, Func<ProfileId, Color> colorOf, Action onSaved)
        : base("//WIN-POWER", Lang.T("pw_mapping"), string.Format(Lang.T("pw_map_body"), Lang.T("pw_auto_seg")),
               Lang.T("set_save"), Lang.T("gen_cancel"), () => { })
    {
        _settings = settings;
        _colorOf = colorOf;
        _onSaved = onSaved;
        for (int i = 0; i < Rows.Length; i++) _sel[i] = Math.Clamp(PowerPlan.ModeGroupFor(settings, Rows[i].Id), 0, 2);
    }

    protected override int CardWidth => 560;

    protected override void Acknowledge()
    {
        var map = _settings.PowerModeMap;
        map.Clear();
        for (int i = 0; i < Rows.Length; i++)
            if (_sel[i] != PowerPlan.DefaultModeGroup(Rows[i].Id)) map[Rows[i].Id.ToString()] = _sel[i];
        _settings.Save();
        _onSaved();
    }

    private static int RowH(float k) => (int)Math.Ceiling(50 * k);

    protected override int MeasureContent(Graphics g, float k, int width) => Rows.Length * RowH(k) + (int)Math.Ceiling(8 * k);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        using var nameF = new Font("Segoe UI", 14.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var cellF = new Font("Segoe UI", 12.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var whiteB = new SolidBrush(White);
        using var inkB = new SolidBrush(Ink);
        using var hair = new Pen(Color.FromArgb(22, 243, 247, 255), 1f);

        int rowH = RowH(k), icon = Ce(22 * k), nameX = area.X + icon + Ce(12 * k);
        float nameW = 0;
        foreach (var r in Rows) nameW = Math.Max(nameW, g.MeasureString(r.Name, nameF).Width);

        // one picker geometry for all rows, so the three choices line up in columns; every cell
        // is as wide as its own caption, and the padding gives way first when a language is long
        var caps = ModeKeys.Select(Lang.T).ToArray();
        var capW = caps.Select(c => g.MeasureString(c, cellF).Width).ToArray();
        int inset = Ce(3 * k), avail = area.Right - (nameX + Ce(nameW) + Ce(18 * k)) - inset * 2;
        int padX = Ce(14 * k);
        while (padX > Ce(5 * k) && capW.Sum() + padX * 6 > avail) padX--;
        var cw = capW.Select(w => Ce(w) + padX * 2).ToArray();
        if (cw.Sum() > avail) { float f = avail / (float)cw.Sum(); for (int j = 0; j < 3; j++) cw[j] = (int)(cw[j] * f); }
        int trackW = cw.Sum() + inset * 2, trackH = Ce(34 * k), trackX = area.Right - trackW;

        for (int i = 0; i < Rows.Length; i++)
        {
            int y = area.Y + i * rowH;
            if (i > 0) g.DrawLine(hair, area.X, y, area.Right, y);
            var (id, name) = Rows[i];
            IconPainter.Scenario(g, id, new RectangleF(area.X, y + (rowH - icon) / 2f, icon, icon), _colorOf(id), 1.8f * k);
            g.DrawString(name, nameF, whiteB, nameX, y + (rowH - nameF.GetHeight(g)) / 2f);

            var track = new Rectangle(trackX, y + (rowH - trackH) / 2, trackW, trackH);
            using (var tp = Round(track, Ce(9 * k)))
            {
                using var tb = new SolidBrush(Color.FromArgb(12, 255, 255, 255));
                g.FillPath(tb, tp);
                using var te = new Pen(i == _kbRow ? Color.FromArgb(170, Cyan) : Color.FromArgb(34, 243, 247, 255), 1f);
                g.DrawPath(te, tp);
            }
            int x = track.X + inset;
            for (int j = 0; j < 3; j++)
            {
                var rc = new Rectangle(x, track.Y + inset, cw[j], trackH - inset * 2);
                _cell[i * 3 + j] = new Rectangle(rc.X + origin.X, rc.Y + origin.Y, rc.Width, rc.Height);
                bool on = _sel[i] == j, hot = HotZone == i * 3 + j;
                if (on || hot)
                {
                    using var cp = Round(rc, Ce(7 * k));
                    using var cb = new SolidBrush(on ? Fill : Color.FromArgb(24, 255, 255, 255));
                    g.FillPath(cb, cp);
                }
                g.DrawString(caps[j], cellF, on || hot ? whiteB : inkB, rc, center);
                x += cw[j];
            }
        }
    }

    private static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override int ContentHit(Point p)
    {
        for (int i = 0; i < _cell.Length; i++) if (_cell[i].Contains(p)) return i;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        _sel[zone / 3] = zone % 3;
        Rerender();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up: case Keys.Down:
                _kbRow = _kbRow < 0 ? 0 : (_kbRow + (keyData == Keys.Down ? 1 : Rows.Length - 1)) % Rows.Length;
                Rerender();
                return true;
            case Keys.Left: case Keys.Right:
                if (_kbRow < 0) _kbRow = 0;
                else _sel[_kbRow] = Math.Clamp(_sel[_kbRow] + (keyData == Keys.Right ? 1 : -1), 0, 2);
                Rerender();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
