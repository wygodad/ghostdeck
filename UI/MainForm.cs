using System.Drawing.Drawing2D;

namespace GhostDeck;

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
    private readonly System.Windows.Forms.Timer _dispDebounce = new() { Interval = 600 };

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

        // Display topology changes (dock/undock, "second screen only", resolution switches)
        // invalidate the rate lists built into the pages. The event fires on the UI thread and
        // also for the app's OWN SetRefresh calls, so the reaction is debounced and each page
        // checks whether anything it derives from Display actually changed before rebuilding.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnSysDisplayChanged;
        _dispDebounce.Tick += (_, _) => OnDisplayTopologyChanged();
        FormClosed += (_, _) => { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnSysDisplayChanged; _dispDebounce.Dispose(); };

        ApplyThemeChrome();
        ShowTab(MainTab.Scenarios);
        Shown += (_, _) => EnsureWarm();

        // Closing the window would dispose every page and repeat the whole first-show cost on
        // reopen; hide instead (the app lives in the tray). App exit still closes it for real.
        FormClosing += (_, e) =>
        {
            // Closing the window is the only way to dismiss a running power test, and there is no
            // other UI for it once hidden - so stop it rather than leaving the fans at full speed.
            if (e.CloseReason == CloseReason.UserClosing) { StopPowerTest(wait: false); e.Cancel = true; Hide(); }
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
                gb.Click += (_, _) => ShowTab((MainTab)gb.Tag!, fromStrip: true);
                _tip.SetToolTip(gb, Lang.T(key));
                _strip.Controls.Add(gb);
                _tabIcons.Add(gb);
            }
            else AddTab(tab, Lang.T(key), glyph);
        }
        _iconTabsApplied = string.Join(",", asIcons.OrderBy(s => s));
    }

    private string _langApplied = Lang.CurrentCode;

    /// <summary>
    /// Re-applies the tab strip after a settings change (no-op when nothing relevant changed).
    /// Two things invalidate it: the tab/icon split, and the UI language - the buttons carry
    /// their captions, so a language switch has to rebuild them or the strip keeps the old one.
    /// </summary>
    public void SyncStrip()
    {
        bool langDrift = _langApplied != Lang.CurrentCode;
        string want = string.Join(",", _d.Settings.IconTabs.OrderBy(s => s));
        if (!langDrift && want == _iconTabsApplied) return;
        _langApplied = Lang.CurrentCode;
        BuildTabs();
        LayoutStrip();
        foreach (var b in _tabs) b.Active = (MainTab)b.Tag! == _active;
        _strip.Invalidate(true);
        // Pages hold captured captions of their own (sub-tab bars, buttons); let each refresh its.
        if (langDrift) foreach (var p in _pages.Values) p.OnLanguageChanged();
    }

    private void AddTab(MainTab tab, string text, string glyph)
    {
        var b = new TabButton { Text = text, Glyph = glyph, Tag = tab };
        b.Click += (_, _) => ShowTab((MainTab)b.Tag!, fromStrip: true);
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

    /// <summary>Gear on the Scenarios tab: jump to Settings → General and flash the visibility card.</summary>
    public void FocusScenVisibility()
    {
        ShowTab(MainTab.Settings);
        if (_pages.TryGetValue(MainTab.Settings, out var p) && p is SettingsPage sp) sp.FocusScenVisibility();
    }

    /// <summary>
    /// Switch to a tab. <paramref name="fromStrip"/> is true only for clicks on the tab strip
    /// itself: clicking the tab you are already on takes that page back to its own start view
    /// (OnReenter). Deep links from the tray must NOT set it - they aim at a specific sub-page.
    /// </summary>
    public void ShowTab(MainTab tab, bool fromStrip = false)
    {
        bool reenter = fromStrip && _active == tab
                       && _pages.TryGetValue(tab, out var cur) && cur.Visible;
        _active = tab;
        if (!_pages.TryGetValue(tab, out var page))
        {
            page = CreatePage(tab);
            _pages[tab] = page;
            _host.Controls.Add(page);
        }
        foreach (var p in _pages.Values) p.Visible = p == page;
        page.OnEnter();
        if (reenter) page.OnReenter();
        page.BringToFront();
        foreach (var b in _tabs) { b.Active = (MainTab)b.Tag! == tab; b.Invalidate(); }
        // Icon-only buttons must show where we are too: tabs collapsed to icons (per Settings →
        // Interface) plus the fixed Report / Updates icons on the right (raised by the user -
        // opening a page via an icon left NOTHING highlighted anywhere in the strip).
        foreach (var gb in _tabIcons) { gb.Active = gb.Tag is MainTab t && t == tab; gb.Invalidate(); }
        _reportBtn.Active = tab == MainTab.Report; _reportBtn.Invalidate();
        _updatesBtn.Active = tab == MainTab.Updates; _updatesBtn.Invalidate();
        _strip.Invalidate();
        Activate();
    }

    /// <summary>Open the Updates tab; when <paramref name="focusTag"/> is set, expand that release's notes.</summary>
    public void ShowUpdates(string? focusTag)
    {
        ShowTab(MainTab.Updates);
        if (_pages.TryGetValue(MainTab.Updates, out var p) && p is UpdatesPage up) up.FocusRelease(focusTag);
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

    /// <summary>Open the Report page on a given sub-tab (0 = profiles, 1 = fan curve, 2 = power test). Deep-linked from Models / Fan curve / the tray.</summary>
    public void ShowReport(int sub)
    {
        ShowTab(MainTab.Report);
        if (_pages.TryGetValue(MainTab.Report, out var p) && p is ReportPage rp) rp.SetSubTab(sub);
    }

    /// <summary>Stop a running power test (window closing, or app exit - see ReportPage.StopPowerTest).</summary>
    public void StopPowerTest(bool wait)
    {
        if (_pages.TryGetValue(MainTab.Report, out var p) && p is ReportPage rp) rp.StopPowerTest(wait);
    }

    /// <summary>Open Status on the Gaming sub-tab. Deep-linked from the session-report popup.</summary>
    public void ShowStatusGaming()
    {
        ShowTab(MainTab.Status);
        if (_pages.TryGetValue(MainTab.Status, out var p) && p is StatusPage sp) sp.ShowGaming();
    }

    public void RefreshActive()
    {
        if (_pages.TryGetValue(_active, out var p) && p.Visible) p.LiveRefresh();
    }

    /// <summary>True while the fan-curve editor is actively driving the EC (see FanCurvePage).</summary>
    public bool CurveEditorHot =>
        _pages.TryGetValue(MainTab.FanCurve, out var p) && p is FanCurvePage fc && fc.CurveHot;

    /// <summary>A newer model database went live: let every page re-read what it derived from it.</summary>
    public void OnDeviceDbChanged()
    {
        foreach (var p in _pages.Values) p.OnDeviceDbChanged();
        Invalidate(true);
    }

    private void OnSysDisplayChanged(object? s, EventArgs e) { _dispDebounce.Stop(); _dispDebounce.Start(); }

    /// <summary>
    /// Debounced display-settings change. The Scenarios rate brick lives in readonly fields, so
    /// when it no longer matches the panel the whole page is recreated (hidden pages get their
    /// handles forced like EnsureWarm does, keeping the next visit flash-free); every other page
    /// refreshes its display-derived state through OnDisplayChanged.
    /// </summary>
    private void OnDisplayTopologyChanged()
    {
        // A modal of ours is pumping (scene editor, schedule rule, ...) or a scene-card menu
        // is open: rebuilding pages now would strand the dialog's/menu's closures on disposed
        // controls, so keep re-arming until the interaction ends. Timer ticks are dispatched
        // by the modal pump too, which is exactly what lets this retry work.
        if ((Form.ActiveForm is { } af && af != this) ||
            (_pages.TryGetValue(MainTab.Scenarios, out var busy) && busy is ScenariosPage b && b.UiBusy))
        {
            _dispDebounce.Stop();
            _dispDebounce.Start();
            return;
        }
        _dispDebounce.Stop();
        if (_pages.TryGetValue(MainTab.Scenarios, out var p) && p is ScenariosPage s && s.RefreshTopologyChanged())
        {
            bool active = _active == MainTab.Scenarios && s.Visible;
            _host.Controls.Remove(s);
            _pages.Remove(MainTab.Scenarios);
            s.Dispose();
            var page = CreatePage(MainTab.Scenarios);
            _pages[MainTab.Scenarios] = page;
            _host.Controls.Add(page);
            // A quiet swap, not ShowTab: this runs off a background system event, so it must
            // not steal foreground (Activate) or replay tab-click side effects.
            if (active) { page.Visible = true; page.OnEnter(); page.BringToFront(); }
            else { page.Visible = false; ForceHandles(page); }
        }
        foreach (var q in _pages.Values) q.OnDisplayChanged();
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
        public bool Active;            // marks the icon whose page is currently shown (accent frame + glyph)
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
            // the accent stroke needs AA room INSIDE the control: a 1.4px pen on a rect only
            // 0.5px from the edge gets clipped at the bounds and looks stair-stepped
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Theme.Surface);
            var r = new RectangleF(1.2f, 1.2f, Width - 2.4f, Height - 2.4f);
            using (var path = Theme.RoundRect(r, 9))
            {
                using var b = new SolidBrush(Active ? Theme.AccentSoft : _hover ? Theme.Card : Theme.Surface);
                g.FillPath(b, path);
                using var pen = new Pen(Active ? Theme.Accent : Theme.Border, Active ? 1.4f : 1f);
                g.DrawPath(pen, path);
            }
            var gr = ClientRectangle; gr.Offset(GlyphDx, GlyphDy);
            // PUA glyphs (the tab icons) live in Segoe MDL2 Assets; Segoe UI Symbol shows them as boxes
            bool mdl2 = Glyph.Length > 0 && Glyph[0] >= '' && Glyph[0] <= '';
            using var gf = new Font(mdl2 ? "Segoe MDL2 Assets" : "Segoe UI Symbol", 14f);
            TextRenderer.DrawText(g, Glyph, gf, gr, Active ? Theme.Accent : Theme.Muted,
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
