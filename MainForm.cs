using System.Drawing.Drawing2D;

namespace GhostDeck;

public enum MainTab { Scenarios, Status, FanCurve, Settings, Models, Report, Updates }

/// <summary>Everything the tabbed UI needs from the tray context (data + actions).</summary>
public sealed class MainDeps
{
    public required AppSettings Settings { get; init; }
    public required Func<StatusInfo> Status { get; init; }
    public required Func<HwSnapshot> Hw { get; init; }
    public required Func<ProfileId> Current { get; init; }
    public required Action<ProfileId> SetProfile { get; init; }
    public required Func<bool> Writable { get; init; }
    public required Func<ProfileId, Color> ColorOf { get; init; }
    public required string Firmware { get; init; }
    public required Func<string> AppVersion { get; init; }
    public required Action SaveSettings { get; init; }
    public required Action CheckNoticesNow { get; init; }   // manual "Check now" also surfaces announcements
    public required Action SettingsChanged { get; init; }     // tray rebuilds menu / hotkeys
    public required Action OpenLegacySettings { get; init; }   // interim: advanced settings dialog
    public required Action StartReportWizard { get; init; }    // interim: report wizard dialog
    public required Action<int> SetChargeLimit { get; init; }  // 0 = off, else 60/80/100
    public required Action<bool> SetAutoSwitch { get; init; }
    public required Func<bool> CoolerBoost { get; init; }          // current Cooler Boost (max fans) state
    public required Action<bool> SetCoolerBoost { get; init; }     // turn Cooler Boost on/off (gated on writable)
    public required Action<Action<DeviceProfile>> WithEcWrite { get; init; }  // runs only if writable + not simulating
    public required Func<bool> OverlayOn { get; init; }
    public required Action<bool> SetOverlay { get; init; }
    public required Action ApplyOverlaySettings { get; init; }   // re-read overlay options after a settings edit
    public required Action<int> SnapOverlay { get; init; }       // 0=TL 1=TR 2=BL 3=BR — snap overlay to a screen corner
}

/// <summary>Base for tab pages. Re-themes on demand; refreshes data on enter; supports scrolling.</summary>
public abstract class ThemedPage : UserControl
{
    protected readonly MainDeps D;
    protected ThemedPage(MainDeps d)
    {
        D = d;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoScroll = true;
        BackColor = Theme.Surface;
        Resize += (_, _) => Invalidate();
    }

    // NB: WS_EX_COMPOSITED was tried here against scroll tearing (discussion #9) and REVERTED -
    // it made every tab visibly slow to paint and flashed white during startup. Do not re-add;
    // the children are individually double-buffered instead.

    // Faint brand grid under every tab's content (cards and controls paint over it).
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (D.Settings.ShowGrid) Ui.DrawGrid(e.Graphics, ClientRectangle);
    }
    public virtual void OnEnter() { }
    // Lightweight refresh after external state changes (profile/cooler/overlay). Unlike OnEnter it must
    // NOT re-run layout — a re-layout on a scrolled page (Settings) yanks the scroll position to the top.
    public virtual void LiveRefresh() { }
    public virtual void ApplyTheme() { BackColor = Theme.Surface; Invalidate(true); }

    /// <summary>Translate painting to honour the scroll offset (call at the top of OnPaint).</summary>
    protected void ApplyScroll(Graphics g) => g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

    // We paint the whole surface ourselves, so a partial scroll blit leaves ghosting.
    // Force a full repaint whenever the scroll position changes.
    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

    // Stop WinForms from auto-scrolling the page when a child control gains focus (clicking a toggle
    // deep in Settings otherwise yanked the scroll back to the top). Keep the current scroll position.
    protected override Point ScrollToControl(Control activeControl) => AutoScrollPosition;
}

/// <summary>Single tabbed window. The tray menu opens it on a specific tab; tabs swap content in-place.</summary>
public sealed class MainForm : Form
{
    private const int StripH = 78;
    private readonly MainDeps _d;
    private readonly Panel _strip = new();
    private readonly NoticeBanner _banner = new();
    private readonly Panel _host = new BufferedPanel();
    private readonly List<TabButton> _tabs = new();
    private readonly Dictionary<MainTab, ThemedPage> _pages = new();
    private readonly GlyphButton _themeBtn = new();
    private readonly GlyphButton _reportBtn = new();
    private readonly GlyphButton _updatesBtn = new();
    private readonly ToolTip _tip = new();
    private readonly Label _version = new();
    private MainTab _active = MainTab.Scenarios;

    public MainForm(MainDeps d)
    {
        _d = d;
        Text = "GhostDeck";
        Icon = TrayIconFactory.AppIcon();
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 620);
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        RestoreBounds2();

        BuildStrip();
        _host.Dock = DockStyle.Fill;
        Controls.Add(_host);
        Controls.Add(_strip);
        Controls.Add(_banner);   // docked Top, below the strip and above the host (hidden until a notice arrives)

        Theme.Changed += OnThemeChanged;
        FormClosing += (_, _) => SaveBounds();
        FormClosed += (_, _) => Theme.Changed -= OnThemeChanged;

        ApplyThemeChrome();
        ShowTab(MainTab.Scenarios);
        Shown += (_, _) => EnsureWarm();

        // Closing the window would dispose every page and repeat the whole first-show cost on
        // reopen; hide instead (the app lives in the tray). App exit still closes it for real.
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };

        // Hidden developer entry to the EC test/discovery tools (Ctrl+Shift+T). See docs/TECHNICAL.md §12.
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.T)
            {
                using var dlg = new TestDialog(_d);
                dlg.ShowDialog(this);
                e.Handled = true;
            }
        };
    }

    private void RestoreBounds2()
    {
        var s = _d.Settings;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 980);
        if (s.WinW >= MinimumSize.Width && s.WinH >= MinimumSize.Height)
        {
            StartPosition = FormStartPosition.Manual;
            var r = new Rectangle(s.WinX, s.WinY, s.WinW, s.WinH);
            if (!IsVisibleOnAnyScreen(r)) r.Location = new Point(wa.X + (wa.Width - r.Width) / 2, wa.Y + (wa.Height - r.Height) / 2);
            Bounds = r;
            WindowState = s.WinMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }
        else
        {
            int w = Math.Min(1600, wa.Width - 80), h = Math.Min(980, wa.Height - 80);
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        }
    }

    private static bool IsVisibleOnAnyScreen(Rectangle r) =>
        Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r));

    private void SaveBounds()
    {
        var s = _d.Settings;
        s.WinMaximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        s.WinX = b.X; s.WinY = b.Y; s.WinW = b.Width; s.WinH = b.Height;
        _d.SaveSettings();
    }

    private void BuildStrip()
    {
        _strip.Dock = DockStyle.Top;
        _strip.Height = StripH;
        _strip.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int sepY = _strip.Height - 1;
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, sepY, _strip.Width, sepY);
            var act = _tabs.FirstOrDefault(t => t.Active);
            if (act != null)
                using (var pen = new Pen(Theme.Accent, 3f))
                    g.DrawLine(pen, act.Left + 14, sepY, act.Right - 14, sepY);
            DrawWordmark(g);
            DrawTierBadge(g);
        };

        BuildTabs();

        _version.AutoSize = true;
        _version.Font = new Font("Segoe UI", 9.5f);
        _strip.Controls.Add(_version);

        // Report + Updates live as icons on the right (next to the theme toggle) instead of top-level
        // tabs, freeing room in the strip. Report opens the Report page (deep-linkable sub-tab).
        _updatesBtn.Size = new Size(40, 38);
        _updatesBtn.Glyph = "⟳";
        _updatesBtn.GlyphDx = 1; _updatesBtn.GlyphDy = -2;   // ⟳ ink sits low/left — nudge up + right to match ⚑ / ☾
        _updatesBtn.Click += (_, _) => ShowTab(MainTab.Updates);
        _tip.SetToolTip(_updatesBtn, Lang.T("tab_updates"));
        _strip.Controls.Add(_updatesBtn);

        _reportBtn.Size = new Size(40, 38);
        _reportBtn.Glyph = "⚑";
        _reportBtn.Click += (_, _) => ShowTab(MainTab.Report);
        _tip.SetToolTip(_reportBtn, Lang.T("tab_report"));
        _strip.Controls.Add(_reportBtn);

        _themeBtn.Size = new Size(40, 38);
        _themeBtn.Glyph = Theme.Dark ? "☀" : "☾";
        _themeBtn.Click += (_, _) => { Theme.Toggle(); _d.Settings.DarkMode = Theme.Dark; _d.SaveSettings(); };
        _strip.Controls.Add(_themeBtn);

        _strip.Resize += (_, _) => { LayoutStrip(); _strip.Invalidate(); };
        LayoutStrip();
    }

    // Brand wordmark (ghost mark + "GhostDeck", "Deck" in accent) at the far LEFT of the strip;
    // the tab row starts after it (LayoutStrip uses WordmarkWidth for the offset).
    private void DrawWordmark(Graphics g)
    {
        using var wf = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        const string s1 = "Ghost", s2 = "Deck";
        int w1 = TextRenderer.MeasureText(g, s1, wf, Size.Empty, TextFormatFlags.NoPadding).Width;
        int mark = (int)(26 * _strip.DeviceDpi / 96f);
        const int bx = 20;
        TrayIconFactory.DrawGhost(g, bx, (StripH - mark) / 2f - 1, mark, Theme.Accent, Theme.Surface);
        int ty = (StripH - wf.Height) / 2 - 1;
        TextRenderer.DrawText(g, s1, wf, new Point(bx + mark + 9, ty), Theme.Text, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, s2, wf, new Point(bx + mark + 9 + w1, ty), Theme.Accent, TextFormatFlags.NoPadding);
    }

    private int WordmarkWidth()
    {
        using var wf = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        int tw = TextRenderer.MeasureText("GhostDeck", wf, Size.Empty, TextFormatFlags.NoPadding).Width;
        return (int)(26 * _strip.DeviceDpi / 96f) + 9 + tw;
    }

    // Tier badge (tested / experimental / unsupported) drawn left of the version label.
    private void DrawTierBadge(Graphics g)
    {
        var info = _d.Status();
        using var bf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        int w = TextRenderer.MeasureText(info.TierText, bf).Width + 32;
        int h = bf.Height + 14;
        Ui.Pill(g, info.TierText, new Point(_version.Left - w - 16, (StripH - h) / 2), info.TierColor);
    }

    // Every main view with its strip glyph; each can live in the tab row or, per user choice
    // (Settings → Interface), as an icon-only button on the right, past the version number.
    private static readonly (MainTab tab, string langKey, string glyph)[] TabDefs =
    {
        (MainTab.Scenarios, "tab_scenarios", ""),
        (MainTab.Status,    "menu_status",   ""),
        (MainTab.FanCurve,  "tab_fancurve",  ""),
        (MainTab.Settings,  "menu_settings", ""),
        (MainTab.Models,    "tab_models",    "\U0001F4BB"),
    };
    private readonly List<GlyphButton> _tabIcons = new();
    private string _iconTabsApplied = "";

    private void BuildTabs()
    {
        foreach (var b in _tabs) { _strip.Controls.Remove(b); b.Dispose(); }
        _tabs.Clear();
        foreach (var gb in _tabIcons) { _strip.Controls.Remove(gb); gb.Dispose(); }
        _tabIcons.Clear();
        var asIcons = _d.Settings.IconTabs;
        foreach (var (tab, key, glyph) in TabDefs)
        {
            if (asIcons.Contains(tab.ToString()))
            {
                var gb = new GlyphButton { Size = new Size(40, 38), Glyph = glyph, Tag = tab };
                gb.Click += (_, _) => ShowTab((MainTab)gb.Tag!);
                _tip.SetToolTip(gb, Lang.T(key));
                _strip.Controls.Add(gb);
                _tabIcons.Add(gb);
            }
            else AddTab(tab, Lang.T(key), glyph);
        }
        _iconTabsApplied = string.Join(",", asIcons.OrderBy(s => s));
    }

    /// <summary>Re-applies the tab/icon split after a settings change (no-op when unchanged).</summary>
    public void SyncStrip()
    {
        string want = string.Join(",", _d.Settings.IconTabs.OrderBy(s => s));
        if (want == _iconTabsApplied) return;
        BuildTabs();
        LayoutStrip();
        foreach (var b in _tabs) b.Active = (MainTab)b.Tag! == _active;
        _strip.Invalidate(true);
    }

    private void AddTab(MainTab tab, string text, string glyph)
    {
        var b = new TabButton { Text = text, Glyph = glyph, Tag = tab };
        b.Click += (_, _) => ShowTab((MainTab)b.Tag!);
        _tabs.Add(b);
        _strip.Controls.Add(b);
    }

    private void LayoutStrip()
    {
        // tabs start after the left wordmark; buttons reach almost down to the separator line,
        // so the whole tab height is clickable (not just the text)
        int x = 20 + WordmarkWidth() + 30, y = 6, h = StripH - 7;
        var f = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        foreach (var b in _tabs)
        {
            int w = TextRenderer.MeasureText(b.Text, f).Width + 30 + 30; // text + icon + padding
            b.SetBounds(x, y, w, h);
            x += w + 8;
        }
        _themeBtn.Location = new Point(_strip.Width - _themeBtn.Width - 18, (StripH - _themeBtn.Height) / 2);
        _reportBtn.Location = new Point(_themeBtn.Left - _reportBtn.Width - 8, (StripH - _reportBtn.Height) / 2);
        _updatesBtn.Location = new Point(_reportBtn.Left - _updatesBtn.Width - 8, (StripH - _updatesBtn.Height) / 2);
        // tabs demoted to icons sit right of the version number, before the updates button
        int ix = _updatesBtn.Left;
        for (int i = _tabIcons.Count - 1; i >= 0; i--)
        {
            ix -= _tabIcons[i].Width + 8;
            _tabIcons[i].Location = new Point(ix, (StripH - _tabIcons[i].Height) / 2);
        }
        _version.Text = "v" + _d.AppVersion();
        _version.Location = new Point(ix - _version.Width - 14, (StripH - _version.Height) / 2);
    }

    public void ShowTab(MainTab tab)
    {
        _active = tab;
        if (!_pages.TryGetValue(tab, out var page))
        {
            page = CreatePage(tab);
            _pages[tab] = page;
            _host.Controls.Add(page);
        }
        foreach (var p in _pages.Values) p.Visible = p == page;
        page.OnEnter();
        page.BringToFront();
        foreach (var b in _tabs) { b.Active = (MainTab)b.Tag! == tab; b.Invalidate(); }
        _strip.Invalidate();
        Activate();
    }

    private bool _warmed;

    /// <summary>
    /// Builds the remaining pages AND forces their native window handles off-screen. The
    /// once-per-tab white flash was the handle-creation storm: showing a page for the first
    /// time created dozens of native controls on the spot. Runs after Shown, or right after
    /// the tray pre-creates this form hidden.
    /// </summary>
    public async void EnsureWarm()
    {
        if (_warmed) return;
        _warmed = true;
        ForceHandles(this);   // strip, host, banner + the initial Scenarios page
        foreach (var t in new[] { MainTab.Settings, MainTab.Status, MainTab.FanCurve, MainTab.Models })
        {
            await Task.Delay(250);   // spread out so the UI thread stays responsive
            if (IsDisposed) return;
            if (!_pages.ContainsKey(t))
            {
                var page = CreatePage(t);
                page.Visible = false;
                _pages[t] = page;
                _host.Controls.Add(page);
            }
            ForceHandles(_pages[t]);
        }
    }

    private static void ForceHandles(Control c)
    {
        _ = c.Handle;   // creates the native window even while invisible
        foreach (Control ch in c.Controls) ForceHandles(ch);
    }

    /// <summary>Open the Report page on a given sub-tab (0 = profiles, 1 = fan curve). Deep-linked from Models / Fan curve.</summary>
    public void ShowReport(int sub)
    {
        ShowTab(MainTab.Report);
        if (_pages.TryGetValue(MainTab.Report, out var p) && p is ReportPage rp) rp.SetSubTab(sub);
    }

    public void RefreshActive()
    {
        if (_pages.TryGetValue(_active, out var p) && p.Visible) p.LiveRefresh();
    }

    /// <summary>Show an announcement banner at the top of the window (marks it seen immediately).</summary>
    public void ShowNotice(string title, string body, string? url, Action onSeen)
        => _banner.ShowNotice(title, body, url, onSeen);

    private ThemedPage CreatePage(MainTab tab) => tab switch
    {
        MainTab.Scenarios => new ScenariosPage(_d),
        MainTab.Status    => new StatusPage(_d),
        MainTab.FanCurve  => new FanCurvePage(_d),
        MainTab.Updates   => new UpdatesPage(_d),
        MainTab.Settings  => new SettingsPage(_d),
        MainTab.Models    => new ModelsPage(_d),
        _                 => new ReportPage(_d),
    };

    private void OnThemeChanged()
    {
        _themeBtn.Glyph = Theme.Dark ? "☀" : "☾";
        Icon = TrayIconFactory.AppIcon();
        ApplyThemeChrome();
        foreach (var p in _pages.Values) p.ApplyTheme();
        Invalidate(true);
    }

    private void ApplyThemeChrome()
    {
        BackColor = Theme.Page;
        _strip.BackColor = Theme.Surface;
        _host.BackColor = Theme.Surface;
        _version.ForeColor = Theme.Muted;
        _version.BackColor = Theme.Surface;
        _banner.Invalidate();
        _strip.Invalidate();
        foreach (var b in _tabs) b.Invalidate();
        _themeBtn.Invalidate();
        _reportBtn.Invalidate();
        _updatesBtn.Invalidate();
        LayoutStrip();
    }

    // ---- chrome controls ----
    private sealed class TabButton : Control
    {
        public bool Active;
        public string Glyph = "";
        private bool _hover;
        public TabButton() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            if (_hover && !Active)
            {
                using var b = new SolidBrush(Theme.Card);
                using var path = Theme.RoundRect(new RectangleF(0, 2, Width, Height - 4), 9);
                g.FillPath(b, path);
            }
            var col = Active ? Theme.Accent : (_hover ? Theme.Text : Theme.Muted);
            var iconFont = new Font("Segoe MDL2 Assets", 15f);
            var textFont = new Font("Segoe UI", 11.5f, Active ? FontStyle.Bold : FontStyle.Regular);
            int tw = TextRenderer.MeasureText(Text, textFont).Width;
            const int iconW = 28, gap = 10;
            int total = iconW + gap + tw;
            int sx = Math.Max(6, (Width - total) / 2);
            // NoClipping: at some DPI scales / font fallbacks the glyph ink is wider than its
            // 28px cell and the default clip cut its edge (discussion #9, "Settings icon cut").
            TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle(sx, 0, iconW, Height), col,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoClipping);
            TextRenderer.DrawText(g, Text, textFont, new Rectangle(sx + iconW + gap, 0, tw + 8, Height), col,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    private sealed class GlyphButton : Control
    {
        public string Glyph = "";
        public int GlyphDx, GlyphDy;   // optical nudge — TextRenderer centres the glyph CELL, not its ink,
                                       // and symbol glyphs have uneven side bearings, so each needs its own tweak
        private bool _hover;
        public GlyphButton() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, 9))
            {
                using var b = new SolidBrush(_hover ? Theme.Card : Theme.Surface);
                g.FillPath(b, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
            var gr = ClientRectangle; gr.Offset(GlyphDx, GlyphDy);
            // PUA glyphs (the tab icons) live in Segoe MDL2 Assets; Segoe UI Symbol shows them as boxes
            bool mdl2 = Glyph.Length > 0 && Glyph[0] >= '' && Glyph[0] <= '';
            using var gf = new Font(mdl2 ? "Segoe MDL2 Assets" : "Segoe UI Symbol", 14f);
            TextRenderer.DrawText(g, Glyph, gf, gr, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Double-buffered host so swapping/resizing pages doesn't flicker.</summary>
    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    /// <summary>Top-docked announcement strip: accent stripe + title/body + optional "Details" link + close.
    /// Hidden until <see cref="ShowNotice"/>; marks the notice seen the moment it's shown. DPI-scaled.</summary>
    private sealed class NoticeBanner : Panel
    {
        private string _title = "", _body = "";
        private string? _url;
        private Rectangle _moreRect, _closeRect;
        private bool _hoverMore, _hoverClose;

        public NoticeBanner()
        {
            Dock = DockStyle.Top;
            Visible = false;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Height = S(56);
        }

        private int S(int v) => (int)Math.Ceiling(v * DeviceDpi / 96f);

        public void ShowNotice(string title, string body, string? url, Action onSeen)
        {
            _title = title; _body = body; _url = string.IsNullOrEmpty(url) ? null : url;
            Height = S(56);
            Visible = true;
            onSeen();                 // seen the moment it's shown in-window
            Invalidate();
        }

        public void ApplyTheme() => Invalidate();

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool m = _url != null && _moreRect.Contains(e.Location), c = _closeRect.Contains(e.Location);
            if (m != _hoverMore || c != _hoverClose)
            {
                _hoverMore = m; _hoverClose = c;
                Cursor = (m || c) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_closeRect.Contains(e.Location)) { Visible = false; return; }
            if (_url != null && _moreRect.Contains(e.Location))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_url) { UseShellExecute = true }); } catch { }
                Visible = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var accent = Theme.Accent;
            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, ClientRectangle);
            using (var b = new SolidBrush(accent)) g.FillRectangle(b, 0, 0, S(4), Height);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            int pad = S(16);
            int cs = S(28);
            _closeRect = new Rectangle(Width - pad - cs, (Height - cs) / 2, cs, cs);
            using (var cf = new Font("Segoe UI", 10f))
                TextRenderer.DrawText(g, "✕", cf, _closeRect, _hoverClose ? Theme.Text : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int rightLimit = _closeRect.Left - S(8);
            if (_url != null)
            {
                using var lf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                string more = Lang.T("notice_more");
                int mw = TextRenderer.MeasureText(more, lf).Width + S(12);
                _moreRect = new Rectangle(rightLimit - mw, (Height - S(26)) / 2, mw, S(26));
                TextRenderer.DrawText(g, more, lf, _moreRect, _hoverMore ? Theme.Text : accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                rightLimit = _moreRect.Left - S(10);
            }
            else _moreRect = Rectangle.Empty;

            int x = S(18);
            int textW = Math.Max(S(60), rightLimit - x);
            using var tf = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            using var bf = new Font("Segoe UI", 9.5f);
            int tH = TextRenderer.MeasureText(_title, tf).Height;
            int bH = TextRenderer.MeasureText(_body, bf).Height;
            int ty = Math.Max(S(6), (Height - tH - bH) / 2);
            TextRenderer.DrawText(g, _title, tf, new Rectangle(x, ty, textW, tH), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _body, bf, new Rectangle(x, ty + tH, textW, bH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
    }
}
