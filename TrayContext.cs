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

    private bool Known => _device != null;
    private bool Writable => Known && (_device!.Tier == Tier.Tested || _settings.ExperimentalEnabled);

    public TrayContext()
    {
        _settings = AppSettings.Load();
        _settings.Autostart = Autostart.IsEnabled();
        Lang.Set(_settings.Language);
        Theme.Set(_settings.DarkMode);

        var forced = Environment.GetEnvironmentVariable("MSIPS_FORCE_FIRMWARE");
        _simulate = !string.IsNullOrEmpty(forced);
        _firmware = _simulate ? forced! : Ec.ReadFirmware();
        _device = Devices.Detect(_firmware);
        _current = Known ? Ec.GetCurrent(_device!) : ProfileId.Balanced;

        if (Writable) TryApplyChargeLimit();

        BuildMenu();
        UpdateUi(_current);
        _tray.Visible = true;
        ApplyHotkeys();

        _lastPower = SystemInformation.PowerStatus.PowerLineStatus;
        if (Writable && _settings.AutoSwitchEnabled) ApplyForPower(_lastPower.Value, osd: false);

        _poll.Tick += (_, _) => Poll();
        _poll.Start();

        ShowState();

        _ui = SynchronizationContext.Current;
        _tray.BalloonTipClicked += (_, _) => { if (_updateUrl != null) OpenUrl(_updateUrl); };
        MaybeCheckForUpdates();
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
            item.Click += (_, _) => SetProfile((ProfileId)item.Tag!, osd: true);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var panel = new ToolStripMenuItem(Lang.T("menu_panel"));
        panel.Click += (_, _) => OpenMain(MainTab.Scenarios);
        menu.Items.Add(panel);

        var status = new ToolStripMenuItem(Lang.T("menu_status"));
        status.Click += (_, _) => OpenMain(MainTab.Status);
        menu.Items.Add(status);

        var report = new ToolStripMenuItem(Lang.T("menu_report"));
        report.Click += (_, _) => OpenMain(MainTab.Report);
        menu.Items.Add(report);

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
        if (Writable) Cycle();
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
    private void SetProfile(ProfileId id, bool osd, bool count = true)
    {
        if (!Writable) { ShowState(); return; }
        try
        {
            if (!_simulate) Ec.Apply(_device!.Recipes[id]);
            if (id != _current && count) _switches++;
            _current = id;
            _profileSince = DateTime.Now;
            UpdateUi(id);
            if (osd) ShowOsd(id);
        }
        catch (Exception ex)
        {
            _osd.ShowProfile("MSI  ·  " + Lang.T("err"), ex.Message, Color.Firebrick);
        }
    }

    private void Cycle()
    {
        int i = Array.IndexOf(Profiles.Order, _current);
        SetProfile(Profiles.Order[(i + 1) % Profiles.Order.Length], osd: true);
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
        if (!Writable) return;
        Reg("Silent", () => SetProfile(ProfileId.Silent, true));
        Reg("Balanced", () => SetProfile(ProfileId.Balanced, true));
        Reg("Extreme", () => SetProfile(ProfileId.Extreme, true));
        Reg("SuperBattery", () => SetProfile(ProfileId.SuperBattery, true));
        Reg("Cycle", Cycle);
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
        SetProfile = id => SetProfile(id, osd: true),
        Writable = () => Writable,
        ColorOf = id => _settings.ColorFor(id),
        Firmware = _firmware,
        AppVersion = AppVersion,
        SaveSettings = () => _settings.Save(),
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
            if (on && Writable) ApplyForPower(SystemInformation.PowerStatus.PowerLineStatus, osd: false);
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
            return;
        }
        _main = new MainForm(BuildDeps());
        _main.FormClosed += (_, _) => _main = null;
        _main.Show();
        _main.ShowTab(tab);
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
            if (ui != null) ui.Post(_ => OnUpdateResult(res), null);
            else OnUpdateResult(res);
        });
    }

    private void OnUpdateResult(Updater.Result? res)
    {
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.Save();

        if (res is not { } r) return;
        _updateUrl = r.Url;
        BuildMenu();
        _tray.BalloonTipTitle = Lang.T("update_available");
        _tray.BalloonTipText = string.Format(Lang.T("update_available_text"), r.Tag);
        _tray.ShowBalloonTip(8000);
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
        if (Writable && !_simulate && _settings.ChargeLimit is 60 or 80 or 100)
        {
            try { Ec.SetChargeLimit(_device!, _settings.ChargeLimit); } catch { }
        }
    }

    // ---------------- poll: auto-switch + external sync ----------------
    private void Poll()
    {
        if (!Writable) return;

        var power = SystemInformation.PowerStatus.PowerLineStatus;
        if (power != PowerLineStatus.Unknown && power != _lastPower)
        {
            if (_settings.AutoSwitchEnabled) ApplyForPower(power, osd: true);
            _lastPower = power;
        }

        try
        {
            var actual = Ec.GetCurrent(_device!);
            if (actual != _current)
            {
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
            SetProfile(id, osd);
    }

    private void ExitApp()
    {
        _poll.Stop();
        _tray.Visible = false;
        _hotkeys.Dispose();
        _main?.Close();
        _osd.Dispose();
        _tray.Dispose();
        _currentIcon?.Dispose();
        ExitThread();
    }
}
