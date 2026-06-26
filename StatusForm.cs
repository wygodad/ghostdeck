namespace MSIProfileSwitcher;

public sealed record StatusInfo(
    ProfileId Profile, bool Active, bool Known, string Device,
    string TierText, Color TierColor,
    int Switches, TimeSpan InProfile, bool Autostart, string AppVersion);

public sealed class StatusForm : Form
{
    private readonly Func<StatusInfo> _provider;
    private readonly Func<HwSnapshot> _hw;
    private readonly Func<ProfileId, Color> _colorOf;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly ToolTip _tip = new();

    private readonly Panel _header = new();
    private readonly Label _hProfile = new();
    private readonly Label _badge = new();
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
        ClientSize = new Size(470, 500);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;
        TopMost = onTop;
        Text = Lang.T("status_title");
        Icon = TrayIconFactory.Create(colorOf(provider().Profile));

        _header.SetBounds(0, 0, 470, 58);
        _hProfile.AutoSize = false;
        _hProfile.SetBounds(20, 10, 440, 38);
        _hProfile.AutoEllipsis = true;
        _hProfile.TextAlign = ContentAlignment.MiddleLeft;
        _hProfile.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        _hProfile.ForeColor = Color.White;
        _hProfile.BackColor = Color.Transparent;
        _header.Controls.Add(_hProfile);

        _badge.AutoSize = true;
        _badge.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _badge.ForeColor = Color.White;
        _badge.Padding = new Padding(7, 3, 7, 3);
        _badge.TextAlign = ContentAlignment.MiddleCenter;
        _header.Controls.Add(_badge);
        _badge.BringToFront();

        Controls.Add(_header);

        int y = 80;
        foreach (var (locKey, id) in Rows)
        {
            Controls.Add(new Label
            {
                Text = Lang.T(locKey) + ":",
                Location = new Point(22, y + 2),
                AutoSize = true,
                ForeColor = Color.DimGray,
            });
            var v = new Label
            {
                Location = new Point(188, y),
                Size = new Size(262, 22),
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
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
        var btnClose = new Button { Text = Lang.T("set_close"), Size = new Size(90, 30), Location = new Point(356, y + 38) };
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
            : info.Known ? "MSI  ·  " + Lang.T("tier_experimental")
            : "MSI  ·  " + Lang.T("unsupported_title");

        HwSnapshot hw;
        try { hw = _hw(); } catch { hw = default; }

        _vals["model"].Text = info.Device;
        _tip.SetToolTip(_vals["model"], info.Device);
        _badge.Text = info.TierText.ToUpperInvariant();
        _badge.BackColor = info.TierColor;
        _badge.Location = new Point(_header.Width - _badge.Width - 16, (_header.Height - _badge.Height) / 2);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}
