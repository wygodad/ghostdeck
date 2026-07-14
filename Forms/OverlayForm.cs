using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>Snapshot fed to the gaming overlay (built by the tray from EC + OS metrics).</summary>
public readonly record struct OverlaySample(
    bool Known, bool Writable,
    string ProfileLabel, Color ProfileColor, bool CoolerBoost,
    int CpuTemp, int GpuTemp, int CpuRpm, int GpuRpm, int CpuFanPct, int GpuFanPct,
    int CpuLoad, int RamPct, double RamUsedGb, int ChargeLimit,
    int BatteryPct, bool Charging,
    int GpuUsage, int VramMb, int CpuClock);

/// <summary>
/// Detachable, always-on-top mini status panel for gaming: temps / fan RPM / profile / load, in a
/// compact card or a horizontal HUD bar. Never steals focus (WS_EX_NOACTIVATE), stays off the taskbar
/// (WS_EX_TOOLWINDOW), adjustable transparency, free drag-to-move, optional click-through (mouse passes
/// to the game). Layout is fully measurement-driven, so it stays correct at any DPI / display scaling.
/// </summary>
public sealed class OverlayForm : Form
{
    private enum IconKind { None, Cpu, Gpu, Fan, Load, Ram, Charge }

    private readonly AppSettings _settings;
    private readonly Func<OverlaySample> _sampler;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private OverlaySample _s;
    private bool _dragging;
    private Point _dragOffset;

    private static readonly Color White = Color.FromArgb(0xF4, 0xF6, 0xF9);
    private static readonly Color Muted = Color.FromArgb(0x98, 0xA0, 0xAE);

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            // WS_EX_LAYERED is permanent: we render with UpdateLayeredWindow (per-pixel ARGB), which
            // gives true independent alpha for background vs text, smooth anti-aliased edges, perfect
            // rounded corners and natural click-through on transparent pixels.
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
    private const int ULW_ALPHA = 2, AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

    public OverlayForm(AppSettings settings, Func<OverlaySample> sampler)
    {
        _settings = settings;
        _sampler = sampler;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;   // we lay out in device pixels from measured text
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(0x12, 0x14, 0x1A);
        _s = sampler();
        ApplySettings();
        _timer.Tick += (_, _) => { _s = _sampler(); RenderLayered(); };
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyClickThrough(); RenderLayered(); }
    protected override void OnLoad(EventArgs e) { base.OnLoad(e); _timer.Start(); RenderLayered(); }
    protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Stop(); base.OnFormClosed(e); }

    // Layered rendering owns the pixels; suppress the normal paint path so nothing fights it.
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    /// <summary>Re-read settings (called by the tray after the user edits overlay options).</summary>
    public void ApplySettings()
    {
        TopMost = _settings.OverlayAlwaysTop;
        Cursor = Locked ? Cursors.Default : Cursors.SizeAll;
        Relayout();
        RestorePosition();
        ApplyClickThrough();
        RenderLayered();
    }

    private void ApplyClickThrough()
    {
        if (!IsHandleCreated) return;
        // Keep WS_EX_LAYERED; only toggle the click-through TRANSPARENT bit. (With per-pixel alpha,
        // transparent pixels are already click-through; TRANSPARENT makes the whole window pass mouse.)
        int ex = GetWindowLong(Handle, GWL_EXSTYLE);
        ex = _settings.OverlayClickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(Handle, GWL_EXSTYLE, ex);
    }

    private bool Locked => _settings.OverlayClickThrough;   // locked = click-through, can't drag
    private float U => Math.Clamp(_settings.OverlayScale, 80, 160) / 100f;
    private bool IsBar => string.Equals(_settings.OverlayLayout, "Bar", StringComparison.OrdinalIgnoreCase);
    private static int Ceil(float v) => (int)Math.Ceiling(v);

    private Color Accent() => !_s.Known
        ? Muted
        : _settings.OverlayAccentFromProfile && _s.ProfileColor.A != 0 ? _s.ProfileColor : Color.FromArgb(0x17, 0xC0, 0xEB);

    private bool ShowHeader => _settings.HasMetric(OverlayMetric.Profile);
    // Optional heavier + larger metric labels — improves legibility when the overlay is scaled down.
    // Semibold alone was too subtle against the muted grey, so the toggle uses full Bold at a bumped size.
    private Font LabelFont() => _settings.OverlayBoldText
        ? new Font("Segoe UI", 10.5f * U, FontStyle.Bold)
        : new Font("Segoe UI", 9f * U);
    private string HeaderText => !_s.Known ? "MSI · n/a" : "MSI · " + _s.ProfileLabel;

    // Metric cells (icon, label, value) picked by the settings flags. Profile/Cooler live in the header.
    private List<(IconKind icon, string label, string value)> Cells()
    {
        var st = _settings;
        var list = new List<(IconKind, string, string)>();
        void Add(OverlayMetric m, IconKind ic, string label, string value) { if (st.HasMetric(m)) list.Add((ic, label, value)); }
        Add(OverlayMetric.CpuTemp, IconKind.Cpu, "CPU", _s.Known ? $"{_s.CpuTemp}°" : "--");
        Add(OverlayMetric.GpuTemp, IconKind.Gpu, "GPU", _s.Known ? $"{_s.GpuTemp}°" : "--");
        Add(OverlayMetric.CpuRpm, IconKind.Fan, "CPU fan", _s.CpuRpm > 0 ? $"{_s.CpuRpm}" : "--");
        Add(OverlayMetric.GpuRpm, IconKind.Fan, "GPU fan", _s.GpuRpm > 0 ? $"{_s.GpuRpm}" : "--");
        Add(OverlayMetric.FanPct, IconKind.Fan, "Fans", $"{_s.CpuFanPct}/{_s.GpuFanPct}%");
        Add(OverlayMetric.CpuLoad, IconKind.Load, "Load", $"{_s.CpuLoad}%");
        Add(OverlayMetric.GpuUsage, IconKind.Gpu, "GPU%", _s.GpuUsage >= 0 ? $"{_s.GpuUsage}%" : "--");
        Add(OverlayMetric.CpuClock, IconKind.Cpu, "CPU clk", _s.CpuClock > 0 ? $"{_s.CpuClock} MHz" : "--");
        Add(OverlayMetric.Ram, IconKind.Ram, "RAM", $"{_s.RamUsedGb:0.0} GB");
        Add(OverlayMetric.Vram, IconKind.Ram, "VRAM", _s.VramMb >= 0 ? $"{_s.VramMb} MB" : "--");
        Add(OverlayMetric.Battery, IconKind.Charge, _s.Charging ? "Bat ⚡" : "Bat", _s.BatteryPct >= 0 ? $"{_s.BatteryPct}%" : "--");
        Add(OverlayMetric.ChargeLimit, IconKind.Charge, "Limit", _s.ChargeLimit > 0 ? $"{_s.ChargeLimit}%" : "—");
        return list;
    }

    // ---------------- layout ----------------
    private void Relayout()
    {
        using var g = CreateGraphics();
        Size sz = IsBar ? RenderBar(g, false) : RenderCard(g, false);
        var clamped = new Size(Math.Max(sz.Width, Ceil(80 * U)), Math.Max(sz.Height, Ceil(30 * U)));
        if (Size != clamped) Size = clamped;   // no Region: per-pixel alpha shapes the window
    }

    private void RestorePosition()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        int x = _settings.OverlayX, y = _settings.OverlayY;
        if (x < 0 || y < 0) { x = wa.X + 24; y = wa.Y + 24; }
        x = Math.Clamp(x, wa.X, Math.Max(wa.X, wa.Right - Width));
        y = Math.Clamp(y, wa.Y, Math.Max(wa.Y, wa.Bottom - Height));
        Location = new Point(x, y);
    }

    // ---------------- per-pixel layered render ----------------
    // Compose the whole overlay into a 32-bpp premultiplied-ARGB bitmap and push it with
    // UpdateLayeredWindow. Background alpha and content (text/icon) alpha are independent; content also
    // gets a soft drop-shadow so it stays readable even with the background off, on any game.
    private void RenderLayered()
    {
        if (!IsHandleCreated) return;
        Relayout();
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        float dpi;
        using (var mg = CreateGraphics()) dpi = mg.DpiY;
        int radius = Ceil(dpi / 96f * 10f * U);

        // 1) content layer (header + cells + icons), fully opaque colours on transparent bg
        using var content = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        content.SetResolution(dpi, dpi);
        using (var gc = Graphics.FromImage(content))
        {
            gc.SmoothingMode = SmoothingMode.AntiAlias;
            gc.TextRenderingHint = TextRenderingHint.AntiAlias;   // grayscale AA -> proper alpha edges
            gc.Clear(Color.Transparent);
            if (IsBar) RenderBar(gc, true); else RenderCard(gc, true);
        }

        // 2) compose final: background -> shadow -> content -> frame/grip
        using var final = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        final.SetResolution(dpi, dpi);
        using (var gf = Graphics.FromImage(final))
        {
            gf.SmoothingMode = SmoothingMode.AntiAlias;
            gf.CompositingQuality = CompositingQuality.HighQuality;
            gf.Clear(Color.Transparent);

            bool moveMode = !Locked;
            Color bgc; try { bgc = ColorTranslator.FromHtml(_settings.OverlayBgColor); } catch { bgc = Color.FromArgb(0x16, 0x18, 0x1D); }
            int bgA = _settings.OverlayBgEnabled ? (int)(Math.Clamp(_settings.OverlayBgOpacity, 0, 100) / 100f * 255) : 0;
            // While unlocked (move mode) force a visible, grabbable surface even if the background is off
            // or nearly transparent — otherwise the panel has almost no hit area / is hard to see to drag.
            Color fillCol = bgA > 0 ? bgc : Color.FromArgb(0x12, 0x14, 0x1A);
            int fillA = moveMode ? Math.Max(bgA, 110) : bgA;
            if (fillA > 0)
            {
                using var bgb = new SolidBrush(Color.FromArgb(fillA, fillCol));
                using var bp = RoundPath(new RectangleF(0, 0, w, h), radius);
                gf.FillPath(bgb, bp);
            }

            float textA = Math.Clamp(_settings.OverlayOpacity, 40, 100) / 100f;
            int off = Math.Max(1, Ceil(U));
            DrawLayer(gf, content, off, off, textA * 0.5f, blackTint: true);   // soft shadow for contrast
            DrawLayer(gf, content, 0, 0, textA, blackTint: false);             // the content itself

            bool locked = Locked;
            var accent = Accent();
            if (!locked)
            {
                float bw = Math.Max(1.8f, 2.1f * U);
                using var pen = new Pen(Color.FromArgb(240, accent), bw);
                gf.DrawPath(pen, RoundPath(new RectangleF(bw / 2, bw / 2, w - bw, h - bw), radius));
                DrawGrip(gf, accent);
            }
            else if (_settings.OverlayBgEnabled)
            {
                using var pen = new Pen(Color.FromArgb(34, 255, 255, 255), 1f);
                gf.DrawPath(pen, RoundPath(new RectangleF(0.5f, 0.5f, w - 1, h - 1), radius));
            }
        }

        PushLayered(final);
    }

    // Draw an image at (dx,dy) with a global alpha multiplier; blackTint renders it as a black silhouette.
    private static void DrawLayer(Graphics g, Image img, int dx, int dy, float alpha, bool blackTint)
    {
        var m = new ColorMatrix { Matrix33 = Math.Clamp(alpha, 0f, 1f) };
        if (blackTint) { m.Matrix00 = m.Matrix11 = m.Matrix22 = 0f; }   // RGB -> 0
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(m);
        g.DrawImage(img, new Rectangle(dx, dy, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
    }

    private void PushLayered(Bitmap bmp)
    {
        if (!IsHandleCreated) return;
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(Left, Top);
            var bf = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref bf, ULW_ALPHA);
        }
        finally
        {
            SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // Vertical card: header row + a 1- or 2-column grid of (icon, label / value) cells.
    private Size RenderCard(Graphics g, bool draw)
    {
        var accent = Accent();
        using var valueF = new Font("Segoe UI", 14.5f * U, FontStyle.Bold);
        using var labelF = LabelFont();
        using var headF = new Font("Segoe UI", 11.5f * U, FontStyle.Bold);
        using var whiteB = new SolidBrush(White);
        using var mutedB = new SolidBrush(Muted);
        using var accentB = new SolidBrush(accent);

        int labelH = Ceil(labelF.GetHeight(g)), valueH = Ceil(valueF.GetHeight(g)), headH = Ceil(headF.GetHeight(g));
        int pad = Ceil(labelH * 1.05f);
        int icon = Ceil(labelH * 1.25f), iconGap = Ceil(labelH * 0.35f);
        int lvGap = Ceil(labelH * 0.10f), rowGap = Ceil(labelH * 0.60f), colGap = Ceil(labelH * 1.5f);
        var cells = Cells();
        int cols = cells.Count <= 3 ? 1 : 2;
        int rows = Math.Max(1, (int)Math.Ceiling(cells.Count / (double)cols));

        int cellW = 0;
        foreach (var (ic, label, value) in cells)
        {
            int lw = icon + iconGap + Ceil(g.MeasureString(label, labelF).Width);
            int vw = Ceil(g.MeasureString(value, valueF).Width);
            cellW = Math.Max(cellW, Math.Max(lw, vw));
        }
        int cellH = labelH + lvGap + valueH;

        int dot = Ceil(labelH * 0.72f);
        int headW = 0;
        if (ShowHeader)
        {
            headW = dot + iconGap + Ceil(g.MeasureString(HeaderText, headF).Width);
            if (_settings.HasMetric(OverlayMetric.CoolerBoost)) headW += iconGap + icon;   // reserve for the snowflake (always shown)
        }

        int innerW = Math.Max(cols * cellW + (cols - 1) * colGap, headW);
        int headBlock = ShowHeader ? headH + Ceil(labelH * 0.65f) : 0;
        int w = pad + innerW + pad;
        int h = pad + headBlock + rows * cellH + (rows - 1) * rowGap + pad;

        if (draw)
        {
            int y = pad;
            if (ShowHeader)
            {
                g.FillEllipse(accentB, pad, y + (headH - dot) / 2, dot, dot);
                g.DrawString(HeaderText, headF, whiteB, pad + dot + iconGap, y);
                if (_settings.HasMetric(OverlayMetric.CoolerBoost))
                {
                    // Snowflake is always visible: dim when Cooler Boost is off, bright cyan when on.
                    var cbCol = _s.CoolerBoost ? Color.FromArgb(0x7E, 0xE0, 0xFF) : Color.FromArgb(0x55, 0x5C, 0x69);
                    using var sf = new Font("Segoe UI Symbol", 10.5f * U, FontStyle.Bold);
                    Ui.CenterGlyph(g, "❄", sf, cbCol, new RectangleF(pad + innerW - icon, y, icon, headH));
                }
                y += headBlock;
            }
            float stroke = Math.Max(1f, 1.3f * U);
            for (int i = 0; i < cells.Count; i++)
            {
                int col = i % cols, row = i / cols;
                int cx = pad + col * (cellW + colGap);
                int cy = y + row * (cellH + rowGap);
                DrawIcon(g, cells[i].icon, new RectangleF(cx, cy + (labelH - icon) / 2f, icon, icon), accent, stroke);
                g.DrawString(cells[i].label, labelF, mutedB, cx + icon + iconGap, cy);
                g.DrawString(cells[i].value, valueF, whiteB, cx, cy + labelH + lvGap);
            }
        }
        return new Size(w, h);
    }

    // Horizontal HUD bar: dot + profile + separator, then icon/value/label chips, all vertically centred.
    private Size RenderBar(Graphics g, bool draw)
    {
        var accent = Accent();
        using var valueF = new Font("Segoe UI", 12.5f * U, FontStyle.Bold);
        using var labelF = LabelFont();
        using var headF = new Font("Segoe UI", 12.5f * U, FontStyle.Bold);
        using var whiteB = new SolidBrush(White);
        using var mutedB = new SolidBrush(Muted);
        using var accentB = new SolidBrush(accent);

        // uH is the geometry unit: the *regular* 9pt label height, so the bar's size (padding, gaps, dot,
        // overall height) stays identical whether the Bold-text toggle enlarges the drawn label or not.
        int uH; using (var uf = new Font("Segoe UI", 9f * U)) uH = Ceil(uf.GetHeight(g));
        int labelH = Ceil(labelF.GetHeight(g)), valueH = Ceil(valueF.GetHeight(g)), headH = Ceil(headF.GetHeight(g));
        int icon = Ceil(valueH * 0.95f), iconGap = Ceil(uH * 0.32f), chipGap = Ceil(uH * 1.0f);
        int padH = Ceil(uH * 1.0f), padV = Ceil(uH * 0.8f);
        int rowH = Math.Max(valueH, icon);
        int h = padV * 2 + rowH;
        int midY = h / 2;
        int dot = Ceil(uH * 0.7f);
        float stroke = Math.Max(1f, 1.3f * U);
        var cells = Cells();

        int x = padH;
        if (ShowHeader)
        {
            if (draw) g.FillEllipse(accentB, x, midY - dot / 2, dot, dot);
            x += dot + iconGap;
            string lab = _s.Known ? _s.ProfileLabel : "n/a";
            if (draw) g.DrawString(lab, headF, whiteB, x, midY - headH / 2f);
            x += Ceil(g.MeasureString(lab, headF).Width) + iconGap;
            if (_settings.HasMetric(OverlayMetric.CoolerBoost))
            {
                var cbCol = _s.CoolerBoost ? Color.FromArgb(0x7E, 0xE0, 0xFF) : Color.FromArgb(0x55, 0x5C, 0x69);
                if (draw)
                    using (var sf = new Font("Segoe UI Symbol", 11f * U, FontStyle.Bold))
                        Ui.CenterGlyph(g, "❄", sf, cbCol, new RectangleF(x, midY - icon / 2f, icon, icon));
                x += icon + iconGap;
            }
            x += chipGap;
            if (draw)
                using (var sp = new Pen(Color.FromArgb(48, 255, 255, 255), Math.Max(1f, U)))
                { int sh = Ceil(rowH * 0.6f); g.DrawLine(sp, x - chipGap / 2, midY - sh / 2, x - chipGap / 2, midY + sh / 2); }
        }
        for (int i = 0; i < cells.Count; i++)
        {
            var (ic, label, value) = cells[i];
            if (draw) DrawIcon(g, ic, new RectangleF(x, midY - icon / 2f, icon, icon), accent, stroke);
            x += icon + iconGap;
            if (draw) g.DrawString(value, valueF, whiteB, x, midY - valueH / 2f);
            x += Ceil(g.MeasureString(value, valueF).Width) + Ceil(uH * 0.25f);
            if (draw) g.DrawString(label, labelF, mutedB, x, midY - labelH / 2f);
            x += Ceil(g.MeasureString(label, labelF).Width) + chipGap;
        }
        int w = (cells.Count > 0 ? x - chipGap : x) + padH;
        return new Size(w, h);
    }

    // Minimalist monochrome vector icons (no icon-font dependency; scale with the given rect).
    private static void DrawIcon(Graphics g, IconKind k, RectangleF r, Color color, float stroke)
    {
        if (k == IconKind.None) return;
        using var pen = new Pen(Color.FromArgb(235, color), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var br = new SolidBrush(Color.FromArgb(235, color));
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        switch (k)
        {
            case IconKind.Cpu:
                g.DrawRectangle(pen, x + w * 0.26f, y + h * 0.26f, w * 0.48f, h * 0.48f);
                for (int i = 0; i < 3; i++)
                {
                    float p = 0.34f + i * 0.16f;
                    g.DrawLine(pen, x + w * p, y + h * 0.14f, x + w * p, y + h * 0.26f);
                    g.DrawLine(pen, x + w * p, y + h * 0.74f, x + w * p, y + h * 0.86f);
                    g.DrawLine(pen, x + w * 0.14f, y + h * p, x + w * 0.26f, y + h * p);
                    g.DrawLine(pen, x + w * 0.74f, y + h * p, x + w * 0.86f, y + h * p);
                }
                break;
            case IconKind.Gpu:
                g.DrawRectangle(pen, x + w * 0.12f, y + h * 0.30f, w * 0.76f, h * 0.40f);
                float cr = Math.Min(w, h) * 0.16f;
                g.DrawEllipse(pen, x + w * 0.62f - cr, y + h * 0.5f - cr, cr * 2, cr * 2);
                break;
            case IconKind.Fan:
                float fx = x + w / 2, fy = y + h / 2, R = Math.Min(w, h) * 0.4f;
                g.DrawEllipse(pen, fx - R, fy - R, R * 2, R * 2);
                for (int i = 0; i < 3; i++)
                {
                    double a = i * 2 * Math.PI / 3 - Math.PI / 2;
                    g.DrawLine(pen, fx, fy, fx + (float)Math.Cos(a) * R * 0.85f, fy + (float)Math.Sin(a) * R * 0.85f);
                }
                g.FillEllipse(br, fx - R * 0.15f, fy - R * 0.15f, R * 0.3f, R * 0.3f);
                break;
            case IconKind.Load:
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * 0.12f, y + h * 0.60f), new PointF(x + w * 0.34f, y + h * 0.60f),
                    new PointF(x + w * 0.48f, y + h * 0.30f), new PointF(x + w * 0.62f, y + h * 0.72f),
                    new PointF(x + w * 0.76f, y + h * 0.50f), new PointF(x + w * 0.90f, y + h * 0.50f),
                });
                break;
            case IconKind.Ram:
                var rect = new RectangleF(x + w * 0.14f, y + h * 0.32f, w * 0.72f, h * 0.32f);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                for (int i = 0; i < 3; i++)
                {
                    float px = rect.X + rect.Width * (0.28f + i * 0.22f);
                    g.DrawLine(pen, px, rect.Y + rect.Height * 0.28f, px, rect.Y + rect.Height * 0.72f);
                }
                g.DrawLine(pen, rect.X + rect.Width * 0.28f, rect.Bottom, rect.X + rect.Width * 0.28f, rect.Bottom + h * 0.12f);
                g.DrawLine(pen, rect.X + rect.Width * 0.72f, rect.Bottom, rect.X + rect.Width * 0.72f, rect.Bottom + h * 0.12f);
                break;
            case IconKind.Charge:
                var bat = new RectangleF(x + w * 0.16f, y + h * 0.32f, w * 0.60f, h * 0.36f);
                g.DrawRectangle(pen, bat.X, bat.Y, bat.Width, bat.Height);
                g.FillRectangle(br, bat.Right + w * 0.02f, bat.Y + bat.Height * 0.28f, w * 0.06f, bat.Height * 0.44f);
                g.FillRectangle(br, bat.X + bat.Width * 0.14f, bat.Y + bat.Height * 0.24f, bat.Width * 0.5f, bat.Height * 0.52f);
                break;
        }
    }

    // Dotted grip in the bottom-right corner — visible only while unlocked (draggable). 3×3 dots so the
    // drag handle is easy to see and grab, especially with the background turned off.
    private void DrawGrip(Graphics g, Color c)
    {
        int r = Math.Max(2, Ceil(2.4f * U)), step = Ceil(6 * U);
        int gx = Width - Ceil(9 * U) - step * 2 - r, gy = Height - Ceil(9 * U) - step * 2 - r;
        using var b = new SolidBrush(Color.FromArgb(210, c));
        for (int col = 0; col < 3; col++)
            for (int row = 0; row < 3; row++)
                g.FillEllipse(b, gx + col * step, gy + row * step, r, r);
    }

    // ---------------- drag ----------------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Locked) return;   // locked = not draggable (belt-and-suspenders on top of click-through)
        if (e.Button == MouseButtons.Left) { _dragging = true; _dragOffset = e.Location; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
            Location = new Point(Location.X + e.X - _dragOffset.X, Location.Y + e.Y - _dragOffset.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        _settings.OverlayX = Location.X;
        _settings.OverlayY = Location.Y;
        _settings.Save();
    }

    private static GraphicsPath RoundPath(RectangleF r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
