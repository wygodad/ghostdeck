namespace GhostDeck;

/// <summary>
/// The amber note of a Settings card: a tinted block with an amber rail on its left, a title
/// and a few lines under it that say what the card's action does. Same look as the "Restore
/// Windows settings" block of the Windows power card (WinPowerBody), as one drawn control: no
/// child windows, colours read from <see cref="Theme"/> while painting.
///
/// The block takes the width it is given and sets its own height from the wrapped text, so a
/// card lays it out as a full-width row (name it "wp-wide" in a CardSection).
/// </summary>
internal sealed class NoteBox : Control
{
    private static readonly Font FTitle = Dpi.Font("Segoe UI Semibold", 10.5f);
    private static readonly Font FBody = Dpi.Font("Segoe UI", 10f);
    private const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private readonly string _title;
    private readonly string _body;

    public NoteBox(string title, IEnumerable<string> lines)
    {
        _title = title;
        _body = string.Join("\n", lines);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Width = S(360);
        FitHeight();
    }

    private int S(float v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);

    private int TextW => Math.Max(S(60), Width - S(18) * 2);

    private static int TextH(string text, Font f, int width) =>
        text.Length == 0 ? 0 : TextRenderer.MeasureText(text, f, new Size(width, int.MaxValue), Wrap).Height;

    private void FitHeight()
    {
        int h = S(14) * 2 + TextH(_title, FTitle, TextW) + S(6) + TextH(_body, FBody, TextW);
        if (Height != h) Height = h;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitHeight();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color card = Theme.Card, amber = Theme.Amber;
        // the card's surface with a little amber in it, as in the Windows power card
        var tint = Color.FromArgb(
            (int)(card.R + (amber.R - card.R) * 0.08f),
            (int)(card.G + (amber.G - card.G) * 0.08f),
            (int)(card.B + (amber.B - card.B) * 0.08f));
        using (var bb = new SolidBrush(tint)) g.FillRectangle(bb, ClientRectangle);
        using (var rb = new SolidBrush(amber)) g.FillRectangle(rb, 0, 0, Math.Max(3, S(3)), Height);
        int x = S(18), y = S(14), w = TextW;
        int titleH = TextH(_title, FTitle, w);
        TextRenderer.DrawText(g, _title, FTitle, new Rectangle(x, y, w, titleH), Theme.Text, Wrap);
        y += titleH + S(6);
        TextRenderer.DrawText(g, _body, FBody, new Rectangle(x, y, w, TextH(_body, FBody, w)), amber, Wrap);
    }
}
