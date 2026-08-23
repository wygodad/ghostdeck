using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Fan-curve editor. ONE curve state (temperature nodes fixed as MSI does, speed % edited),
/// FOUR views of it selected by a sub-tab strip: Chart (drag the nodes), Equalizer (one
/// vertical slider per node), Deck (dials + a crossfader between two shapes) and Playground
/// (what-would-the-fans-do simulator + the real sweep test). Every view edits the same
/// arrays and shares the preset bar, the per-profile assignment row and the on/off bar.
/// "Apply" writes the curve and engages Advanced fan mode; the switch OFF hands the fans
/// back to the profile. All writes go through MainDeps.WithEcWrite (gated on Writable +
/// not simulating). Read-only when unsupported/not writable.
/// </summary>
public sealed class FanCurvePage : ThemedPage
{
    private const int Pad = 28;
    private const int ViewChart = 0, ViewEq = 1, ViewDeck = 2, ViewPlay = 3, ViewCount = 4;

    // DPI scale for every dimension the redesign added. The page predates per-monitor DPI
    // handling (the original two charts size themselves off Width/Height and use TextRenderer,
    // which follows the font), so ONLY the new fixed-size widgets go through S(): chips, tiles,
    // the table, faders, dials, rings, labels. At 140 % text grows with the font and the boxes
    // did not - which is what clipped every label in the first test build.
    private float K => DeviceDpi / 96f;
    private int S(int px) => (int)Math.Round(px * K);
    private float Sf(float px) => px * K;

    // MSI factory default (the curve we verified) — used by the "MSI default" button.
    private static readonly int[] DefCpuT = { 0, 50, 57, 64, 70, 76 }, DefCpuS = { 0, 40, 48, 60, 75, 89 };
    private static readonly int[] DefGpuT = { 0, 50, 55, 60, 65, 70 }, DefGpuS = { 0, 48, 60, 70, 82, 93 };

    // NOT readonly: a model database applied while the app runs re-points both. The swap is
    // deferred while CurveHot is true, so they never change under an active editing session.
    private DeviceProfile? _dev;
    private FanCurveSpec? _fc;
    private int[] _cpuT, _cpuS, _gpuT, _gpuS;
    private bool _loaded;
    private bool _loading;   // background first-load in flight
    private int _dragFan = -1, _dragIdx = -1;
    private byte _fanMode;

    // One background reader for the whole page (mode byte + live temps/duty/rpm), started
    // while visible. Views read _live.Sample / _live.Trail() on the UI thread.
    private readonly FanLiveFeed _live;
    private readonly SubTabs _views;
    private int _view;   // ViewChart..ViewPlay, persisted in AppSettings.FanCurveView

    // The In-action view is the only one that can outgrow the window (chart + diagnostics), so
    // it is the only one that scrolls.
    //
    // It scrolls by OFFSETTING ITS GEOMETRY (PlayArea), never by transforming the Graphics.
    // That is not a style choice: TextRenderer.DrawText - which draws every label in this app -
    // ignores both Graphics.TranslateTransform and Graphics.SetClip (it hands the text to GDI
    // through a raw HDC). A transform-based scroll therefore moves the cards, curves and dots
    // while every label stays behind, and no clip can hold the overflow back. With the offset
    // baked into the rectangles, one coordinate space serves painting, child controls (Place)
    // and hit tests alike, and the page header is painted LAST so scrolled content cannot bleed
    // into it. WinForms AutoScroll is not used either: it moves child controls on its own.
    private int _scrollY;
    private bool _scrollDrag;      // dragging the painted scrollbar thumb
    private int _scrollGrabDy;     // grab point inside the thumb

    // ---- chart-view options bar (audibility zones, intent tiles, comparison layers, table) ----
    private readonly CheckItem _optZones, _optIntents, _optTrail, _optCompare;
    private readonly HelpDot _optHelp = new();
    private readonly Button _optTable = new();
    private readonly List<(string name, int[] cpu, int[] gpu, Color color)> _layers = new();   // resolved comparison layers
    private Rectangle[] _layerChips = Array.Empty<Rectangle>();   // hit rects of the layer chips (painted)
    private string[] _layerNames = Array.Empty<string>();          // chip i -> preset name ("" = MSI default)
    private int _hoverChip = -1;
    private static readonly Color[] LayerColors = { Theme.Violet, Color.FromArgb(0xFF, 0xC1, 0x5D), Color.FromArgb(0x61, 0xE7, 0xA4) };
    private const int MaxLayers = 3;

    // intent tiles (view 08): painted rects, hit-tested in OnDown
    private Rectangle[] _intentRects = Array.Empty<Rectangle>();
    private int _hoverIntent = -1;

    // coupled points table (view 05, variant A): painted under the charts, one row per point,
    // hover row highlights the node on BOTH charts, click on a % cell opens an inline editor
    private Rectangle[] _tableRows = Array.Empty<Rectangle>();
    private Rectangle[,] _tablePctCells = new Rectangle[0, 0];   // [row, fan]
    private int _hoverRow = -1;
    private readonly TextBox _cellEdit = new() { Visible = false, TextAlign = HorizontalAlignment.Center, MaxLength = 3 };
    private int _editRow = -1, _editFan = -1;

    private static readonly int[] TableTempColW = { 84, 70 };   // temp column, % column

    private readonly ToggleSwitch _enable = new();
    private readonly Label _enableLabel = new();
    private readonly Button _default = new();
    private readonly Button _report = new();

    // ---- preset bar + per-profile assignment ----
    private static readonly ProfileId[] AssignableProfiles =   // Silent = always stock (0xD4 constraint)
        { ProfileId.Balanced, ProfileId.Extreme, ProfileId.SuperBattery };
    private readonly Label _presetLabel = new() { AutoSize = true };
    private readonly ThemedComboBox _presetCombo = new() { Width = 180 };
    private readonly Button _psSave = new(), _psSaveAs = new(), _psRename = new(), _psDelete = new();
    private readonly Button _psImport = new(), _psExport = new(), _psShare = new();
    private readonly Label _assignLabel = new() { AutoSize = true };
    private readonly Label[] _assignNames = new Label[AssignableProfiles.Length];
    private readonly ThemedComboBox[] _assignCombos = new ThemedComboBox[AssignableProfiles.Length];
    private bool _syncingPresets;   // guard: programmatic combo fills also raise SelectedIndexChanged

    public FanCurvePage(MainDeps d) : base(d)
    {
        AutoScroll = false;
        _dev = Devices.Detect(d.Firmware());
        _fc = _dev?.FanCurve;
        _cpuT = (int[])DefCpuT.Clone(); _cpuS = (int[])DefCpuS.Clone();
        _gpuT = (int[])DefGpuT.Clone(); _gpuS = (int[])DefGpuS.Clone();

        // View strip (same control as Status/Report/Settings). Glyphs: chart, equalizer
        // (slider bars), deck (a mixer-ish dial glyph) and playground (a flask).
        _view = Math.Clamp(d.Settings.FanCurveView, 0, ViewCount - 1);
        _views = new SubTabs(ViewLabels(), new[] { "", "", "", "" });
        _views.SetActive(_view);
        _views.Changed += i => SelectView(i, save: true);
        Controls.Add(_views);

        _live = new FanLiveFeed(this, () => _dev,
                                () => _fc != null && D.Status().Known && !D.Simulating());
        _live.Updated += OnLiveSample;

        // Chart-view options. Zones and intents are plain checkboxes; the table is a toggle
        // button (it changes the layout, so it reads as an action, not a preference); the
        // comparison layers are painted chips (see DrawLayerChips) hit-tested in OnDown.
        _optZones = new CheckItem(Lang.T("fc_opt_zones"), d.Settings.FanCurveZones);
        _optZones.Toggled += v => { D.Settings.FanCurveZones = v; D.SaveSettings(); Invalidate(); };
        _optIntents = new CheckItem(Lang.T("fc_opt_intents"), d.Settings.FanCurveIntents);
        _optIntents.Toggled += v => { D.Settings.FanCurveIntents = v; D.SaveSettings(); LayoutButtons(); Invalidate(); };
        _optTrail = new CheckItem(Lang.T("fc_opt_trail"), d.Settings.FanCurveTrail);
        _optTrail.Toggled += v => { D.Settings.FanCurveTrail = v; D.SaveSettings(); Invalidate(); };
        _optCompare = new CheckItem(CompareCaption(), d.Settings.FanCurveCompareBar);
        _optCompare.Toggled += v => { D.Settings.FanCurveCompareBar = v; D.SaveSettings(); LayoutButtons(); Invalidate(); };
        _optHelp.TextProvider = () => Lang.T("fc_opt_help");
        Controls.Add(_optZones);
        Controls.Add(_optIntents);
        Controls.Add(_optTrail);
        Controls.Add(_optCompare);
        Controls.Add(_optHelp);
        _optTable.Click += (_, _) =>
        {
            D.Settings.FanCurveTable = !D.Settings.FanCurveTable;
            D.SaveSettings();
            CloseCellEditor(commit: false);
            Restyle(); LayoutButtons(); Invalidate();
        };
        Controls.Add(_optTable);
        _cellEdit.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { CloseCellEditor(commit: true); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { CloseCellEditor(commit: false); e.SuppressKeyPress = true; }
        };
        _cellEdit.LostFocus += (_, _) => CloseCellEditor(commit: true);
        Controls.Add(_cellEdit);
        InitDeck();
        InitPlayground();

        _enableLabel.AutoSize = true;
        Controls.Add(_enableLabel);
        Controls.Add(_enable);
        Controls.Add(_default);
        _report.Click += (_, _) => (FindForm() as MainForm)?.ShowReport(1);
        Controls.Add(_report);

        // Preset bar: pick / save / manage named curves; import-export as JSON; share on GitHub.
        Controls.Add(_presetLabel);
        Controls.Add(_presetCombo);
        _presetCombo.SelectedIndexChanged += (_, _) => OnPresetPicked();
        foreach (var (btn, act) in new (Button, Action)[]
        {
            (_psSave, SavePreset), (_psSaveAs, SavePresetAs), (_psRename, RenamePreset), (_psDelete, DeletePreset),
            (_psImport, ImportPreset), (_psExport, ExportPreset), (_psShare, SharePreset),
        })
        {
            var a = act;
            btn.AutoSize = false;   // width from text in Restyle; height matches the preset picker
            btn.Click += (_, _) => a();
            Controls.Add(btn);
        }

        // Per-profile assignment (auto-applied on every switch made through GhostDeck).
        Controls.Add(_assignLabel);
        for (int i = 0; i < AssignableProfiles.Length; i++)
        {
            int idx = i;
            _assignNames[i] = new Label { AutoSize = true, Text = Profiles.Get(AssignableProfiles[i]).Label };
            _assignCombos[i] = new ThemedComboBox { Width = 150 };
            _assignCombos[i].SelectedIndexChanged += (_, _) => OnAssignChanged(idx);
            Controls.Add(_assignNames[i]);
            Controls.Add(_assignCombos[i]);
        }

        Restyle();
        RefreshPresetUi();

        // The single switch: ON = write our curve + Advanced fan; OFF = hand fans back to the
        // current profile's normal behaviour and reset the graph to the MSI default.
        // ToggleSwitch.Toggled fires on user click only (programmatic Checked= does not), so no guard needed.
        _enable.Toggled += on => { if (on) Apply(); else RevertToProfileDefault(); };
        _default.Click += (_, _) => { _cpuS = (int[])DefCpuS.Clone(); _gpuS = (int[])DefGpuS.Clone(); if (_enable.Checked) ReApply(); Invalidate(); };

        VisibleChanged += (_, _) => { if (Visible && _fc != null) _live.Start(); else _live.Stop(); };
        Resize += (_, _) => { LayoutButtons(); Invalidate(); };
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += (_, _) =>
        {
            _scrollDrag = false;
            bool dragged = _dragIdx >= 0; _dragFan = _dragIdx = -1;
            if (dragged && _enable.Checked) ReApply();
        };
    }

    public override void OnEnter()
    {
        // First open used to read the whole curve (dozens of WMI calls) synchronously and froze
        // the tab switch; load it on a worker and repaint when it lands.
        if (!_loaded && !_loading && _fc != null)
        {
            _loading = true;
            var dev = _dev!;
            int points = _fc.Points;
            Task.Run(() =>
            {
                (int[] cpuTemp, int[] cpuSpeed, int[] gpuTemp, int[] gpuSpeed)? c = null;
                try { c = Ec.ReadFanCurve(dev); } catch { }
                try
                {
                    BeginInvoke(() =>
                    {
                        _loading = false;
                        if (c is { } v && v.cpuSpeed.Length == points)
                        {
                            // Defensive clamp: some boards keep other units in (or near) these
                            // tables — out-of-range values pushed points off the plot and broke
                            // the page's hit-testing (issue #28). Speeds cap at the model's own
                            // scale (MSI Center's sliders reach 150 %), temperatures at 100 °C.
                            _cpuT = ClampArr(v.cpuTemp, 100); _cpuS = ClampArr(v.cpuSpeed, MaxPct);
                            _gpuT = ClampArr(v.gpuTemp, 100); _gpuS = ClampArr(v.gpuSpeed, MaxPct);
                            _loaded = true;
                        }
                        Invalidate();
                    });
                }
                catch { _loading = false; }   // page disposed mid-flight
            });
        }
        _enable.Enabled = _enableLabel.Enabled = _default.Enabled = Editable;
        RefreshPresetUi();
        RefreshMode();
        LayoutButtons();
        Invalidate();
    }

    // ---------------- views ----------------
    private static string[] ViewLabels() =>
        new[] { Lang.T("fc_view_chart"), Lang.T("fc_view_eq"), Lang.T("fc_view_deck"), Lang.T("fc_view_play") };

    private void SelectView(int i, bool save)
    {
        CloseCellEditor(commit: true);   // an open inline editor belongs to the chart view
        bool wasPlay = _view == ViewPlay;
        _view = Math.Clamp(i, 0, ViewCount - 1);
        _views.SetActive(_view);
        if (wasPlay && _view != ViewPlay && _fc != null) RestoreChrome();
        if (save && D.Settings.FanCurveView != _view) { D.Settings.FanCurveView = _view; D.SaveSettings(); }
        _dragFan = _dragIdx = -1;
        _hoverChip = _hoverIntent = _hoverRow = _eqHoverFan = _eqHoverIdx = _dialHoverFan = _dialHoverIdx = -1;
        Cursor = Cursors.Default;
        LayoutButtons();
        Invalidate();
    }

    public override void OnLanguageChanged()
    {
        _views.SetLabels(ViewLabels());
        Restyle();
        RefreshPresetUi();
        LayoutButtons();
        Invalidate();
    }

    // A live sample landed (UI thread): the mode byte drives the switch, the rest repaints
    // whichever view is showing. Cheap enough at 1.5 s - the page has no off-screen cache.
    private void OnLiveSample()
    {
        _fanMode = _live.Sample.FanMode;
        SyncEnable();
    }

    // Top of the speed axis/scale for this model (100 when no curve spec is present).
    private int MaxPct => _fc?.MaxFanPct ?? 100;

    private static int[] ClampArr(int[] a, int max)
    {
        var r = new int[a.Length];
        for (int i = 0; i < a.Length; i++) r[i] = Math.Clamp(a[i], 0, max);
        return r;
    }

    // ---------------- presets ----------------
    private string? SelectedPresetName =>
        _presetCombo.SelectedIndex >= 0 ? _presetCombo.Items[_presetCombo.SelectedIndex] as string : null;

    private FanCurvePreset? SelectedPreset() =>
        SelectedPresetName is { } n ? D.Settings.FindPreset(n) : null;

    /// <summary>Refill the preset picker + assignment combos from settings (optionally selecting a name).</summary>
    private void RefreshPresetUi(string? select = null)
    {
        _syncingPresets = true;
        try
        {
            bool hasCurve = _fc != null;
            string? keep = select ?? SelectedPresetName;
            var names = D.Settings.CurvePresets.Select(p => p.Name).ToArray();

            _presetCombo.Items.Clear();
            foreach (var n in names) _presetCombo.Items.Add(n);
            int si = keep != null ? Array.IndexOf(names, keep) : -1;
            if (si < 0 && names.Length > 0) si = 0;   // always have a selection -> buttons never sit disabled
            _presetCombo.SelectedIndex = si;
            _presetCombo.Enabled = names.Length > 0;

            // With no saved presets only the two "create one" actions show (Save as… / Import…);
            // an empty picker and five disabled buttons looked broken (user feedback).
            bool any = names.Length > 0;
            foreach (var c in new Control[] { _psSaveAs, _psImport })
                c.Visible = hasCurve;
            foreach (var c in new Control[] { _presetLabel, _presetCombo, _psSave, _psRename, _psDelete, _psExport, _psShare })
                c.Visible = hasCurve && any;
            _assignLabel.Visible = hasCurve && any;
            for (int i = 0; i < AssignableProfiles.Length; i++)
            {
                var combo = _assignCombos[i];
                combo.Visible = _assignNames[i].Visible = hasCurve && any;
                combo.Items.Clear();
                combo.Items.Add(Lang.T("fc_preset_auto"));
                foreach (var n in names) combo.Items.Add(n);
                string key = Profiles.Get(AssignableProfiles[i]).Key;
                int ai = 0;
                if (D.Settings.ProfileCurves.TryGetValue(key, out var assigned))
                {
                    int f = Array.IndexOf(names, assigned);
                    if (f >= 0) ai = f + 1;
                }
                combo.SelectedIndex = ai;
            }
            UpdatePresetButtons();
        }
        finally { _syncingPresets = false; }
        RebuildLayers();   // a renamed/deleted preset also changes the comparison chips
        FillPoles();       // ...and the crossfader poles
        LayoutButtons();   // visibility may have changed (first preset created / last one deleted)
    }

    private void UpdatePresetButtons()
    {
        bool has = _presetCombo.SelectedIndex >= 0;
        _psSave.Enabled = _psRename.Enabled = _psDelete.Enabled = _psExport.Enabled = _psShare.Enabled = has;
    }

    // Picking a preset loads it into the editor; if the curve is currently running, re-apply live.
    private void OnPresetPicked()
    {
        UpdatePresetButtons();
        if (_syncingPresets) return;
        var p = SelectedPreset();
        if (p == null || _fc == null || !p.IsValid(_fc)) return;
        _cpuT = (int[])p.CpuTemp.Clone(); _cpuS = (int[])p.CpuSpeed.Clone();
        _gpuT = (int[])p.GpuTemp.Clone(); _gpuS = (int[])p.GpuSpeed.Clone();
        _loaded = true;
        if (_enable.Checked && Editable) ReApply();
        Invalidate();
    }

    /// <summary>(#100) The tray quick-switch applied a saved preset; mirror it here so the
    /// editor shows the curve the machine is actually running. Loads the points and selects
    /// the preset without re-applying anything - the EC already has it.</summary>
    public void SyncExternalPreset(string name)
    {
        if (_fc == null || D.Settings.FindPreset(name) is not { } p || !p.IsValid(_fc)) return;
        _cpuT = (int[])p.CpuTemp.Clone(); _cpuS = (int[])p.CpuSpeed.Clone();
        _gpuT = (int[])p.GpuTemp.Clone(); _gpuS = (int[])p.GpuSpeed.Clone();
        _loaded = true;
        RefreshPresetUi(name);   // selects it in the picker; the sync guard keeps OnPresetPicked from re-applying
        Invalidate();
    }

    private FanCurvePreset SnapshotPreset(string name) => new()
    {
        Name = name,
        CpuTemp = (int[])_cpuT.Clone(), CpuSpeed = (int[])_cpuS.Clone(),
        GpuTemp = (int[])_gpuT.Clone(), GpuSpeed = (int[])_gpuS.Clone(),
    };

    private void SavePreset()
    {
        var p = SelectedPreset();
        if (p == null) return;
        p.CpuTemp = (int[])_cpuT.Clone(); p.CpuSpeed = (int[])_cpuS.Clone();
        p.GpuTemp = (int[])_gpuT.Clone(); p.GpuSpeed = (int[])_gpuS.Clone();
        D.SaveSettings();
        D.SettingsChanged();
    }

    private void SavePresetAs()
    {
        string? name = InputDialog.Ask(FindForm(), Lang.T("fc_ps_saveas"), Lang.T("fc_ps_name"));
        if (name == null) return;
        if (D.Settings.FindPreset(name) != null)
        {
            MessageBox.Show(FindForm(), Lang.T("fc_ps_exists"), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        D.Settings.CurvePresets.Add(SnapshotPreset(name));
        D.SaveSettings();
        D.SettingsChanged();
        RefreshPresetUi(name);
    }

    private void RenamePreset()
    {
        var p = SelectedPreset();
        if (p == null) return;
        string? name = InputDialog.Ask(FindForm(), Lang.T("fc_ps_rename"), Lang.T("fc_ps_name"), p.Name);
        if (name == null || name == p.Name) return;
        if (D.Settings.FindPreset(name) != null)
        {
            MessageBox.Show(FindForm(), Lang.T("fc_ps_exists"), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        foreach (var k in D.Settings.ProfileCurves.Keys.ToList())
            if (D.Settings.ProfileCurves[k] == p.Name) D.Settings.ProfileCurves[k] = name;
        p.Name = name;
        D.SaveSettings();
        D.SettingsChanged();
        RefreshPresetUi(name);
    }

    private void DeletePreset()
    {
        var p = SelectedPreset();
        if (p == null) return;
        if (MessageBox.Show(FindForm(), string.Format(Lang.T("fc_ps_del_confirm"), p.Name), "GhostDeck",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        D.Settings.CurvePresets.Remove(p);
        foreach (var k in D.Settings.ProfileCurves.Keys.ToList())
            if (D.Settings.ProfileCurves[k] == p.Name) D.Settings.ProfileCurves.Remove(k);
        D.SaveSettings();
        D.SettingsChanged();
        RefreshPresetUi();
    }

    private void ExportPreset()
    {
        var p = SelectedPreset();
        if (p == null) return;
        string safe = string.Concat(p.Name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-'));
        using var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = $"ghostdeck-curve-{safe}.json" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), string.Format(Lang.T("bk_err"), ex.Message), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportPreset()
    {
        using var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var p = JsonSerializer.Deserialize<FanCurvePreset>(File.ReadAllText(dlg.FileName));
            if (p == null || _fc == null || !p.IsValid(_fc))
            {
                MessageBox.Show(FindForm(), Lang.T("fc_ps_invalid"), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string name = p.Name;
            for (int n = 2; D.Settings.FindPreset(name) != null; n++) name = $"{p.Name} ({n})";
            p.Name = name;
            D.Settings.CurvePresets.Add(p);
            D.SaveSettings();
            D.SettingsChanged();
            RefreshPresetUi(name);
        }
        catch (JsonException)
        {
            MessageBox.Show(FindForm(), Lang.T("fc_ps_invalid"), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), string.Format(Lang.T("bk_err"), ex.Message), "GhostDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Opens the browser with a prefilled GitHub Discussion (Fan curves category) containing the
    // preset JSON + model/firmware. Nothing is posted automatically; the user reviews and submits.
    private void SharePreset()
    {
        var p = SelectedPreset();
        if (p == null) return;
        string json = JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true });
        string body = $"Model: {(_dev?.Name ?? "unknown")}\nFirmware: {D.Firmware()}\nApp: {D.AppVersion()}\n\n```json\n{json}\n```\n";
        string url = "https://github.com/wygodad/ghostdeck/discussions/new?category=fan-curves"
                   + "&title=" + Uri.EscapeDataString("Fan curve preset: " + p.Name)
                   + "&body=" + Uri.EscapeDataString(body);
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void OnAssignChanged(int idx)
    {
        if (_syncingPresets) return;
        string key = Profiles.Get(AssignableProfiles[idx]).Key;
        int si = _assignCombos[idx].SelectedIndex;
        if (si <= 0) D.Settings.ProfileCurves.Remove(key);
        else D.Settings.ProfileCurves[key] = _assignCombos[idx].Items[si] as string ?? "";
        D.SaveSettings();
        D.SettingsChanged();
    }

    // Ask the feed for a reading now (after a write, so the switch reflects it without
    // waiting for the next tick). The feed itself keeps every WMI call off the UI thread.
    private void RefreshMode()
    {
        if (_fc != null && _dev != null) _live.Poll();
        else SyncEnable();
    }

    // keep the switch in sync with the actual hardware state (programmatic set won't fire Toggled).
    // Frozen while a sweep runs: the sweep drives the mode byte itself, and the switch must keep
    // showing what the USER had (which the sweep restores at the end).
    private void SyncEnable()
    {
        if (_sweepCts != null) { Invalidate(); return; }
        _enable.Checked = _fc != null && _fanMode == _fc.AdvancedModeValue;
        Invalidate();
    }

    // Everything that edits the curve or writes fan registers is off while a sweep holds the fans.
    private bool Editable => _fc != null && D.Writable() && _sweepCts == null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _live.Dispose(); _airTimer.Dispose(); _sweepCts?.Cancel(); }
        base.Dispose(disposing);
    }

    public override void ApplyTheme() { base.ApplyTheme(); Restyle(); }

    private void Restyle()
    {
        Ui.StyleGhost(_default);
        _default.Text = Lang.T("fc_default");
        Ui.StyleGhost(_optTable);
        _optTable.Font = new Font("Segoe UI", 10.5f);
        _optTable.Text = (D.Settings.FanCurveTable ? "▴  " : "▾  ") + Lang.T("fc_opt_table");
        _optTable.Width = TextRenderer.MeasureText(_optTable.Text, _optTable.Font).Width + 26;
        _optZones.Text = Lang.T("fc_opt_zones");
        _optCompare.Text = CompareCaption();
        _optIntents.Text = Lang.T("fc_opt_intents");
        _optTrail.Text = Lang.T("fc_opt_trail");
        foreach (var c in new Control[] { _optZones, _optIntents, _optTrail, _optCompare, _optHelp }) { c.ForeColor = Theme.Text; c.BackColor = Theme.Surface; }
        // playground buttons
        if (_sweepCts != null) { Ui.StyleGhost(_sweepStart); _sweepStart.Text = Lang.T("fc_sweep_stop"); }
        else { Ui.StylePrimary(_sweepStart); _sweepStart.Text = Lang.T("fc_sweep_start"); }
        Ui.StyleGhost(_sweepToggle);
        _sweepToggle.Text = (_sweepOpen ? "▴  " : "▾  ") + Lang.T(_sweepOpen ? "fc_sweep_hide" : "fc_sweep_show");
        _sweepToggle.Width = TextRenderer.MeasureText(_sweepToggle.Text, _sweepToggle.Font).Width + S(28);
        _sweepStart.Width = TextRenderer.MeasureText(_sweepStart.Text, _sweepStart.Font).Width + 34;
        Ui.StyleGhost(_sweepCopy);
        _sweepCopy.Text = Lang.T("fc_sweep_copy");
        _sweepCopy.Width = TextRenderer.MeasureText(_sweepCopy.Text, _sweepCopy.Font).Width + 30;
        Ui.StylePrimary(_report);
        _report.Text = Lang.T("fc_report_curve");
        _enableLabel.Text = Lang.T("fc_enable");
        _enableLabel.Font = new Font("Segoe UI", 11.5f);
        _enableLabel.ForeColor = Theme.Text;
        _enableLabel.BackColor = Theme.Surface;

        var barFont = new Font("Segoe UI", 10.5f);
        foreach (var (btn, key) in new (Button, string)[]
        {
            (_psSave, "fc_ps_save"), (_psSaveAs, "fc_ps_saveas"), (_psRename, "fc_ps_rename"), (_psDelete, "fc_ps_delete"),
            (_psImport, "fc_ps_import"), (_psExport, "fc_ps_export"), (_psShare, "fc_ps_share"),
        })
        {
            Ui.StyleGhost(btn);
            btn.Font = barFont;
            btn.Text = Lang.T(key);
            btn.Width = TextRenderer.MeasureText(btn.Text, barFont).Width + 26;
        }
        // colour-coded by action family, SOLID fills (user feedback: outlines alone read as empty):
        // save = blue, rename = amber, delete = pink/red, share = green; import/export = neutral ghost.
        // Amber/green are light in dark mode -> dark ink for contrast (AccentFill/red take white).
        var darkInk = Color.FromArgb(0x05, 0x07, 0x0B);
        void FillBtn(Button b, Color bg, Color fg)
        {
            b.BackColor = bg;
            b.ForeColor = fg;
            b.FlatAppearance.BorderSize = 0;
        }
        FillBtn(_psSave, Theme.AccentFill, Color.White);
        FillBtn(_psSaveAs, Theme.AccentFill, Color.White);
        FillBtn(_psRename, Theme.Amber, Theme.Dark ? darkInk : Color.White);
        FillBtn(_psDelete, Theme.Red, Color.White);
        FillBtn(_psShare, Theme.Green, Theme.Dark ? darkInk : Color.White);
        _presetLabel.Text = Lang.T("fc_preset");
        _presetLabel.Font = barFont;
        _presetLabel.ForeColor = Theme.Text; _presetLabel.BackColor = Theme.Surface;
        _assignLabel.Text = Lang.T("fc_assign");
        _assignLabel.Font = barFont;
        _assignLabel.ForeColor = Theme.Text; _assignLabel.BackColor = Theme.Surface;
        for (int i = 0; i < _assignNames.Length; i++)
        {
            _assignNames[i].Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _assignNames[i].ForeColor = D.ColorOf(AssignableProfiles[i]);
            _assignNames[i].BackColor = Theme.Surface;
        }
    }

    // Vertical stack: title 22 · hint 68-88 · view strip · preset bar · content (charts) ..
    // H-124 · assignment row H-112 · on/off bar H-62. The strip and bar rows follow DPI (they
    // hold real controls that grow with the font); the bottom rows are the pre-existing layout.
    // 68 = the hint line's own top; the strip clears BOTH of its lines (10.5pt wraps to two on a
    // narrow window), so the second line is never cut in half by the strip.
    private int ViewsY => 68 + S(48);
    private int PresetY => ViewsY + S(52);
    // Content starts under the preset bar, or right under the sub-tabs on the view that
    // hides that bar (nothing should sit in an empty strip).
    // The comparison-chip row sits between the preset bar and the tiles; when it is on, the
    // whole content stack below it moves down by one row.
    private bool CompareRowOn => _view == ViewChart && D.Settings.FanCurveCompareBar && _fc != null;
    private int ContentTop => _view == ViewPlay ? ViewsY + S(46) : PresetY + S(44) + (CompareRowOn ? S(34) : 0);

    private void LayoutButtons()
    {
        _views.Visible = _fc != null;
        _views.SetBounds(Pad, ViewsY, _views.FitTo(Width - Pad * 2), _views.Height);
        LayoutDeck();
        LayoutPlayground();

        int by = Height - 62, bh = 42;
        _enable.Location = new Point(Pad, by + (bh - _enable.Height) / 2);
        _enableLabel.Location = new Point(Pad + _enable.Width + 12, by + (bh - _enableLabel.Height) / 2);
        _default.SetBounds(Width - Pad - 170, by, 170, bh);
        int rw = TextRenderer.MeasureText(_report.Text, _report.Font).Width + 40;
        _report.SetBounds(_default.Left - 14 - rw, by, rw, bh);

        // preset bar (label, picker, manage buttons, then import/export/share);
        // hidden controls (e.g. the whole manage group when no presets exist) take no space
        int py = PresetY;
        int x = Pad;
        if (_presetLabel.Visible)
        {
            _presetLabel.Location = new Point(x, py + 7);
            x += _presetLabel.PreferredWidth + 10;
        }
        int barH = _presetCombo.Height;   // one shared height for the picker and every button
        if (_presetCombo.Visible)
        {
            _presetCombo.Location = new Point(x, py);
            x += _presetCombo.Width + 10;
        }
        foreach (var b in new[] { _psSave, _psSaveAs, _psRename, _psDelete })
        {
            if (!b.Visible) continue;
            b.SetBounds(x, py, b.Width, barH);
            x += b.Width + 6;
        }
        x += 14;
        foreach (var b in new[] { _psImport, _psExport, _psShare })
        {
            if (!b.Visible) continue;
            b.SetBounds(x, py, b.Width, barH);
            x += b.Width + 6;
        }

        // chart-view options, right-aligned on the same row: [Table] [?] [Trail] [Tiles] [Zones]
        // (comparison chips are painted to their left, see DrawLayerChips)
        bool chart = _view == ViewChart && _fc != null;
        _optTable.Visible = _optZones.Visible = _optIntents.Visible = _optTrail.Visible = _optCompare.Visible = _optHelp.Visible = chart;
        if (chart)
        {
            int rx = Width - Pad;
            _optTable.SetBounds(rx - _optTable.Width, py, _optTable.Width, barH);
            rx = _optTable.Left - S(14);
            _optHelp.Size = new Size(S(22), S(22));
            _optHelp.SetBounds(rx - _optHelp.Width, py + (barH - _optHelp.Height) / 2, _optHelp.Width, _optHelp.Height);
            rx = _optHelp.Left - S(10);
            foreach (var c in new[] { _optTrail, _optIntents, _optZones, _optCompare })
            {
                c.Height = S(26);
                c.Width = c.PreferredWidth;
                c.SetBounds(rx - c.Width, py + (barH - c.Height) / 2, c.Width, c.Height);
                rx = c.Left - S(12);
            }
        }

        // per-profile assignment row (between the graphs and the bottom bar)
        int ay = Height - 112;
        _assignLabel.Location = new Point(Pad, ay + 7);
        x = Pad + _assignLabel.PreferredWidth + 14;
        for (int i = 0; i < AssignableProfiles.Length; i++)
        {
            _assignNames[i].Location = new Point(x, ay + 8);
            x += _assignNames[i].PreferredWidth + 6;
            _assignCombos[i].Location = new Point(x, ay + 1);
            x += _assignCombos[i].Width + 18;
        }

        // The Playground neither edits nor applies the curve, so none of the editing chrome
        // belongs there: preset bar, assignment row, on/off switch, MSI default, Report. The
        // other views restore it through RefreshPresetUi/RefreshMode on their next layout.
        if (_view == ViewPlay && _fc != null)
        {
            foreach (var c in new Control[] { _presetLabel, _presetCombo, _psSave, _psSaveAs, _psRename, _psDelete, _psImport, _psExport, _psShare,
                                              _assignLabel, _enable, _enableLabel, _default, _report })
                c.Visible = false;
            foreach (var c in _assignNames) c.Visible = false;
            foreach (var c in _assignCombos) c.Visible = false;
        }
    }

    // Leaving the Playground: bring the editing chrome back (RefreshPresetUi decides which
    // preset buttons show; the rest is unconditional).
    private void RestoreChrome()
    {
        _enable.Visible = _enableLabel.Visible = _default.Visible = _report.Visible = _assignLabel.Visible = true;
        foreach (var c in _assignNames) c.Visible = true;
        foreach (var c in _assignCombos) c.Visible = true;
        _presetLabel.Visible = _presetCombo.Visible = _psImport.Visible = true;
        RefreshPresetUi();
    }

    // Editing is gated by the normal write permission (Tested, or Experimental opted in) — same as
    // profile switching. On unverified models the live preview is the user's sanity check (a wrong
    // address shows nonsense), and the curve is fully reversible, so we don't hard-block it.
    // (Editable itself is defined next to SyncEnable: it also goes false while a sweep runs.)

    private byte ProfileFanByte() => D.Status().Profile == ProfileId.Silent ? _dev!.FanSilentValue : (byte)0x0D;

    // Switch OFF: give fans back to the current profile's normal behaviour and reset the graph.
    private void RevertToProfileDefault()
    {
        D.WithEcWrite(dev => Ec.SetFanMode(dev, ProfileFanByte()));
        D.Settings.ClearActiveCurve();   // (#49) back to profile fans = nothing to restore at boot
        _cpuS = (int[])DefCpuS.Clone(); _gpuS = (int[])DefGpuS.Clone();
        RefreshMode();
        if (D.Writable()) ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_curve_off"), $"{_dev!.FanMode:X2}={ProfileFanByte():X2}");
    }

    // Re-write the current graph while the curve is already on (e.g. after dragging a point).
    private void ReApply()
    {
        if (_fc == null) return;
        D.WithEcWrite(dev => { Ec.WriteFanCurve(dev, _cpuT, _cpuS, _gpuT, _gpuS); Ec.SetFanMode(dev, _fc.AdvancedModeValue); });
        if (D.Writable()) D.Settings.RecordActiveCurve(null, _cpuT, _cpuS, _gpuT, _gpuS);   // (#49) manual curve
        RefreshMode();
    }

    // Single-curve board (e.g. GF63 12VE): only the CPU curve exists; the GPU plot is hidden
    // and the CPU plot takes the full width.
    private bool Single => _fc is { SingleFan: true };

    // ---------------- comparison layers (view 04) ----------------
    // Names live in settings (FanCurveCompare); resolved to point arrays here. "" stands for
    // the MSI factory default, always offered. A layer whose preset was deleted disappears
    // silently. Layers only paint - the editable curve is always the page's own arrays.
    private string[] AllLayerNames()
    {
        var names = new List<string> { "" };
        names.AddRange(D.Settings.CurvePresets.Select(p => p.Name));
        return names.ToArray();
    }

    private void RebuildLayers()
    {
        _layers.Clear();
        _layerNames = AllLayerNames();
        int ci = 0;
        foreach (var n in D.Settings.FanCurveCompare)
        {
            if (ci >= MaxLayers) break;
            if (n.Length == 0) _layers.Add((Lang.T("fc_layer_default"), DefCpuS, DefGpuS, LayerColors[ci++]));
            else if (_fc != null && D.Settings.FindPreset(n) is { } p && p.IsValid(_fc))
                _layers.Add((p.Name, p.CpuSpeed, p.GpuSpeed, LayerColors[ci++]));
        }
    }

    private void ToggleLayer(string name)
    {
        var list = D.Settings.FanCurveCompare;
        if (list.Contains(name)) list.Remove(name);
        else if (list.Count < MaxLayers) list.Add(name);
        else return;   // three is the cap: more lines than that and nothing is readable
        D.SaveSettings();
        RebuildLayers();
        Invalidate();
    }

    // ---------------- intent tiles (view 08) ----------------
    private static readonly CurveModel.Intent[] Intents =
        { CurveModel.Intent.Quiet, CurveModel.Intent.Balanced, CurveModel.Intent.Cool, CurveModel.Intent.Max };

    private static string IntentKey(CurveModel.Intent i) => i switch
    {
        CurveModel.Intent.Quiet => "fc_intent_quiet",
        CurveModel.Intent.Cool => "fc_intent_cool",
        CurveModel.Intent.Max => "fc_intent_max",
        _ => "fc_intent_balanced",
    };

    // Height budget: the charts need at least MinPlotH. At the 900x620 minimum window the page
    // is ~530 px tall, which fits the table OR the tiles but not both - the table wins (it is
    // the one the user just clicked), the tiles hide until there is room again.
    private int MinPlotH => S(150);
    private bool ShowTable => _view == ViewChart && D.Settings.FanCurveTable && _fc != null;
    private bool ShowIntents => _view == ViewChart && D.Settings.FanCurveIntents && _fc != null
                                && Height - 124 - (ShowTable ? TableH + S(12) : 0) - (ContentTop + IntentH + S(12)) >= MinPlotH;

    // Which tile matches the current shape (both fans), -1 = none. Drives the "active" look.
    // What to call the shape that is loaded right now, most specific first:
    // a saved preset whose points match it, then one of the four intent tiles, then the MSI
    // factory curve, and only if it is none of those - "your own".
    private string CurveInUseName()
    {
        foreach (var pr in D.Settings.CurvePresets)
            if (_fc != null && pr.IsValid(_fc) && CurveModel.SameShape(pr.CpuSpeed, _cpuS) && (Single || CurveModel.SameShape(pr.GpuSpeed, _gpuS)))
                return pr.Name;
        int intent = ActiveIntent();
        if (intent >= 0) return Lang.T(IntentKey(Intents[intent]));
        if (CurveModel.SameShape(DefCpuS, _cpuS) && (Single || CurveModel.SameShape(DefGpuS, _gpuS)))
            return Lang.T("fc_layer_default");
        return Lang.T("fc_play_curve_manual");
    }

    private int ActiveIntent()
    {
        for (int i = 0; i < Intents.Length; i++)
        {
            var c = CurveModel.IntentShape(Intents[i], DefCpuS, _cpuT);
            var gpu = CurveModel.IntentShape(Intents[i], DefGpuS, _gpuT);
            if (CurveModel.SameShape(c, _cpuS) && (Single || CurveModel.SameShape(gpu, _gpuS))) return i;
        }
        return -1;
    }

    private void ApplyIntent(int i)
    {
        _cpuS = CurveModel.IntentShape(Intents[i], DefCpuS, _cpuT);
        _gpuS = CurveModel.IntentShape(Intents[i], DefGpuS, _gpuT);
        _loaded = true;
        if (_enable.Checked && Editable) ReApply();
        Invalidate();
    }

    // ---------------- coupled table: inline % editor (view 05) ----------------
    private void OpenCellEditor(int row, int fan)
    {
        if (!Editable) return;
        CloseCellEditor(commit: true);
        _editRow = row; _editFan = fan;
        var cell = _tablePctCells[row, fan];
        _cellEdit.SetBounds(cell.X + 2, cell.Y + 2, cell.Width - 4, cell.Height - 4);
        _cellEdit.Text = (fan == 0 ? _cpuS : _gpuS)[row].ToString();
        _cellEdit.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _cellEdit.BackColor = Theme.Surface; _cellEdit.ForeColor = Theme.Accent;
        _cellEdit.BorderStyle = BorderStyle.FixedSingle;
        _cellEdit.Visible = true;
        _cellEdit.BringToFront();
        _cellEdit.Focus();
        _cellEdit.SelectAll();
    }

    private void CloseCellEditor(bool commit)
    {
        if (_editRow < 0) return;
        int row = _editRow, fan = _editFan;
        _editRow = _editFan = -1;
        _cellEdit.Visible = false;
        if (commit && int.TryParse(_cellEdit.Text.Trim().TrimEnd('%'), out int v))
        {
            int[] s = fan == 0 ? _cpuS : _gpuS;
            // same rule as a drag: 0-MaxPct and never past either neighbour
            int lo = row > 0 ? s[row - 1] : 0;
            int hi = row < s.Length - 1 ? s[row + 1] : MaxPct;
            int nv = Math.Clamp(Math.Clamp(v, 0, MaxPct), lo, hi);
            if (nv != s[row])
            {
                s[row] = nv;
                if (_enable.Checked && Editable) ReApply();
            }
        }
        Invalidate();
    }

    // ---------------- equalizer view (06): one vertical fader per node ----------------
    // Painted on the page (like the chart nodes) rather than 12 child controls: the drag/
    // clamp rule is exactly the chart's, hit-testing lives next to the chart's, and there is
    // no z-order fight with the painted card. Wheel over a fader = +-1 pp.
    private Rectangle[,] _eqTracks = new Rectangle[0, 0];   // [fan, node] full track hit rects
    private int _eqHoverFan = -1, _eqHoverIdx = -1;

    private Rectangle EqGroupRect(int fan)
    {
        var c = ContentRect;
        if (Single) return c;
        int gap = 40, gw = (c.Width - gap) / 2;
        return new Rectangle(c.X + fan * (gw + gap), c.Y, gw, c.Height);
    }

    // Track rect of node i in fan group: faders spread evenly; a slim column each.
    private Rectangle EqTrack(int fan, int i, int n)
    {
        var gr = EqGroupRect(fan);
        // title row, then a value row above the tracks, temperature row below; a left gutter
        // for the % scale so "100%" never collides with the first fader
        int titleH = S(44), valueH = S(30), footH = S(30), sidePadL = S(58), sidePadR = S(24), trackW = S(22);
        int usable = gr.Width - sidePadL - sidePadR;
        int colW = usable / Math.Max(1, n);
        int x = gr.X + sidePadL + i * colW + (colW - trackW) / 2;
        int top = gr.Y + titleH + valueH;
        return new Rectangle(x, top, trackW, gr.Bottom - footH - top);
    }

    private void SetEqValueFromY(int fan, int i, int y)
    {
        var tr = _eqTracks[fan, i];
        int[] s = fan == 0 ? _cpuS : _gpuS;
        int sp = (int)Math.Round((tr.Bottom - y) / (float)tr.Height * MaxPct);
        int lo = i > 0 ? s[i - 1] : 0, hi = i < s.Length - 1 ? s[i + 1] : MaxPct;
        s[i] = Math.Clamp(Math.Clamp(sp, 0, MaxPct), lo, hi);
        Invalidate();
    }

    private void DrawEqualizer(Graphics g)
    {
        int fans = Single ? 1 : 2;
        int n = _cpuS.Length;
        _eqTracks = new Rectangle[fans, n];
        using var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var valFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var tempFont = new Font("Segoe UI", 8.5f);
        using var scaleFont = new Font("Segoe UI", 8f);
        int scaleW = TextRenderer.MeasureText(g, MaxPct + "%", scaleFont).Width + S(6);
        for (int fan = 0; fan < fans; fan++)
        {
            var gr = EqGroupRect(fan);
            Ui.FillCard(g, gr);
            string title = Lang.T(fan == 0 ? (Single ? "fc_fan_single" : "fc_fan_cpu") : "fc_fan_gpu");
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(gr.X + S(16), gr.Y + S(10), gr.Width - S(32), titleFont.Height + S(4)),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);
            int[] temps = fan == 0 ? _cpuT : _gpuT, sp = fan == 0 ? _cpuS : _gpuS;

            // sparkline of the whole shape, top-right of the card: the "what am I building" cue
            var mini = new Rectangle(gr.Right - S(120), gr.Y + S(12), S(100), S(28));
            var mp = new PointF[n];
            for (int k = 0; k < n; k++) mp[k] = new PointF(mini.X + k * mini.Width / (float)(n - 1), mini.Bottom - sp[k] / (float)MaxPct * mini.Height);
            using (var mpen = new Pen(Theme.Accent, Sf(1.6f)) { LineJoin = LineJoin.Round }) g.DrawLines(mpen, mp);

            // shared % scale in the left gutter, grid lines across all faders
            var t0 = EqTrack(fan, 0, n);
            var tn = EqTrack(fan, n - 1, n);
            using (var grid = new Pen(Theme.Border) { DashStyle = DashStyle.Dot })
                for (int v = 0; v <= MaxPct; v += 25)
                {
                    int y = t0.Bottom - (int)(v / (float)MaxPct * t0.Height);
                    g.DrawLine(grid, t0.Left - S(6), y, tn.Right + S(6), y);
                    TextRenderer.DrawText(g, v + "%", scaleFont, new Rectangle(t0.Left - S(10) - scaleW, y - scaleFont.Height / 2 - 1, scaleW, scaleFont.Height + 2), Theme.Faint,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

            int labelHalf = Math.Max(S(20), TextRenderer.MeasureText(g, MaxPct + "%", valFont).Width / 2 + S(4));
            for (int i = 0; i < n; i++)
            {
                var tr = EqTrack(fan, i, n);
                _eqTracks[fan, i] = tr;
                bool hot = fan == _eqHoverFan && i == _eqHoverIdx || fan == _dragFan && i == _dragIdx;
                int fillH = (int)(sp[i] / (float)MaxPct * tr.Height);
                var fillCol = D.Settings.FanCurveZones
                    ? CurveModel.Band(sp[i]) switch { 0 => Theme.Green, 1 => Theme.Amber, _ => Theme.Red }
                    : Theme.AccentFill;
                using (var path = Theme.RoundRect(new RectangleF(tr.X, tr.Y, tr.Width, tr.Height), Sf(6)))
                using (var tb = new SolidBrush(Theme.Surface)) g.FillPath(tb, path);
                using (var bp = new Pen(hot ? Theme.BorderStrong : Theme.Border)) g.DrawRectangle(bp, tr.X, tr.Y, tr.Width, tr.Height);
                if (fillH > 0)
                {
                    using var fb = new SolidBrush(Color.FromArgb(Editable ? 255 : 120, fillCol));
                    g.FillRectangle(fb, tr.X + S(3), tr.Bottom - fillH, tr.Width - S(6), fillH);
                }
                // thumb
                int ty = tr.Bottom - fillH, thH = S(10), thO = S(4);
                using (var thb = new SolidBrush(Theme.Card)) g.FillRectangle(thb, tr.X - thO, ty - thH / 2, tr.Width + thO * 2, thH);
                using (var thp = new Pen(hot ? Theme.Accent : Theme.BorderStrong, hot ? 1.6f : 1f)) g.DrawRectangle(thp, tr.X - thO, ty - thH / 2, tr.Width + thO * 2, thH);
                // value above (in the value row), temperature below (in the foot row)
                int cx = tr.X + tr.Width / 2;
                TextRenderer.DrawText(g, sp[i] + "%", valFont, new Rectangle(cx - labelHalf, tr.Y - S(8) - valFont.Height, labelHalf * 2, valFont.Height), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, temps[i] + "°", tempFont, new Rectangle(cx - labelHalf, tr.Bottom + S(6), labelHalf * 2, tempFont.Height), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }
        }
    }

    // ---------------- deck view (W2): radial dials + crossfader ----------------
    // Each fan is ONE radial dial, exactly as the mockup: the curve is drawn around a circle -
    // temperature is the ANGLE (leftmost point = the first node, rightmost = the last), fan
    // speed is the RADIUS (centre = 0 %, rim = 100 %), and the ghost sits in the middle. Every
    // node is a handle: drag it toward the rim for more speed, toward the ghost for less. It is
    // the same curve as everywhere else, wrapped around a circle instead of laid on an axis.
    //
    // Below both dials sits the crossfader: pick a shape for pole A and pole B (a saved preset
    // or one of the four intent shapes), then slide between them - at 30 % the curve sits 30 %
    // of the way from A to B, node by node. One move instead of dragging six points, and the
    // result is a normal curve you can save as a preset. The two poles must differ; picking the
    // same shape on both sides is refused (the fader would do nothing).
    private Rectangle[,] _dials = new Rectangle[0, 0];     // hit rects per node (fan, node)
    private int _dialHoverFan = -1, _dialHoverIdx = -1;
    private readonly ThemedComboBox _poleA = new() { Width = 170 }, _poleB = new() { Width = 170 };
    private readonly Slider _fader = new(0, 100, 50) { ShowValue = false, Width = 320 };
    private readonly HelpDot _deckHelp = new();
    private int _faderMix = 50;
    private bool _syncingPoles;
    private static readonly string[] PoleIntentKeys = { "fc_intent_quiet", "fc_intent_balanced", "fc_intent_cool", "fc_intent_max" };

    private void InitDeck()
    {
        foreach (var c in new Control[] { _poleA, _poleB, _fader, _deckHelp }) { c.Visible = false; Controls.Add(c); }
        _deckHelp.TextProvider = () => Lang.T("fc_deck_help");
        _poleA.SelectedIndexChanged += (_, _) => { if (!_syncingPoles) { EnsurePolesDiffer(_poleA); Invalidate(); } };
        _poleB.SelectedIndexChanged += (_, _) => { if (!_syncingPoles) { EnsurePolesDiffer(_poleB); Invalidate(); } };
        _fader.ValueChanged += v => { _faderMix = v; ApplyFader(); };
        _fader.MouseUp += (_, _) => { if (_enable.Checked && Editable) ReApply(); };
    }

    // A fader between two identical shapes does nothing - move the OTHER pole to a different
    // entry instead of silently accepting it.
    private void EnsurePolesDiffer(ThemedComboBox changed)
    {
        if (_poleA.SelectedIndex != _poleB.SelectedIndex) return;
        var other = changed == _poleA ? _poleB : _poleA;
        _syncingPoles = true;
        try
        {
            int n = other.Items.Count;
            if (n > 1) other.SelectedIndex = (changed.SelectedIndex + 1) % n;
        }
        finally { _syncingPoles = false; }
    }

    private void FillPoles()
    {
        _syncingPoles = true;
        try
        {
            foreach (var cb in new[] { _poleA, _poleB })
            {
                int keep = cb.SelectedIndex;
                cb.Items.Clear();
                foreach (var k in PoleIntentKeys) cb.Items.Add(Lang.T(k));
                foreach (var p in D.Settings.CurvePresets) cb.Items.Add(p.Name);
                cb.SelectedIndex = keep >= 0 && keep < cb.Items.Count ? keep : (cb == _poleA ? 0 : 3);
            }
            if (_poleA.SelectedIndex == _poleB.SelectedIndex && _poleB.Items.Count > 1)
                _poleB.SelectedIndex = (_poleA.SelectedIndex + 1) % _poleB.Items.Count;
        }
        finally { _syncingPoles = false; }
    }

    private (int[] cpu, int[] gpu) PoleShape(ThemedComboBox cb)
    {
        int i = cb.SelectedIndex;
        if (i >= 0 && i < PoleIntentKeys.Length)
        {
            var it = Intents[i];
            return (CurveModel.IntentShape(it, DefCpuS, _cpuT), CurveModel.IntentShape(it, DefGpuS, _gpuT));
        }
        int pi = i - PoleIntentKeys.Length;
        if (pi >= 0 && pi < D.Settings.CurvePresets.Count && _fc != null && D.Settings.CurvePresets[pi].IsValid(_fc))
        {
            var p = D.Settings.CurvePresets[pi];
            return (p.CpuSpeed, p.GpuSpeed);
        }
        return (DefCpuS, DefGpuS);
    }

    private void ApplyFader()
    {
        if (!Editable) return;
        var a = PoleShape(_poleA); var b = PoleShape(_poleB);
        var c = CurveModel.Blend(a.cpu, b.cpu, _faderMix);
        var gp = CurveModel.Blend(a.gpu, b.gpu, _faderMix);
        if (c.Length != _cpuS.Length || gp.Length != _gpuS.Length) return;
        _cpuS = c; _gpuS = gp;
        _loaded = true;
        Invalidate();
    }

    // ---- geometry ----
    private int FaderH => S(96);
    private Rectangle DeckRect => new(ContentRect.X, ContentRect.Y, ContentRect.Width, ContentRect.Height - FaderH - S(12));
    private Rectangle FaderRect => new(ContentRect.X, ContentRect.Bottom - FaderH, ContentRect.Width, FaderH);

    // The circle a fan's dial is drawn in.
    private (PointF centre, float rMax) DialGeometry(int fan)
    {
        var d = DeckRect;
        int fans = Single ? 1 : 2;
        int gap = S(40), w = (d.Width - gap * (fans - 1)) / fans;
        // The circle lives BELOW the card title, and its radius leaves room for the temperature
        // labels drawn just outside the rim - otherwise the topmost label lands on the title.
        int titleBand = S(40), labelRoom = S(30);
        var box = new Rectangle(d.X + fan * (w + gap), d.Y + titleBand, w, d.Height - titleBand - S(16));
        float r = Math.Min(box.Width, box.Height) / 2f - labelRoom;
        return (new PointF(box.X + box.Width / 2f, box.Y + box.Height / 2f), Math.Max(S(40), r));
    }

    // Node i of n sits on the sweep from 200° (bottom-left) clockwise through the top to 340°.
    private static float NodeAngle(int i, int n) => 200f + (n <= 1 ? 0 : i * 200f / (n - 1));

    private PointF NodePoint(int fan, int i, int n, int speedPct)
    {
        var (c, rMax) = DialGeometry(fan);
        double a = NodeAngle(i, n) * Math.PI / 180.0;
        float r = rMax * (0.22f + 0.78f * Math.Clamp(speedPct, 0, MaxPct) / (float)MaxPct);   // inner hole keeps the ghost visible
        return new PointF(c.X + (float)Math.Cos(a) * r, c.Y + (float)Math.Sin(a) * r);
    }

    // Dragging: the distance from the centre becomes the speed (angle stays fixed - the node's
    // temperature never moves, exactly like the chart view).
    private void SetDialFromDrag(int fan, int i, Point mouse)
    {
        var (c, rMax) = DialGeometry(fan);
        float dist = (float)Math.Sqrt(Math.Pow(mouse.X - c.X, 2) + Math.Pow(mouse.Y - c.Y, 2));
        int sp = (int)Math.Round((dist / rMax - 0.22f) / 0.78f * MaxPct);
        int[] s = fan == 0 ? _cpuS : _gpuS;
        int lo = i > 0 ? s[i - 1] : 0, hi = i < s.Length - 1 ? s[i + 1] : MaxPct;
        int nv = Math.Clamp(Math.Clamp(sp, 0, MaxPct), lo, hi);
        if (nv != s[i]) { s[i] = nv; Invalidate(); }
    }

    private void LayoutDeck()
    {
        bool show = _view == ViewDeck && _fc != null;
        _poleA.Visible = _poleB.Visible = _fader.Visible = _deckHelp.Visible = show;
        if (!show) return;
        var fr = FaderRect;
        int cy = fr.Y + S(56);
        _fader.Width = Math.Max(S(160), Math.Min(S(420), fr.Width - 2 * S(200)));
        _fader.Height = S(26);
        _fader.SetBounds(fr.X + (fr.Width - _fader.Width) / 2, cy - _fader.Height / 2, _fader.Width, _fader.Height);
        _poleA.SetBounds(_fader.Left - S(14) - _poleA.Width, cy - _poleA.Height / 2, _poleA.Width, _poleA.Height);
        _poleB.SetBounds(_fader.Right + S(14), cy - _poleB.Height / 2, _poleB.Width, _poleB.Height);
        _poleA.Enabled = _poleB.Enabled = _fader.Enabled = Editable;
        _deckHelp.Size = new Size(S(22), S(22));
        var d = DeckRect;
        _deckHelp.SetBounds(d.Right - S(10) - _deckHelp.Width, d.Y + S(4), _deckHelp.Width, _deckHelp.Height);
    }

    private void DrawDeck(Graphics g)
    {
        int fans = Single ? 1 : 2, n = _cpuS.Length;
        _dials = new Rectangle[fans, n];
        var d = DeckRect;
        Ui.FillCard(g, d);
        using var titleF = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var valF = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var smallF = new Font("Segoe UI", 8.5f);
        using var capF = new Font("Segoe UI", 9f, FontStyle.Bold);
        var live = _live.Sample;
        var cols = new[] { Theme.Accent, Theme.Violet };

        for (int fan = 0; fan < fans; fan++)
        {
            var (c, rMax) = DialGeometry(fan);
            int[] temps = fan == 0 ? _cpuT : _gpuT, sp = fan == 0 ? _cpuS : _gpuS;
            var col = cols[fan];
            string title = Lang.T(fan == 0 ? (Single ? "fc_fan_single" : "fc_fan_cpu") : "fc_fan_gpu");
            int colW = (d.Width - S(40) * (fans - 1)) / fans;
            TextRenderer.DrawText(g, title, titleF, new Rectangle(d.X + fan * (colW + S(40)), d.Y + S(10), colW, titleF.Height + S(4)),
                Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

            // rings every 25 % up to the model top + the audibility tint between them when zones are on
            for (int v = 25; v <= MaxPct; v += 25)
            {
                float rr = rMax * (0.22f + 0.78f * v / (float)MaxPct);
                using var rp = new Pen(Color.FromArgb(v == MaxPct ? 90 : 55, Theme.Border), 1f) { DashStyle = v == MaxPct ? DashStyle.Solid : DashStyle.Dot };
                g.DrawEllipse(rp, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                TextRenderer.DrawText(g, v + "%", smallF, new Rectangle((int)(c.X - S(18)), (int)(c.Y - rr) - smallF.Height / 2, S(36), smallF.Height),
                    Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
            if (D.Settings.FanCurveZones)
            {
                float rQuiet = rMax * (0.22f + 0.78f * CurveModel.QuietMax / (float)MaxPct);
                float rLoud = rMax * (0.22f + 0.78f * CurveModel.LoudMin / (float)MaxPct);
                using var qb = new SolidBrush(Color.FromArgb(Theme.Dark ? 14 : 20, Theme.Green));
                g.FillEllipse(qb, c.X - rQuiet, c.Y - rQuiet, rQuiet * 2, rQuiet * 2);
                using var lb = new SolidBrush(Color.FromArgb(Theme.Dark ? 12 : 18, Theme.Red));
                using var gp2 = new GraphicsPath();
                gp2.AddEllipse(c.X - rMax, c.Y - rMax, rMax * 2, rMax * 2);
                gp2.AddEllipse(c.X - rLoud, c.Y - rLoud, rLoud * 2, rLoud * 2);
                g.FillPath(lb, gp2);
            }

            // the curve itself: node points joined around the arc
            var pts = new PointF[n];
            for (int i = 0; i < n; i++) pts[i] = NodePoint(fan, i, n, sp[i]);
            using (var fillPath = new GraphicsPath())
            {
                fillPath.AddLines(pts.Concat(new[] { c }).ToArray());
                fillPath.CloseFigure();
                using var fb = new SolidBrush(Color.FromArgb(Theme.Dark ? 26 : 20, col));
                g.FillPath(fb, fillPath);
            }
            using (var pen = new Pen(col, Sf(2.4f)) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, pts);

            // temperature labels just outside the rim, at each node's angle
            for (int i = 0; i < n; i++)
            {
                double a = NodeAngle(i, n) * Math.PI / 180.0;
                float lr = rMax + S(14);
                var lp = new PointF(c.X + (float)Math.Cos(a) * lr, c.Y + (float)Math.Sin(a) * lr);
                TextRenderer.DrawText(g, temps[i] + "°", smallF, new Rectangle((int)lp.X - S(18), (int)lp.Y - smallF.Height / 2, S(36), smallF.Height),
                    Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }

            // handles
            for (int i = 0; i < n; i++)
            {
                bool hot = fan == _dialHoverFan && i == _dialHoverIdx || fan == _dragFan && i == _dragIdx;
                float hr = hot ? Sf(8f) : Sf(6.5f);
                _dials[fan, i] = new Rectangle((int)(pts[i].X - S(12)), (int)(pts[i].Y - S(12)), S(24), S(24));
                var hc = D.Settings.FanCurveZones
                    ? CurveModel.Band(sp[i]) switch { 0 => Theme.Green, 1 => Theme.Amber, _ => Theme.Red }
                    : col;
                using (var hb = new SolidBrush(hc)) g.FillEllipse(hb, pts[i].X - hr, pts[i].Y - hr, hr * 2, hr * 2);
                using (var hp = new Pen(Theme.Card, Sf(2f))) g.DrawEllipse(hp, pts[i].X - hr, pts[i].Y - hr, hr * 2, hr * 2);
                if (hot)
                    TextRenderer.DrawText(g, sp[i] + "%", valF, new Rectangle((int)pts[i].X - S(26), (int)pts[i].Y - S(30), S(52), valF.Height),
                        Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }

            // the ghost in the axis + the live reading under it
            float gs = rMax * 0.30f;
            TrayIconFactory.DrawGhost(g, c.X - gs / 2, c.Y - gs / 2 - S(6), gs, Color.FromArgb(150, col), Theme.Card);
            int t = fan == 0 ? live.CpuTemp : live.GpuTemp, duty = fan == 0 ? live.CpuFan : live.GpuFan;
            string mid = live.Time != DateTime.MinValue && t > 0
                ? $"{t}°  →  {(int)Math.Round(CurveModel.SpeedAt(temps, sp, t))}%"
                : "--";
            TextRenderer.DrawText(g, mid, valF, new Rectangle((int)(c.X - rMax), (int)(c.Y + gs / 2 + S(2)), (int)(rMax * 2), valF.Height),
                Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            if (live.Time != DateTime.MinValue && t > 0)
                TextRenderer.DrawText(g, string.Format(Lang.T("fc_deck_now"), duty), smallF,
                    new Rectangle((int)(c.X - rMax), (int)(c.Y + gs / 2 + S(4)) + valF.Height, (int)(rMax * 2), smallF.Height),
                    Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        // ---- crossfader ----
        var fr = FaderRect;
        Ui.FillCard(g, fr);
        TextRenderer.DrawText(g, Lang.T("fc_fader"), capF, new Rectangle(fr.X + S(16), fr.Y + S(10), fr.Width - S(32), capF.Height + S(2)),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "A", capF, new Rectangle(_poleA.Left, _poleA.Top - capF.Height - S(2), _poleA.Width, capF.Height),
            Theme.Accent, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "B", capF, new Rectangle(_poleB.Left, _poleB.Top - capF.Height - S(2), _poleB.Width, capF.Height),
            Theme.Violet, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        string mixTxt = _faderMix == 0 ? "100 % A" : _faderMix == 100 ? "100 % B" : $"{100 - _faderMix} % A  ·  {_faderMix} % B";
        TextRenderer.DrawText(g, mixTxt, capF, new Rectangle(_fader.Left, _fader.Top - capF.Height - S(4), _fader.Width, capF.Height),
            Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }

    // ---------------- playground view: "curve in action" ----------------
    // Top: ONE big chart with both curves (CPU cyan, GPU violet) and, on top of them, what the
    // machine REALLY did over the last hour - one small dot per HwHistory sample at
    // (temperature, actual duty), CPU dots cyan, GPU dots violet, newer = brighter. So the eye
    // sees at once whether the laptop actually walks along the curve or sits above/below it.
    // The live operating point is the big dot. Airflow "particles" on the right run at a speed
    // set by the CURRENT real fan speed - a wind gauge, not a simulation.
    // Bottom: a collapsible "diagnostics" panel with the fan sweep (Core/FanSweep) - real EC
    // writes behind a consent dialog, its own explanation and start button, results table.
    private readonly Button _sweepStart = new(), _sweepCopy = new(), _sweepToggle = new();
    private readonly HelpDot _playHelp = new(), _sweepHelp = new(), _reactHelp = new();   // click-open explanations
    private readonly ThemedComboBox _sweepHistory = new() { Width = 210 };   // past sweeps, newest first
    private List<FanSweepHistory.Entry> _sweepEntries = new();
    private FanSweepHistory.Entry? _sweepEntry;   // set when the shown result came from the picker
    private bool _syncingHistory;
    private readonly System.Windows.Forms.Timer _airTimer = new() { Interval = 90 };
    private float _airPhase;
    private readonly Dictionary<(int fan, int temp, int duty), Rectangle> _dotHits = new();   // history dots, hit-tested for labels
    private (int fan, int temp, int duty)? _hoverDot;
    private readonly HashSet<(int fan, int temp, int duty)> _pinnedDots = new();
    private readonly CheckItem _optDotLabels = new(Lang.T("fc_play_values"), false);
    private bool _sweepOpen;
    private FanSweep.Result? _sweepResult;
    private CancellationTokenSource? _sweepCts;
    private string _sweepStatus = "";
    private int _sweepStep, _sweepCount;

    private void InitPlayground()
    {
        foreach (var c in new Control[] { _sweepStart, _sweepCopy, _sweepToggle, _playHelp, _sweepHelp, _reactHelp, _optDotLabels, _sweepHistory }) { c.Visible = false; Controls.Add(c); }
        _sweepHistory.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingHistory || _sweepHistory.SelectedIndex < 0 || _sweepHistory.SelectedIndex >= _sweepEntries.Count) return;
            SelectSweepEntry(_sweepHistory.SelectedIndex);
            Restyle(); LayoutButtons(); Invalidate();
        };
        _sweepEntries = FanSweepHistory.Load();
        FillSweepHistory();
        _optDotLabels.Toggled += v => { D.Settings.FanCurveDotLabels = v; D.SaveSettings(); Invalidate(); };
        _playHelp.TextProvider = () => Lang.T("fc_play_help");
        _sweepHelp.TextProvider = () => Lang.T("fc_sweep_help");
        _reactHelp.TextProvider = () => Lang.T("fc_react_help");
        _airTimer.Tick += (_, _) =>
        {
            if (_view != ViewPlay || !Visible) return;
            _airPhase += 1f;
            var a = PlayAirRect;
            Invalidate(a);
        };
        _sweepToggle.Click += (_, _) => { _sweepOpen = !_sweepOpen; Restyle(); LayoutButtons(); Invalidate(); };
        _sweepStart.Click += (_, _) => { if (_sweepCts != null) _sweepCts.Cancel(); else StartSweep(); };
        _sweepCopy.Click += (_, _) =>
        {
            if (_sweepResult == null || _dev == null) return;
            // A report re-exported from history must describe the machine as it was AT THE TIME:
            // firmware, app version and the findings the app worded then. Stamping today's values
            // on an old run would put a false claim in whatever issue it gets pasted into.
            var e2 = _sweepEntry;
            Ui.CopyText(e2 != null
                ? FanSweep.Report(_sweepResult, _dev, e2.Firmware, e2.AppVersion, e2.Findings.Count > 0 ? e2.Findings : FindingLines(_sweepResult))
                : FanSweep.Report(_sweepResult, _dev, D.Firmware(), D.AppVersion(), FindingLines(_sweepResult)));
        };
    }

    // ---- geometry ----
    // Stacked layout needs more height than the side-by-side one.
    // What the diagnostics panel needs: one row when collapsed, a side-by-side layout on a wide
    // window, a stacked one when narrow. It is never squeezed - if the two blocks do not fit in
    // the window, the view scrolls (see PlayNeededH).
    private int SweepPanelH => !_sweepOpen ? S(64) : (Width >= S(1000) + Pad * 2 ? S(320) : S(640));

    // Height the In-action view wants: a usable chart + the readings row + the panel.
    private int PlayMinChartH => S(300);
    private int PlayNeededH => PlayMinChartH + S(56) + S(12) + SweepPanelH;
    // Every rectangle of this view descends from here, with the scroll already applied, so
    // painting, child controls and hit tests all live in plain client coordinates.
    private Rectangle PlayArea { get { var c = ContentRect; c.Offset(0, -_scrollY); return c; } }
    private Rectangle PlayChartRect { get { var c = PlayArea; return new Rectangle(c.X, c.Y, c.Width, Math.Max(0, c.Height - S(56) - SweepPanelH - S(12))); } }
    private Rectangle PlayInfoRect { get { var ch = PlayChartRect; return new Rectangle(ch.X, ch.Bottom + S(6), ch.Width, S(50)); } }
    private Rectangle PlayAirRect { get { var ch = PlayChartRect; return new Rectangle(ch.Right - S(130), ch.Y + S(50), S(120), ch.Height - S(90)); } }
    private Rectangle SweepPanelRect { get { var c = PlayArea; return new Rectangle(c.X, c.Bottom - SweepPanelH, c.Width, SweepPanelH); } }

    private void LayoutPlayground()
    {
        bool show = _view == ViewPlay && _fc != null;
        if (!show)
        {
            // scrolling belongs to this view only; the others are laid out to fit by construction
            _scrollY = 0;
            _sweepToggle.Visible = _playHelp.Visible = _sweepHelp.Visible = false;
            _sweepStart.Visible = _sweepCopy.Visible = false;
            _reactHelp.Visible = _sweepHistory.Visible = _optDotLabels.Visible = false;
            if (_airTimer.Enabled) _airTimer.Stop();
            return;
        }
        _scrollY = Math.Clamp(_scrollY, 0, ScrollMax);   // the window may have grown since the last scroll
        var ch = PlayChartRect;
        _playHelp.Size = new Size(S(22), S(22));
        Place(_playHelp, true, ch.Right - S(16) - _playHelp.Width, ch.Y + S(10), _playHelp.Width, _playHelp.Height);
        _optDotLabels.Checked = D.Settings.FanCurveDotLabels;
        _optDotLabels.Text = Lang.T("fc_play_values");
        _optDotLabels.Height = S(26);
        _optDotLabels.Width = _optDotLabels.PreferredWidth;
        Place(_optDotLabels, true, _playHelp.Left - S(12) - _optDotLabels.Width, ch.Y + S(8), _optDotLabels.Width, _optDotLabels.Height);
        _optDotLabels.ForeColor = Theme.Text; _optDotLabels.BackColor = Theme.Card;
        var sp = SweepPanelRect;
        Place(_sweepToggle, true, sp.Right - S(16) - _sweepToggle.Width, sp.Y + S(14), _sweepToggle.Width, S(32));
        _sweepHelp.Size = new Size(S(22), S(22));
        Place(_sweepHelp, true, _sweepToggle.Left - S(10) - _sweepHelp.Width, sp.Y + S(14) + (S(32) - _sweepHelp.Height) / 2, _sweepHelp.Width, _sweepHelp.Height);
        Place(_sweepStart, _sweepOpen, sp.X + S(20), sp.Bottom - S(50), _sweepStart.Width, S(34));
        Place(_sweepCopy, _sweepOpen && _sweepResult is { Steps.Count: > 0 }, _sweepStart.Right + S(10), sp.Bottom - S(50), _sweepCopy.Width, S(34));
        _sweepHistory.Width = S(150);   // fits "2026-08-16 15:55" at any DPI (a fixed 210 clipped it at 140 %)
        Place(_sweepHistory, _sweepOpen && _sweepEntries.Count > 0,
              Math.Max(_sweepCopy.Right + S(16), sp.Right - S(20) - _sweepHistory.Width), sp.Bottom - S(48),
              _sweepHistory.Width, _sweepHistory.Height);
        _sweepStart.Enabled = _sweepCts != null || (Editable && D.Status().Known && !D.Simulating());
        foreach (var b in new[] { _sweepCopy, _sweepStart })
            b.ForeColor = b.Enabled ? (b == _sweepStart && _sweepCts == null ? Theme.AccentText : Theme.Text) : Theme.Faint;
        if (!_airTimer.Enabled) _airTimer.Start();
    }

    // The findings as the panel shows them (localised, fans named) - shared by the painter
    // and the copied report so the two can never drift apart.
    private void FillSweepHistory(int select = 0)
    {
        _syncingHistory = true;
        try
        {
            _sweepHistory.Items.Clear();
            foreach (var e in _sweepEntries) _sweepHistory.Items.Add(e.Label());
            if (_sweepHistory.Items.Count > 0) _sweepHistory.SelectedIndex = Math.Clamp(select, 0, _sweepHistory.Items.Count - 1);
        }
        finally { _syncingHistory = false; }
        // The programmatic selection above is guarded, so it raises no event: load the entry the
        // picker now shows by hand. Without this the panel offers the newest run in the list and
        // shows "no sweep yet" underneath it, and picking that same row changes nothing.
        SelectSweepEntry(_sweepHistory.SelectedIndex);
    }

    private void SelectSweepEntry(int index)
    {
        if (index < 0 || index >= _sweepEntries.Count) { _sweepEntry = null; return; }
        _sweepEntry = _sweepEntries[index];
        _sweepResult = _sweepEntry.ToResult();
        _sweepStatus = "";
    }

    private IEnumerable<string> FindingLines(FanSweep.Result r) =>
        FanSweep.Findings(r).Select(f =>
        {
            var args = (object[])f.args.Clone();
            if (args.Length > 0 && args[0] is int fi && IsFanScopedFinding(f.key))
                args[0] = Lang.T(fi == 1 ? (Single ? "fc_fan_single" : "fc_fan_cpu") : "fc_fan_gpu");
            return string.Format(Lang.T(f.key), args);
        });

    private static bool IsFanScopedFinding(string key) =>
        key is "fc_find_follows" or "fc_find_drop" or "fc_find_floor" or "fc_find_notach";

    private void StartSweep()
    {
        if (!Editable || _dev == null || _fc == null) return;
        // same rule as Apply: a curve needs Advanced fan mode, which drops the Silent power cap.
        // Unlike Apply this is a TEMPORARY test, so the profile the user had comes back at the end.
        // BOTH questions first, THEN the profile change. Asking about Silent, switching to
        // Balanced and only then asking for consent to the writes left the machine in Balanced
        // when the second answer was No - the test never ran, but Silent was gone.
        bool wasSilent = D.Current() == ProfileId.Silent;
        if (wasSilent &&
            MessageBox.Show(FindForm(), Lang.T("fc_sweep_silent"), Lang.T("fc_sweep_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        string consent = string.Format(Lang.T("fc_sweep_consent"),
            $"0x{_fc.CpuSpeedBase:X2}-0x{_fc.CpuSpeedBase + _fc.Points - 1:X2}" + (Single ? "" : $", 0x{_fc.GpuSpeedBase:X2}-0x{_fc.GpuSpeedBase + _fc.Points - 1:X2}"),
            $"0x{_dev.FanMode:X2}");
        if (MessageBox.Show(FindForm(), consent, Lang.T("fc_sweep_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (wasSilent) D.SetProfile(ProfileId.Balanced);   // only now, with both answers in hand

        // remember what to put back: the page's own curve if the switch is on, else the profile fans
        bool wasOn = _enable.Checked;
        int[] ct = (int[])_cpuT.Clone(), cs = (int[])_cpuS.Clone(), gt = (int[])_gpuT.Clone(), gs = (int[])_gpuS.Clone();
        byte restoreMode = wasOn ? _fc.AdvancedModeValue : ProfileFanByte();
        var dev = _dev; var fc = _fc;
        var session = D.EcSession();   // blocks the automatic engines and a model-DB swap while we hold the fans
        _sweepCts = new CancellationTokenSource();
        var ctk = _sweepCts.Token;
        _sweepResult = null; _sweepEntry = null; _sweepStatus = Lang.T("fc_sweep_running"); _sweepStep = 0; _sweepCount = FanSweep.StepsFor(dev).Length;
        // freeze the user-facing writers for the duration: switch, default button, cell editor
        CloseCellEditor(commit: false);
        _enable.Enabled = _enableLabel.Enabled = _default.Enabled = false;
        Restyle(); LayoutButtons(); Invalidate();
        ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_sweep_start"));
        Task.Run(() =>
        {
            FanSweep.Result? r = null;
            try
            {
                r = FanSweep.Run(dev, FanSweep.StepsFor(dev),
                    (i, n, msg) => { try { BeginInvoke(() => { _sweepStep = i; _sweepCount = n; _sweepStatus = msg; Invalidate(); }); } catch { } },
                    ctk);
            }
            finally
            {
                // restore, whatever happened: the previous curve tables and the previous mode
                try { Ec.WriteFanCurve(dev, ct, cs, gt, gs); Ec.SetFanMode(dev, restoreMode); } catch { }
                session.Dispose();
                try
                {
                    BeginInvoke(() =>
                    {
                        _sweepCts?.Dispose(); _sweepCts = null;
                        _sweepResult = r;
                        ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_sweep_end"), r == null ? "" : $"{r.Steps.Count} steps");
                        if (r is { Steps.Count: > 0 })
                        {
                            _sweepEntries = FanSweepHistory.Add(r, dev.Name, D.Firmware(), D.AppVersion(), FindingLines(r));
                            FillSweepHistory();   // selects the run that just finished (and clears the status)
                        }
                        // after FillSweepHistory: selecting an entry resets the status line
                        _sweepStatus = r == null ? "" : r.Aborted ? Lang.T("fc_sweep_aborted") : r.Error != null ? r.Error : Lang.T("fc_sweep_done");
                        // the profile switched away for the test comes back (its recipe rewrites the
                        // fan byte, so it must run AFTER the register restore above - it does: we are
                        // on the UI thread after the worker's finally)
                        if (wasSilent && !wasOn) D.SetProfile(ProfileId.Silent);
                        _enable.Enabled = _enableLabel.Enabled = _default.Enabled = Editable;
                        RefreshMode();
                        Restyle(); LayoutButtons(); Invalidate();
                    });
                }
                catch { }
            }
        });
    }

    private void DrawPlayground(Graphics g)
    {
        using var titleF = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var subF = new Font("Segoe UI", 9f);
        using var bF = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var axisF = new Font("Segoe UI", 8.5f);
        int fans = Single ? 1 : 2;
        var cols = new[] { Theme.Accent, Theme.Violet };
        var live = _live.Sample;
        bool haveLive = live.Time != DateTime.MinValue && live.CpuTemp > 0;

        // ---- big chart: both curves + the last hour of real (temperature, duty) samples ----
        var ch = PlayChartRect;
        Ui.FillCard(g, ch);
        TextRenderer.DrawText(g, Lang.T("fc_play_title"), titleF, new Rectangle(ch.X + S(16), ch.Y + S(10), ch.Width / 2, titleF.Height + S(4)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Lang.T("fc_play_sub"), subF, new Rectangle(ch.X + S(16), ch.Y + S(12) + titleF.Height + S(4), ch.Width - S(200), subF.Height), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        int axisW = TextRenderer.MeasureText(g, MaxPct + "%", axisF).Width + S(10);
        int plotTop = ch.Y + S(20) + titleF.Height + subF.Height + S(14);
        var plot = new Rectangle(ch.X + S(16) + axisW, plotTop, ch.Width - S(32) - axisW - S(140), ch.Bottom - S(34) - plotTop);
        bool plotFits = plot.Width >= S(160) && plot.Height >= S(60);
        if (!plotFits)
        {
            _dotHits.Clear();   // no dots painted -> none to hover, and last paint's rects are stale
            // Too little room for the chart (a very small window with the diagnostics open):
            // say so and carry on - the panel below must still be drawn, never a blank page.
            TextRenderer.DrawText(g, Lang.T("fc_play_small"), subF, new Rectangle(ch.X + S(16), ch.Y + S(50), ch.Width - S(32), subF.Height * 2),
                Theme.Faint, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
            DrawSweepPanel(g, titleF, subF, bF, axisF);
            return;
        }
        using (var grid = new Pen(Theme.Border))
            for (int v = 0; v <= MaxPct; v += 25)
            {
                float y = plot.Bottom - v / (float)MaxPct * plot.Height;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                TextRenderer.DrawText(g, v + "%", axisF, new Rectangle(plot.Left - axisW - S(4), (int)y - axisF.Height / 2, axisW, axisF.Height), Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        int n = _cpuT.Length;
        for (int i = 0; i < n; i++)
        {
            float x = plot.Left + i * plot.Width / (float)(n - 1);
            TextRenderer.DrawText(g, _cpuT[i] + "°", axisF, new Rectangle((int)x - S(20), plot.Bottom + S(6), S(40), axisF.Height), Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        // history dots FIRST (under the curves): one per sample, older = fainter
        List<HwSample> hist;
        try { hist = HwHistory.Window(TimeSpan.FromMinutes(60)); } catch { hist = new List<HwSample>(); }
        int cnt = hist.Count;
        float dr = Sf(2.2f);
        using var dotF = new Font("Segoe UI", 7.5f);
        // The dots carry their values only where the reader asks for it: hovering one shows its
        // label, clicking pins it (click again to unpin), and the "Values" switch turns every
        // label on at once. Labelling all of them by default was a wall of text.
        _dotHits.Clear();
        bool allLabels = D.Settings.FanCurveDotLabels;
        var labelled = new List<PointF>();
        for (int i = cnt - 1; i >= 0; i--)
        {
            var smp = hist[i];
            int alpha = 30 + (int)(150f * i / Math.Max(1, cnt - 1));
            for (int fan = 0; fan < fans; fan++)
            {
                int t = fan == 0 ? smp.CpuTemp : smp.GpuTemp, d = fan == 0 ? smp.CpuFan : smp.GpuFan;
                int[] temps = fan == 0 ? _cpuT : _gpuT;
                if (t <= 0) continue;
                var dotCol = fan == 0 ? Theme.Amber : Color.FromArgb(0xF0, 0x8A, 0x3C);
                var pt = new PointF(plot.Left + CurveModel.OrdinalX(temps, t) * plot.Width / (n - 1), plot.Bottom - Math.Clamp(d, 0, MaxPct) / (float)MaxPct * plot.Height);
                var key = (fan, t, d);
                _dotHits[key] = new Rectangle((int)(pt.X - S(6)), (int)(pt.Y - S(6)), S(12), S(12));
                bool hovered = _hoverDot.HasValue && _hoverDot.Value == key;
                bool pinned = _pinnedDots.Contains(key);
                float rr = hovered || pinned ? dr * 1.6f : dr;
                using (var hb = new SolidBrush(Color.FromArgb(hovered || pinned ? 230 : alpha, dotCol))) g.FillEllipse(hb, pt.X - rr, pt.Y - rr, rr * 2, rr * 2);
                if (!(allLabels || hovered || pinned)) continue;
                if (allLabels && !hovered && !pinned && labelled.Any(q => Math.Abs(q.X - pt.X) < S(24) && Math.Abs(q.Y - pt.Y) < S(13))) continue;
                labelled.Add(pt);
                string dl = $"{t}°/{d}%";
                var dsz = TextRenderer.MeasureText(g, dl, dotF, Size.Empty, TextFormatFlags.NoPadding);
                int dx = (int)pt.X + S(6);
                if (dx + dsz.Width > plot.Right) dx = (int)pt.X - S(6) - dsz.Width;
                var lrect = new Rectangle(dx, (int)pt.Y - dsz.Height / 2, dsz.Width, dsz.Height);
                if (hovered || pinned)
                {
                    using var bb3 = new SolidBrush(Color.FromArgb(Theme.Dark ? 220 : 240, Theme.Card));
                    g.FillRectangle(bb3, Rectangle.Inflate(lrect, S(3), S(2)));
                }
                TextRenderer.DrawText(g, dl, dotF, lrect,
                    Color.FromArgb(hovered || pinned ? 255 : Math.Clamp(alpha + 60, 0, 255), dotCol), TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
        }
        // curves
        for (int fan = 0; fan < fans; fan++)
        {
            int[] sp = fan == 0 ? _cpuS : _gpuS;
            var pts = new PointF[sp.Length];
            for (int i = 0; i < sp.Length; i++)
                pts[i] = new PointF(plot.Left + i * plot.Width / (float)(sp.Length - 1), plot.Bottom - sp[i] / (float)MaxPct * plot.Height);
            using var pen = new Pen(Color.FromArgb(fan == 0 ? 255 : 210, cols[fan]), Sf(2.2f)) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);
        }
        // legend
        int lx = plot.Left + S(6), ly = plot.Top + S(4);
        for (int fan = 0; fan < fans; fan++)
        {
            using var lb = new SolidBrush(cols[fan]);
            g.FillRectangle(lb, lx, ly + S(5), S(14), S(3));
            string name = Lang.T(fan == 0 ? (Single ? "fc_fan_single" : "fc_fan_cpu") : "fc_fan_gpu");
            TextRenderer.DrawText(g, name, axisF, new Rectangle(lx + S(18), ly, S(200), axisF.Height), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            lx += S(18) + TextRenderer.MeasureText(g, name, axisF).Width + S(18);
        }
        string curveName = string.Format(Lang.T("fc_play_curve_name"), CurveInUseName());
        TextRenderer.DrawText(g, curveName, axisF, new Rectangle(lx + S(6), ly, S(260), axisF.Height), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Lang.T("fc_play_legend_dots"), axisF, new Rectangle(lx + S(6), ly + axisF.Height + S(2), S(400), axisF.Height), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        // live operating points (both fans), on the curve like the Chart view
        if (haveLive)
        {
            for (int fan = 0; fan < fans; fan++)
            {
                int t = fan == 0 ? live.CpuTemp : live.GpuTemp;
                if (t <= 0) continue;
                int[] temps = fan == 0 ? _cpuT : _gpuT, sp = fan == 0 ? _cpuS : _gpuS;
                var dot = new PointF(plot.Left + CurveModel.OrdinalX(temps, t) * plot.Width / (n - 1), plot.Bottom - Math.Clamp(CurveModel.SpeedAt(temps, sp, t), 0, MaxPct) / (float)MaxPct * plot.Height);
                using (var halo = new SolidBrush(Color.FromArgb(70, cols[fan]))) g.FillEllipse(halo, dot.X - Sf(12), dot.Y - Sf(12), Sf(24), Sf(24));
                using (var db = new SolidBrush(cols[fan])) g.FillEllipse(db, dot.X - Sf(6), dot.Y - Sf(6), Sf(12), Sf(12));
                using (var cb = new SolidBrush(Color.White)) g.FillEllipse(cb, dot.X - Sf(2.2f), dot.Y - Sf(2.2f), Sf(4.4f), Sf(4.4f));
                if (fan == 0)
                {
                    int duty = live.CpuFan, rpm = live.CpuRpm;
                    string lbl = $"{t}°C  ·  {Lang.T("fc_live_fan")} {duty}%" + (rpm > 0 ? $"  ·  {rpm} rpm" : "");
                    var lsz = TextRenderer.MeasureText(g, lbl, axisF, Size.Empty, TextFormatFlags.NoPadding);
                    int lw2 = lsz.Width + S(12), lh2 = lsz.Height + S(6);
                    int lx2 = (int)dot.X + S(14); if (lx2 + lw2 > plot.Right) lx2 = (int)dot.X - lw2 - S(14);
                    int ly2 = (int)dot.Y - lh2 - S(10); if (ly2 < plot.Top) ly2 = (int)dot.Y + S(12);
                    using (var path2 = Theme.RoundRect(new RectangleF(lx2, ly2, lw2, lh2), Sf(5)))
                    {
                        using var bb2 = new SolidBrush(Color.FromArgb(Theme.Dark ? 215 : 240, Theme.Card)); g.FillPath(bb2, path2);
                        using var bp2 = new Pen(Color.FromArgb(160, cols[fan])); g.DrawPath(bp2, path2);
                    }
                    TextRenderer.DrawText(g, lbl, axisF, new Rectangle(lx2, ly2, lw2, lh2), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        // ---- airflow particles: speed = the REAL fan speed right now (a wind gauge) ----
        var air = PlayAirRect;
        int liveDuty = haveLive ? Math.Max(live.CpuFan, live.GpuFan) : 0;
        int lanes = 5;
        float speed = haveLive ? 0.3f + Math.Clamp(liveDuty, 0, MaxPct) / (float)MaxPct * 2.4f : 0f;
        for (int lane = 0; lane < lanes; lane++)
        {
            float y = air.Y + (lane + 0.5f) * air.Height / lanes;
            float phase = (_airPhase * speed + lane * 17) % 40;
            for (float x = air.X + phase; x < air.Right; x += 40)
            {
                int a = (int)(40 + 150 * (x - air.X) / air.Width);
                using var pb = new SolidBrush(Color.FromArgb(Math.Clamp(a, 0, 255), Theme.Accent));
                g.FillEllipse(pb, x, y - Sf(1.5f), Sf(3), Sf(3));
            }
        }
        string airLbl = haveLive ? string.Format(Lang.T("fc_play_air"), liveDuty) : Lang.T("fc_live_none");
        TextRenderer.DrawText(g, airLbl, axisF, new Rectangle(air.X - S(20), air.Bottom + S(4), air.Width + S(40), axisF.Height), Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        // ---- info row: now + last hour ----
        var ir = PlayInfoRect;
        Ui.FillCard(g, ir);
        string nowS = haveLive
            ? string.Format(Lang.T("fc_play_now"), live.CpuTemp, live.CpuFan, live.CpuRpm > 0 ? live.CpuRpm + " rpm" : "--", live.GpuTemp > 0 ? live.GpuTemp.ToString() : "--", live.GpuTemp > 0 ? live.GpuFan.ToString() : "--", live.GpuRpm > 0 ? live.GpuRpm + " rpm" : "--")
            : Lang.T("fc_live_none");
        TextRenderer.DrawText(g, nowS, bF, new Rectangle(ir.X + S(16), ir.Y + S(8), ir.Width - S(32), bF.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        string hourS;
        if (cnt >= 5)
        {
            var ct = hist.Where(s => s.CpuTemp > 0).Select(s => (int)s.CpuTemp).ToList();
            var cf = hist.Select(s => (int)s.CpuFan).ToList();
            hourS = string.Format(Lang.T("fc_play_hour"), cnt, ct.Count > 0 ? ct.Min() : 0, ct.Count > 0 ? ct.Max() : 0, cf.Min(), cf.Max());
        }
        else hourS = Lang.T("fc_play_hour_none");
        TextRenderer.DrawText(g, hourS, subF, new Rectangle(ir.X + S(16), ir.Y + S(10) + bF.Height, ir.Width - S(32), subF.Height), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        DrawSweepPanel(g, titleF, subF, bF, axisF);
    }

    // The sweep results table. Column widths are measured in PIXELS from the header and every
    // cell of that column, and each cell is then drawn right-aligned inside its own column
    // rectangle. Padding with spaces would have been enough for Latin headers only: a monospace
    // font renders a CJK glyph two cells wide, so "设定" counted as 2 characters and slid its
    // header off the numbers underneath it.
    private (string[] heads, List<string[]> rows, int[] colW, int width) BuildSweepTable(FanSweep.Result r, Font mono)
    {
        string unit = r.HasTach ? " rpm" : " %";
        var heads = new List<string> { Lang.T("fc_col_set"), (Single ? Lang.T("fc_fan_single") : Lang.T("fc_fan_cpu")) + unit };
        if (!Single) heads.Add(Lang.T("fc_fan_gpu") + unit);
        heads.Add(Lang.T("fc_col_react"));
        var rows = new List<string[]>();
        foreach (var st in r.Steps)
        {
            var cells = new List<string> { st.DutyPct + "%", r.HasTach ? (st.CpuRpm > 0 ? st.CpuRpm.ToString() : "--") : st.CpuDuty.ToString() };
            if (!Single) cells.Add(r.HasTach ? (st.GpuRpm > 0 ? st.GpuRpm.ToString() : "--") : st.GpuDuty.ToString());
            cells.Add(st.SecondsToSettle < 0 ? "--" : st.SecondsToSettle.ToString("0.0") + " s");
            rows.Add(cells.ToArray());
        }
        var colW = new int[heads.Count];
        for (int i = 0; i < heads.Count; i++)
        {
            int w = TextRenderer.MeasureText(heads[i], mono, Size.Empty, TextFormatFlags.NoPadding).Width;
            foreach (var row in rows) w = Math.Max(w, TextRenderer.MeasureText(row[i], mono, Size.Empty, TextFormatFlags.NoPadding).Width);
            colW[i] = w;
        }
        return (heads.ToArray(), rows, colW, colW.Sum() + SweepColGap * (heads.Count - 1));
    }

    private int SweepColGap => S(18);

    private void DrawSweepRow(Graphics g, IReadOnlyList<string> cells, Font mono, int x, int y, int[] colW, Color color)
    {
        for (int i = 0; i < cells.Count && i < colW.Length; i++)
        {
            TextRenderer.DrawText(g, cells[i], mono, new Rectangle(x, y, colW[i], mono.Height), color,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
            x += colW[i] + SweepColGap;
        }
    }

    private void DrawSweepPanel(Graphics g, Font titleF, Font subF, Font bF, Font axisF)
    {
        int fans = Single ? 1 : 2;
        var cols = new[] { Theme.Accent, Theme.Violet };
        var pnl = SweepPanelRect;
        Ui.FillCard(g, pnl);
        TextRenderer.DrawText(g, Lang.T("fc_sweep_title"), titleF, new Rectangle(pnl.X + S(16), pnl.Y + S(12), pnl.Width - S(200), titleF.Height + S(4)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        if (!_sweepOpen)
        {
            _reactHelp.Visible = false;
            TextRenderer.DrawText(g, Lang.T("fc_sweep_short"), subF,
                new Rectangle(pnl.X + S(16), pnl.Y + S(16) + titleF.Height + S(6), Math.Max(S(200), _sweepHelp.Left - pnl.X - S(28)), subF.Height * 2),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            return;
        }
        int ty = pnl.Y + S(14) + titleF.Height + S(4);
        TextRenderer.DrawText(g, Lang.T(_dev is { CpuRpmAddr: 0, GpuRpmAddr: 0 } ? "fc_sweep_note_notach" : "fc_sweep_note"), subF,
            new Rectangle(pnl.X + S(16), ty + S(4), Math.Max(S(240), _sweepHelp.Left - pnl.X - S(28)), subF.Height * 3),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        int top = ty + subF.Height * 3 + S(8);
        int bottom = pnl.Bottom - S(60);
        if (_sweepCts != null)
        {
            _reactHelp.Visible = false;
            var pb = new Rectangle(pnl.X + S(20), top + S(6), pnl.Width - S(40), S(10));
            using (var tb = new SolidBrush(Theme.Surface)) g.FillRectangle(tb, pb);
            float frac = _sweepCount == 0 ? 0 : (_sweepStep + 0.5f) / _sweepCount;
            using (var fb = new SolidBrush(Theme.AccentFill)) g.FillRectangle(fb, pb.X, pb.Y, (int)(pb.Width * frac), pb.Height);
            TextRenderer.DrawText(g, string.Format(Lang.T("fc_sweep_step"), _sweepStep + 1, _sweepCount, _sweepStatus), bF, new Rectangle(pnl.X + S(20), pb.Bottom + S(6), pnl.Width - S(40), bF.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
        else if (_sweepResult is { Steps.Count: > 0 } r)
        {
            using var mono = new Font("Consolas", 9.5f);
            int y = top, tx = pnl.X + S(20);
            var tbl = BuildSweepTable(r, mono);
            DrawSweepRow(g, tbl.heads, mono, tx, y, tbl.colW, Theme.Muted);
            _reactHelp.Size = new Size(S(20), S(20));
            Place(_reactHelp, true, tx + tbl.width + S(8), y + (mono.Height - _reactHelp.Height) / 2, _reactHelp.Width, _reactHelp.Height);
            y += mono.Height + S(2);
            foreach (var cells in tbl.rows)
            {
                if (y + mono.Height > bottom) break;
                DrawSweepRow(g, cells, mono, tx, y, tbl.colW, Theme.Text);
                y += mono.Height + S(1);
            }
            // Three blocks - table, findings, chart. Side by side while there is room; under a
            // threshold they stack instead of overlapping (and the panel grows, see SweepPanelH).
            bool wide = pnl.Width >= S(1000);
            // The findings column starts after the table's MEASURED width, not at a guessed
            // third of the panel (they used to overlap).
            int tableW = tbl.width + S(40);
            int tableH = mono.Height * (r.Steps.Count + 2);
            int frX = wide ? pnl.X + S(20) + tableW : pnl.X + S(20);
            int frW = wide ? Math.Max(S(220), pnl.Width - tableW - S(40) - (pnl.Width / 3)) : pnl.Width - S(40);
            var fr = wide
                ? new Rectangle(frX, top, frW, bottom - top)
                : new Rectangle(frX, top + tableH + S(16), frW, Math.Max(S(60), bottom - top - tableH - S(16)));
            int fy = fr.Y;
            TextRenderer.DrawText(g, Lang.T("fc_find_title"), bF, new Rectangle(fr.X, fy, fr.Width, bF.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            fy += bF.Height + S(4);
            var findingKeys = FanSweep.Findings(r).Select(f => f.key).ToList();
            var findingTexts = FindingLines(r).ToList();
            for (int fi2 = 0; fi2 < findingTexts.Count; fi2++)
            {
                string key = findingKeys[fi2];
                string line = "•  " + findingTexts[fi2];
                var need = TextRenderer.MeasureText(g, line, subF, new Size(fr.Width, 1000), TextFormatFlags.WordBreak);
                if (fy + need.Height > bottom) break;
                var col = key.Contains("drop") || key.Contains("gap") ? Theme.Amber : Theme.Muted;
                TextRenderer.DrawText(g, line, subF, new Rectangle(fr.X, fy, fr.Width, need.Height), col, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
                fy += need.Height + S(3);
            }
            // mini chart: right third when wide, under the findings when narrow
            var mc = wide
                ? new Rectangle(fr.Right + S(30), top + S(4), Math.Max(S(160), pnl.Right - fr.Right - S(60)), Math.Max(30, bottom - top - S(16)))
                : new Rectangle(pnl.X + S(40), fy + S(22), pnl.Width - S(80), Math.Max(0, bottom - fy - S(40)));
            if (mc.Height >= 30)
            {
                using var axp = new Pen(Theme.Border);
                g.DrawLine(axp, mc.Left, mc.Bottom, mc.Right, mc.Bottom); g.DrawLine(axp, mc.Left, mc.Top, mc.Left, mc.Bottom);
                int maxV = r.HasTach ? Math.Max(1000, r.Steps.Max(x => Math.Max(x.CpuRpm, x.GpuRpm))) : 100;
                for (int f = 0; f < fans; f++)
                {
                    var pts = r.Steps.Select(x => new PointF(mc.Left + x.DutyPct / (float)MaxPct * mc.Width,
                        mc.Bottom - (r.HasTach ? (f == 0 ? x.CpuRpm : x.GpuRpm) : (f == 0 ? x.CpuDuty : x.GpuDuty)) / (float)maxV * mc.Height)).ToArray();
                    if (pts.Length < 2) continue;
                    using var lp = new Pen(cols[f], Sf(2f)) { LineJoin = LineJoin.Round };
                    g.DrawLines(lp, pts);
                    foreach (var p in pts) { using var db = new SolidBrush(cols[f]); g.FillEllipse(db, p.X - Sf(3), p.Y - Sf(3), Sf(6), Sf(6)); }
                }
                int lgx = mc.Left + S(4), lgy = mc.Top;
                for (int f = 0; f < fans; f++)
                {
                    using var lb = new SolidBrush(cols[f]);
                    g.FillRectangle(lb, lgx, lgy + S(5), S(12), S(3));
                    string nm = Lang.T(f == 0 ? (Single ? "fc_fan_single" : "fc_fan_cpu") : "fc_fan_gpu") + (r.HasTach ? " (rpm)" : " (%)");
                    TextRenderer.DrawText(g, nm, axisF, new Rectangle(lgx + S(16), lgy, S(200), axisF.Height), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
                    lgy += axisF.Height + S(2);
                }
                TextRenderer.DrawText(g, "set %", axisF, new Rectangle(mc.Right - S(50), mc.Bottom + S(2), S(50), axisF.Height), Theme.Faint, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            }
            int stX = _sweepCopy.Right + S(16);
            int stW = Math.Max(S(40), (_sweepHistory.Visible ? _sweepHistory.Left - S(12) : pnl.Right - S(16)) - stX);
            TextRenderer.DrawText(g, _sweepStatus, subF, new Rectangle(stX, pnl.Bottom - S(50), stW, S(34)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        else
        {
            _reactHelp.Visible = false;
            TextRenderer.DrawText(g, _sweepStatus.Length > 0 ? _sweepStatus : Lang.T("fc_sweep_idle"), subF, new Rectangle(pnl.X + S(20), top, pnl.Width - S(40), subF.Height * 2), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        }
    }

    // ---- geometry ----
    // The content area every view lays itself out in: below the preset bar (or below the
    // intent tiles when shown), above the assignment row (or above the table when expanded).
    private int IntentH => S(58);
    private int TableH => S(190);
    private int ContentTopNow => ContentTop + (ShowIntents ? IntentH + S(12) : 0);
    // On the Playground the assignment row and the on/off bar are hidden, so the content
    // reaches the page bottom.
    private int ContentBottomNow => _view == ViewPlay
        ? ContentTopNow + Math.Max(Height - S(20) - ContentTopNow, PlayNeededH)
        : Height - 124 - (ShowTable ? TableH + S(12) : 0);
    private Rectangle ContentRect => new(Pad, ContentTopNow, Width - Pad * 2, ContentBottomNow - ContentTopNow);
    private Rectangle IntentBar => new(Pad, ContentTop, Width - Pad * 2, IntentH);
    private Rectangle TableRect => new(Pad, Height - 124 - TableH, Width - Pad * 2, TableH);

    // ---- scrolling (In-action view only, see the note next to _scrollY) ----
    // The window the content is seen through: everything below the sub-tab strip. Painting is
    // clipped to it, so the title and the strip stay untouched however far the view is scrolled.
    private Rectangle Viewport => new(0, ContentTop - S(8), Width, Math.Max(0, Height - ContentTop + S(8)));
    private int ScrollMax => _view != ViewPlay || _fc == null ? 0 : Math.Max(0, ContentBottomNow + S(16) - Height);
    private Rectangle ScrollTrack { get { var vp = Viewport; return new Rectangle(Width - S(11), vp.Top + S(4), S(7), Math.Max(0, vp.Height - S(12))); } }
    private Rectangle ScrollThumb
    {
        get
        {
            int max = ScrollMax; var t = ScrollTrack;
            if (max <= 0 || t.Height <= 0) return Rectangle.Empty;
            int viewH = Math.Max(1, Viewport.Height), contentH = viewH + max;
            int h = Math.Max(S(36), (int)(t.Height * (viewH / (float)contentH)));
            h = Math.Min(h, t.Height);
            int y = t.Y + (int)((t.Height - h) * (_scrollY / (float)max));
            return new Rectangle(t.X, y, t.Width, h);
        }
    }

    private void ScrollTo(int y)
    {
        y = Math.Clamp(y, 0, ScrollMax);
        if (y == _scrollY) return;
        _scrollY = y;
        HelpPopup.CloseAny();   // a bubble is a top-level popup; it would hang over the moved content
        LayoutButtons();
        Invalidate();
    }

    /// <summary>
    /// Positions a control of the scrolled view (its rectangle already carries the scroll) and
    /// hides it once it leaves the viewport at the top: a child control paints over its parent,
    /// so one that scrolled into the header band would sit on top of the title.
    /// </summary>
    private void Place(Control c, bool show, int x, int y, int w, int h)
    {
        c.SetBounds(x, y, w, h);
        var vp = Viewport;
        c.Visible = show && y >= vp.Top - S(2) && y < vp.Bottom;
    }

    private Rectangle GraphRect(int fan)
    {
        int top = ContentTopNow, bottom = ContentBottomNow, gap = 40;
        if (Single) return new Rectangle(Pad, top, Width - Pad * 2, bottom - top);
        int gw = (Width - Pad * 2 - gap) / 2;
        int x = Pad + fan * (gw + gap);
        return new Rectangle(x, top, gw, bottom - top);
    }

    private Rectangle PlotRect(int fan)
    {
        var r = GraphRect(fan);
        const int titleH = 48, axisH = 46, leftAxis = 54, rightPad = 16;
        return new Rectangle(r.X + leftAxis, r.Y + titleH, r.Width - leftAxis - rightPad, r.Height - titleH - axisH);
    }

    private PointF PointAt(int fan, int i)
    {
        var p = PlotRect(fan);
        int[] s = fan == 0 ? _cpuS : _gpuS;
        int n = s.Length;
        float x = p.Left + (n <= 1 ? 0 : i * p.Width / (float)(n - 1));
        float y = p.Bottom - s[i] / (float)MaxPct * p.Height;
        return new PointF(x, y);
    }

    // ---- interaction ----
    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (_fc == null) return;
        // scrollbar first: it sits in the page margin, outside every view's own hit areas
        if (ScrollMax > 0)
        {
            var thumb = ScrollThumb;
            if (thumb.Contains(e.Location)) { _scrollDrag = true; _scrollGrabDy = e.Y - thumb.Y; Invalidate(); return; }
            if (ScrollTrack.Contains(e.Location))
            { ScrollTo(_scrollY + (e.Y < thumb.Y ? -Viewport.Height : Viewport.Height)); return; }
        }
        if (_view == ViewEq)
        {
            if (!Editable) return;
            for (int f = 0; f < _eqTracks.GetLength(0); f++)
                for (int i = 0; i < _eqTracks.GetLength(1); i++)
                    if (_eqTracks[f, i].Inflate2(6, 12).Contains(e.Location))
                    { _dragFan = f; _dragIdx = i; SetEqValueFromY(f, i, e.Y); return; }
            return;
        }
        if (_view == ViewPlay)
        {
            var cpt = e.Location;
            if (!Viewport.Contains(cpt)) return;
            foreach (var kv in _dotHits)
                if (kv.Value.Contains(cpt))
                {
                    if (!_pinnedDots.Add(kv.Key)) _pinnedDots.Remove(kv.Key);
                    Invalidate();
                    return;
                }
            return;
        }
        if (_view == ViewDeck)
        {
            if (!Editable) return;
            for (int f = 0; f < _dials.GetLength(0); f++)
                for (int i = 0; i < _dials.GetLength(1); i++)
                    if (_dials[f, i].Contains(e.Location))
                    { _dragFan = f; _dragIdx = i; SetDialFromDrag(f, i, e.Location); return; }
            return;
        }
        if (_view != ViewChart) return;
        CloseCellEditor(commit: true);

        // comparison chips work even read-only (they only paint)
        for (int i = 0; i < _layerChips.Length; i++)
            if (_layerChips[i].Contains(e.Location)) { ToggleLayer(_layerNames[i]); return; }

        if (!Editable) return;

        if (ShowIntents)
            for (int i = 0; i < _intentRects.Length; i++)
                if (_intentRects[i].Contains(e.Location)) { ApplyIntent(i); return; }

        if (ShowTable)
            for (int r = 0; r < _tablePctCells.GetLength(0); r++)
                for (int f = 0; f < _tablePctCells.GetLength(1); f++)
                    if (_tablePctCells[r, f].Contains(e.Location)) { OpenCellEditor(r, f); return; }

        for (int fan = 0; fan < (Single ? 1 : 2); fan++)
        {
            if (!GraphRect(fan).Contains(e.Location)) continue;
            int[] s = fan == 0 ? _cpuS : _gpuS;
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < s.Length; i++)
            {
                var pt = PointAt(fan, i);
                double dx = pt.X - e.X, dy = pt.Y - e.Y, dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < bd) { bd = dist; best = i; }
            }
            if (best >= 0 && bd < 26) { _dragFan = fan; _dragIdx = best; SetSpeed(e.Y); }
            return;
        }
    }

    private void OnMove(object? sender, MouseEventArgs e)
    {
        if (_scrollDrag)
        {
            var track = ScrollTrack;
            int h = ScrollThumb.Height, span = Math.Max(1, track.Height - h);
            ScrollTo((int)Math.Round((e.Y - _scrollGrabDy - track.Y) / (float)span * ScrollMax));
            return;
        }
        if (_dragIdx >= 0)
        {
            if (_view == ViewEq) SetEqValueFromY(_dragFan, _dragIdx, e.Y);
            else if (_view == ViewDeck) SetDialFromDrag(_dragFan, _dragIdx, e.Location);
            else SetSpeed(e.Y);
            return;
        }
        if (_view == ViewPlay)
        {
            (int, int, int)? hit = null;
            var cpt = e.Location;
            // a dot scrolled under the header band is painted over: it must not answer either
            if (Viewport.Contains(cpt))
                foreach (var kv in _dotHits) if (kv.Value.Contains(cpt)) hit = kv.Key;
            if (!Equals(hit, _hoverDot)) { _hoverDot = hit; Cursor = hit != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            return;
        }
        if (_view == ViewDeck)
        {
            int hf = -1, hi = -1;
            for (int f = 0; f < _dials.GetLength(0); f++)
                for (int i = 0; i < _dials.GetLength(1); i++)
                    if (_dials[f, i].Contains(e.Location)) { hf = f; hi = i; }
            if (hf != _dialHoverFan || hi != _dialHoverIdx)
            {
                _dialHoverFan = hf; _dialHoverIdx = hi;
                Cursor = hf >= 0 && Editable ? Cursors.SizeNS : Cursors.Default;
                Invalidate();
            }
            return;
        }
        if (_view == ViewEq)
        {
            int hf = -1, hi = -1;
            for (int f = 0; f < _eqTracks.GetLength(0); f++)
                for (int i = 0; i < _eqTracks.GetLength(1); i++)
                    if (_eqTracks[f, i].Inflate2(6, 12).Contains(e.Location)) { hf = f; hi = i; }
            if (hf != _eqHoverFan || hi != _eqHoverIdx)
            {
                _eqHoverFan = hf; _eqHoverIdx = hi;
                Cursor = hf >= 0 && Editable ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            return;
        }
        if (_view != ViewChart) return;
        // hover states for the painted widgets: chips, intent tiles, table rows
        int chip = -1, tile = -1, row = -1;
        for (int i = 0; i < _layerChips.Length; i++) if (_layerChips[i].Contains(e.Location)) chip = i;
        if (ShowIntents) for (int i = 0; i < _intentRects.Length; i++) if (_intentRects[i].Contains(e.Location)) tile = i;
        if (ShowTable) for (int i = 0; i < _tableRows.Length; i++) if (_tableRows[i].Contains(e.Location)) row = i;
        if (chip != _hoverChip || tile != _hoverIntent || row != _hoverRow)
        {
            _hoverChip = chip; _hoverIntent = tile; _hoverRow = row;
            Cursor = chip >= 0 || (tile >= 0 && Editable) ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverChip >= 0 || _hoverIntent >= 0 || _hoverRow >= 0 || _eqHoverFan >= 0)
        { _hoverChip = _hoverIntent = _hoverRow = _eqHoverFan = _eqHoverIdx = -1; Cursor = Cursors.Default; Invalidate(); }
    }

    // Wheel: scrolls the In-action view when it is longer than the window, otherwise nudges the
    // equalizer fader or deck dial under the pointer by one point (fine-tuning without pixel
    // hunting).
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ScrollMax > 0) { ScrollTo(_scrollY - Math.Sign(e.Delta) * S(64)); return; }
        if (!Editable) return;
        int fan, i;
        if (_view == ViewEq && _eqHoverFan >= 0) { fan = _eqHoverFan; i = _eqHoverIdx; }
        else if (_view == ViewDeck && _dialHoverFan >= 0) { fan = _dialHoverFan; i = _dialHoverIdx; }
        else return;
        int[] s = fan == 0 ? _cpuS : _gpuS;
        int lo = i > 0 ? s[i - 1] : 0, hi = i < s.Length - 1 ? s[i + 1] : MaxPct;
        int nv = Math.Clamp(Math.Clamp(s[i] + Math.Sign(e.Delta), 0, MaxPct), lo, hi);
        if (nv != s[i]) { s[i] = nv; if (_enable.Checked) ReApply(); Invalidate(); }
    }

    private void SetSpeed(int mouseY)
    {
        var p = PlotRect(_dragFan);
        int[] s = _dragFan == 0 ? _cpuS : _gpuS;
        int sp = (int)Math.Round((p.Bottom - mouseY) / (float)p.Height * MaxPct);
        int lo = _dragIdx > 0 ? s[_dragIdx - 1] : 0;
        int hi = _dragIdx < s.Length - 1 ? s[_dragIdx + 1] : MaxPct;
        s[_dragIdx] = Math.Clamp(Math.Clamp(sp, 0, MaxPct), lo, hi);
        Invalidate();
    }

    private void Apply()
    {
        if (_fc is not { } fc) return;
        int peak = Single ? _cpuS[^1] : Math.Max(_cpuS[^1], _gpuS[^1]);
        if (peak < 40 &&
            MessageBox.Show(FindForm(), Lang.T("fc_warn_low"), Lang.T("fc_title"),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            _enable.Checked = false;   // user backed out (programmatic set doesn't re-fire Toggled)
            return;
        }

        // In Silent the power policy lives in the SAME byte as the fan curve (0xD4): 1D = Silent,
        // 8D = curve. So a curve in Silent necessarily drops the Silent power cap -> the machine
        // becomes Balanced + custom fans. Warn once and switch the profile to Balanced explicitly.
        bool fromSilent = D.Current() == ProfileId.Silent;
        if (fromSilent &&
            MessageBox.Show(FindForm(), Lang.T("fc_silent_warn"), Lang.T("fc_title"),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            _enable.Checked = false;
            return;
        }

        if (fromSilent) D.SetProfile(ProfileId.Balanced);        // leave Silent (power cap shares the fan byte)
        D.WithEcWrite(dev =>
        {
            Ec.WriteFanCurve(dev, _cpuT, _cpuS, _gpuT, _gpuS);    // our curve tables
            Ec.SetFanMode(dev, fc.AdvancedModeValue);             // advanced fan (0x8D)
        });
        if (D.Writable()) D.Settings.RecordActiveCurve(null, _cpuT, _cpuS, _gpuT, _gpuS);   // (#49) manual curve
        RefreshMode();
        if (D.Writable()) ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_curve_on"), $"{_dev!.FanMode:X2}={fc.AdvancedModeValue:X2}");
    }

    // ---- paint ----
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_fc == null)
        {
            DrawPageHeader(g);
            TextRenderer.DrawText(g, Lang.T("test_curve_none"), new Font("Segoe UI", 11f),
                new Rectangle(Pad, 72, Width - Pad * 2, 40), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordEllipsis);
            return;
        }

        // The content goes down FIRST and the header on top of it, because the In-action view
        // scrolls its content up past the header band and nothing here can be clipped away
        // (TextRenderer ignores the Graphics clip). The sub-tab strip is a child control, so it
        // paints after the page either way.
        switch (_view)
        {
            case ViewChart:
                if (CompareRowOn) DrawLayerChips(g);
                else { _layerChips = Array.Empty<Rectangle>(); _layerNames = Array.Empty<string>(); }
                if (ShowIntents) DrawIntentTiles(g);
                DrawFan(g, 0, Lang.T(Single ? "fc_fan_single" : "fc_fan_cpu"), _cpuT, _cpuS);
                if (!Single) DrawFan(g, 1, Lang.T("fc_fan_gpu"), _gpuT, _gpuS);
                if (ShowTable) DrawTable(g);
                break;
            case ViewEq:
                DrawEqualizer(g);
                break;
            case ViewDeck:
                DrawDeck(g);
                break;
            case ViewPlay:
                DrawPlayground(g);
                break;
        }
        DrawPageHeader(g);
        DrawScrollBar(g);
    }

    // Title, live fan-mode indicator and the per-view hint line. Drawn last (see OnPaint): on
    // the scrolling view it repaints the band the content may have run into.
    private void DrawPageHeader(Graphics g)
    {
        if (_view == ViewPlay && _scrollY > 0)
        {
            var band = new Rectangle(0, 0, Width, Viewport.Top);
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, band);
            if (D.Settings.ShowGrid)
            {
                // Repaint the brand grid the fill just covered, from the page's own origin so
                // the lines stay aligned with the rest of the background (clip = GDI+ drawing,
                // which honours it).
                using var saved = g.Clip;
                g.SetClip(band);
                Ui.DrawGrid(g, new Rectangle(0, 0, Width, Height));
                g.Clip = saved;
            }
        }
        TextRenderer.DrawText(g, Lang.T("fc_title"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 22), Theme.Text);
        if (_fc == null) return;

        // live fan-mode indicator (feedback for Apply / Restore automatic). Values come from
        // the model's spec, not literals: a board with a different Advanced value must not
        // read as "0x8D".
        string modeName = _fanMode == _fc.AdvancedModeValue ? "Advanced"
                        : _dev != null && _fanMode == _dev.FanSilentValue ? "Silent"
                        : _fanMode == 0x0D ? "Auto"
                        : $"0x{_fanMode:X2}";
        using var modeFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        TextRenderer.DrawText(g, Lang.T("fc_mode") + " " + modeName, modeFont,
            new Rectangle(Width - Pad - 360, 24, 360, 28), Theme.Accent,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        // one hint line per view: the drag instruction only makes sense on the chart
        string hint = _view switch
        {
            ViewEq => Lang.T("fc_hint_eq"),
            ViewDeck => Lang.T("fc_hint_deck"),
            ViewPlay => Lang.T("fc_hint_play"),
            _ => !D.Writable() ? Lang.T("fc_locked")
               : _fc is { Verified: false } ? Lang.T("fc_preview")   // editable, but addresses unconfirmed
               : Lang.T("fc_hint"),
        };
        if (Single && _view == ViewChart) hint = Lang.T("fc_single_note") + "  ·  " + hint;
        using var hintFont = new Font("Segoe UI", 10.5f);
        // Wrap: on a narrow window the one-liner used to run off the right edge instead of
        // breaking. Two lines fit under the title before the sub-tab strip starts.
        TextRenderer.DrawText(g, hint, hintFont,
            new Rectangle(Pad, 68, Width - Pad * 2 - S(120), hintFont.Height * 2 + S(2)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    // Slim scrollbar in the right margin, drawn only while the view actually scrolls. Painted
    // rather than a real ScrollBar control: a WinForms scrollbar cannot be themed and would
    // also drag the whole page's child controls around (which is the bug this replaced).
    private void DrawScrollBar(Graphics g)
    {
        var thumb = ScrollThumb;
        if (thumb.IsEmpty) return;
        var track = ScrollTrack;
        using (var tb = new SolidBrush(Color.FromArgb(Theme.Dark ? 40 : 26, Theme.Text)))
        using (var tp = Theme.RoundRect(track, track.Width / 2))
            g.FillPath(tb, tp);
        using (var hb = new SolidBrush(Color.FromArgb(_scrollDrag ? 220 : 150, Theme.Muted)))
        using (var hp = Theme.RoundRect(thumb, thumb.Width / 2))
            g.FillPath(hb, hp);
    }

    private void DrawFan(Graphics g, int fan, string title, int[] temps, int[] speeds)
    {
        var card = GraphRect(fan);
        Ui.FillCard(g, card);
        var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        TextRenderer.DrawText(g, title, titleFont,
            new Rectangle(card.X + 16, card.Y + 10, card.Width - 32, titleFont.Height + 4), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top);

        var p = PlotRect(fan);

        // (07) audibility bands behind everything else: quiet / audible / loud, fixed thresholds,
        // faint tints with a small label at the right edge. Indicative - see the help text.
        if (D.Settings.FanCurveZones)
        {
            using var zf = new Font("Segoe UI", 8f);
            (int lo, int hi, Color c, string key)[] bands =
            {
                (0, CurveModel.QuietMax, Theme.Green, "fc_zone_quiet"),
                (CurveModel.QuietMax, CurveModel.LoudMin, Theme.Amber, "fc_zone_audible"),
                (CurveModel.LoudMin, MaxPct, Theme.Red, "fc_zone_loud"),
            };
            foreach (var (lo, hi, c, key) in bands)
            {
                float y1 = p.Bottom - hi / (float)MaxPct * p.Height, y2 = p.Bottom - lo / (float)MaxPct * p.Height;
                using var br = new SolidBrush(Color.FromArgb(Theme.Dark ? 16 : 22, c));
                g.FillRectangle(br, p.Left, y1, p.Width, y2 - y1);
                TextRenderer.DrawText(g, Lang.T(key), zf, new Rectangle(p.Right - 90, (int)y1 + 3, 88, 14),
                    Color.FromArgb(150, c), TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }
        }

        using (var grid = new Pen(Theme.Border))
        using (var axisFont = new Font("Segoe UI", 8.5f))
        {
            for (int v = 0; v <= MaxPct; v += 25)
            {
                float y = p.Bottom - v / (float)MaxPct * p.Height;
                g.DrawLine(grid, p.Left, y, p.Right, y);
                TextRenderer.DrawText(g, v + "%", axisFont, new Rectangle(card.X + 8, (int)y - 9, 44, 18), Theme.Faint,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        int n = speeds.Length;
        var pts = new PointF[n];
        for (int i = 0; i < n; i++) pts[i] = PointAt(fan, i);

        // (04) comparison layers: dashed polylines, no nodes, one colour per layer
        foreach (var (_, lc, lgs, color) in _layers)
        {
            int[] ls = fan == 0 ? lc : lgs;
            if (ls.Length != n) continue;
            var lp = new PointF[n];
            for (int i = 0; i < n; i++)
                lp[i] = new PointF(p.Left + (n <= 1 ? 0 : i * p.Width / (float)(n - 1)), p.Bottom - ls[i] / (float)MaxPct * p.Height);
            using var lpen = new Pen(Color.FromArgb(200, color), 2f) { DashStyle = DashStyle.Dash, LineJoin = LineJoin.Round };
            g.DrawLines(lpen, lp);
        }

        if (n >= 2)
        {
            // translucent gradient wash under the curve (fades toward the axis, like the site mockup)
            using (var area = new GraphicsPath())
            {
                area.AddLines(pts);
                area.AddLine(pts[n - 1], new PointF(pts[n - 1].X, p.Bottom));
                area.AddLine(new PointF(pts[n - 1].X, p.Bottom), new PointF(pts[0].X, p.Bottom));
                area.CloseFigure();
                var box = new RectangleF(p.Left, p.Top, p.Width, p.Height + 1);
                using var grad = new LinearGradientBrush(box,
                    Color.FromArgb(70, Theme.Accent), Color.FromArgb(8, Theme.Accent), 90f);
                g.FillPath(grad, area);
            }
            // vertical guide from each node down to the axis
            using (var guide = new Pen(Color.FromArgb(60, Theme.Accent)) { DashStyle = DashStyle.Dash })
                foreach (var pt in pts)
                    g.DrawLine(guide, pt.X, pt.Y, pt.X, p.Bottom);
        }

        using (var line = new Pen(Theme.Accent, 2.5f) { LineJoin = LineJoin.Round })
            if (n >= 2) g.DrawLines(line, pts);

        using var tempFont = new Font("Segoe UI", 8.5f);
        using var valFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        for (int i = 0; i < n; i++)
        {
            // temperature label on the X axis
            TextRenderer.DrawText(g, temps[i] + "°", tempFont,
                new Rectangle((int)pts[i].X - 24, p.Bottom + 8, 48, tempFont.Height + 2), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            // speed % above the point
            int vh = valFont.Height + 2;
            TextRenderer.DrawText(g, speeds[i] + "%", valFont,
                new Rectangle((int)pts[i].X - 28, (int)pts[i].Y - vh - 10, 56, vh), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            // node (bigger while dragged; amber halo when its table row is hovered)
            bool active = fan == _dragFan && i == _dragIdx;
            bool rowHi = ShowTable && i == _hoverRow;
            float r = active ? 9f : 7f;
            if (rowHi)
            {
                using var halo = new Pen(Color.FromArgb(180, Theme.Amber), 3f);
                g.DrawEllipse(halo, pts[i].X - r - 4, pts[i].Y - r - 4, (r + 4) * 2, (r + 4) * 2);
            }
            using var fill = new SolidBrush(Theme.Accent);
            using var ring = new Pen(Theme.Card, 2.5f);
            g.FillEllipse(fill, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
            g.DrawEllipse(ring, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
        }

        DrawOperatingPoint(g, fan, p, temps);
    }

    // (02) Live operating point + trail. Temperature -> ordinal x through the node
    // temperatures (the axis is index-spaced, so a linear temp scale would put the dot in
    // the wrong place); y = the EC's reported duty. The dotted trail is the last three
    // minutes from the feed's ring, fading toward the past. Below the dot a small delta
    // says how far the fan is from what the curve asks at this temperature (the firmware
    // may still be ramping) - the "curve in action" idea in miniature.
    private void DrawOperatingPoint(Graphics g, int fan, Rectangle p, int[] temps)
    {
        var s = _live.Sample;
        if (s.Time == DateTime.MinValue) return;
        int temp = fan == 0 ? s.CpuTemp : s.GpuTemp;
        int duty = fan == 0 ? s.CpuFan : s.GpuFan;
        int rpm = fan == 0 ? s.CpuRpm : s.GpuRpm;
        int[] speeds = fan == 0 ? _cpuS : _gpuS;
        int n = temps.Length;
        if (n < 2) return;
        var color = Theme.Profile(D.ColorOf(D.Current()));

        // The dot rides ON the curve: x = temperature mapped onto the node axis, y = what the
        // curve asks at that temperature. The EC's actual duty (which can exceed 100 % and would
        // otherwise throw the dot off the plot) and the rpm live in the label only.
        PointF OnCurve(int t) =>
            new(p.Left + CurveModel.OrdinalX(temps, t) * p.Width / (n - 1),
                p.Bottom - Math.Clamp(CurveModel.SpeedAt(temps, speeds, t), 0, MaxPct) / (float)MaxPct * p.Height);

        // optional trail (opt-in): where the temperature has been over the last three minutes,
        // as small dots along the curve, older = fainter, in the amber "history" colour so it
        // never reads as part of the curve or the live dot
        if (D.Settings.FanCurveTrail)
        {
            var trail = _live.Trail();
            int cnt = trail.Count;
            float rr = Sf(2.5f);
            for (int i = 0; i < cnt - 1; i++)
            {
                var q = trail[i];
                int qt = fan == 0 ? q.CpuTemp : q.GpuTemp;
                if (qt <= 0) continue;
                int alpha = 30 + (int)(150f * i / Math.Max(1, cnt - 1));
                var pt = OnCurve(qt);
                using var tb = new SolidBrush(Color.FromArgb(alpha, Theme.Amber));
                g.FillEllipse(tb, pt.X - rr, pt.Y - rr, rr * 2, rr * 2);
            }
        }

        using var lf = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var df = new Font("Segoe UI", 8.5f);
        if (temp <= 0)
        {
            // sleeping dGPU (or no reading): say so instead of drawing a dot at 0 °C
            TextRenderer.DrawText(g, Lang.T("fc_live_none"), df, new Rectangle(p.Left, p.Top + S(2), p.Width, df.Height),
                Theme.Faint, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            return;
        }

        var dot = OnCurve(temp);
        float R = Sf(6.5f), ring = Sf(11f), core = Sf(2.5f);
        using (var ringPen = new Pen(Color.FromArgb(110, color), Sf(2f)))
            g.DrawEllipse(ringPen, dot.X - ring, dot.Y - ring, ring * 2, ring * 2);
        using (var db = new SolidBrush(color)) g.FillEllipse(db, dot.X - R, dot.Y - R, R * 2, R * 2);
        using (var cb = new SolidBrush(Color.White)) g.FillEllipse(cb, dot.X - core, dot.Y - core, core * 2, core * 2);

        // ONE label line: "72°C · fan 61%" (+ rpm) (+ "curve 58%" when the fan differs from it)
        int expected = (int)Math.Round(CurveModel.SpeedAt(temps, speeds, temp));
        string label = $"{temp}°C  ·  {Lang.T("fc_live_fan")} {duty}%" + (rpm > 0 ? $"  ·  {rpm} rpm" : "");
        if (duty != expected) label += $"  ·  {Lang.T("fc_live_curve")} {expected}%";
        var sz = TextRenderer.MeasureText(g, label, lf, Size.Empty, TextFormatFlags.NoPadding);
        int lw = sz.Width + S(14), lh = sz.Height + S(6);
        int lx = (int)dot.X + S(14);
        if (lx + lw > p.Right) lx = (int)dot.X - lw - S(14);
        if (lx < p.Left) lx = p.Left;
        int ly = (int)dot.Y - lh - S(10);
        if (ly < p.Top) ly = (int)dot.Y + S(12);
        var box = new Rectangle(lx, ly, lw, lh);
        using (var path = Theme.RoundRect(new RectangleF(box.X, box.Y, box.Width, box.Height), Sf(5)))
        {
            using var bb = new SolidBrush(Color.FromArgb(Theme.Dark ? 215 : 240, Theme.Card));
            g.FillPath(bb, path);
            using var bp = new Pen(Color.FromArgb(160, color));
            g.DrawPath(bp, path);
        }
        TextRenderer.DrawText(g, label, lf, box, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // The Compare checkbox reuses the chip-row caption; the caption carries a trailing colon
    // (fullwidth in CJK, preceded by a space in French), which a checkbox label must not.
    private static string CompareCaption() => Lang.T("fc_compare").TrimEnd(':', '：', ' ', ' ');

    // (04) comparison chips on their own row, between the preset bar and the tiles. They used
    // to share the preset row, where the overlap guard silently swallowed them on narrow
    // windows; a full-width row of their own fits every chip, and the Compare checkbox turns
    // the whole row off. Filled chip = layer on (in its layer colour), outline = available.
    private void DrawLayerChips(Graphics g)
    {
        var chips = new List<Rectangle>();
        var kept = new List<string>();
        using var cf = new Font("Segoe UI", 9f, FontStyle.Bold);
        int h = cf.Height + S(8);
        int y = PresetY + S(42);
        string cap = Lang.T("fc_compare");
        using var capF = new Font("Segoe UI", 9.5f);
        int capW = TextRenderer.MeasureText(g, cap, capF).Width;
        TextRenderer.DrawText(g, cap, capF, new Rectangle(Pad, y, capW, h), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        int x = Pad + capW + S(10);
        int right = Width - Pad;
        foreach (var name in AllLayerNames())
        {
            string text = name.Length == 0 ? Lang.T("fc_layer_default") : name;
            int w = TextRenderer.MeasureText(g, text, cf).Width + S(18);
            if (x + w > right) break;   // narrower than the full set: the tail waits for a wider window
            var r = new Rectangle(x, y, w, h);
            x += w + S(6);
            int li = D.Settings.FanCurveCompare.IndexOf(name);
            bool on = li >= 0;
            var col = on ? LayerColors[Math.Min(li, LayerColors.Length - 1)] : Theme.Muted;
            using var path = Theme.RoundRect(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), h / 2f);
            if (on) { using var fb = new SolidBrush(Color.FromArgb(Theme.Dark ? 60 : 40, col)); g.FillPath(fb, path); }
            using var pen = new Pen(chips.Count == _hoverChip ? Theme.Text : col, on ? 1.6f : 1f);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, text, cf, r, on ? col : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            chips.Add(r);
            kept.Add(name);
        }
        // hit-testing sees only the chips that fit; RebuildLayers restores the full name list
        // on the next settings change, and every paint re-filters it here
        _layerChips = chips.ToArray();
        _layerNames = kept.ToArray();
    }

    // (08) intent tiles: four shapes derived from the factory default; the one matching the
    // current curve is lit. Clicking loads the shape into the editor (and re-applies when on).
    private void DrawIntentTiles(Graphics g)
    {
        var bar = IntentBar;
        int gap = S(12), w = (bar.Width - gap * (Intents.Length - 1)) / Intents.Length;
        var rects = new Rectangle[Intents.Length];
        int active = ActiveIntent();
        using var tf = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var sf = new Font("Segoe UI", 8.5f);
        int titleH = tf.Height, subH = sf.Height;
        int textBlock = titleH + S(2) + subH;
        for (int i = 0; i < Intents.Length; i++)
        {
            var r = new Rectangle(bar.X + i * (w + gap), bar.Y, w, bar.Height);
            rects[i] = r;
            bool on = i == active, hov = i == _hoverIntent && Editable;
            using var path = Theme.RoundRect(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), Sf(8));
            using (var fb = new SolidBrush(on ? Theme.AccentSoft : hov ? Theme.RowAlt : Theme.Card)) g.FillPath(fb, path);
            using (var pen = new Pen(on ? Theme.Accent : hov ? Theme.BorderStrong : Theme.Border, on ? 1.6f : 1f)) g.DrawPath(pen, path);
            // mini shape preview at the left: the CPU intent shape as a tiny polyline
            var shape = CurveModel.IntentShape(Intents[i], DefCpuS, _cpuT);
            var mini = new Rectangle(r.X + S(14), r.Y + S(12), S(44), r.Height - S(24));
            var mp = new PointF[shape.Length];
            for (int k = 0; k < shape.Length; k++)
                mp[k] = new PointF(mini.X + k * mini.Width / (float)(shape.Length - 1), mini.Bottom - shape[k] / 100f * mini.Height);
            using (var mpen = new Pen(on ? Theme.Accent : Theme.Muted, Sf(1.8f)) { LineJoin = LineJoin.Round }) g.DrawLines(mpen, mp);
            // text block vertically centred in the tile
            int ty = r.Y + Math.Max(S(6), (r.Height - textBlock) / 2);
            var tr = new Rectangle(mini.Right + S(12), ty, r.Right - mini.Right - S(24), titleH);
            TextRenderer.DrawText(g, Lang.T(IntentKey(Intents[i])), tf, tr, on ? Theme.Accent : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Lang.T(IntentKey(Intents[i]) + "_sub"), sf, new Rectangle(tr.X, tr.Y + titleH + S(2), tr.Width, subH),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        _intentRects = rects;
    }

    // (05, variant A) ONE table under both charts, full width: the left half sits under the CPU
    // chart (Temp | % | band | vs. MSI default), the right half under the GPU chart, one shared
    // row per point index. Hovering a row halos that node on both charts; a % cell opens the
    // inline editor. Every dimension follows DPI; column widths come from the fonts.
    private void DrawTable(Graphics g)
    {
        var t = TableRect;
        Ui.FillCard(g, t);
        using var hf = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var rf = new Font("Segoe UI", 10f);
        using var bf = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var tipF = new Font("Segoe UI", 8.5f);
        int fans = Single ? 1 : 2;
        int n = _cpuS.Length;
        int pad = S(16);
        int headY = t.Y + S(10);
        int headH = hf.Height + S(6);
        int rowsTop = headY + headH + S(6);
        int rowH = Math.Max(rf.Height + S(6), (t.Bottom - S(8) - rowsTop) / Math.Max(1, n));
        int cellW = TextRenderer.MeasureText(g, MaxPct + "%", bf).Width + S(20);
        int tempW = TextRenderer.MeasureText(g, "100°", rf).Width + S(14);
        int bandW = new[] { "fc_zone_quiet", "fc_zone_audible", "fc_zone_loud" }.Max(k => TextRenderer.MeasureText(g, Lang.T(k), rf).Width) + S(14);
        int idxW = TextRenderer.MeasureText(g, "6", bf).Width + S(12);

        var rows = new Rectangle[n];
        var cells = new Rectangle[n, fans];
        // header hint at the far right
        TextRenderer.DrawText(g, Lang.T(Editable ? "fc_table_hint" : "fc_locked"), tipF,
            new Rectangle(t.X + t.Width / 2, headY, t.Width / 2 - pad, headH), Theme.Faint,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        for (int fan = 0; fan < fans; fan++)
        {
            // this half's x range = the matching chart's x range, so columns line up under it
            var gr = GraphRect(fan);
            int x0 = gr.X + pad, x1 = gr.Right - pad;
            int[] temps = fan == 0 ? _cpuT : _gpuT, sp = fan == 0 ? _cpuS : _gpuS, def = fan == 0 ? DefCpuS : DefGpuS;
            // columns: [#] temp | % | band | vs default (the # only in the first half)
            int cx = x0;
            int xIdx = fan == 0 ? cx : -1; if (fan == 0) cx += idxW;
            int xTemp = cx; cx += tempW;
            int xPct = cx; cx += cellW + S(10);
            int xBand = cx; cx += bandW;
            int xDef = cx;
            // header
            if (fan == 0) TextRenderer.DrawText(g, "#", hf, new Rectangle(xIdx, headY, idxW, headH), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Lang.T("fc_col_temp"), hf, new Rectangle(xTemp, headY, tempW, headH), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "%", hf, new Rectangle(xPct, headY, cellW, headH), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Lang.T("fc_col_band"), hf, new Rectangle(xBand, headY, bandW, headH), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Lang.T("fc_col_vsdefault"), hf, new Rectangle(xDef, headY, Math.Max(40, x1 - xDef), headH), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, x0, rowsTop - S(3), x1, rowsTop - S(3));

            for (int r = 0; r < n; r++)
            {
                var row = new Rectangle(x0, rowsTop + r * rowH, x1 - x0, rowH);
                if (fan == 0) rows[r] = new Rectangle(t.X + S(8), row.Y, t.Width - S(16), rowH);
                if (r == _hoverRow) { using var hb = new SolidBrush(Theme.RowAlt); g.FillRectangle(hb, row); }
                var f0 = r == _hoverRow ? bf : rf;
                if (fan == 0)
                    TextRenderer.DrawText(g, (r + 1).ToString(), f0, new Rectangle(xIdx, row.Y, idxW, rowH), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, temps[r] + "°", f0, new Rectangle(xTemp, row.Y, tempW, rowH), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var cell = new Rectangle(xPct, row.Y + S(2), cellW, rowH - S(4));
                cells[r, fan] = cell;
                bool editing = r == _editRow && fan == _editFan;
                using (var path = Theme.RoundRect(new RectangleF(cell.X + .5f, cell.Y + .5f, cell.Width - 1, cell.Height - 1), Sf(5)))
                {
                    using var cb = new SolidBrush(Theme.Surface); g.FillPath(cb, path);
                    using var cp = new Pen(editing ? Theme.Accent : Theme.Border); g.DrawPath(cp, path);
                }
                if (!editing)
                    TextRenderer.DrawText(g, sp[r] + "%", bf, cell, Editable ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int band = CurveModel.Band(sp[r]);
                var (bc, bk) = band == 0 ? (Theme.Green, "fc_zone_quiet") : band == 1 ? (Theme.Amber, "fc_zone_audible") : (Theme.Red, "fc_zone_loud");
                TextRenderer.DrawText(g, Lang.T(bk), rf, new Rectangle(xBand, row.Y, bandW, rowH), bc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int d = sp[r] - def[r];
                string vs = def[r] + "%" + (d == 0 ? "" : $"  ({(d > 0 ? "+" : "")}{d})");
                TextRenderer.DrawText(g, vs, rf, new Rectangle(xDef, row.Y, Math.Max(40, x1 - xDef), rowH), d == 0 ? Theme.Muted : Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
        _tableRows = rows;
        _tablePctCells = cells;
    }
    /// <summary>
    /// True while this page is actively driving the EC: visible with the curve switch on, so
    /// every mouse-up writes the tables. A model-database swap waits for this to go false,
    /// because the page holds its own copy of the register layout and the point values.
    /// </summary>
    public bool CurveHot => Visible && _enable.Checked;

    public override void OnDeviceDbChanged()
    {
        _dev = Devices.Detect(D.Firmware());
        _fc = _dev?.FanCurve;
        _loaded = false;              // re-read the points from the new addresses on next entry
        _live.ClearTrail();           // old samples were read from the old register layout
        if (Visible) OnEnter();
        Invalidate();
    }

}

internal static class RectExt
{
    /// <summary>A copy grown by dx/dy on each side (Rectangle.Inflate mutates in place).</summary>
    public static Rectangle Inflate2(this Rectangle r, int dx, int dy) =>
        new(r.X - dx, r.Y - dy, r.Width + dx * 2, r.Height + dy * 2);
}
