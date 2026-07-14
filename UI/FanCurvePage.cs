using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Phase 2 fan-curve editor. Two graphs (Fan 1 / Fan 2); each point's temperature is
/// fixed (as MSI does), the speed % is dragged up/down. "Apply" writes the curve and
/// runs it in Silent (Silent recipe + Advanced fan mode). Escape hatches restore the
/// stock Silent or Auto fan behaviour. All writes go through MainDeps.WithEcWrite
/// (gated on Writable + not simulating). Read-only when unsupported/not writable.
/// </summary>
public sealed class FanCurvePage : ThemedPage
{
    private const int Pad = 28;

    // MSI factory default (the curve we verified) — used by the "MSI default" button.
    private static readonly int[] DefCpuT = { 0, 50, 57, 64, 70, 76 }, DefCpuS = { 0, 40, 48, 60, 75, 89 };
    private static readonly int[] DefGpuT = { 0, 50, 55, 60, 65, 70 }, DefGpuS = { 0, 48, 60, 70, 82, 93 };

    private readonly DeviceProfile? _dev;
    private readonly FanCurveSpec? _fc;
    private int[] _cpuT, _cpuS, _gpuT, _gpuS;
    private bool _loaded;
    private bool _loading;   // background first-load in flight
    private int _dragFan = -1, _dragIdx = -1;
    private byte _fanMode;
    private readonly System.Windows.Forms.Timer _modeTimer = new() { Interval = 1200 };

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
        _dev = Devices.Detect(d.Firmware);
        _fc = _dev?.FanCurve;
        _cpuT = (int[])DefCpuT.Clone(); _cpuS = (int[])DefCpuS.Clone();
        _gpuT = (int[])DefGpuT.Clone(); _gpuS = (int[])DefGpuS.Clone();

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

        _modeTimer.Tick += (_, _) => RefreshMode();
        VisibleChanged += (_, _) => { if (Visible && _fc != null) _modeTimer.Start(); else _modeTimer.Stop(); };
        Resize += (_, _) => { LayoutButtons(); Invalidate(); };
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += (_, _) => { bool dragged = _dragIdx >= 0; _dragFan = _dragIdx = -1; if (dragged && _enable.Checked) ReApply(); };
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
                            _cpuT = v.cpuTemp; _cpuS = v.cpuSpeed; _gpuT = v.gpuTemp; _gpuS = v.gpuSpeed;
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
        if (p == null || !p.IsValid(_fc?.Points ?? 6)) return;
        _cpuT = (int[])p.CpuTemp.Clone(); _cpuS = (int[])p.CpuSpeed.Clone();
        _gpuT = (int[])p.GpuTemp.Clone(); _gpuS = (int[])p.GpuSpeed.Clone();
        _loaded = true;
        if (_enable.Checked && Editable) ReApply();
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
            if (p == null || !p.IsValid(_fc?.Points ?? 6))
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
        string body = $"Model: {(_dev?.Name ?? "unknown")}\nFirmware: {D.Firmware}\nApp: {D.AppVersion()}\n\n```json\n{json}\n```\n";
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

    private void RefreshMode()
    {
        // Single-byte EC read, but still a WMI round-trip; keep it off the UI thread
        // (called on enter and every 1.2 s by the mode timer).
        if (_fc != null && _dev != null)
        {
            var dev = _dev;
            Task.Run(() =>
            {
                byte m = _fanMode;
                try { m = Ec.ReadByte(dev.FanMode); } catch { }
                try { BeginInvoke(() => { _fanMode = m; SyncEnable(); }); } catch { }
            });
        }
        else SyncEnable();
    }

    // keep the switch in sync with the actual hardware state (programmatic set won't fire Toggled)
    private void SyncEnable()
    {
        _enable.Checked = _fanMode == 0x8D;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _modeTimer.Dispose();
        base.Dispose(disposing);
    }

    public override void ApplyTheme() { base.ApplyTheme(); Restyle(); }

    private void Restyle()
    {
        Ui.StyleGhost(_default);
        _default.Text = Lang.T("fc_default");
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

    private void LayoutButtons()
    {
        int by = Height - 62, bh = 42;
        _enable.Location = new Point(Pad, by + (bh - _enable.Height) / 2);
        _enableLabel.Location = new Point(Pad + _enable.Width + 12, by + (bh - _enableLabel.Height) / 2);
        _default.SetBounds(Width - Pad - 170, by, 170, bh);
        int rw = TextRenderer.MeasureText(_report.Text, _report.Font).Width + 40;
        _report.SetBounds(_default.Left - 14 - rw, by, rw, bh);

        // preset bar (label, picker, manage buttons, then import/export/share);
        // hidden controls (e.g. the whole manage group when no presets exist) take no space
        int py = 98;
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
    }

    // Editing is gated by the normal write permission (Tested, or Experimental opted in) — same as
    // profile switching. On unverified models the live preview is the user's sanity check (a wrong
    // address shows nonsense), and the curve is fully reversible, so we don't hard-block it.
    private bool Editable => _fc != null && D.Writable();

    private byte ProfileFanByte() => D.Status().Profile == ProfileId.Silent ? _dev!.FanSilentValue : (byte)0x0D;

    // Switch OFF: give fans back to the current profile's normal behaviour and reset the graph.
    private void RevertToProfileDefault()
    {
        D.WithEcWrite(dev => Ec.SetFanMode(dev, ProfileFanByte()));
        _cpuS = (int[])DefCpuS.Clone(); _gpuS = (int[])DefGpuS.Clone();
        RefreshMode();
        if (D.Writable()) ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_curve_off"), $"{_dev!.FanMode:X2}={ProfileFanByte():X2}");
    }

    // Re-write the current graph while the curve is already on (e.g. after dragging a point).
    private void ReApply()
    {
        if (_fc == null) return;
        D.WithEcWrite(dev => { Ec.WriteFanCurve(dev, _cpuT, _cpuS, _gpuT, _gpuS); Ec.SetFanMode(dev, _fc.AdvancedModeValue); });
        RefreshMode();
    }

    // ---- geometry ----
    private Rectangle GraphRect(int fan)
    {
        int top = 150, bottom = Height - 124, gap = 40;
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
        float y = p.Bottom - s[i] / 100f * p.Height;
        return new PointF(x, y);
    }

    // ---- interaction ----
    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (!Editable) return;
        for (int fan = 0; fan < 2; fan++)
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
        if (_dragIdx >= 0) SetSpeed(e.Y);
    }

    private void SetSpeed(int mouseY)
    {
        var p = PlotRect(_dragFan);
        int[] s = _dragFan == 0 ? _cpuS : _gpuS;
        int sp = (int)Math.Round((p.Bottom - mouseY) / (float)p.Height * 100);
        int lo = _dragIdx > 0 ? s[_dragIdx - 1] : 0;
        int hi = _dragIdx < s.Length - 1 ? s[_dragIdx + 1] : 100;
        s[_dragIdx] = Math.Clamp(Math.Clamp(sp, 0, 100), lo, hi);
        Invalidate();
    }

    private void Apply()
    {
        if (_fc is not { } fc) return;
        int peak = Math.Max(_cpuS[^1], _gpuS[^1]);
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
        RefreshMode();
        if (D.Writable()) ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_curve_on"), $"{_dev!.FanMode:X2}={fc.AdvancedModeValue:X2}");
    }

    // ---- paint ----
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, Lang.T("fc_title"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 22), Theme.Text);

        if (_fc == null)
        {
            TextRenderer.DrawText(g, Lang.T("test_curve_none"), new Font("Segoe UI", 11f),
                new Rectangle(Pad, 72, Width - Pad * 2, 40), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordEllipsis);
            return;
        }

        // live fan-mode indicator (feedback for Apply / Restore automatic)
        string modeName = _fanMode switch
        {
            0x8D => "Advanced", 0x1D => "Silent", 0x0D => "Auto", _ => $"0x{_fanMode:X2}"
        };
        var modeFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        TextRenderer.DrawText(g, Lang.T("fc_mode") + " " + modeName, modeFont,
            new Rectangle(Width - Pad - 360, 24, 360, 28), Theme.Accent,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        string hint = !D.Writable() ? Lang.T("fc_locked")
                    : _fc is { Verified: false } ? Lang.T("fc_preview")   // editable, but addresses unconfirmed
                    : Lang.T("fc_hint");
        TextRenderer.DrawText(g, hint, new Font("Segoe UI", 10.5f),
            new Rectangle(Pad, 68, Width - Pad * 2, 40), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordEllipsis);

        DrawFan(g, 0, Lang.T("fc_fan_cpu"), _cpuT, _cpuS);
        DrawFan(g, 1, Lang.T("fc_fan_gpu"), _gpuT, _gpuS);
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
        using (var grid = new Pen(Theme.Border))
        using (var axisFont = new Font("Segoe UI", 8.5f))
        {
            for (int v = 0; v <= 100; v += 25)
            {
                float y = p.Bottom - v / 100f * p.Height;
                g.DrawLine(grid, p.Left, y, p.Right, y);
                TextRenderer.DrawText(g, v + "%", axisFont, new Rectangle(card.X + 8, (int)y - 9, 44, 18), Theme.Faint,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        int n = speeds.Length;
        var pts = new PointF[n];
        for (int i = 0; i < n; i++) pts[i] = PointAt(fan, i);

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
            // node
            bool active = fan == _dragFan && i == _dragIdx;
            float r = active ? 9f : 7f;
            using var fill = new SolidBrush(Theme.Accent);
            using var ring = new Pen(Theme.Surface, 2.5f);
            g.FillEllipse(fill, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
            g.DrawEllipse(ring, pts[i].X - r, pts[i].Y - r, r * 2, r * 2);
        }
    }
}
