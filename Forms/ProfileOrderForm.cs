namespace GhostDeck;

/// <summary>
/// Editor for the order of the four profiles (discussion #101), as a GhostDeck card: two
/// one-click presets ("Standard" and "By power" - Super Battery up to Extreme) and one row
/// per profile with an up and a down arrow for any other order. Save stores the order in
/// <see cref="AppSettings.ProfileOrder"/> (empty = the standard one) and makes it current
/// through <see cref="Profiles.SetShown"/>; the tiles, the tray menu, the profile lists and
/// "next profile" all read it from there.
/// Keyboard: ↑/↓ pick a row, Ctrl+↑ / Ctrl+↓ move it, Enter saves.
/// </summary>
public sealed class ProfileOrderForm : GhostCardForm
{
    private enum ZoneKind { Preset, Up, Down }
    private readonly record struct Zone(Rectangle R, ZoneKind Kind, int Idx);

    private readonly AppSettings _settings;
    private readonly Func<ProfileId, Color> _colorOf;
    private readonly List<ProfileId> _order;
    private readonly List<Zone> _zones = new();
    private int _kbRow = -1;   // keyboard row; its marker appears only once the arrows are used

    public ProfileOrderForm(AppSettings settings, Func<ProfileId, Color> colorOf)
        : base("//PROFILES", Lang.T("po_title"), Lang.T("po_body"), Lang.T("set_save"), Lang.T("gen_cancel"), () => { })
    {
        _settings = settings;
        _colorOf = colorOf;
        _order = Profiles.Shown.ToList();
    }

    protected override int CardWidth => 520;

    protected override bool Acknowledge()
    {
        _settings.ProfileOrder = _order.SequenceEqual(Profiles.Order) ? new List<string>() : _order.Select(id => Profiles.Get(id).Key).ToList();
        Profiles.SetShown(_settings.ProfileOrder);
        _settings.Save();
        DialogResult = DialogResult.OK;
        return true;
    }

    private static int Ce(float v) => (int)Math.Ceiling(v);
    private static int RowH(float k) => Ce(46 * k);
    private static int PresetH(float k) => Ce(34 * k) + Ce(14 * k);

    protected override int MeasureContent(Graphics g, float k, int width) => PresetH(k) + _order.Count * RowH(k) + Ce(4 * k);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        _zones.Clear();
        Rectangle Win(Rectangle r) => new(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height);
        using var whiteB = new SolidBrush(White);
        using var hair = new Pen(Color.FromArgb(22, 243, 247, 255), 1f);
        using var glyphF = new Font("Segoe MDL2 Assets", 13f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var nameF = new Font("Segoe UI", 14.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);

        // two presets in one frame; a cell is lit while the rows below match it
        var presets = new[] { (Lang.T("po_std"), Profiles.Order), (Lang.T("po_power"), Profiles.ByPower) };
        int inset = Ce(3 * k), trackH = Ce(34 * k);
        var track = new Rectangle(area.X, area.Y, area.Width, trackH);
        PaintTrack(g, track, true);
        int cellW = (track.Width - inset * 2) / presets.Length;
        for (int i = 0; i < presets.Length; i++)
        {
            var rc = new Rectangle(track.X + inset + i * cellW, track.Y + inset, cellW, trackH - inset * 2);
            PaintCell(g, rc, presets[i].Item1, _order.SequenceEqual(presets[i].Item2), true, HotZone == _zones.Count);
            _zones.Add(new Zone(Win(rc), ZoneKind.Preset, i));
        }

        // the rows: icon, name, and the two arrows on the right
        int rowH = RowH(k), y0 = area.Y + PresetH(k), icon = Ce(22 * k), btn = Ce(32 * k);
        for (int i = 0; i < _order.Count; i++)
        {
            var id = _order[i];
            int y = y0 + i * rowH;
            g.DrawLine(hair, area.X, y, area.Right, y);
            if (i == _kbRow)
            {
                using var kb = new SolidBrush(Color.FromArgb(16, Cyan));
                g.FillRectangle(kb, area.X, y + 1, area.Width, rowH - 1);
            }
            IconPainter.Scenario(g, id, new RectangleF(area.X + Ce(4 * k), y + (rowH - icon) / 2f, icon, icon), _colorOf(id), 1.8f * k);
            string name = id == ProfileId.SuperBattery ? "Super Battery" : id.ToString();
            g.DrawString(name, nameF, whiteB, area.X + Ce(4 * k) + icon + Ce(12 * k), y + (rowH - nameF.GetHeight(g)) / 2f);

            void Arrow(string glyph, int x, bool enabled, ZoneKind kind)
            {
                var rc = new Rectangle(x, y + (rowH - btn) / 2, btn, btn);
                bool hot = enabled && HotZone == _zones.Count;
                if (hot)
                {
                    using var hp = RoundPath(rc, Ce(7 * k));
                    using var hb = new SolidBrush(Color.FromArgb(26, 255, 255, 255));
                    g.FillPath(hb, hp);
                }
                using var gb = new SolidBrush(!enabled ? Color.FromArgb(60, Muted) : hot ? Cyan : Ink);
                g.DrawString(glyph, glyphF, gb, rc, CenterText);
                // a disabled arrow keeps its zone slot (so the indices stay stable) but takes no clicks
                _zones.Add(new Zone(enabled ? Win(rc) : Rectangle.Empty, kind, i));
            }
            Arrow("", area.Right - btn * 2 - Ce(6 * k), i > 0, ZoneKind.Up);                    // MDL2 ChevronUp
            Arrow("", area.Right - btn, i < _order.Count - 1, ZoneKind.Down);                    // MDL2 ChevronDown
        }
    }

    protected override int ContentHit(Point p)
    {
        for (int i = 0; i < _zones.Count; i++) if (_zones[i].R.Contains(p)) return i;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        if (zone < 0 || zone >= _zones.Count) return;
        var z = _zones[zone];
        switch (z.Kind)
        {
            case ZoneKind.Preset:
                _order.Clear();
                _order.AddRange(z.Idx == 0 ? Profiles.Order : Profiles.ByPower);
                break;
            case ZoneKind.Up: MoveRow(z.Idx, -1); break;
            case ZoneKind.Down: MoveRow(z.Idx, 1); break;
        }
        Rerender();
    }

    private bool MoveRow(int i, int dir)
    {
        int j = i + dir;
        if (i < 0 || j < 0 || i >= _order.Count || j >= _order.Count) return false;
        (_order[i], _order[j]) = (_order[j], _order[i]);
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up: case Keys.Down:
                _kbRow = _kbRow < 0 ? 0 : Math.Clamp(_kbRow + (keyData == Keys.Down ? 1 : -1), 0, _order.Count - 1);
                Rerender();
                return true;
            case Keys.Control | Keys.Up: case Keys.Control | Keys.Down:
                if (_kbRow < 0) _kbRow = 0;
                else
                {
                    int dir = keyData == (Keys.Control | Keys.Down) ? 1 : -1;
                    if (MoveRow(_kbRow, dir)) _kbRow += dir;
                }
                Rerender();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
