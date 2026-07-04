namespace GhostDeck;

public sealed class SettingsForm : Form
{
    private readonly Action<AppSettings> _onSave;
    private readonly AppSettings _incoming;

    private readonly ComboBox _cmbLang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TabControl _tabs = new();
    private readonly TabPage _tabHotkeys = new();
    private readonly TabPage _tabLook = new();
    private readonly TabPage _tabPower = new();

    private readonly Dictionary<string, HotkeyBox> _boxes = new();
    private readonly CheckBox _autostart = new() { AutoSize = true };
    private readonly Label _lblHotkeys = new() { Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true };
    private readonly Label _lblHint = new() { ForeColor = Color.Gray, AutoSize = false };
    private readonly Label _lblCycle = new() { AutoSize = true };

    private readonly Label _lblLang = new() { AutoSize = true };
    private readonly Label _lblColors = new() { Font = new Font("Segoe UI", 11, FontStyle.Bold), AutoSize = true };
    private readonly Dictionary<string, string> _selColors = new();
    private readonly Dictionary<string, List<Panel>> _swatches = new();

    private readonly Label _lblCharge = new() { AutoSize = true };
    private readonly ComboBox _cmbCharge = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _chkAutoSwitch = new() { AutoSize = true };
    private readonly Label _lblOnAc = new() { AutoSize = true };
    private readonly ComboBox _cmbAc = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Label _lblOnBattery = new() { AutoSize = true };
    private readonly ComboBox _cmbBattery = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _chkExperimental = new() { AutoSize = true };
    private readonly CheckBox _chkUpdates = new() { AutoSize = true };

    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
    private readonly Button _btnDefault = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnClose = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2500 };

    private static readonly int[] ChargeValues = { 0, 100, 80, 60 };

    public SettingsForm(AppSettings current, Action<AppSettings> onSave)
    {
        _onSave = onSave;
        _incoming = current;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(470, 470);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;
        Icon = TrayIconFactory.Create(current.ColorFor(ProfileId.Silent));

        // --- jezyk (gora) ---
        _lblLang.Location = new Point(18, 16);
        _cmbLang.Location = new Point(150, 13);
        _cmbLang.Items.AddRange(Lang.Names);
        _cmbLang.SelectedIndex = Math.Max(0, Array.IndexOf(Lang.Codes, current.Language));
        _cmbLang.SelectedIndexChanged += (_, _) => { /* zastosuje sie przy Zapisz */ };
        Controls.Add(_lblLang);
        Controls.Add(_cmbLang);

        // --- zakladki ---
        _tabs.Location = new Point(12, 48);
        _tabs.Size = new Size(446, 360);
        _tabs.TabPages.AddRange(new[] { _tabHotkeys, _tabLook, _tabPower });
        Controls.Add(_tabs);

        BuildHotkeysTab(current);
        BuildLookTab(current);
        BuildPowerTab(current);

        // --- dol ---
        _status.Location = new Point(16, 416);
        _status.ForeColor = ColorTranslator.FromHtml("#1FA855");
        Controls.Add(_status);
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); _status.Text = ""; };

        _btnDefault.SetBounds(14, 432, 110, 30);
        _btnSave.SetBounds(268, 432, 100, 30);
        _btnClose.SetBounds(374, 432, 84, 30);
        _btnDefault.Click += (_, _) => ResetDefaults();
        _btnSave.Click += (_, _) => Save();
        _btnClose.Click += (_, _) => Close();
        Controls.Add(_btnDefault);
        Controls.Add(_btnSave);
        Controls.Add(_btnClose);
        AcceptButton = _btnSave;
        CancelButton = _btnClose;

        Localize();
    }

    private void BuildHotkeysTab(AppSettings current)
    {
        _lblHotkeys.Location = new Point(12, 12);
        _lblHint.SetBounds(12, 40, 410, 34);
        _tabHotkeys.Controls.Add(_lblHotkeys);
        _tabHotkeys.Controls.Add(_lblHint);

        var rows = new (string key, string label)[]
        {
            ("Silent", "Silent"), ("Balanced", "Balanced"),
            ("Extreme", "Extreme"), ("SuperBattery", "Super Battery"),
        };
        int y = 80;
        foreach (var (key, label) in rows)
        {
            _tabHotkeys.Controls.Add(new Label { Text = label, Location = new Point(18, y + 5), AutoSize = true });
            var box = new HotkeyBox { Location = new Point(180, y), Width = 240 };
            box.SetValue(current.Hotkeys.TryGetValue(key, out var hd) ? hd : new HotkeyDef());
            _tabHotkeys.Controls.Add(box);
            _boxes[key] = box;
            y += 34;
        }
        _lblCycle.Location = new Point(18, y + 5);
        var cbox = new HotkeyBox { Location = new Point(180, y), Width = 240 };
        cbox.SetValue(current.Hotkeys.TryGetValue("Cycle", out var ch) ? ch : new HotkeyDef());
        _tabHotkeys.Controls.Add(_lblCycle);
        _tabHotkeys.Controls.Add(cbox);
        _boxes["Cycle"] = cbox;
        y += 44;

        _autostart.Location = new Point(18, y);
        _autostart.Checked = current.Autostart;
        _tabHotkeys.Controls.Add(_autostart);
    }

    private void BuildLookTab(AppSettings current)
    {
        _lblColors.Location = new Point(12, 12);
        _tabLook.Controls.Add(_lblColors);

        var profiles = new (string key, string label)[]
        {
            ("Silent", "Silent"), ("Balanced", "Balanced"),
            ("Extreme", "Extreme"), ("SuperBattery", "Super Battery"),
        };
        int y = 48;
        foreach (var (key, label) in profiles)
        {
            _tabLook.Controls.Add(new Label { Text = label, Location = new Point(18, y), AutoSize = true });
            var id = Enum.Parse<ProfileId>(key);
            _selColors[key] = ColorTranslator.ToHtml(current.ColorFor(id));
            var list = new List<Panel>();
            _swatches[key] = list;

            int sx = 18, sy = y + 22;
            foreach (var hex in Profiles.Palette)
            {
                var sw = new Panel
                {
                    Size = new Size(26, 22),
                    Location = new Point(sx, sy),
                    BackColor = ColorTranslator.FromHtml(hex),
                    Cursor = Cursors.Hand,
                    Tag = hex,
                };
                string pk = key, ph = hex;
                sw.Paint += (s, e) =>
                {
                    if (string.Equals(_selColors[pk], ph, StringComparison.OrdinalIgnoreCase))
                    {
                        using var p1 = new Pen(Color.White, 2);
                        e.Graphics.DrawRectangle(p1, 2, 2, sw.Width - 5, sw.Height - 5);
                        using var p2 = new Pen(Color.FromArgb(60, 60, 60), 1);
                        e.Graphics.DrawRectangle(p2, 0, 0, sw.Width - 1, sw.Height - 1);
                    }
                };
                sw.Click += (s, e) =>
                {
                    _selColors[pk] = ph;
                    foreach (var p in _swatches[pk]) p.Invalidate();
                };
                _tabLook.Controls.Add(sw);
                list.Add(sw);
                sx += 30;
            }
            y += 66;
        }
    }

    private void BuildPowerTab(AppSettings current)
    {
        _lblCharge.Location = new Point(12, 16);
        _cmbCharge.Location = new Point(220, 13);
        _cmbCharge.SelectedIndexChanged += (_, _) => { };
        _tabPower.Controls.Add(_lblCharge);
        _tabPower.Controls.Add(_cmbCharge);

        _chkAutoSwitch.Location = new Point(14, 64);
        _chkAutoSwitch.Checked = current.AutoSwitchEnabled;
        _tabPower.Controls.Add(_chkAutoSwitch);

        _lblOnAc.Location = new Point(34, 102);
        _cmbAc.Location = new Point(220, 99);
        _lblOnBattery.Location = new Point(34, 140);
        _cmbBattery.Location = new Point(220, 137);
        foreach (var id in Profiles.Order)
        {
            _cmbAc.Items.Add(Profiles.Get(id).Label);
            _cmbBattery.Items.Add(Profiles.Get(id).Label);
        }
        _cmbAc.SelectedIndex = IndexOfProfileKey(current.ProfileOnAC);
        _cmbBattery.SelectedIndex = IndexOfProfileKey(current.ProfileOnBattery);
        _tabPower.Controls.Add(_lblOnAc);
        _tabPower.Controls.Add(_cmbAc);
        _tabPower.Controls.Add(_lblOnBattery);
        _tabPower.Controls.Add(_cmbBattery);

        _chkExperimental.Location = new Point(14, 196);
        _chkExperimental.Checked = current.ExperimentalEnabled;
        _tabPower.Controls.Add(_chkExperimental);

        _chkUpdates.Location = new Point(14, 232);
        _chkUpdates.Checked = current.UpdateCheckEnabled;
        _tabPower.Controls.Add(_chkUpdates);

        // wartosci charge combo wypelnia Localize()
        _cmbCharge.Tag = current.ChargeLimit;
    }

    private static int IndexOfProfileKey(string key)
    {
        for (int i = 0; i < Profiles.Order.Length; i++)
            if (Profiles.Get(Profiles.Order[i]).Key == key) return i;
        return 1; // Balanced
    }

    private void Localize()
    {
        Text = "MSI Profile Switcher — " + Lang.T("menu_settings");
        _lblLang.Text = Lang.T("set_language") + ":";
        _tabHotkeys.Text = Lang.T("set_hotkeys");
        _tabLook.Text = Lang.T("set_colors");
        _tabPower.Text = Lang.T("menu_settings");
        _lblHotkeys.Text = Lang.T("set_hotkeys");
        _lblHint.Text = Lang.T("set_hint");
        _lblCycle.Text = Lang.T("cycle");
        _autostart.Text = Lang.T("set_autostart");
        _lblColors.Text = Lang.T("set_colors");
        _lblCharge.Text = Lang.T("set_charge");
        _chkAutoSwitch.Text = Lang.T("set_autoswitch");
        _lblOnAc.Text = Lang.T("on_ac") + ":";
        _lblOnBattery.Text = Lang.T("on_battery") + ":";
        _chkExperimental.Text = Lang.T("experimental_enable");
        _chkUpdates.Text = Lang.T("set_check_updates");
        _btnDefault.Text = Lang.T("set_default");
        _btnSave.Text = Lang.T("set_save");
        _btnClose.Text = Lang.T("set_close");

        int curCharge = _cmbCharge.Tag is int c ? c : 0;
        _cmbCharge.Items.Clear();
        _cmbCharge.Items.Add(Lang.T("charge_dont"));
        _cmbCharge.Items.Add("100%");
        _cmbCharge.Items.Add("80%");
        _cmbCharge.Items.Add("60%");
        _cmbCharge.SelectedIndex = Math.Max(0, Array.IndexOf(ChargeValues, curCharge));
    }

    private void ResetDefaults()
    {
        var def = new AppSettings();
        def.EnsureDefaults();
        foreach (var (key, box) in _boxes) box.SetValue(def.Hotkeys[key]);
        foreach (var id in Profiles.Order)
        {
            var k = Profiles.Get(id).Key;
            _selColors[k] = ColorTranslator.ToHtml(Profiles.Get(id).DefaultColor);
            foreach (var p in _swatches[k]) p.Invalidate();
        }
        _cmbCharge.SelectedIndex = 0;
        _chkAutoSwitch.Checked = false;
        ShowStatus(Lang.T("set_reset_hint"), Color.Gray);
    }

    private void Save()
    {
        var s = new AppSettings
        {
            Language = Lang.Codes[Math.Max(0, _cmbLang.SelectedIndex)],
            Autostart = _autostart.Checked,
            AutoSwitchEnabled = _chkAutoSwitch.Checked,
            ProfileOnAC = Profiles.Get(Profiles.Order[Math.Max(0, _cmbAc.SelectedIndex)]).Key,
            ProfileOnBattery = Profiles.Get(Profiles.Order[Math.Max(0, _cmbBattery.SelectedIndex)]).Key,
            ChargeLimit = ChargeValues[Math.Max(0, _cmbCharge.SelectedIndex)],
            ExperimentalEnabled = _chkExperimental.Checked,
            StatusOnTop = _incoming.StatusOnTop,
            UpdateCheckEnabled = _chkUpdates.Checked,
            LastUpdateCheckUtc = _incoming.LastUpdateCheckUtc,
        };
        foreach (var (key, box) in _boxes) s.Hotkeys[key] = box.Value.Clone();
        foreach (var (k, hex) in _selColors) s.Colors[k] = hex;
        s.EnsureDefaults();

        _onSave(s);             // aplikuje globalnie (w tym Lang.Set)
        _cmbCharge.Tag = s.ChargeLimit;
        Localize();             // odswiez UI w nowym jezyku
        ShowStatus(Lang.T("set_saved"), ColorTranslator.FromHtml("#1FA855"));
    }

    private void ShowStatus(string text, Color color)
    {
        _status.ForeColor = color;
        _status.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _statusTimer.Dispose();
        base.Dispose(disposing);
    }
}
