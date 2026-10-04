using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// First card of the backup path test (Core/BackupPathTest.cs): what the test does, the one
/// choice it offers - whether to check the fan curve as well - and the start button. The
/// start button is the owner's consent to the writes the text lists; the curve switch is a
/// separate consent, off until the owner turns it on.
/// </summary>
public sealed class BackupTestIntroForm : GhostCardForm
{
    private readonly Action<bool> _start;
    private bool _curve;
    private Rectangle _row;   // window coordinates of the switch row

    public BackupTestIntroForm(Action<bool> start)
        : base("//PATH-TEST", Lang.T("bpt_title"), Lang.T("bpt_intro"), Lang.T("bpt_start"), Lang.T("fw_dlg_later"), () => { })
    {
        _start = start;
    }

    protected override int CardWidth => 580;

    protected override bool Acknowledge() { _start(_curve); return true; }

    private static Font HintFont(float k) => new("Segoe UI", 12f * k, FontStyle.Regular, GraphicsUnit.Pixel);
    private static int Ce(float v) => (int)Math.Ceiling(v);

    protected override int MeasureContent(Graphics g, float k, int width)
    {
        using var hintF = HintFont(k);
        int textW = width - Ce(44 * k) - Ce(12 * k);
        return Ce(Math.Max(24 * k, g.MeasureString(Lang.T("bpt_curve_opt"), LabelFont, textW).Height))
             + Ce(3 * k) + Ce(g.MeasureString(Lang.T("bpt_curve_hint"), hintF, textW).Height) + Ce(4 * k);
    }

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        using var hintF = HintFont(k);
        int swW = Ce(44 * k), swH = Ce(24 * k), textX = area.X + swW + Ce(12 * k), textW = area.Right - textX;
        float labelH = Math.Max(swH, g.MeasureString(Lang.T("bpt_curve_opt"), LabelFont, textW).Height);
        PaintSwitch(g, new Rectangle(area.X, area.Y + Ce((labelH - swH) / 2f), swW, swH), _curve);
        using (var lb = new SolidBrush(HotZone == 0 || _curve ? White : Ink))
        using (var mid = new StringFormat { LineAlignment = StringAlignment.Center })
            g.DrawString(Lang.T("bpt_curve_opt"), LabelFont, lb, new RectangleF(textX, area.Y, textW, labelH), mid);
        using (var hb = new SolidBrush(Muted))
            g.DrawString(Lang.T("bpt_curve_hint"), hintF, hb, new RectangleF(textX, area.Y + labelH + 3 * k, textW, area.Height));
        _row = new Rectangle(area.X + origin.X, area.Y + origin.Y, area.Width, Ce(labelH));
    }

    protected override int ContentHit(Point p) => _row.Contains(p) ? 0 : -1;

    protected override void ContentClick(int zone)
    {
        _curve = !_curve;
        Rerender();
    }
}

/// <summary>
/// The running card of the backup path test: the steps as a checklist, the live reading and
/// a bar for the current step. Its one button cancels the run; so does closing the card. The
/// card never closes itself on Cancel - the run restores the state it found first, and the
/// caller closes the card when that has finished (<see cref="Finish"/>).
/// </summary>
public sealed class BackupTestRunForm : GhostCardForm
{
    private readonly (BackupPathTest.Step Step, string Label)[] _steps;
    private readonly Action _cancel;
    private BackupPathTest.Progress _at = new(BackupPathTest.Step.Read, 0, "");
    private bool _finished, _cancelled;

    public BackupTestRunForm(bool withCurve, Action cancel)
        : base("//PATH-TEST", Lang.T("bpt_title"), Lang.T("bpt_running"), Lang.T("pt_cancel"), "", () => { })
    {
        _cancel = cancel;
        var steps = new List<(BackupPathTest.Step, string)>
        {
            (BackupPathTest.Step.Read, Lang.T("bpt_s_read")),
            (BackupPathTest.Step.OneByte, Lang.T("bpt_s_byte")),
            (BackupPathTest.Step.Profiles, Lang.T("bpt_s_profiles")),
        };
        if (withCurve) steps.Add((BackupPathTest.Step.Curve, Lang.T("bpt_s_curve")));
        steps.Add((BackupPathTest.Step.Restore, Lang.T("bpt_s_restore")));
        _steps = steps.ToArray();
    }

    protected override int CardWidth => 580;

    /// <summary>Progress from the run (UI thread).</summary>
    public void Advance(BackupPathTest.Progress p)
    {
        if (IsDisposed || _finished) return;
        _at = p;
        Rerender();
    }

    /// <summary>The run is over: close without asking for a cancel.</summary>
    public void Finish()
    {
        _finished = true;
        if (!IsDisposed) Close();
    }

    private void RequestCancel()
    {
        if (_finished || _cancelled) return;
        _cancelled = true;
        try { _cancel(); } catch { }
    }

    protected override bool Acknowledge() { RequestCancel(); return false; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Esc and the ✕ ask for a cancel too, and the card stays until the restore has run.
        if (!_finished) { RequestCancel(); if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true; }
        base.OnFormClosing(e);
    }

    private static int Ce(float v) => (int)Math.Ceiling(v);
    private static int RowH(float k) => Ce(30 * k);

    protected override int MeasureContent(Graphics g, float k, int width) =>
        _steps.Length * RowH(k) + Ce(10 * k) + Ce(18 * k) + Ce(10 * k) + Ce(6 * k);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        using var liveF = new Font("Segoe UI", 12f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var mid = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        int cur = Array.FindIndex(_steps, s => s.Step == _at.Step);
        if (cur < 0) cur = _steps.Count(s => s.Step < _at.Step);   // a step that is not on this card's list
        int rowH = RowH(k), dot = Ce(16 * k);

        for (int i = 0; i < _steps.Length; i++)
        {
            int y = area.Y + i * rowH;
            var circle = new RectangleF(area.X + 1, y + (rowH - dot) / 2f, dot, dot);
            bool done = i < cur, now = i == cur;
            if (done)
            {
                using var fb = new SolidBrush(Cyan);
                g.FillEllipse(fb, circle);
                using var tick = new Pen(Color.FromArgb(0x0A, 0x0D, 0x14), 1.8f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(tick, new[]
                {
                    new PointF(circle.X + dot * 0.27f, circle.Y + dot * 0.52f),
                    new PointF(circle.X + dot * 0.44f, circle.Y + dot * 0.69f),
                    new PointF(circle.X + dot * 0.74f, circle.Y + dot * 0.34f),
                });
            }
            else
            {
                using var ring = new Pen(now ? Cyan : Color.FromArgb(120, Muted), Math.Max(1.4f, 1.6f * k));
                g.DrawEllipse(ring, circle);
                if (now) { using var core = new SolidBrush(Cyan); g.FillEllipse(core, RectangleF.Inflate(circle, -dot * 0.28f, -dot * 0.28f)); }
            }
            using var tb = new SolidBrush(now ? White : done ? Ink : Muted);
            g.DrawString(_steps[i].Label, LabelFont, tb, new RectangleF(area.X + dot + Ce(12 * k), y, area.Width - dot - Ce(12 * k), rowH), mid);
        }

        int ly = area.Y + _steps.Length * rowH + Ce(10 * k);
        using (var lb = new SolidBrush(Muted))
            g.DrawString(_cancelled ? Lang.T("bpt_cancelling") : _at.Live, liveF, lb, new RectangleF(area.X, ly, area.Width, Ce(18 * k)), mid);

        var track = new RectangleF(area.X, ly + Ce(18 * k) + Ce(10 * k), area.Width, Ce(6 * k));
        using (var tp = RoundPath(track, Ce(3 * k)))
        using (var tb = new SolidBrush(Color.FromArgb(0x2E, 0x36, 0x46)))
            g.FillPath(tb, tp);
        float f = (float)Math.Clamp(_at.Fraction, 0, 1);
        if (f > 0.01f)
        {
            var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * f), track.Height);
            using var fp = RoundPath(fill, Ce(3 * k));
            using var fb = new SolidBrush(Cyan);
            g.FillPath(fb, fp);
        }
    }
}
