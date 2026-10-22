using System.Drawing.Drawing2D;

namespace GhostDeck;

// =====================================================================
//  Models - the database of recognised firmware IDs (from Devices.All)
// =====================================================================
/// <summary>
/// Five sub-tabs over the database in effect (Devices.All), every one of them computed from it at
/// runtime so a model change (new entry, promotion, curve, RPM, credit, a downloaded database)
/// refreshes ALL of them in one go: Start (your laptop, numbers, growth, closest to tested,
/// recent changes), List (the table with filters and expandable rows), Features (coverage and a
/// dot matrix), Series (tested share per series) and Community (thanks and the timeline).
/// A fixed header (title, detected model, database line, sub-tabs, search) sits above one
/// scrolling view; the footer keeps the legend and the two actions. Every view is ONE drawn
/// surface (ModelsViews.cs), never a control per row.
/// </summary>
public sealed class ModelsPage : ThemedPage
{
    internal const int Pad = 28;
    public const int TabStart = 0, TabList = 1, TabFeatures = 2, TabSeries = 3, TabCommunity = 4;

    private DeviceProfile? _detected;
    private List<DeviceProfile> _all;
    private List<DeviceProfile> _rows;
    private ModelsStats? _stats;   // computed on first use (the page is built in the background, see ctor)
    private HashSet<DeviceProfile> _new = new();
    private static HashSet<string>? _seenAtStart;   // the "seen" list as it was when this app run started

    private readonly SubTabs _subTabs;
    private readonly Panel _scrollHost = new() { AutoScroll = true };
    private readonly ModelsView[] _views;
    private int _tab = TabStart;
    private readonly SearchBox _searchBox = new();
    private readonly TextBox _search;
    private readonly Button _verify = new();
    private readonly Button _dbCheck = new();
    private readonly Panel _footer = new();
    private readonly ToolTip _tip = new() { InitialDelay = 250, AutoPopDelay = 8000 };

    internal readonly HashSet<string> Filters = new();   // List chips: t e g1 g2 c r a n
    internal DeviceProfile? Expanded;                    // List row opened for details

    public ModelsPage(MainDeps d) : base(d)
    {
        AutoScroll = false;   // the inner _scrollHost scrolls the view; the header stays fixed
        _detected = Devices.Detect(d.Firmware());
        _all = BuildCatalogue();
        _rows = _all;
        UpdateNew();
        // The statistics are the expensive part of building this page (first call includes .NET
        // warm-up): MainForm pre-builds the page in the background, so they follow in a message of
        // their own once the window exists, and no single step blocks the UI thread for long.
        HandleCreated += (_, _) => BeginInvoke(() => { _ = Stats; });

        _subTabs = new SubTabs(TabLabels(), new[] { "\uE80F", "\uE8FD", "\uE8A9", "\uE81E", "\uE716" });
        _subTabs.Changed += i => ShowTab(i);
        Controls.Add(_subTabs);

        _views = new ModelsView[]
        {
            new ModelsStartView(this), new ModelsListView(this), new ModelsFeaturesView(this),
            new ModelsSeriesView(this), new ModelsCommunityView(this),
        };
        foreach (var v in _views) { v.Visible = false; _scrollHost.Controls.Add(v); }
        _views[_tab].Visible = true;
        Controls.Add(_scrollHost);

        _search = _searchBox.Box;
        _searchBox.Width = 320;
        _search.PlaceholderText = Lang.T("mdl_search");
        _search.TextChanged += (_, _) =>
        {
            // typing anywhere means "find a model": the List shows the result (Features filters too)
            if (_search.Text.Length > 0 && _tab != TabList && _tab != TabFeatures) ShowTab(TabList);
            ApplyFilter();
        };
        Controls.Add(_searchBox);
        _searchBox.BringToFront();

        // Bottom footer: legend of the current sub-tab on the left, the two actions on the right.
        _footer.Paint += PaintFooter;
        Controls.Add(_footer);
        Ui.StylePrimary(_verify);
        _verify.Text = Lang.T("models_verify_btn");
        _verify.AutoSize = false;
        _verify.Click += (_, _) => Verify();
        _tip.SetToolTip(_verify, Lang.T("models_verify_desc"));
        _footer.Controls.Add(_verify);

        Ui.StyleGhost(_dbCheck);
        _dbCheck.Text = Lang.T("modeldb_check");
        _dbCheck.AutoSize = false;
        _dbCheck.Click += (_, _) =>
        {
            _dbCheck.Enabled = false;
            _dbCheck.Text = Lang.T("modeldb_checking");
            LayoutBits();
            D.CheckModelDbNow(code =>
            {
                if (IsDisposed || _dbCheck.IsDisposed) return;
                void Show()
                {
                    _dbCheck.Enabled = true;
                    _dbCheck.Text = Lang.T("modeldb_check");
                    _tip.SetToolTip(_dbCheck, code switch
                    {
                        > 0 => string.Format(Lang.T("modeldb_applied"), code),
                        -1  => Lang.T("modeldb_failed"),
                        -2  => Lang.T("modeldb_deferred"),
                        _   => Lang.T("modeldb_current"),
                    });
                    LayoutBits();
                }
                if (InvokeRequired) BeginInvoke(Show); else Show();
            });
        };
        _footer.Controls.Add(_dbCheck);

        ClientSizeChanged += (_, _) => LayoutBits();
    }

    private static string[] TabLabels() => new[]
    {
        Lang.T("subtab_start"), Lang.T("mdl_tab_list"), Lang.T("mdl_tab_features"),
        Lang.T("mdl_tab_series"), Lang.T("mdl_tab_community"),
    };

    // ---- state the views read ----
    internal DeviceProfile? Detected => _detected;
    internal IReadOnlyList<DeviceProfile> All => _all;
    internal IReadOnlyList<DeviceProfile> Rows => _rows;
    internal ModelsStats Stats => _stats ??= ModelsStats.Build(_all);
    internal bool IsNew(DeviceProfile d) => _new.Contains(d);
    internal int NewCount => _new.Count;
    internal string Firmware => D.Firmware();
    internal ToolTip Tip => _tip;

    internal void Verify() => (FindForm() as MainForm)?.ShowReport(0);

    internal static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static List<DeviceProfile> BuildCatalogue() =>
        Devices.All
            .OrderBy(x => x.Tier == Tier.Tested ? 0 : (x.ShiftMode == 0xF2 ? 2 : 1))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    // "New since your last visit": the list of first-prefix + tier keys seen on the previous run.
    // The snapshot is taken once per app run, so the badges stay for the whole session and are
    // gone on the next one; the very first run marks nothing (no history to compare with).
    private static string Key(DeviceProfile d) => d.FirmwarePrefixes[0] + "|" + (d.Tier == Tier.Tested ? "T" : "E");
    private void UpdateNew()
    {
        var keys = _all.Select(Key).ToList();
        if (_seenAtStart == null)
        {
            _seenAtStart = new HashSet<string>(D.Settings.ModelsSeen, StringComparer.OrdinalIgnoreCase);
            if (_seenAtStart.Count == 0) foreach (var k in keys) _seenAtStart.Add(k);
        }
        _new = _all.Where(d => !_seenAtStart.Contains(Key(d))).ToHashSet();
        if (!D.Settings.ModelsSeen.SequenceEqual(keys))
        {
            D.Settings.ModelsSeen = keys;
            D.SaveSettings();
        }
    }

    public override void OnEnter()
    {
        D.PollModelDb();               // debounced; the tab is where people look for this
        StyleSearch(); LayoutBits(); Invalidate(); _views[_tab].Invalidate();
        if (_tab == TabStart) (_views[TabStart] as ModelsStartView)?.Animate();
    }

    public override void OnReenter() => ShowTab(TabStart);

    /// <summary>The database changed (download / button): rebuild EVERYTHING the tab shows.</summary>
    public override void OnDeviceDbChanged()
    {
        _detected = Devices.Detect(D.Firmware());
        _all = BuildCatalogue();
        _stats = null;                 // rebuilt on the next paint
        UpdateNew();
        if (Expanded != null) Expanded = _all.FirstOrDefault(m => m.FirmwarePrefixes[0] == Expanded.FirmwarePrefixes[0]);
        ApplyFilter();                 // keeps whatever is typed in the search box
        LayoutBits(); Invalidate();
    }

    public override void OnLanguageChanged()
    {
        _subTabs.SetLabels(TabLabels());
        _stats = null;                 // candidate reasons are text: rebuilt on the next paint
        _search.PlaceholderText = Lang.T("mdl_search");
        _verify.Text = Lang.T("models_verify_btn");
        _dbCheck.Text = Lang.T("modeldb_check");
        LayoutBits(); Invalidate();
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _scrollHost.BackColor = Theme.Surface;
        foreach (var v in _views) v.BackColor = Theme.Surface;
        _footer.BackColor = Theme.Surface;
        StyleSearch();
        Ui.StylePrimary(_verify);
        Invalidate(); _views[_tab].Invalidate(); _footer.Invalidate(); _subTabs.Invalidate();
    }

    private void StyleSearch() => _searchBox.ApplyTheme();

    // ---- navigation ----
    internal int Tab => _tab;

    internal void ShowTab(int tab)
    {
        tab = Math.Clamp(tab, 0, _views.Length - 1);
        if (tab == _tab) { if (_subTabs.Active != tab) _subTabs.SetActive(tab); return; }
        // one repaint for the switch: hide the old view and show the new one inside a layout batch
        _scrollHost.SuspendLayout();
        _views[_tab].Visible = false;
        _tab = tab;
        _subTabs.SetActive(tab);
        _views[_tab].Visible = true;
        _scrollHost.AutoScrollPosition = Point.Empty;
        LayoutContent();
        _scrollHost.ResumeLayout(true);
        _footer.Invalidate();
        if (tab == TabStart) (_views[TabStart] as ModelsStartView)?.Animate();
    }

    /// <summary>Open the List with the search box set (series cards, candidates, tiles).</summary>
    internal void ShowListFor(string query, DeviceProfile? expand = null, string? filter = null)
    {
        Filters.Clear();
        if (filter != null) Filters.Add(filter);
        Expanded = expand;
        if (_search.Text != query) _search.Text = query;   // TextChanged applies the filter
        else ApplyFilter();
        ShowTab(TabList);
    }

    internal void ToggleFilter(string f)
    {
        if (!Filters.Remove(f)) Filters.Add(f);
        // tested/experimental and G1/G2 are pairs: picking one side drops the other
        foreach (var (a, b) in new[] { ("t", "e"), ("g1", "g2") })
            if (f == a) Filters.Remove(b); else if (f == b) Filters.Remove(a);
        ApplyFilter();
    }

    internal void ToggleExpanded(DeviceProfile m)
    {
        Expanded = ReferenceEquals(Expanded, m) ? null : m;
        LayoutContent();
        _views[_tab].Invalidate();
    }

    private void ApplyFilter()
    {
        string q = _search.Text.Trim();
        IEnumerable<DeviceProfile> r = _all;
        if (q.Length > 0)
            r = r.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                             x.FirmwarePrefixes.Any(f => f.Contains(q, StringComparison.OrdinalIgnoreCase)));
        foreach (var f in Filters)
            r = f switch
            {
                "t"  => r.Where(x => x.Tier == Tier.Tested),
                "e"  => r.Where(x => x.Tier != Tier.Tested),
                "g1" => r.Where(x => x.ShiftMode == 0xF2),
                "g2" => r.Where(x => x.ShiftMode != 0xF2),
                "c"  => r.Where(x => x.FanCurve != null),
                "r"  => r.Where(ModelsStats.HasRpm),
                "a"  => r.Where(x => x.FourthMode != null),
                "n"  => r.Where(IsNew),
                _    => r,
            };
        _rows = r.ToList();
        LayoutContent();
        _views[_tab].Invalidate();
    }

    // ---- fonts / metrics ----
    private static readonly Font FTitle = Dpi.Font("Segoe UI", 18f, FontStyle.Bold);
    private static readonly Font FSub = Dpi.Font("Segoe UI", 11f, FontStyle.Bold);
    private static readonly Font FIntro = Dpi.Font("Segoe UI", 10f);
    internal static readonly Font FHead = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
    internal static readonly Font FCell = Dpi.Font("Segoe UI", 10.5f);
    internal static readonly Font FMono = Dpi.Font("Consolas", 10.5f);
    internal static readonly Font FLegend = Dpi.Font("Segoe UI", 9f);
    internal static readonly Font FSmall = Dpi.Font("Segoe UI", 9f);
    internal static readonly Font FCardTitle = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
    internal static readonly Font FName = Dpi.Font("Segoe UI", 12f, FontStyle.Bold);
    internal static readonly Font FBig = Dpi.Font("Segoe UI", 18f, FontStyle.Bold);
    internal static readonly Font FBold = Dpi.Font("Segoe UI", 10.5f, FontStyle.Bold);
    internal static readonly Font FGlyph = Dpi.Font("Segoe MDL2 Assets", 10.5f);

    private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);
    private int SubTop() => 24 + FTitle.Height + 12 + FSub.Height + 6 + FIntro.Height + 14;
    private int HeaderBandH() => SubTop() + _subTabs.Height + S(14);

    private void LayoutBits()
    {
        _searchBox.Height = _searchBox.PreferredHeight;
        _searchBox.Location = new Point(Math.Max(Pad, ClientSize.Width - Pad - _searchBox.Width), 22);
        _subTabs.SetBounds(Pad, SubTop(), _subTabs.FitTo(ClientSize.Width - Pad * 2), _subTabs.Height);

        int footerH = Math.Max(66, FLegend.Height * 3 + 26);
        _footer.SetBounds(0, ClientSize.Height - footerH, ClientSize.Width, footerH);
        int vw = TextRenderer.MeasureText(_verify.Text, _verify.Font).Width + 44, vh = 42;
        _verify.SetBounds(_footer.Width - Pad - vw, (footerH - vh) / 2, vw, vh);
        int dw = TextRenderer.MeasureText(_dbCheck.Text, _dbCheck.Font).Width + 32;
        _dbCheck.SetBounds(_verify.Left - 12 - dw, (footerH - vh) / 2, dw, vh);

        int hb = HeaderBandH();
        _scrollHost.SetBounds(0, hb, ClientSize.Width, Math.Max(0, ClientSize.Height - hb - footerH));
        _footer.Invalidate();
        LayoutContent();
    }

    /// <summary>Size the visible view to its content (the host scrolls it).</summary>
    internal void LayoutContent()
    {
        // The view's width is decided BEFORE the vertical scrollbar shows up: measure at the full
        // width, and when the content is taller than the host, lay out again at the width that is
        // left next to the bar. Sizing the child after the bar appeared left the panel with a
        // horizontal bar it never recomputed (same trap as ThemedPage.LayoutAndSyncScroll).
        var v = _views[_tab];
        int full = _scrollHost.Width, hostH = _scrollHost.Height;
        int h = v.ContentHeight(full);
        int w = full;
        if (h > hostH)
        {
            w = full - SystemInformation.VerticalScrollBarWidth;
            h = v.ContentHeight(w);
        }
        v.SetBounds(v.Left, v.Top, w, Math.Max(h, hostH));
        _scrollHost.PerformLayout();   // the panel recomputes its bars for the new child size
    }

    private void PaintFooter(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, 0, _footer.Width, 0);
        int legendRight = _dbCheck.Left - 20;
        string legend = _tab switch
        {
            TabList => Lang.T("mdl_legend"),
            TabFeatures => Lang.T("mdl_legend_features"),
            TabSeries => Lang.T("mdl_legend_series"),
            TabCommunity => Lang.T("mdl_legend_community"),
            _ => Lang.T("mdl_legend_start"),
        };
        TextRenderer.DrawText(g, legend, FLegend,
            new Rectangle(Pad, 10, Math.Max(60, legendRight - Pad), _footer.Height - 16), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int avail = ClientSize.Width - Pad * 2;
        const TextFormatFlags One = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        TextRenderer.DrawText(g, Lang.T("tab_models"), FTitle, new Point(Pad, 24), Theme.Text);

        int y = 24 + FTitle.Height + 12;
        int lineW = avail - _searchBox.Width - 20;
        if (_detected is { } det)
        {
            string sub = Lang.T("mdl_you") + ":  " + det.Name + "   ·   " + D.Firmware();
            TextRenderer.DrawText(g, sub, FSub, new Rectangle(Pad, y, lineW, FSub.Height), Theme.Text, One);
        }
        else
        {
            string sub = string.Format(Lang.T("mdl_unknown"), string.IsNullOrEmpty(D.Firmware()) ? "?" : D.Firmware());
            TextRenderer.DrawText(g, sub, FSub, new Rectangle(Pad, y, avail, FSub.Height), Theme.Amber, One);
        }
        y += FSub.Height + 6;
        // database line: version and what changed since the last visit
        string db = string.Format(Lang.T("mdl_db_line"), Devices.EffectiveDataVersion, Stats.Total, Stats.Tested);
        if (_new.Count > 0) db += "   ·   " + string.Format(Lang.T("mdl_new_since"), _new.Count);
        TextRenderer.DrawText(g, db, FIntro, new Rectangle(Pad, y, avail, FIntro.Height), _new.Count > 0 ? Theme.Accent : Theme.Muted, One);
    }

    protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }

    /// <summary>
    /// The search field: a borderless TextBox inside a drawn frame (app card colour, rounded,
    /// accent border while focused) with a magnifier and inner padding - the plain FixedSingle
    /// box put the text right against its border.
    /// </summary>
    private sealed class SearchBox : Control
    {
        public readonly TextBox Box = new() { BorderStyle = BorderStyle.None, Font = Dpi.Font("Segoe UI", 10.5f) };
        private static readonly Font Glyph = Dpi.Font("Segoe MDL2 Assets", 10.5f);
        private static readonly Font ClearGlyph = Dpi.Font("Segoe MDL2 Assets", 8.5f);

        public SearchBox()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.IBeam;
            Controls.Add(Box);
            Box.GotFocus += (_, _) => Invalidate();
            Box.LostFocus += (_, _) => Invalidate();
            Box.TextChanged += (_, _) => { PerformLayout(); Invalidate(); };   // the X comes and goes
            ApplyTheme();
        }

        private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);
        public int PreferredHeight => Box.PreferredHeight + S(16);

        public void ApplyTheme() { Box.BackColor = Theme.Card; Box.ForeColor = Theme.Text; BackColor = Theme.Surface; Invalidate(); }

        private bool HasText => Box.TextLength > 0;
        private Rectangle ClearRect => new(Width - S(30), (Height - S(22)) / 2, S(22), S(22));
        private bool _overClear;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = HasText && ClearRect.Contains(e.Location);
            Cursor = over ? Cursors.Hand : Cursors.IBeam;
            if (over != _overClear) { _overClear = over; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_overClear) { _overClear = false; Invalidate(); } }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (HasText && ClearRect.Contains(e.Location)) Box.Clear();   // TextChanged re-filters
            Box.Focus();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int left = S(34), right = HasText ? S(36) : S(12);
            Box.SetBounds(left, (Height - Box.Height) / 2, Math.Max(10, Width - left - right), Box.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            bool focus = Box.Focused;
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7))
            {
                using var b = new SolidBrush(Theme.Card); g.FillPath(b, path);
                using var pen = new Pen(focus ? Theme.Accent : Theme.Border, focus ? 1.5f : 1f); g.DrawPath(pen, path);
            }
            Ui.CenterGlyph(g, "\uE721", Glyph, focus ? Theme.Accent : Theme.Muted, new RectangleF(S(8), 0, S(20), Height));
            if (HasText)
            {
                var cr = ClearRect;
                if (_overClear)
                {
                    using var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 30 : 20, Theme.Text));
                    g.FillEllipse(b, cr);
                }
                Ui.CenterGlyph(g, "\uE711", ClearGlyph, _overClear ? Theme.Text : Theme.Muted, cr);
            }
        }
    }
}
