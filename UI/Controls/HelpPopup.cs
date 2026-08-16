using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The app's own help bubble, shown by clicking a <see cref="HelpDot"/> and dismissed by clicking
/// anywhere else, or the same dot again.
///
/// A system tooltip was doing this job before and could not: it styles itself from the OS rather
/// than from the theme, it appears on hover so it covers what the reader is pointing at, and it
/// takes itself away after a few seconds, which is not long enough for a paragraph that explains a
/// measurement. This one is drawn like the rest of the app and stays until it is dismissed.
///
/// Built on ToolStripDropDown purely for the window behaviour: a top-level surface that shows
/// without stealing focus and closes itself on an outside click is exactly what that class already
/// is. The content is a hosted control - a dropdown with no items lays itself out to nothing, so
/// the card control is what gives the popup its size and its painting.
/// </summary>
internal sealed class HelpPopup : ToolStripDropDown
{
    // 640, not 430: the fan-curve bubbles explain a whole view (what it shows, how to
    // read it, what to watch for) and a narrow column turned them into a ribbon.
    private const int MaxWidth = 640, PadX = 18, PadY = 15;
    private static readonly Font TextFont = new("Segoe UI", 10f);

    private static HelpPopup? _open;
    private static object? _lastOwner;
    private static DateTime _closedAt = DateTime.MinValue;

    private HelpPopup(string text, int maxScreenWidth)
    {
        AutoSize = false;
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        DropShadowEnabled = true;
        BackColor = Theme.Card;

        int textW = Math.Max(140, Math.Min(MaxWidth, maxScreenWidth) - PadX * 2);
        var measured = TextRenderer.MeasureText(text, TextFont, new Size(textW, 0), TextFormatFlags.WordBreak);
        var card = new Card(text, new Size(measured.Width + PadX * 2, measured.Height + PadY * 2));
        var host = new ToolStripControlHost(card)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = card.Size,
        };
        Items.Add(host);
        MinimumSize = MaximumSize = Size = card.Size;
    }

    /// <summary>The drawn body of the bubble: themed card, accent border, wrapped text.</summary>
    private sealed class Card : Control
    {
        private readonly string _text;

        public Card(string text, Size size)
        {
            _text = text;
            Size = size;
            DoubleBuffered = true;
            TabStop = false;
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            using (var path = Theme.RoundRect(new RectangleF(0.7f, 0.7f, Width - 1.4f, Height - 1.4f), 9))
            using (var pen = new Pen(Theme.Accent, 1.4f))
                g.DrawPath(pen, path);
            TextRenderer.DrawText(g, _text, TextFont,
                new Rectangle(PadX, PadY, Width - PadX * 2, Height - PadY * 2), Theme.Text,
                TextFormatFlags.WordBreak);
        }
    }

    protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
    {
        base.OnClosed(e);
        if (ReferenceEquals(_open, this)) { _open = null; _closedAt = DateTime.UtcNow; }
    }

    /// <summary>Closes whatever bubble is open, if any.</summary>
    public static void CloseAny() => _open?.Close();

    /// <summary>
    /// Opens the bubble under <paramref name="anchor"/> (a rectangle in <paramref name="host"/>
    /// coordinates), or closes it if that same anchor opened the one already showing.
    /// <paramref name="owner"/> identifies the caller so a second click on the same dot closes
    /// rather than reopens.
    /// </summary>
    public static void Toggle(Control host, Rectangle anchor, string text, object owner)
    {
        bool sameOwner = ReferenceEquals(_lastOwner, owner);
        // Clicking the dot while its bubble is open: the outside-click close may have already run
        // before this click arrives, so a fresh close by the same owner reads as "close", not as
        // an immediate reopen.
        if (_open != null || (sameOwner && (DateTime.UtcNow - _closedAt).TotalMilliseconds < 250))
        {
            CloseAny();
            if (sameOwner) { _lastOwner = null; return; }
        }
        if (string.IsNullOrWhiteSpace(text)) return;

        var screen = Screen.FromControl(host).WorkingArea;
        var below = host.PointToScreen(new Point(anchor.Left, anchor.Bottom + 8));
        var popup = new HelpPopup(text, screen.Width - 48);

        // Prefer hanging under the dot and running left, which keeps the bubble inside the screen
        // for a dot near the right edge; flip above when there is no room below.
        int x = Math.Max(screen.Left + 8, Math.Min(below.X - popup.Width + anchor.Width, screen.Right - popup.Width - 8));
        int y = below.Y;
        if (y + popup.Height > screen.Bottom - 8)
            y = host.PointToScreen(new Point(anchor.Left, anchor.Top - 8)).Y - popup.Height;

        _open = popup;
        _lastOwner = owner;
        popup.Show(new Point(x, y));
    }
}

/// <summary>
/// The circled question mark that opens a <see cref="HelpPopup"/>. Same drawing everywhere it
/// appears, so a reader who has learned it on one page recognises it on the next.
/// </summary>
internal sealed class HelpDot : Control
{
    /// <summary>Supplies the text at click time, so a bubble about a live reading is never stale.</summary>
    public Func<string>? TextProvider { get; set; }

    public HelpDot()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Size = new Size(22, 22);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var host = Parent ?? this;
        HelpPopup.Toggle(host, Bounds, TextProvider?.Invoke() ?? "", this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        Render(g, new RectangleF(0, 0, Width, Height));
    }

    /// <summary>Draws the dot into an existing surface, for pages that paint rather than lay out.</summary>
    public static void Render(Graphics g, RectangleF r)
    {
        using var pen = new Pen(Theme.Muted, 1.4f);
        g.DrawEllipse(pen, r.X + 1f, r.Y + 1f, r.Width - 2f, r.Height - 2f);
        using var f = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        Ui.CenterGlyph(g, "?", f, Theme.Muted, r);
    }
}
