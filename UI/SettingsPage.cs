using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

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
        ("PanicReset", "Panic reset"),
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

        // Thermal notifications: OSD + tray balloon when CPU/GPU stays above the threshold for
        // the chosen time. Off by default — the user opts in.
        var alerts = new CardSection(Lang.T("set_grp_alerts"), "");
        alerts.AddRow(Lang.T("ta_enable"), Toggle(D.Settings.TempAlertEnabled, v => { D.Settings.TempAlertEnabled = v; D.SaveSettings(); }));
        // 70/75 exist mainly so the alert can be tried out without heating the laptop up first.
        int[] degVals = { 70, 75, 80, 85, 90, 95, 100 };
        var deg = Combo(degVals.Select(x => x + " °C").ToArray(), Math.Max(0, Array.IndexOf(degVals, D.Settings.TempAlertDegrees)));
        deg.SelectedIndexChanged += (_, _) => { D.Settings.TempAlertDegrees = degVals[Math.Max(0, deg.SelectedIndex)]; D.SaveSettings(); };
        alerts.AddRow(Lang.T("ta_threshold"), deg);
        int[] secVals = { 5, 10, 20, 30, 60 };
        var secsCombo = Combo(secVals.Select(x => x + " s").ToArray(), Math.Max(0, Array.IndexOf(secVals, D.Settings.TempAlertSeconds)));
        secsCombo.SelectedIndexChanged += (_, _) => { D.Settings.TempAlertSeconds = secVals[Math.Max(0, secsCombo.SelectedIndex)]; D.SaveSettings(); };
        alerts.AddRow(Lang.T("ta_time"), secsCombo);
        _right.Add(alerts);

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

        // Settings backup: export = a copy of settings.json, import = adopt the preferences from
        // such a file. Machine-local state survives an import (see AppSettings.ImportFrom).
        var backup = new CardSection(Lang.T("set_grp_backup"), "");
        var expBtn = new Button { Text = Lang.T("set_export"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(expBtn);
        expBtn.Click += (_, _) => ExportSettings();
        var impBtn = new Button { Text = Lang.T("set_import"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(impBtn);
        impBtn.Click += (_, _) => ImportSettings();
        var bRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
        bRow.Controls.Add(expBtn); bRow.Controls.Add(impBtn);
        backup.AddRow(null, bRow);
        _left.Add(backup);

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
            hk.AddRow(key == "Cycle" ? Lang.T("cycle") : key == "CoolerBoost" ? Lang.T("cooler_boost") : key == "Overlay" ? Lang.T("overlay_title") : key == "OverlayLock" ? Lang.T("ov_lock_menu") : key == "PanicReset" ? Lang.T("hk_panic") : label, row);
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

    // ---------------- settings backup ----------------
    private void ExportSettings()
    {
        using var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "ghostdeck-settings.json" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(D.Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), string.Format(Lang.T("bk_err"), ex.Message), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportSettings()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            string txt = File.ReadAllText(dlg.FileName);
            AppSettings? imported = null;
            // Cheap shape check first: an arbitrary JSON object would otherwise deserialize
            // into a defaults instance and silently wipe the user's settings.
            using (var doc = JsonDocument.Parse(txt))
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("Language", out _))
                    imported = JsonSerializer.Deserialize<AppSettings>(txt);
            if (imported == null)
            {
                MessageBox.Show(FindForm(), Lang.T("imp_err"), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            D.Settings.ImportFrom(imported);
            Lang.Set(D.Settings.Language);
            Theme.Set(D.Settings.DarkMode);
            try { Autostart.Set(D.Settings.Autostart); } catch { }
            D.SaveSettings();
            D.SettingsChanged();          // hotkeys + tray menu + icons follow the imported values
            D.SetChargeLimit(D.Settings.ChargeLimit);
            D.ApplyOverlaySettings();
            D.SetOverlay(D.Settings.OverlayEnabled);
            Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
            MessageBox.Show(FindForm(), Lang.T("imp_ok"), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (JsonException)
        {
            MessageBox.Show(FindForm(), Lang.T("imp_err"), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), string.Format(Lang.T("bk_err"), ex.Message), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
