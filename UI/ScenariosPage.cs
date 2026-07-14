using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

// =====================================================================
//  Scenarios
// =====================================================================
public sealed class ScenariosPage : ThemedPage
{
    private const int TileH = 280, Gap = 16, Pad = 28;
    private readonly Tile[] _tiles;
    private readonly SegControl _charge;
    private readonly ToggleSwitch _auto;
    private readonly FeatureBrick[] _bricks;
    private int _headH, _subY, _bricksTop;

    public ScenariosPage(MainDeps d) : base(d)
    {
        _tiles = Profiles.Order.Select(id => new Tile(d, id)).ToArray();
        foreach (var t in _tiles) Controls.Add(t);

        _charge = new SegControl(new[] { Lang.T("gen_off_short"), "60%", "80%", "100%" }, ChargeIndex()) { Size = new Size(280, 34) };
        _charge.SelectedChanged += i => D.SetChargeLimit(i switch { 1 => 60, 2 => 80, 3 => 100, _ => 0 });

        _auto = new ToggleSwitch { Checked = D.Settings.AutoSwitchEnabled };
        _auto.Toggled += v => D.SetAutoSwitch(v);

        // One uniform "brick" per feature (discussion feedback): toggles and the charge-limit
        // segment all live in matching boxes under the profile tiles.
        _bricks = new[]
        {
            new FeatureBrick("cooler_boost_short", "❄", "cooler_boost_hint",
                             () => D.Writable() && D.CoolerBoost(), v => D.SetCoolerBoost(v)),
            new FeatureBrick("overlay_title", "▦", "overlay_hint",
                             () => D.OverlayOn(), v => D.SetOverlay(v)),
            new FeatureBrick("st_charge", "⚡", _charge),
            new FeatureBrick("scen_autoswitch", "⇄", _auto),
        };
        foreach (var b in _bricks) Controls.Add(b);

        Resize += (_, _) => Relayout();
    }

    private int ChargeIndex() => D.Settings.ChargeLimit switch { 60 => 1, 80 => 2, 100 => 3, _ => 0 };

    public override void OnEnter()
    {
        _charge.Selected = ChargeIndex();
        _auto.Checked = D.Settings.AutoSwitchEnabled;
        foreach (var b in _bricks) b.SyncState();
        Relayout();
        Invalidate();
    }

    // External state changed (profile/cooler/overlay) — refresh tiles + bricks without re-laying out.
    public override void LiveRefresh()
    {
        _charge.Selected = ChargeIndex();
        _auto.Checked = D.Settings.AutoSwitchEnabled;
        foreach (var b in _bricks) b.SyncState();
        Invalidate(true);
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        foreach (var t in _tiles) t.Invalidate();
        _charge.Invalidate(); _auto.Invalidate();
        foreach (var b in _bricks) b.ApplyTheme();
    }

    private void Relayout()
    {
        // header height from real font metrics (DPI-safe)
        int titleH = new Font("Segoe UI", 18f, FontStyle.Bold).Height;
        int subH = new Font("Segoe UI", 10.5f).Height;
        _subY = 24 + titleH + 20;                       // title -> subtitle gap
        _headH = _subY + subH + 28;                     // subtitle -> tiles gap

        int avail = ClientSize.Width - Pad * 2;
        int tw = (avail - Gap * 3) / 4;                 // 4 in a row
        for (int i = 0; i < _tiles.Length; i++)
            _tiles[i].SetBounds(Pad + i * (tw + Gap), _headH, tw, TileH);

        // Uniform feature bricks, two per row, straight under the tiles (mockup W5 layout).
        _bricksTop = _headH + TileH + 24;
        const int cols = 2, brickH = 82;
        int bw = (avail - Gap * (cols - 1)) / cols;
        int rows = 0;
        for (int i = 0; i < _bricks.Length; i++)
        {
            int r = i / cols, c = i % cols;
            _bricks[i].SetBounds(Pad + c * (bw + Gap), _bricksTop + r * (brickH + Gap), bw, brickH);
            rows = r + 1;
        }
        int bricksBottom = _bricksTop + rows * (brickH + Gap);
        AutoScrollMinSize = new Size(820, bricksBottom + 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ApplyScroll(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var info = D.Status();
        TextRenderer.DrawText(g, Lang.T("scen_title"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);
        string sub = info.Device + (string.IsNullOrEmpty(D.Firmware) ? "" : "  ·  " + D.Firmware);
        TextRenderer.DrawText(g, sub, new Font("Segoe UI", 10.5f), new Point(Pad, _subY), Theme.Muted);
        // (the tier badge lives in the header strip now, next to the version)
    }

    private sealed class Tile : Control
    {
        private readonly MainDeps _d;
        private readonly ProfileId _id;
        private bool _hover;
        public Tile(MainDeps d, ProfileId id)
        {
            _d = d; _id = id; DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand;
            Click += (_, _) => { if (_d.Writable()) { _d.SetProfile(_id); Parent?.Invalidate(true); } };
        }
        public void Refresh2() => Invalidate();
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            bool active = _d.Writable() && _d.Current() == _id;
            var def = Profiles.Get(_id);
            var col = Theme.Profile(_d.ColorOf(_id));
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(outer, 6))     // mało zaokrąglone
            {
                using var b = new SolidBrush(active ? Theme.AccentSoft : Theme.Card);
                g.FillPath(b, path);
                using var pen = new Pen(active ? Theme.Accent : (_hover ? Theme.BorderStrong : Theme.Border), active ? 2f : 1f);
                g.DrawPath(pen, path);
            }
            if (active)
            {
                // soft inner neon (ghostdeck.dev card style): fading strokes just inside the border
                for (int i = 1; i <= 3; i++)
                {
                    using var gp = Theme.RoundRect(new RectangleF(0.5f + i * 2, 0.5f + i * 2, Width - 1 - i * 4, Height - 1 - i * 4), 6);
                    using var pen = new Pen(Color.FromArgb(46 - i * 12, Theme.Accent), 2f);
                    g.DrawPath(pen, gp);
                }
            }
            // icon centred on top, text stacked below, SELECT/ACTIVE footer (font-height stacked = DPI-safe)
            int iconBox = 76;
            var nameFont = new Font("Segoe UI", 15f, FontStyle.Bold);
            var subFont = new Font("Segoe UI", 10.5f);
            var footFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            int nameH = nameFont.Height, subH = subFont.Height, g1 = 16, g2 = 6, g3 = 18, footH = footFont.Height + 12;
            int blockH = iconBox + g1 + nameH + g2 + subH + g3 + footH;
            int top = Math.Max(16, (Height - blockH) / 2);
            IconPainter.Scenario(g, _id, new RectangleF((Width - iconBox) / 2f, top, iconBox, iconBox), col, 4f);
            int textW = Width - 24;
            TextRenderer.DrawText(g, def.Label, nameFont,
                new Rectangle(12, top + iconBox + g1, textW, nameH), Theme.Text,
                TextFormatFlags.Top | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Lang.T(def.SubKey), subFont,
                new Rectangle(12, top + iconBox + g1 + nameH + g2, textW, subH), Theme.Muted,
                TextFormatFlags.Top | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            int footY = top + iconBox + g1 + nameH + g2 + subH + g3;
            if (active)
            {
                string t = Lang.T("scen_active");
                int tw = TextRenderer.MeasureText(t, new Font("Segoe UI", 9.5f, FontStyle.Bold)).Width;   // same font Ui.Pill sizes with
                Ui.Pill(g, t, new Point((Width - (tw + 32)) / 2, footY), Theme.Accent);
            }
            else
            {
                TextRenderer.DrawText(g, Lang.T("scen_select"), footFont,
                    new Rectangle(12, footY + 6, textW, footFont.Height + 2),
                    _hover ? Theme.Muted : Theme.Faint,
                    TextFormatFlags.Top | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    /// <summary>
    /// Small reusable "feature card": rounded card with an icon box, a label and a right-side
    /// toggle switch — styled after MSI Center's feature tiles. Cooler Boost is the first; more
    /// on/off features (e.g. Windows key, display features) can be added to the grid the same way.
    /// </summary>
    private sealed class FeatureBrick : Control
    {
        private readonly string _labelKey;
        private readonly string _glyph;
        private readonly Func<bool>? _get;
        private readonly ToggleSwitch? _toggle;
        private readonly Control _right;
        private readonly HelpDot? _help;
        private readonly ToolTip _tip = new() { InitialDelay = 250, AutoPopDelay = 15000, ReshowDelay = 100 };
        private bool _hover;

        public FeatureBrick(string labelKey, string glyph, string tipKey, Func<bool> get, Action<bool> set)
        {
            _labelKey = labelKey; _glyph = glyph; _get = get;
            DoubleBuffered = true; ResizeRedraw = true;
            BackColor = Theme.Card;                       // so the child controls blend with the card interior
            var toggle = new ToggleSwitch { Checked = get() };
            toggle.Toggled += v => set(v);
            _toggle = toggle; _right = toggle;
            _help = new HelpDot();
            _tip.SetToolTip(_help, Ui.Wrap(Lang.T(tipKey), 46));
            Controls.Add(_right);
            Controls.Add(_help);
            Resize += (_, _) => LayoutInner();
        }

        /// <summary>Brick hosting an arbitrary right-side control (e.g. a SegControl) instead of a toggle.</summary>
        public FeatureBrick(string labelKey, string glyph, Control right)
        {
            _labelKey = labelKey; _glyph = glyph; _right = right;
            DoubleBuffered = true; ResizeRedraw = true;
            BackColor = Theme.Card;
            Controls.Add(_right);
            Resize += (_, _) => LayoutInner();
        }

        public void SyncState() { if (_toggle != null && _get != null) _toggle.Checked = _get(); }
        public void ApplyTheme() { BackColor = Theme.Card; _right.Invalidate(); _help?.Invalidate(); Invalidate(); }

        private void LayoutInner()
        {
            _right.Location = new Point(Width - _right.Width - 18, (Height - _right.Height) / 2);
            _help?.SetBounds(_right.Left - _help.Width - 14, (Height - _help.Height) / 2, _help.Width, _help.Height);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(outer, 6))
            {
                using var b = new SolidBrush(Theme.Card);
                g.FillPath(b, path);
                using var pen = new Pen(_hover ? Theme.BorderStrong : Theme.Border, 1f);
                g.DrawPath(pen, path);
            }
            // icon box (outlined square + glyph, like the MSI Center feature cards)
            int box = 32, bx = 18, by = (Height - box) / 2;
            using (var pen = new Pen(Theme.Accent, 1.6f))
            using (var ip = Theme.RoundRect(new RectangleF(bx + 0.5f, by + 0.5f, box - 1, box - 1), 6))
                g.DrawPath(pen, ip);
            using (var gf = new Font("Segoe UI Symbol", 12f))
                Ui.CenterGlyph(g, _glyph, gf, Theme.Accent, new RectangleF(bx, by, box, box));
            // label (stops before the help dot + toggle)
            int lx = bx + box + 14, rightPad = _right.Width + 14 + (_help != null ? _help.Width + 14 : 0) + 12;
            TextRenderer.DrawText(g, Lang.T(_labelKey), new Font("Segoe UI", 11.5f, FontStyle.Bold),
                new Rectangle(lx, 0, Math.Max(20, Width - lx - rightPad), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Circled "?" help marker; shows an explanatory tooltip on hover (used by feature bricks).</summary>
    private sealed class HelpDot : Control
    {
        public HelpDot() { DoubleBuffered = true; ResizeRedraw = true; Size = new Size(22, 22); Cursor = Cursors.Help; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Card);
            using (var pen = new Pen(Theme.Muted, 1.4f))
                g.DrawEllipse(pen, 1f, 1f, Width - 2f, Height - 2f);
            using var f = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            Ui.CenterGlyph(g, "?", f, Theme.Muted, new RectangleF(0, 0, Width, Height));
        }
    }
}
