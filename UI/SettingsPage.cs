using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
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
        ("PanicReset", "Panic reset"), ("KbdLight", "Keyboard backlight"), ("Webcam", "Webcam"),
        ("EcView", "EC live view"), ("WinLock", "Windows key lock"), ("Touchpad", "Touchpad"),
    };
    private static readonly int[] ChargeVals = { 0, 60, 80, 100 };
    private const int Pad = 28, Gutter = 24, TitleTop = 22;

    // Sub-tab groups: 0 = Start (tiles), then the six content groups. The active one is
    // persisted (AppSettings.SettingsSubTab), so Settings reopens where the user left off,
    // across app restarts too.
    private const int SubHome = 0, SubGeneral = 1, SubPower = 2, SubNotif = 3, SubGaming = 4, SubHotkeys = 5, SubSystem = 6;
    private const int GroupCount = 7;
    private readonly List<CardSection>[] _gLeft = new List<CardSection>[GroupCount];
    private readonly List<CardSection>[] _gRight = new List<CardSection>[GroupCount];
    private SubTabs? _subTabs;
    private readonly List<GroupTile> _tiles = new();
    private HomeHeader? _homeHeader;   // Start: model + tier + version + "update available" chip
    private Label? _whatsNew;          // Start: "What's new in vX" -> Updates with the notes expanded
    private int _cur;
    private readonly Dictionary<string, HotkeyBox> _boxes = new();
    private readonly Dictionary<string, ToggleSwitch> _hkToggles = new();
    private ToggleSwitch? _hkMaster;
    private readonly Dictionary<string, List<Panel>> _swatches = new();
    private OverlaySettingsPanel? _overlayPanel;
    private SegControl? _themeSeg;             // kept to re-point after a theme change from the header button
    private CardSection? _scenVisCard;         // "Scenarios tab" visibility card (flash target for the gear)
    private static readonly Color FlashPink = Color.FromArgb(0xEC, 0x48, 0x99);   // highlight frame color
    private Label? _refreshNow;                // live current panel refresh rate (Settings → Power → Display)
    private Action? _syncRefreshMan;           // re-points the manual rate picker at the live rate
    private List<int> _dispRates = new();      // snapshot the Display card was built from...
    private (bool Internal, string? Name) _dispTarget;   // ...compared in OnDisplayChanged
    private string _uiLang = Lang.CurrentCode; // language the form was built with
    private bool _builtTravelOn;               // travel/charge snapshot the Power card was built from...
    private int _builtCharge;                  // ...compared in SyncTravelRow (rebuild only on a real change)
    private readonly Label _title = new() { AutoSize = true, Font = new Font("Segoe UI", 18f, FontStyle.Bold) };

    public SettingsPage(MainDeps d) : base(d)
    {
        _cur = Math.Clamp(d.Settings.SettingsSubTab, 0, GroupCount - 1);
        _title.Location = new Point(Pad, TitleTop);
        Controls.Add(_title);
        BuildForm();
        Resize += (_, _) => Layout2();
    }

    private IEnumerable<CardSection> AllCards() =>
        _gLeft.Concat(_gRight).Where(l => l != null).SelectMany(l => l);

    private void SelectSub(int i, bool save)
    {
        _cur = Math.Clamp(i, 0, GroupCount - 1);
        _subTabs?.SetActive(_cur);
        if (save && D.Settings.SettingsSubTab != _cur) { D.Settings.SettingsSubTab = _cur; D.SaveSettings(); }
        AutoScrollPosition = Point.Empty;   // each sub-page starts at its top
        ApplyVisibility();
        Layout2();
        // Layout2 re-enters itself through Resize when a child resize creates the vertical
        // scrollbar mid-pass (the overlay panel sets its own Width/Height), and the layout the
        // AutoScrollMinSize setter asks for is then dropped. One settled pass from HERE - never
        // from inside Layout2, which itself runs from Resize - recomputes the scroll extent.
        PerformLayout();
        Invalidate();
    }

    private void ApplyVisibility()
    {
        for (int gi = 1; gi < GroupCount; gi++)
        {
            foreach (var c in _gLeft[gi]) c.Visible = gi == _cur;
            foreach (var c in _gRight[gi]) c.Visible = gi == _cur;
        }
        foreach (var t in _tiles) t.Visible = _cur == SubHome;
        if (_homeHeader != null) _homeHeader.Visible = _cur == SubHome;
        if (_whatsNew != null) _whatsNew.Visible = _cur == SubHome;
        if (_overlayPanel != null) _overlayPanel.Visible = _cur == SubGaming;
    }

    public override void OnEnter()
    {
        // opt-in (discussion #9): land on the Start dashboard every time instead of resuming
        // the sub-tab you left. FocusScenVisibility runs AFTER OnEnter, so the gear deep link
        // from the Scenarios tab still wins - do not reorder those two.
        if (D.Settings.SettingsAlwaysStart && _cur != SubHome) SelectSub(SubHome, save: false);
        SyncTravelRow(); SyncExternal(); _overlayPanel?.SyncFromSettings(); RefreshTiles(); Layout2(); Invalidate();
    }

    // Thin themed rule between unrelated option groups inside one card (the Notifications
    // card hosts three of them). Reads Theme at paint time, so a theme switch needs no rewiring.
    private sealed class SepLine : Control
    {
        public SepLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 9;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Card);
            using var p = new Pen(Theme.Border);
            int y = Height / 2;
            e.Graphics.DrawLine(p, 0, y, Width, y);
        }
    }

    // Travel mode and the charge limit can change outside this page (CLI, a scene, the expiry
    // itself) and the Power-card rows are a build-time snapshot - rebuild when it went stale.
    private void SyncTravelRow()
    {
        if (_builtTravelOn == (D.Settings.TravelUntil != DateTime.MinValue) &&
            _builtCharge == D.Settings.ChargeLimit) return;
        Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
    }

    /// <summary>Clicking the Settings tab while already on it goes back to the Start dashboard.</summary>
    public override void OnReenter() => SelectSub(SubHome, save: true);

    public override void OnDeviceDbChanged() =>
        Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });   // the model-database row and the tier gates

    // The Display card is a snapshot of the target's mode list; a display-mode switch
    // (dock/undock, "second screen only") invalidates it, so rebuild - but ONLY then: the
    // app's own SetRefresh also raises the system event, and a full rebuild would yank the
    // scroll position out from under the click that caused it.
    public override void OnDisplayChanged()
    {
        if (Display.SupportedRates().SequenceEqual(_dispRates) && Display.Target() == _dispTarget)
        {
            SyncExternal();
            return;
        }
        Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
    }
    // Sync overlay toggles from settings (they can change via the Scenarios brick / tray / hotkey);
    // no re-layout here, which would reset the scroll position mid-edit.
    public override void LiveRefresh() { SyncTravelRow(); SyncExternal(); _overlayPanel?.SyncFromSettings(); RefreshTiles(); }

    // The Start page is a dashboard: every tile carries a live third line with the group's
    // current values, so all of it must be re-read whenever the page shows or state changes.
    private void RefreshTiles()
    {
        if (_tiles.Count < 6) return;
        var s = D.Settings;
        int li = Math.Max(0, Array.IndexOf(Lang.Codes, s.Language));
        _tiles[0].SetState(Lang.T(Theme.Dark ? "set_theme_dark" : "set_theme_light") + " · " + Lang.Names[li], null);

        string p = AppSettings.ChargeManaged(s.ChargeLimit) ? string.Format(Lang.T("st2_limit_on"), s.ChargeLimit) : Lang.T("st2_limit_off");
        if (s.AutoSwitchEnabled &&
            Enum.TryParse<ProfileId>(s.ProfileOnAC, out var pa) && Enum.TryParse<ProfileId>(s.ProfileOnBattery, out var pb))
            p += " · " + Profiles.Get(pa).Label + " / " + Profiles.Get(pb).Label;
        if (s.RefreshSwitchEnabled && s.RefreshOnAC > 0 && s.RefreshOnBattery > 0)
            p += " · " + string.Format(Lang.T("st2_hz"), s.RefreshOnAC, s.RefreshOnBattery);
        if (Display.Current() is > 0 and var curHz) p += " · " + curHz + " Hz";   // live panel rate
        _tiles[1].SetState(p, AppSettings.ChargeManaged(s.ChargeLimit) || s.RefreshSwitchEnabled);

        _tiles[2].SetState(s.TempAlertEnabled
                ? $"{s.TempAlertDegrees} °C / {s.TempAlertSeconds} s · OSD {s.OsdSeconds} s"
                : Lang.T("gen_off") + $" · OSD {s.OsdSeconds} s",
            s.TempAlertEnabled);

        int mc = Enum.GetValues<OverlayMetric>().Count(m => s.HasMetric(m));
        _tiles[3].SetState("Overlay " + Lang.T(s.OverlayEnabled ? "st_on" : "st_off") + " · " + string.Format(Lang.T("st2_metrics"), mc),
            s.OverlayEnabled);

        int en = Acts.Count(a => !s.Hotkeys.TryGetValue(a.key, out var hd) || hd.Enabled);
        _tiles[4].SetState(s.HotkeysEnabled ? string.Format(Lang.T("st2_hotkeys"), en, Acts.Length) : Lang.T("gen_off"),
            s.HotkeysEnabled);

        string sys = string.Format(Lang.T("st2_system"),
            Lang.T(s.Autostart ? "st_on" : "st_off"), Lang.T(s.UpdateCheckEnabled ? "st_on" : "st_off"));
        if (s.ExperimentalEnabled) sys += " · " + Lang.T("st2_exp");   // worth surfacing: it gates EC writes
        _tiles[5].SetState(sys, s.Autostart);

        foreach (var t in _tiles) t.SyncToggle();
        _homeHeader?.Invalidate();
        if (_whatsNew != null) { _whatsNew.ForeColor = Theme.Accent; _whatsNew.BackColor = Theme.Surface; }
    }

    // The page must always show the LIVE state: the language can change from the tray menu and
    // the theme from the header moon button while this page exists (built controls keep their
    // build-time values otherwise). Language drift = full rebuild (every label changes anyway);
    // theme drift = re-point the segment (Selected does not raise SelectedChanged, so no loop).
    private void SyncExternal()
    {
        if (_uiLang != Lang.CurrentCode)
        {
            Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
            return;
        }
        if (_themeSeg is { } ts) ts.Selected = Theme.Dark ? 1 : 0;
        if (_refreshNow is { IsDisposed: false })
            _refreshNow.Text = Display.Current() > 0 ? Display.Current() + " Hz" : "—";
        _syncRefreshMan?.Invoke();
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _title.ForeColor = Theme.Text;
        _title.BackColor = Theme.Surface;
        if (_themeSeg is { } ts) ts.Selected = Theme.Dark ? 1 : 0;   // follow the header toggle
        foreach (var c in AllCards()) c.ApplyTheme();
        _subTabs?.Invalidate();
        foreach (var t in _tiles) t.Invalidate();
        _overlayPanel?.ApplyThemeColors();
        Invalidate();
    }

    // No custom OnPaint: the title is a child Label and everything else is a child control, so the page
    // scrolls natively (smooth, no title/blit mismatch). The base clears to Theme.Surface.

    // Wrapped so the scroll extent is recomputed after the pass and the re-entrant Resize it used
    // to fight with is dropped - see ThemedPage.LayoutAndSyncScroll. SelectSub already did the
    // PerformLayout part by hand; now every entry point gets it, resize included.
    private void Layout2() => LayoutAndSyncScroll(Layout2Pass);

    private void Layout2Pass()
    {
        if (_subTabs == null) return;
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
        int top = TitleTop + _title.Height + 14;   // content coords throughout; offset applied at Location

        // The strip does not fit at the minimum window size in any language - let it collapse to
        // icons (the active one keeps its label, the hovered one expands in place) instead of
        // pushing a horizontal scrollbar onto the whole page.
        _subTabs.FitTo(ClientSize.Width - Pad * 2);
        _subTabs.Location = new Point(Pad + ox, top + oy);
        top += _subTabs.Height + 28;   // breathing room between the strip and the content

        if (_cur == SubHome)
        {
            // Start: status header, then one tile per group (3 per row, 2 on a narrow window),
            // then the "What's new" link
            float k = DeviceDpi / 96f;
            int gap = (int)(14 * k), th = (int)(110 * k);
            if (_homeHeader != null)
            {
                _homeHeader.SetBounds(Pad + ox, top + oy, fullW, (int)(44 * k));
                top += _homeHeader.Height + gap;
            }
            int cols = fullW < (int)(760 * k) ? 2 : 3;
            int tw = (fullW - gap * (cols - 1)) / cols;
            for (int i = 0; i < _tiles.Count; i++)
                _tiles[i].SetBounds(Pad + i % cols * (tw + gap) + ox, top + i / cols * (th + gap) + oy, tw, th);
            int rows = (_tiles.Count + cols - 1) / cols;
            int bottom = top + rows * (th + gap);
            if (_whatsNew != null) _whatsNew.Location = new Point(Pad + ox, bottom + oy);
            AutoScrollMinSize = new Size(0, bottom + (_whatsNew?.Height ?? 0) + 16);
            return;
        }

        if (_cur == SubGaming && _overlayPanel != null)
        {
            _overlayPanel.Relayout(fullW);
            _overlayPanel.Location = new Point(Pad + ox, top + oy);
            AutoScrollMinSize = new Size(0, top + _overlayPanel.Height + 20);
            return;
        }

        int yL = top, yR = top;
        foreach (var c in _gLeft[_cur]) { c.Relayout(colW); c.Location = new Point(Pad + ox, yL + oy); yL += c.Height + 16; }
        foreach (var c in _gRight[_cur]) { c.Relayout(colW); c.Location = new Point(Pad + colW + Gutter + ox, yR + oy); yR += c.Height + 16; }
        // Setting AutoScrollMinSize after positioning lets WinForms clamp AutoScrollPosition to the new
        // content extent and shift the children by that same delta, keeping everything consistent.
        // Width 0 = the horizontal extent comes from the children. Pinning it to Pad*2 + fullW made
        // it exactly the client width, so the 17 px the vertical scrollbar takes was always enough
        // to trip the horizontal one (the page had zero horizontal slack).
        AutoScrollMinSize = new Size(0, Math.Max(yL, yR) + 20);
    }

    // ---------------- build ----------------
    private void BuildForm()
    {
        foreach (var c in AllCards()) Controls.Remove(c);
        if (_overlayPanel != null) { Controls.Remove(_overlayPanel); _overlayPanel.Dispose(); _overlayPanel = null; }
        if (_subTabs != null) { Controls.Remove(_subTabs); _subTabs.Dispose(); _subTabs = null; }
        foreach (var t in _tiles) { Controls.Remove(t); t.Dispose(); }
        _tiles.Clear();
        if (_homeHeader != null) { Controls.Remove(_homeHeader); _homeHeader.Dispose(); _homeHeader = null; }
        if (_whatsNew != null) { Controls.Remove(_whatsNew); _whatsNew.Dispose(); _whatsNew = null; }
        for (int gi = 0; gi < GroupCount; gi++)
        {
            (_gLeft[gi] ??= new()).Clear();
            (_gRight[gi] ??= new()).Clear();
        }
        _boxes.Clear(); _swatches.Clear();
        _uiLang = Lang.CurrentCode;   // the form now reflects this language (see SyncExternal)

        // ---- left column ----
        var look = new CardSection(Lang.T("set_grp_look"), "");
        var theme = new SegControl(new[] { Lang.T("set_theme_light"), Lang.T("set_theme_dark") }, Theme.Dark ? 1 : 0) { Size = new Size(220, 34) };
        theme.SelectedChanged += i => { Theme.Set(i == 1); D.Settings.DarkMode = Theme.Dark; D.SaveSettings(); };
        _themeSeg = theme;
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
        _gLeft[SubGeneral].Add(look);

        var start = new CardSection(Lang.T("set_grp_start"), "");
        start.AddRow(Lang.T("set_autostart"), Toggle(D.Settings.Autostart, v => { D.Settings.Autostart = v; try { Autostart.Set(v); } catch { } D.SaveSettings(); }));
        start.AddRow(Lang.T("experimental_enable"), Toggle(D.Settings.ExperimentalEnabled, v => { D.Settings.ExperimentalEnabled = v; D.SaveSettings(); D.SettingsChanged(); }));
        _gLeft[SubSystem].Add(start);

        // ---- Power group: battery card + display card ----
        var power = new CardSection(Lang.T("set_grp_power"), "");
        // The three presets are the values MSI Center exposes and the only ones verified on real
        // hardware, so they stay one click away; "Custom" opens a slider for any threshold the
        // register accepts (20-100). The custom value is remembered, so switching 80 % <-> 73 %
        // is a click, not another aim with the mouse.
        bool chargeCustom = AppSettings.ChargeManaged(D.Settings.ChargeLimit) && !AppSettings.ChargeVerified(D.Settings.ChargeLimit);
        int chargeIdx = chargeCustom ? 4 : Math.Max(0, Array.IndexOf(ChargeVals, D.Settings.ChargeLimit));
        var charge = new SegControl(new[] { Lang.T("gen_off_short"), "60%", "80%", "100%", Lang.T("charge_custom") }, chargeIdx) { Size = new Size(360, 34) };
        charge.SelectedChanged += i =>
        {
            bool wasTravel = D.Settings.TravelUntil != DateTime.MinValue;
            bool wasCustom = chargeCustom;
            D.SetChargeLimit(i == 4 ? D.Settings.ChargeCustom : ChargeVals[i]);
            // a manual limit cancels a pending travel revert - flip the travel row back too;
            // entering or leaving Custom adds/removes the slider row, so rebuild for that too
            if ((wasTravel && D.Settings.TravelUntil == DateTime.MinValue) || wasCustom != (i == 4))
                Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
        };
        power.AddRow(Lang.T("set_charge"), charge);
        if (chargeCustom)
        {
            var slider = new Slider(AppSettings.ChargeMin, AppSettings.ChargeMax, D.Settings.ChargeLimit, 5, "%") { Width = 300 };
            slider.ValueChanged += v => { D.Settings.ChargeCustom = v; D.SetChargeLimit(v); };
            power.AddRow(Lang.T("charge_custom_row"), slider);
            // Tag "warn" is the card convention: ApplyTheme paints it amber and Relayout rewraps it
            // to the card width. A hand-set ForeColor is overwritten on the next theme pass, and a
            // fixed MaximumSize keeps the text in a narrow column - both were wrong here.
            var warn = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f), Tag = "warn", Text = "\u26A0  " + Lang.T("charge_custom_warn") };
            power.AddRow(null, warn);
        }

        // Travel mode: one-shot "charge to 100% until a date", then the previous limit comes
        // back on its own. State lives in TravelUntil, so the picker only chooses the length.
        bool travelOn = D.Settings.TravelUntil != DateTime.MinValue;
        _builtTravelOn = travelOn;                    // snapshot for SyncTravelRow
        _builtCharge = D.Settings.ChargeLimit;
        var travelBtn = new Button { Text = Lang.T(travelOn ? "travel_stop" : "travel_start"), AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        Ui.StyleGhost(travelBtn);
        // the help dot rides in a flow panel with the button, so it shows in both states
        Control WithTravelHelp(Control main)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
            main.Margin = new Padding(0);
            flow.Controls.Add(main);
            flow.Controls.Add(new HelpDot { TextProvider = () => Lang.T("travel_help"), Margin = new Padding(6, 4, 0, 0) });
            return flow;
        }
        if (!travelOn)
        {
            int[] travelDays = { 3, 7, 14, 30 };
            var travelSel = Combo(travelDays.Select(x => string.Format(Lang.T("travel_days_fmt"), x)).ToArray(), 1);
            travelBtn.Click += (_, _) =>
            {
                D.SetTravelDays(travelDays[Math.Max(0, travelSel.SelectedIndex)]);
                Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
            };
            power.AddRow(Lang.T("set_travel"), travelSel);
            power.AddRow("", WithTravelHelp(travelBtn));
        }
        else
        {
            var travelNote = new Label
            {
                Text = string.Format(Lang.T("travel_note"), D.Settings.TravelUntil.ToShortDateString()),
                AutoSize = true, MaximumSize = new Size(360, 0),
                Font = new Font("Segoe UI", 9f), Tag = "muted",
            };
            travelBtn.Click += (_, _) =>
            {
                D.SetTravelDays(0);
                Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
            };
            power.AddRow(Lang.T("set_travel"), WithTravelHelp(travelBtn));
            power.AddRow(null, travelNote);
        }
        power.AddRow(Lang.T("set_autoswitch"), Toggle(D.Settings.AutoSwitchEnabled, v => D.SetAutoSwitch(v)));
        var ac = Combo(Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray(), ProfileIndex(D.Settings.ProfileOnAC));
        ac.SelectedIndexChanged += (_, _) => { D.Settings.ProfileOnAC = Profiles.Get(Profiles.Order[ac.SelectedIndex]).Key; D.SaveSettings(); };
        power.AddRow(Lang.T("on_ac"), ac);
        var bat = Combo(Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray(), ProfileIndex(D.Settings.ProfileOnBattery));
        bat.SelectedIndexChanged += (_, _) => { D.Settings.ProfileOnBattery = Profiles.Get(Profiles.Order[bat.SelectedIndex]).Key; D.SaveSettings(); };
        power.AddRow(Lang.T("on_battery"), bat);

        // Some ECs wake from sleep/hibernation in Super Battery on their own — opt-in restore of
        // the chosen profile after resume and at startup (skipped while auto-switch manages profiles).
        power.AddRow(Lang.T("set_restore_profile"), Toggle(D.Settings.RestoreProfileOnResume,
            v => { D.Settings.RestoreProfileOnResume = v; D.SaveSettings(); }));
        // (#49) restore the last active fan curve too - the EC loses it on every cold boot
        power.AddRow(Lang.T("set_restore_curve"), Toggle(D.Settings.RestoreCurveOnResume,
            v => { D.Settings.RestoreCurveOnResume = v; D.SaveSettings(); }));
        _gLeft[SubPower].Add(power);

        // Scene schedule: different settings for work hours, nights and weekends. Rules are
        // edited in a small dialog; the whole page rebuilds after a change (import pattern).
        var sch = new CardSection(Lang.T("sch_grp"), "");   // MDL2 Calendar
        var schInfo = new Label
        {
            Text = Lang.T("sch_desc"), AutoSize = true, MaximumSize = new Size(360, 0),
            Font = new Font("Segoe UI", 9f), Tag = "muted",
        };
        sch.AddRow(null, schInfo);
        sch.AddRow(Lang.T("sch_enable"), Toggle(D.Settings.ScheduleEnabled,
            v => { D.Settings.ScheduleEnabled = v; D.SaveSettings(); }));
        string[] dayAbbr;
        try { dayAbbr = System.Globalization.CultureInfo.GetCultureInfo(Lang.CurrentCode).DateTimeFormat.AbbreviatedDayNames; }
        catch { dayAbbr = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames; }
        void RebuildAfterRules() { D.SaveSettings(); Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); }); }
        for (int ri = 0; ri < D.Settings.Schedules.Count; ri++)
        {
            var r = D.Settings.Schedules[ri];
            int idx = ri;
            var scName = D.Settings.Scenes.FirstOrDefault(s => s.Id.Equals(r.SceneId, StringComparison.OrdinalIgnoreCase))?.Name ?? "?";
            // common day sets get a name; anything else lists the abbreviations
            string days = r.Days switch
            {
                0x7F => Lang.T("sch_daily"),
                0x1F => Lang.T("sch_weekdays"),
                0x60 => Lang.T("sch_weekend"),
                _ => string.Join(" ", Enumerable.Range(0, 7).Where(i => (r.Days >> i & 1) != 0).Select(i => dayAbbr[(i + 1) % 7])),
            };
            var tg = new ToggleSwitch { Checked = r.Enabled };
            tg.Toggled += v => { r.Enabled = v; D.SaveSettings(); };
            // plain labels nested in a panel are NOT themed by CardSection - color explicitly;
            // wide enough for the whole summary (user request: no truncation)
            var lbl = new Label
            {
                Text = $"{scName} · {days} · {r.Start}-{r.End}", AutoEllipsis = true,
                AutoSize = false, Size = new Size(330, 24), Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Text, BackColor = Theme.Card,
            };
            // flat glyph hotspots like the scene cards (no boxed buttons), just this size
            Button Mk(string glyph, Color? fixedColor = null)
            {
                var b = new Button
                {
                    Text = glyph, Size = new Size(30, 28), Font = new Font("Segoe UI", 10f),
                    FlatStyle = FlatStyle.Flat, BackColor = Theme.Card,
                    ForeColor = fixedColor ?? Theme.Muted, TabStop = false, Cursor = Cursors.Hand,
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Theme.Card;
                b.FlatAppearance.MouseDownBackColor = Theme.Card;
                b.MouseEnter += (_, _) => { if (b.Enabled) b.ForeColor = fixedColor ?? Theme.Accent; };
                b.MouseLeave += (_, _) => b.ForeColor = fixedColor ?? Theme.Muted;
                return b;
            }
            var up = Mk("↑"); var down = Mk("↓"); var edit = Mk("✎"); var del = Mk("✕", Theme.Red);
            up.Enabled = idx > 0;
            down.Enabled = idx < D.Settings.Schedules.Count - 1;
            void Move(int dir)
            {
                int j = idx + dir;
                if (j < 0 || j >= D.Settings.Schedules.Count) return;
                D.Settings.Schedules.RemoveAt(idx);
                D.Settings.Schedules.Insert(j, r);
                RebuildAfterRules();
            }
            up.Click += (_, _) => Move(-1);     // list order = priority on overlapping windows
            down.Click += (_, _) => Move(1);
            edit.Click += (_, _) =>
            {
                var copy = r.Clone();
                using var dlg = new ScheduleRuleForm(D, copy);
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                r.SceneId = copy.SceneId; r.Days = copy.Days; r.Start = copy.Start; r.End = copy.End;
                RebuildAfterRules();
            };
            del.Click += (_, _) => { D.Settings.Schedules.Remove(r); RebuildAfterRules(); };
            var row = new Panel { Width = tg.Width + 8 + 330 + 8 + 4 * (30 + 4) - 4, Height = 32, BackColor = Theme.Card };
            tg.Location = new Point(0, (row.Height - tg.Height) / 2);
            lbl.Location = new Point(tg.Width + 8, (row.Height - lbl.Height) / 2);
            int bx = tg.Width + 8 + 330 + 8;
            foreach (var b in new[] { up, down, edit, del })
            {
                b.Location = new Point(bx, (row.Height - b.Height) / 2);
                bx += b.Width + 4;
                row.Controls.Add(b);
            }
            row.Controls.Add(tg); row.Controls.Add(lbl);
            sch.AddRow(null, row);
        }
        if (D.Settings.Scenes.Count == 0)
        {
            var need = new Label
            {
                Text = Lang.T("sch_need_scene"), AutoSize = true, MaximumSize = new Size(360, 0),
                Font = new Font("Segoe UI", 9f), Tag = "muted",
            };
            sch.AddRow(null, need);
        }
        else
        {
            var add = new Button { Text = "+  " + Lang.T("sch_add"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
            Ui.StyleGhost(add);
            add.Click += (_, _) =>
            {
                var nr = new ScheduleRule();
                using var dlg = new ScheduleRuleForm(D, nr);
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                D.Settings.Schedules.Add(nr);
                RebuildAfterRules();
            };
            sch.AddRow(null, add);
        }
        _gLeft[SubPower].Add(sch);

        // Display refresh-rate auto-switch (discussion #18): pure Windows API, works on every
        // model. Pickers list only the modes the panel reports at its current resolution.
        var disp = new CardSection(Lang.T("set_grp_display"), "");
        var rates = Display.SupportedRates();
        // live current panel rate first, so the switch rows below have a reference point
        _refreshNow = new Label { Text = Display.Current() > 0 ? Display.Current() + " Hz" : "—", AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        disp.AddRow(Lang.T("set_refresh_now"), _refreshNow);
        // which display the controls act on: the built-in panel when one is active, the
        // primary display otherwise (#69); EDID name appended when the panel reports one
        var target = Display.Target();
        _dispRates = rates; _dispTarget = target;   // snapshot compared in OnDisplayChanged
        var targetInfo = new Label
        {
            Text = target.Internal
                ? Lang.T("ref_panel_internal") + (target.Name is null ? "" : " · " + target.Name)
                : Lang.T("ref_panel_primary"),
            AutoSize = true, MaximumSize = new Size(360, 0),
            Font = new Font("Segoe UI", 9f), Tag = "muted",
        };
        disp.AddRow(null, targetInfo);
        // manual rate switch, same control as the Scenarios brick: a segmented button group
        // when the panel reports a handful of modes, a combo when there are many
        var manRates = Display.SupportedRates();
        if (manRates.Count > 1)
        {
            void ApplyMan(int hz)
            {
                int before = Display.Current();
                if (before != hz && Display.SetRefresh(hz))
                {
                    ChangeLog.Add(ChangeSource.Display, $"{before} Hz → {hz} Hz");
                    if (_refreshNow is { IsDisposed: false }) _refreshNow.Text = hz + " Hz";
                }
            }
            int manCur = Math.Max(0, manRates.IndexOf(Display.Current()));
            if (manRates.Count <= 4)
            {
                var seg = new SegControl(manRates.Select(r => r + " Hz").ToArray(), manCur) { Size = new Size(Math.Min(280, 74 * manRates.Count), 34) };
                seg.SelectedChanged += i => ApplyMan(manRates[i]);
                _syncRefreshMan = () => { int i = manRates.IndexOf(Display.Current()); if (i >= 0) seg.Selected = i; };
                disp.AddRow(Lang.T("set_refresh_set"), seg);
            }
            else
            {
                var man = Combo(manRates.Select(r => r + " Hz").ToArray(), manCur);
                man.SelectedIndexChanged += (_, _) => ApplyMan(manRates[Math.Max(0, man.SelectedIndex)]);
                _syncRefreshMan = () => { int i = manRates.IndexOf(Display.Current()); if (i >= 0 && man.SelectedIndex != i) man.SelectedIndex = i; };
                disp.AddRow(Lang.T("set_refresh_set"), man);
            }
        }
        disp.AddRow(Lang.T("set_refresh_toggle"), Toggle(D.Settings.RefreshSwitchEnabled, v => { D.Settings.RefreshSwitchEnabled = v; D.SaveSettings(); D.SettingsChanged(); }));
        string[] rateItems = new[] { Lang.T("ref_keep") }.Concat(rates.Select(r => r + " Hz")).ToArray();
        int RateIdx(int hz) { int i = rates.IndexOf(hz); return i < 0 ? 0 : i + 1; }
        var rAc = Combo(rateItems, RateIdx(D.Settings.RefreshOnAC));
        rAc.SelectedIndexChanged += (_, _) => { D.Settings.RefreshOnAC = rAc.SelectedIndex <= 0 ? 0 : rates[rAc.SelectedIndex - 1]; D.SaveSettings(); D.SettingsChanged(); };
        disp.AddRow(Lang.T("set_refresh_ac"), rAc);
        var rBat = Combo(rateItems, RateIdx(D.Settings.RefreshOnBattery));
        rBat.SelectedIndexChanged += (_, _) => { D.Settings.RefreshOnBattery = rBat.SelectedIndex <= 0 ? 0 : rates[rBat.SelectedIndex - 1]; D.SaveSettings(); D.SettingsChanged(); };
        disp.AddRow(Lang.T("set_refresh_batt"), rBat);
        if (rates.Count == 0) rAc.Enabled = rBat.Enabled = false;   // enumeration failed - leave visible but inert
        // HDR (advanced color) - same switch as Windows Settings → Display; only on capable
        // panels (or with MSIPS_FORCE_HDR=1 for UI testing). Scenes/CLI share the state.
        if (Hdr.Supported())
            disp.AddRow("HDR", Toggle(Hdr.Enabled(), v =>
            {
                try { if (Hdr.Set(v)) ChangeLog.Add(ChangeSource.Panel, "HDR: " + Lang.T(v ? "st_on" : "st_off")); } catch { }
            }));
        _gRight[SubPower].Add(disp);

        // (#51) Fan Boost auto-off: the one control users forget to switch back. Presets cover the
        // "quick blast" (30 s) and the "cool down after a session" (up to 15 min) cases; Custom…
        // asks for any value up to 2 h. Stored in seconds (AppSettings.FanBoostSeconds, 0 = never).
        int[] fbVals = { 0, 30, 60, 120, 180, 300, 600, 900 };
        string FbLabel(int sec) => sec == 0 ? Lang.T("fb_never")
            : sec < 60 ? string.Format(Lang.T("fb_secs"), sec)
            : string.Format(Lang.T("fb_mins"), sec / 60);
        var fbItems = fbVals.Select(FbLabel).Append(Lang.T("fb_custom")).ToArray();
        int fbCur = D.Settings.FanBoostSeconds;
        int fbIdx = Array.IndexOf(fbVals, fbCur);
        // a custom value keeps its own label in the list so the current setting is always visible
        if (fbIdx < 0) { fbItems = fbVals.Select(FbLabel).Append(FbLabel(fbCur)).Append(Lang.T("fb_custom")).ToArray(); fbIdx = fbVals.Length; }
        var fb = Combo(fbItems, Math.Max(0, fbIdx));
        fb.SelectedIndexChanged += (_, _) =>
        {
            if (fb.SelectedIndex == fb.Items.Count - 1)   // Custom…
            {
                string? txt = InputDialog.Ask(FindForm(), Lang.T("cooler_boost"), Lang.T("fb_custom_ask"),
                    (Math.Max(60, D.Settings.FanBoostSeconds) / 60).ToString());
                if (int.TryParse(txt, out int mins) && mins is >= 1 and <= 120)
                {
                    D.Settings.FanBoostSeconds = mins * 60;
                    D.SaveSettings();
                    Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });   // relabel the list
                    return;
                }
                fb.SelectedIndex = Math.Max(0, Array.IndexOf(fbVals, D.Settings.FanBoostSeconds));
                return;
            }
            if (fb.SelectedIndex < fbVals.Length) { D.Settings.FanBoostSeconds = fbVals[fb.SelectedIndex]; D.SaveSettings(); }
        };
        power.AddRow(Lang.T("set_fb_timer"), fb);

        // (#14) Battery health - read-only wear data from the root\wmi battery classes.
        var bh = BatteryHealth.Read();
        var batt = new CardSection(Lang.T("set_grp_batt"), "");
        Label BhVal(string txt) => new() { Text = txt, AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        batt.AddRow(Lang.T("bh_design"), BhVal(bh.DesignMWh > 0 ? $"{bh.DesignMWh / 1000f:0.0} Wh" : "—"));
        batt.AddRow(Lang.T("bh_full"), BhVal(bh.FullMWh > 0 ? $"{bh.FullMWh / 1000f:0.0} Wh" : "—"));
        batt.AddRow(Lang.T("bh_wear"), BhVal(bh.WearPct >= 0 ? $"{bh.WearPct} %" : "—"));
        batt.AddRow(Lang.T("bh_cycles"), BhVal(bh.Cycles > 0 ? bh.Cycles.ToString() : "—"));
        _gRight[SubPower].Add(batt);

        // Battery-level rules: e.g. below 30 % -> Super Battery, above 80 % -> Balanced.
        // Direction-aware and edge-triggered (see TrayContext.CheckBatteryRules).
        var brc = new CardSection(Lang.T("bat_rules_grp"), "");   // MDL2 LightningBolt
        var brInfo = new Label
        {
            Text = Lang.T("bat_rules_desc"), AutoSize = true, MaximumSize = new Size(360, 0),
            Font = new Font("Segoe UI", 9f), Tag = "muted",
        };
        brc.AddRow(null, brInfo);
        // master switch for the whole feature - some people simply don't want it running
        brc.AddRow(Lang.T("bat_enable"), Toggle(D.Settings.BattRulesEnabled,
            v => { D.Settings.BattRulesEnabled = v; D.SaveSettings(); }));
        var actionVals = Profiles.Order.Select(id => "P:" + Profiles.Get(id).Key)
            .Concat(D.Settings.Scenes.Select(s => "S:" + s.Id)).ToArray();
        var actionNames = Profiles.Order.Select(id => Profiles.Get(id).Label)
            .Concat(D.Settings.Scenes.Select(s => "▶ " + s.Name)).ToArray();
        var pctVals = Enumerable.Range(1, 19).Select(i => i * 5).ToArray();   // 5..95
        Panel BattRow(bool low)
        {
            var tg = Toggle(low ? D.Settings.BattLowEnabled : D.Settings.BattHighEnabled, v =>
            {
                if (low) D.Settings.BattLowEnabled = v; else D.Settings.BattHighEnabled = v;
                D.SaveSettings();
            });
            var pct = new ThemedComboBox { Width = 84 };
            pct.Items.AddRange(pctVals.Select(v => (object)(v + " %")).ToArray());
            pct.SelectedIndex = Math.Max(0, Array.IndexOf(pctVals, low ? D.Settings.BattLowPct : D.Settings.BattHighPct));
            pct.SelectedIndexChanged += (_, _) =>
            {
                int v = pctVals[Math.Max(0, pct.SelectedIndex)];
                if (low) D.Settings.BattLowPct = v; else D.Settings.BattHighPct = v;
                D.SaveSettings();
            };
            var act = new ThemedComboBox { Width = 168 };
            act.Items.AddRange(actionNames.Cast<object>().ToArray());
            act.SelectedIndex = Math.Max(0, Array.IndexOf(actionVals, low ? D.Settings.BattLowAction : D.Settings.BattHighAction));
            act.SelectedIndexChanged += (_, _) =>
            {
                string v = actionVals[Math.Max(0, act.SelectedIndex)];
                if (low) D.Settings.BattLowAction = v; else D.Settings.BattHighAction = v;
                D.SaveSettings();
            };
            var row = new Panel { Width = tg.Width + 8 + 84 + 8 + 168, Height = 30 };
            tg.Location = new Point(0, (row.Height - tg.Height) / 2);
            pct.Location = new Point(tg.Width + 8, (row.Height - pct.Height) / 2);
            act.Location = new Point(tg.Width + 8 + 84 + 8, (row.Height - act.Height) / 2);
            row.Controls.Add(tg); row.Controls.Add(pct); row.Controls.Add(act);
            return row;
        }
        brc.AddRow(Lang.T("bat_below"), BattRow(true));
        brc.AddRow(Lang.T("bat_above"), BattRow(false));
        _gRight[SubPower].Add(brc);

        // Thermal notifications: OSD + tray balloon when CPU/GPU stays above the threshold for
        // the chosen time. Off by default — the user opts in.
        var alerts = new CardSection(Lang.T("set_grp_alerts"), "");
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
        // SSD alert: same opt-in pattern, but the data comes from Windows storage APIs
        // (Perf.Disks), not the EC. Dwell is fixed (30 s) - disk heat moves slowly.
        alerts.AddRow(null, new SepLine());
        alerts.AddRow(Lang.T("ssd_enable"), Toggle(D.Settings.SsdAlertEnabled, v => { D.Settings.SsdAlertEnabled = v; D.SaveSettings(); }));
        int[] ssdVals = { 55, 60, 65, 70, 75, 80 };
        var ssdDeg = Combo(ssdVals.Select(x => x + " °C").ToArray(), Math.Max(0, Array.IndexOf(ssdVals, D.Settings.SsdAlertDegrees)));
        ssdDeg.SelectedIndexChanged += (_, _) => { D.Settings.SsdAlertDegrees = ssdVals[Math.Max(0, ssdDeg.SelectedIndex)]; D.SaveSettings(); };
        alerts.AddRow(Lang.T("ssd_threshold"), ssdDeg);
        // Someone else moving the charge threshold (MSI Center and its installer do) is not a hardware
        // alarm, it is "your setting no longer applies" - opt-OUT, because silence there means the
        // app shows a limit that is not in the EC any more.
        alerts.AddRow(null, new SepLine());
        alerts.AddRow(Lang.T("charge_ext_enable"), Toggle(D.Settings.ChargeExternalNotify, v => { D.Settings.ChargeExternalNotify = v; D.SaveSettings(); }));
        // How long OSD toasts stay fully visible; the temperature alert enforces a 5 s minimum.
        alerts.AddRow(null, new SepLine());
        int[] osdVals = Enumerable.Range(1, 15).ToArray();
        var osdCombo = Combo(osdVals.Select(x => x + " s").ToArray(), Math.Max(0, Array.IndexOf(osdVals, D.Settings.OsdSeconds)));
        osdCombo.SelectedIndexChanged += (_, _) => { D.Settings.OsdSeconds = osdVals[Math.Max(0, osdCombo.SelectedIndex)]; D.SaveSettings(); D.SettingsChanged(); };
        alerts.AddRow(Lang.T("set_osd_secs"), osdCombo);
        // One button back to stock: with three alert groups in one card (and more to come),
        // undoing an experiment by hand means remembering six values.
        var alertsReset = new Button { Text = Lang.T("set_defaults"), AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        Ui.StyleGhost(alertsReset);
        alertsReset.Click += (_, _) =>
        {
            var d = new AppSettings();
            D.Settings.TempAlertEnabled = d.TempAlertEnabled;
            D.Settings.TempAlertDegrees = d.TempAlertDegrees;
            D.Settings.TempAlertSeconds = d.TempAlertSeconds;
            D.Settings.SsdAlertEnabled = d.SsdAlertEnabled;
            D.Settings.SsdAlertDegrees = d.SsdAlertDegrees;
            D.Settings.ChargeExternalNotify = d.ChargeExternalNotify;
            D.Settings.OsdSeconds = d.OsdSeconds;
            D.SaveSettings(); D.SettingsChanged();
            Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });
        };
        alerts.AddRow("", alertsReset);
        _gLeft[SubNotif].Add(alerts);
        // (the game-session report options live in the Gaming-overlay panel, next to the feature)

        var upd = new CardSection(Lang.T("set_grp_updates"), "");
        upd.AddRow(Lang.T("set_check_updates"), Toggle(D.Settings.UpdateCheckEnabled, v => { D.Settings.UpdateCheckEnabled = v; D.SaveSettings(); }));
        // Signed model database (ModelDb): which data is in effect, plus a manual fetch. The
        // check runs on its own cadence (every start, on the Models tab, and this button), so
        // the row also reports the result of the last press.
        var dbLabel = new Label { AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold) };
        // The status of the last press goes on its OWN full-width note row. Appending it to the
        // value label grew the right-aligned row control until it ran off the card.
        // Idle it collapses to zero height, so the card shows no empty gap. AutoSize is what
        // gives it a height, hence the toggle (Visible is useless here: a child of a form that is
        // not shown yet reports false, which would break the layout during construction).
        var dbNote = new Label { AutoSize = false, Size = new Size(1, 0), Font = new Font("Segoe UI", 9f), Tag = "muted" };
        void SyncDbLabel(string? note = null)
        {
            dbLabel.Text = Devices.EffectiveDataVersion
                         + (Devices.UsingOverride ? "  ·  " + Lang.T("modeldb_downloaded") : "");
            if (ModelDb.PendingVersion() is { } pend)
                dbLabel.Text += "  ·  " + string.Format(Lang.T("modeldb_pending"), pend);
            dbNote.Text = note ?? "";
            if (note == null) { dbNote.AutoSize = false; dbNote.Size = new Size(1, 0); }
            else dbNote.AutoSize = true;
            Layout2();   // the note changes the card height; re-measure instead of overlapping
        }
        var dbCheck = new Button { Text = Lang.T("modeldb_check"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(dbCheck);
        dbCheck.Click += (_, _) =>
        {
            dbCheck.Enabled = false;
            SyncDbLabel(Lang.T("modeldb_checking"));
            D.CheckModelDbNow(code =>
            {
                if (IsDisposed || dbLabel.IsDisposed) return;
                void Show()
                {
                    dbCheck.Enabled = true;
                    SyncDbLabel(code switch
                    {
                        > 0  => string.Format(Lang.T("modeldb_applied"), code),
                        -1   => Lang.T("modeldb_failed"),
                        -2   => Lang.T("modeldb_deferred"),
                        _    => Lang.T("modeldb_current"),
                    });
                }
                if (InvokeRequired) BeginInvoke(Show); else Show();
            });
        };
        var dbRow = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty,
        };
        dbLabel.Margin = new Padding(0, 8, 14, 0);
        dbRow.Controls.Add(dbLabel);
        dbRow.Controls.Add(dbCheck);
        upd.AddRow(Lang.T("set_modeldb"), dbRow);
        upd.AddRow(null, dbNote);
        SyncDbLabel();
        _gLeft[SubSystem].Add(upd);

        // Tray context-menu visibility toggles (discussion #9); all default on.
        var tray = new CardSection(Lang.T("set_grp_tray"), "");
        // (#23) Mouse actions on the tray icon itself: left / middle click and the scroll wheel.
        TrayAction[] actOrder =
        {
            TrayAction.None, TrayAction.CycleProfile, TrayAction.FanBoost, TrayAction.Overlay,
            TrayAction.ShowState, TrayAction.PanicReset, TrayAction.OpenScenarios, TrayAction.OpenStatus,
            TrayAction.OpenFanCurve, TrayAction.OpenSettings, TrayAction.OpenModels, TrayAction.OpenChangeLog,
        };
        string ActName(TrayAction a) => a switch
        {
            TrayAction.None => Lang.T("act_none"),
            TrayAction.CycleProfile => Lang.T("cycle"),
            TrayAction.FanBoost => Lang.T("cooler_boost"),
            TrayAction.Overlay => Lang.T("overlay_title"),
            TrayAction.ShowState => Lang.T("act_show_state"),
            TrayAction.PanicReset => Lang.T("hk_panic"),
            TrayAction.OpenScenarios => string.Format(Lang.T("act_open"), Lang.T("tab_scenarios")),
            TrayAction.OpenStatus => string.Format(Lang.T("act_open"), Lang.T("menu_status")),
            TrayAction.OpenFanCurve => string.Format(Lang.T("act_open"), Lang.T("fc_title")),
            TrayAction.OpenSettings => string.Format(Lang.T("act_open"), Lang.T("menu_settings")),
            TrayAction.OpenModels => string.Format(Lang.T("act_open"), Lang.T("tab_models")),
            TrayAction.OpenChangeLog => string.Format(Lang.T("act_open"), Lang.T("menu_log")),
            _ => "",
        };
        var actNames = actOrder.Select(ActName).ToArray();
        ComboBox ActCombo(int cur, Action<int> set)
        {
            var c = Combo(actNames, Math.Max(0, Array.IndexOf(actOrder, (TrayAction)cur)));
            c.SelectedIndexChanged += (_, _) => { set((int)actOrder[Math.Max(0, c.SelectedIndex)]); D.SaveSettings(); D.SettingsChanged(); };
            return c;
        }
        tray.AddRow(Lang.T("set_tray_left"), ActCombo(D.Settings.TrayClickLeft, v => D.Settings.TrayClickLeft = v));
        tray.AddRow(Lang.T("set_tray_mid"), ActCombo(D.Settings.TrayClickMiddle, v => D.Settings.TrayClickMiddle = v));
        var wheelModes = new[] { TrayWheelMode.None, TrayWheelMode.Profiles, TrayWheelMode.Scenes, TrayWheelMode.KbdLight };
        var wheelNames = new[] { Lang.T("act_none"), Lang.T("twa_profiles"), Lang.T("twa_scenes"), Lang.T("twa_kbd") };
        var wh = Combo(wheelNames, Math.Max(0, Array.IndexOf(wheelModes, (TrayWheelMode)D.Settings.TrayWheelMode)));
        wh.SelectedIndexChanged += (_, _) => { D.Settings.TrayWheelMode = (int)wheelModes[Math.Max(0, wh.SelectedIndex)]; D.SaveSettings(); D.SettingsChanged(); };
        tray.AddRow(Lang.T("set_tray_wheel"), wh);
        tray.AddRow(Lang.T("menu_status"), Toggle(D.Settings.TrayShowStatus, v => { D.Settings.TrayShowStatus = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("fc_title"), Toggle(D.Settings.TrayShowFanCurve, v => { D.Settings.TrayShowFanCurve = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("tab_models"), Toggle(D.Settings.TrayShowModels, v => { D.Settings.TrayShowModels = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("tray_report"), Toggle(D.Settings.TrayShowReport, v => { D.Settings.TrayShowReport = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("menu_log"), Toggle(D.Settings.TrayShowChangeLog, v => { D.Settings.TrayShowChangeLog = v; D.SaveSettings(); D.SettingsChanged(); }));
        tray.AddRow(Lang.T("menu_feedback"), Toggle(D.Settings.TrayShowFeedback, v => { D.Settings.TrayShowFeedback = v; D.SaveSettings(); D.SettingsChanged(); }));
        _gRight[SubSystem].Add(tray);

        // Interface: background grid on/off + which main tabs collapse to icon buttons on the
        // right of the strip (e.g. keep Models reachable but out of the tab row).
        var uiSec = new CardSection(Lang.T("set_grp_ui"), "");
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
        _gRight[SubGeneral].Add(uiSec);

        // Which quick-control bricks (and the Scenes section) the Scenarios tab shows - not
        // everyone wants the full wall of switches there.
        var scenVis = new CardSection(Lang.T("set_grp_scen"), "");   // MDL2 Tiles glyph
        void VisRow(string key, string label)
        {
            scenVis.AddRow(label, Toggle(!D.Settings.ScenHidden.Contains(key), v =>
            {
                if (v) D.Settings.ScenHidden.Remove(key);
                else if (!D.Settings.ScenHidden.Contains(key)) D.Settings.ScenHidden.Add(key);
                D.SaveSettings(); D.SettingsChanged();
            }));
        }
        VisRow("fanboost", Lang.T("cooler_boost"));
        VisRow("overlay", Lang.T("overlay_title"));
        VisRow("charge", Lang.T("st_charge"));
        VisRow("autoswitch", Lang.T("scen_autoswitch"));
        if (Display.SupportedRates().Count > 1) VisRow("refresh", Lang.T("ref_title"));
        if (D.KbdLevel() >= 0) VisRow("kbd", Lang.T("kbd_title"));
        if (D.WebcamState() >= 0) VisRow("webcam", Lang.T("webcam_title"));
        VisRow("winlock", Lang.T("winlock_title"));
        if (D.TouchpadState() >= 0) VisRow("touchpad", Lang.T("tp_title"));
        VisRow("panic", Lang.T("hk_panic"));
        VisRow("scenes", Lang.T("scene_title"));
        _gLeft[SubGeneral].Add(scenVis);   // left column (user request; the right one is crowded)

        // (discussion #9) Settings can land on the Start dashboard every time instead of
        // resuming the last sub-tab. Off by default = the behaviour people already know.
        var navCard = new CardSection(Lang.T("set_grp_nav"), "");
        var navInfo = new Label
        {
            Text = Lang.T("set_always_start_desc"), AutoSize = true, MaximumSize = new Size(360, 0),
            Font = new Font("Segoe UI", 9f), Tag = "muted",
        };
        navCard.AddRow(null, navInfo);
        navCard.AddRow(Lang.T("set_always_start"), Toggle(D.Settings.SettingsAlwaysStart,
            v => { D.Settings.SettingsAlwaysStart = v; D.SaveSettings(); }));
        _gRight[SubGeneral].Add(navCard);
        _scenVisCard = scenVis;            // gear on the Scenarios tab jumps here and flashes it

        // Settings backup: export = a copy of settings.json, import = adopt the preferences from
        // such a file. Machine-local state survives an import (see AppSettings.ImportFrom).
        var backup = new CardSection(Lang.T("set_grp_backup"), "");
        var expBtn = new Button { Text = Lang.T("set_export"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(expBtn);
        expBtn.Click += (_, _) => ExportSettings();
        var impBtn = new Button { Text = Lang.T("set_import"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(impBtn);
        impBtn.Click += (_, _) => ImportSettings();
        var bRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
        bRow.Controls.Add(expBtn); bRow.Controls.Add(impBtn);
        backup.AddRow(null, bRow);
        _gLeft[SubSystem].Add(backup);   // left column (user request)

        // One-click diagnostics (#30 on the roadmap): everything a bug report needs, one zip.
        var diag = new CardSection(Lang.T("set_grp_diag"), "");
        var diagBtn = new Button { Text = Lang.T("diag_save"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(diagBtn);
        diagBtn.Click += (_, _) => SaveDiagnostics();
        // plain-sight description of exactly what gets collected (user request)
        var diagInfo = new Label
        {
            Text = Lang.T("diag_desc"), AutoSize = true, MaximumSize = new Size(360, 0),
            Font = new Font("Segoe UI", 9f), Tag = "muted",
        };
        diag.AddRow(null, diagInfo);
        diag.AddRow(null, diagBtn);
        _gRight[SubSystem].Add(diag);

        // (discussion #9) Temperature readouts in the notification area - two separate icons,
        // because a tray icon is 16x16 px at 100% scaling: room for two bold digits, not two
        // values. Hidden on machines we cannot read temperatures from.
        if (D.Status().Known || D.Hw().CpuTemp > 0)
        {
            var tt = new CardSection(Lang.T("temptray_grp"), "");
            var ttInfo = new Label
            {
                Text = Lang.T("temptray_desc"), AutoSize = true, MaximumSize = new Size(360, 0),
                Font = new Font("Segoe UI", 9f), Tag = "muted",
            };
            tt.AddRow(null, ttInfo);
            tt.AddRow(Lang.T("st_cpu_temp"), Toggle(D.Settings.TempTrayCpu,
                v => { D.Settings.TempTrayCpu = v; D.SaveSettings(); D.SettingsChanged(); }));
            tt.AddRow(Lang.T("st_gpu_temp"), Toggle(D.Settings.TempTrayGpu,
                v => { D.Settings.TempTrayGpu = v; D.SaveSettings(); D.SettingsChanged(); }));
            var warnVals = new[] { 50, 55, 60, 65, 70, 75, 80 };
            var warn = Combo(warnVals.Select(x => x + " °C").ToArray(), Math.Max(0, Array.IndexOf(warnVals, D.Settings.TempTrayWarn)));
            warn.SelectedIndexChanged += (_, _) =>
            {
                D.Settings.TempTrayWarn = warnVals[Math.Max(0, warn.SelectedIndex)];
                if (D.Settings.TempTrayHot <= D.Settings.TempTrayWarn) D.Settings.TempTrayHot = D.Settings.TempTrayWarn + 10;
                D.SaveSettings(); D.SettingsChanged();
            };
            tt.AddRow(Lang.T("temptray_warn"), warn);
            var hotVals = new[] { 75, 80, 85, 90, 95, 100 };
            var hot = Combo(hotVals.Select(x => x + " °C").ToArray(), Math.Max(0, Array.IndexOf(hotVals, D.Settings.TempTrayHot)));
            hot.SelectedIndexChanged += (_, _) =>
            {
                D.Settings.TempTrayHot = Math.Max(D.Settings.TempTrayWarn + 1, hotVals[Math.Max(0, hot.SelectedIndex)]);
                D.SaveSettings(); D.SettingsChanged();
            };
            tt.AddRow(Lang.T("temptray_hot"), hot);
            tt.AddRow(null, ColorRow(
                (Lang.T("temptray_ok"), () => D.Settings.TempTrayColorOk, v => D.Settings.TempTrayColorOk = v),
                (Lang.T("temptray_warn_c"), () => D.Settings.TempTrayColorWarn, v => D.Settings.TempTrayColorWarn = v),
                (Lang.T("temptray_hot_c"), () => D.Settings.TempTrayColorHot, v => D.Settings.TempTrayColorHot = v)));
            var ttReset = new Button { Text = Lang.T("temptray_reset"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
            Ui.StyleGhost(ttReset);
            ttReset.Click += (_, _) =>
            {
                var def = new AppSettings();
                D.Settings.TempTrayColorOk = def.TempTrayColorOk;
                D.Settings.TempTrayColorWarn = def.TempTrayColorWarn;
                D.Settings.TempTrayColorHot = def.TempTrayColorHot;
                D.Settings.TempTrayWarn = def.TempTrayWarn;
                D.Settings.TempTrayHot = def.TempTrayHot;
                D.SaveSettings(); D.SettingsChanged();
                Ui.BatchRedraw(this, () => { BuildForm(); Layout2(); });   // swatches + combos follow
            };
            tt.AddRow(null, ttReset);
            _gRight[SubSystem].Add(tt);
        }

        // (#27) Advanced privacy option: hard camera block (0x2F) - locks the camera off below
        // the Fn key and the Scenarios switch until lifted here (or by a panic reset).
        if (D.WebcamState() >= 0)
        {
            var priv = new CardSection(Lang.T("set_grp_privacy"), "");
            var privInfo = new Label
            {
                Text = "⚠  " + Lang.T("webcam_block_desc"), AutoSize = true, MaximumSize = new Size(360, 0),
                Font = new Font("Segoe UI", 9f), Tag = "warn",
            };
            priv.AddRow(null, privInfo);
            // Two-step, no popup: flipping the toggle ON only ARMS the block - an inline confirm
            // button appears and the switch snaps back until it is pressed. OFF applies at once
            // (lifting a lock needs no friction). ToggleSwitch.Checked setter fires no event.
            var tgBlock = new ToggleSwitch { Checked = D.WebcamBlocked() };
            var confirm = new Button
            {
                Text = Lang.T("webcam_block_confirm"), AutoSize = true,
                Padding = new Padding(10, 2, 10, 2), Visible = false,
            };
            Ui.StyleGhost(confirm);
            confirm.ForeColor = Theme.Amber;
            tgBlock.Toggled += v =>
            {
                if (v) { tgBlock.Checked = false; confirm.Visible = true; }
                else { confirm.Visible = false; D.SetWebcamBlock(false); }
            };
            confirm.Click += (_, _) =>
            {
                confirm.Visible = false;
                D.SetWebcamBlock(true);
                tgBlock.Checked = true;
            };
            priv.AddRow(Lang.T("webcam_block"), tgBlock);
            priv.AddRow(null, confirm);
            _gLeft[SubSystem].Add(priv);
        }

        // Windows-key lock + touchpad: the same switches as the Scenarios bricks, mirrored
        // here so every input/device toggle also has a home in Settings (user request).
        var wlCard = new CardSection(Lang.T("winlock_title"), "");   // MDL2 Lock
        var wlInfo = new Label
        {
            Text = Lang.T("winlock_hint"), AutoSize = true, MaximumSize = new Size(360, 0),
            Font = new Font("Segoe UI", 9f), Tag = "muted",
        };
        wlCard.AddRow(null, wlInfo);
        wlCard.AddRow(Lang.T("winlock_title"), Toggle(D.WinLockOn(), v => D.SetWinLock(v)));
        _gLeft[SubSystem].Add(wlCard);

        if (D.TouchpadState() >= 0)
        {
            var tpCard = new CardSection(Lang.T("tp_title"), "");   // MDL2 TouchPad
            var tpInfo = new Label
            {
                Text = Lang.T("tp_hint"), AutoSize = true, MaximumSize = new Size(360, 0),
                Font = new Font("Segoe UI", 9f), Tag = "muted",
            };
            tpCard.AddRow(null, tpInfo);
            tpCard.AddRow(Lang.T("tp_title"), Toggle(D.TouchpadState() == 1, v => D.SetTouchpad(v)));
            _gLeft[SubSystem].Add(tpCard);
        }

        // Fn/Win key swap - EC-persisted layout switch (msi-ec fn_win_swap), only on mapped boards.
        if (D.FnLeft() >= 0)
        {
            var fnCard = new CardSection(Lang.T("fnswap_grp"), "");
            var fnInfo = new Label
            {
                Text = Lang.T("fnswap_desc"), AutoSize = true, MaximumSize = new Size(360, 0),
                Font = new Font("Segoe UI", 9f), Tag = "muted",
            };
            var fnSeg = new SegControl(new[] { Lang.T("fnswap_left"), Lang.T("fnswap_right") },
                D.FnLeft() == 1 ? 0 : 1) { Size = new Size(280, 34) };
            fnSeg.SelectedChanged += i => D.SetFnLeft(i == 0);
            fnCard.AddRow(null, fnInfo);
            fnCard.AddRow(null, fnSeg);
            _gLeft[SubSystem].Add(fnCard);
        }

        var hk = new CardSection(Lang.T("set_hotkeys"), "");
        // Second card so BOTH columns of the Hotkeys group are used: fixed actions on the
        // left, per-scene shortcuts on the right (the right half of a wide window used to
        // stay empty). Added only when at least one scene exists.
        var hkScenes = new CardSection(Lang.T("scene_title"), "");
        _hkToggles.Clear();
        _hkMaster = new ToggleSwitch { Checked = D.Settings.HotkeysEnabled };
        _hkMaster.Toggled += v => { D.Settings.HotkeysEnabled = v; UpdateHotkeyRowsEnabled(); D.SaveSettings(); D.SettingsChanged(); };
        hk.AddRow(Lang.T("hk_all"), _hkMaster);   // master on/off (#9), default on
        if (TrayContext.HotkeysRefused.Count > 0)
        {
            var warn = new Label { AutoSize = true, Font = new Font("Segoe UI", 9f), Tag = "warn", Text = "\u26A0  " + Lang.T("hk_refused_row") };
            hk.AddRow(null, warn);
        }
        // static actions + one row per scene (#21); scene rows label with the scene's name
        var hkRows = Acts.Select(a => (a.key, a.label, scene: false)).ToList();
        foreach (var s in D.Settings.Scenes) hkRows.Add((s.HotkeyKey, s.Name, true));
        foreach (var (key, label, isScene) in hkRows)
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
            // A shortcut Windows refused (another app owns the combination) is marked right on
            // its row - it used to look identical to one that works (issue #92).
            string mark = TrayContext.HotkeysRefused.Contains(key) ? "  ⚠" : "";
            (isScene ? hkScenes : hk).AddRow(mark + (key == "Cycle" ? Lang.T("cycle") : key == "CoolerBoost" ? Lang.T("cooler_boost") : key == "Overlay" ? Lang.T("overlay_title") : key == "OverlayLock" ? Lang.T("ov_lock_menu") : key == "PanicReset" ? Lang.T("hk_panic") : key == "KbdLight" ? Lang.T("kbd_title") : key == "Webcam" ? Lang.T("webcam_title") : key == "EcView" ? Lang.T("ec_view_title") : key == "WinLock" ? Lang.T("winlock_title") : key == "Touchpad" ? Lang.T("tp_title") : label), row);
        }
        var reset = new Button { Text = Lang.T("set_default"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(reset);
        reset.Click += (_, _) => ResetHotkeys();
        hk.AddRow(null, reset);
        _gLeft[SubHotkeys].Add(hk);
        if (D.Settings.Scenes.Count > 0) _gRight[SubHotkeys].Add(hkScenes);
        UpdateHotkeyRowsEnabled();

        // Application icon: visual tiles; clicking one applies it immediately (#9).
        var iconCard = new CardSection(Lang.T("set_app_icon"), "");
        iconCard.AddRow(null, new IconStylePicker(D));
        _gRight[SubGeneral].Add(iconCard);

        _overlayPanel = new OverlaySettingsPanel(D);
        Controls.Add(_overlayPanel);

        // ---- sub-tab strip (Start + six groups, MDL2 glyphs like the main tab strip) ----
        _subTabs = new SubTabs(
            new[]
            {
                Lang.T("set_sub_home"), Lang.T("set_sub_general"), Lang.T("set_grp_power"),
                Lang.T("set_grp_alerts"), Lang.T("set_sub_gaming"), Lang.T("set_sub_hotkeys"),
                Lang.T("set_sub_system"),
            },
            new[] { "", "", "", "", "", "", "" });
        _subTabs.Changed += i => SelectSub(i, save: true);
        _subTabs.SetActive(_cur);
        Controls.Add(_subTabs);

        // ---- Start page: one clickable tile per group ----
        (int g, string glyph, string titleKey, string descKey)[] tiles =
        {
            (SubGeneral, "", "set_sub_general", "set_tile_general"),
            (SubPower,   "", "set_grp_power",   "set_tile_power"),
            (SubNotif,   "", "set_grp_alerts",  "set_tile_notif"),
            (SubGaming,  "", "set_sub_gaming",  "set_tile_gaming"),
            (SubHotkeys, "", "set_sub_hotkeys", "set_tile_hotkeys"),
            (SubSystem,  "", "set_sub_system",  "set_tile_system"),
        };
        foreach (var t in tiles)
        {
            int gi = t.g;
            var tile = new GroupTile(t.glyph, Lang.T(t.titleKey), Lang.T(t.descKey), () => SelectSub(gi, save: true));
            _tiles.Add(tile);
            Controls.Add(tile);
        }
        // Quick master switches straight on the Start tiles - only where the group has one
        // obvious main on/off (Gaming = overlay, Notifications = temperature alert).
        _tiles[2].AttachToggle(() => D.Settings.TempAlertEnabled,
            v => { D.Settings.TempAlertEnabled = v; D.SaveSettings(); RefreshTiles(); });
        _tiles[3].AttachToggle(() => D.Settings.OverlayEnabled,
            v => { D.SetOverlay(v); RefreshTiles(); });

        _homeHeader = new HomeHeader(D);
        Controls.Add(_homeHeader);

        _whatsNew = new Label { AutoSize = true, Font = new Font("Segoe UI", 9.5f), Cursor = Cursors.Hand };
        _whatsNew.Text = "✦  " + string.Format(Lang.T("st2_whatsnew"), D.AppVersion());
        _whatsNew.Click += (_, _) => D.OpenUpdates("v" + D.AppVersion());
        Controls.Add(_whatsNew);

        foreach (var c in AllCards()) Controls.Add(c);
        ApplyVisibility();
        RefreshTiles();
        Layout2(); ApplyTheme();
    }

    // ---------------- diagnostics package (#30) ----------------
    // One zip with everything issue triage keeps asking for piecemeal: a read-only EC dump (or
    // the exact error it produced - itself a diagnostic, see issue #48), settings, the change
    // history and errors.log. No personal data lives in any of these files.
    private void SaveDiagnostics()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "ZIP (*.zip)|*.zip",
            FileName = $"ghostdeck-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var info = D.Status();
            Diagnostics.Save(dlg.FileName, D.AppVersion(), D.Firmware(), info.Device, info.TierText);
            MessageBox.Show(FindForm(), string.Format(Lang.T("rep_saved_to"), dlg.FileName), "GhostDeck",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), string.Format(Lang.T("bk_err"), ex.Message), "GhostDeck",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    /// <summary>Open General and flash the Scenarios-visibility card so the user sees where it lives.</summary>
    public void FocusScenVisibility()
    {
        SelectSub(SubGeneral, save: true);
        _scenVisCard?.Flash(FlashPink, 30);
    }

    /// <summary>
    /// A row of colour swatches; clicking one opens the picker and stores the new hex.
    /// A FlowLayoutPanel that sizes ITSELF to its buttons - a hand-sized Panel kept clipping
    /// their bottom edge, and an auto-sizing container cannot get that wrong.
    /// </summary>
    private Control ColorRow(params (string label, Func<string> get, Action<string> set)[] items)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        foreach (var (label, get, set) in items)
        {
            var b = new Button { Text = label, AutoSize = false, Size = new Size(96, 30), Margin = new Padding(0, 0, 8, 0) };
            Ui.StyleGhost(b);
            void Paint()
            {
                try { b.ForeColor = ColorTranslator.FromHtml(get()); } catch { b.ForeColor = Theme.Text; }
            }
            Paint();
            b.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { FullOpen = true };
                try { dlg.Color = ColorTranslator.FromHtml(get()); } catch { }
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
                set(ColorTranslator.ToHtml(dlg.Color));
                Paint();
                D.SaveSettings(); D.SettingsChanged();
            };
            row.Controls.Add(b);
        }
        return row;
    }

    private ToggleSwitch Toggle(bool on, Action<bool> onChange)
    {
        var t = new ToggleSwitch { Checked = on };
        t.Toggled += v => onChange(v);
        return t;
    }

    /// <summary>
    /// One tile on the Settings Start page: group glyph + name + short description.
    /// Clicking jumps to that group's sub-tab (same target as the strip above).
    /// </summary>
    private sealed class GroupTile : Control
    {
        private static readonly Font TitleF = new("Segoe UI", 11f, FontStyle.Bold);
        private static readonly Font DescF = new("Segoe UI", 9f);
        private static readonly Font StateF = new("Segoe UI", 8.75f);
        private static readonly Font GlyphF = new("Segoe MDL2 Assets", 15f);
        private readonly string _glyph, _desc;
        private bool _hover;
        private string? _state;            // live third line ("Limit 60% · …")
        private bool? _stateOn;            // green/gray dot; null = no dot
        private ToggleSwitch? _tgl;        // optional quick master switch (top-right)
        private Func<bool>? _tglGet;

        public GroupTile(string glyph, string title, string desc, Action onClick)
        {
            _glyph = glyph; _desc = desc; Text = title;
            DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand;
            BackColor = Theme.Card;   // the toggle child clears to Parent.BackColor
            Click += (_, _) => onClick();
        }

        public void SetState(string? text, bool? on) { _state = text; _stateOn = on; Invalidate(); }

        public void AttachToggle(Func<bool> get, Action<bool> set)
        {
            _tglGet = get;
            _tgl = new ToggleSwitch { Checked = get() };
            _tgl.Toggled += v => set(v);
            Controls.Add(_tgl);
            PlaceToggle();
        }

        // Checked's setter is silent (no Toggled), so re-syncing can't loop back into the action.
        public void SyncToggle() { if (_tgl != null && _tglGet != null) _tgl.Checked = _tglGet(); }

        private void PlaceToggle() { if (_tgl != null) _tgl.Location = new Point(Width - _tgl.Width - 10, 10); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PlaceToggle(); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;   // room for the 1.4px hover stroke (no clipped AA)
            g.Clear(Theme.Surface);
            BackColor = _hover ? Theme.RowAlt : Theme.Card;
            using (var path = Theme.RoundRect(new RectangleF(1.2f, 1.2f, Width - 2.4f, Height - 2.4f), 10))
            {
                using var b = new SolidBrush(_hover ? Theme.RowAlt : Theme.Card);
                g.FillPath(b, path);
                using var pen = new Pen(_hover ? Theme.Accent : Theme.Border, _hover ? 1.4f : 1f);
                g.DrawPath(pen, path);
            }
            float k = DeviceDpi / 96f;
            int pad = (int)(16 * k);
            const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, _glyph, GlyphF, new Point(pad, (int)(12 * k)), Theme.Accent, F);
            int titleY = (int)(12 * k) + GlyphF.Height + (int)(6 * k);
            TextRenderer.DrawText(g, Text, TitleF, new Point(pad, titleY), Theme.Text, F);
            int descY = titleY + TitleF.Height + (int)(3 * k);
            TextRenderer.DrawText(g, _desc, DescF, new Rectangle(pad, descY, Width - pad * 2, DescF.Height + 2),
                Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (_state is { } st)
            {
                int sy = Height - (int)(13 * k) - StateF.Height;
                int sx = pad;
                if (_stateOn is { } on)
                {
                    using var b = new SolidBrush(on ? Theme.Green : Theme.Faint);
                    g.FillEllipse(b, sx, sy + (StateF.Height - 7) / 2f, 7, 7);
                    sx += 13;
                }
                TextRenderer.DrawText(g, st, StateF, new Rectangle(sx, sy, Width - sx - pad, StateF.Height + 2),
                    Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>
    /// Start-page status strip: model + tier badge + firmware + app version, and (when the
    /// daily check found a newer release) a clickable "new version" chip that jumps to Updates.
    /// </summary>
    private sealed class HomeHeader : Control
    {
        private static readonly Font NameF = new("Segoe UI", 10.5f, FontStyle.Bold);
        private static readonly Font MetaF = new("Segoe UI", 9.5f);
        private static readonly Font ChipF = new("Segoe UI", 9f, FontStyle.Bold);
        private readonly MainDeps D;
        private Rectangle _chip;     // update-chip hit area (Empty = no update pending)
        private bool _chipHover;

        public HomeHeader(MainDeps d)
        {
            D = d;
            DoubleBuffered = true; ResizeRedraw = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = !_chip.IsEmpty && _chip.Contains(e.Location);
            Cursor = h ? Cursors.Hand : Cursors.Default;
            if (h != _chipHover) { _chipHover = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_chipHover) { _chipHover = false; Invalidate(); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_chip.IsEmpty && _chip.Contains(e.Location) && D.UpdateAvail() is { } r)
                D.OpenUpdates(r.Tag);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10))
            {
                using var b = new SolidBrush(Theme.Card); g.FillPath(b, path);
                using var pen = new Pen(Theme.Border); g.DrawPath(pen, path);
            }
            const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            var info = D.Status();
            int x = 18;
            int cy = Height / 2;

            TextRenderer.DrawText(g, info.Device, NameF, new Point(x, cy - NameF.Height / 2), Theme.Text, F);
            x += TextRenderer.MeasureText(g, info.Device, NameF, Size.Empty, F).Width + 22;

            // tier badge: a deliberately SMALL chip (the strip's Ui.Pill was too loud here)
            using (var bf = new Font("Segoe UI", 8f, FontStyle.Bold))
            {
                int tw = TextRenderer.MeasureText(g, info.TierText, bf, Size.Empty, F).Width;
                int cw = tw + 18, chh = bf.Height + 8;
                var pr = new RectangleF(x, cy - chh / 2f, cw, chh);
                using var path = Theme.RoundRect(pr, 6);   // house style: minimal rounding, not a full pill
                using var fill = new SolidBrush(Color.FromArgb(26, info.TierColor));
                g.FillPath(fill, path);
                using var pen = new Pen(info.TierColor, 1f);
                g.DrawPath(pen, path);
                TextRenderer.DrawText(g, info.TierText, bf, Rectangle.Round(pr), info.TierColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += cw + 26;
            }

            string meta = D.Firmware().Length > 0 ? D.Firmware() + "      ·      GhostDeck v" + D.AppVersion() : "GhostDeck v" + D.AppVersion();
            TextRenderer.DrawText(g, meta, MetaF, new Point(x, cy - MetaF.Height / 2), Theme.Muted, F);

            // right side: update chip only when the daily check found something newer
            _chip = Rectangle.Empty;
            if (D.UpdateAvail() is { } r)
            {
                string txt = string.Format(Lang.T("upd_available"), r.Version) + "  →";
                int tw = TextRenderer.MeasureText(g, txt, ChipF, Size.Empty, F).Width;
                int cw = tw + 24, ch = ChipF.Height + 10;
                _chip = new Rectangle(Width - 14 - cw, cy - ch / 2, cw, ch);
                using var path = Theme.RoundRect(new RectangleF(_chip.X, _chip.Y, _chip.Width, _chip.Height), 6);
                using var b = new SolidBrush(_chipHover ? Theme.Accent : Theme.AccentFill);
                g.FillPath(b, path);
                TextRenderer.DrawText(g, txt, ChipF, _chip, _chipHover ? Theme.Page : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    // ---------------- card ----------------
    private sealed class CardSection : Panel
    {
        private readonly Label _head;
        private readonly string _glyph;
        private readonly List<(Label? label, Control ctl)> _rows = new();
        private Color? _flash;                                  // temporary highlight frame (gear jump)
        private long _flashStart;
        private int _flashMs;
        private System.Windows.Forms.Timer? _flashTimer;

        /// <summary>
        /// Draw a colored 2 px frame that fades back to the normal border over the given time -
        /// "here are the settings you asked for", without an abrupt cut at the end.
        /// </summary>
        public void Flash(Color color, int seconds)
        {
            _flash = color;
            _flashStart = Environment.TickCount64;
            _flashMs = seconds * 1000;
            _flashTimer?.Stop();
            _flashTimer?.Dispose();
            _flashTimer = new System.Windows.Forms.Timer { Interval = 120 };   // repaint tick for the fade
            _flashTimer.Tick += (_, _) =>
            {
                if (Environment.TickCount64 - _flashStart >= _flashMs) { _flashTimer!.Stop(); _flash = null; }
                Invalidate();
            };
            _flashTimer.Start();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _flashTimer?.Dispose();
            base.Dispose(disposing);
        }

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
                // full-width note labels (Tag "muted", e.g. the diagnostics blurb) rewrap to the
                // card's current width instead of a fixed MaximumSize
                if (l == null && ctl is Label note && note.Tag as string is "muted" or "warn")
                    note.MaximumSize = new Size(width - pad * 2, 0);
                // group separators stretch with the card
                if (l == null && ctl is SepLine sep) sep.Width = width - pad * 2;
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
                if (ctl is FlowLayoutPanel fp)
                {
                    fp.BackColor = Theme.Card;
                    // Refresh the surface/border of nested buttons, but NEVER their ForeColor -
                    // on the colour swatches that is the user's chosen colour, not a theme colour.
                    foreach (Control child in fp.Controls)
                    {
                        // nested value labels follow the same Tag convention as row labels
                        if (child is Label nl)
                        {
                            nl.ForeColor = nl.Tag as string == "muted" ? Theme.Muted
                                         : nl.Tag as string == "warn" ? Theme.Amber : Theme.Text;
                            nl.BackColor = Theme.Card;
                        }
                        if (child is Button swatch)
                        {
                            swatch.BackColor = Theme.Surface;
                            swatch.FlatAppearance.BorderColor = Theme.BorderStrong;
                            swatch.FlatAppearance.MouseOverBackColor = Theme.AccentSoft;
                            swatch.FlatAppearance.MouseDownBackColor = Theme.RowAlt;
                        }
                    }
                }
                // value labels (battery health) = Text; "muted"-tagged notes (diagnostics blurb) = Muted
                if (ctl is Label vl) { vl.ForeColor = vl.Tag as string == "muted" ? Theme.Muted : vl.Tag as string == "warn" ? Theme.Amber : Theme.Text; vl.BackColor = Theme.Card; }
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
            using (var pen = new Pen(Theme.Border))
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10))
                g.DrawPath(pen, path);
            if (_flash is { } fc)
            {
                // highlight frame on top of the normal border, alpha falling linearly to zero
                float t = Math.Clamp((Environment.TickCount64 - _flashStart) / (float)Math.Max(1, _flashMs), 0f, 1f);
                int a = (int)(255 * (1f - t));
                if (a > 0)
                {
                    using var pen2 = new Pen(Color.FromArgb(a, fc), 2f);
                    using var path2 = Theme.RoundRect(new RectangleF(1f, 1f, Width - 2, Height - 2), 10);
                    g.DrawPath(pen2, path2);
                }
            }

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
