using System.Drawing.Text;

namespace GhostDeck;

/// <summary>
/// The .NET card (TECHNICAL 19.10): an update needs a newer .NET Desktop Runtime than the
/// computer has, so the update does not go ahead on its own. Three answers, one per button:
/// install .NET together with the update (accent), download .NET myself (left, opens
/// Microsoft's page), cancel. Esc and the ✕ are Cancel. The card only records the answer in
/// <see cref="Picked"/>; the Updates page acts on it.
///
/// The card is as wide as its three buttons need in the current language, and never narrower
/// than a plain message card, so no label is cut or wraps.
/// </summary>
public sealed class NetRuntimeCard : GhostCardForm
{
    public enum Choice { Cancel, Install, Self }

    /// <summary>The user's answer; Cancel until a button says otherwise.</summary>
    public Choice Picked { get; private set; } = Choice.Cancel;

    private readonly int _width;
    private readonly string _aux;

    public NetRuntimeCard(string tag, int major)
        : base("//UPDATE", string.Format(Lang.T("net_title"), major),
               string.Format(Lang.T("net_body"), tag, major), string.Format(Lang.T("net_install"), major), Lang.T("gen_cancel"), () => { })
    {
        _aux = string.Format(Lang.T("net_self"), major);
        _width = WidthFor(string.Format(Lang.T("net_install"), major), Lang.T("gen_cancel"), _aux);
    }

    protected override int CardWidth => _width;
    protected override string AuxLabel => _aux;
    protected override Color AuxColor => Ink;

    protected override bool Acknowledge() { Picked = Choice.Install; return true; }

    protected override void AuxClick()
    {
        Picked = Choice.Self;
        Close();
    }

    /// <summary>
    /// The card width (logical pixels) that holds the three buttons side by side: the same
    /// measure, padding and gaps the base class uses for its button row, plus the card's side
    /// margins and room between the left button and the right pair.
    /// </summary>
    private static int WidthFor(string ack, string later, string aux)
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var f = Dpi.Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        float w(string s) => g.MeasureString(s, f).Width + 28;   // padX 14 on each side
        float row = w(ack) + 8 + w(later) + 24 + w(aux);         // ack, gap, later, room, aux
        int sides = 5 + 15 + 18;                                  // rail + left inset + right inset
        return Math.Max(500, (int)Math.Ceiling(row * 1.04f) + sides);
    }
}
