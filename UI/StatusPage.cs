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

    // Sub-tabs split the (heavy) Status page into shorter views: charts, history, gaming, EC bytes, change log.
    private readonly SubTabs _statusTabs = new(Lang.T("st_sub_charts"), Lang.T("st_sub_history"), Lang.T("st_sub_gaming"), Lang.T("st_sub_bytes"), Lang.T("st_sub_log"));
    private int _statusSub;
    private readonly SegControl _histRange = new(new[] { "5 min", "15 min", "30 min", "60 min" }, 1) { Size = new Size(300, 30), Visible = false };
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
        _dev = Devices.Detect(d.Firmware);
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
        VisibleChanged += (_, _) => { if (Visible) { Relayout(); _timer.Start(); } else _timer.Stop(); UpdateFpsViewer(); };
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
                    // Gaming: the last-session card / picker can change between ticks -> keep in sync
                    if (Visible && _statusSub == 2) { RefreshSessions(); Relayout(); }
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

    // Height of the active sub-tab's content (0 = charts, 1 = history, 2 = gaming, 3 = EC bytes, 4 = change log).
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
            return SecTop + 34 + GmBoxH + 24 + GmChartH + 24 + sess + 40;
        }
        if (sub == 3)
        {
            int h = SecTop + GridH(true, 5, 2) + NoteH + 8 + GridH(false, 4);
            if (_dev?.FanCurve is { } fc) h += 16 + GridH(true, fc.Points);
            return h + 40;
        }
        return SecTop + 16 + GridH(true, RecentLogRows) + 40;
    }

    private const int RecentLogRows = 16;

    // Paint the cached snapshot immediately (instant tab switch) and refresh in the background.
    public override void OnEnter() { _logBtn.Text = Lang.T("log_full"); _logBtn.Visible = _statusSub == 4; _histRange.Visible = _histExport.Visible = _statusSub == 1; _sessPick.Visible = _sessExport.Visible = _statusSub == 2; if (_statusSub == 2) RefreshSessions(); UpdateFpsViewer(); Relayout(); _canvas.Rebuild(); RefreshAsync(); }
    public override void ApplyTheme() { base.ApplyTheme(); if (_canvas != null) { _canvas.BackColor = Theme.Surface; Ui.StyleGhost(_logBtn); _statusTabs.Invalidate(); _canvas.Rebuild(); } }
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

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); _p.HistMouse(e.Location); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _p.HistMouse(null); }

        protected override void Dispose(bool disposing) { if (disposing) _buf?.Dispose(); base.Dispose(disposing); }
    }

    internal void Render(Graphics g, int width)
    {
        var info = D.Status();
        var hw = _hw;   // cached; refreshed off the UI thread by RefreshAsync

        TextRenderer.DrawText(g, Lang.T("menu_status"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);
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

    // ---- sub-tab 2: profile-byte matrix + legend + live fan-curve tables ----
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

    // ---- sub-tab 1: local hardware history (fed by the tray sampler, in-memory only) ----
    private const int HistChartH = 310;

    // Chart geometry captured during the buffered render so the cursor overlay (drawn per
    // paint, on top of the buffer) can hit-test and map x -> time without a full re-render.
    // BLabel == "" means a single-series chart (FPS) — the cursor readout skips the B side.
    private sealed record HistPlot(RectangleF Plot, int MaxVal, string Unit,
        Func<HwSample, int> A, Func<HwSample, int> B, float LegendLeft, int CardTop,
        string ALabel, string BLabel);
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
        top = DrawHistoryChart(g, top, avail, Lang.T("st_hist_fans"), "%", data,
            s => s.CpuFan, s => s.GpuFan, "CPU", "GPU") + 24;
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

    // One line chart card (two series, 0..maxVal scale) over the last _histMin minutes.
    private int DrawHistoryChart(Graphics g, int top, int avail, string title, string unit,
        List<HwSample> data, Func<HwSample, int> serA, Func<HwSample, int> serB, string aLabel, string bLabel,
        int maxVal = 100, string? hint = null)
    {
        var card = new RectangleF(Pad, top, avail, HistChartH);
        Ui.FillCard(g, card);
        TextRenderer.DrawText(g, title, GTitle, new Rectangle(Pad + 16, top + 10, avail - 200, GTitle.Height + 4),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);

        // legend (series colours follow the gauges: CPU = accent, GPU = violet)
        var legFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        int lx = Pad + avail - 16;
        foreach (var (label, color) in new[] { (bLabel, Theme.Violet), (aLabel, Theme.Accent) })
        {
            if (label.Length == 0) continue;   // single-series chart (FPS)
            int w = TextRenderer.MeasureText(label, legFont).Width;
            lx -= w;
            TextRenderer.DrawText(g, label, legFont, new Point(lx, top + 12), color);
            lx -= 18;
            using var b = new SolidBrush(color);
            g.FillEllipse(b, lx + 4, top + 16, 9, 9);
            lx -= 14;
        }

        // 50 px reserved under the plot so the time labels never clip against the card edge
        var plot = new RectangleF(Pad + 62, top + 46, avail - 62 - 20, HistChartH - 46 - 50);
        _histPlots.Add(new HistPlot(plot, maxVal, unit, serA, serB, lx, top, aLabel, bLabel));
        using (var grid = new Pen(Theme.Border))
        using (var axisFont = new Font("Segoe UI", 8.5f))
        {
            for (int i = 0; i <= 4; i++)
            {
                int v = maxVal * i / 4;
                float y = plot.Bottom - v / (float)maxVal * plot.Height;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                TextRenderer.DrawText(g, v + unit, axisFont, new Rectangle(Pad + 6, (int)y - 9, 52, 18),
                    Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            // time ticks: -N min ... now
            int ticks = 4;
            for (int t = 0; t <= ticks; t++)
            {
                float x = plot.Left + t / (float)ticks * plot.Width;
                string lab = t == ticks ? Lang.T("st_hist_now") : $"-{_histMin - t * _histMin / ticks} min";
                TextRenderer.DrawText(g, lab, axisFont, new Rectangle((int)x - 34, (int)plot.Bottom + 10, 68, 18),
                    Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            }
        }

        // The FPS chart passes a hint ("fills in while a game runs") shown both on an empty
        // window and when the window has samples but none carried an FPS reading.
        bool noA = true;
        foreach (var s in data) { int v = serA(s); if (v > 0 && v <= maxVal * 1.3f) { noA = false; break; } }
        if (data.Count < 2 || (hint != null && noA))
        {
            TextRenderer.DrawText(g, hint ?? Lang.T("st_hist_empty"), new Font("Segoe UI", 10.5f),
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
                if (v <= 0 || v > maxVal * 1.3f) continue;   // unknown reads leave a gap rather than plotting 0
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
    private const int GmChartH = 310;
    private const int GmBoxH = 64;
    private const int GmRowH = 40;
    private const int GmSessRows = 9;

    private void RenderGaming(Graphics g, int avail)
    {
        int top = SecTop;
        var snap = FpsMonitor.Current;

        // current game line ("Game: witcher3" or the how-to hint) + a raw ETW diag readout on the
        // right (untranslated by design, like CLI output) so a dead session is visible, not silent
        string tgt = FpsMonitor.TargetName;
        string diag = $"ETW: {FpsMonitor.DiagStatus} · ev {FpsMonitor.EventsSeen}" + (tgt.Length > 0 ? $" · {tgt}" : "");
        var diagFont = new Font("Consolas", 9f);
        int diagW = TextRenderer.MeasureText(g, diag, diagFont).Width + 8;
        TextRenderer.DrawText(g, diag, diagFont, new Rectangle(Pad + avail - diagW, top, diagW, 24),
            Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        string head = snap is { } s0 ? string.Format(Lang.T("gm_game"), s0.Process) : Lang.T("gm_none");
        TextRenderer.DrawText(g, head, new Font("Segoe UI", 10.5f), new Rectangle(Pad, top, avail - diagW - 10, 24),
            snap != null ? Theme.Text : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        top += 34;

        // live stat boxes: FPS · frametime · 1% low · stutters (rolling 30 s)
        int gap = 14, bw = (avail - gap * 3) / 4;
        void Box(int i, string label, string value, Color? c = null)
        {
            var r = new RectangleF(Pad + i * (bw + gap), top, bw, GmBoxH);
            Ui.FillCard(g, r);
            TextRenderer.DrawText(g, label, new Font("Segoe UI", 9f, FontStyle.Bold),
                new Rectangle((int)r.X + 14, (int)r.Y + 8, bw - 28, 18), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, value, new Font("Segoe UI", 14f, FontStyle.Bold),
                new Rectangle((int)r.X + 14, (int)r.Y + 26, bw - 28, GmBoxH - 32), c ?? Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        Box(0, "FPS", snap is { } a ? a.Fps.ToString() : "--", Theme.Accent);
        Box(1, Lang.T("ov_m_frametime"), snap is { } b ? $"{b.FrameTimeMs:0.0} ms" : "--");
        Box(2, "1% low", snap is { Fps: > 0, P1LowFps: > 0 } c ? c.P1LowFps.ToString() : "--");
        Box(3, Lang.T("gm_stut"), snap is { } d ? d.Stutters30s.ToString() : "--",
            snap is { Stutters30s: > 0 } ? Theme.Amber : null);
        top += GmBoxH + 24;

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
        using (var legFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
        {
            int lx = Pad + avail - 16;
            void Leg(string label, Color c, bool dot)
            {
                int w = TextRenderer.MeasureText(label, legFont).Width;
                lx -= w;
                TextRenderer.DrawText(g, label, legFont, new Point(lx, top + 12), c);
                lx -= 18;
                using var b = new SolidBrush(c);
                if (dot) g.FillEllipse(b, lx + 4, top + 16, 9, 9);
                else { using var p = new Pen(c, 3f); g.DrawLine(p, lx, top + 20, lx + 13, top + 20); }
                lx -= 14;
            }
            Leg(Lang.T("gm_stut"), Theme.Red, dot: true);
            Leg(Lang.T("ov_m_frametime"), Theme.Accent, dot: false);
        }

        var plot = new RectangleF(Pad + 62, top + 46, avail - 62 - 20, GmChartH - 46 - 50);
        var frames = FpsMonitor.RecentFrames(60);

        // dynamic ceiling: fit the p99 with headroom, rounded to 5 ms (floor 20 ms)
        float maxMs = 20;
        if (frames.Count >= 10)
        {
            var sorted = frames.Select(f => f.Ms).OrderBy(v => v).ToArray();
            float p99 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))];
            maxMs = Math.Max(20f, (float)Math.Ceiling(p99 * 1.6f / 5f) * 5f);
        }

        using (var grid = new Pen(Theme.Border))
        using (var axisFont = new Font("Segoe UI", 8.5f))
        {
            for (int i = 0; i <= 4; i++)
            {
                float v = maxMs * i / 4f;
                float gy = plot.Bottom - v / maxMs * plot.Height;
                g.DrawLine(grid, plot.Left, gy, plot.Right, gy);
                TextRenderer.DrawText(g, $"{v:0} ms", axisFont, new Rectangle(Pad + 6, (int)gy - 9, 52, 18),
                    Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            for (int t = 0; t <= 4; t++)
            {
                float gx = plot.Left + t / 4f * plot.Width;
                string lab = t == 4 ? Lang.T("st_hist_now") : $"-{60 - t * 15} s";
                TextRenderer.DrawText(g, lab, axisFont, new Rectangle((int)gx - 30, (int)plot.Bottom + 10, 60, 18),
                    Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            }
        }

        if (frames.Count < 10)
        {
            TextRenderer.DrawText(g, Lang.T("gm_chart_empty"), new Font("Segoe UI", 10.5f),
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
            ("1% low", s.P1LowFps > 0 ? s.P1LowFps + " FPS" : "—", false),
            (Lang.T("gm_frames"), s.Frames.ToString("N0"), true),
            (Lang.T("gm_stut"), s.Stutters.ToString("N0"), true),
            (Lang.T("gm_temp_max"), s.MaxCpuTemp > 0 ? $"CPU {s.MaxCpuTemp}°  ·  GPU {s.MaxGpuTemp}°" : "—", false),
            (Lang.T("gm_rpm_avg"), s.AvgCpuRpm > 0 || s.AvgGpuRpm > 0 ? $"CPU {s.AvgCpuRpm}  ·  GPU {s.AvgGpuRpm}" : "—", false),
            (Lang.T("gm_profile"), s.Profile.Length > 0 ? s.Profile : "—", false),
        };
        var card = new RectangleF(Pad, y, avail, GmSessRows * GmRowH + 14);
        Ui.FillCard(g, card);
        int ry = y + 7;
        for (int i = 0; i < rows.Length; i++)
        {
            if (i % 2 == 1) { using var b = new SolidBrush(Theme.RowAlt); g.FillRectangle(b, Pad + 8, ry, avail - 16, GmRowH); }
            TextRenderer.DrawText(g, rows[i].label, new Font("Segoe UI", 10.5f),
                new Rectangle(Pad + 22, ry, avail - 44, GmRowH), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            var f = rows[i].mono ? new Font("Consolas", 11f, FontStyle.Bold) : new Font("Segoe UI", 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, rows[i].val, f,
                new Rectangle(Pad, ry, avail - 22, GmRowH), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            ry += GmRowH;
        }
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
        using var valFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        string now = Lang.T("st_hist_now");
        foreach (var hp in _histPlots)
        {
            int Val(Func<HwSample, int> f, HwSample s)
            {
                int v = f(s);
                return v > 0 && v <= hp.MaxVal * 1.3f ? v : -1;
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

            string Fmt(int v) => v > 0 ? v + hp.Unit : "--";
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
