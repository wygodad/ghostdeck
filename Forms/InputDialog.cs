namespace GhostDeck;

/// <summary>
/// One-line text prompt as a GhostDeck card (fan-curve preset names, the custom Fan Boost
/// time). The text is typed into a real text box riding over the card
/// (<see cref="CardTextHost"/>). An optional validator runs on OK: a non-null result keeps
/// the card open with the field marked, and a non-empty one is shown under the field - so a
/// taken name is answered where it was typed, not in a second message window.
/// </summary>
public sealed class InputDialog : GhostCardForm
{
    private readonly string _initial;
    private readonly Func<string, string?>? _validate;
    private CardTextHost? _host;
    private Rectangle _textRect, _fieldZone;
    private string? _error;        // null = fine, "" = marked without a message
    private string _result = "";

    private InputDialog(string tag, string title, string label, string initial, Func<string, string?>? validate)
        : base(tag, title, label, Lang.T("gen_ok"), Lang.T("gen_cancel"), () => { })
    {
        _initial = initial;
        _validate = validate;
    }

    protected override int CardWidth => 460;
    protected override bool SelectAllOnOpen => true;

    protected override void CreateTextFields()
    {
        _host = AddTextField(_initial, HorizontalAlignment.Left, () => _textRect);
        _host.Box.TextChanged += (_, _) => { if (_error != null) { _error = null; Rerender(); } };
    }

    protected override bool Acknowledge()
    {
        string t = (_host?.Box.Text ?? _initial).Trim();
        string? err = t.Length == 0 ? "" : _validate?.Invoke(t);
        if (err != null)
        {
            _error = err;
            Rerender();
            FocusField(_host, selectAll: true);
            return false;
        }
        _result = t;
        DialogResult = DialogResult.OK;
        return true;
    }

    private static int Ce(float v) => (int)Math.Ceiling(v);

    private int ErrorH(Graphics g, float k, int width) =>
        string.IsNullOrEmpty(_error) ? 0 : Ce(g.MeasureString(_error, LabelFont, width).Height) + Ce(8 * k);

    protected override int MeasureContent(Graphics g, float k, int width) => Ce(36 * k) + ErrorH(g, k, width) + Ce(2 * k);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        var field = new Rectangle(area.X, area.Y, area.Width, Ce(36 * k));
        var text = PaintField(g, field, IsFocused(_host), _error != null);
        _textRect = new Rectangle(text.X + origin.X, text.Y + origin.Y, text.Width, text.Height);
        _fieldZone = new Rectangle(field.X + origin.X, field.Y + origin.Y, field.Width, field.Height);
        if (!string.IsNullOrEmpty(_error))
        {
            using var eb = new SolidBrush(SoftRed);
            g.DrawString(_error, LabelFont, eb, new RectangleF(area.X, field.Bottom + Ce(8 * k), area.Width, area.Bottom - field.Bottom));
        }
    }

    protected override int ContentHit(Point p) => _fieldZone.Contains(p) ? 0 : -1;
    protected override void ContentClick(int zone) => FocusField(_host);

    /// <summary>
    /// Returns the trimmed text, or null when cancelled. <paramref name="validate"/> gets the
    /// trimmed text and returns null to accept it, or a message (may be empty) to refuse it.
    /// </summary>
    public static string? Ask(IWin32Window? owner, string title, string label, string initial = "",
                              Func<string, string?>? validate = null, string tag = "//INPUT")
    {
        using var dlg = new InputDialog(tag, title, label, initial, validate);
        return dlg.ShowOver(owner as Form) == DialogResult.OK ? dlg._result : null;
    }
}
