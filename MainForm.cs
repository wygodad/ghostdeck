using System.Drawing.Drawing2D;

namespace MSIProfileSwitcher;

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
    private readonly Label _version = new();
    private MainTab _active = MainTab.Scenarios;

    public MainForm(MainDeps d)
    {
        _d = d;
        Text = "MSI Profile Switcher";
        Icon = TrayIconFactory.Create(Theme.Accent);
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
        };

        AddTab(MainTab.Scenarios, Lang.T("tab_scenarios"), "");
        AddTab(MainTab.Status,    Lang.T("menu_status"),   "");
        AddTab(MainTab.FanCurve, Lang.T("tab_fancurve"),"");
        AddTab(MainTab.Settings,  Lang.T("menu_settings"), "");
        AddTab(MainTab.Models,    Lang.T("tab_models"),   "\U0001F4BB");
        AddTab(MainTab.Report,    Lang.T("tab_report"),   "");
        AddTab(MainTab.Updates,   Lang.T("tab_updates"),   "");

        _version.AutoSize = true;
        _version.Font = new Font("Segoe UI", 9.5f);
        _strip.Controls.Add(_version);

        _themeBtn.Size = new Size(40, 38);
        _themeBtn.Glyph = Theme.Dark ? "☀" : "☾";
        _themeBtn.Click += (_, _) => { Theme.Toggle(); _d.Settings.DarkMode = Theme.Dark; _d.SaveSettings(); };
        _strip.Controls.Add(_themeBtn);

        _strip.Resize += (_, _) => { LayoutStrip(); _strip.Invalidate(); };
        LayoutStrip();
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
        int x = 20, y = 12, h = 44;
        var f = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        foreach (var b in _tabs)
        {
            int w = TextRenderer.MeasureText(b.Text, f).Width + 30 + 30; // text + icon + padding
            b.SetBounds(x, y, w, h);
            x += w + 8;
        }
        _themeBtn.Location = new Point(_strip.Width - _themeBtn.Width - 18, (StripH - _themeBtn.Height) / 2);
        _version.Text = "v" + _d.AppVersion();
        _version.Location = new Point(_themeBtn.Left - _version.Width - 14, (StripH - _version.Height) / 2);
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
        Icon = TrayIconFactory.Create(Theme.Accent);
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
            TextRenderer.DrawText(g, Glyph, iconFont, new Rectangle(sx, 0, iconW, Height), col,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, Text, textFont, new Rectangle(sx + iconW + gap, 0, tw + 8, Height), col,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    private sealed class GlyphButton : Control
    {
        public string Glyph = "";
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
            TextRenderer.DrawText(g, Glyph, new Font("Segoe UI Symbol", 14f), ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
