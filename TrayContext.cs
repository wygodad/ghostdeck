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
    private ProfileId _current;
    private Icon? _currentIcon;
    private DateTime _profileSince = DateTime.Now;
    private int _switches;
    private PowerLineStatus? _lastPower;
    private StatusForm? _statusForm;

    private bool Known => _device != null;
    private bool Writable => Known && (_device!.Tier == Tier.Tested || _settings.ExperimentalEnabled);

    public TrayContext()
    {
        _settings = AppSettings.Load();
        _settings.Autostart = Autostart.IsEnabled();
        Lang.Set(_settings.Language);

        _firmware = Ec.ReadFirmware();
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
    }

    private string DeviceDescriptor()
    {
        if (!Known) return Lang.T("unsupported_title");
        string tier = _device!.Tier == Tier.Tested ? Lang.T("tier_tested")
                    : Writable ? Lang.T("tier_experimental")
                    : Lang.T("experimental_locked");
        return _device.Name + "  ·  " + tier;
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
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("MSI Profile Switcher") { Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        menu.Items.Add(new ToolStripLabel(DeviceDescriptor()) { ForeColor = Color.Gray, Font = new Font("Segoe UI", 8) });
        menu.Items.Add(new ToolStripSeparator());

        foreach (var id in Profiles.Order)
        {
            var item = new ToolStripMenuItem(Profiles.Get(id).Label) { Tag = id, Enabled = Writable };
            item.Click += (_, _) => SetProfile((ProfileId)item.Tag!, osd: true);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var status = new ToolStripMenuItem(Lang.T("menu_status"));
        status.Click += (_, _) => OpenStatus();
        menu.Items.Add(status);

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
        settings.Click += (_, _) => OpenSettings();
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

    // ---------------- profile ----------------
    private void SetProfile(ProfileId id, bool osd, bool count = true)
    {
        if (!Writable) { ShowState(); return; }
        try
        {
            Ec.Apply(_device!.Recipes[id]);
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

    private void OpenStatus()
    {
        if (_statusForm is { IsDisposed: false })
        {
            _statusForm.WindowState = FormWindowState.Normal;
            _statusForm.BringToFront();
            _statusForm.Activate();
            return;
        }
        _statusForm = new StatusForm(
            () => new StatusInfo(_current, Writable, Known, DeviceDescriptor(),
                                 _switches, DateTime.Now - _profileSince, Autostart.IsEnabled(), AppVersion()),
            () => Known ? Ec.ReadHw(_device!) : new HwSnapshot(0, 0, 0, 0, 0, _firmware),
            id => _settings.ColorFor(id),
            _settings.StatusOnTop,
            v => { _settings.StatusOnTop = v; _settings.Save(); });
        _statusForm.Show();
    }

    private static string AppVersion()
    {
        var v = typeof(TrayContext).Assembly.GetName().Version;
        return v == null ? "1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private void TryApplyChargeLimit()
    {
        if (Writable && _settings.ChargeLimit is 60 or 80 or 100)
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
        _statusForm?.Close();
        _osd.Dispose();
        _tray.Dispose();
        _currentIcon?.Dispose();
        ExitThread();
    }
}
