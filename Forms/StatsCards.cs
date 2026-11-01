using System.Drawing;
using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The anonymous-statistics consent card (plan §8.3, §12 pkt 8): what a report holds, two equal
/// choices, and a third small action that opens the report as it would go out now. No names, no
/// e-mail addresses, nothing to copy for a fork.
/// </summary>
public sealed class StatsConsentCard : GhostCardForm
{
    private readonly Action _sample;

    public StatsConsentCard(Action onShare, Action onShowSample)
        : base("//STATS", Lang.T("stats_consent_title"), Lang.T("stats_consent_body"), Lang.T("stats_consent_yes"), Lang.T("stats_consent_no"), onShare)
    {
        _sample = onShowSample;
    }

    protected override int CardWidth => 560;
    protected override string AuxLabel => Lang.T("stats_sample");
    protected override Color AuxColor => Cyan;
    protected override void AuxClick() => _sample();
}

/// <summary>The report text in a fixed-width block, one top-level field per line.</summary>
public sealed class StatsReportCard : GhostCardForm
{
    private readonly string _text;

    public StatsReportCard(string text)
        : base("//STATS", Lang.T("stats_report_title"), Lang.T("stats_report_body"), Lang.T("gen_ok"), "", () => { })
    {
        _text = text;
    }

    protected override int CardWidth => 640;

    private static Font MonoFont(float k) => Dpi.Font("Consolas", 11.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);

    protected override int MeasureContent(Graphics g, float k, int width)
    {
        using var f = MonoFont(k);
        return (int)Math.Ceiling(g.MeasureString(_text, f, width - (int)(16 * k)).Height) + (int)(12 * k);
    }

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        using var f = MonoFont(k);
        using var bg = new SolidBrush(FieldBg);
        using var path = RoundPath(area, (int)(6 * k));
        g.FillPath(bg, path);
        using var ink = new SolidBrush(Ink);
        g.DrawString(_text, f, ink, new RectangleF(area.X + 8 * k, area.Y + 6 * k, area.Width - 16 * k, area.Height - 8 * k));
    }
}
