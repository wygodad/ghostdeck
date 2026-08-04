using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Win32;

namespace GhostDeck;

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
    private Updater.Result? _updateAvail;      // newer release found by the daily check (Settings Start header chip)
    private bool _telemetryOnly;               // (#48) no EC interface, but MSI WMI data blocks answer
    private string? _balloonUrl;              // URL opened when the tray balloon is clicked (update or notice)
    private Notices.Notice? _pendingNotice;   // fetched notice waiting to be shown as an in-window banner
    private bool _firmwareChanged;             // EC firmware differs from last-seen -> block auto-writes
    private bool _coolerBoost;                 // Cooler Boost (max fans) currently on
    private byte? _fanBeforeBoost;             // fan-mode byte captured before boost, restored on off
    private DateTime? _tempOverSince;          // when CPU/GPU first crossed the thermal-alert threshold
    private DateTime _lastTempAlert = DateTime.MinValue;
    private int _thermalBusy;                  // 1 while a background EC temperature read is in flight
    private ToolStripMenuItem? _coolerItem;
    private OverlayForm? _overlay;             // gaming status overlay (lazy)
    private ToolStripMenuItem? _overlayItem;
    private ToolStripMenuItem? _overlayLockItem;
    private bool _statusWantsFps;              // Status → Gaming sub-tab visible (keeps FpsMonitor running)
    private TrayWheel? _wheel;                 // (#23) wheel-over-tray hook, installed only while a wheel mode is set
    private int _wheelAccum;                   // raw wheel delta accumulator (one step per ±120)
    private ProfileId? _wheelTarget;           // pending wheel selection (previewed via OSD, applied after a pause)
    private System.Windows.Forms.Timer? _wheelTimer;
    private Action? _wheelCommit;              // what the wheel timer applies when the spin rests
    private int _wheelSceneIdx = -1;           // (#21) pending scene index while wheeling through scenes
    private byte _kbdAddr;                     // (#26) keyboard-backlight register (0 = model not in the msi-ec map)
    private int _kbdSim;                       // simulated level when MSIPS_FORCE_FIRMWARE is set
    private bool _webcamSupported;             // (#27) EC webcam switch expected on this board
    private bool _webcamOn = true;             // cached switch state, kept in sync by Poll (Fn key changes it too)
    private (byte Addr, bool Invert)? _fnSwap; // Fn/Win swap register (null = model not in the msi-ec map)
    private bool _fnLeftSim;                   // simulated Fn side when MSIPS_FORCE_FIRMWARE is set
    private readonly WinKeyLock _winLock = new();   // software Win-key lock (LL keyboard hook)
    private string? _lastScheduleRule;         // schedule engine: rule active at the last check ("" = none)
    private long _scheduleHoldUntil;           // Poll skips schedule checks briefly after resume (EC settle + restore order)
    private int _lastBattPct = -1;             // battery rules: last seen percent (edge detection)
    private bool _battLowFired, _battHighFired;

    private bool Known => _device != null;
    private bool Writable => Known && (_device!.Tier == Tier.Tested || _settings.ExperimentalEnabled);
    // Automatic (non user-initiated) writes are additionally blocked after a firmware change until acknowledged.
    private bool AutoWritable => Writable && !_firmwareChanged;

    public TrayContext()
    {
        _settings = AppSettings.Load();
        Autostart.Migrate();                       // move a pre-rename "MSIProfileSwitcher" autostart task to "GhostDeck"
        Autostart.Heal();                          // re-point the task if the exe was moved since it was created
        _settings.Autostart = Autostart.IsEnabled();
        Lang.Set(_settings.Language);
        Theme.Set(_settings.DarkMode);
        TrayIconFactory.Style = _settings.IconStyle;

        ChangeLog.Load();
        GameSessions.Load();

        FpsMonitor.StopOrphan();                   // clear an ETW session a crashed instance left behind
        FpsMonitor.SessionEnded += OnGameSession;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        var forced = Environment.GetEnvironmentVariable("MSIPS_FORCE_FIRMWARE");
        _simulate = !string.IsNullOrEmpty(forced);
        _firmware = _simulate ? forced! : Ec.ReadFirmware();
        _device = Devices.Detect(_firmware);
        // (#48) No EC interface for this firmware? The vendor WMI data blocks may still report
        // live temperatures - that turns a dead app into a working thermometer. Probed once.
        // MSIPS_FORCE_FIRMWARE=telemetry simulates this state on a normal machine (UI preview only)
        _telemetryOnly = !Known && (_firmware.Equals("telemetry", StringComparison.OrdinalIgnoreCase)
                                    ? MsiTelemetry.Available()
                                    : !_simulate && MsiTelemetry.Available());
        _current = Known ? Ec.GetCurrent(_device!) : ProfileId.Balanced;
        _kbdAddr = Known ? Devices.KbdBacklightFor(_firmware) : (byte)0;   // (#26)
        _webcamSupported = Known && Devices.WebcamSupported(_firmware);    // (#27)
        _fnSwap = Known ? Devices.FnWinSwapFor(_firmware) : null;
        if (_webcamSupported && !_simulate) { try { _webcamOn = Ec.GetWebcam(); } catch { } }

        DetectFirmwareChange();
        if (Known && !_simulate) { try { _coolerBoost = Ec.GetCoolerBoost(_device!); } catch { } }

        if (AutoWritable) TryApplyChargeLimit();

        BuildMenu();
        UpdateUi(_current);
        _tray.Visible = true;
        ApplyHotkeys();

        _lastPower = SystemInformation.PowerStatus.PowerLineStatus;
        // Some ECs boot (or wake) in Super Battery instead of the last profile; opt-in restore
        // brings back what the user actually chose. AC/battery auto-switch takes precedence.
        // Deliberately NO "already there" short-circuit: a cold boot loses the rest of the
        // profile state (fan mode, curve) even when the EC happens to wake in the same profile,
        // so restore always re-asserts the full recipe (re-writing identical bytes is harmless).
        if (_settings.RestoreProfileOnResume && !_settings.AutoSwitchEnabled && AutoWritable &&
            Enum.TryParse<ProfileId>(_settings.LastProfile, out var lastProf))
            SetProfile(lastProf, osd: false, ChangeSource.Restore, count: false);
        if (AutoWritable && _settings.AutoSwitchEnabled) ApplyForPower(_lastPower.Value, osd: false);
        TryRestoreCurve();   // (#49) after the profile settles; no-op unless opted in
        ApplyRefreshForPower(_lastPower.Value);   // align the panel with the current power source once at start

        _poll.Tick += (_, _) => Poll();
        _poll.Start();

        ShowState();
        if (_firmwareChanged) ShowFirmwareWarning();
        if (_settings.OverlayEnabled) SetOverlay(true, osd: false);

        _ui = SynchronizationContext.Current;
        ApplyTrayWheel();   // (#23) needs _ui for cross-thread posting, so after it is captured
        _tray.BalloonTipClicked += (_, _) => { if (_balloonUrl != null) OpenUrl(_balloonUrl); };
        MaybeCheckForUpdates();

        // Pre-create the main window HIDDEN shortly after startup: the form, its pages and all
        // their native handles are built off-screen, so the first open (and every tab) shows
        // instantly instead of flashing while dozens of controls are created.
        var prewarm = new System.Windows.Forms.Timer { Interval = 1800 };
        prewarm.Tick += (_, _) =>
        {
            prewarm.Stop();
            prewarm.Dispose();
            var m = EnsureMain();
            if (!m.Visible) { _ = m.Handle; m.EnsureWarm(); }
        };
        prewarm.Start();

        // A second launched exe can't start (single-instance mutex); it sets this named event
        // instead, and we bring the main window up - so double-clicking the exe again doesn't
        // look like "nothing happened".
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "GhostDeck_ShowMainWindow");
        new Thread(() =>
        {
            while (showSignal.WaitOne())
                _ui?.Post(_ => OpenMain(MainTab.Scenarios), null);
        }) { IsBackground = true }.Start();

        StartCliServer();

        // Scene schedule: the active window applies at startup too, AFTER the restore logic
        // above - so a boot inside "work hours" lands in the work scene, and the schedule
        // deliberately outranks the restored profile/curve.
        CheckSchedule(applyNow: true);
    }

    // ---------------- CLI pipe server ----------------
    // "GhostDeck.exe --profile Silent" from a second process lands here: the command line is
    // sent over a named pipe and executed on the UI thread with the exact same gates as the
    // UI itself. Response format: "<exitcode>|<message>".
    private void StartCliServer()
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var srv = new NamedPipeServerStream(Cli.PipeName, PipeDirection.InOut, 1);
                    srv.WaitForConnection();
                    using var r = new StreamReader(srv);
                    using var w = new StreamWriter(srv) { AutoFlush = true };
                    string? line = r.ReadLine();
                    if (line == null) continue;
                    string resp = "1|busy";
                    // deliberately NOT disposed here: on a timeout the UI thread may still call
                    // Set() later, which would throw on a disposed event inside a finally
                    var done = new ManualResetEventSlim();
                    var ui = _ui;
                    if (ui == null) resp = ExecuteCli(line);
                    else
                    {
                        ui.Post(_ => { try { resp = ExecuteCli(line); } finally { done.Set(); } }, null);
                        done.Wait(8000);
                    }
                    w.WriteLine(resp);
                }
                catch { Thread.Sleep(300); }
            }
        }) { IsBackground = true, Name = "GhostDeckCli" }.Start();
    }

    private string ExecuteCli(string raw)
    {
        try
        {
            var cmd = Cli.Parse(raw.Split('\t'));
            if (cmd == null) return "2|bad command";
            switch (cmd.Kind)
            {
                case CliKind.Status:
                {
                    var hw = ReadHwOrTelemetry();   // EC when known, else (#48) the vendor WMI blocks
                    var fs = FpsMonitor.Current;   // null unless the FPS monitor is on and a game presents
                    var ps = SystemInformation.PowerStatus;
                    int batt = ps.BatteryLifePercent is >= 0f and <= 1f ? (int)Math.Round(ps.BatteryLifePercent * 100) : -1;
                    bool noBatt = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
                    int battMin = Perf.BatteryMinutesLeft();
                    int wear = BatteryHealth.Read().WearPct;
                    int kbdLvl = KbdLevel();
                    int fnl = FnLeftState();
                    return "0|" + JsonSerializer.Serialize(new
                    {
                        running = true,
                        model = Known ? _device!.Name : "unsupported",
                        firmware = _firmware,
                        tier = _device?.Tier.ToString() ?? "None",
                        writable = Writable,
                        telemetry = _telemetryOnly,
                        profile = Known ? _current.ToString() : null,
                        fanBoost = _coolerBoost,
                        overlay = OverlayVisible,
                        winLock = _winLock.Enabled,
                        cpuTemp = hw.CpuTemp, gpuTemp = hw.GpuTemp,
                        cpuFan = hw.CpuFan, gpuFan = hw.GpuFan,
                        cpuRpm = hw.CpuRpm, gpuRpm = hw.GpuRpm,
                        refreshHz = Display.Current(),
                        chargeLimit = _settings.ChargeLimit,
                        kbdLight = kbdLvl >= 0 ? kbdLvl : (int?)null,
                        webcam = _webcamSupported && Writable ? _webcamOn : (bool?)null,
                        fnLeft = fnl >= 0 ? fnl == 1 : (bool?)null,
                        hdr = Hdr.Supported() ? Hdr.Enabled() : (bool?)null,
                        touchpad = Touchpad.State() is >= 0 and var tps ? tps == 1 : (bool?)null,
                        batteryPercent = noBatt || batt < 0 ? (int?)null : batt,
                        batteryCharging = noBatt ? (bool?)null : ps.PowerLineStatus == PowerLineStatus.Online,
                        batteryMinutesLeft = battMin > 0 ? battMin : (int?)null,
                        batteryWearPct = wear >= 0 ? wear : (int?)null,
                        disks = Perf.Disks().Select(dk => new { name = dk.Name, tempC = dk.TempC > 0 ? dk.TempC : (int?)null }).ToArray(),
                        fps = fs is { } f1 ? f1.Fps : (int?)null,
                        frameTimeMs = fs is { } f2 ? Math.Round(f2.FrameTimeMs, 1) : (double?)null,
                        game = fs is { } f3 ? f3.Process : null,
                    });
                }
                case CliKind.Overlay:
                    SetOverlay(cmd.Arg == "on", osd: false);
                    return "0|overlay: " + cmd.Arg;
                case CliKind.Brightness:
                {
                    // Windows-level (WMI), independent of EC writability.
                    int pct = int.Parse(cmd.Arg);
                    Brightness.Set(pct);   // a throw lands in the outer catch -> "1|<message>"
                    ChangeLog.Add(ChangeSource.Cli, Lang.T("bri_title") + ": " + pct + " %");
                    return "0|brightness: " + pct;
                }
                case CliKind.WinLock:
                    SetWinLockState(cmd.Arg == "on", ChangeSource.Cli, osd: false);
                    return "0|win key lock: " + cmd.Arg;
                case CliKind.HdrSwitch:
                {
                    // DisplayConfig API, independent of EC writability.
                    if (!Hdr.Supported()) return "1|no HDR-capable display";
                    bool on = cmd.Arg == "on";
                    if (!Hdr.Set(on)) return "1|the display refused the HDR change";
                    ChangeLog.Add(ChangeSource.Cli, $"HDR: {(on ? "on" : "off")}");
                    return "0|hdr: " + cmd.Arg;
                }
                case CliKind.Touchpad:
                {
                    // Devnode switch, independent of EC writability.
                    if (Touchpad.State() < 0) return "1|no precision touchpad found";
                    Touchpad.Set(cmd.Arg == "on");   // a throw lands in the outer catch
                    ChangeLog.Add(ChangeSource.Cli, Lang.T("tp_title") + ": " + cmd.Arg);
                    if (_main is { IsDisposed: false }) _main.RefreshActive();
                    return "0|touchpad: " + cmd.Arg;
                }
                case CliKind.Refresh:
                {
                    // Windows display API, independent of EC writability.
                    var rates = Display.SupportedRates();
                    if (rates.Count == 0) return "1|the display reports no switchable rates";
                    int hz = cmd.Arg == "max" ? rates.Max() : int.Parse(cmd.Arg);
                    if (!rates.Contains(hz)) return "1|unsupported rate: " + hz + " (supported: " + string.Join(", ", rates) + ")";
                    int before = Display.Current();
                    if (before != hz)
                    {
                        if (!Display.SetRefresh(hz)) return "1|the display refused the mode change";
                        ChangeLog.Add(ChangeSource.Display, $"{before} Hz → {hz} Hz");
                    }
                    return "0|refresh rate: " + hz + " Hz";
                }
            }

            if (!Writable) return "1|" + (Known ? "model is experimental - enable Experimental writes in Settings" : "unsupported hardware");
            switch (cmd.Kind)
            {
                case CliKind.Profile:
                    SetProfile(Enum.Parse<ProfileId>(cmd.Arg), osd: true, ChangeSource.Cli);
                    return "0|profile set: " + cmd.Arg;
                case CliKind.Cycle:
                    Cycle(ChangeSource.Cli);
                    return "0|profile set: " + _current;
                case CliKind.FanBoost:
                    SetCoolerBoostState(cmd.Arg == "on");
                    // optional per-call auto-off (#51): replaces the timer the setter just armed
                    if (cmd.Arg == "on" && cmd.Arg2.Length > 0 && int.TryParse(cmd.Arg2, out int fbSecs))
                        ArmBoostTimer(true, fbSecs);
                    return "0|fan boost: " + cmd.Arg + (cmd.Arg2.Length > 0 ? $" (auto-off in {cmd.Arg2} s)" : "");
                case CliKind.Curve:
                    if (_device?.FanCurve is not { } fc) return "1|no fan-curve support on this model";
                    if (cmd.Arg.Equals("auto", StringComparison.OrdinalIgnoreCase)) { ApplyPresetFromTray(null); return "0|fan curve: stock"; }
                    if (_settings.FindPreset(cmd.Arg) is not { } p || !p.IsValid(fc.Points)) return "1|preset not found: " + cmd.Arg;
                    ApplyPresetFromTray(p.Name);
                    return "0|fan curve applied: " + p.Name;
                case CliKind.Kbd:
                    if (_kbdAddr == 0) return "1|no keyboard-backlight support on this model";
                    SetKbdLight(Cli.ParseKbdLevel(cmd.Arg), ChangeSource.Cli);
                    return "0|keyboard backlight: " + cmd.Arg;
                case CliKind.Webcam:
                    if (!_webcamSupported) return "1|no webcam control on this model";
                    SetWebcamState(cmd.Arg == "on", ChangeSource.Cli);
                    return "0|webcam: " + cmd.Arg;
                case CliKind.FnSwap:
                    if (_fnSwap == null) return "1|no Fn/Win swap register on this model";
                    SetFnLeftState(cmd.Arg == "left", ChangeSource.Cli);
                    return "0|fn key: " + cmd.Arg;
                case CliKind.Scene:
                {
                    var sc = _settings.Scenes.FirstOrDefault(x => x.Name.Equals(cmd.Arg, StringComparison.OrdinalIgnoreCase));
                    if (sc == null) return "1|scene not found: " + cmd.Arg;
                    ApplyScene(sc, ChangeSource.Cli);
                    return "0|scene applied: " + sc.Name;
                }
                case CliKind.Charge:
                {
                    int limit = int.Parse(cmd.Arg);
                    _settings.ChargeLimit = limit;
                    _settings.Save();
                    TryApplyChargeLimit();   // logs the write itself; 0 = stop managing (no EC write)
                    if (limit == 0) ChangeLog.Add(ChangeSource.Cli, Lang.T("st_charge") + ": " + Lang.T("st_off"));
                    if (_main is { IsDisposed: false }) _main.RefreshActive();
                    return "0|charge limit: " + (limit > 0 ? limit + " %" : "off (no longer managed)");
                }
                case CliKind.Panic:
                    PanicReset();
                    return "0|panic reset done";
                default:
                    return "2|bad command";
            }
        }
        catch (Exception ex) { return "1|" + ex.Message; }
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
        // Theme-aware badge colours matching the ghostdeck.dev chips: positive = accent,
        // limited = amber, unsupported = pink/red.
        if (!Known) return _telemetryOnly ? (Lang.T("tier_telemetry"), Theme.Amber)
                                          : (Lang.T("tier_unsupported"), Theme.Red);
        return _device!.Tier == Tier.Tested
            ? (Lang.T("tier_tested"),       Theme.Accent)
            : (Lang.T("tier_experimental"), Theme.Amber);
    }

    private string DeviceDescriptor()
    {
        if (!Known) return (_telemetryOnly ? Lang.T("tier_telemetry") : Lang.T("unsupported_title"))
                         + (_simulate ? "  (test)" : "");
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
    // Small state indicator for the toggle menu items: a filled accent dot when ON, a dim hollow ring
    // when OFF — so an inactive toggle still shows a (greyed) marker instead of nothing (discussion #9).
    private static Image StateDot(bool on)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(3, 3, 9, 9);
        if (on) { using var b = new SolidBrush(Theme.Accent); g.FillEllipse(b, r); }
        else { using var p = new Pen(Color.FromArgb(0x6A, 0x70, 0x7C), 1.6f); g.DrawEllipse(p, r); }
        return bmp;
    }
    private static void SetDot(ToolStripMenuItem it, bool on) { var old = it.Image; it.Image = StateDot(on); old?.Dispose(); }

    private void BuildMenu()
    {
        _tray.ContextMenuStrip?.Dispose();
        foreach (var im in _menuSwatches) im.Dispose();
        _menuSwatches.Clear();

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("GhostDeck") { Font = new Font("Segoe UI", 9, FontStyle.Bold) });
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

        // (#21) scenes right under the profiles - same one-click spirit
        if (Writable && _settings.Scenes.Count > 0)
        {
            var scenesItem = new ToolStripMenuItem(Lang.T("scene_title"));
            foreach (var s in _settings.Scenes)
            {
                var scene = s;
                var it = new ToolStripMenuItem((scene.Glyph.Length > 0 ? scene.Glyph + "  " : "") + scene.Name);
                it.Click += (_, _) => ApplyScene(scene, ChangeSource.Tray);
                scenesItem.DropDownItems.Add(it);
            }
            menu.Items.Add(scenesItem);
        }

        menu.Items.Add(new ToolStripSeparator());

        _coolerItem = new ToolStripMenuItem(Lang.T("cooler_boost")) { Enabled = Writable, CheckOnClick = false };
        SetDot(_coolerItem, _coolerBoost);
        _coolerItem.Click += (_, _) => ToggleCoolerBoost();
        menu.Items.Add(_coolerItem);

        _overlayItem = new ToolStripMenuItem(Lang.T("overlay_title")) { CheckOnClick = false };
        SetDot(_overlayItem, OverlayVisible);
        _overlayItem.Click += (_, _) => ToggleOverlay();
        menu.Items.Add(_overlayItem);

        _overlayLockItem = new ToolStripMenuItem(Lang.T("ov_lock_menu")) { CheckOnClick = false };
        SetDot(_overlayLockItem, _settings.OverlayClickThrough);
        _overlayLockItem.Click += (_, _) => ToggleOverlayLock();
        menu.Items.Add(_overlayLockItem);

        menu.Items.Add(new ToolStripSeparator());

        var panel = new ToolStripMenuItem(Lang.T("menu_panel"));
        panel.Click += (_, _) => OpenMain(MainTab.Scenarios);
        menu.Items.Add(panel);

        if (_settings.TrayShowStatus)
        {
            var status = new ToolStripMenuItem(Lang.T("menu_status"));
            status.Click += (_, _) => OpenMain(MainTab.Status);
            menu.Items.Add(status);
        }

        if (_settings.TrayShowFanCurve)
        {
            var curve = new ToolStripMenuItem(Lang.T("fc_title"));
            // With saved presets the entry becomes a submenu (editor + quick preset switch);
            // without any it stays the plain "open the editor" click it always was.
            if (Writable && _device?.FanCurve != null && _settings.CurvePresets.Count > 0)
            {
                var open = new ToolStripMenuItem(Lang.T("fc_open_editor"));
                open.Click += (_, _) => OpenMain(MainTab.FanCurve);
                curve.DropDownItems.Add(open);
                curve.DropDownItems.Add(new ToolStripSeparator());
                var auto = new ToolStripMenuItem(Lang.T("fc_preset_auto"));
                auto.Click += (_, _) => ApplyPresetFromTray(null);
                curve.DropDownItems.Add(auto);
                foreach (var p in _settings.CurvePresets)
                {
                    string name = p.Name;
                    var it = new ToolStripMenuItem(name);
                    it.Click += (_, _) => ApplyPresetFromTray(name);
                    curve.DropDownItems.Add(it);
                }
            }
            else curve.Click += (_, _) => OpenMain(MainTab.FanCurve);
            menu.Items.Add(curve);
        }

        // Settings sits right after Fan curve, mirroring the main window's tab order (discussion #9).
        var settings = new ToolStripMenuItem(Lang.T("menu_settings"));
        settings.Click += (_, _) => OpenMain(MainTab.Settings);
        menu.Items.Add(settings);

        if (_settings.TrayShowModels)
        {
            var models = new ToolStripMenuItem(Lang.T("tab_models"));
            models.Click += (_, _) => OpenMain(MainTab.Models);
            menu.Items.Add(models);
        }

        // Grouped "Report / verify" submenu: my model (profiles) + fan curve.
        if (_settings.TrayShowReport)
        {
            var report = new ToolStripMenuItem(Lang.T("tray_report"));
            var reportModel = new ToolStripMenuItem(Lang.T("tray_report_model"));
            reportModel.Click += (_, _) => OpenReportTab(0);
            var reportCurve = new ToolStripMenuItem(Lang.T("tray_report_curve"));
            reportCurve.Click += (_, _) => OpenReportTab(1);
            report.DropDownItems.Add(reportModel);
            report.DropDownItems.Add(reportCurve);
            menu.Items.Add(report);
        }

        if (_settings.TrayShowFeedback)
        {
            var feedback = new ToolStripMenuItem(Lang.T("menu_feedback"));
            feedback.Click += (_, _) => OpenFeedback();
            menu.Items.Add(feedback);
        }

        var langMenu = new ToolStripMenuItem(Lang.T("menu_language"));
        for (int i = 0; i < Lang.Names.Length; i++)
        {
            string code = Lang.Codes[i];
            var li = new ToolStripMenuItem(Lang.Names[i]) { Checked = code == Lang.CurrentCode };
            li.Click += (_, _) => ChangeLanguage(code);
            langMenu.DropDownItems.Add(li);
        }
        menu.Items.Add(langMenu);

        // Change log goes after Language (discussion #9).
        if (_settings.TrayShowChangeLog)
        {
            var log = new ToolStripMenuItem(Lang.T("menu_log"));
            log.Click += (_, _) => LogForm.ShowSingleton();
            menu.Items.Add(log);
        }

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
        // (#23) Left and middle click run whatever the user picked in Settings → System → Tray.
        // Right click stays the context menu (handled by NotifyIcon itself).
        if (e.Button == MouseButtons.Left) RunTrayAction((TrayAction)_settings.TrayClickLeft);
        else if (e.Button == MouseButtons.Middle) RunTrayAction((TrayAction)_settings.TrayClickMiddle);
    }

    private void RunTrayAction(TrayAction a)
    {
        switch (a)
        {
            case TrayAction.CycleProfile: if (Writable) Cycle(ChangeSource.Tray); else ShowState(); break;
            case TrayAction.FanBoost: ToggleCoolerBoost(); break;
            case TrayAction.Overlay: ToggleOverlay(); break;
            case TrayAction.ShowState: ShowState(); break;
            case TrayAction.PanicReset: PanicReset(); break;
            case TrayAction.OpenScenarios: OpenMain(MainTab.Scenarios); break;
            case TrayAction.OpenStatus: OpenMain(MainTab.Status); break;
            case TrayAction.OpenFanCurve: OpenMain(MainTab.FanCurve); break;
            case TrayAction.OpenSettings: OpenMain(MainTab.Settings); break;
            case TrayAction.OpenModels: OpenMain(MainTab.Models); break;
            case TrayAction.OpenChangeLog: LogForm.ShowSingleton(); break;
        }
    }

    // ---------------- tray wheel (#23) ----------------
    // The hook only exists while a wheel mode is selected; "None" removes it entirely.
    private void ApplyTrayWheel()
    {
        bool want = _settings.TrayWheelMode != (int)TrayWheelMode.None && _ui != null;
        if (want && _wheel == null) _wheel = new TrayWheel(_tray, _ui!, OnTrayWheel);
        else if (!want && _wheel != null) { _wheel.Dispose(); _wheel = null; }
    }

    private void OnTrayWheel(int delta)
    {
        _wheelAccum += delta;
        int steps = _wheelAccum / 120;
        if (steps == 0) return;
        _wheelAccum -= steps * 120;
        switch ((TrayWheelMode)_settings.TrayWheelMode)
        {
            case TrayWheelMode.Profiles: WheelProfileStep(steps); break;
            case TrayWheelMode.Scenes: WheelSceneStep(steps); break;
            case TrayWheelMode.KbdLight: WheelKbdStep(steps); break;
        }
    }

    // Fast spins are coalesced: each notch only moves the previewed target (OSD), and the
    // selection is committed once, when the wheel rests - a 4-notch spin is one apply, not four.
    private void ArmWheelCommit(Action commit)
    {
        _wheelCommit = commit;
        if (_wheelTimer == null)
        {
            _wheelTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _wheelTimer.Tick += (_, _) =>
            {
                _wheelTimer!.Stop();
                var c = _wheelCommit;
                _wheelCommit = null;
                c?.Invoke();
            };
        }
        _wheelTimer.Stop();
        _wheelTimer.Start();
    }

    // Wheel up = next profile, wheel down = previous.
    private void WheelProfileStep(int steps)
    {
        if (!Writable) { ShowState(); return; }
        int n = Profiles.Order.Length;
        int i = Array.IndexOf(Profiles.Order, _wheelTarget ?? _current);
        var next = Profiles.Order[((i + steps) % n + n) % n];
        _wheelTarget = next;
        ShowOsd(next);
        ArmWheelCommit(() =>
        {
            if (_wheelTarget is { } t)
            {
                _wheelTarget = null;
                if (t != _current) SetProfile(t, osd: true, ChangeSource.Tray);
            }
        });
    }

    // (#21) Wheel through the scene list; the previewed scene is applied when the wheel rests.
    private void WheelSceneStep(int steps)
    {
        var list = _settings.Scenes;
        if (!Writable || list.Count == 0) { ShowState(); return; }
        int n = list.Count;
        _wheelSceneIdx = _wheelSceneIdx < 0
            ? (steps > 0 ? 0 : n - 1)
            : ((_wheelSceneIdx + steps) % n + n) % n;
        var s = list[_wheelSceneIdx];
        _osd.ShowProfile("MSI  ·  " + Lang.T("scene_title"), s.Name, _settings.ColorFor(_current));
        ArmWheelCommit(() =>
        {
            int idx = _wheelSceneIdx;
            _wheelSceneIdx = -1;
            if (idx >= 0 && idx < _settings.Scenes.Count) ApplyScene(_settings.Scenes[idx], ChangeSource.Tray);
        });
    }

    // (#26) Backlight is a single cheap byte - applied per notch, no coalescing needed.
    private void WheelKbdStep(int steps)
    {
        int cur = KbdLevel();
        if (cur < 0) { ShowState(); return; }
        int next = Math.Clamp(cur + steps, 0, 3);
        if (next != cur) SetKbdLight(next, ChangeSource.Tray);
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
    // applyCurve=false skips the per-profile curve preset: panic reset wants stock fans, and
    // internal switches that write their own curve right after (fan-curve editor) don't need it.
    private void SetProfile(ProfileId id, bool osd, ChangeSource source, bool count = true, bool applyCurve = true)
    {
        if (!Writable) { ShowState(); return; }
        try
        {
            if (_simulate)
                ChangeLog.Add(source, $"{Profiles.Get(id).Label}  ·  {RecipeStr(id)}", "(simulate)");
            else
            {
                ApplyRecipeLogged(id, source);
                if (applyCurve) ApplyAssignedCurve(id);
            }
            if (id != _current && count) _switches++;
            _current = id;
            _profileSince = DateTime.Now;
            // remember the deliberate choice (external syncs don't land here) for the
            // startup / resume restore option
            if (_settings.LastProfile != id.ToString()) { _settings.LastProfile = id.ToString(); _settings.Save(); }
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

    // ---------------- fan-curve presets ----------------
    // Per-profile preset, applied right after the profile recipe. Never for Silent (its power
    // cap shares the fan byte 0xD4 with the curve mode) and never on ExternalSync (a profile
    // set by MSI Center is not ours to re-style). Runs only inside SetProfile, i.e. under the
    // same Writable gate as the recipe itself.
    private void ApplyAssignedCurve(ProfileId id)
    {
        if (id == ProfileId.Silent || _device?.FanCurve is not { } fc) return;
        if (!_settings.ProfileCurves.TryGetValue(Profiles.Get(id).Key, out var name) || string.IsNullOrEmpty(name)) return;
        var p = _settings.FindPreset(name);
        if (p == null || !p.IsValid(fc.Points)) return;
        try
        {
            Ec.WriteFanCurve(_device!, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);
            Ec.SetFanMode(_device!, fc.AdvancedModeValue);
            _settings.RecordActiveCurve(name, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);   // (#49)
            ChangeLog.Add(ChangeSource.FanCurve,
                string.Format(Lang.T("log_curve_preset"), name),
                $"{_device!.FanMode:X2}={fc.AdvancedModeValue:X2}");
        }
        catch { }   // curve is cosmetic on top of the recipe; a failed write must not fail the switch
    }

    // Tray quick-switch: apply a named preset (or null = back to the profile's stock fans).
    // Scenes (#21) reuse this with osd: false, under their own single toast.
    private void ApplyPresetFromTray(string? name, bool osd = true)
    {
        if (!Writable || _device?.FanCurve is not { } fc) { ShowState(); return; }
        try
        {
            if (name == null)
            {
                byte b = _current == ProfileId.Silent ? _device!.FanSilentValue : (byte)0x0D;
                if (!_simulate) Ec.SetFanMode(_device!, b);
                _settings.ClearActiveCurve();   // (#49) back to profile fans = nothing to restore
                ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_curve_off"), $"{_device!.FanMode:X2}={b:X2}");
                if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("fc_title"), Lang.T("fc_preset_auto"), _settings.ColorFor(_current));
                return;
            }
            var p = _settings.FindPreset(name);
            if (p == null || !p.IsValid(fc.Points)) return;
            // A curve in Silent drops the Silent cap (same EC byte) -> leave Silent for Balanced first.
            if (_current == ProfileId.Silent)
                SetProfile(ProfileId.Balanced, osd: false, ChangeSource.Tray, applyCurve: false);
            if (!_simulate)
            {
                Ec.WriteFanCurve(_device!, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);
                Ec.SetFanMode(_device!, fc.AdvancedModeValue);
            }
            _settings.RecordActiveCurve(p.Name, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);   // (#49)
            ChangeLog.Add(ChangeSource.FanCurve,
                string.Format(Lang.T("log_curve_preset"), p.Name),
                $"{_device!.FanMode:X2}={fc.AdvancedModeValue:X2}");
            if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("fc_title"), p.Name, _settings.ColorFor(_current));
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    // ---------------- keyboard backlight (#26) ----------------
    // Level 0-3 (off/low/mid/high); the register is read back on demand, so a change made with
    // the laptop's own Fn key stays in sync with what the brick / hotkey shows next.
    private int KbdLevel()
    {
        if (_kbdAddr == 0 || !Writable) return -1;
        if (_simulate) return _kbdSim;
        try { return Ec.GetKbdBacklight(_kbdAddr); } catch { return -1; }
    }

    private void SetKbdLight(int level, ChangeSource source, bool osd = true)
    {
        if (_kbdAddr == 0 || !Writable) { ShowState(); return; }
        level = Math.Clamp(level, 0, 3);
        try
        {
            if (_simulate) _kbdSim = level;
            else Ec.SetKbdBacklight(_kbdAddr, level);
            string name = Lang.T(level switch { 0 => "kbd_off", 1 => "kbd_low", 2 => "kbd_mid", _ => "kbd_high" });
            ChangeLog.Add(source, Lang.T("kbd_title") + ": " + name,
                _simulate ? "(simulate)" : $"{_kbdAddr:X2}={0x80 | level:X2}");
            if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("kbd_title"), name, Color.FromArgb(0x17, 0xC0, 0xEB));
            if (_main is { IsDisposed: false }) _main.RefreshActive();
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    /// <summary>Fn key side: 1 = left, 0 = right, -1 = no fn_win_swap register / not writable.</summary>
    private int FnLeftState()
    {
        if (_fnSwap is not { } fs || !Writable) return -1;
        if (_simulate) return _fnLeftSim ? 1 : 0;
        try { return Ec.GetFnLeft(fs) ? 1 : 0; } catch { return -1; }
    }

    private void SetFnLeftState(bool left, ChangeSource source, bool osd = true)
    {
        if (_fnSwap is not { } fs || !Writable) { ShowState(); return; }
        try
        {
            if (_simulate) _fnLeftSim = left;
            else Ec.SetFnLeft(fs, left);
            string name = Lang.T(left ? "fnswap_left" : "fnswap_right");
            string read = "(simulate)";
            if (!_simulate) { try { read = $"{fs.Addr:X2}={Ec.ReadByte(fs.Addr):X2}"; } catch { read = Lang.T("log_read_fail"); } }
            ChangeLog.Add(source, Lang.T("fnswap_title") + ": " + name, read);
            if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("fnswap_title"), name, Color.FromArgb(0x17, 0xC0, 0xEB));
            if (_main is { IsDisposed: false }) _main.RefreshActive();
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    // ---------------- touchpad ----------------
    // Device-level switch (CM devnode), independent of EC writability - works on any laptop.
    private void ToggleTouchpad()
    {
        int st = Touchpad.State();
        if (st < 0) { ShowState(); return; }
        SetTouchpadState(st != 1, ChangeSource.Hotkey);
    }

    private void SetTouchpadState(bool on, ChangeSource source, bool osd = true)
    {
        try
        {
            Touchpad.Set(on);
            ChangeLog.Add(source, Lang.T("tp_title") + ": " + Lang.T(on ? "st_on" : "st_off"));
            if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("tp_title"),
                Lang.T(on ? "st_on" : "st_off"), on ? Color.FromArgb(0x17, 0xC0, 0xEB) : Color.Gray);
            if (_main is { IsDisposed: false }) _main.RefreshActive();
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    // ---------------- Windows-key lock ----------------
    // Software feature (LL keyboard hook), independent of EC writability - works on any laptop.
    private void ToggleWinLock() => SetWinLockState(!_winLock.Enabled, ChangeSource.Hotkey);

    private void SetWinLockState(bool on, ChangeSource source, bool osd = true)
    {
        if (on == _winLock.Enabled) return;
        _winLock.Set(on);
        ChangeLog.Add(source, Lang.T("winlock_title") + ": " + Lang.T(on ? "st_on" : "st_off"));
        if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("winlock_title"),
            Lang.T(on ? "st_on" : "st_off"), on ? Color.FromArgb(0x17, 0xC0, 0xEB) : Color.Gray);
        if (_main is { IsDisposed: false }) _main.RefreshActive();
    }

    // Hotkey: cycle like the Fn key does (off -> low -> mid -> high -> off).
    private void CycleKbdLight()
    {
        int cur = KbdLevel();
        if (cur < 0) { ShowState(); return; }
        SetKbdLight((cur + 1) % 4, ChangeSource.Hotkey);
    }

    // ---------------- scenes (#21) ----------------
    // One click applies every field the scene defines, in a deliberate order: profile first
    // (its recipe rewrites the fan byte), then the curve, then everything independent of the
    // EC fan state. Sub-steps run with osd: false - the scene shows ONE toast at the end.
    private void ApplyScene(SceneDef s, ChangeSource source)
    {
        if (!Writable) { ShowState(); return; }
        try
        {
            if (s.Profile is { } pn && Enum.TryParse<ProfileId>(pn, out var pid))
                SetProfile(pid, osd: false, source, applyCurve: s.CurvePreset == null);
            if (s.CurvePreset is { } cp)
                ApplyPresetFromTray(cp.Length == 0 ? null : cp, osd: false);
            if (s.FanBoost is { } fb && fb != _coolerBoost)
                SetCoolerBoostState(fb, osd: false);
            if (s.ChargeLimit is { } cl)
            {
                if (_settings.ChargeLimit != cl) { _settings.ChargeLimit = cl; _settings.Save(); }
                TryApplyChargeLimit();
            }
            if (s.RefreshHz is { } hz && hz > 0)
            {
                int before = Display.Current();
                if (before != hz && Display.SetRefresh(hz))
                    ChangeLog.Add(ChangeSource.Display, $"{before} Hz → {hz} Hz");
            }
            if (s.BrightnessPct is { } bp && Brightness.Supported)
            {
                try { Brightness.Set(bp); ChangeLog.Add(source, Lang.T("bri_title") + ": " + bp + " %"); } catch { }
            }
            if (s.Hdr is { } hd && Hdr.Supported() && Hdr.Enabled() != hd)
            {
                if (Hdr.Set(hd)) ChangeLog.Add(source, "HDR: " + Lang.T(hd ? "st_on" : "st_off"));
            }
            if (s.Overlay is { } ov && ov != OverlayVisible) SetOverlay(ov, osd: false);
            if (s.KbdLight is { } kl) SetKbdLight(kl, source, osd: false);
            if (s.Webcam is { } wc && _webcamSupported && wc != _webcamOn) SetWebcamState(wc, source, osd: false);
            if (s.WinLock is { } wl) SetWinLockState(wl, source, osd: false);
            if (s.Touchpad is { } tp && Touchpad.State() is >= 0 and var tst && (tst == 1) != tp)
                SetTouchpadState(tp, source, osd: false);

            ChangeLog.Add(ChangeSource.Scene, string.Format(Lang.T("log_scene"), s.Name), s.Summary());
            // no glyph in the OSD title: the OSD's text renderer has no emoji fallback (tofu)
            _osd.ShowProfile("MSI  ·  " + s.Name, Lang.T("scene_applied"), _settings.ColorFor(_current));
            if (_main is { IsDisposed: false }) _main.RefreshActive();
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    // ---------------- webcam (#27) ----------------
    // Soft switch = the same EC bit the Fn camera key flips (device drops off the USB bus).
    // The hard block is a separate Settings-only option; while it is on, this switch (and Fn)
    // cannot re-enable the camera, so turning ON warns instead of silently failing.
    private void ToggleWebcam() => SetWebcamState(!_webcamOn, ChangeSource.Hotkey);

    private void SetWebcamState(bool on, ChangeSource source, bool osd = true)
    {
        if (!_webcamSupported || !Writable) { ShowState(); return; }
        try
        {
            if (!_simulate)
            {
                if (on && Ec.GetWebcamBlock())
                {
                    _osd.ShowProfile("MSI  ·  " + Lang.T("webcam_title"), Lang.T("webcam_blocked_warn"), Theme.Amber);
                    return;
                }
                Ec.SetWebcam(on);
            }
            _webcamOn = on;
            string read = "(simulate)";
            if (!_simulate) { try { read = $"2E={Ec.ReadByte(0x2E):X2}"; } catch { read = Lang.T("log_read_fail"); } }
            ChangeLog.Add(source, Lang.T("webcam_title") + ": " + Lang.T(on ? "st_on" : "st_off"), read);
            if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("webcam_title"),
                Lang.T(on ? "st_on" : "st_off"), on ? Color.FromArgb(0x17, 0xC0, 0xEB) : Color.Gray);
            if (_main is { IsDisposed: false }) _main.RefreshActive();
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    // Advanced privacy option (Settings → System): locks the camera off below the Fn key.
    private void SetWebcamBlockState(bool blocked)
    {
        if (!_webcamSupported || !Writable) { ShowState(); return; }
        try
        {
            if (!_simulate)
            {
                Ec.SetWebcamBlock(blocked);
                if (blocked) { Ec.SetWebcam(false); _webcamOn = false; }   // block implies off
            }
            else if (blocked) _webcamOn = false;
            string read = "(simulate)";
            if (!_simulate) { try { read = $"2F={Ec.ReadByte(0x2F):X2}"; } catch { read = Lang.T("log_read_fail"); } }
            ChangeLog.Add(ChangeSource.Panel, Lang.T("webcam_block") + ": " + Lang.T(blocked ? "st_on" : "st_off"), read);
            _osd.ShowProfile("MSI  ·  " + Lang.T("webcam_title"),
                Lang.T(blocked ? "webcam_blocked" : "webcam_unblocked"), blocked ? Theme.Amber : Color.FromArgb(0x17, 0xC0, 0xEB));
            if (_main is { IsDisposed: false }) _main.RefreshActive();
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    // ---------------- cooler boost (max fans) ----------------
    private void ToggleCoolerBoost() => SetCoolerBoostState(!_coolerBoost);

    // (#51) Optional auto-off timer: Fan Boost is the one control users forget to switch back,
    // so it can hand itself back to the profile after N minutes. Started whenever boost goes ON
    // (tray, hotkey, Scenarios brick, CLI) and cancelled on any OFF - including a panic reset.
    private System.Windows.Forms.Timer? _boostTimer;

    private void ArmBoostTimer(bool on, int? overrideSeconds = null)
    {
        _boostTimer?.Stop();
        _boostTimer?.Dispose();
        _boostTimer = null;
        int cfg = overrideSeconds ?? _settings.FanBoostSeconds;   // per-call value: CLI --fanboost on <s>
        if (!on || cfg <= 0) return;
        int seconds = Math.Clamp(cfg, 10, 7200);
        _boostTimer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        _boostTimer.Tick += (_, _) =>
        {
            ArmBoostTimer(false);
            if (_coolerBoost) SetCoolerBoostState(false, auto: true);
        };
        _boostTimer.Start();
    }

    // auto = the boost timer fired (#51); it only changes the on-screen wording.
    // osd = false lets a scene (#21) flip boost silently under its own single toast.
    private void SetCoolerBoostState(bool next, bool auto = false, bool osd = true)
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
            ArmBoostTimer(next);   // (#51) start the auto-off countdown, or cancel it on OFF
            ChangeLog.Add(ChangeSource.CoolerBoost,
                Lang.T("cooler_boost") + ": " + (next ? Lang.T("st_on") : Lang.T("st_off"))
                    + (auto ? "  ·  " + Lang.T("fb_auto_off") : ""),
                read);
            if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("cooler_boost"),
                auto ? Lang.T("fb_auto_off") : Lang.T(next ? "cooler_boost_on" : "cooler_boost_off"),
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
        if (_coolerItem is { } it && !it.IsDisposed) SetDot(it, _coolerBoost);
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
        UpdateFpsActive();
        if (osd) _osd.ShowProfile("MSI  ·  " + Lang.T("overlay_title"),
            Lang.T(on ? "st_on" : "st_off"), Color.FromArgb(0x17, 0xC0, 0xEB));
    }

    // Re-read overlay options after the user edits them in Settings.
    private void ApplyOverlaySettings() { _overlay?.ApplySettings(); UpdateOverlayMenu(); UpdateFpsActive(); }

    // The ETW-based FPS monitor runs only while someone is looking: the overlay with an FPS metric
    // enabled, or the Status → Gaming sub-tab. Off otherwise — zero idle cost by design.
    private void UpdateFpsActive()
    {
        bool overlayWants = OverlayVisible &&
            (_settings.HasMetric(OverlayMetric.Fps) || _settings.HasMetric(OverlayMetric.FrameTime));
        FpsMonitor.SetActive(overlayWants || _statusWantsFps);
    }

    // Game-session summary: the FPS side comes from the monitor; the EC side (temps / fan RPM /
    // profile) is read out of the HW-history ring for the session's timespan — the combination
    // only GhostDeck has. Raised on a worker thread → marshal the UI bits.
    private void OnGameSession(GameSession s)
    {
        int maxCpu = 0, maxGpu = 0; long rpmCpu = 0, rpmGpu = 0; int rpmN = 0;
        var prof = new Dictionary<ProfileId, int>();
        foreach (var h in HwHistory.Window(DateTime.Now - s.Start))
        {
            if (h.Time < s.Start || h.Time > s.End) continue;
            maxCpu = Math.Max(maxCpu, h.CpuTemp);
            maxGpu = Math.Max(maxGpu, h.GpuTemp);
            if (h.CpuRpm > 0 || h.GpuRpm > 0) { rpmCpu += h.CpuRpm; rpmGpu += h.GpuRpm; rpmN++; }
            prof[h.Profile] = prof.GetValueOrDefault(h.Profile) + 1;
        }
        var full = s with
        {
            MaxCpuTemp = maxCpu, MaxGpuTemp = maxGpu,
            AvgCpuRpm = rpmN > 0 ? (int)(rpmCpu / rpmN) : 0,
            AvgGpuRpm = rpmN > 0 ? (int)(rpmGpu / rpmN) : 0,
            Profile = prof.Count > 0 ? Profiles.Get(prof.MaxBy(kv => kv.Value).Key).Label : "",
        };
        FpsMonitor.LastSession = full;
        GameSessions.Add(full, _settings.GameSessionKeep);   // persisted picker on Status → Gaming
        _ui?.Post(_ =>
        {
            string dur = FmtDur(full.End - full.Start);
            string text = string.Format(Lang.T("gm_sess_text"), dur, full.AvgFps, full.P1LowFps);
            if (full.MaxCpuTemp > 0) text += string.Format(Lang.T("gm_sess_ec"), full.MaxCpuTemp);
            ChangeLog.Add(ChangeSource.Game, full.Process + "  ·  " + text);
            if (_settings.SessionPopupEnabled) ShowSessionReport(full);
            if (_main is { IsDisposed: false }) _main.RefreshActive();
        }, null);
    }

    // ---- profile restore around sleep/hibernation ----
    // Observed on the GE78HX: the EC sometimes comes back from S3/S4 in Super Battery on its own
    // (no MSI software running). The poll's external sync would faithfully ADOPT that, so instead
    // we remember the profile at suspend and re-assert it a few seconds after resume (opt-in).
    private ProfileId? _profileBeforeSleep;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _profileBeforeSleep = Writable ? _current : null;
            return;
        }
        if (e.Mode != PowerModes.Resume) return;
        bool wantProfile = _settings.RestoreProfileOnResume && !_settings.AutoSwitchEnabled && _profileBeforeSleep is { };
        bool wantCurve = _settings.RestoreCurveOnResume && _settings.CurveActive;   // (#49)
        bool wantSchedule = _settings.ScheduleEnabled;
        // Poll would otherwise run the schedule check ~3 s after wake - before the EC settled
        // and BEFORE the restore below, which would then overwrite the scheduled scene.
        _scheduleHoldUntil = Environment.TickCount64 + 8000;
        if (!wantProfile && !wantCurve && !wantSchedule) return;
        var want = _profileBeforeSleep;
        _ui?.Post(_ =>
        {
            // the EC needs a moment after wake; one delayed shot on the UI thread
            var t = new System.Windows.Forms.Timer { Interval = 6000 };
            t.Tick += (_, _) =>
            {
                t.Stop();
                t.Dispose();
                if (wantProfile && AutoWritable && !_settings.AutoSwitchEnabled && want is { } w)
                    SetProfile(w, osd: true, ChangeSource.Restore, count: false);
                TryRestoreCurve();   // after the profile, so its recipe can't overwrite the fan mode
                CheckSchedule();     // last, so a window crossed during sleep outranks the restore
            };
            t.Start();
        }, null);
    }

    // (#49) The EC cold-boots into its factory fan mode, losing any custom curve; some machines
    // do the same out of hibernation. Opt-in: re-assert the last ACTIVE curve at startup and
    // after resume, once the profile logic has settled. Same gates as every automatic write.
    private void TryRestoreCurve()
    {
        if (!_settings.RestoreCurveOnResume || !_settings.CurveActive) return;
        if (!AutoWritable || _simulate || _device?.FanCurve is not { } fc) return;
        if (_current == ProfileId.Silent) return;   // a curve would drop Silent's power cap (shared byte)
        var s = _settings;
        if (s.CurveCpuTemp.Length != fc.Points || s.CurveCpuSpeed.Length != fc.Points) return;
        if (!fc.SingleFan && (s.CurveGpuTemp.Length != fc.Points || s.CurveGpuSpeed.Length != fc.Points)) return;
        try
        {
            Ec.WriteFanCurve(_device!, s.CurveCpuTemp, s.CurveCpuSpeed, s.CurveGpuTemp, s.CurveGpuSpeed);
            Ec.SetFanMode(_device!, fc.AdvancedModeValue);
            ChangeLog.Add(ChangeSource.Restore,
                string.Format(Lang.T("log_curve_restore"), s.CurveName.Length > 0 ? s.CurveName : Lang.T("fc_custom")),
                $"{_device!.FanMode:X2}={fc.AdvancedModeValue:X2}");
        }
        catch { }   // best-effort: a refused write must not disturb startup/resume
    }

    // The custom borderless report popup replaced the plain tray balloon (user request).
    private SessionReportForm? _report;
    private void ShowSessionReport(GameSession s)
    {
        try
        {
            if (_report is { IsDisposed: false }) _report.Close();
            _report = new SessionReportForm(s, () =>
            {
                OpenMain(MainTab.Status);
                if (_main is { IsDisposed: false }) _main.ShowStatusGaming();
            }, _settings.SessionPopupSeconds);
            _report.FormClosed += (_, _) => _report = null;
            _report.Show();
        }
        catch { }
    }

    private static string FmtDur(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min"
        : $"{t.Seconds} s";

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
        if (_overlayItem is { } it && !it.IsDisposed) SetDot(it, OverlayVisible);
        if (_overlayLockItem is { } lk && !lk.IsDisposed) SetDot(lk, _settings.OverlayClickThrough);
        if (_main is { IsDisposed: false }) _main.RefreshActive();
    }

    // Hardware readings for the UI: the EC when we have it, otherwise (#48) the vendor WMI
    // data blocks - temperatures only, everything else stays zero.
    private HwSnapshot ReadHwOrTelemetry()
    {
        if (Known && Ec.TryReadHw(_device!, out var hw)) return hw;
        if (_telemetryOnly)
        {
            var t = MsiTelemetry.Read();
            return new HwSnapshot(t.CpuTemp, t.GpuTemp, 0, 0, 0, _firmware);
        }
        return new HwSnapshot(0, 0, 0, 0, 0, _firmware);
    }

    // Snapshot for the overlay: EC hardware + OS metrics + the active profile/cooler state.
    // null = the EC read was refused this tick; the overlay keeps showing its previous sample.
    private OverlaySample? BuildOverlaySample()
    {
        HwSnapshot hw;
        if (Known) { if (!Ec.TryReadHw(_device!, out hw)) return null; }
        else hw = ReadHwOrTelemetry();
        int load = SysInfo.CpuUsage();
        var (ramPct, _, ramUsed) = SysInfo.Ram();
        var ps = SystemInformation.PowerStatus;
        int batt = ps.BatteryLifePercent is >= 0f and <= 1f ? (int)Math.Round(ps.BatteryLifePercent * 100) : -1;
        bool charging = ps.PowerLineStatus == PowerLineStatus.Online && (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0;
        return new OverlaySample(
            Known, Writable,
            Profiles.Get(_current).Label, _settings.ColorFor(_current), _coolerBoost,
            hw.CpuTemp, hw.GpuTemp, hw.CpuRpm, hw.GpuRpm, hw.CpuFan, hw.GpuFan,
            // overlay shows the app-managed limit: OFF unless we actively enforce 60/80/100
            // (the EC byte keeps the last value even when the app stops managing it)
            load, ramPct, ramUsed, _settings.ChargeLimit is 60 or 80 or 100 ? _settings.ChargeLimit : 0, batt, charging,
            Perf.GpuUsage(), Perf.VramUsedMb(), Perf.CpuClockMhz(),
            FpsMonitor.Current?.Fps ?? -1, FpsMonitor.Current?.FrameTimeMs ?? -1,
            Perf.DiskTemps2().First, Perf.BatteryMinutesLeft(), Perf.DiskTemps2().Second);
    }

    private void ShowOsd(ProfileId id)
    {
        var def = Profiles.Get(id);
        _osd.ShowProfile("MSI  ·  " + def.Label, Lang.T(def.SubKey), _settings.ColorFor(id));
    }

    private void UpdateUi(ProfileId id)
    {
        TrayIconFactory.Style = _settings.IconStyle;   // follow the Settings icon-style choice
        _osd.HoldSeconds = _settings.OsdSeconds;       // follow the OSD display-time choice
        var color = Writable ? _settings.ColorFor(id) : Color.Gray;
        var newIcon = TrayIconFactory.Create(color);
        _tray.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
        _trayBase = Writable ? "GhostDeck · " + Profiles.Get(id).Label : "GhostDeck · " + DeviceDescriptor();
        UpdateTrayText();

        if (_tray.ContextMenuStrip is { } menu)
            foreach (var it in menu.Items)
                if (it is ToolStripMenuItem mi && mi.Tag is ProfileId pid)
                    mi.Checked = Writable && pid == id;

        if (_main is { IsDisposed: false })
        {
            var appIcon = TrayIconFactory.AppIcon();
            if (!ReferenceEquals(_main.Icon, appIcon)) _main.Icon = appIcon;   // follows an icon-style change
            _main.SyncStrip();                                                 // follows a tabs-as-icons change
            _main.RefreshActive();
        }
    }

    // ---------------- hotkeys ----------------
    private void ApplyHotkeys()
    {
        _hotkeys.UnregisterAll();
        Reg("Overlay", ToggleOverlay);       // read-only, so both work even when EC writes are disabled
        Reg("OverlayLock", ToggleOverlayLock);
        Reg("EcView", ShowEcViewer);         // live EC dump viewer - read-only diagnostics
        Reg("WinLock", ToggleWinLock);       // software hook, no EC needed
        if (Touchpad.Present()) Reg("Touchpad", ToggleTouchpad);   // devnode switch, no EC needed
        if (!Writable) return;
        Reg("Silent", () => SetProfile(ProfileId.Silent, true, ChangeSource.Hotkey));
        Reg("Balanced", () => SetProfile(ProfileId.Balanced, true, ChangeSource.Hotkey));
        Reg("Extreme", () => SetProfile(ProfileId.Extreme, true, ChangeSource.Hotkey));
        Reg("SuperBattery", () => SetProfile(ProfileId.SuperBattery, true, ChangeSource.Hotkey));
        Reg("Cycle", () => Cycle(ChangeSource.Hotkey));
        Reg("CoolerBoost", ToggleCoolerBoost);
        Reg("PanicReset", PanicReset);
        if (_kbdAddr != 0) Reg("KbdLight", CycleKbdLight);   // (#26) only when the model has the register
        if (_webcamSupported) Reg("Webcam", ToggleWebcam);   // (#27)
        foreach (var s in _settings.Scenes)                  // (#21) per-scene hotkeys ("Scene:<id>")
        {
            var scene = s;
            Reg(scene.HotkeyKey, () => ApplyScene(scene, ChangeSource.Hotkey));
        }
    }

    // Live EC viewer (Ctrl+Shift+E; also a button in the Ctrl+Shift+T test dialog): read-only
    // 256-byte dump with change highlighting - lets an owner see which register reacts to an
    // Fn key without diffing diagnostic zips.
    private void ShowEcViewer()
    {
        if (_simulate || (!Known && !_telemetryOnly && string.IsNullOrEmpty(_firmware))) { ShowState(); return; }
        EcViewForm.ShowSingleton();
    }

    // "Panic" hotkey: one press back to a safe stock state — Fan Boost off, Balanced profile.
    // The Balanced recipe rewrites the fan-mode byte to auto (0x0D), which also releases a
    // custom fan curve (0x8D) and the Silent cap (0x1D), so no separate fan write is needed.
    private void PanicReset()
    {
        // Software locks lift first - they must release even on hardware where EC writes are off.
        SetWinLockState(false, ChangeSource.Hotkey, osd: false);
        try { if (Touchpad.State() == 0) Touchpad.Set(true); } catch { }   // keyboard-only escape hatch
        if (!Writable) { ShowState(); return; }
        if (!_simulate) { try { Ec.SetCoolerBoost(_device!, false); } catch { } }
        _coolerBoost = false;
        _fanBeforeBoost = null;
        UpdateCoolerBoostMenu();
        SetProfile(ProfileId.Balanced, osd: false, ChangeSource.Hotkey, applyCurve: false);   // panic = stock fans, no preset
        _settings.ClearActiveCurve();   // (#49) panic means "stock state" - don't restore the curve at next boot
        // (#27) stock state includes a working camera: lift the hard block and re-enable the switch
        if (_webcamSupported && !_simulate) { try { Ec.SetWebcamBlock(false); Ec.SetWebcam(true); _webcamOn = true; } catch { } }
        ChangeLog.Add(ChangeSource.Hotkey, Lang.T("hk_panic") + "  ·  " + Lang.T("panic_sub"));
        _osd.ShowProfile("MSI  ·  " + Lang.T("hk_panic"), Lang.T("panic_sub"), Theme.Amber);
    }

    private void Reg(string key, Action action)
    {
        if (_settings.HotkeysEnabled && _settings.Hotkeys.TryGetValue(key, out var hd) && hd.IsSet && hd.Enabled)
            _hotkeys.Register(hd.Mods, hd.Vk, action);
    }

    // ---------------- settings / language / status ----------------
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
                                  _switches, DateTime.Now - _profileSince, Autostart.IsEnabled(), AppVersion(),
                                  _telemetryOnly);
        },
        Hw = () => ReadHwOrTelemetry(),
        Current = () => _current,
        SetProfile = id => SetProfile(id, osd: true, ChangeSource.Panel),
        Writable = () => Writable,
        ColorOf = id => _settings.ColorFor(id),
        Firmware = _firmware,
        AppVersion = AppVersion,
        SaveSettings = () => _settings.Save(),
        CheckNoticesNow = CheckNoticesNow,
        SettingsChanged = () =>
        {
            ApplyHotkeys(); BuildMenu(); UpdateUi(_current);
            ApplyTrayWheel();   // (#23) follow a just-edited wheel mode (install or remove the hook)
            // apply a just-edited refresh preference right away (no-op when disabled)
            ApplyRefreshForPower(SystemInformation.PowerStatus.PowerLineStatus);
            GameSessions.ApplyLimit(_settings.GameSessionKeep);   // a lowered keep-count trims at once
        },
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
        SetCoolerBoost = on => SetCoolerBoostState(on),
        KbdLevel = KbdLevel,                                        // (#26) -1 = no support on this model
        SetKbdLevel = l => SetKbdLight(l, ChangeSource.Panel),
        WebcamState = () => !_webcamSupported || !Writable ? -1 : _webcamOn ? 1 : 0,   // (#27)
        SetWebcam = on => SetWebcamState(on, ChangeSource.Panel),
        WebcamBlocked = () =>
        {
            if (!_webcamSupported || !Writable || _simulate) return false;
            try { return Ec.GetWebcamBlock(); } catch { return false; }
        },
        SetWebcamBlock = SetWebcamBlockState,
        FnLeft = FnLeftState,
        SetFnLeft = left => SetFnLeftState(left, ChangeSource.Panel),
        WinLockOn = () => _winLock.Enabled,
        SetWinLock = on => SetWinLockState(on, ChangeSource.Panel),
        TouchpadState = Touchpad.State,
        SetTouchpad = on => SetTouchpadState(on, ChangeSource.Panel),
        OpenScenSettings = () => { OpenMain(MainTab.Settings); _main!.FocusScenVisibility(); },
        RunScene = s => ApplyScene(s, ChangeSource.Panel),          // (#21)
        HasFanCurve = () => _device?.FanCurve != null,
        PanicReset = PanicReset,
        OverlayOn = () => OverlayVisible,
        SetOverlay = on => SetOverlay(on, osd: false),
        ApplyOverlaySettings = ApplyOverlaySettings,
        SnapOverlay = SnapOverlayCorner,
        SetFpsViewer = on => { _statusWantsFps = on; UpdateFpsActive(); },
        UpdateAvail = () => _updateAvail,
        OpenUpdates = tag => { OpenMain(MainTab.Updates); if (_main is { IsDisposed: false }) _main.ShowUpdates(tag); },
        WithEcWrite = act =>
        {
            if (Writable && !_simulate && _device != null)
            {
                try { act(_device); } catch { }
            }
        },
    };

    private MainForm EnsureMain()
    {
        if (_main is { IsDisposed: false }) return _main;
        _main = new MainForm(BuildDeps());
        _main.FormClosed += (_, _) => _main = null;   // real close = app exit; user close only hides
        return _main;
    }

    private void OpenMain(MainTab tab)
    {
        var m = EnsureMain();
        if (m.WindowState == FormWindowState.Minimized) m.WindowState = FormWindowState.Normal;
        m.Show();
        m.ShowTab(tab);
        m.BringToFront();
        m.Activate();
        if (_pendingNotice is { } pn) ShowNoticeBanner(pn);
    }

    private void OpenReport()
    {
        using var form = new ReportForm(_firmware, Known ? _device!.Name : "", AppVersion());
        if (_main is { IsDisposed: false }) form.ShowDialog(_main);
        else form.ShowDialog();
    }

    // Open the in-window Report page on a specific sub-tab (0 = profiles, 1 = fan curve).
    private void OpenReportTab(int sub)
    {
        OpenMain(MainTab.Report);
        if (_main is { IsDisposed: false }) _main.ShowReport(sub);
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
            // Signed model database (ModelDb): same daily cadence and privacy footprint as the
            // update check. A valid, newer file is stored and takes effect on the next start;
            // the change-log line is the only notification (no balloon - nothing is urgent).
            int? db = await ModelDb.FetchUpdateAsync();
            void Apply()
            {
                OnUpdateResult(res);
                OnNoticesResult(notices);
                if (db is { } v) ChangeLog.Add(ChangeSource.Startup, string.Format(Lang.T("log_modeldb"), v));
            }
            if (ui != null) ui.Post(_ => Apply(), null);
            else Apply();
        });
    }

    private void OnUpdateResult(Updater.Result? res)
    {
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.Save();

        if (res is not { } r) return;
        _updateAvail = r;
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
        OpenUrl($"https://github.com/wygodad/ghostdeck/discussions/new?category=ideas&body={body}");
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

    // ---------------- background HW sampler (history + thermal alert) ----------------
    // One EC read per 3 s poll, off the UI thread (same reasoning as the Status page's
    // RefreshAsync). Every sample lands in the local HwHistory ring buffer (Status -> History
    // charts); the thermal alert consumes the same sample when enabled - one read, two features.
    private static readonly TimeSpan ThermalCooldown = TimeSpan.FromMinutes(5);

    private void SampleHw()
    {
        if (!Known || _simulate) return;
        if (Interlocked.Exchange(ref _thermalBusy, 1) != 0) return;
        var dev = _device!;
        var ui = _ui;
        Task.Run(() =>
        {
            try
            {
                if (!Ec.TryReadHw(dev, out var hw)) return;   // refused read = no history sample this tick
                int load = SysInfo.CpuUsage();
                HwHistory.Add(new HwSample(DateTime.Now, (short)hw.CpuTemp, (short)hw.GpuTemp,
                    (short)hw.CpuFan, (short)hw.GpuFan, hw.CpuRpm, hw.GpuRpm, (short)load, _current,
                    (short)FpsMonitor.CurrentFps));
                if (_settings.TempAlertEnabled) ui?.Post(_ => OnThermalSample(hw), null);
            }
            catch { }
            finally { Interlocked.Exchange(ref _thermalBusy, 0); }
        });
    }

    private void OnThermalSample(HwSnapshot hw)
    {
        if (!_settings.TempAlertEnabled) return;
        int worst = Math.Max(hw.CpuTemp, hw.GpuTemp);
        if (worst < _settings.TempAlertDegrees) { _tempOverSince = null; return; }
        var now = DateTime.Now;
        _tempOverSince ??= now;
        if ((now - _tempOverSince.Value).TotalSeconds < _settings.TempAlertSeconds) return;
        if (now - _lastTempAlert < ThermalCooldown) return;
        _lastTempAlert = now;
        string text = string.Format(Lang.T("ta_alert_text"),
            hw.CpuTemp, hw.GpuTemp, _settings.TempAlertDegrees, _settings.TempAlertSeconds);
        _osd.ShowProfile("MSI  ·  " + Lang.T("ta_alert_title"), text, Theme.Red, minSeconds: 5);
        _balloonUrl = null;
        _tray.BalloonTipTitle = Lang.T("ta_alert_title");
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(8000);
        ChangeLog.Add(ChangeSource.Thermal, text);
    }

    // ---------------- display refresh-rate switch (AC/battery, discussion #18) ----------------
    // Pure Windows display API (no EC write), so this runs OUTSIDE the Writable gates and works
    // on every model, including unrecognised firmware.
    private void ApplyRefreshForPower(PowerLineStatus power)
    {
        if (!_settings.RefreshSwitchEnabled || power == PowerLineStatus.Unknown) return;
        int hz = power == PowerLineStatus.Online ? _settings.RefreshOnAC : _settings.RefreshOnBattery;
        if (hz <= 0) return;
        int before = Display.Current();
        if (before == hz) return;
        if (!Display.SetRefresh(hz)) return;
        ChangeLog.Add(ChangeSource.Display, $"{before} Hz → {hz} Hz");
        _osd.ShowProfile("MSI  ·  " + Lang.T("ref_title"), $"{before} Hz → {hz} Hz", Theme.Accent);
    }

    // (#15) Tray tooltip carries Windows' battery-time estimate while discharging.
    // NotifyIcon.Text throws above 127 chars, so the suffix is appended defensively.
    private string _trayBase = "GhostDeck";

    private void UpdateTrayText()
    {
        string text = _trayBase;
        if (Perf.BatteryMinutesLeft() is int m and > 0)
        {
            string t = $"{text} · ~{m / 60} h {m % 60:00} min";
            if (t.Length <= 127) text = t;
        }
        if (_tray.Text != text) _tray.Text = text;
    }

    // ---------------- poll: auto-switch + external sync ----------------
    // ---------------- scene schedule + battery rules ----------------
    // Both engines are EDGE-triggered: they act on transitions (a window begins, a threshold
    // is crossed), never continuously - so a manual change in between is always respected.

    private ScheduleRule? ActiveScheduleRule(DateTime now)
    {
        if (!_settings.ScheduleEnabled) return null;
        foreach (var r in _settings.Schedules)          // list order = priority on overlap
            if (r.Enabled && r.ActiveAt(now)) return r;
        return null;
    }

    // applyNow = startup: apply the currently active window even without a transition.
    private void CheckSchedule(bool applyNow = false)
    {
        var r = ActiveScheduleRule(DateTime.Now);
        string id = r?.Id ?? "";
        string prev = _lastScheduleRule ?? "";
        _lastScheduleRule = id;
        if (id.Length == 0 || (!applyNow && id == prev)) return;
        var sc = _settings.Scenes.FirstOrDefault(s => s.Id.Equals(r!.SceneId, StringComparison.OrdinalIgnoreCase));
        if (sc == null || !AutoWritable) return;        // automatic write - firmware guard applies
        ChangeLog.Add(ChangeSource.Schedule, string.Format(Lang.T("log_schedule"), sc.Name, r!.Start, r.End));
        ApplyScene(sc, ChangeSource.Schedule);
    }

    private void CheckBatteryRules()
    {
        if (!_settings.BattRulesEnabled) return;   // master switch for the whole feature
        if (!_settings.BattLowEnabled && !_settings.BattHighEnabled) return;
        var ps = SystemInformation.PowerStatus;
        if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0) return;
        if (ps.BatteryLifePercent is not (>= 0f and <= 1f)) return;
        int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
        bool online = ps.PowerLineStatus == PowerLineStatus.Online;
        int prev = _lastBattPct;
        _lastBattPct = pct;
        if (prev < 0) return;   // first sample is the baseline

        // re-arm once the level moves 3 pp past the threshold again
        if (_battLowFired && pct >= _settings.BattLowPct + 3) _battLowFired = false;
        if (_battHighFired && pct <= _settings.BattHighPct - 3) _battHighFired = false;

        if (_settings.BattLowEnabled && !online && !_battLowFired
            && prev > _settings.BattLowPct && pct <= _settings.BattLowPct)
        {
            _battLowFired = true;
            ChangeLog.Add(ChangeSource.Battery, string.Format(Lang.T("log_batt_low"), pct, _settings.BattLowPct));
            RunBatteryAction(_settings.BattLowAction);
        }
        else if (_settings.BattHighEnabled && online && !_battHighFired
            && prev < _settings.BattHighPct && pct >= _settings.BattHighPct)
        {
            _battHighFired = true;
            ChangeLog.Add(ChangeSource.Battery, string.Format(Lang.T("log_batt_high"), pct, _settings.BattHighPct));
            RunBatteryAction(_settings.BattHighAction);
        }
    }

    private void RunBatteryAction(string action)
    {
        if (!AutoWritable) return;                      // automatic write - firmware guard applies
        try
        {
            if (action.StartsWith("S:", StringComparison.OrdinalIgnoreCase))
            {
                var sc = _settings.Scenes.FirstOrDefault(s => s.Id.Equals(action[2..], StringComparison.OrdinalIgnoreCase));
                if (sc != null) ApplyScene(sc, ChangeSource.Battery);
            }
            else if (action.StartsWith("P:", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<ProfileId>(action[2..], out var pid))
                SetProfile(pid, osd: true, ChangeSource.Battery);
        }
        catch { }
    }

    private void Poll()
    {
        // Windows is going down: WMI already refuses every call, so stop polling the EC for good.
        if (AppLifecycle.ShuttingDown) { _poll.Stop(); return; }

        SampleHw();   // reads only; also works on non-writable (Experimental locked) models
        UpdateTrayText();   // battery-time suffix follows discharge state (#15)

        // Power-transition actions live BEFORE the Writable gate: the refresh-rate switch is
        // not an EC write and must work on every machine. Profile auto-switch keeps its gates.
        var power = SystemInformation.PowerStatus.PowerLineStatus;
        if (power != PowerLineStatus.Unknown && power != _lastPower)
        {
            ApplyRefreshForPower(power);
            if (AutoWritable && _settings.AutoSwitchEnabled) ApplyForPower(power, osd: true);
            _lastPower = power;
        }

        CheckBatteryRules();   // both engines gate their own writes (AutoWritable inside)
        if (Environment.TickCount64 >= _scheduleHoldUntil) CheckSchedule();

        if (!Writable) return;

        try
        {
            // Cooler Boost may be toggled elsewhere (or cleared by the firmware) — keep the menu in sync.
            bool cb = Ec.GetCoolerBoost(_device!);
            if (cb != _coolerBoost) { _coolerBoost = cb; UpdateCoolerBoostMenu(); }

            // (#27) The Fn camera key flips the same EC bit — keep the Scenarios brick in sync.
            if (_webcamSupported)
            {
                bool wc = Ec.GetWebcam();
                if (wc != _webcamOn) { _webcamOn = wc; if (_main is { IsDisposed: false }) _main.RefreshActive(); }
            }

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
        _wheelTimer?.Stop();
        _wheel?.Dispose();
        _winLock.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        FpsMonitor.Shutdown();   // stop the ETW session (also flushes an open game session)
        _tray.Visible = false;
        _hotkeys.Dispose();
        _main?.Close();
        _overlay?.Close();
        _report?.Close();
        _osd.Dispose();
        _tray.Dispose();
        _currentIcon?.Dispose();
        ExitThread();
    }
}
