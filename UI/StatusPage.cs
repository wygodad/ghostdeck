using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

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
        ("st_firmware",  (s, h) => string.IsNullOrEmpty(h.Firmware) ? "-" : h.Firmware, true),
        ("st_charge",    (s, h) => h.ChargeLimit is >= 10 and <= 100 ? $"{h.ChargeLimit} %" : "-", false),
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
        return "-";
    }

    private readonly Button _test = new();
    private readonly Button _logBtn = new();
    private DeviceProfile? _dev;   // re-pointed by OnDeviceDbChanged
    private byte[] _live = Array.Empty<byte>();    // [shift, 0x34, 0xEB, fan]
    private (int[] ct, int[] cs, int[] gt, int[] gs)? _curve;
    private HwSnapshot _hw;                        // last EC/hw snapshot, refreshed off the UI thread
    private int _refreshing;                       // 1 while a background refresh is in flight

    private readonly Canvas _canvas;

    // Sub-tabs split the (heavy) Status page into shorter views: charts, history, gaming, EC bytes, change log.
    // Sub-tab glyphs follow the Settings strip convention (Segoe MDL2 Assets, see SubTabs).
    private readonly SubTabs _statusTabs = new(
        new[] { Lang.T("st_sub_charts"), Lang.T("st_sub_history"), Lang.T("st_sub_gaming"), Lang.T("st_sub_bytes"), Lang.T("st_sub_log") },
        new[] { "\uE9D2", "\uE81C", "\uE7FC", "\uE943", "\uEA37" });
    private int _statusSub;
    private readonly SegControl _histRange = new(new[] { "5 min", "15 min", "30 min", "60 min" }, 1) { Size = new Size(300, 30), Visible = false };   // dpi-ok: SegControl's MinimumSize grows it to its labels
    private readonly Button _histExport = new() { Visible = false };
    private readonly ThemedComboBox _sessPick = new() { Visible = false, Width = 280 };   // saved game sessions, newest first
    private readonly Button _sessExport = new() { Visible = false };
    private List<GameSession> _sessList = new();
    private static readonly int[] HistMins = { 5, 15, 30, 60 };
    private int _histMin = 15;
    private const int SubY = 90;      // sub-tab bar Y (clear gap under the title)
    private const int SecTop = 168;   // content starts below the title + sub-tab bar (clear gap under it)

    public StatusPage(MainDeps d) : base(d)
    {
        _dev = Devices.Detect(d.Firmware());
        _canvas = new Canvas(this) { Location = new Point(0, 0) };
        Controls.Add(_canvas);

        _statusTabs.Location = new Point(Pad, SubY);
        _statusTabs.Changed += i => { _statusSub = i; _logBtn.Visible = i == 4; _histRange.Visible = _histExport.Visible = i == 1; _sessPick.Visible = _sessExport.Visible = i == 2; if (i == 2) RefreshSessions(); UpdateFpsViewer(); Relayout(); _canvas.Rebuild(); };
        _canvas.Controls.Add(_statusTabs);

        // History range picker (5-60 min) lives on the canvas, shown only on the History sub-tab.
        _histRange.SelectedChanged += i => { _histMin = HistMins[i]; _canvas.Rebuild(); };
        _canvas.Controls.Add(_histRange);

        // Export the visible history window to CSV/JSON (for external analysis).
        Ui.StyleGhost(_histExport);
        _histExport.Text = Lang.T("st_hist_export");
        _histExport.Click += (_, _) => ExportHistory();
        _canvas.Controls.Add(_histExport);

        // Gaming: saved-session picker + per-session export (JSON/CSV).
        _sessPick.SelectedIndexChanged += (_, _) => _canvas.Rebuild();
        _canvas.Controls.Add(_sessPick);
        Ui.StyleGhost(_sessExport);
        _sessExport.Text = Lang.T("st_hist_export");
        _sessExport.Click += (_, _) =>
        {
            if (SelectedSession() is { } s)
            {
                try { GameSessions.ExportWithDialog(s, FindForm()); }
                catch (Exception ex) { MessageBox.Show(FindForm(), string.Format(Lang.T("bk_err"), ex.Message), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        };
        _canvas.Controls.Add(_sessExport);

        // "Full log…" button lives on the canvas so it scrolls with the recent-changes section.
        _logBtn.Text = Lang.T("log_full");
        _logBtn.AutoSize = false;
        _logBtn.Visible = false;   // only on the change-log sub-tab
        Ui.StyleGhost(_logBtn);
        _logBtn.Click += (_, _) => LogForm.ShowSingleton();
        _canvas.Controls.Add(_logBtn);

        _timer.Tick += (_, _) => RefreshAsync();
        // Hidden page: stop the timer AND drop the offscreen buffer. It is sized to the whole
        // scrollable content (measured 6.4 MB at 1600x980) and, since pages are never disposed,
        // it would otherwise stay allocated for the rest of the session. OnPaint re-renders when
        // the buffer is null, so re-opening costs one render.
        VisibleChanged += (_, _) =>
        {
            if (Visible) { Relayout(); _timer.Start(); }
            else { _timer.Stop(); _canvas.ReleaseBuffer(); }
            UpdateFpsViewer();
        };
        ClientSizeChanged += (_, _) => Relayout();
        ChangeLog.Changed += OnLogChanged;

        // Test/discovery tools are now hidden; opened via Ctrl+Shift+T (see MainForm / docs/TECHNICAL.md §12).
        _test.Visible = false;
    }

    private void OnLogChanged() { if (!IsDisposed && Visible) { try { BeginInvoke(() => { Relayout(); _canvas.Rebuild(); }); } catch { } } }

    // The ETW-based FPS monitor runs only while someone is looking; the Gaming sub-tab counts
    // as a viewer, so entering it starts the monitor and leaving it lets the tray turn it off.
    private void UpdateFpsViewer() { try { D.SetFpsViewer(Visible && _statusSub == 2); } catch { } }

    /// <summary>Deep-link: jump straight to the Gaming sub-tab (used by the session-report popup).</summary>
    public void ShowGaming() => _statusTabs.SetActive(2, raise: true);

    private GameSession? SelectedSession()
    {
        int i = _sessPick.SelectedIndex;
        if (i >= 0 && i < _sessList.Count) return _sessList[i];
        return FpsMonitor.LastSession;
    }

    // Rebuild the session-picker items when the store changed (new session / trimmed list);
    // keeps the current pick when it still exists, otherwise selects the newest.
    private void RefreshSessions()
    {
        var list = GameSessions.All().ToList();
        bool same = list.Count == _sessList.Count &&
                    (list.Count == 0 || ReferenceEquals(list[0], _sessList[0]));
        if (same) return;
        var prev = SelectedSession();
        _sessList = list;
        if (_sessPick.DroppedDown) return;   // don't yank an open dropdown; next refresh catches up
        _sessPick.BeginUpdate();
        _sessPick.Items.Clear();
        foreach (var s in list) _sessPick.Items.Add($"{s.Process} · {s.End:MM-dd HH:mm}");
        int sel = prev == null ? -1 : list.FindIndex(x => x.Process == prev.Process && x.End == prev.End);
        if (_sessPick.Items.Count > 0) _sessPick.SelectedIndex = sel >= 0 ? sel : 0;
        _sessPick.EndUpdate();
        _sessExport.Enabled = list.Count > 0;
    }

    private int _laidDisks;   // disk count the Charts view was laid out for

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
            Perf.Warm();   // disks, counters, battery estimate: queried here, the window thread reads the cached answers
            if (dev != null)
            {
                try
                {
                    live = Ec.OnBackupPath
                        ? new[] { Ec.ReadByte(dev.ShiftMode), (byte)0, (byte)0, Ec.ReadByte(dev.FanMode) }   // 0x34 / 0xEB have no slot there
                        : Ec.ReadMany(new[] { dev.ShiftMode, (byte)0x34, (byte)0xEB, dev.FanMode });
                }
                catch { }
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
                    // Gaming: the last-session card / picker can change between ticks -> keep in sync
                    if (Visible && _statusSub == 2) { RefreshSessions(); Relayout(); }
                    if (Visible && _statusSub == 0 && Perf.DisksCached().Count != _laidDisks) Relayout();   // the storage box grew or shrank
                    if (Visible) _canvas.Rebuild();
                });
            }
            catch { _refreshing = 0; }   // page disposed mid-flight
        });
    }

    private void PlaceTest() { }

    private const int RingCount = 5;
    private const int RingTop = 92, RowH = 56;

    // #172: row heights of the charts sub-tab follow the font. The literals are what fits up to
    // about 150 % scaling and stay the floor, so the page looks the same there; above that the
    // text lines, the bars under them and the boxes grow with the text instead of cutting it.
    private static readonly Font RowNameF = Dpi.Font("Segoe UI", 10.5f, FontStyle.Bold);
    private static readonly Font RowValF = Dpi.Font("Segoe UI", 10.5f);
    private static readonly Font MetricF = Dpi.Font("Segoe UI", 11.5f, FontStyle.Bold);
    private static readonly Font RingLabelF = Dpi.Font("Segoe UI", 10f);
    private static int LineH => Math.Max(28, RowNameF.Height + 2);          // RAM / VRAM / disk text line
    private static int BarOff => LineH + 8;                                 // its bar, under the line
    private static int DiskBlockH => BarOff + 14 + 8;                       // one disk: line + bar + air
    private static int SubH => Math.Max(54, Math.Max(MetricF.Height + 16, BarOff + 14 + 4));
    private static int RingBelow => Math.Max(68, 12 + RingLabelF.Height + 4 + 16);   // ring label + air

    private int RamW(int width)
    {
        int ring = RingSize(width), avail = width - Pad * 2;
        return ring * 2 + Math.Max(RingGap(), (avail - RingCount * ring) / (RingCount - 1));
    }

    // Disk row: the name on the left, "used / total GB · temp" on the right. When both do not fit
    // on one line the values move to a line of their own instead of cutting the name.
    private static string DiskValues(Perf.DiskInfo d)
    {
        string cap = d.VolGb > 0 ? $"{d.UsedGb:0} / {d.SizeGb:0} GB" :
                     d.SizeGb >= 1000 ? $"{d.SizeGb / 1000:0.0} TB" : $"{d.SizeGb:0} GB";
        return cap + " · " + (d.TempC > 0 ? $"{d.TempC} °C" : "-");
    }
    private static bool DiskTwoLines(Perf.DiskInfo d, int ramW) =>
        TextRenderer.MeasureText(d.Name, RowNameF).Width + 10 + TextRenderer.MeasureText(DiskValues(d), RowValF).Width + 6 > ramW;
    private static int StorageH(IReadOnlyList<Perf.DiskInfo> disks, int ramW)
    {
        int h = 0;
        foreach (var d in disks) h += DiskBlockH + (DiskTwoLines(d, ramW) ? LineH : 0);
        return Math.Max(SubH, h);
    }
    private static readonly Color CpuUseColor = Color.FromArgb(0x3C, 0x7D, 0xFF);   // blue: CPU side of the palette (fans = cyan/violet)
    // #172: the ring grid is the base the metric boxes are cut from, so its bounds grow with the
    // scale; with fixed 150-240 px rings the boxes under them stayed 100 % wide while their text
    // doubled at 200 %, and each label shrank to its own size to fit
    private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);
    private int RingGap() => S(24);
    private int RingSize(int width)
    {
        int avail = width - Pad * 2, gap = RingGap();
        return Math.Max(S(150), Math.Min(S(240), (avail - gap * (RingCount - 1)) / RingCount));
    }

    // Wrapped so the scroll extent is recomputed after the canvas is resized: without it a
    // horizontal scrollbar survived a maximize/restore even though nothing overhangs any more
    // (ThemedPage.LayoutAndSyncScroll).
    private void Relayout() => LayoutAndSyncScroll(RelayoutPass);

    private void RelayoutPass()
    {
        if (_canvas == null) return;
        _statusTabs.Size = new Size(_statusTabs.FitTo(ClientSize.Width - 24), _statusTabs.Height);
        _canvas.Width = ClientSize.Width;
        _canvas.Height = Math.Max(SectionHeight(_canvas.Width, _statusSub), ClientSize.Height);
    }

    private static int GridH(bool title, int rows, int headerLines = 1) =>
        (title ? GTitle.Height + 12 : 0) + (GHead.Height * headerLines + 14) + rows * (GCell.Height + 14) + 8;

    // Height of the active sub-tab's content (0 = charts, 1 = history, 2 = gaming, 3 = EC bytes, 4 = change log).
    private int SectionHeight(int width, int sub)
    {
        if (sub == 0)
        {
            int ring = RingSize(width);
            // rings, then three stacked 54px rows (RPM/clock, battery/GPU load/VRAM, storage), and
            // the graphics-clock tile adds a fourth on the right when Windows reports a card
            int subY3 = SecTop + ring + RingBelow + SubH + 14 + SubH + 14;
            int rightH = GpuTelemetry.Read().Ok ? SubH * 2 + 14 : SubH;
            var disks = Perf.DisksCached();
            _laidDisks = disks.Count;   // RefreshAsync re-lays the page out when the list arrives or changes
            int storageH = StorageH(disks, RamW(width));
            int cardTop = subY3 + Math.Max(storageH, rightH) + 40;
            return cardTop + RowH * Rows.Length + 14 + 40;
        }
        if (sub == 1)
        {
            // RPM chart only once a sample carried a tach reading; FPS chart is always there
            // (with a hint how to fill it) so nobody has to discover it by accident
            int charts = 3 + (HwHistory.HasRpm ? 1 : 0);
            return SecTop + 44 + charts * HistChartH + (charts - 1) * 24 + 40;
        }
        if (sub == 2)
        {
            int sess = SelectedSession() != null
                ? GTitle.Height + 12 + GmSessRows * GmRowH + 14
                : GTitle.Height + 12 + 56;
            return SecTop + GmHeadH + 10 + GmBoxH + 24 + GmChartH + 24 + sess + 40;
        }
        if (sub == 3)
        {
            int h = SecTop + GridH(true, 5, 2) + NoteH(width - Pad * 2) + 8 + GridH(false, 4);
            if (_dev?.FanCurve is { } fc) h += 16 + GridH(true, fc.Points);
            return h + 40;
        }
        return SecTop + 16 + GridH(true, RecentLogRows) + 40;
    }

    private const int RecentLogRows = 16;

    // Paint the cached snapshot immediately (instant tab switch) and refresh in the background.
    /// <summary>Clicking the Status tab while already on it goes back to the first sub-tab.</summary>
    public override void OnReenter() => _statusTabs.SetActive(0, raise: true);

    public override void OnEnter() { _logBtn.Text = Lang.T("log_full"); _logBtn.Visible = _statusSub == 4; _histRange.Visible = _histExport.Visible = _statusSub == 1; _sessPick.Visible = _sessExport.Visible = _statusSub == 2; if (_statusSub == 2) RefreshSessions(); UpdateFpsViewer(); Relayout(); _canvas.Rebuild(); RefreshAsync(); }
    public override void ApplyTheme() { base.ApplyTheme(); if (_canvas != null) { _canvas.BackColor = Theme.Surface; Ui.StyleGhost(_logBtn); _statusTabs.Invalidate(); _canvas.Rebuild(); } }
    public override void OnLanguageChanged()
    {
        _statusTabs.SetLabels(new[]
        {
            Lang.T("st_sub_charts"), Lang.T("st_sub_history"), Lang.T("st_sub_gaming"),
            Lang.T("st_sub_bytes"), Lang.T("st_sub_log"),
        });
        _histExport.Text = Lang.T("st_hist_export");
        _sessExport.Text = Lang.T("st_hist_export");
        _canvas.Rebuild();
    }

    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); ChangeLog.Changed -= OnLogChanged; UpdateFpsViewer(); } base.Dispose(disposing); }

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
            // OptimizedDoubleBuffer: OnPaint composes buffer-blit + cursor overlay offscreen and
            // presents once - without it the overlay text visibly flickered on every mouse move.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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
            _p.DrawHistCursor(e.Graphics);   // cheap per-paint overlay (crosshair on the history charts)
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _p.HistMouse(e.Location);
            _p.ChartsMouse(this, e.Location);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _p.HistMouse(null);
            _p.ChartsMouse(this, null);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) _p.ChartsClick(e.Location);
        }

        /// <summary>Free the offscreen buffer (the page is hidden); OnPaint rebuilds it on demand.</summary>
        internal void ReleaseBuffer()
        {
            _buf?.Dispose();
            _buf = null;
            _bufW = _bufH = 0;
        }

        protected override void Dispose(bool disposing) { if (disposing) _buf?.Dispose(); base.Dispose(disposing); }
    }

    internal void Render(Graphics g, int width)
    {
        var info = D.Status();
        var hw = _hw;   // cached; refreshed off the UI thread by RefreshAsync

        TextRenderer.DrawText(g, Lang.T("menu_status"), Dpi.Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);
        // (the tier badge lives in the header strip now, next to the version)

        int avail = width - Pad * 2;
        if (_statusSub == 1) { RenderHistory(g, avail); return; }
        if (_statusSub == 2) { RenderGaming(g, avail); return; }
        if (_statusSub == 3) { RenderBytes(g, avail, info); return; }
        if (_statusSub == 4) { RenderLog(g, avail); return; }

        // ---- sub-tab 0: charts (rings + RAM + metric boxes + details card) ----
        int ring = RingSize(width);
        // justify the gauge row: spread the leftover width into the gaps so the five rings
        // (and the metric boxes that follow their columns) span the full content width
        int ringGap = Math.Max(RingGap(), (avail - RingCount * ring) / (RingCount - 1));
        int top = SecTop;
        int cpuUse = SysInfo.CpuUsage();
        var (ramPct, ramTot, ramUsed) = SysInfo.Ram();
        int X(int i) => Pad + i * (ring + ringGap);
        // temperature rings stay live in telemetry-only mode (#48); the fan rings do not
        bool temps = info.Known || info.Telemetry;
        DrawRing(g, X(0), top, ring, hw.CpuTemp, 100, "°C", Lang.T("st_cpu_temp"), TempColor(hw.CpuTemp), temps);
        DrawRing(g, X(1), top, ring, hw.GpuTemp, 100, "°C", Lang.T("st_gpu_temp"), TempColor(hw.GpuTemp), temps);
        // The fan rings show the curve target (0x71 / 0x89), not the fan speed, on MSI's own
        // 0-150 scale; 0 is a real target (the first curve point), so it reads "0", not a dash.
        // TECHNICAL §76.
        // While hardware readings are lost (Ec.ReadsLost) the zero fan targets are not real: dash them.
        bool fans = temps && !(info.Known && Ec.ReadsLost);
        DrawRing(g, X(2), top, ring, fans ? hw.CpuFan : 0, 150, "%", Lang.T("st_cpu_fan"), Theme.Accent, fans, allowZero: true);
        DrawRing(g, X(3), top, ring, fans ? hw.GpuFan : 0, 150, "%", Lang.T("st_gpu_fan"), Theme.Violet, fans, allowZero: true);
        int hd = S(22);   // help dot, grown with the scale like its "?"
        _fanHelpBtns[0] = new RectangleF(X(2) + ring - hd, top, hd, hd);
        _fanHelpBtns[1] = new RectangleF(X(3) + ring - hd, top, hd, hd);
        HelpDot.Render(g, _fanHelpBtns[0]);
        HelpDot.Render(g, _fanHelpBtns[1]);
        DrawRing(g, X(4), top, ring, cpuUse, 100, "%", Lang.T("st_cpu_usage"), CpuUseColor, true, allowZero: true);

        // --- sub-row under the rings (clear gap above and below) ---
        int subY = top + ring + RingBelow, subH = SubH, lineH = LineH;

        // RAM as a horizontal bar spanning the two temperature rings, with values; bar inset ~20px each side
        int ramX = X(0), ramW = ring * 2 + ringGap;
        TextRenderer.DrawText(g, Lang.T("st_ram"), RowNameF,
            new Rectangle(ramX, subY, ramW, lineH), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, ramTot > 0 ? $"{ramUsed:0.0} / {ramTot:0.0} GB · {ramPct}%" : "-", RowValF,
            new Rectangle(ramX, subY, ramW, lineH), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        DrawBar(g, new RectangleF(ramX + 20, subY + BarOff, ramW - 40, 14), ramPct / 100f, ramPct >= 90 ? Theme.Amber : Theme.Accent);

        // Framed metric counters (the RPM / clock / GPU / VRAM / battery boxes). The frames are
        // drawn here, the texts at the end with ONE font for all of them: the largest size at
        // which every text fits its box. Each box shrinking on its own made neighbours differ in
        // size ("CPU: -" large, "CPU: 2383 MHz" small next to it).
        var metricTexts = new List<(Rectangle r, string text)>();
        void MetricBox(RectangleF box, string text, int reserveRight = 0)
        {
            Ui.FillCard(g, box);
            var r = Rectangle.Round(box);
            r.Inflate(-(S(10) + reserveRight), 0);
            metricTexts.Add((r, text));
        }
        void RpmUnder(int i, int rpm, string label)
            => MetricBox(new RectangleF(X(i) + 14, subY, ring - 28, subH),
                !(info.Known || info.Telemetry) ? $"{label}: -" : rpm > 0 ? $"{label}: {rpm} RPM" : $"{label}: - RPM");
        RpmUnder(2, hw.CpuRpm, "CPU");
        RpmUnder(3, hw.GpuRpm, "GPU");
        // CPU clock (approx.) under the CPU-usage ring — same row as the RPM counters
        MetricBox(new RectangleF(X(4) + 14, subY, ring - 28, subH),
            Perf.CpuClockMhz() is int mhz and > 0 ? $"CPU: {mhz} MHz" : "CPU: - MHz");

        // second sub-row: battery, GPU load %, VRAM — same box width as the RPM counters, under rings 2/3/4
        int subY2 = subY + subH + 14;
        int gu = Perf.GpuUsage(), vm = Perf.VramUsedMb(), vt = Perf.VramTotalMb();
        bool vramBar = vt > 0 && vm >= 0;   // only meaningful as a bar when we know the total (discussion #9)
        void Box2(int i, string text) => MetricBox(new RectangleF(X(i) + 14, subY2, ring - 28, subH), text);
        Box2(2, $"{Lang.T("st_battery")}: {BatteryText()}");
        Box2(3, $"{Lang.T("ov_m_gpuusage")}: " + (gu >= 0 ? $"{gu} %" : "-"));
        // VRAM: shown as a bar under RAM when the total is known; otherwise fall back to the MB box.
        if (vramBar)
        {
            int vpct = (int)Math.Round(Math.Clamp(vm / (float)vt, 0f, 1f) * 100);
            TextRenderer.DrawText(g, Lang.T("ov_m_vram"), RowNameF,
                new Rectangle(ramX, subY2, ramW, lineH), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, $"{vm / 1024f:0.0} / {vt / 1024f:0.0} GB · {vpct}%", RowValF,
                new Rectangle(ramX, subY2, ramW, lineH), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            DrawBar(g, new RectangleF(ramX + 20, subY2 + BarOff, ramW - 40, 14), vm / (float)vt, vpct >= 90 ? Theme.Amber : Theme.AccentFill);
        }
        else
        {
            Box2(4, $"{Lang.T("ov_m_vram")}: " + (vm >= 0 ? $"{vm} MB" : "-"));
        }

        // Integrated card, two-card laptops only (roadmap #114): the free slot under the CPU-usage
        // ring, next to the GPU load, or one row down beside the graphics clock when VRAM took that
        // slot as a box. Its shared memory rides along only when it fits at a comfortable size.
        // "iGPU" stays untranslated, like the "CPU:" / "GPU:" boxes above it.
        int ig = Perf.IgpuUsage();
        void IgpuBox(float y)
        {
            var box = new RectangleF(X(4) + 14, y, ring - 28, subH);
            int sm = Perf.IgpuSharedMb();
            string shortText = $"iGPU: {ig} %";
            string full = sm > 0 ? $"{shortText} · {sm / 1024f:0.0} GB" : shortText;
            using var fit = Dpi.Font("Segoe UI", 10f, FontStyle.Bold);
            MetricBox(box, TextRenderer.MeasureText(g, full, fit).Width <= box.Width - 20 ? full : shortText);
        }
        if (ig >= 0 && vramBar) IgpuBox(subY2);

        // third sub-row: storage panel (#17) on the left - every physical disk with its name,
        // used/total space (usage bar, like the RAM/VRAM bars) and S.M.A.R.T. temperature -
        // plus a compact battery-time box (#15) under the rings
        int subY3 = subY2 + subH + 14;
        var disks = Perf.DisksCached();
        int storageH = StorageH(disks, ramW);
        if (disks.Count == 0)
        {
            TextRenderer.DrawText(g, $"{Lang.T("ov_m_ssd")}: -", RowNameF,
                new Rectangle(ramX, subY3, ramW, subH), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        else
        {
            int ly = subY3;
            foreach (var d in disks)
            {
                // right side: "1658 / 1908 GB · 37 °C" (used/total like MSI Center; temp amber when hot)
                string right = DiskValues(d);
                int rightW = TextRenderer.MeasureText(right, RowValF).Width + 6;
                bool two = DiskTwoLines(d, ramW);
                TextRenderer.DrawText(g, d.Name, RowNameF,
                    new Rectangle(ramX, ly, two ? ramW : ramW - rightW - 10, lineH), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                int vy = two ? ly + lineH : ly;
                TextRenderer.DrawText(g, right, RowValF,
                    new Rectangle(ramX + ramW - rightW, vy, rightW, lineH),
                    d.TempC >= 70 ? Theme.Amber : Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                if (d.VolGb > 0)
                {
                    float frac = (float)Math.Clamp(d.UsedGb / d.VolGb, 0, 1);
                    DrawBar(g, new RectangleF(ramX + 20, vy + BarOff, ramW - 40, 14), frac,
                        frac >= 0.9f ? Theme.Amber : Theme.AccentFill);
                }
                ly = vy + DiskBlockH;
            }
        }
        // Right of the storage panel, two wide boxes stacked: the graphics clock and the battery
        // time. Both span two ring slots, because both carry a label plus two values and the
        // one-slot boxes above are only wide enough for one.
        int wideW = (X(3) - X(2)) + ring - 28;
        int subY4 = subY3 + subH + 14;

        // The graphics clock is shown with its share of the card's own ceiling, because that share
        // is what a power profile actually moves: a busy card sitting well under its ceiling is the
        // firmware holding it there. Windows supplies this with no vendor software installed.
        var gt = GpuTelemetry.Read();
        _gpuHelpBtn = RectangleF.Empty;
        if (gt.Ok)
        {
            // The tile keeps its place whether or not the card is awake. A card that has powered
            // itself down reports no clock, and a dash says that; a tile that came and went would
            // read as the app breaking.
            var box = new RectangleF(X(2) + 14, subY3, wideW, subH);
            MetricBox(box, $"{Lang.T("st_gpu_clock")}: " +
                (gt.Mhz <= 0 ? "- MHz"
                 : gt.MaxMhz > 0 ? $"{gt.Mhz} MHz · {gt.Percent} %"
                 : $"{gt.Mhz} MHz"), hd + S(10));   // centred clear of the help dot on both sides
            _gpuHelpText = gt.MaxMhz > 0 && gt.Mhz > 0
                ? string.Format(Lang.T("st_gpu_clock_tip"), gt.Name, gt.Mhz, gt.MaxMhz, gt.Percent)
                : string.Format(Lang.T("st_gpu_clock_tip_idle"), gt.Name);
            _gpuHelpBtn = new RectangleF(box.Right - S(10) - hd, box.Y + (subH - hd) / 2f, hd, hd);
            HelpDot.Render(g, _gpuHelpBtn);
        }

        if (ig >= 0 && !vramBar) IgpuBox(subY3);

        int bm = Perf.BatteryMinutesLeftCached();
        MetricBox(new RectangleF(X(2) + 14, gt.Ok ? subY4 : subY3, wideW, subH),
            $"{Lang.T("ov_m_batttime")}: " + (bm > 0 ? $"{bm / 60} h {bm % 60:00} min" : "-"));

        // the metric texts, all in one size (see MetricBox)
        float metricPt = 11.5f;
        Font metricF = Dpi.Font("Segoe UI", metricPt, FontStyle.Bold);
        foreach (var (r, text) in metricTexts)
            while (metricPt > 8.5f && TextRenderer.MeasureText(g, text, metricF).Width > r.Width)
            {
                metricF.Dispose();
                metricPt -= 0.5f;
                metricF = Dpi.Font("Segoe UI", metricPt, FontStyle.Bold);
            }
        foreach (var (r, text) in metricTexts)
            TextRenderer.DrawText(g, text, metricF, r, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        metricF.Dispose();

        int rightH = gt.Ok ? subH * 2 + 14 : subH;
        int cardTop = subY3 + Math.Max(storageH, rightH) + 40;
        // hardware readings unavailable for a while (Ec.ReadsLost): the rings show "--"; say why
        if (info.Known && Ec.ReadsLost)
        {
            using var noteFont = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
            string note = Lang.T("hw_lost_note");
            int noteH = TextRenderer.MeasureText(g, note, noteFont, new Size(avail, int.MaxValue), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, note, noteFont, new Rectangle(Pad, cardTop, avail, noteH), Theme.Amber, TextFormatFlags.WordBreak);
            cardTop += noteH + 10;
        }
        // (#48) telemetry-only machines: say plainly why the fan rings and controls are dead
        if (info.Telemetry)
        {
            // why the controls are off, and - when the blocks show the layout other boards of this
            // generation have - that one report is what turns this machine into a supported one
            var noteFont = Dpi.Font("Segoe UI", 9.5f);
            string note = Lang.T(info.SchemaMissing ? "telemetry_note_schema" : "telemetry_note");
            if (EcBlocks.LayoutKnown()) note += Environment.NewLine + Lang.T("blocks_report_hint");
            int noteH = TextRenderer.MeasureText(g, note, noteFont, new Size(avail, int.MaxValue), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, note, noteFont, new Rectangle(Pad, cardTop, avail, noteH), Theme.Muted, TextFormatFlags.WordBreak);
            cardTop += noteH + 10;
        }
        // Backup WMI path: say which road the readings come from and what it does not carry,
        // then the profile the hardware reports (the tiles cannot show it while writes are off).
        if (info.BackupPath)
        {
            var noteFont = Dpi.Font("Segoe UI", 9.5f);
            // the tiles show the profile once switching is unlocked; until then this line does
            string note = Lang.T("backup_note") + (info.Active ? "" : Environment.NewLine
                        + string.Format(Lang.T("backup_profile"), Profiles.Get(info.Profile).Label));
            int noteH = TextRenderer.MeasureText(g, note, noteFont, new Size(avail, int.MaxValue),
                TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, note, noteFont, new Rectangle(Pad, cardTop, avail, noteH),
                Theme.Muted, TextFormatFlags.WordBreak);
            cardTop += noteH + 10;
        }
        int rowH = RowH;
        var card = new RectangleF(Pad, cardTop, avail, rowH * Rows.Length + 14);
        Ui.FillCard(g, card);
        int y = cardTop + 7;
        for (int i = 0; i < Rows.Length; i++)
        {
            var (key, val, mono) = Rows[i];
            // zebra stripe on odd rows (matches the tables on the EC-bytes / change-log sub-tabs)
            if (i % 2 == 1) { using var b = new SolidBrush(Theme.RowAlt); g.FillRectangle(b, Pad + 8, y, avail - 16, rowH); }
            TextRenderer.DrawText(g, Lang.T(key), Dpi.Font("Segoe UI", 10.5f),
                new Rectangle(Pad + 22, y, avail - 44, rowH), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            var font = mono ? Dpi.Font("Consolas", 11f, FontStyle.Bold) : Dpi.Font("Segoe UI", 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, val(info, hw), font,
                new Rectangle(Pad, y, avail - 22, rowH), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            y += rowH;
        }
    }

    // The help dot on this page is painted rather than laid out, because the whole sub-tab is one
    // buffered canvas. It draws and behaves exactly like the ones on the Scenarios page: the same
    // circle, and the same bubble.
    private RectangleF _gpuHelpBtn;
    private string _gpuHelpText = "";
    private readonly RectangleF[] _fanHelpBtns = new RectangleF[2];

    private void ChartsClick(Point p)
    {
        if (_statusSub != 0) return;
        foreach (var b in _fanHelpBtns)
            if (!b.IsEmpty && b.Contains(p)) { HelpPopup.Toggle(_canvas, Rectangle.Round(b), Lang.T("st_fan_target_tip"), this); return; }
        if (_gpuHelpBtn.IsEmpty || !_gpuHelpBtn.Contains(p)) return;
        HelpPopup.Toggle(_canvas, Rectangle.Round(_gpuHelpBtn), _gpuHelpText, this);
    }

    // ---- sub-tab 2: profile-byte matrix + legend + live fan-curve tables ----
    private void RenderBytes(Graphics g, int avail, StatusInfo info)
    {
        int sec = SecTop;
        if (!info.Known)
        {
            // no register matrix without a model entry; the monitoring-only note says why
            string note = info.Telemetry ? Lang.T(info.SchemaMissing ? "telemetry_note_schema" : "telemetry_note") : Lang.T("unsupported_title");
            var f = Dpi.Font("Segoe UI", 9.5f);
            int h = TextRenderer.MeasureText(g, note, f, new Size(avail, int.MaxValue), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, note, f, new Rectangle(Pad, sec, avail, h), Theme.Muted, TextFormatFlags.WordBreak);
            return;
        }
        sec = DrawMatrix(g, sec, avail, info);
        int noteH = NoteH(avail);
        TextRenderer.DrawText(g, Lang.T("st_matrix_note"), NoteF, new Rectangle(Pad, sec + 4, avail, noteH - 4),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        sec += noteH;
        sec = DrawLegend(g, sec, avail);
        if (_dev?.FanCurve != null && _curve is { } cv) DrawCurveLive(g, sec, avail, cv);
    }

    // ---- sub-tab 1: local hardware history (fed by the tray sampler, in-memory only) ----
    // 310 px up to about 150 %; above that the header and the time-label band grow with their
    // text, and the plot keeps room for its five value labels
    private static int HistChartH => Math.Max(310, ChartHead + ChartFoot + 6 * AxisH);

    // Chart geometry captured during the buffered render so the cursor overlay (drawn per
    // paint, on top of the buffer) can hit-test and map x -> time without a full re-render.
    // BLabel == "" means a single-series chart (FPS) — the cursor readout skips the B side.
    private sealed record HistPlot(RectangleF Plot, int MaxVal, string Unit,
        Func<HwSample, int> A, Func<HwSample, int> B, float LegendLeft, int CardTop,
        string ALabel, string BLabel, bool AllowZero = false);
    private readonly List<HistPlot> _histPlots = new();
    private List<HwSample>? _histData;
    private DateTime _histT0;
    private double _histSpanSec;
    private Point? _histCursor;

    private void RenderHistory(Graphics g, int avail)
    {
        // range picker right-aligned on the row above the first chart, export button to its left
        var rb = new Rectangle(Pad + avail - _histRange.Width, SecTop - 6, _histRange.Width, _histRange.Height);
        if (_histRange.Bounds != rb) _histRange.Bounds = rb;
        _histRange.BringToFront();
        // Measure the label against the buffer's DPI-aware DC. Neither earlier approach survives:
        // MeasureText(text, font) without a DC assumes 96 DPI (label clipped at 125/140% scale),
        // and PreferredSize feeds the button's CURRENT bounds back into its answer, so assigning
        // it on every chart rebuild grew the button a few px per tick, without end.
        var exSz = TextRenderer.MeasureText(g, _histExport.Text, _histExport.Font);
        int ewW = exSz.Width + 28, ewH = Math.Max(rb.Height, exSz.Height + 8);
        var eb = new Rectangle(rb.Left - ewW - 10, rb.Top + (rb.Height - ewH) / 2, ewW, ewH);
        if (_histExport.Bounds != eb) _histExport.Bounds = eb;
        _histExport.BringToFront();

        var data = HwHistory.Window(TimeSpan.FromMinutes(_histMin));
        _histPlots.Clear();
        _histData = data;
        _histT0 = DateTime.Now - TimeSpan.FromMinutes(_histMin);
        _histSpanSec = _histMin * 60.0;
        int top = SecTop + 44;
        top = DrawHistoryChart(g, top, avail, Lang.T("st_hist_temps"), "°C", data,
            s => s.CpuTemp, s => s.GpuTemp, "CPU", "GPU") + 24;
        // the fan curve target on the same 0-150 scale as the Status rings, 0 included (TECHNICAL §76)
        top = DrawHistoryChart(g, top, avail, Lang.T("st_hist_fans"), "%", data,
            s => s.CpuFan, s => s.GpuFan, "CPU", "GPU", maxVal: 150, gridSteps: 3, allowZero: true) + 24;
        if (HwHistory.HasRpm)
        {
            // dynamic RPM ceiling: at least 500 RPM of headroom above the observed peak
            // (rounded up to the next 500), so the line never hugs the top edge
            int peak = 0;
            foreach (var s in data) peak = Math.Max(peak, Math.Max(s.CpuRpm, s.GpuRpm));
            int maxRpm = Math.Max(2000, (peak + 500 + 499) / 500 * 500);
            top = DrawHistoryChart(g, top, avail, Lang.T("st_hist_rpm"), "", data,
                s => s.CpuRpm, s => s.GpuRpm, "CPU", "GPU", maxRpm) + 24;
        }
        {
            // single-series FPS chart, always present; ceiling rounded to the next 30 with headroom
            int peak = 0;
            foreach (var s in data) peak = Math.Max(peak, (int)s.Fps);
            int maxFps = Math.Max(60, (peak + 15 + 29) / 30 * 30);
            DrawHistoryChart(g, top, avail, Lang.T("st_hist_fps"), "", data,
                s => s.Fps, _ => -1, "FPS", "", maxFps, hint: Lang.T("st_hist_fps_hint"));
        }
    }

    // #172: chart geometry from the fonts. The header (46 px), the value-label column (62 px) and
    // the time-label band (50 px) are what fits up to about 150 % scaling and stay the floor;
    // above that each grows with its text, so labels are not cut and do not run into the title.
    private static readonly Font AxisF = Dpi.Font("Segoe UI", 8.5f);
    private static readonly Font LegF = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
    private static int AxisH => Math.Max(18, AxisF.Height);

    // header: the title (drawn at +10) and the top value label's upper half under it, with air;
    // foot: the bottom value label's lower half, then the time labels under it
    private static int ChartHead => Math.Max(46, 10 + GTitle.Height + 4 + AxisH / 2);
    private static int ChartFoot => Math.Max(50, XLabelGap + AxisH + 8);
    private static int XLabelGap => Math.Max(10, AxisH / 2 + 4);

    private static RectangleF ChartPlot(int top, int avail, int chartH, string widestValue)
    {
        int left = Math.Max(62, TextRenderer.MeasureText(widestValue, AxisF).Width + 14);
        return new RectangleF(Pad + left, top + ChartHead, avail - left - 20, chartH - ChartHead - ChartFoot);
    }

    // value label left of a grid line, centred on it
    private static void YLabel(Graphics g, string text, RectangleF plot, float y) =>
        TextRenderer.DrawText(g, text, AxisF, new Rectangle(Pad + 6, (int)y - AxisH / 2, (int)plot.Left - Pad - 10, AxisH),
            Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

    // time label under the plot, centred on its tick; the last one ("now") stays inside the card
    private static void XLabel(Graphics g, string text, RectangleF plot, float x, int cardRight)
    {
        int tw = TextRenderer.MeasureText(text, AxisF).Width;
        int cx = Math.Min((int)x, cardRight - 4 - tw / 2);
        int w = Math.Max(68, tw + 8);
        TextRenderer.DrawText(g, text, AxisF, new Rectangle(cx - w / 2, (int)plot.Bottom + XLabelGap, w, AxisH),
            Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
    }

    // legend entry right-to-left from *lx: label, then its marker (dot or short line) centred on the text
    private static void Legend(Graphics g, ref int lx, int top, string label, Color color, bool dot = true)
    {
        int w = TextRenderer.MeasureText(label, LegF).Width;
        lx -= w;
        TextRenderer.DrawText(g, label, LegF, new Point(lx, top + 12), color);
        lx -= 18;
        float cy = top + 12 + LegF.Height / 2f;
        using var b = new SolidBrush(color);
        if (dot)
        {
            float d = Math.Max(9, LegF.Height / 3f);
            g.FillEllipse(b, lx + 4, cy - d / 2, d, d);
        }
        else { using var pn = new Pen(color, 3f); g.DrawLine(pn, lx, cy, lx + 13, cy); }
        lx -= 14;
    }

    // One line chart card (two series, 0..maxVal scale) over the last _histMin minutes.
    private int DrawHistoryChart(Graphics g, int top, int avail, string title, string unit,
        List<HwSample> data, Func<HwSample, int> serA, Func<HwSample, int> serB, string aLabel, string bLabel,
        int maxVal = 100, string? hint = null, int gridSteps = 4, bool allowZero = false)
    {
        // A reading counts when it is above 0 (0 = no reading) and not far past the scale. The fan
        // target chart follows the rings instead: 0 is a real target, above the scale is a misread.
        bool Valid(int v) => allowZero ? v >= 0 && v <= maxVal : v > 0 && v <= maxVal * 1.3f;
        var card = new RectangleF(Pad, top, avail, HistChartH);
        Ui.FillCard(g, card);
        TextRenderer.DrawText(g, title, GTitle, new Rectangle(Pad + 16, top + 10, avail - 200, GTitle.Height + 4),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);

        // legend (series colours follow the gauges: CPU = accent, GPU = violet)
        int lx = Pad + avail - 16;
        foreach (var (label, color) in new[] { (bLabel, Theme.Violet), (aLabel, Theme.Accent) })
            if (label.Length > 0) Legend(g, ref lx, top, label, color);   // single-series chart (FPS) has no B

        // room under the plot so the time labels never clip against the card edge
        var plot = ChartPlot(top, avail, HistChartH, maxVal + unit);
        _histPlots.Add(new HistPlot(plot, maxVal, unit, serA, serB, lx, top, aLabel, bLabel, allowZero));
        using (var grid = new Pen(Theme.Border))
        {
            for (int i = 0; i <= gridSteps; i++)
            {
                int v = maxVal * i / gridSteps;
                float y = plot.Bottom - v / (float)maxVal * plot.Height;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                YLabel(g, v + unit, plot, y);
            }
            // time ticks: -N min ... now
            int ticks = 4;
            for (int t = 0; t <= ticks; t++)
            {
                float x = plot.Left + t / (float)ticks * plot.Width;
                string lab = t == ticks ? Lang.T("st_hist_now") : $"-{_histMin - t * _histMin / ticks} min";
                XLabel(g, lab, plot, x, Pad + avail);
            }
        }

        // The FPS chart passes a hint ("fills in while a game runs") shown both on an empty
        // window and when the window has samples but none carried an FPS reading.
        bool noA = true;
        foreach (var s in data) { if (Valid(serA(s))) { noA = false; break; } }
        if (data.Count < 2 || (hint != null && noA))
        {
            TextRenderer.DrawText(g, hint ?? Lang.T("st_hist_empty"), Dpi.Font("Segoe UI", 10.5f),
                Rectangle.Round(plot), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (data.Count < 2) return top + HistChartH;
        }

        var now = DateTime.Now;
        var t0 = now - TimeSpan.FromMinutes(_histMin);
        double span = (now - t0).TotalSeconds;
        void DrawSeries(Func<HwSample, int> sel, Color color)
        {
            var pts = new List<PointF>(data.Count);
            foreach (var s in data)
            {
                int v = sel(s);
                if (!Valid(v)) continue;   // unknown reads leave a gap rather than plotting 0
                float x = plot.Left + (float)((s.Time - t0).TotalSeconds / span) * plot.Width;
                float y = plot.Bottom - Math.Clamp(v, 0, maxVal) / (float)maxVal * plot.Height;
                pts.Add(new PointF(x, y));
            }
            if (pts.Count < 2) return;
            using var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts.ToArray());
        }
        DrawSeries(serA, Theme.Accent);
        DrawSeries(serB, Theme.Violet);
        return top + HistChartH;
    }

    // Export the visible history window as CSV or JSON (picked by the chosen extension).
    // Plain local file write - the same data the charts show, nothing more.
    private void ExportHistory()
    {
        var data = HwHistory.Window(TimeSpan.FromMinutes(_histMin));
        if (data.Count == 0) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            FileName = $"ghostdeck-history-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            if (Path.GetExtension(dlg.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var rows = data.Select(s => new
                {
                    time = s.Time.ToString("yyyy-MM-dd'T'HH:mm:ss"),
                    profile = s.Profile.ToString(),
                    cpuTempC = (int)s.CpuTemp, gpuTempC = (int)s.GpuTemp,
                    cpuFanPct = (int)s.CpuFan, gpuFanPct = (int)s.GpuFan,
                    cpuRpm = s.CpuRpm, gpuRpm = s.GpuRpm,
                    cpuLoadPct = (int)s.CpuLoad,
                    fps = (int)s.Fps,   // -1 = no reading (FPS monitor off / no game)
                });
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("time,profile,cpu_temp_c,gpu_temp_c,cpu_fan_pct,gpu_fan_pct,cpu_rpm,gpu_rpm,cpu_load_pct,fps");
                foreach (var s in data)
                    sb.Append(s.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append(',').Append(s.Profile).Append(',')
                      .Append(s.CpuTemp).Append(',').Append(s.GpuTemp).Append(',')
                      .Append(s.CpuFan).Append(',').Append(s.GpuFan).Append(',')
                      .Append(s.CpuRpm).Append(',').Append(s.GpuRpm).Append(',')
                      .Append(s.CpuLoad).Append(',').Append(s.Fps).AppendLine();
                File.WriteAllText(dlg.FileName, sb.ToString());
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), string.Format(Lang.T("bk_err"), ex.Message), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- sub-tab 2: gaming (live FPS/frametime from the ETW monitor + last session summary) ----
    private static int GmChartH => Math.Max(310, ChartHead + ChartFoot + 6 * AxisH);   // grows like HistChartH
    // #172: the stat boxes grow with their two lines of text above about 150 % scaling
    private static readonly Font GmLabF = Dpi.Font("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font GmValF = Dpi.Font("Segoe UI", 14f, FontStyle.Bold);
    private static readonly Font GmHeadF = Dpi.Font("Segoe UI", 10.5f);
    private static readonly Font GmDiagF = Dpi.Font("Consolas", 9f);
    private static int GmBoxH => Math.Max(64, GmLabF.Height + GmValF.Height + 4);
    // The whole line height plus air: a box 2 px shorter than the font cut the descenders of the
    // "No game detected" hint (y, j, ę) against the stat boxes below it.
    private static int GmHeadH => Math.Max(24, Math.Max(GmHeadF.Height, GmDiagF.Height) + 4);
    private static int GmRowH => Math.Max(40, GCell.Height + 6);   // #172: session rows grow with the text
    private const int GmSessRows = 9;

    private void RenderGaming(Graphics g, int avail)
    {
        int top = SecTop;
        var snap = FpsMonitor.Current;

        // current game line ("Game: witcher3" or the how-to hint) + a raw ETW diag readout on the
        // right (untranslated by design, like CLI output) so a dead session is visible, not silent
        string tgt = FpsMonitor.TargetName;
        string diag = $"ETW: {FpsMonitor.DiagStatus} · ev {FpsMonitor.EventsSeen}" + (tgt.Length > 0 ? $" · {tgt}" : "");
        int headH = GmHeadH;
        int diagW = TextRenderer.MeasureText(g, diag, GmDiagF).Width + 8;
        TextRenderer.DrawText(g, diag, GmDiagF, new Rectangle(Pad + avail - diagW, top, diagW, headH),
            Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        string head = snap is { } s0 ? string.Format(Lang.T("gm_game"), s0.Process) : Lang.T("gm_none");
        TextRenderer.DrawText(g, head, GmHeadF, new Rectangle(Pad, top, avail - diagW - 10, headH),
            snap != null ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);   // dpi-ok: a game's process name can be any length
        top += headH + 10;

        // live stat boxes: FPS · frametime · 1% low · stutters (rolling 30 s)
        int gap = 14, bw = (avail - gap * 3) / 4, boxH = GmBoxH;
        int valY = Math.Max(26, GmLabF.Height + 2);   // value line under the label
        void Box(int i, string label, string value, Color? c = null)
        {
            var r = new RectangleF(Pad + i * (bw + gap), top, bw, boxH);
            Ui.FillCard(g, r);
            TextRenderer.DrawText(g, label, GmLabF,
                new Rectangle((int)r.X + 14, (int)r.Y + 8, bw - 28, Math.Max(18, GmLabF.Height)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
            TextRenderer.DrawText(g, value, GmValF,
                new Rectangle((int)r.X + 14, (int)r.Y + valY, bw - 28, boxH - valY - 6), c ?? Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        Box(0, "FPS", snap is { } a ? a.Fps.ToString() : "--", Theme.Accent);
        Box(1, Lang.T("ov_m_frametime"), snap is { } b ? $"{b.FrameTimeMs:0.0} ms" : "--");
        Box(2, "1% low", snap is { Fps: > 0, P1LowFps: > 0 } c ? c.P1LowFps.ToString() : "--");
        Box(3, Lang.T("gm_stut"), snap is { } d ? d.Stutters30s.ToString() : "--",
            snap is { Stutters30s: > 0 } ? Theme.Amber : null);
        top += boxH + 24;

        top = DrawFrametimeChart(g, top, avail) + 24;
        DrawLastSession(g, top, avail);
    }

    private int DrawFrametimeChart(Graphics g, int top, int avail)
    {
        var card = new RectangleF(Pad, top, avail, GmChartH);
        Ui.FillCard(g, card);
        TextRenderer.DrawText(g, Lang.T("gm_chart"), GTitle, new Rectangle(Pad + 16, top + 10, avail - 40, GTitle.Height + 4),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);

        // legend: what the cyan line and the red dots mean
        int lx = Pad + avail - 16;
        Legend(g, ref lx, top, Lang.T("gm_stut"), Theme.Red, dot: true);
        Legend(g, ref lx, top, Lang.T("ov_m_frametime"), Theme.Accent, dot: false);

        var frames = FpsMonitor.RecentFrames(60);

        // dynamic ceiling: fit the p99 with headroom, rounded to 5 ms (floor 20 ms)
        float maxMs = 20;
        if (frames.Count >= 10)
        {
            var sorted = frames.Select(f => f.Ms).OrderBy(v => v).ToArray();
            float p99 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))];
            maxMs = Math.Max(20f, (float)Math.Ceiling(p99 * 1.6f / 5f) * 5f);
        }
        var plot = ChartPlot(top, avail, GmChartH, $"{maxMs:0} ms");

        using (var grid = new Pen(Theme.Border))
        {
            for (int i = 0; i <= 4; i++)
            {
                float v = maxMs * i / 4f;
                float gy = plot.Bottom - v / maxMs * plot.Height;
                g.DrawLine(grid, plot.Left, gy, plot.Right, gy);
                YLabel(g, $"{v:0} ms", plot, gy);
            }
            for (int t = 0; t <= 4; t++)
            {
                float gx = plot.Left + t / 4f * plot.Width;
                string lab = t == 4 ? Lang.T("st_hist_now") : $"-{60 - t * 15} s";
                XLabel(g, lab, plot, gx, Pad + avail);
            }
        }

        if (frames.Count < 10)
        {
            TextRenderer.DrawText(g, Lang.T("gm_chart_empty"), Dpi.Font("Segoe UI", 10.5f),
                Rectangle.Round(plot), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return top + GmChartH;
        }

        // Per-2-px buckets: the average makes the line, the bucket max feeds the stutter dots.
        // (A raw polyline would be thousands of points at high FPS and would swallow single
        // slow frames between fast neighbours.)
        var t0 = DateTime.Now.AddSeconds(-60);
        int buckets = Math.Max(60, (int)plot.Width / 2);
        var sum = new float[buckets]; var cnt = new int[buckets]; var max = new float[buckets];
        foreach (var (time, ms) in frames)
        {
            int bi = (int)((time - t0).TotalSeconds / 60.0 * buckets);
            if (bi < 0 || bi >= buckets) continue;
            sum[bi] += ms; cnt[bi]++; if (ms > max[bi]) max[bi] = ms;
        }
        var sortedMs = frames.Select(f => f.Ms).OrderBy(v => v).ToArray();
        float median = sortedMs[sortedMs.Length / 2];
        float stutterAt = Math.Max(25f, 2f * median);   // same rule as FpsMonitor
        float Y(float ms) => plot.Bottom - Math.Clamp(ms, 0, maxMs) / maxMs * plot.Height;
        float X(int bi) => plot.Left + (bi + 0.5f) / buckets * plot.Width;

        if (median > 0)
        {
            using var mp = new Pen(Color.FromArgb(140, Theme.Muted)) { DashStyle = DashStyle.Dash };
            g.DrawLine(mp, plot.Left, Y(median), plot.Right, Y(median));
        }

        var pts = new List<PointF>(buckets);
        for (int bi = 0; bi < buckets; bi++)
            if (cnt[bi] > 0) pts.Add(new PointF(X(bi), Y(sum[bi] / cnt[bi])));
        if (pts.Count >= 2)
        {
            using var pen = new Pen(Theme.Accent, 2f) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts.ToArray());
        }

        using (var sb = new SolidBrush(Theme.Red))
            for (int bi = 0; bi < buckets; bi++)
                if (cnt[bi] > 0 && max[bi] > stutterAt)
                    g.FillEllipse(sb, X(bi) - 3, Y(max[bi]) - 3, 6, 6);

        return top + GmChartH;
    }

    // Saved game sessions: a picker (newest first, count set in Settings) + the selected session's
    // card: FPS stats + the EC summary (temps / RPM / profile) — the pairing no plain FPS overlay
    // has. "—" on models where the EC history had nothing for that window.
    private void DrawLastSession(Graphics g, int top, int avail)
    {
        TextRenderer.DrawText(g, Lang.T("gm_last"), GTitle, new Rectangle(Pad, top, avail, GTitle.Height + 4),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);

        // picker + export button, right-aligned on the title row (same pattern as History)
        var pb = new Rectangle(Pad + avail - _sessPick.Width, top - 4, _sessPick.Width, _sessPick.Height);
        if (_sessPick.Bounds != pb) _sessPick.Bounds = pb;
        _sessPick.BringToFront();
        var exSz = TextRenderer.MeasureText(g, _sessExport.Text, _sessExport.Font);
        int ewW = exSz.Width + 28, ewH = Math.Max(pb.Height, exSz.Height + 8);
        var eb = new Rectangle(pb.Left - ewW - 10, pb.Top + (pb.Height - ewH) / 2, ewW, ewH);
        if (_sessExport.Bounds != eb) _sessExport.Bounds = eb;
        _sessExport.BringToFront();

        int y = top + GTitle.Height + 12;

        if (SelectedSession() is not { } s)
        {
            var empty = new RectangleF(Pad, y, avail, 56);
            Ui.FillCard(g, empty);
            TextRenderer.DrawText(g, Lang.T("gm_last_none"), GCell, Rectangle.Round(empty), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var rows = new (string label, string val, bool mono)[]
        {
            (Lang.T("gm_game_lbl"), $"{s.Process}  ·  {s.End:HH:mm}", false),
            (Lang.T("gm_dur"), FmtTs(s.End - s.Start), false),
            ("FPS", string.Format(Lang.T("gm_fps_row"), s.AvgFps, s.MinFps, s.MaxFps), false),
            ("1% low", s.P1LowFps > 0 ? s.P1LowFps + " FPS" : "-", false),
            (Lang.T("gm_frames"), s.Frames.ToString("N0"), true),
            (Lang.T("gm_stut"), s.Stutters.ToString("N0"), true),
            (Lang.T("gm_temp_max"), s.MaxCpuTemp > 0 ? $"CPU {s.MaxCpuTemp}°  ·  GPU {s.MaxGpuTemp}°" : "-", false),
            (Lang.T("gm_rpm_avg"), s.AvgCpuRpm > 0 || s.AvgGpuRpm > 0 ? $"CPU {s.AvgCpuRpm}  ·  GPU {s.AvgGpuRpm}" : "-", false),
            (Lang.T("gm_profile"), s.Profile.Length > 0 ? s.Profile : "-", false),
        };
        var card = new RectangleF(Pad, y, avail, GmSessRows * GmRowH + 14);
        Ui.FillCard(g, card);
        int ry = y + 7;
        for (int i = 0; i < rows.Length; i++)
        {
            if (i % 2 == 1) { using var b = new SolidBrush(Theme.RowAlt); g.FillRectangle(b, Pad + 8, ry, avail - 16, GmRowH); }
            TextRenderer.DrawText(g, rows[i].label, Dpi.Font("Segoe UI", 10.5f),
                new Rectangle(Pad + 22, ry, avail - 44, GmRowH), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            var f = rows[i].mono ? Dpi.Font("Consolas", 11f, FontStyle.Bold) : Dpi.Font("Segoe UI", 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, rows[i].val, f,
                new Rectangle(Pad, ry, avail - 22, GmRowH), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            ry += GmRowH;
        }
    }

    private void ChartsMouse(Control owner, Point? p)
    {
        bool onDot = _statusSub == 0 && p is { } pt
            && (_gpuHelpBtn.Contains(pt) || _fanHelpBtns[0].Contains(pt) || _fanHelpBtns[1].Contains(pt));
        var want = onDot ? Cursors.Hand : Cursors.Default;
        if (owner.Cursor != want) owner.Cursor = want;
    }

    // Cursor tracking for the history charts (mouse over the canvas). Only stores the point and
    // repaints - the heavy chart render stays in the buffer, the overlay is drawn per paint.
    private void HistMouse(Point? p)
    {
        if (_statusSub != 1) p = null;
        if (_histCursor == p) return;
        _histCursor = p;
        _canvas.Invalidate();
    }

    // Overlay for the history charts, drawn on every paint on top of the buffered render:
    // a permanent "selected · now" value row left of each legend ("--" when the cursor is off
    // the charts), plus the tracking line + dots when the cursor is over the plot area.
    private void DrawHistCursor(Graphics g)
    {
        if (_statusSub != 1 || _histPlots.Count == 0) return;
        var data = _histData;
        if (data == null || data.Count < 2) return;
        var p0 = _histPlots[0].Plot;

        bool hasCur = _histCursor is { } cur && cur.X >= p0.Left - 6 && cur.X <= p0.Right + 6;
        HwSample selSample = default;
        float sx = 0;
        if (hasCur)
        {
            float cx = Math.Clamp(_histCursor!.Value.X, p0.Left, p0.Right);
            var tSel = _histT0.AddSeconds((cx - p0.Left) / p0.Width * _histSpanSec);
            double best = double.MaxValue;
            foreach (var s in data)
            {
                double d = Math.Abs((s.Time - tSel).TotalSeconds);
                if (d < best) { best = d; selSample = s; }
            }
            sx = Math.Clamp(p0.Left + (float)((selSample.Time - _histT0).TotalSeconds / _histSpanSec) * p0.Width, p0.Left, p0.Right);
        }

        var last = data[^1];
        using var line = new Pen(Color.FromArgb(120, Theme.Muted)) { DashStyle = DashStyle.Dash };
        using var valFont = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
        string now = Lang.T("st_hist_now");
        foreach (var hp in _histPlots)
        {
            int Val(Func<HwSample, int> f, HwSample s)
            {
                int v = f(s);
                bool ok = hp.AllowZero ? v >= 0 && v <= hp.MaxVal : v > 0 && v <= hp.MaxVal * 1.3f;
                return ok ? v : -1;
            }
            int va = -1, vb = -1;
            if (hasCur)
            {
                g.DrawLine(line, sx, hp.Plot.Top, sx, hp.Plot.Bottom);
                void Dot(int v, Color c)
                {
                    if (v < 0) return;
                    float y = hp.Plot.Bottom - Math.Clamp(v, 0, hp.MaxVal) / (float)hp.MaxVal * hp.Plot.Height;
                    using var fill = new SolidBrush(c);
                    using var ring = new Pen(Theme.Surface, 2f);
                    g.FillEllipse(fill, sx - 5, y - 5, 10, 10);
                    g.DrawEllipse(ring, sx - 5, y - 5, 10, 10);
                }
                va = Val(hp.A, selSample);
                vb = Val(hp.B, selSample);
                Dot(va, Theme.Accent);
                Dot(vb, Theme.Violet);
            }

            string Fmt(int v) => v >= 0 ? v + hp.Unit : "--";
            string ta = $"{hp.ALabel} {Fmt(va)} · {now} {Fmt(Val(hp.A, last))}";
            int xRight = (int)hp.LegendLeft - 16;
            if (hp.BLabel.Length > 0)
            {
                string tb = $"{hp.BLabel} {Fmt(vb)} · {now} {Fmt(Val(hp.B, last))}";
                int wb = TextRenderer.MeasureText(tb, valFont).Width;
                TextRenderer.DrawText(g, tb, valFont, new Point(xRight - wb, hp.CardTop + 12), Theme.Violet);
                xRight -= wb + 18;
            }
            int wa = TextRenderer.MeasureText(ta, valFont).Width;
            TextRenderer.DrawText(g, ta, valFont, new Point(xRight - wa, hp.CardTop + 12), Theme.Accent);

            // Semi-transparent value bubble next to the cursor, on the chart it is actually over
            // (the crosshair line + the readout row above stay for the other charts).
            if (hasCur && _histCursor is { } cp && cp.Y >= hp.Plot.Top && cp.Y <= hp.Plot.Bottom)
            {
                var lines = new List<(string txt, Color col)> { (selSample.Time.ToString("HH:mm:ss"), Theme.Muted) };
                if (va > 0 || hp.BLabel.Length == 0) lines.Add(($"{hp.ALabel}: {Fmt(va)}", Theme.Accent));
                if (hp.BLabel.Length > 0) lines.Add(($"{hp.BLabel}: {Fmt(vb)}", Theme.Violet));
                int tw = 0;
                foreach (var (txt, _) in lines) tw = Math.Max(tw, TextRenderer.MeasureText(txt, valFont).Width);
                int lh = valFont.Height + 2;
                int bw2 = tw + 24, bh = lines.Count * lh + 14;
                float bx = sx + 16, by = Math.Clamp(cp.Y - bh / 2f, hp.Plot.Top, hp.Plot.Bottom - bh);
                if (bx + bw2 > hp.Plot.Right - 4) bx = sx - bw2 - 16;   // flip near the right edge
                var bub = new RectangleF(bx, by, bw2, bh);
                using (var bb = new SolidBrush(Color.FromArgb(232, Theme.Card)))
                using (var bp = Theme.RoundRect(bub, 8))
                    g.FillPath(bb, bp);
                using (var op = new Pen(Theme.BorderStrong))
                using (var bp = Theme.RoundRect(bub, 8))
                    g.DrawPath(op, bp);
                int ly = (int)by + 7;
                foreach (var (txt, col) in lines)
                {
                    TextRenderer.DrawText(g, txt, valFont, new Point((int)bx + 12, ly), col);
                    ly += lh;
                }
            }
        }
    }

    // ---- sub-tab 3: recent-changes log ----
    private void RenderLog(Graphics g, int avail) => DrawRecentLog(g, SecTop - 16, avail);

    private int DrawRecentLog(Graphics g, int top, int avail)
    {
        var headers = new[] { Lang.T("log_col_time"), Lang.T("log_col_source"), Lang.T("log_col_detail"), Lang.T("log_col_result") };
        var rows = new List<string[]>();
        foreach (var e in ChangeLog.Recent(RecentLogRows))
            rows.Add(new[] { e.Time.ToString("MM-dd HH:mm:ss"), ChangeLog.SourceLabel(e.Source), e.Detail, e.Result });
        if (rows.Count == 0) rows.Add(new[] { "-", "", Lang.T("log_empty"), "" });
        bool[] mono = { true, false, false, true };
        var lefts = LogColumns(headers, rows, mono, avail);
        int titleY = top + 16;
        // column colour language shared with the full-log window: muted time, accent source,
        // plain detail, muted mono readback
        Color? LogCell(int r, int c) => c switch { 0 => Theme.Muted, 1 => Theme.Accent, 3 => Theme.Muted, _ => Theme.Text };
        int bottom = DrawGrid(g, titleY, avail, Lang.T("log_recent"), headers, lefts, rows, mono, cellColor: LogCell);

        // Right-align the "Full log…" button on the section title row, as wide as its label needs.
        int btnW = Math.Max(150, TextRenderer.MeasureText(_logBtn.Text, _logBtn.Font).Width + 28), btnH = GTitle.Height + 6;
        var b = new Rectangle(Pad + avail - btnW, titleY - 3, btnW, btnH);
        if (_logBtn.Bounds != b) _logBtn.Bounds = b;
        _logBtn.BringToFront();
        return bottom;
    }

    // #172: the change-log columns follow their content. Fixed shares (17 / 18 / 39 / 26 %) left a
    // wide empty gap after the detail column at high scaling while the readback was cut. Time,
    // source and readback get what their longest cell needs, the detail column takes the rest.
    private static float[] LogColumns(string[] headers, List<string[]> rows, bool[] mono, int avail)
    {
        int inner = avail - 36;
        int[] need = new int[headers.Length];
        for (int c = 0; c < headers.Length; c++)
        {
            need[c] = TextRenderer.MeasureText(headers[c], GHead).Width;
            foreach (var r in rows)
                need[c] = Math.Max(need[c], TextRenderer.MeasureText(r[c], mono[c] ? GMono : GCell).Width);
            need[c] += 24;   // the 10 px cell gap plus air
        }
        // the readback never takes more than 40 % (a very long one still leaves the detail room)
        need[3] = Math.Min(need[3], inner * 2 / 5);
        int detail = Math.Max(inner / 5, inner - need[0] - need[1] - need[3]);
        float x0 = 0, x1 = x0 + need[0], x2 = x1 + need[1], x3 = x2 + detail;
        if (x3 + need[3] > inner) x3 = inner - need[3];   // keep the readback whole, the detail gives way
        return new[] { 0f, x1 / inner, x2 / inner, x3 / inner };
    }

    // the EC-bytes note under the matrix wraps to as many lines as the width needs (#172)
    private static readonly Font NoteF = Dpi.Font("Segoe UI", 9f);
    private static int NoteH(int avail) =>
        TextRenderer.MeasureText(Lang.T("st_matrix_note"), NoteF, new Size(avail, int.MaxValue), TextFormatFlags.WordBreak).Height + 12;

    private static readonly Font GTitle = Dpi.Font("Segoe UI", 12f, FontStyle.Bold);
    private static readonly Font GHead = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);
    private static readonly Font GCell = Dpi.Font("Segoe UI", 10.5f);
    private static readonly Font GMono = Dpi.Font("Consolas", 11f, FontStyle.Bold);

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
            : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;   // dpi-ok: one-line headers of data tables; the columns are sized to fit them
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
                    cellColor?.Invoke(r, c) ?? Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);   // dpi-ok: table cells are data (log lines, byte values); the full-log window shows a long line whole
            ry += rowH;
        }
        return y + totalH;
    }

    private string RecipeVal(ProfileId p, byte addr)
    {
        if (_dev == null) return "-";
        foreach (var (a, v) in _dev.Recipes[p]) if (a == addr) return v.ToString("X2");
        return "-";
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
        var order = Profiles.Shown;

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
        string LiveHex(int i) => _live.Length > i && !(Ec.OnBackupPath && i is 1 or 2) ? _live[i].ToString("X2") : "-";
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
        // above the ring's own scale is a misread; temperatures and loads keep the old 130 guard
        bool ok = known && (allowZero ? value >= 0 : value > 0) && value <= Math.Max(max, 129);
        float frac = ok ? Math.Clamp(value / (float)max, 0, 1) : 0;
        IconPainter.Ring(g, new RectangleF(x, y, size, size), frac, color, ok ? value.ToString() : "-", unit, label, sub);
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
    public override void OnDeviceDbChanged()
    {
        _dev = Devices.Detect(D.Firmware());
        _live = Array.Empty<byte>();
        _curve = null;                       // the byte matrix and curve tables move with the layout
        Relayout(); _canvas?.Rebuild(); Invalidate();
    }

}
