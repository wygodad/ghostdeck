using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace MSIProfileSwitcher;

public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray = new();
    private readonly OsdForm _osd = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 3000 };

    private AppSettings _settings;
    private readonly DeviceProfile? _device;
    private readonly string _firmware;
    private readonly bool _simulate;   // MSIPS_FORCE_FIRMWARE set -> UI preview, no EC writes
    private ProfileId _current;
    private Icon? _currentIcon;
    private DateTime _profileSince = DateTime.Now;
    private int _switches;
    private PowerLineStatus? _lastPower;
    private MainForm? _main;
    private readonly List<Image> _menuSwatches = new();
    private SynchronizationContext? _ui;
    private string? _updateUrl;
    private string? _balloonUrl;              // URL opened when the tray balloon is clicked (update or notice)
    private Notices.Notice? _pendingNotice;   // fetched notice waiting to be shown as an in-window banner
    private bool _firmwareChanged;             // EC firmware differs from last-seen -> block auto-writes
    private bool _coolerBoost;                 // Cooler Boost (max fans) currently on
    private byte? _fanBeforeBoost;             // fan-mode byte captured before boost, restored on off
    private ToolStripMenuItem? _coolerItem;
    private OverlayForm? _overlay;             // gaming status overlay (lazy)
    private ToolStripMenuItem? _overlayItem;
    private ToolStripMenuItem? _overlayLockItem;

    private bool Known => _device != null;
    private bool Writable => Known && (_device!.Tier == Tier.Tested || _settings.ExperimentalEnabled);
    // Automatic (non user-initiated) writes are additionally blocked after a firmware change until acknowledged.
    private bool AutoWritable => Writable && !_firmwareChanged;

    public TrayContext()
    {
        _settings = AppSettings.Load();
        _settings.Autostart = Autostart.IsEnabled();
        Lang.Set(_settings.Language);
        Theme.Set(_settings.DarkMode);

        ChangeLog.Load();

        var forced = Environment.GetEnvironmentVariable("MSIPS_FORCE_FIRMWARE");
        _simulate = !string.IsNullOrEmpty(forced);
        _firmware = _simulate ? forced! : Ec.ReadFirmware();
        _device = Devices.Detect(_firmware);
        _current = Known ? Ec.GetCurrent(_device!) : ProfileId.Balanced;

        DetectFirmwareChange();
        if (Known && !_simulate) { try { _coolerBoost = Ec.GetCoolerBoost(_device!); } catch { } }

        if (AutoWritable) TryApplyChargeLimit();

        BuildMenu();
        UpdateUi(_current);
        _tray.Visible = true;
        ApplyHotkeys();

        _lastPower = SystemInformation.PowerStatus.PowerLineStatus;
        if (AutoWritable && _settings.AutoSwitchEnabled) ApplyForPower(_lastPower.Value, osd: false);

        _poll.Tick += (_, _) => Poll();
        _poll.Start();

        ShowState();
        if (_firmwareChanged) ShowFirmwareWarning();
        if (_settings.OverlayEnabled) SetOverlay(true, osd: false);

        _ui = SynchronizationContext.Current;
        _tray.BalloonTipClicked += (_, _) => { if (_balloonUrl != null) OpenUrl(_balloonUrl); };
        MaybeCheckForUpdates();
    }

    // ---------------- firmware-change guard ----------------
    private void DetectFirmwareChange()
    {
        if (_simulate || string.IsNullOrEmpty(_firmware)) return;   // only judge on real hardware
        if (!string.IsNullOrEmpty(_settings.LastFirmware) &&
            !_settings.LastFirmware.Equals(_firmware, StringComparison.OrdinalIgnoreCase))
        {
            _firmwareChanged = true;
            ChangeLog.Add(ChangeSource.Firmware,
                string.Format(Lang.T("log_fw_changed"), _settings.LastFirmware, _firmware));
        }
        else if (string.IsNullOrEmpty(_settings.LastFirmware))
        {
            _settings.LastFirmware = _firmware;   // first run: remember silently
            _settings.Save();
        }
    }

    private void ShowFirmwareWarning()
    {
        _tray.BalloonTipTitle = Lang.T("fw_changed_title");
        _tray.BalloonTipText = Lang.T("fw_changed_text");
        _tray.ShowBalloonTip(9000);
    }

    private void AcknowledgeFirmware()
    {
        _settings.LastFirmware = _firmware;
        _settings.Save();
        _firmwareChanged = false;
        ChangeLog.Add(ChangeSource.Firmware, Lang.T("log_fw_ack"), _firmware);
        BuildMenu();
        UpdateUi(_current);
        if (AutoWritable) TryApplyChargeLimit();
        if (AutoWritable && _settings.AutoSwitchEnabled)
            ApplyForPower(SystemInformation.PowerStatus.PowerLineStatus, osd: false);
    }

    private string DeviceName() => Known ? _device!.Name : Lang.T("unsupported_title");

    private (string text, Color color) TierBadge()
    {
        if (!Known) return (Lang.T("tier_unsupported"), Color.FromArgb(0xB0, 0x4A, 0x3A));
        return _device!.Tier == Tier.Tested
            ? (Lang.T("tier_tested"),       Color.FromArgb(0x2E, 0xA0, 0x43))
            : (Lang.T("tier_experimental"), Color.FromArgb(0xCC, 0x7A, 0x12));
    }

    private string DeviceDescriptor()
    {
        if (!Known) return Lang.T("unsupported_title") + (_simulate ? "  (test)" : "");
        string tier = _device!.Tier == Tier.Tested ? Lang.T("tier_tested")
                    : Writable ? Lang.T("tier_experimental")
                    : Lang.T("experimental_locked");
        return _device.Name + "  ·  " + tier + (_simulate ? "  (test)" : "");
    }

    private void ShowState()
    {
        if (Writable) ShowOsd(_current);
        else if (Known) _osd.ShowProfile("MSI  ·  " + _device!.Name, Lang.T("experimental_locked"), Color.Gray);
        else _osd.ShowProfile("MSI  ·  " + Lang.T("unsupported_title"),
                              string.IsNullOrEmpty(_firmware) ? Lang.T("unsupported_sub") : _firmware + " · " + Lang.T("unsupported_sub"),
                              Color.Gray);
    }

    // ---------------- menu ----------------
    private void BuildMenu()
    {
        _tray.ContextMenuStrip?.Dispose();
        foreach (var im in _menuSwatches) im.Dispose();
        _menuSwatches.Clear();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("MSI Profile Switcher") { Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        menu.Items.Add(new ToolStripLabel(DeviceDescriptor()) { ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) });
        menu.Items.Add(new ToolStripSeparator());

        if (_firmwareChanged)
        {
            var fw = new ToolStripMenuItem(Lang.T("menu_fw_ack"))
            {
                ForeColor = Color.FromArgb(0xB0, 0x4A, 0x3A),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ToolTipText = Lang.T("fw_changed_text"),
            };
            fw.Click += (_, _) => AcknowledgeFirmware();
            menu.Items.Add(fw);
            menu.Items.Add(new ToolStripSeparator());
        }

        if (_updateUrl is { } url)
        {
            var upd = new ToolStripMenuItem(Lang.T("menu_update"))
            {
                ForeColor = Color.FromArgb(0x2E, 0xA0, 0x43),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
            };
            upd.Click += (_, _) => OpenUrl(url);
            menu.Items.Add(upd);
            menu.Items.Add(new ToolStripSeparator());
        }

        foreach (var id in Profiles.Order)
        {
            var swatch = MakeSwatch(_settings.ColorFor(id));
            _menuSwatches.Add(swatch);
            var item = new ToolStripMenuItem(Profiles.Get(id).Label, swatch)
            {
                Tag = id,
                Enabled = Writable,
                ImageScaling = ToolStripItemImageScaling.None,
            };
            item.Click += (_, _) => SetProfile((ProfileId)item.Tag!, osd: true, ChangeSource.Tray);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        _coolerItem = new ToolStripMenuItem(Lang.T("cooler_boost"))
        {
            Enabled = Writable,
            Checked = _coolerBoost,
            CheckOnClick = false,
        };
        _coolerItem.Click += (_, _) => ToggleCoolerBoost();
        menu.Items.Add(_coolerItem);

        _overlayItem = new ToolStripMenuItem(Lang.T("overlay_title")) { Checked = OverlayVisible, CheckOnClick = false };
        _overlayItem.Click += (_, _) => ToggleOverlay();
        menu.Items.Add(_overlayItem);

        _overlayLockItem = new ToolStripMenuItem(Lang.T("ov_lock_menu")) { Checked = _settings.OverlayClickThrough, CheckOnClick = false };
        _overlayLockItem.Click += (_, _) => ToggleOverlayLock();
        menu.Items.Add(_overlayLockItem);

        menu.Items.Add(new ToolStripSeparator());

        var panel = new ToolStripMenuItem(Lang.T("menu_panel"));
        panel.Click += (_, _) => OpenMain(MainTab.Scenarios);
        menu.Items.Add(panel);

        var status = new ToolStripMenuItem(Lang.T("menu_status"));
        status.Click += (_, _) => OpenMain(MainTab.Status);
        menu.Items.Add(status);

        var curve = new ToolStripMenuItem(Lang.T("fc_title"));
        curve.Click += (_, _) => OpenMain(MainTab.FanCurve);
        menu.Items.Add(curve);

        var models = new ToolStripMenuItem(Lang.T("tab_models"));
        models.Click += (_, _) => OpenMain(MainTab.Models);
        menu.Items.Add(models);

        var report = new ToolStripMenuItem(Lang.T("menu_report"));
        report.Click += (_, _) => OpenMain(MainTab.Report);
        menu.Items.Add(report);

        var feedback = new ToolStripMenuItem(Lang.T("menu_feedback"));
        feedback.Click += (_, _) => OpenFeedback();
        menu.Items.Add(feedback);

        var log = new ToolStripMenuItem(Lang.T("menu_log"));
        log.Click += (_, _) => LogForm.ShowSingleton();
        menu.Items.Add(log);

        var langMenu = new ToolStripMenuItem(Lang.T("menu_language"));
        for (int i = 0; i < Lang.Names.Length; i++)
        {
            string code = Lang.Codes[i];
            var li = new ToolStripMenuItem(Lang.Names[i]) { Checked = code == Lang.CurrentCode };
            li.Click += (_, _) => ChangeLanguage(code);
            langMenu.DropDownItems.Add(li);
        }
        menu.Items.Add(langMenu);

        var settings = new ToolStripMenuItem(Lang.T("menu_settings"));
        settings.Click += (_, _) => OpenMain(MainTab.Settings);
        menu.Items.Add(settings);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem(Lang.T("menu_exit"));
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        _tray.ContextMenuStrip = menu;
        _tray.MouseClick -= TrayClick;
        _tray.MouseClick += TrayClick;
    }

    private void TrayClick(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (Writable) Cycle(ChangeSource.Tray);
        else ShowState();
    }

    // maly kafelek w kolorze profilu (do menu)
    private static Image MakeSwatch(Color c)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        var rect = new Rectangle(2, 2, 11, 11);
        const int d = 6;
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using (var b = new SolidBrush(c)) g.FillPath(b, path);
        using (var pen = new Pen(Color.FromArgb(45, 0, 0, 0))) g.DrawPath(pen, path);
        return bmp;
    }

    // ---------------- profile ----------------
    private void SetProfile(ProfileId id, bool osd, ChangeSource source, bool count = true)
    {
        if (!Writable) { ShowState(); return; }
        try
        {
            if (_simulate)
                ChangeLog.Add(source, $"{Profiles.Get(id).Label}  ·  {RecipeStr(id)}", "(simulate)");
            else
                ApplyRecipeLogged(id, source);
            if (id != _current && count) _switches++;
            _current = id;
            _profileSince = DateTime.Now;
            UpdateUi(id);
            if (osd) ShowOsd(id);
        }
        catch (Exception ex)
        {
            ChangeLog.Add(source, Profiles.Get(id).Label, Lang.T("log_err") + ": " + ex.Message);
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    private string RecipeStr(ProfileId id) =>
        string.Join(" ", _device!.Recipes[id].Select(r => $"{r.addr:X2}={r.val:X2}"));

    // Apply the recipe, then read the same addresses back (informational only, see TECHNICAL §19.4)
    // and record both in the history log.
    private void ApplyRecipeLogged(ProfileId id, ChangeSource source)
    {
        var recipe = _device!.Recipes[id];
        Ec.Apply(recipe);
        string read;
        try
        {
            var addrs = recipe.Select(r => r.addr).ToArray();
            var got = Ec.ReadMany(addrs);
            read = string.Join(" ", addrs.Zip(got, (a, v) => $"{a:X2}={v:X2}"));
        }
        catch { read = Lang.T("log_read_fail"); }
        ChangeLog.Add(source, $"{Profiles.Get(id).Label}  ·  {RecipeStr(id)}", read);
    }

    private void Cycle(ChangeSource source)
    {
        int i = Array.IndexOf(Profiles.Order, _current);
        SetProfile(Profiles.Order[(i + 1) % Profiles.Order.Length], osd: true, source);
    }

    // ---------------- cooler boost (max fans) ----------------
    private void ToggleCoolerBoost() => SetCoolerBoostState(!_coolerBoost);

    private void SetCoolerBoostState(bool next)
    {
        if (!Writable) { ShowState(); UpdateCoolerBoostMenu(); return; }
        if (next == _coolerBoost) { UpdateCoolerBoostMenu(); return; }
        try
        {
            string read = "(simulate)";
            if (!_simulate)
            {
                if (next)
                {
                    // Remember the active fan mode (Silent 0x1D / auto 0x0D / curve 0x8D) so we can
                    // restore it precisely when boost is turned off.
                    try { _fanBeforeBoost = Ec.ReadByte(_device!.FanMode); } catch { _fanBeforeBoost = null; }
                    Ec.SetCoolerBoost(_device!, true);
                }
                else
                {
                    Ec.SetCoolerBoost(_device!, false);
                    // Clearing the boost bit alone does not always spin the fans back down on this EC —
                    // the firmware keeps them at max until the fan mode is re-asserted. Re-write the fan
                    // byte that was active before boost to hand control back to the profile / curve.
                    byte fallback = 0x0D;   // auto fan, if the recipe somehow lacks the fan byte
                    foreach (var (a, v) in _device!.Recipes[_current]) if (a == _device!.FanMode) { fallback = v; break; }
                    byte restore = _fanBeforeBoost ?? fallback;
                    try { Ec.SetFanMode(_device!, restore); } catch { }
                    _fanBeforeBoost = null;
                }
                try { read = $"{_device!.CoolerBoost:X2}={Ec.ReadByte(_device!.CoolerBoost):X2} {_device!.FanMode:X2}={Ec.ReadByte(_device!.FanMode):X2}"; }
                catch { read = Lang.T("log_read_fail"); }
            }
            _coolerBoost = next;
            ChangeLog.Add(ChangeSource.CoolerBoost,
                Lang.T("cooler_boost") + ": " + (next ? Lang.T("st_on") : Lang.T("st_off")),
                read);
            _osd.ShowProfile("MSI  ·  " + Lang.T("cooler_boost"),
                Lang.T(next ? "cooler_boost_on" : "cooler_boost_off"),
                next ? Color.FromArgb(0x17, 0xC0, 0xEB) : Color.Gray);
            UpdateCoolerBoostMenu();
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    private void UpdateCoolerBoostMenu()
    {
        if (_coolerItem is { } it && !it.IsDisposed) it.Checked = _coolerBoost;
        if (_main is { IsDisposed: false }) _main.RefreshActive();
    }

    // ---------------- gaming overlay ----------------
    private bool OverlayVisible => _overlay is { IsDisposed: false, Visible: true };

    private void ToggleOverlay() => SetOverlay(!OverlayVisible, osd: true);

    private void SetOverlay(bool on, bool osd)
    {
        if (on)
        {
            if (_overlay is not { IsDisposed: false })
            {
                _overlay = new OverlayForm(_settings, BuildOverlaySample);
                _overlay.FormClosed += (_, _) => _overlay = null;
            }
            _overlay.ApplySettings();
            if (!_overlay.Visible) _overlay.Show();
        }
        else _overlay?.Hide();

        if (_settings.OverlayEnabled != on) { _settings.OverlayEnabled = on; _settings.Save(); }
        UpdateOverlayMenu();
        if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("overlay_title"),
            Lang.T(on ? "st_on" : "st_off"), Color.FromArgb(0x17, 0xC0, 0xEB));
    }

    // Re-read overlay options after the user edits them in Settings.
    private void ApplyOverlaySettings() { _overlay?.ApplySettings(); UpdateOverlayMenu(); }

    // Snap the overlay to a screen corner (0=TL 1=TR 2=BL 3=BR); persists and re-applies.
    private void SnapOverlayCorner(int corner)
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        const int m = 24;
        int w = _overlay?.Width ?? 240, h = _overlay?.Height ?? 120;
        int x = (corner is 1 or 3) ? wa.Right - w - m : wa.X + m;
        int y = (corner is 2 or 3) ? wa.Bottom - h - m : wa.Y + m;
        _settings.OverlayX = x; _settings.OverlayY = y;
        _settings.Save();
        _overlay?.ApplySettings();
    }

    // Lock = click-through (mouse passes to the game, panel can't be dragged); unlock to reposition.
    private void ToggleOverlayLock()
    {
        _settings.OverlayClickThrough = !_settings.OverlayClickThrough;
        _settings.Save();
        _overlay?.ApplySettings();
        UpdateOverlayMenu();
        _osd.ShowProfile("MSI  ·  " + Lang.T("overlay_title"),
            Lang.T(_settings.OverlayClickThrough ? "ov_locked" : "ov_unlocked"), Color.FromArgb(0x17, 0xC0, 0xEB));
    }

    private void UpdateOverlayMenu()
    {
        if (_overlayItem is { } it && !it.IsDisposed) it.Checked = OverlayVisible;
        if (_overlayLockItem is { } lk && !lk.IsDisposed) lk.Checked = _settings.OverlayClickThrough;
        if (_main is { IsDisposed: false }) _main.RefreshActive();
    }

    // Snapshot for the overlay: EC hardware + OS metrics + the active profile/cooler state.
    private OverlaySample BuildOverlaySample()
    {
        var hw = Known ? Ec.ReadHw(_device!) : new HwSnapshot(0, 0, 0, 0, 0, _firmware);
        int load = SysInfo.CpuUsage();
        var (ramPct, _, ramUsed) = SysInfo.Ram();
        var ps = SystemInformation.PowerStatus;
        int batt = ps.BatteryLifePercent is >= 0f and <= 1f ? (int)Math.Round(ps.BatteryLifePercent * 100) : -1;
        bool charging = ps.PowerLineStatus == PowerLineStatus.Online && (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0;
        return new OverlaySample(
            Known, Writable,
            Profiles.Get(_current).Label, _settings.ColorFor(_current), _coolerBoost,
            hw.CpuTemp, hw.GpuTemp, hw.CpuRpm, hw.GpuRpm, hw.CpuFan, hw.GpuFan,
            load, ramPct, ramUsed, hw.ChargeLimit, batt, charging,
            Perf.GpuUsage(), Perf.VramUsedMb(), Perf.CpuClockMhz());
    }

    private void ShowOsd(ProfileId id)
    {
        var def = Profiles.Get(id);
        _osd.ShowProfile("MSI  ·  " + def.Label, Lang.T(def.SubKey), _settings.ColorFor(id));
    }

    private void UpdateUi(ProfileId id)
    {
        var color = Writable ? _settings.ColorFor(id) : Color.Gray;
        var newIcon = TrayIconFactory.Create(color);
        _tray.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
        _tray.Text = Writable ? "MSI Profile: " + Profiles.Get(id).Label : "MSI · " + DeviceDescriptor();

        if (_tray.ContextMenuStrip is { } menu)
            foreach (var it in menu.Items)
                if (it is ToolStripMenuItem mi && mi.Tag is ProfileId pid)
                    mi.Checked = Writable && pid == id;

        if (_main is { IsDisposed: false }) _main.RefreshActive();
    }

    // ---------------- hotkeys ----------------
    private void ApplyHotkeys()
    {
        _hotkeys.UnregisterAll();
        Reg("Overlay", ToggleOverlay);       // read-only, so both work even when EC writes are disabled
        Reg("OverlayLock", ToggleOverlayLock);
        if (!Writable) return;
        Reg("Silent", () => SetProfile(ProfileId.Silent, true, ChangeSource.Hotkey));
        Reg("Balanced", () => SetProfile(ProfileId.Balanced, true, ChangeSource.Hotkey));
        Reg("Extreme", () => SetProfile(ProfileId.Extreme, true, ChangeSource.Hotkey));
        Reg("SuperBattery", () => SetProfile(ProfileId.SuperBattery, true, ChangeSource.Hotkey));
        Reg("Cycle", () => Cycle(ChangeSource.Hotkey));
        Reg("CoolerBoost", ToggleCoolerBoost);
    }

    private void Reg(string key, Action action)
    {
        if (_settings.Hotkeys.TryGetValue(key, out var hd) && hd.IsSet)
            _hotkeys.Register(hd.Mods, hd.Vk, action);
    }

    // ---------------- settings / language / status ----------------
    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings.Clone(), saved =>
        {
            _settings = saved;
            Lang.Set(saved.Language);
            _settings.Save();
            if (Known) _current = Ec.GetCurrent(_device!);
            ApplyHotkeys();
            BuildMenu();
            UpdateUi(_current);
            if (Writable) TryApplyChargeLimit();
            try { Autostart.Set(_settings.Autostart); } catch { }
        });
        form.ShowDialog();
    }

    private void ChangeLanguage(string code)
    {
        _settings.Language = code;
        Lang.Set(code);
        _settings.Save();
        BuildMenu();
        UpdateUi(_current);
    }

    private MainDeps BuildDeps() => new()
    {
        Settings = _settings,
        Status = () =>
        {
            var (tier, color) = TierBadge();
            return new StatusInfo(_current, Writable, Known, DeviceName(), tier, color,
                                  _switches, DateTime.Now - _profileSince, Autostart.IsEnabled(), AppVersion());
        },
        Hw = () => Known ? Ec.ReadHw(_device!) : new HwSnapshot(0, 0, 0, 0, 0, _firmware),
        Current = () => _current,
        SetProfile = id => SetProfile(id, osd: true, ChangeSource.Panel),
        Writable = () => Writable,
        ColorOf = id => _settings.ColorFor(id),
        Firmware = _firmware,
        AppVersion = AppVersion,
        SaveSettings = () => _settings.Save(),
        CheckNoticesNow = CheckNoticesNow,
        SettingsChanged = () => { ApplyHotkeys(); BuildMenu(); UpdateUi(_current); },
        OpenLegacySettings = OpenSettings,
        StartReportWizard = OpenReport,
        SetChargeLimit = limit =>
        {
            _settings.ChargeLimit = limit;
            _settings.Save();
            TryApplyChargeLimit();
        },
        SetAutoSwitch = on =>
        {
            _settings.AutoSwitchEnabled = on;
            _settings.Save();
            if (on && AutoWritable) ApplyForPower(SystemInformation.PowerStatus.PowerLineStatus, osd: false);
        },
        CoolerBoost = () => _coolerBoost,
        SetCoolerBoost = SetCoolerBoostState,
        OverlayOn = () => OverlayVisible,
        SetOverlay = on => SetOverlay(on, osd: false),
        ApplyOverlaySettings = ApplyOverlaySettings,
        SnapOverlay = SnapOverlayCorner,
        WithEcWrite = act =>
        {
            if (Writable && !_simulate && _device != null)
            {
                try { act(_device); } catch { }
            }
        },
    };

    private void OpenMain(MainTab tab)
    {
        if (_main is { IsDisposed: false })
        {
            _main.WindowState = FormWindowState.Normal;
            _main.ShowTab(tab);
            _main.BringToFront();
            _main.Activate();
            if (_pendingNotice is { } pn1) ShowNoticeBanner(pn1);
            return;
        }
        _main = new MainForm(BuildDeps());
        _main.FormClosed += (_, _) => _main = null;
        _main.Show();
        _main.ShowTab(tab);
        if (_pendingNotice is { } pn2) ShowNoticeBanner(pn2);
    }

    private void OpenReport()
    {
        using var form = new ReportForm(_firmware, Known ? _device!.Name : "", AppVersion());
        if (_main is { IsDisposed: false }) form.ShowDialog(_main);
        else form.ShowDialog();
    }

    // ---------------- update check ----------------
    private void MaybeCheckForUpdates()
    {
        if (!_settings.UpdateCheckEnabled) return;
        if (DateTime.UtcNow - _settings.LastUpdateCheckUtc < TimeSpan.FromHours(24)) return;

        var current = typeof(TrayContext).Assembly.GetName().Version ?? new Version(1, 0, 0);
        var ui = _ui;
        Task.Run(async () =>
        {
            var res = await Updater.CheckAsync(current);
            var notices = await Notices.FetchAsync(current, _settings.SeenNoticeIds);
            void Apply() { OnUpdateResult(res); OnNoticesResult(notices); }
            if (ui != null) ui.Post(_ => Apply(), null);
            else Apply();
        });
    }

    private void OnUpdateResult(Updater.Result? res)
    {
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.Save();

        if (res is not { } r) return;
        _updateUrl = r.Url;
        _balloonUrl = r.Url;
        BuildMenu();
        _tray.BalloonTipTitle = Lang.T("update_available");
        _tray.BalloonTipText = string.Format(Lang.T("update_available_text"), r.Tag);
        _tray.ShowBalloonTip(8000);
    }

    // Announcements (one-way notices): show the newest unseen as a tray balloon now, and as an in-window
    // banner when the panel is (or gets) opened. Seen ids are persisted so each notice shows once.
    private void OnNoticesResult(List<Notices.Notice> notices)
    {
        if (notices.Count == 0) return;
        var n = notices[0];               // newest-first by convention in announcements.json
        _pendingNotice = n;

        // One place at a time: banner if the window is open (marks it seen → never nags again),
        // otherwise a tray balloon to nudge. The balloon doesn't mark it seen, so it keeps nudging
        // on the daily check until the user actually opens the app once.
        if (_main is { IsDisposed: false }) ShowNoticeBanner(n);
        else
        {
            _balloonUrl = string.IsNullOrEmpty(n.Url) ? null : n.Url;
            _tray.BalloonTipTitle = n.Title;
            _tray.BalloonTipText = n.Body;
            _tray.ShowBalloonTip(9000);
        }
    }

    // Manual "Check now": respect SeenNoticeIds so an already-read notice does NOT pop up again.
    private void CheckNoticesNow()
    {
        var current = typeof(TrayContext).Assembly.GetName().Version ?? new Version(1, 0, 0);
        var ui = _ui;
        Task.Run(async () =>
        {
            var notices = await Notices.FetchAsync(current, _settings.SeenNoticeIds);
            if (ui != null) ui.Post(_ => OnNoticesResult(notices), null);
            else OnNoticesResult(notices);
        });
    }

    private void ShowNoticeBanner(Notices.Notice n)
    {
        if (_main is not { IsDisposed: false }) return;
        _main.ShowNotice(n.Title, n.Body, string.IsNullOrEmpty(n.Url) ? null : n.Url, () => MarkNoticeSeen(n.Id));
    }

    private void MarkNoticeSeen(string id)
    {
        if (_pendingNotice?.Id == id) _pendingNotice = null;
        if (_settings.SeenNoticeIds.Contains(id)) return;
        _settings.SeenNoticeIds.Add(id);
        _settings.Save();
    }

    // Two-way feedback: open a prefilled GitHub Discussion in the browser (no data collected by the app;
    // the user chooses what to post). Model reports keep going to Issues via the Report wizard.
    private void OpenFeedback()
    {
        string body = Uri.EscapeDataString(
            $"\n\n---\nApp: {AppVersion()}  |  Model: {(Known ? _device!.Name : "unknown")}  |  Firmware: {_firmware}");
        OpenUrl($"https://github.com/wygodad/msi-profile-switcher/discussions/new?category=ideas&body={body}");
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static string AppVersion()
    {
        var v = typeof(TrayContext).Assembly.GetName().Version;
        return v == null ? "1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private void TryApplyChargeLimit()
    {
        if (AutoWritable && !_simulate && _settings.ChargeLimit is 60 or 80 or 100)
        {
            try
            {
                Ec.SetChargeLimit(_device!, _settings.ChargeLimit);
                ChangeLog.Add(ChangeSource.ChargeLimit,
                    string.Format(Lang.T("log_charge"), _settings.ChargeLimit),
                    $"{_device!.ChargeCtrl:X2}={(0x80 | _settings.ChargeLimit):X2}");
            }
            catch { }
        }
    }

    // ---------------- poll: auto-switch + external sync ----------------
    private void Poll()
    {
        if (!Writable) return;

        var power = SystemInformation.PowerStatus.PowerLineStatus;
        if (power != PowerLineStatus.Unknown && power != _lastPower)
        {
            if (AutoWritable && _settings.AutoSwitchEnabled) ApplyForPower(power, osd: true);
            _lastPower = power;
        }

        try
        {
            // Cooler Boost may be toggled elsewhere (or cleared by the firmware) — keep the menu in sync.
            bool cb = Ec.GetCoolerBoost(_device!);
            if (cb != _coolerBoost) { _coolerBoost = cb; UpdateCoolerBoostMenu(); }

            // While a custom fan curve runs (Advanced fan mode) the fan byte no longer tells
            // Silent from Balanced, so don't re-detect — keep the profile the user chose.
            if (Ec.ReadByte(_device!.FanMode) == 0x8D) return;
            var actual = Ec.GetCurrent(_device!);
            if (actual != _current)
            {
                ChangeLog.Add(ChangeSource.ExternalSync,
                    string.Format(Lang.T("log_external"), Profiles.Get(_current).Label, Profiles.Get(actual).Label));
                _current = actual;
                _profileSince = DateTime.Now;
                UpdateUi(actual);
            }
        }
        catch { }
    }

    private void ApplyForPower(PowerLineStatus power, bool osd)
    {
        var key = power == PowerLineStatus.Online ? _settings.ProfileOnAC : _settings.ProfileOnBattery;
        if (Enum.TryParse<ProfileId>(key, out var id))
            SetProfile(id, osd, ChangeSource.AutoAc);
    }

    private void ExitApp()
    {
        _poll.Stop();
        _tray.Visible = false;
        _hotkeys.Dispose();
        _main?.Close();
        _overlay?.Close();
        _osd.Dispose();
        _tray.Dispose();
        _currentIcon?.Dispose();
        ExitThread();
    }
}
