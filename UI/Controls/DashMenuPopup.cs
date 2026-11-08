using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// The small menu of a Dashboard tile (the ⋯), in the look of the app's own popups (HelpPopup,
/// the themed drop-down list) instead of the system context menu: a dark card with a soft
/// shadow, rows that light up under the cursor, thin separators. A ToolStripDropDown hosts the
/// painted control, so it closes by itself on a click outside, on Escape and when the window
/// loses focus; an item runs its action after the menu has closed.
/// </summary>
internal sealed class DashMenuPopup : ToolStripDropDown
{
    public sealed record Item(string Text, Action? Run, bool Separator = false, Color? Accent = null)
    {
        public static Item Line => new("", null, true);
    }

    private DashMenuPopup(Control owner, IReadOnlyList<Item> items)
    {
        AutoSize = false;
        Padding = Padding.Empty;
        Margin = Padding.Empty;
        DropShadowEnabled = true;
        BackColor = Theme.Card;
        var body = new Body(items, Dpi.Of(owner) / 96f, run => { Close(); run(); });
        var host = new ToolStripControlHost(body) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty, Size = body.Size };
        Items.Add(host);
        MinimumSize = MaximumSize = Size = body.Size;
    }

    /// <summary>Preview tool only: the menu as a bitmap, with row <paramref name="hot"/> under the cursor.</summary>
    internal static Bitmap Render(Control owner, IReadOnlyList<Item> items, int hot)
    {
        using var body = new Body(items, Dpi.Of(owner) / 96f, _ => { }) { Hot = hot };
        var bmp = new Bitmap(body.Width, body.Height);
        body.DrawToBitmap(bmp, new Rectangle(Point.Empty, body.Size));
        return bmp;
    }

    /// <summary>Opens the menu with its top-left corner at <paramref name="at"/> (client coordinates of <paramref name="owner"/>), kept on screen.</summary>
    public static void Open(Control owner, Point at, IReadOnlyList<Item> items)
    {
        var menu = new DashMenuPopup(owner, items);
        var screen = owner.PointToScreen(at);
        var area = Screen.FromControl(owner).WorkingArea;
        if (screen.X + menu.Width > area.Right) screen.X = area.Right - menu.Width;
        if (screen.Y + menu.Height > area.Bottom) screen.Y = owner.PointToScreen(at).Y - menu.Height - (int)(8 * Dpi.Of(owner) / 96f);
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(screen);
    }

    private sealed class Body : Control
    {
        private readonly IReadOnlyList<Item> _items;
        private readonly Action<Action> _pick;
        private readonly float _k;
        private readonly Font _font;
        private readonly int _rowH, _sepH, _padX, _padY;
        private int _hot = -1;

        /// <summary>The highlighted row (the preview tool sets it; the mouse drives it otherwise).</summary>
        public int Hot { get => _hot; set { _hot = value; Invalidate(); } }

        public Body(IReadOnlyList<Item> items, float k, Action<Action> pick)
        {
            _items = items; _pick = pick; _k = k;
            _font = Dpi.Font("Segoe UI", 10f);
            _rowH = Math.Max((int)(34 * k), _font.Height + (int)(14 * k));
            _sepH = (int)(9 * k);
            _padX = (int)(14 * k);
            _padY = (int)(6 * k);
            DoubleBuffered = true;
            TabStop = false;
            BackColor = Theme.Card;
            int w = (int)(180 * k);
            foreach (var it in items)
                if (!it.Separator) w = Math.Max(w, TextRenderer.MeasureText(it.Text, _font, Size.Empty, TextFormatFlags.NoPadding).Width + _padX * 2 + (int)(12 * k));
            int h = _padY * 2;
            foreach (var it in items) h += it.Separator ? _sepH : _rowH;
            Size = new Size(w, h);
        }

        private Rectangle RowRect(int index)
        {
            int y = _padY;
            for (int i = 0; i < index; i++) y += _items[i].Separator ? _sepH : _rowH;
            return new Rectangle(0, y, Width, _items[index].Separator ? _sepH : _rowH);
        }

        private int HitRow(Point p)
        {
            for (int i = 0; i < _items.Count; i++) if (!_items[i].Separator && RowRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            using (var pen = new Pen(Theme.BorderStrong)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                var r = RowRect(i);
                if (it.Separator)
                {
                    using var sp = new Pen(Theme.Border);
                    g.DrawLine(sp, _padX, r.Top + r.Height / 2, Width - _padX, r.Top + r.Height / 2);
                    continue;
                }
                bool hot = i == _hot;
                if (hot)
                {
                    var hr = new Rectangle((int)(6 * _k), r.Top + (int)(2 * _k), Width - (int)(12 * _k), r.Height - (int)(4 * _k));
                    using var path = Theme.RoundRect(hr, 6 * _k);
                    using var b = new SolidBrush(Theme.AccentSoft);
                    g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, it.Text, _font, new Rectangle(_padX, r.Top, Width - _padX * 2, r.Height), hot ? Theme.Accent : it.Accent ?? Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitRow(e.Location);
            if (h != _hot) { _hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != -1) { _hot = -1; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitRow(e.Location);
            if (h >= 0 && _items[h].Run is { } run) _pick(run);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _font.Dispose();
            base.Dispose(disposing);
        }
    }
}
