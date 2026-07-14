using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

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
