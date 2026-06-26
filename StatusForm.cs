namespace MSIProfileSwitcher;

public sealed record StatusInfo(
    ProfileId Profile, bool Active, bool Known, string Device,
    int Switches, TimeSpan InProfile, bool Autostart, string AppVersion);

public sealed class StatusForm : Form
{
    private readonly Func<StatusInfo> _provider;
    private readonly Func<HwSnapshot> _hw;
    private readonly Func<ProfileId, Color> _colorOf;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };

    private readonly Panel _header = new();
    private readonly Label _hProfile = new();
    private readonly Dictionary<string, Label> _vals = new();

    private static readonly (string locKey, string id)[] Rows =
    {
        ("st_model", "model"),
        ("st_cpu_temp", "cpu_temp"), ("st_gpu_temp", "gpu_temp"),
        ("st_cpu_fan", "cpu_fan"),   ("st_gpu_fan", "gpu_fan"),
        ("st_charge", "charge"),     ("st_firmware", "fw"),
        ("st_switches", "sw"),       ("st_in_profile", "inp"),
        ("st_autostart", "auto"),    ("st_app_ver", "ver"),
    };

    public StatusForm(Func<StatusInfo> provider, Func<HwSnapshot> hw, Func<ProfileId, Color> colorOf,
                      bool onTop, Action<bool> onToggleOnTop)
    {
        _provider = provider;
        _hw = hw;
        _colorOf = colorOf;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(440, 500);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;
        TopMost = onTop;
        Text = Lang.T("status_title");
        Icon = TrayIconFactory.Create(colorOf(provider().Profile));

        _header.SetBounds(0, 0, 440, 58);
        _hProfile.AutoSize = true;
        _hProfile.MaximumSize = new Size(410, 0);
        _hProfile.Location = new Point(20, 14);
        _hProfile.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        _hProfile.ForeColor = Color.White;
        _hProfile.BackColor = Color.Transparent;
        _header.Controls.Add(_hProfile);
        Controls.Add(_header);

        int y = 80;
        foreach (var (locKey, id) in Rows)
        {
            Controls.Add(new Label
            {
                Text = Lang.T(locKey) + ":",
                Location = new Point(22, y),
                AutoSize = true,
                ForeColor = Color.DimGray,
            });
            var v = new Label
            {
                Location = new Point(200, y),
                AutoSize = true,
                MaximumSize = new Size(218, 0),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Text = "—",
            };
            Controls.Add(v);
            _vals[id] = v;
            y += 30;
        }

        var chkOnTop = new CheckBox
        {
            Text = Lang.T("always_on_top"),
            AutoSize = true,
            Location = new Point(22, y + 6),
            Checked = onTop,
        };
        chkOnTop.CheckedChanged += (_, _) =>
        {
            TopMost = chkOnTop.Checked;
            onToggleOnTop(chkOnTop.Checked);
        };
        Controls.Add(chkOnTop);

        var btnRefresh = new Button { Text = Lang.T("st_refresh"), Size = new Size(110, 30), Location = new Point(22, y + 38) };
        var btnClose = new Button { Text = Lang.T("set_close"), Size = new Size(90, 30), Location = new Point(326, y + 38) };
        btnRefresh.Click += (_, _) => Refresh2();
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnRefresh);
        Controls.Add(btnClose);

        _timer.Tick += (_, _) => Refresh2();
        Load += (_, _) => { Refresh2(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Stop();
    }

    private void Refresh2()
    {
        var info = _provider();
        _header.BackColor = info.Active ? _colorOf(info.Profile) : Color.Gray;
        _hProfile.Text = info.Active
            ? "MSI  ·  " + Profiles.Get(info.Profile).Label
            : "MSI  ·  " + info.Device;

        HwSnapshot hw;
        try { hw = _hw(); } catch { hw = default; }

        _vals["model"].Text = info.Device;
        _vals["cpu_temp"].Text = hw.CpuTemp is > 0 and < 130 ? $"{hw.CpuTemp} °C" : "—";
        _vals["gpu_temp"].Text = hw.GpuTemp is > 0 and < 130 ? $"{hw.GpuTemp} °C" : "—";
        _vals["cpu_fan"].Text = info.Known ? $"{hw.CpuFan} %" : "—";
        _vals["gpu_fan"].Text = info.Known ? $"{hw.GpuFan} %" : "—";
        _vals["charge"].Text = hw.ChargeLimit is >= 10 and <= 100 ? $"{hw.ChargeLimit} %" : "—";
        _vals["fw"].Text = string.IsNullOrEmpty(hw.Firmware) ? "—" : hw.Firmware;
        _vals["sw"].Text = info.Switches.ToString();
        _vals["inp"].Text = FmtTs(info.InProfile);
        _vals["auto"].Text = info.Autostart ? Lang.T("yes") : Lang.T("no");
        _vals["ver"].Text = info.AppVersion;
    }

    private static string FmtTs(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} min";
        return $"{t.Seconds} s";
    }
}
