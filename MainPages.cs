using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>Shared themed widgets / drawing helpers for the tab pages.</summary>
internal static class Ui
{
    public static void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Theme.AccentFill;
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.Height = 40;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Faint background grid (ghostdeck.dev texture), drawn under the page content.</summary>
    public static void DrawGrid(Graphics g, Rectangle r)
    {
        const int pitch = 48;
        using var pen = new Pen(Theme.GridLine);
        for (int x = r.Left + pitch; x < r.Right; x += pitch) g.DrawLine(pen, x, r.Top, x, r.Bottom);
        for (int y = r.Top + pitch; y < r.Bottom; y += pitch) g.DrawLine(pen, r.Left, y, r.Right, y);
    }

    /// <summary>
    /// Runs a bulk control rebuild with painting + layout suspended (WM_SETREDRAW), then repaints
    /// once. Without this a rebuild (e.g. Settings after a language change) visibly blanks the
    /// page and repaints control-by-control.
    /// </summary>
    public static void BatchRedraw(Control c, Action work)
    {
        const int WM_SETREDRAW = 0x000B;
        if (!c.IsHandleCreated) { work(); return; }
        SendMessage(c.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        c.SuspendLayout();
        try { work(); }
        finally
        {
            c.ResumeLayout(true);
            SendMessage(c.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            c.Invalidate(true);
        }
    }

    public static void StyleGhost(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Theme.BorderStrong;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = Theme.Surface;
        b.ForeColor = Theme.Text;
        b.Font = new Font("Segoe UI", 10.5f);
        b.Cursor = Cursors.Hand;
        b.Height = 40;
    }

    // Pixel-centre a single glyph inside a rect by its measured ink box. Layout-rect centring
    // (StringFormat/TextRenderer VerticalCenter) leaves glyphs looking off because it centres the
    // font line box, not the glyph; measuring with GenericTypographic and offsetting fixes it.
    public static void CenterGlyph(Graphics g, string glyph, Font font, Color color, RectangleF rect)
    {
        var oldHint = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        var sz = g.MeasureString(glyph, font, rect.Size, fmt);
        var centred = new RectangleF(
            rect.X + (rect.Width - sz.Width) / 2f,
            rect.Y + (rect.Height - sz.Height) / 2f,
            sz.Width, sz.Height);
        using var br = new SolidBrush(color);
        g.DrawString(glyph, font, br, centred, fmt);
        g.TextRenderingHint = oldHint;
    }

    // Word-wrap plain text to at most maxChars per line (WinForms ToolTip does not wrap by itself,
    // so a long hint would run off-screen). Breaks on spaces; overlong words are kept whole.
    public static string Wrap(string text, int maxChars)
    {
        var words = text.Split(' ');
        var sb = new System.Text.StringBuilder();
        int lineLen = 0;
        foreach (var w in words)
        {
            if (lineLen > 0 && lineLen + 1 + w.Length > maxChars) { sb.Append('\n'); lineLen = 0; }
            else if (lineLen > 0) { sb.Append(' '); lineLen++; }
            sb.Append(w);
            lineLen += w.Length;
        }
        return sb.ToString();
    }

    public static void FillCard(Graphics g, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(r, 12);
        using var b = new SolidBrush(Theme.Card);
        g.FillPath(b, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
    }

    public static void Pill(Graphics g, string text, Point at, Color fg)
    {
        var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        var sz = TextRenderer.MeasureText(text, font);
        int w = sz.Width + 32, h = sz.Height + 14;       // bigger padding
        var r = new RectangleF(at.X, at.Y, w, h);
        // outlined badge like the ghostdeck.dev status chips: soft tint + 1px border in the badge colour
        using (var path = Theme.RoundRect(r, 4))
        {
            using (var b = new SolidBrush(Color.FromArgb(26, fg))) g.FillPath(b, path);
            using var pen = new Pen(Color.FromArgb(170, fg));
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, text, font, Rectangle.Round(r), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

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

// =====================================================================
//  Status
// =====================================================================
public sealed class StatusPage : ThemedPage
{
    private const int Pad = 28;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private static readonly (string key, Func<StatusInfo, HwSnapshot, string> val, bool mono)[] Rows =
    {
        ("st_model",     (s, h) => s.Device, false),
        ("st_firmware",  (s, h) => string.IsNullOrEmpty(h.Firmware) ? "—" : h.Firmware, true),
        ("st_charge",    (s, h) => h.ChargeLimit is >= 10 and <= 100 ? $"{h.ChargeLimit} %" : "—", false),
        ("st_switches",  (s, h) => s.Switches.ToString(), false),
        ("st_in_profile",(s, h) => FmtTs(s.InProfile), false),
        ("st_autostart", (s, h) => s.Autostart ? Lang.T("yes") : Lang.T("no"), false),
        ("st_app_ver",   (s, h) => s.AppVersion, false),
    };

    private static string BatteryText()
    {
        var ps = SystemInformation.PowerStatus;
        if (ps.BatteryLifePercent is >= 0f and <= 1f)
            return $"{(int)Math.Round(ps.BatteryLifePercent * 100)} %" + (ps.PowerLineStatus == PowerLineStatus.Online ? " ⚡" : "");
        return "—";
    }

    private readonly Button _test = new();
    private readonly Button _logBtn = new();
    private readonly DeviceProfile? _dev;
    private byte[] _live = Array.Empty<byte>();    // [shift, 0x34, 0xEB, fan]
    private (int[] ct, int[] cs, int[] gt, int[] gs)? _curve;
    private HwSnapshot _hw;                        // last EC/hw snapshot, refreshed off the UI thread
    private int _refreshing;                       // 1 while a background refresh is in flight

    private readonly Canvas _canvas;

    // Sub-tabs split the (heavy) Status page into three shorter views: charts, EC bytes, change log.
    private readonly SubTabs _statusTabs = new(Lang.T("st_sub_charts"), Lang.T("st_sub_bytes"), Lang.T("st_sub_log"));
    private int _statusSub;
    private const int SubY = 90;      // sub-tab bar Y (clear gap under the title)
    private const int SecTop = 168;   // content starts below the title + sub-tab bar (clear gap under it)

    public StatusPage(MainDeps d) : base(d)
    {
        _dev = Devices.Detect(d.Firmware);
        _canvas = new Canvas(this) { Location = new Point(0, 0) };
        Controls.Add(_canvas);

        _statusTabs.Location = new Point(Pad, SubY);
        _statusTabs.Changed += i => { _statusSub = i; _logBtn.Visible = i == 2; Relayout(); _canvas.Rebuild(); };
        _canvas.Controls.Add(_statusTabs);

        // "Full log…" button lives on the canvas so it scrolls with the recent-changes section.
        _logBtn.Text = Lang.T("log_full");
        _logBtn.AutoSize = false;
        _logBtn.Visible = false;   // only on the change-log sub-tab
        Ui.StyleGhost(_logBtn);
        _logBtn.Click += (_, _) => LogForm.ShowSingleton();
        _canvas.Controls.Add(_logBtn);

        _timer.Tick += (_, _) => RefreshAsync();
        VisibleChanged += (_, _) => { if (Visible) { Relayout(); _timer.Start(); } else _timer.Stop(); };
        ClientSizeChanged += (_, _) => Relayout();
        ChangeLog.Changed += OnLogChanged;

        // Test/discovery tools are now hidden; opened via Ctrl+Shift+T (see MainForm / docs/TECHNICAL.md §12).
        _test.Visible = false;
    }

    private void OnLogChanged() { if (!IsDisposed && Visible) { try { BeginInvoke(() => { Relayout(); _canvas.Rebuild(); }); } catch { } } }

    public override void LiveRefresh() { _canvas.Rebuild(); RefreshAsync(); }

    // EC/WMI reads take tens of ms each and the full snapshot is dozens of them; doing that
    // synchronously froze every tab switch (user-visible lag). Read on a worker, then rebuild
    // the canvas back on the UI thread. Get_Data is stateless (address in the payload), so a
    // background read can't interleave badly with UI-thread writes.
    private void RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        var dev = _dev;
        Task.Run(() =>
        {
            HwSnapshot hw = default;
            byte[]? live = null;
            (int[], int[], int[], int[])? curve = null;
            try { hw = D.Hw(); } catch { }
            if (dev != null)
            {
                try { live = Ec.ReadMany(new[] { dev.ShiftMode, (byte)0x34, (byte)0xEB, dev.FanMode }); } catch { }
                try { curve = Ec.ReadFanCurve(dev); } catch { }
            }
            try
            {
                BeginInvoke(() =>
                {
                    _hw = hw;
                    if (live != null) _live = live;
                    if (curve != null) _curve = curve;
                    _refreshing = 0;
                    if (Visible) _canvas.Rebuild();
                });
            }
            catch { _refreshing = 0; }   // page disposed mid-flight
        });
    }

    private void PlaceTest() { }

    private const int RingCount = 5;
    private const int RingTop = 92, RowH = 56;
    private static readonly Color CpuUseColor = Color.FromArgb(0x3C, 0x7D, 0xFF);   // blue: CPU side of the palette (fans = cyan/violet)
    private int RingGap() => 24;
    private int RingSize(int width)
    {
        int avail = width - Pad * 2, gap = RingGap();
        return Math.Max(150, Math.Min(240, (avail - gap * (RingCount - 1)) / RingCount));
    }

    private void Relayout()
    {
        if (_canvas == null) return;
        _statusTabs.Size = new Size(_statusTabs.PreferredWidth, _statusTabs.Height);
        _canvas.Width = ClientSize.Width;
        _canvas.Height = Math.Max(SectionHeight(_canvas.Width, _statusSub), ClientSize.Height);
    }

    private static int GridH(bool title, int rows, int headerLines = 1) =>
        (title ? GTitle.Height + 12 : 0) + (GHead.Height * headerLines + 14) + rows * (GCell.Height + 14) + 8;

    // Height of the active sub-tab's content (0 = charts, 1 = EC bytes, 2 = change log).
    private int SectionHeight(int width, int sub)
    {
        if (sub == 0)
        {
            int ring = RingSize(width);
            int cardTop = SecTop + ring + 68 + 54 + 14 + 54 + 40;
            return cardTop + RowH * Rows.Length + 14 + 40;
        }
        if (sub == 1)
        {
            int h = SecTop + GridH(true, 5, 2) + NoteH + 8 + GridH(false, 4);
            if (_dev?.FanCurve is { } fc) h += 16 + GridH(true, fc.Points);
            return h + 40;
        }
        return SecTop + 16 + GridH(true, RecentLogRows) + 40;
    }

    private const int RecentLogRows = 16;

    // Paint the cached snapshot immediately (instant tab switch) and refresh in the background.
    public override void OnEnter() { _logBtn.Text = Lang.T("log_full"); _logBtn.Visible = _statusSub == 2; Relayout(); _canvas.Rebuild(); RefreshAsync(); }
    public override void ApplyTheme() { base.ApplyTheme(); if (_canvas != null) { _canvas.BackColor = Theme.Surface; Ui.StyleGhost(_logBtn); _statusTabs.Invalidate(); _canvas.Rebuild(); } }
    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); ChangeLog.Changed -= OnLogChanged; } base.Dispose(disposing); }

    // The page is painted by an inner canvas sized to the full content height; WinForms scrolls that
    // child natively (no manual translate), which removes the ghosting the self-scrolled paint had.
    // Renders the (heavy) content once into a persistent BufferedGraphics allocated FROM this control's
    // own Graphics — so the offscreen DC is DPI-aware and TextRenderer draws exactly like on-screen
    // (correct size + crisp), unlike a plain Bitmap which renders at 96 DPI and blurs when scaled.
    // Scrolling then only BitBlts the buffer (fast), so it's smooth; a full re-render happens only when
    // data / size / theme change (Rebuild) — not per scroll frame.
    private sealed class Canvas : Control
    {
        private readonly StatusPage _p;
        private BufferedGraphics? _buf;
        private int _bufW, _bufH;

        public Canvas(StatusPage p)
        {
            _p = p;
            BackColor = Theme.Surface;
            ResizeRedraw = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }

        public void Rebuild() { RenderToBuffer(); Invalidate(); }

        private void RenderToBuffer()
        {
            if (Width <= 0 || Height <= 0) return;
            using var cg = CreateGraphics();   // DPI-aware DC of this control
            if (_buf == null || _bufW != Width || _bufH != Height)
            {
                _buf?.Dispose();
                var ctx = BufferedGraphicsManager.Current;
                ctx.MaximumBuffer = new Size(Width + 1, Height + 1);
                _buf = ctx.Allocate(cg, new Rectangle(0, 0, Width, Height));
                _bufW = Width; _bufH = Height;
            }
            var g = _buf!.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            if (_p.D.Settings.ShowGrid) Ui.DrawGrid(g, new Rectangle(0, 0, Width, Height));
            _p.Render(g, Width);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }   // buffer covers the whole surface

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_buf == null || _bufW != Width || _bufH != Height) RenderToBuffer();
            _buf?.Render(e.Graphics);
        }

        protected override void Dispose(bool disposing) { if (disposing) _buf?.Dispose(); base.Dispose(disposing); }
    }

    internal void Render(Graphics g, int width)
    {
        var info = D.Status();
        var hw = _hw;   // cached; refreshed off the UI thread by RefreshAsync

        TextRenderer.DrawText(g, Lang.T("menu_status"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);
        // (the tier badge lives in the header strip now, next to the version)

        int avail = width - Pad * 2;
        if (_statusSub == 1) { RenderBytes(g, avail, info); return; }
        if (_statusSub == 2) { RenderLog(g, avail); return; }

        // ---- sub-tab 0: charts (rings + RAM + metric boxes + details card) ----
        int ring = RingSize(width);
        // justify the gauge row: spread the leftover width into the gaps so the five rings
        // (and the metric boxes that follow their columns) span the full content width
        int ringGap = Math.Max(24, (avail - RingCount * ring) / (RingCount - 1));
        int top = SecTop;
        int cpuUse = SysInfo.CpuUsage();
        var (ramPct, ramTot, ramUsed) = SysInfo.Ram();
        int X(int i) => Pad + i * (ring + ringGap);
        DrawRing(g, X(0), top, ring, hw.CpuTemp, 100, "°C", Lang.T("st_cpu_temp"), TempColor(hw.CpuTemp), info.Known);
        DrawRing(g, X(1), top, ring, hw.GpuTemp, 100, "°C", Lang.T("st_gpu_temp"), TempColor(hw.GpuTemp), info.Known);
        DrawRing(g, X(2), top, ring, info.Known ? hw.CpuFan : 0, 100, "%", Lang.T("st_cpu_fan"), Theme.Accent, info.Known);
        DrawRing(g, X(3), top, ring, info.Known ? hw.GpuFan : 0, 100, "%", Lang.T("st_gpu_fan"), Theme.Violet, info.Known);
        DrawRing(g, X(4), top, ring, cpuUse, 100, "%", Lang.T("st_cpu_usage"), CpuUseColor, true, allowZero: true);

        // --- sub-row under the rings (clear gap above and below) ---
        int subY = top + ring + 68, subH = 54;

        // RAM as a horizontal bar spanning the two temperature rings, with values; bar inset ~20px each side
        int ramX = X(0), ramW = ring * 2 + ringGap;
        TextRenderer.DrawText(g, Lang.T("st_ram"), new Font("Segoe UI", 10.5f, FontStyle.Bold),
            new Rectangle(ramX, subY, ramW, 28), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, ramTot > 0 ? $"{ramUsed:0.0} / {ramTot:0.0} GB · {ramPct}%" : "—", new Font("Segoe UI", 10.5f),
            new Rectangle(ramX, subY, ramW, 28), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        DrawBar(g, new RectangleF(ramX + 20, subY + 36, ramW - 40, 14), ramPct / 100f, ramPct >= 90 ? Theme.Amber : Theme.Accent);

        // Framed metric counter (shared by the RPM / clock / GPU / VRAM / battery boxes).
        void MetricBox(RectangleF box, string text)
        {
            Ui.FillCard(g, box);
            TextRenderer.DrawText(g, text, new Font("Segoe UI", 11.5f, FontStyle.Bold),
                Rectangle.Round(box), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        void RpmUnder(int i, int rpm, string label)
            => MetricBox(new RectangleF(X(i) + 14, subY, ring - 28, subH),
                !info.Known ? $"{label}: —" : rpm > 0 ? $"{label}: {rpm} RPM" : $"{label}: — RPM");
        RpmUnder(2, hw.CpuRpm, "CPU");
        RpmUnder(3, hw.GpuRpm, "GPU");
        // CPU clock (approx.) under the CPU-usage ring — same row as the RPM counters
        MetricBox(new RectangleF(X(4) + 14, subY, ring - 28, subH),
            Perf.CpuClockMhz() is int mhz and > 0 ? $"CPU: {mhz} MHz" : "CPU: — MHz");

        // second sub-row: battery, GPU load %, VRAM — same box width as the RPM counters, under rings 2/3/4
        int subY2 = subY + subH + 14;
        int gu = Perf.GpuUsage(), vm = Perf.VramUsedMb(), vt = Perf.VramTotalMb();
        bool vramBar = vt > 0 && vm >= 0;   // only meaningful as a bar when we know the total (discussion #9)
        void Box2(int i, string text) => MetricBox(new RectangleF(X(i) + 14, subY2, ring - 28, subH), text);
        Box2(2, $"{Lang.T("st_battery")}: {BatteryText()}");
        Box2(3, $"{Lang.T("ov_m_gpuusage")}: " + (gu >= 0 ? $"{gu} %" : "—"));
        // VRAM: shown as a bar under RAM when the total is known; otherwise fall back to the MB box.
        if (vramBar)
        {
            int vpct = (int)Math.Round(Math.Clamp(vm / (float)vt, 0f, 1f) * 100);
            TextRenderer.DrawText(g, Lang.T("ov_m_vram"), new Font("Segoe UI", 10.5f, FontStyle.Bold),
                new Rectangle(ramX, subY2, ramW, 28), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, $"{vm / 1024f:0.0} / {vt / 1024f:0.0} GB · {vpct}%", new Font("Segoe UI", 10.5f),
                new Rectangle(ramX, subY2, ramW, 28), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            DrawBar(g, new RectangleF(ramX + 20, subY2 + 36, ramW - 40, 14), vm / (float)vt, vpct >= 90 ? Theme.Amber : Theme.AccentFill);
        }
        else
        {
            Box2(4, $"{Lang.T("ov_m_vram")}: " + (vm >= 0 ? $"{vm} MB" : "—"));
        }

        int cardTop = subY2 + subH + 40;
        int rowH = RowH;
        var card = new RectangleF(Pad, cardTop, avail, rowH * Rows.Length + 14);
        Ui.FillCard(g, card);
        int y = cardTop + 7;
        for (int i = 0; i < Rows.Length; i++)
        {
            var (key, val, mono) = Rows[i];
            // zebra stripe on odd rows (matches the tables on the EC-bytes / change-log sub-tabs)
            if (i % 2 == 1) { using var b = new SolidBrush(Theme.RowAlt); g.FillRectangle(b, Pad + 8, y, avail - 16, rowH); }
            TextRenderer.DrawText(g, Lang.T(key), new Font("Segoe UI", 10.5f),
                new Rectangle(Pad + 22, y, avail - 44, rowH), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            var font = mono ? new Font("Consolas", 11f, FontStyle.Bold) : new Font("Segoe UI", 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, val(info, hw), font,
                new Rectangle(Pad, y, avail - 22, rowH), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            y += rowH;
        }
    }

    // ---- sub-tab 1: profile-byte matrix + legend + live fan-curve tables ----
    private void RenderBytes(Graphics g, int avail, StatusInfo info)
    {
        int sec = SecTop;
        sec = DrawMatrix(g, sec, avail, info);
        TextRenderer.DrawText(g, Lang.T("st_matrix_note"), new Font("Segoe UI", 9f), new Rectangle(Pad, sec + 4, avail, NoteH - 4),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordEllipsis);
        sec += NoteH;
        sec = DrawLegend(g, sec, avail);
        if (_dev?.FanCurve != null && _curve is { } cv) DrawCurveLive(g, sec, avail, cv);
    }

    // ---- sub-tab 2: recent-changes log ----
    private void RenderLog(Graphics g, int avail) => DrawRecentLog(g, SecTop - 16, avail);

    private int DrawRecentLog(Graphics g, int top, int avail)
    {
        var headers = new[] { Lang.T("log_col_time"), Lang.T("log_col_source"), Lang.T("log_col_detail"), Lang.T("log_col_result") };
        var lefts = new[] { 0f, .17f, .35f, .74f };
        var rows = new List<string[]>();
        foreach (var e in ChangeLog.Recent(RecentLogRows))
            rows.Add(new[] { e.Time.ToString("MM-dd HH:mm:ss"), ChangeLog.SourceLabel(e.Source), e.Detail, e.Result });
        if (rows.Count == 0) rows.Add(new[] { "—", "", Lang.T("log_empty"), "" });
        bool[] mono = { true, false, false, true };
        int titleY = top + 16;
        // column colour language shared with the full-log window: muted time, accent source,
        // plain detail, muted mono readback
        Color? LogCell(int r, int c) => c switch { 0 => Theme.Muted, 1 => Theme.Accent, 3 => Theme.Muted, _ => Theme.Text };
        int bottom = DrawGrid(g, titleY, avail, Lang.T("log_recent"), headers, lefts, rows, mono, cellColor: LogCell);

        // Right-align the "Full log…" button on the section title row.
        int btnW = 150, btnH = GTitle.Height + 6;
        var b = new Rectangle(Pad + avail - btnW, titleY - 3, btnW, btnH);
        if (_logBtn.Bounds != b) _logBtn.Bounds = b;
        _logBtn.BringToFront();
        return bottom;
    }

    private static int NoteH => GCell.Height + 12;

    private static readonly Font GTitle = new("Segoe UI", 12f, FontStyle.Bold);
    private static readonly Font GHead = new("Segoe UI", 9.5f, FontStyle.Bold);
    private static readonly Font GCell = new("Segoe UI", 10.5f);
    private static readonly Font GMono = new("Consolas", 11f, FontStyle.Bold);

    // Generic table drawer; all row heights derive from font.Height (DPI-safe). Returns bottom Y.
    private int DrawGrid(Graphics g, int top, int avail, string title, string[] headers, float[] lefts,
        IReadOnlyList<string[]> rows, bool[] mono, Func<int, Color?>? rowTint = null,
        Func<int, int, Color?>? cellColor = null, int activeRow = -1, int headerLines = 1,
        Func<int, Color?>? rowBar = null, bool zebra = true)
    {
        int x = Pad;
        int y = top;
        if (!string.IsNullOrEmpty(title))
        {
            TextRenderer.DrawText(g, title, GTitle, new Rectangle(x, top, avail, GTitle.Height + 4), Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);
            y = top + GTitle.Height + 12;
        }
        int headH = GHead.Height * headerLines + 14, rowH = GCell.Height + 14;
        int totalH = headH + rows.Count * rowH + 8;
        Ui.FillCard(g, new RectangleF(x, y, avail, totalH));

        int n = lefts.Length;
        int[] cx = new int[n + 1];
        for (int i = 0; i < n; i++) cx[i] = x + 18 + (int)(lefts[i] * (avail - 36));
        cx[n] = x + avail - 14;
        int ColW(int c) => cx[c + 1] - cx[c] - 10;

        var hFlags = headerLines > 1
            ? TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
            : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        for (int c = 0; c < headers.Length; c++)
            TextRenderer.DrawText(g, headers[c], GHead, new Rectangle(cx[c], y + 6, ColW(c), headH - 8), Theme.Muted, hFlags);

        int ry = y + headH;
        for (int r = 0; r < rows.Count; r++)
        {
            // row background: explicit tint wins, else zebra stripe on odd rows
            var fill = rowTint?.Invoke(r) ?? (zebra && r % 2 == 1 ? Theme.RowAlt : (Color?)null);
            if (fill is Color bg) { using var b = new SolidBrush(bg); g.FillRectangle(b, x + 8, ry, avail - 16, rowH); }
            // left accent bar: per-row colour (e.g. profile), or the active-row marker
            var bar = r == activeRow ? Theme.Accent : rowBar?.Invoke(r);
            if (bar is Color bc) { using var b = new SolidBrush(bc); g.FillRectangle(b, x + 8, ry, 4, rowH); }
            for (int c = 0; c < rows[r].Length; c++)
                TextRenderer.DrawText(g, rows[r][c], mono[c] ? GMono : GCell, new Rectangle(cx[c], ry, ColW(c), rowH),
                    cellColor?.Invoke(r, c) ?? Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            ry += rowH;
        }
        return y + totalH;
    }

    private string RecipeVal(ProfileId p, byte addr)
    {
        if (_dev == null) return "—";
        foreach (var (a, v) in _dev.Recipes[p]) if (a == addr) return v.ToString("X2");
        return "—";
    }

    private int DrawMatrix(Graphics g, int top, int avail, StatusInfo info)
    {
        byte shiftA = _dev?.ShiftMode ?? 0xD2, fanA = _dev?.FanMode ?? 0xD4;
        var headers = new[]
        {
            "",
            $"{Lang.T("st_b_power")}\n(0x{shiftA:X2})",
            $"{Lang.T("st_b_cap")}\n(0x34)",
            $"{Lang.T("st_b_batt")}\n(0xEB)",
            $"{Lang.T("st_b_fan")}\n(0x{fanA:X2})",
        };
        var lefts = new[] { 0f, .34f, .51f, .67f, .83f };
        var order = Profiles.Order;

        // a word in parentheses next to the fan hex value
        string FanCell(string hex) => hex switch
        {
            "1D" => $"1D ({Lang.T("st_fan_silent")})",
            "0D" => $"0D ({Lang.T("st_fan_auto")})",
            "8D" => $"8D ({Lang.T("st_fan_curve")})",
            _ => hex,
        };

        var rows = new List<string[]>();
        foreach (var id in order)
            rows.Add(new[] { Profiles.Get(id).Label, RecipeVal(id, shiftA), RecipeVal(id, 0x34), RecipeVal(id, 0xEB), FanCell(RecipeVal(id, fanA)) });
        string LiveHex(int i) => _live.Length > i ? _live[i].ToString("X2") : "—";
        rows.Add(new[] { Lang.T("st_now"), LiveHex(0), LiveHex(1), LiveHex(2), FanCell(LiveHex(3)) });

        int active = Array.IndexOf(order, info.Profile);
        bool[] mono = { false, true, true, true, true };

        // gentle profile wash per row (a touch stronger on the active one), plus a solid left
        // accent bar in the profile colour; the "Now (live)" row falls back to zebra + cyan bar
        Color? Tint(int r) => r < order.Length
            ? Color.FromArgb(r == active ? 40 : 20, D.ColorOf(order[r]))
            : (Color?)null;
        Color? Bar(int r) => r < order.Length ? Theme.Profile(D.ColorOf(order[r])) : (Color?)null;
        Color? Cell(int r, int c)
        {
            if (r < order.Length) return c == 0 ? Theme.Profile(D.ColorOf(order[r])) : Theme.Text;
            if (c == 0) return Theme.Accent;                                   // "Now" label
            if (c == 4 && _live.Length > 3 && _live[3] == 0x8D) return Theme.Accent;   // custom curve active (fan byte)
            return Theme.Text;
        }
        return DrawGrid(g, top, avail, Lang.T("st_matrix"), headers, lefts, rows, mono, Tint, Cell, active, headerLines: 2, rowBar: Bar);
    }

    private int DrawLegend(Graphics g, int top, int avail)
    {
        byte shiftA = _dev?.ShiftMode ?? 0xD2, fanA = _dev?.FanMode ?? 0xD4;
        var headers = new[] { Lang.T("st_b_byte"), Lang.T("st_b_role"), Lang.T("st_b_vals") };
        var lefts = new[] { 0f, .22f, .60f };
        string C = Lang.T("st_v_comfort"), T = Lang.T("st_v_turbo"), E = Lang.T("st_v_eco");
        string On = Lang.T("st_on"), Off = Lang.T("st_off");
        string Si = Lang.T("st_fan_silent"), Au = Lang.T("st_fan_auto"), Cu = Lang.T("st_fan_curve");
        var rows = new List<string[]>
        {
            new[] { $"0x{shiftA:X2}", Lang.T("st_b_power"), $"C1 ({C}) · C4 ({T}) · C2 ({E})" },
            new[] { "0x34",          Lang.T("st_b_cap"),   $"00 (Extreme) · 01 ({Lang.T("st_b_others")})" },
            new[] { "0xEB",          Lang.T("st_b_batt"),  $"00 ({Off}) · 0F ({On})" },
            new[] { $"0x{fanA:X2}",  Lang.T("st_b_fan"),   $"1D ({Si}) · 0D ({Au}) · 8D ({Cu})" },
        };
        bool[] mono = { true, false, false };
        Color? LegendCell(int r, int c) => c switch { 0 => Theme.Accent, 2 => Theme.Muted, _ => Theme.Text };
        return DrawGrid(g, top + 8, avail, "", headers, lefts, rows, mono, cellColor: LegendCell);
    }

    private int DrawCurveLive(Graphics g, int top, int avail, (int[] ct, int[] cs, int[] gt, int[] gs) cv)
    {
        var headers = new[] { Lang.T("st_point"), "CPU °C", "CPU %", "GPU °C", "GPU %" };
        var lefts = new[] { 0f, .20f, .40f, .60f, .80f };
        int n = Math.Min(Math.Min(cv.ct.Length, cv.cs.Length), Math.Min(cv.gt.Length, cv.gs.Length));
        var rows = new List<string[]>();
        for (int i = 0; i < n; i++) rows.Add(new[] { (i + 1).ToString(), cv.ct[i] + "°", cv.cs[i] + "%", cv.gt[i] + "°", cv.gs[i] + "%" });
        bool[] mono = { false, true, true, true, true };
        // temps muted, duty coloured per fan (CPU accent / GPU violet), point number faint
        Color? CurveCell(int r, int c) => c switch { 0 => Theme.Faint, 2 => Theme.Accent, 4 => Theme.Violet, _ => Theme.Muted };
        return DrawGrid(g, top + 16, avail, Lang.T("st_curve_live"), headers, lefts, rows, mono, cellColor: CurveCell);
    }

    private static void DrawRing(Graphics g, int x, int y, int size, int value, int max, string unit, string label, Color color, bool known, string? sub = null, bool allowZero = false)
    {
        bool ok = known && (allowZero ? value >= 0 : value > 0) && value < 130;
        float frac = ok ? Math.Clamp(value / (float)max, 0, 1) : 0;
        IconPainter.Ring(g, new RectangleF(x, y, size, size), frac, color, ok ? value.ToString() : "—", unit, label, sub);
    }

    private static void DrawBar(Graphics g, RectangleF r, float frac, Color color)
    {
        frac = Math.Clamp(frac, 0, 1);
        float rad = r.Height / 2f;
        using (var p = Rounded(r, rad)) using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, p);
        if (frac > 0)
        {
            var fr = new RectangleF(r.X, r.Y, Math.Max(r.Height, r.Width * frac), r.Height);
            using var p = Rounded(fr, rad);
            using var b = new SolidBrush(color);
            g.FillPath(b, p);
        }
    }

    private static GraphicsPath Rounded(RectangleF r, float rad)
    {
        float d = rad * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Color TempColor(int t) =>
        t <= 0 ? Theme.Muted : t < 70 ? Theme.Green : t < 85 ? Theme.Amber : Theme.Red;

    private static string FmtTs(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} min";
        return $"{t.Seconds} s";
    }
}

// =====================================================================
//  Updates
// =====================================================================
public sealed class UpdatesPage : ThemedPage
{
    private readonly Button _check = new();
    private readonly Button _install = new();
    private readonly Label _status = new();
    private readonly Label _lastChecked = new();
    private readonly ThinBar _bar = new();     // download progress, next to the status text
    private readonly FlowLayoutPanel _history = new();
    private bool _loaded;
    private Updater.Result? _avail;   // newer release found by the last check

    /// <summary>Rounded progress bar (0..1), styled like the report wizard's capture bar.</summary>
    private sealed class ThinBar : Control
    {
        private float _value;
        public float Value { get => _value; set { _value = Math.Clamp(value, 0, 1); Invalidate(); } }
        public ThinBar() { DoubleBuffered = true; Size = new Size(320, 12); Visible = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(track, Height / 2f))
            {
                using var b = new SolidBrush(Theme.Card); g.FillPath(b, p);
                using var pen = new Pen(Theme.Border); g.DrawPath(pen, p);
            }
            if (_value > 0)
            {
                float w = Math.Max(Height, (Width - 1) * _value);
                var fr = new RectangleF(0, 0, w, Height - 1);
                using var p = Theme.RoundRect(fr, Height / 2f);
                using var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height),
                    ControlPaint.Light(Theme.Accent, 0.2f), Theme.Accent, 0f);
                g.FillPath(b, p);
            }
        }
    }

    public UpdatesPage(MainDeps d) : base(d)
    {
        _check.Text = Lang.T("upd_check_now");
        Ui.StylePrimary(_check);
        _check.Width = 150;
        _check.Click += async (_, _) => await CheckNow();

        Ui.StylePrimary(_install);
        _install.Width = 220;
        _install.Visible = false;
        _install.Click += async (_, _) => await InstallNow();
        Controls.Add(_install);
        Controls.Add(_bar);

        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI", 10.5f);
        _lastChecked.Font = new Font("Segoe UI", 9.5f);
        _lastChecked.AutoSize = true;

        _history.FlowDirection = FlowDirection.TopDown;
        _history.WrapContents = false;
        _history.AutoScroll = true;
        _history.ClientSizeChanged += (_, _) => SetRowWidths();

        Controls.Add(_check);
        Controls.Add(_status);
        Controls.Add(_lastChecked);
        Controls.Add(_history);
        Resize += (_, _) => LayoutBits();
    }

    public override async void OnEnter()
    {
        LayoutBits();
        ApplyThemeText();
        if (!_loaded) { _loaded = true; await LoadHistory(); }
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        Ui.StylePrimary(_check);
        Ui.StylePrimary(_install);
        ApplyThemeText();
        foreach (Control c in _history.Controls) c.Invalidate();
    }

    private void ApplyThemeText()
    {
        _lastChecked.ForeColor = Theme.Muted;
        if (_status.ForeColor != Theme.Green && _status.ForeColor != Theme.Accent)
            _status.ForeColor = Theme.Text;
        _status.BackColor = _lastChecked.BackColor = Theme.Surface;
        var d = D.Settings.LastUpdateCheckUtc;
        _lastChecked.Text = string.Format(Lang.T("upd_last_checked"),
            d == DateTime.MinValue ? Lang.T("upd_never") : d.ToLocalTime().ToString("g"));
        _lastChecked.Location = new Point(ClientSize.Width - 28 - _lastChecked.PreferredWidth, _check.Bottom + 12);
    }

    // y positions derived from real font metrics (DPI-safe)
    private int InstalledY => 24 + new Font("Segoe UI", 18f, FontStyle.Bold).Height + 16;
    private int VersionY => InstalledY + new Font("Segoe UI", 10f).Height + 4;
    private int HistoryLabelY => VersionY + new Font("Segoe UI", 16f, FontStyle.Bold).Height + 26;
    private int HistoryTop => HistoryLabelY + new Font("Segoe UI", 10f, FontStyle.Bold).Height + 12;

    private void LayoutBits()
    {
        int w = ClientSize.Width - 56;
        _check.Location = new Point(ClientSize.Width - 28 - _check.Width, 66);
        _install.Location = new Point(_check.Left - _install.Width - 10, 66);
        _lastChecked.Location = new Point(ClientSize.Width - 28 - _lastChecked.PreferredWidth, _check.Bottom + 12);
        if (_bar.Visible)
        {
            // download mode: buttons hidden, "Downloading… X%" label stacked ABOVE the bar,
            // right-aligned in the button area (like the report wizard's capture bar)
            int bx = ClientSize.Width - 28 - _bar.Width;
            _status.Location = new Point(bx, 66);
            _bar.Location = new Point(bx, _status.Bottom + 8);
        }
        else
        {
            // idle: status ("new version…" / "up to date") sits to the LEFT of the buttons
            int rowLeft = _install.Visible ? _install.Left : _check.Left;
            _status.Location = new Point(rowLeft - _status.PreferredWidth - 14, 66 + (_check.Height - _status.PreferredHeight) / 2);
        }
        _history.SetBounds(28, HistoryTop, w, Math.Max(120, ClientSize.Height - HistoryTop - 24));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, Lang.T("tab_updates"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(28, 24), Theme.Text);
        TextRenderer.DrawText(g, Lang.T("upd_installed"), new Font("Segoe UI", 10f), new Point(28, InstalledY), Theme.Muted);
        TextRenderer.DrawText(g, "v" + D.AppVersion(), new Font("Segoe UI", 16f, FontStyle.Bold), new Point(28, VersionY), Theme.Text);
        TextRenderer.DrawText(g, Lang.T("upd_history"), new Font("Segoe UI", 10f, FontStyle.Bold), new Point(28, HistoryLabelY), Theme.Muted);
    }

    private async Task CheckNow()
    {
        _check.Enabled = false;
        _status.ForeColor = Theme.Accent;
        _status.Text = Lang.T("upd_checking");
        LayoutBits();
        Version cur = Version.TryParse(D.AppVersion(), out var v) ? v : new Version(0, 0, 0);
        var res = await Updater.CheckAsync(cur);
        D.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
        D.SaveSettings();
        ApplyThemeText();
        if (res is { } r)
        {
            // in-app install instead of bouncing the user to the browser (discussion #9)
            _avail = r;
            _status.ForeColor = Theme.Accent;
            _status.Text = string.Format(Lang.T("upd_available"), r.Version);
            _install.Text = string.Format(Lang.T("upd_install"), r.Tag);
            _install.Visible = true;
            LayoutBits();
        }
        else
        {
            _avail = null;
            _install.Visible = false;
            _status.ForeColor = Theme.Green;
            _status.Text = "✓  " + Lang.T("upd_latest_ok");
            LayoutBits();
        }
        _check.Enabled = true;
        D.CheckNoticesNow();     // manual check also refreshes announcements (banner + tray balloon)
        await LoadHistory();
    }

    private async Task InstallNow()
    {
        if (_avail is not { } r) return;
        // download mode: hide the buttons, show the stacked label + progress bar
        _install.Visible = _check.Visible = false;
        _status.ForeColor = Theme.Accent;
        _bar.Visible = true;
        _bar.Value = 0;
        _status.Text = string.Format(Lang.T("upd_downloading"), 0);
        LayoutBits();
        var progress = new Progress<int>(p =>
        {
            _bar.Value = p / 100f;
            _status.Text = string.Format(Lang.T("upd_downloading"), p);
            LayoutBits();
        });
        string? path = await Updater.DownloadAsync(r, progress);
        if (path == null || !Updater.StartSelfUpdate(path))
        {
            // no exe asset / download or launch failed - fall back to the release page
            _bar.Visible = false;
            _install.Visible = _check.Visible = true;
            _status.ForeColor = Theme.Red;
            _status.Text = Lang.T("upd_dl_failed");
            LayoutBits();
            try { Process.Start(new ProcessStartInfo(r.Url) { UseShellExecute = true }); } catch { }
            return;
        }
        _status.Text = Lang.T("upd_restarting");
        LayoutBits();
        Application.Exit();   // the hidden updater script waits for this process, swaps the exe and relaunches
    }

    private async Task LoadHistory()
    {
        var list = await Updater.RecentAsync(5);
        _history.Controls.Clear();
        if (list.Count == 0)
        {
            _history.Controls.Add(new Label { Text = Lang.T("upd_offline"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 8, 0, 0) });
            return;
        }
        int rw = RowWidth();
        foreach (var rel in list)
            _history.Controls.Add(new ReleaseRow(rel, rw));
    }

    // base on the control's full width minus the vertical scrollbar, so a horizontal
    // scrollbar never appears whether or not the vertical one is shown.
    private int RowWidth() => Math.Max(200, _history.Width - SystemInformation.VerticalScrollBarWidth - 6);

    private void SetRowWidths()
    {
        int w = RowWidth();
        foreach (Control c in _history.Controls) if (c is ReleaseRow) c.Width = w;
    }

    private sealed class ReleaseRow : Control
    {
        private readonly Updater.ReleaseInfo _r;
        public ReleaseRow(Updater.ReleaseInfo r, int width)
        {
            _r = r; DoubleBuffered = true; ResizeRedraw = true; Width = width; Margin = new Padding(0, 0, 0, 12);
            var titleF = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            var bodyF = new Font("Segoe UI", 9.5f);
            Height = 16 + titleF.Height + 8 + bodyF.Height * 2 + 16;   // title + two body lines
            Cursor = Cursors.Hand;
            Click += (_, _) => { try { Process.Start(new ProcessStartInfo(_r.Url) { UseShellExecute = true }); } catch { } };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            Ui.FillCard(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1));
            var titleF = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            var dateF = new Font("Segoe UI", 9.5f);
            var bodyF = new Font("Segoe UI", 9.5f);
            var linkF = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            string title = string.IsNullOrEmpty(_r.Tag) ? _r.Name : _r.Tag;
            int titleY = 16;
            TextRenderer.DrawText(g, title, titleF, new Rectangle(18, titleY, Width / 2, titleF.Height), Theme.Text, TextFormatFlags.Left);

            // top-right: "details ↗" link, with the date to its left (each measured -> no overlap)
            string link = Lang.T("upd_details") + " ↗";
            int linkW = TextRenderer.MeasureText(link, linkF).Width;
            TextRenderer.DrawText(g, link, linkF, new Rectangle(Width - 18 - linkW, titleY, linkW + 2, titleF.Height), Theme.Accent, TextFormatFlags.Left);
            string date = _r.Published?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
            int dateW = TextRenderer.MeasureText(date, dateF).Width;
            TextRenderer.DrawText(g, date, dateF, new Rectangle(Width - 18 - linkW - 16 - dateW, titleY + 2, dateW + 4, dateF.Height), Theme.Muted, TextFormatFlags.Left);

            // body: up to two changelog lines, with **bold** rendered and links removed
            int by = titleY + titleF.Height + 8;
            var bodyB = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            DrawRich(g, ParseRuns(CleanBody(_r.Body)), new Rectangle(18, by, Width - 36, bodyF.Height * 2 + 2),
                bodyF, bodyB, Theme.Muted, 2);
        }

        private static readonly Regex MdLink = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);

        // join up to 2 content lines; drop headers, section words, "Full Changelog", and md links
        private static string CleanBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            var lines = new List<string>();
            foreach (var raw in body.Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                if (t.StartsWith("Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase)) continue;
                bool bullet = t.StartsWith("-") || t.StartsWith("*");
                var l = t.TrimStart('-', '*', ' ').Trim();
                l = MdLink.Replace(l, "$1");                 // [text](url) -> text
                if (l.Length == 0) continue;
                if (!bullet && IsSection(l)) continue;
                lines.Add(l);
                if (lines.Count >= 2) break;
            }
            return string.Join("   ·   ", lines);
        }

        private static bool IsSection(string s) => s.TrimEnd(':') is
            "Added" or "Fixed" or "Changed" or "Removed" or "Deprecated" or "Security";

        // split on ** markers into (text, bold) runs
        private static List<(string text, bool bold)> ParseRuns(string s)
        {
            var runs = new List<(string, bool)>();
            var sb = new StringBuilder(); bool bold = false;
            for (int i = 0; i < s.Length;)
            {
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
                { if (sb.Length > 0) { runs.Add((sb.ToString(), bold)); sb.Clear(); } bold = !bold; i += 2; }
                else { sb.Append(s[i]); i++; }
            }
            if (sb.Length > 0) runs.Add((sb.ToString(), bold));
            return runs;
        }

        // word-wrap runs across up to maxLines, switching font for bold words
        private static void DrawRich(Graphics g, List<(string text, bool bold)> runs, Rectangle rect,
                                     Font reg, Font bold, Color color, int maxLines)
        {
            const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int spaceW = TextRenderer.MeasureText(g, " ", reg, Size.Empty, F).Width;
            int lineH = reg.Height;
            int x = rect.Left, y = rect.Top, line = 1;
            foreach (var (text, b) in runs)
            {
                var f = b ? bold : reg;
                foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int ww = TextRenderer.MeasureText(g, w, f, Size.Empty, F).Width;
                    if (x > rect.Left && x + ww > rect.Right)
                    {
                        if (line >= maxLines) { TextRenderer.DrawText(g, "…", reg, new Point(x, y), color, F); return; }
                        line++; x = rect.Left; y += lineH;
                    }
                    TextRenderer.DrawText(g, w, f, new Point(x, y), color, F);
                    x += ww + spaceW;
                }
            }
        }
    }
}

// =====================================================================
//  Settings (grouped cards; real controls = DPI-safe text)
// =====================================================================
public sealed class SettingsPage : ThemedPage
{
    private static readonly (string key, string label)[] Acts =
    {
        ("Silent", "Silent"), ("Balanced", "Balanced"),
        ("Extreme", "Extreme"), ("SuperBattery", "Super Battery"), ("Cycle", "Cycle"),
        ("CoolerBoost", "Fan Boost"), ("Overlay", "Gaming overlay"), ("OverlayLock", "Lock overlay"),
    };
    private static readonly int[] ChargeVals = { 0, 60, 80, 100 };
    private const int Pad = 28, Gutter = 24, TitleTop = 22;

    private readonly List<CardSection> _left = new();
    private readonly List<CardSection> _right = new();
    private readonly Dictionary<string, HotkeyBox> _boxes = new();
    private readonly Dictionary<string, ToggleSwitch> _hkToggles = new();
    private ToggleSwitch? _hkMaster;
    private readonly Dictionary<string, List<Panel>> _swatches = new();
    private OverlaySettingsPanel? _overlayPanel;
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 18f, FontStyle.Bold) };

    public SettingsPage(MainDeps d) : base(d)
    {
        _title.Location = new Point(Pad, TitleTop);
        Controls.Add(_title);
        BuildForm();
        Resize += (_, _) => Layout2();
    }

    public override void OnEnter() { _overlayPanel?.SyncFromSettings(); Layout2(); Invalidate(); }
    // Sync overlay toggles from settings (they can change via the Scenarios brick / tray / hotkey);
    // no re-layout here, which would reset the scroll position mid-edit.
    public override void LiveRefresh() { _overlayPanel?.SyncFromSettings(); }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _title.ForeColor = Theme.Text;
        _title.BackColor = Theme.Surface;
        foreach (var c in _left.Concat(_right)) c.ApplyTheme();
        _overlayPanel?.ApplyThemeColors();
        Invalidate();
    }

    // No custom OnPaint: the title is a child Label and everything else is a child control, so the page
    // scrolls natively (smooth, no title/blit mismatch). The base clears to Theme.Surface.

    private void Layout2()
    {
        if (_left.Count == 0) return;
        // Manual layout inside an AutoScroll panel: WinForms physically shifts children by the scroll
        // delta, so child Location must be expressed in *content* coordinates offset by AutoScrollPosition
        // (which is <= 0). Positioning at absolute content coords while the page is scrolled desynced the
        // scrollbar from the children — resizing the window width (which changes the overlay panel height,
        // as its checkboxes rewrap) then scrolling back up left a large empty gap. See docs/RENDERING.md.
        int ox = AutoScrollPosition.X, oy = AutoScrollPosition.Y;
        _title.Text = Lang.T("menu_settings");
        _title.ForeColor = Theme.Text;
        _title.BackColor = Theme.Surface;
        _title.Location = new Point(Pad + ox, TitleTop + oy);
        int colW = Math.Max(320, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        int fullW = colW * 2 + Gutter;
        int top = TitleTop + _title.Height + 18;   // content coords throughout; offset applied at Location

        if (_overlayPanel != null)
        {
            _overlayPanel.Relayout(fullW);
            _overlayPanel.Location = new Point(Pad + ox, top + oy);
            top += _overlayPanel.Height + 18;
        }

        int yL = top, yR = top;
        foreach (var c in _left) { c.Relayout(colW); c.Location = new Point(Pad + ox, yL + oy); yL += c.Height + 16; }
        foreach (var c in _right) { c.Relayout(colW); c.Location = new Point(Pad + colW + Gutter + ox, yR + oy); yR += c.Height + 16; }
        // Setting AutoScrollMinSize after positioning lets WinForms clamp AutoScrollPosition to the new
        // content extent and shift the children by that same delta, keeping everything consistent.
        AutoScrollMinSize = new Size(Pad * 2 + fullW, Math.Max(yL, yR) + 20);
    }

    // ---------------- build ----------------
    private void BuildForm()
    {
        foreach (var c in _left.Concat(_right)) Controls.Remove(c);
        if (_overlayPanel != null) { Controls.Remove(_overlayPanel); _overlayPanel.Dispose(); _overlayPanel = null; }
        _left.Clear(); _right.Clear(); _boxes.Clear(); _swatches.Clear();

        // ---- left column ----
        var look = new CardSection(Lang.T("set_grp_look"), "");
        var theme = new SegControl(new[] { Lang.T("set_theme_light"), Lang.T("set_theme_dark") }, Theme.Dark ? 1 : 0) { Size = new Size(220, 34) };
        theme.SelectedChanged += i => { Theme.Set(i == 1); D.Settings.DarkMode = Theme.Dark; D.SaveSettings(); };
        look.AddRow(Lang.T("set_theme"), theme);
        var lang = Combo(Lang.Names, Math.Max(0, Array.IndexOf(Lang.Codes, D.Settings.Language)));
        lang.SelectedIndexChanged += (_, _) =>
        {
            D.Settings.Language = Lang.Codes[Math.Max(0, lang.SelectedIndex)];
            Lang.Set(D.Settings.Language); D.SaveSettings(); D.SettingsChanged();
            // batch the full rebuild into one repaint - it used to blank the page for a second
            Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
        };
        look.AddRow(Lang.T("set_language"), lang);
        foreach (var id in Profiles.Order) look.AddRow(Profiles.Get(id).Label, BuildSwatches(id));
        var resetColors = new Button { Text = Lang.T("set_colors_reset"), AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        Ui.StyleGhost(resetColors);
        resetColors.Click += (_, _) =>
        {
            foreach (var pid in Profiles.Order) D.Settings.Colors.Remove(Profiles.Get(pid).Key);
            D.SaveSettings(); D.SettingsChanged();
            foreach (var lst in _swatches.Values) foreach (var p in lst) p.Invalidate();
        };
        look.AddRow("", resetColors);
        _left.Add(look);

        var start = new CardSection(Lang.T("set_grp_start"), "");
        start.AddRow(Lang.T("set_autostart"), Toggle(D.Settings.Autostart, v => { D.Settings.Autostart = v; try { Autostart.Set(v); } catch { } D.SaveSettings(); }));
        start.AddRow(Lang.T("experimental_enable"), Toggle(D.Settings.ExperimentalEnabled, v => { D.Settings.ExperimentalEnabled = v; D.SaveSettings(); D.SettingsChanged(); }));
        _left.Add(start);

        // ---- right column ----
        var power = new CardSection(Lang.T("set_grp_power"), "");
        var charge = new SegControl(new[] { Lang.T("gen_off_short"), "60%", "80%", "100%" }, Math.Max(0, Array.IndexOf(ChargeVals, D.Settings.ChargeLimit))) { Size = new Size(280, 34) };
        charge.SelectedChanged += i => D.SetChargeLimit(ChargeVals[i]);
        power.AddRow(Lang.T("set_charge"), charge);
        power.AddRow(Lang.T("set_autoswitch"), Toggle(D.Settings.AutoSwitchEnabled, v => D.SetAutoSwitch(v)));
        var ac = Combo(Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray(), ProfileIndex(D.Settings.ProfileOnAC));
        ac.SelectedIndexChanged += (_, _) => { D.Settings.ProfileOnAC = Profiles.Get(Profiles.Order[ac.SelectedIndex]).Key; D.SaveSettings(); };
        power.AddRow(Lang.T("on_ac"), ac);
        var bat = Combo(Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray(), ProfileIndex(D.Settings.ProfileOnBattery));
        bat.SelectedIndexChanged += (_, _) => { D.Settings.ProfileOnBattery = Profiles.Get(Profiles.Order[bat.SelectedIndex]).Key; D.SaveSettings(); };
        power.AddRow(Lang.T("on_battery"), bat);
        _right.Add(power);

        var upd = new CardSection(Lang.T("set_grp_updates"), "");
        upd.AddRow(Lang.T("set_check_updates"), Toggle(D.Settings.UpdateCheckEnabled, v => { D.Settings.UpdateCheckEnabled = v; D.SaveSettings(); }));
        _left.Add(upd);   // Updates -> bottom of LEFT column, aligns with Power+Shortcuts on right (#9)

        // Tray context-menu visibility toggles (discussion #9); all default on.
        var tray = new CardSection(Lang.T("set_grp_tray"), "");
        tray.AddRow(Lang.T("menu_status"), Toggle(D.Settings.TrayShowStatus, v => { D.Settings.TrayShowStatus = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("fc_title"), Toggle(D.Settings.TrayShowFanCurve, v => { D.Settings.TrayShowFanCurve = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("tab_models"), Toggle(D.Settings.TrayShowModels, v => { D.Settings.TrayShowModels = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("tray_report"), Toggle(D.Settings.TrayShowReport, v => { D.Settings.TrayShowReport = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("menu_log"), Toggle(D.Settings.TrayShowChangeLog, v => { D.Settings.TrayShowChangeLog = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("menu_feedback"), Toggle(D.Settings.TrayShowFeedback, v => { D.Settings.TrayShowFeedback = v; D.SaveSettings(); D.SettingsChanged(); }));
        _left.Add(tray);

        // Interface: background grid on/off + which main tabs collapse to icon buttons on the
        // right of the strip (e.g. keep Models reachable but out of the tab row).
        var uiSec = new CardSection(Lang.T("set_grp_ui"), "");
        uiSec.AddRow(Lang.T("set_grid"), Toggle(D.Settings.ShowGrid, v =>
        {
            D.Settings.ShowGrid = v;
            D.SaveSettings(); D.SettingsChanged();
            Invalidate(true);
        }));
        foreach (var (id, nameKey) in new[]
        {
            ("Scenarios", "tab_scenarios"), ("Status", "menu_status"), ("FanCurve", "tab_fancurve"),
            ("Settings", "menu_settings"), ("Models", "tab_models"),
        })
        {
            string tid = id;
            uiSec.AddRow(string.Format(Lang.T("set_tab_as_icon"), Lang.T(nameKey)),
                Toggle(D.Settings.IconTabs.Contains(tid), v =>
                {
                    if (v) { if (!D.Settings.IconTabs.Contains(tid)) D.Settings.IconTabs.Add(tid); }
                    else D.Settings.IconTabs.Remove(tid);
                    D.SaveSettings(); D.SettingsChanged();
                }));
        }
        _left.Add(uiSec);

        var hk = new CardSection(Lang.T("set_hotkeys"), "");
        _hkToggles.Clear();
        _hkMaster = new ToggleSwitch { Checked = D.Settings.HotkeysEnabled };
        _hkMaster.Toggled += v => { D.Settings.HotkeysEnabled = v; UpdateHotkeyRowsEnabled(); D.SaveSettings(); D.SettingsChanged(); };
        hk.AddRow(Lang.T("hk_all"), _hkMaster);   // master on/off (#9), default on
        foreach (var (key, label) in Acts)
        {
            var box = new HotkeyBox { Width = 200, AutoSize = false, Height = 28 };   // fixed height so the row panel doesn't clip it
            box.SetValue(D.Settings.Hotkeys.TryGetValue(key, out var hd) ? hd : new HotkeyDef());
            string k = key;
            var tg = new ToggleSwitch { Checked = D.Settings.Hotkeys.TryGetValue(k, out var hd2) ? hd2.Enabled : true };
            tg.Toggled += v =>
            {
                if (!D.Settings.Hotkeys.TryGetValue(k, out var cur)) { cur = new HotkeyDef(); D.Settings.Hotkeys[k] = cur; }
                cur.Enabled = v; box.Value.Enabled = v;
                D.SaveSettings(); D.SettingsChanged();
            };
            _hkToggles[key] = tg;
            box.Leave += (_, _) => { var def = box.Value.Clone(); def.Enabled = tg.Checked; D.Settings.Hotkeys[k] = def; D.SaveSettings(); D.SettingsChanged(); };
            _boxes[key] = box;
            var row = new Panel { Width = tg.Width + 12 + box.Width, Height = Math.Max(tg.Height, box.Height) + 4 };
            tg.Location = new Point(0, (row.Height - tg.Height) / 2);
            box.Location = new Point(tg.Width + 12, (row.Height - box.Height) / 2);
            row.Controls.Add(tg); row.Controls.Add(box);
            hk.AddRow(key == "Cycle" ? Lang.T("cycle") : key == "CoolerBoost" ? Lang.T("cooler_boost") : key == "Overlay" ? Lang.T("overlay_title") : key == "OverlayLock" ? Lang.T("ov_lock_menu") : label, row);
        }
        var reset = new Button { Text = Lang.T("set_default"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(reset);
        reset.Click += (_, _) => ResetHotkeys();
        hk.AddRow(null, reset);
        _right.Add(hk);
        UpdateHotkeyRowsEnabled();

        // Application icon: four visual tiles under Keyboard shortcuts; clicking one applies it
        // immediately to the window/taskbar/tray (#9).
        var iconCard = new CardSection(Lang.T("set_app_icon"), "");
        iconCard.AddRow(null, new IconStylePicker(D));
        _right.Add(iconCard);

        _overlayPanel = new OverlaySettingsPanel(D);
        Controls.Add(_overlayPanel);

        foreach (var c in _left.Concat(_right)) Controls.Add(c);
        Layout2(); ApplyTheme();
    }

    private void ResetHotkeys()
    {
        var def = new AppSettings(); def.EnsureDefaults();
        foreach (var (key, box) in _boxes)
        {
            box.SetValue(def.Hotkeys[key]);
            D.Settings.Hotkeys[key] = def.Hotkeys[key].Clone();
            if (_hkToggles.TryGetValue(key, out var tg)) tg.Checked = true;   // defaults = all enabled
        }
        D.Settings.HotkeysEnabled = true;
        if (_hkMaster != null) _hkMaster.Checked = true;
        UpdateHotkeyRowsEnabled();
        D.SaveSettings(); D.SettingsChanged();
    }

    // Grey out and disable the per-shortcut toggles + capture boxes when the master switch is off.
    private void UpdateHotkeyRowsEnabled()
    {
        bool on = _hkMaster?.Checked ?? true;
        foreach (var tg in _hkToggles.Values) tg.Enabled = on;
        foreach (var box in _boxes.Values) box.Enabled = on;
    }

    private int ProfileIndex(string key)
    {
        for (int i = 0; i < Profiles.Order.Length; i++)
            if (Profiles.Get(Profiles.Order[i]).Key == key) return i;
        return 1;
    }

    private FlowLayoutPanel BuildSwatches(ProfileId id)
    {
        string key = Profiles.Get(id).Key;
        // Single row of swatches (discussion #9): no wrap, no width cap.
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
        var list = new List<Panel>(); _swatches[key] = list;
        foreach (var hex in Profiles.Palette)
        {
            var sw = new Panel { Size = new Size(24, 22), BackColor = ColorTranslator.FromHtml(hex), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 4, 0), Tag = hex };
            string ph = hex;
            sw.Paint += (s, e) =>
            {
                // compare against the live effective colour so a defaults-reset moves the marker too
                if (string.Equals(ColorTranslator.ToHtml(D.Settings.ColorFor(id)), ph, StringComparison.OrdinalIgnoreCase))
                {
                    using var p1 = new Pen(Color.White, 2); e.Graphics.DrawRectangle(p1, 2, 2, sw.Width - 5, sw.Height - 5);
                    using var p2 = new Pen(Color.FromArgb(80, 0, 0, 0), 1); e.Graphics.DrawRectangle(p2, 0, 0, sw.Width - 1, sw.Height - 1);
                }
            };
            sw.Click += (s, e) => { D.Settings.Colors[key] = ph; D.SaveSettings(); D.SettingsChanged(); foreach (var p in list) p.Invalidate(); };
            flow.Controls.Add(sw); list.Add(sw);
        }
        return flow;
    }

    private ComboBox Combo(string[] items, int sel)
    {
        var c = new ThemedComboBox { Width = 220 };
        c.Items.AddRange(items);
        c.SelectedIndex = Math.Clamp(sel, 0, items.Length - 1);
        return c;
    }

    /// <summary>
    /// Four clickable icon-style tiles (preview + label); the selected one gets an accent frame.
    /// Clicking applies the style immediately (window/taskbar/tray) via SettingsChanged.
    /// </summary>
    private sealed class IconStylePicker : Control
    {
        private static readonly string[] LabelKeys = { "icon_logo", "icon_ghost_dark", "icon_ghost_light", "icon_gauge", "icon_ghost_cyan" };
        private readonly MainDeps D;
        private readonly int _cellW, _gap, _icon;
        private int _hover = -1;

        public IconStylePicker(MainDeps d)
        {
            D = d;
            DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand;
            float k = DeviceDpi / 96f;
            _cellW = (int)(88 * k); _gap = (int)(8 * k); _icon = (int)(44 * k);
            Width = LabelKeys.Length * _cellW + (LabelKeys.Length - 1) * _gap;
            Height = (int)(104 * k);
        }

        private Rectangle Cell(int i) => new(i * (_cellW + _gap), 0, _cellW, Height);
        private int HitTest(Point p) { for (int i = 0; i < LabelKeys.Length; i++) if (Cell(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != _hover) { _hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (i < 0 || i == D.Settings.IconStyle) return;
            D.Settings.IconStyle = i;
            TrayIconFactory.Style = i;
            D.SaveSettings(); D.SettingsChanged();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Card);
            using var lf = new Font("Segoe UI", 8.5f);
            for (int i = 0; i < LabelKeys.Length; i++)
            {
                var c = Cell(i);
                bool sel = D.Settings.IconStyle == i;
                using (var path = Theme.RoundRect(new RectangleF(c.X + 1f, c.Y + 1f, c.Width - 2, c.Height - 2), 8))
                {
                    if (sel) { using var b = new SolidBrush(Theme.AccentSoft); g.FillPath(b, path); }
                    using var pen = new Pen(sel ? Theme.Accent : _hover == i ? Theme.BorderStrong : Theme.Border, sel ? 2f : 1f);
                    g.DrawPath(pen, path);
                }
                int iy = (int)(12 * DeviceDpi / 96f);
                TrayIconFactory.DrawStylePreview(g, i, c.X + (c.Width - _icon) / 2f, c.Y + iy, _icon);
                TextRenderer.DrawText(g, Lang.T(LabelKeys[i]), lf,
                    new Rectangle(c.X + 4, c.Y + iy + _icon + 6, c.Width - 8, c.Height - iy - _icon - 10),
                    sel ? Theme.Text : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }
        }
    }

    private ToggleSwitch Toggle(bool on, Action<bool> onChange)
    {
        var t = new ToggleSwitch { Checked = on };
        t.Toggled += v => onChange(v);
        return t;
    }

    // ---------------- card ----------------
    private sealed class CardSection : Panel
    {
        private readonly Label _head;
        private readonly string _glyph;
        private readonly List<(Label? label, Control ctl)> _rows = new();

        public CardSection(string title, string glyph = "")
        {
            DoubleBuffered = true;
            BackColor = Theme.Card;
            _glyph = glyph;
            _head = new Label { Text = title.ToUpperInvariant(), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            Controls.Add(_head);
        }

        public void AddRow(string? label, Control ctl)
        {
            Label? l = null;
            if (label != null) { l = new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 10.5f) }; Controls.Add(l); }
            Controls.Add(ctl);
            _rows.Add((l, ctl));
        }

        public void Relayout(int width)
        {
            Width = width;
            const int pad = 18;
            int y = 16;
            int hx = pad + (string.IsNullOrEmpty(_glyph) ? 0 : Ceil(26 * DeviceDpi / 96f) + Ceil(10 * DeviceDpi / 96f));
            _head.Location = new Point(hx, y + Ceil(4 * DeviceDpi / 96f));
            y += Math.Max(_head.Height, Ceil(26 * DeviceDpi / 96f)) + 14;
            foreach (var (l, ctl) in _rows)
            {
                int rowH = Math.Max(l?.Height ?? 0, ctl.Height);
                if (l != null) l.Location = new Point(pad, y + (rowH - l.Height) / 2);
                int cx = l != null ? Width - pad - ctl.Width : pad;
                ctl.Location = new Point(Math.Max(pad, cx), y + (rowH - ctl.Height) / 2);
                y += rowH + 16;
            }
            Height = y + 2;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Card;
            _head.ForeColor = Theme.Accent; _head.BackColor = Theme.Card;
            foreach (var (l, ctl) in _rows)
            {
                if (l != null) { l.ForeColor = Theme.Text; l.BackColor = Theme.Card; }
                if (ctl is FlowLayoutPanel fp) { fp.BackColor = Theme.Card; foreach (Control _ in fp.Controls) { } }
                if (ctl is HotkeyBox hb) { hb.BackColor = Theme.Surface; hb.ForeColor = Theme.Text; }
                if (ctl is ComboBox cb) { cb.BackColor = Theme.Surface; cb.ForeColor = Theme.Text; }
                // Composite hotkey row (Panel holding a ToggleSwitch + HotkeyBox): theme the nested box too.
                if (ctl is Panel p && ctl is not FlowLayoutPanel)
                {
                    p.BackColor = Theme.Card;
                    foreach (Control child in p.Controls)
                    {
                        if (child is HotkeyBox chb) { chb.BackColor = Theme.Surface; chb.ForeColor = Theme.Text; }
                        child.Invalidate();
                    }
                }
                ctl.Invalidate();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            using var pen = new Pen(Theme.Border);
            using var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
            g.DrawPath(pen, path);

            if (!string.IsNullOrEmpty(_glyph))
            {
                float k = DeviceDpi / 96f;
                int isz = Ceil(26 * k);
                var iconR = new Rectangle(18, 14, isz, isz);
                using (var ap = new Pen(Theme.Accent, 1.7f))
                using (var ip = Theme.RoundRect(new RectangleF(iconR.X + 0.5f, iconR.Y + 0.5f, iconR.Width - 1, iconR.Height - 1), 6))
                    g.DrawPath(ap, ip);
                using var gf = new Font("Segoe MDL2 Assets", 10.5f);
                Ui.CenterGlyph(g, _glyph, gf, Theme.Accent, iconR);
            }
        }

        private static int Ceil(float v) => (int)Math.Ceiling(v);
    }
}

/// <summary>Small segmented control (themed).</summary>
/// <summary>Small checkbox + label (blue box, white tick), matching the overlay-settings mockup.</summary>
public sealed class CheckItem : Control
{
    private bool _on;
    public event Action<bool>? Toggled;
    public CheckItem(string text, bool on)
    {
        Text = text; _on = on; DoubleBuffered = true; Cursor = Cursors.Hand; Height = 26;
        SetStyle(ControlStyles.Selectable, false);
    }
    public bool Checked { get => _on; set { _on = value; Invalidate(); } }
    private static readonly Font F = new("Segoe UI", 10.5f);
    private int Box => (int)Math.Ceiling(18 * DeviceDpi / 96f);   // DPI-aware box size
    public int PreferredWidth => Box + 10 + TextRenderer.MeasureText(Text, F).Width + 6;
    protected override void OnClick(EventArgs e) { base.OnClick(e); _on = !_on; Invalidate(); Toggled?.Invoke(_on); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        int b = Box, y = (Height - b) / 2;
        using var path = Theme.RoundRect(new RectangleF(0.5f, y + 0.5f, b - 1, b - 1), 4);
        if (_on)
        {
            using (var br = new SolidBrush(Theme.AccentFill)) g.FillPath(br, path);
            using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[] { new PointF(b * 0.26f, y + b * 0.52f), new PointF(b * 0.44f, y + b * 0.70f), new PointF(b * 0.74f, y + b * 0.30f) });
        }
        else using (var pen = new Pen(Theme.BorderStrong, 1.4f)) g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, F, new Rectangle(b + 10, 0, Width - b - 10, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// Full-width gaming-overlay settings block laid out like the mockup: a "what to show" checkbox grid,
/// opacity/size sliders, layout + position + hotkey, and an options group — in two responsive columns.
/// </summary>
public sealed class OverlaySettingsPanel : Panel
{
    private readonly MainDeps _d;
    private readonly ToggleSwitch _enable, _optTop, _optClick, _optAccent, _optBold, _bgToggle;
    private readonly Slider _opacity, _scale, _bgOpacity;
    private readonly SegControl _layout, _opacityPre, _scalePre, _bgOpacityPre;
    private readonly ComboBox _position;
    private readonly Button _bgColor, _restore;
    private static readonly int[] OpacityPresets = { 60, 75, 90, 100 };
    private static readonly int[] ScalePresets = { 90, 100, 120, 140 };
    private static readonly int[] BgOpacityPresets = { 0, 40, 70, 100 };
    private readonly List<(CheckItem item, OverlayMetric metric)> _metrics = new();
    private readonly List<(string text, Rectangle rect, int kind)> _texts = new();
    private readonly List<int> _dividers = new();
    private int _w;

    private static int Ceil(float v) => (int)Math.Ceiling(v);

    public OverlaySettingsPanel(MainDeps d)
    {
        _d = d;
        DoubleBuffered = true;
        var s = d.Settings;

        _enable = new ToggleSwitch { Checked = s.OverlayEnabled };
        _enable.Toggled += v => d.SetOverlay(v);
        Controls.Add(_enable);

        void AddMetric(string key, OverlayMetric m)
        {
            var it = new CheckItem(Lang.T(key), s.HasMetric(m));
            it.Toggled += v => { s.SetMetric(m, v); d.SaveSettings(); d.ApplyOverlaySettings(); };
            _metrics.Add((it, m));
            Controls.Add(it);
        }
        AddMetric("ov_m_temp", OverlayMetric.CpuTemp | OverlayMetric.GpuTemp);
        AddMetric("ov_m_rpm", OverlayMetric.CpuRpm | OverlayMetric.GpuRpm);
        AddMetric("ov_m_profile", OverlayMetric.Profile);
        AddMetric("ov_m_fanpct", OverlayMetric.FanPct);
        AddMetric("ov_m_load", OverlayMetric.CpuLoad);
        AddMetric("ov_m_gpuusage", OverlayMetric.GpuUsage);
        AddMetric("ov_m_cpuclock", OverlayMetric.CpuClock);
        AddMetric("ov_m_ram", OverlayMetric.Ram);
        AddMetric("ov_m_vram", OverlayMetric.Vram);
        AddMetric("ov_m_cooler", OverlayMetric.CoolerBoost);
        AddMetric("ov_m_battery", OverlayMetric.Battery);
        AddMetric("ov_m_charge", OverlayMetric.ChargeLimit);

        // Opacity & size each get BOTH quick preset chips and a free-drag slider.
        void SetOpacity(int v) { s.OverlayOpacity = v; _opacityPre!.Selected = Array.IndexOf(OpacityPresets, v); d.SaveSettings(); d.ApplyOverlaySettings(); Relayout(_w); }
        void SetScale(int v) { s.OverlayScale = v; _scalePre!.Selected = Array.IndexOf(ScalePresets, v); d.SaveSettings(); d.ApplyOverlaySettings(); Relayout(_w); }

        _opacity = new Slider(40, 100, s.OverlayOpacity, 1, "%") { ShowValue = false };
        _opacity.ValueChanged += SetOpacity;
        Controls.Add(_opacity);
        _opacityPre = new SegControl(OpacityPresets.Select(p => p + "%").ToArray(), Array.IndexOf(OpacityPresets, s.OverlayOpacity));
        _opacityPre.SelectedChanged += i => { _opacity.Value = OpacityPresets[i]; SetOpacity(OpacityPresets[i]); };
        Controls.Add(_opacityPre);

        _scale = new Slider(80, 160, s.OverlayScale, 5, "%") { ShowValue = false };
        _scale.ValueChanged += SetScale;
        Controls.Add(_scale);
        _scalePre = new SegControl(ScalePresets.Select(p => p + "%").ToArray(), Array.IndexOf(ScalePresets, s.OverlayScale));
        _scalePre.SelectedChanged += i => { _scale.Value = ScalePresets[i]; SetScale(ScalePresets[i]); };
        Controls.Add(_scalePre);

        _layout = new SegControl(new[] { Lang.T("ov_layout_card"), Lang.T("ov_layout_bar") },
            string.Equals(s.OverlayLayout, "Bar", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        _layout.SelectedChanged += i => { s.OverlayLayout = i == 1 ? "Bar" : "Card"; d.SaveSettings(); d.ApplyOverlaySettings(); };
        Controls.Add(_layout);

        _position = new ThemedComboBox();
        _position.Items.AddRange(new object[] { Lang.T("ov_pos_pick"), Lang.T("ov_pos_tl"), Lang.T("ov_pos_tr"), Lang.T("ov_pos_bl"), Lang.T("ov_pos_br") });
        _position.SelectedIndex = 0;
        _position.SelectedIndexChanged += (_, _) => { if (_position.SelectedIndex > 0) { d.SnapOverlay(_position.SelectedIndex - 1); _position.SelectedIndex = 0; } };
        Controls.Add(_position);

        // Options use toggle switches (consistent with the rest of the app), not checkboxes.
        ToggleSwitch OptToggle(bool on, Action<bool> onChange) { var t = new ToggleSwitch { Checked = on }; t.Toggled += v => onChange(v); Controls.Add(t); return t; }
        _optTop = OptToggle(s.OverlayAlwaysTop, v => { s.OverlayAlwaysTop = v; d.SaveSettings(); d.ApplyOverlaySettings(); });
        _optClick = OptToggle(s.OverlayClickThrough, v => { s.OverlayClickThrough = v; d.SaveSettings(); d.ApplyOverlaySettings(); });
        _optAccent = OptToggle(s.OverlayAccentFromProfile, v => { s.OverlayAccentFromProfile = v; d.SaveSettings(); d.ApplyOverlaySettings(); });
        _optBold = OptToggle(s.OverlayBoldText, v => { s.OverlayBoldText = v; d.SaveSettings(); d.ApplyOverlaySettings(); });

        // Background: on/off toggle + colour swatch.
        _bgToggle = new ToggleSwitch { Checked = s.OverlayBgEnabled };
        _bgToggle.Toggled += v => { s.OverlayBgEnabled = v; d.SaveSettings(); d.ApplyOverlaySettings(); };
        Controls.Add(_bgToggle);

        void SetBgOpacity(int v) { s.OverlayBgOpacity = v; _bgOpacityPre!.Selected = Array.IndexOf(BgOpacityPresets, v); d.SaveSettings(); d.ApplyOverlaySettings(); Relayout(_w); }
        _bgOpacity = new Slider(0, 100, s.OverlayBgOpacity, 1, "%") { ShowValue = false };
        _bgOpacity.ValueChanged += SetBgOpacity;
        Controls.Add(_bgOpacity);
        _bgOpacityPre = new SegControl(BgOpacityPresets.Select(p => p + "%").ToArray(), Array.IndexOf(BgOpacityPresets, s.OverlayBgOpacity));
        _bgOpacityPre.SelectedChanged += i => { _bgOpacity.Value = BgOpacityPresets[i]; SetBgOpacity(BgOpacityPresets[i]); };
        Controls.Add(_bgOpacityPre);

        _bgColor = new Button { FlatStyle = FlatStyle.Flat, Text = "", TabStop = false };
        _bgColor.FlatAppearance.BorderSize = 1;
        try { _bgColor.BackColor = ColorTranslator.FromHtml(s.OverlayBgColor); } catch { _bgColor.BackColor = Color.FromArgb(0x16, 0x18, 0x1D); }
        _bgColor.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _bgColor.BackColor, FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _bgColor.BackColor = dlg.Color;
                s.OverlayBgColor = ColorTranslator.ToHtml(dlg.Color);
                d.SaveSettings(); d.ApplyOverlaySettings();
            }
        };
        Controls.Add(_bgColor);

        _restore = new Button { Text = Lang.T("ov_restore"), AutoSize = false };
        Ui.StyleGhost(_restore);
        _restore.Click += (_, _) => { s.RestoreOverlayDefaults(); d.SaveSettings(); d.ApplyOverlaySettings(); RefreshFromSettings(); Relayout(_w); };
        Controls.Add(_restore);
    }

    // Reflect the current settings onto every control (after "restore defaults").
    private void RefreshFromSettings()
    {
        var s = _d.Settings;
        foreach (var (item, m) in _metrics) item.Checked = s.HasMetric(m);
        _opacity.Value = s.OverlayOpacity; _opacityPre.Selected = Array.IndexOf(OpacityPresets, s.OverlayOpacity);
        _scale.Value = s.OverlayScale; _scalePre.Selected = Array.IndexOf(ScalePresets, s.OverlayScale);
        _layout.Selected = string.Equals(s.OverlayLayout, "Bar", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _optTop.Checked = s.OverlayAlwaysTop; _optClick.Checked = s.OverlayClickThrough; _optAccent.Checked = s.OverlayAccentFromProfile;
        _optBold.Checked = s.OverlayBoldText;
        _bgToggle.Checked = s.OverlayBgEnabled;
        _bgOpacity.Value = s.OverlayBgOpacity; _bgOpacityPre.Selected = Array.IndexOf(BgOpacityPresets, s.OverlayBgOpacity);
        try { _bgColor.BackColor = ColorTranslator.FromHtml(s.OverlayBgColor); } catch { }
    }

    // Fully DPI-aware: every offset is scaled by the current display scaling, and captions/labels are
    // built into the draw list here so nothing overlaps at 100% / 125% / 150% etc.
    public void Relayout(int width)
    {
        _w = width; Width = width;
        _texts.Clear(); _dividers.Clear();
        float k = DeviceDpi / 96f;
        int pad = Ceil(22 * k), colGap = Ceil(40 * k), capH = Ceil(20 * k), gap = Ceil(24 * k), blockGap = Ceil(24 * k);
        var s = _d.Settings;

        // header
        int hy = Ceil(22 * k);
        _enable.Location = new Point(width - pad - _enable.Width, hy);
        int headerH = Math.Max(_enable.Height, Ceil(30 * k));
        _texts.Add((Lang.T("overlay_title"), new Rectangle(pad + Ceil(42 * k), hy - Ceil(2 * k), width - pad * 2 - Ceil(180 * k), headerH), 0));
        _texts.Add((Lang.T("ov_show"), new Rectangle(_enable.Left - Ceil(150 * k), hy, Ceil(142 * k), headerH), 4));
        int y = hy + headerH + Ceil(14 * k);
        _dividers.Add(y); y += Ceil(18 * k);

        // "what to show" checkbox grid
        _texts.Add((Lang.T("ov_metrics"), new Rectangle(pad, y, Ceil(300 * k), capH), 1));
        int gridTop = y + Ceil(28 * k);
        int cols = Math.Max(1, (width - pad * 2) / Ceil(200 * k));
        int cellW = (width - pad * 2) / cols;
        int itemH = Ceil(30 * k), rowH = itemH + Ceil(6 * k);
        for (int i = 0; i < _metrics.Count; i++)
        {
            int c = i % cols, r = i / cols;
            _metrics[i].item.SetBounds(pad + c * cellW, gridTop + r * rowH, cellW - Ceil(14 * k), itemH);
        }
        int rows = (int)Math.Ceiling(_metrics.Count / (double)cols);
        y = gridTop + rows * rowH + Ceil(12 * k);
        _dividers.Add(y); y += Ceil(18 * k);

        // two columns
        int colW = (width - pad * 2 - colGap) / 2;
        int leftX = pad, rightX = pad + colW + colGap;
        int sliderH = Ceil(22 * k), segH = Ceil(34 * k);
        int yL = y, yR = y;

        int presetH = Ceil(30 * k), presetW = Ceil(210 * k), capGap = Ceil(6 * k);

        // opacity (left) & size (right): caption + preset chips + free slider
        _texts.Add(($"{Lang.T("ov_opacity")} — {s.OverlayOpacity}%", new Rectangle(leftX, yL, colW, capH), 2)); yL += capH + capGap;
        _texts.Add(($"{Lang.T("ov_scale")} — {s.OverlayScale}%", new Rectangle(rightX, yR, colW, capH), 2)); yR += capH + capGap;
        _opacityPre.SetBounds(leftX, yL, presetW, presetH); yL += presetH + Ceil(8 * k);
        _scalePre.SetBounds(rightX, yR, presetW, presetH); yR += presetH + Ceil(8 * k);
        _opacity.SetBounds(leftX, yL, colW, sliderH); yL += sliderH + blockGap;
        _scale.SetBounds(rightX, yR, colW, sliderH); yR += sliderH + blockGap;

        // layout (left) & position (right)
        _texts.Add((Lang.T("ov_layout"), new Rectangle(leftX, yL, colW, capH), 2)); yL += capH + capGap;
        _texts.Add((Lang.T("ov_position"), new Rectangle(rightX, yR, colW, capH), 2)); yR += capH + capGap;
        _layout.SetBounds(leftX, yL, Ceil(210 * k), segH); yL += segH + blockGap;
        _position.SetBounds(rightX, yR, Ceil(170 * k), _position.Height);
        _texts.Add((Lang.T("ov_drag_hint"), new Rectangle(rightX + Ceil(184 * k), yR, colW - Ceil(184 * k), Math.Max(_position.Height, capH)), 3));
        yR += Math.Max(_position.Height, Ceil(30 * k)) + blockGap;

        // background (left): toggle + colour swatch + label
        _texts.Add((Lang.T("ov_bg"), new Rectangle(leftX, yL, colW, capH), 2)); yL += capH + capGap;
        int bgRowH = Math.Max(_bgToggle.Height, Ceil(28 * k)), swW = Ceil(52 * k), swH = Ceil(28 * k);
        _bgToggle.Location = new Point(leftX, yL + (bgRowH - _bgToggle.Height) / 2);
        _bgColor.SetBounds(leftX + _bgToggle.Width + Ceil(16 * k), yL + (bgRowH - swH) / 2, swW, swH);
        _texts.Add((Lang.T("ov_bg_color"), new Rectangle(_bgColor.Right + Ceil(12 * k), yL, colW, bgRowH), 5));
        yL += bgRowH + Ceil(12 * k);
        // background opacity (independent of content) — preset chips + free-drag slider
        _texts.Add(($"{Lang.T("ov_bg_opacity")} — {s.OverlayBgOpacity}%", new Rectangle(leftX, yL, colW, capH), 2)); yL += capH + capGap;
        _bgOpacityPre.SetBounds(leftX, yL, presetW, presetH); yL += presetH + Ceil(8 * k);
        _bgOpacity.SetBounds(leftX, yL, colW, sliderH); yL += sliderH + blockGap;

        // options group (right) — toggle rows: label left, switch right
        _texts.Add((Lang.T("ov_options"), new Rectangle(rightX, yR, colW, capH), 1)); yR += capH + capGap;
        int oy = yR;
        void OptRow(ToggleSwitch t, string label)
        {
            int rh = Math.Max(t.Height, capH);
            t.Location = new Point(rightX + colW - t.Width, oy + (rh - t.Height) / 2);
            _texts.Add((label, new Rectangle(rightX, oy, colW - t.Width - Ceil(10 * k), rh), 5));
            oy += rh + Ceil(12 * k);
        }
        OptRow(_optTop, Lang.T("ov_ontop"));
        OptRow(_optClick, Lang.T("ov_lock_row"));
        OptRow(_optAccent, Lang.T("ov_accent"));
        OptRow(_optBold, Lang.T("ov_bold"));
        yR = oy;

        // restore-defaults button, bottom-right
        int by = Math.Max(yL, yR) + Ceil(4 * k);
        _restore.SetBounds(width - pad - Ceil(190 * k), by, Ceil(190 * k), Ceil(34 * k));
        Height = by + Ceil(34 * k) + pad;
        Invalidate();
    }

    // Pull control states from settings so this panel stays in sync when the overlay is toggled
    // elsewhere (Scenarios brick, tray menu, hotkey). Checked/value setters don't fire events.
    public void SyncFromSettings()
    {
        var s = _d.Settings;
        _enable.Checked = s.OverlayEnabled;
        foreach (var (item, metric) in _metrics) item.Checked = s.HasMetric(metric);
        _optTop.Checked = s.OverlayAlwaysTop;
        _optClick.Checked = s.OverlayClickThrough;
        _optAccent.Checked = s.OverlayAccentFromProfile;
        _optBold.Checked = s.OverlayBoldText;
        Invalidate();
    }

    public void ApplyThemeColors()
    {
        BackColor = Theme.Card;
        foreach (Control c in Controls)
        {
            if (c is ComboBox) { c.BackColor = Theme.Surface; c.ForeColor = Theme.Text; }
            c.Invalidate();
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Card);
        float k = DeviceDpi / 96f;
        int pad = Ceil(22 * k);
        using (var pen = new Pen(Theme.Border))
        using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12))
            g.DrawPath(pen, path);

        var acc = Theme.Accent;
        int isz = Ceil(26 * k);
        var iconR = new Rectangle(pad, Ceil(20 * k), isz, isz);
        using (var pen = new Pen(acc, 1.7f))
        using (var ip = Theme.RoundRect(new RectangleF(iconR.X + 0.5f, iconR.Y + 0.5f, iconR.Width - 1, iconR.Height - 1), 6))
            g.DrawPath(pen, ip);
        using (var gf = new Font("Segoe UI Symbol", 11f)) Ui.CenterGlyph(g, "▦", gf, acc, iconR);

        foreach (var (text, rect, kind) in _texts)
        {
            var (font, color, flags) = kind switch
            {
                0 => (new Font("Segoe UI", 13.5f, FontStyle.Bold), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left),
                1 => (new Font("Segoe UI", 9.5f, FontStyle.Bold), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left),
                3 => (new Font("Segoe UI", 9f), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left),
                4 => (new Font("Segoe UI", 10f), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right),
                5 => (new Font("Segoe UI", 10.5f), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis),
                _ => (new Font("Segoe UI", 10f), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left),
            };
            TextRenderer.DrawText(g, text, font, rect, color, flags);
            font.Dispose();
        }

        using var dp = new Pen(Theme.Border);
        foreach (int dy in _dividers) g.DrawLine(dp, pad, dy, Width - pad, dy);
    }
}

/// <summary>Themed horizontal slider: drag to set any value in [min,max]; shows the value + suffix.</summary>
public sealed class Slider : Control
{
    private readonly int _min, _max, _step;
    private readonly string _suffix;
    private int _val;
    private bool _drag;
    public event Action<int>? ValueChanged;
    public bool ShowValue { get; set; } = true;   // false = full-width track, caption drawn elsewhere

    public Slider(int min, int max, int val, int step = 1, string suffix = "")
    {
        _min = min; _max = max; _step = Math.Max(1, step); _suffix = suffix;
        _val = Clamp(val);
        DoubleBuffered = true; ResizeRedraw = true; Height = 26; Width = 240; Cursor = Cursors.Hand;
    }

    public int Value { get => _val; set { _val = Clamp(value); Invalidate(); } }
    private int Clamp(int v) => Math.Clamp(v, _min, _max);
    private int ValueW => ShowValue ? 46 : 0;
    private int X0 => 8;
    private int X1 => Width - (ShowValue ? ValueW + 10 : 8);

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _drag = true; SetFromX(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) => _drag = false;

    private void SetFromX(int x)
    {
        float t = Math.Clamp((x - X0) / (float)Math.Max(1, X1 - X0), 0f, 1f);
        int raw = _min + (int)Math.Round(t * (_max - _min) / _step) * _step;
        int nv = Clamp(raw);
        if (nv != _val) { _val = nv; Invalidate(); ValueChanged?.Invoke(_val); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        int cy = Height / 2;
        float t = (_val - _min) / (float)Math.Max(1, _max - _min);
        int tx = X0 + (int)(t * (X1 - X0));
        using (var b = new SolidBrush(Theme.Border)) g.FillRectangle(b, X0, cy - 2, X1 - X0, 4);
        using (var b = new SolidBrush(Theme.AccentFill)) g.FillRectangle(b, X0, cy - 2, Math.Max(0, tx - X0), 4);
        using (var b = new SolidBrush(Theme.Surface)) g.FillEllipse(b, tx - 8, cy - 8, 16, 16);
        using (var p = new Pen(Theme.BorderStrong)) g.DrawEllipse(p, tx - 8, cy - 8, 16, 16);
        if (ShowValue)
            TextRenderer.DrawText(g, _val + _suffix, new Font("Segoe UI", 10f, FontStyle.Bold),
                new Rectangle(X1 + 6, 0, ValueW, Height), Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

public sealed class SegControl : Control
{
    private readonly string[] _items;
    private int _sel;
    public event Action<int>? SelectedChanged;
    public int Selected { get => _sel; set { _sel = value; Invalidate(); } }

    public SegControl(string[] items, int sel) { _items = items; _sel = sel; DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int seg = Math.Clamp(e.X * _items.Length / Math.Max(1, Width), 0, _items.Length - 1);
        if (seg != _sel) { _sel = seg; Invalidate(); SelectedChanged?.Invoke(_sel); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(outer, 8))
        {
            using var b = new SolidBrush(Theme.Card); g.FillPath(b, path);
            using var pen = new Pen(Theme.Border); g.DrawPath(pen, path);
        }
        float seg = (float)Width / _items.Length;
        for (int i = 0; i < _items.Length; i++)
        {
            var r = new RectangleF(i * seg, 0, seg, Height);
            if (i == _sel)
            {
                using var path = Theme.RoundRect(new RectangleF(r.X + 2, 2, r.Width - 4, Height - 4), 7);
                using var b = new SolidBrush(Theme.AccentFill); g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, _items[i], new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Rectangle.Round(r), i == _sel ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>iOS-style themed on/off switch.</summary>
public sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event Action<bool>? Toggled;
    public bool Checked { get => _checked; set { _checked = value; Invalidate(); } }

    public ToggleSwitch() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; Size = new Size(52, 28); }

    protected override void OnClick(EventArgs e) { if (!Enabled) return; _checked = !_checked; Invalidate(); Toggled?.Invoke(_checked); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        // Dimmed when disabled (master shortcut switch off): muted track + knob so it reads as inactive.
        var track = !Enabled ? Theme.Border : _checked ? Theme.AccentFill : Theme.BorderStrong;
        var knob = Enabled ? Color.White : Theme.Muted;
        using (var path = Theme.RoundRect(r, Height / 2f))
        using (var b = new SolidBrush(track))
            g.FillPath(b, path);
        float d = Height - 8;
        float kx = _checked ? Width - d - 4 : 4;
        using var kb = new SolidBrush(knob);
        g.FillEllipse(kb, kx, 4, d, d);
    }
}

/// <summary>
/// DropDownList combo that honours the app theme (the stock ComboBox keeps a white field/button/list in
/// dark mode). Items are owner-drawn; the drop button, chevron and border are repainted after the default
/// paint so nothing stays light. Read theme colours at paint time so a light/dark switch just needs Invalidate.
/// </summary>
public sealed class ThemedComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? app, string? idList);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore; public int fIncUpdate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    public ThemedComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 30;   // roomy rows in the drop-down list
    }

    // Disable visual styles so the drop-down list scrollbar/border follow the flat dark look too.
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetWindowTheme(Handle, "", ""); }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hWnd, ref COMBOBOXINFO info);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int LB_GETTOPINDEX = 0x018E, LB_SETTOPINDEX = 0x0197;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int cbSize; public RECT rcItem, rcButton; public int stateButton;
        public IntPtr hwndCombo, hwndItem, hwndList;
    }

    // Windows routes the wheel to the control under the cursor, and a stock combo responds by
    // CHANGING ITS VALUE - so scrolling Settings past the language combo switched the language
    // and the page stopped scrolling (discussion #9). Never let the wheel change the value:
    // with the list closed, hand the scroll to the nearest scrollable ancestor; with the list
    // open, scroll the drop-down listbox itself (LB_SETTOPINDEX on its window).
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs h) h.Handled = true;
        if (DroppedDown)
        {
            var info = new COMBOBOXINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<COMBOBOXINFO>() };
            if (GetComboBoxInfo(Handle, ref info) && info.hwndList != IntPtr.Zero)
            {
                int top = (int)SendMessage(info.hwndList, LB_GETTOPINDEX, IntPtr.Zero, IntPtr.Zero);
                int next = Math.Max(0, top - Math.Sign(e.Delta) * SystemInformation.MouseWheelScrollLines);
                SendMessage(info.hwndList, LB_SETTOPINDEX, (IntPtr)next, IntPtr.Zero);
            }
            return;
        }
        for (Control? p = Parent; p != null; p = p.Parent)
            if (p is ScrollableControl sc && sc.AutoScroll)
            {
                sc.AutoScrollPosition = new Point(-sc.AutoScrollPosition.X, -sc.AutoScrollPosition.Y - e.Delta);
                break;
            }
    }

    // Drop-down LIST rows. The CLOSED field can also arrive here (DrawItemState.ComboBoxEdit),
    // e.g. when the combo merely receives focus - clicking an overlay checkbox pushed focus to
    // the Position combo and its field lit up solid blue (discussion #9). Paint the field like
    // the normal closed state instead: no selection highlight outside the drop-down list.
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) { e.DrawBackground(); return; }
        bool field = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool sel = !field && (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? Theme.AccentFill : Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
        var fore = sel ? Color.White : Theme.Text;
        var rect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, rect, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    // Fully own the closed-field paint: the stock combo flashes its button light on hover/press before an
    // overpaint can cover it. We validate the update region ourselves and never let the base class paint,
    // so there is no light frame at all.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PAINT)
        {
            BeginPaint(Handle, out var ps);
            try { using var g = Graphics.FromHdc(ps.hdc); PaintField(g); }
            finally { EndPaint(Handle, ref ps); }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    private void PaintField(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, ClientRectangle);
        var textRect = new Rectangle(8, 0, Width - 30, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        int cx = Width - 13, cy = Height / 2;
        using (var pen = new Pen(Theme.Muted, 1.6f))
            g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
        using (var bp = new Pen(Theme.Border))
            g.DrawRectangle(bp, 0, 0, Width - 1, Height - 1);
    }
}
