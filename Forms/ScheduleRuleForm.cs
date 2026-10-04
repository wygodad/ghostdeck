using System.Globalization;

namespace GhostDeck;

/// <summary>
/// Editor for one schedule rule, as a GhostDeck card: the scene (a select field), the
/// weekdays (seven cells, any number of them lit, Monday first) and a start / end time on a
/// 30-minute grid (two select fields). Commits into the passed rule on Save; a rule needs at
/// least one day, so Save with none lit marks the day row and keeps the card open.
/// </summary>
public sealed class ScheduleRuleForm : GhostCardForm
{
    private enum ZoneKind { Scene, Day, From, To }
    private readonly record struct Zone(Rectangle R, ZoneKind Kind, int Idx);

    private readonly ScheduleRule _rule;
    private readonly List<SceneDef> _scenes;
    private readonly string[] _times = Enumerable.Range(0, 48).Select(i => $"{i / 2:D2}:{i % 2 * 30:D2}").ToArray();
    private readonly string[] _dayAbbr = new string[7];   // Monday first
    private readonly List<Zone> _zones = new();
    private int _scene, _from, _to, _days;
    private bool _daysMissing;

    public ScheduleRuleForm(MainDeps d, ScheduleRule rule)
        : base("//SCHEDULE", Lang.T("sch_rule_title"), Lang.T("sch_desc"), Lang.T("set_save"), Lang.T("gen_cancel"), () => { })
    {
        _rule = rule;
        // the Settings card only opens this editor when at least one scene exists
        _scenes = d.Settings.Scenes;
        _scene = Math.Max(0, _scenes.FindIndex(s => s.Id.Equals(rule.SceneId, StringComparison.OrdinalIgnoreCase)));
        _days = rule.Days & 0x7F;
        int Idx(string t) => Math.Clamp((ScheduleRule.MinutesOf(t) + 15) / 30 % 48, 0, 47);
        _from = Idx(rule.Start);
        _to = Idx(rule.End);

        string[] abbr;
        try { abbr = CultureInfo.GetCultureInfo(Lang.CurrentCode).DateTimeFormat.AbbreviatedDayNames; }
        catch { abbr = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames; }
        for (int i = 0; i < 7; i++) _dayAbbr[i] = abbr[(i + 1) % 7];   // AbbreviatedDayNames is Sunday-first
    }

    protected override int CardWidth => 560;

    protected override bool Acknowledge()
    {
        if (_scenes.Count == 0) return true;
        if (_days == 0)
        {
            _daysMissing = true;
            Rerender();
            return false;
        }
        _rule.SceneId = _scenes[Math.Clamp(_scene, 0, _scenes.Count - 1)].Id;
        _rule.Days = _days;
        _rule.Start = _times[_from];
        _rule.End = _times[_to];
        DialogResult = DialogResult.OK;
        return true;
    }

    private static int Ce(float v) => (int)Math.Ceiling(v);
    private static int RowH(float k) => Ce(46 * k);

    protected override int MeasureContent(Graphics g, float k, int width) => 3 * RowH(k) + Ce(4 * k);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        _zones.Clear();
        Rectangle Win(Rectangle r) => new(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height);
        using var whiteB = new SolidBrush(White);
        using var inkB = new SolidBrush(Ink);
        using var hair = new Pen(Color.FromArgb(22, 243, 247, 255), 1f);

        int rowH = RowH(k), trackH = Ce(34 * k), pickW = Ce(340 * k), pickX = area.Right - pickW, inset = Ce(3 * k);
        Rectangle Track(int row, int x, int w) => new(x, area.Y + row * rowH + (rowH - trackH) / 2, w, trackH);
        void Label(int row, string text)
        {
            int y = area.Y + row * rowH;
            if (row > 0) g.DrawLine(hair, area.X, y, area.Right, y);
            g.DrawString(text, LabelFont, whiteB, new RectangleF(area.X, y, pickX - Ce(12 * k) - area.X, rowH), LeftText);
        }

        // scene
        Label(0, Lang.T("sch_scene"));
        var sceneTrack = Track(0, pickX, pickW);
        PaintSelect(g, sceneTrack, _scenes.Count > 0 ? _scenes[Math.Clamp(_scene, 0, _scenes.Count - 1)].Name : "", true, HotZone == _zones.Count);
        _zones.Add(new Zone(Win(sceneTrack), ZoneKind.Scene, 0));

        // days: one frame, seven cells, any number of them lit
        Label(1, Lang.T("sch_days"));
        var dayTrack = Track(1, pickX, pickW);
        PaintTrack(g, dayTrack, true);
        if (_daysMissing)
        {
            using var mp = RoundPath(dayTrack, Ce(9 * k));
            using var pen = new Pen(SoftRed, 1f);
            g.DrawPath(pen, mp);
        }
        int cellW = (pickW - inset * 2) / 7;
        for (int i = 0; i < 7; i++)
        {
            var rc = new Rectangle(dayTrack.X + inset + i * cellW, dayTrack.Y + inset, cellW, trackH - inset * 2);
            PaintCell(g, Rectangle.Inflate(rc, -1, 0), _dayAbbr[i], (_days >> i & 1) != 0, true, HotZone == _zones.Count);
            _zones.Add(new Zone(Win(rc), ZoneKind.Day, i));
        }

        // from ... to
        Label(2, Lang.T("sch_from"));
        string toWord = Lang.T("sch_to");
        int wordW = Ce(g.MeasureString(toWord, LabelFont).Width), gap = Ce(12 * k);
        int timeW = (pickW - wordW - gap * 2) / 2;
        var fromTrack = Track(2, pickX, timeW);
        var toTrack = Track(2, area.Right - timeW, timeW);
        PaintSelect(g, fromTrack, _times[_from], true, HotZone == _zones.Count);
        _zones.Add(new Zone(Win(fromTrack), ZoneKind.From, 0));
        g.DrawString(toWord, LabelFont, inkB, new RectangleF(fromTrack.Right, fromTrack.Y, toTrack.X - fromTrack.Right, trackH), CenterText);
        PaintSelect(g, toTrack, _times[_to], true, HotZone == _zones.Count);
        _zones.Add(new Zone(Win(toTrack), ZoneKind.To, 0));
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
        var screen = new Rectangle(Left + z.R.X, Top + z.R.Y, z.R.Width, z.R.Height);
        switch (z.Kind)
        {
            case ZoneKind.Day:
                _days ^= 1 << z.Idx;
                _daysMissing = false;
                Rerender();
                break;
            case ZoneKind.Scene:
                CardPopupList.Open(this, screen, _scenes.Select(s => new CardPopupList.Item(s.Name)).ToArray(), _scene, UiScale, _scenes,
                    i => { _scene = i; Rerender(); });
                break;
            case ZoneKind.From:
                CardPopupList.Open(this, screen, _times.Select(t => new CardPopupList.Item(t)).ToArray(), _from, UiScale, _times,
                    i => { _from = i; Rerender(); });
                break;
            case ZoneKind.To:
                CardPopupList.Open(this, screen, _times.Select(t => new CardPopupList.Item(t)).ToArray(), _to, UiScale, _dayAbbr,
                    i => { _to = i; Rerender(); });
                break;
        }
    }
}
