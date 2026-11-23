namespace GhostDeck;

/// <summary>Native controls for the tray-owned cooling controller.</summary>
public sealed class CrossCoolingPanel : UserControl
{
    private readonly MainDeps _d;
    private readonly Func<DeviceProfile?> _device;
    private readonly Func<FanLiveSample> _sample;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly TableLayoutPanel _rows = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _values = new() { AutoSize = true };
    private readonly CrossCoolingGraph _graph = new();
    private readonly ToggleSwitch _enable = new();
    private readonly ThemedComboBox _mode = new(), _algorithm = new(), _weights = new();
    private readonly CheckItem _smooth = new("", false);
    private readonly Panel _advanced = new() { AutoSize = true, Dock = DockStyle.Top, Visible = false };
    private bool _sync;
    private CrossCoolingMode _selected = CrossCoolingMode.Both;
    private CrossCoolingSettings Settings => _d.Settings.CrossCooling;

    public CrossCoolingPanel(MainDeps d, Func<DeviceProfile?> device, Func<FanLiveSample> sample)
    {
        _d = d; _device = device; _sample = sample;
        AutoScroll = true; DoubleBuffered = true;
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        Controls.Add(_rows);
        _timer.Tick += (_, _) => UpdateStatus();
        VisibleChanged += (_, _) => { if (Visible) { Rebuild(); _timer.Start(); UpdateStatus(); } else _timer.Stop(); };
        _mode.SelectedIndexChanged += (_, _) =>
        {
            if (_sync || _mode.SelectedIndex < 0) return;
            Settings.Mode = (CrossCoolingMode)_mode.SelectedIndex;
            if (Settings.Mode != CrossCoolingMode.Off) _selected = Settings.Mode;
            Save(Settings.Mode != CrossCoolingMode.Off);
        };
        _enable.Toggled += on =>
        {
            if (_sync) return;
            if (on && Settings.Mode == CrossCoolingMode.Off) _mode.SelectedIndex = (int)_selected;
            else Save(on);
        };
        _algorithm.SelectedIndexChanged += (_, _) =>
        { if (!_sync && _algorithm.SelectedIndex >= 0) { Settings.Algorithm = (CoolingMix)_algorithm.SelectedIndex; Save(); Rebuild(); } };
        _weights.SelectedIndexChanged += (_, _) =>
        {
            if (_sync || _weights.SelectedIndex < 0) return;
            (Settings.CpuWeight, Settings.GpuWeight) = _weights.SelectedIndex switch { 0 => (75, 25), 1 => (25, 75), 2 => (50, 50), _ => (Settings.CpuWeight, Settings.GpuWeight) };
            Save(); Rebuild();
        };
        _smooth.Toggled += v => { Settings.Smoothing = v; Save(); };
        Rebuild();
    }

    private void Save(bool? enabled = null)
    {
        if (_sync) return;
        if (enabled.HasValue) Settings.Enabled = enabled.Value;
        Settings.Normalize(); _d.SaveSettings();
        _d.ConfigureCooling(Settings.Enabled);
        UpdateStatus();
    }

    public void Rebuild()
    {
        if (_sync || IsDisposed) return;
        _sync = true; SuspendLayout(); _rows.SuspendLayout();
        try
        {
            Settings.Normalize();
            if (Settings.Mode != CrossCoolingMode.Off) _selected = Settings.Mode;
            // Retain stateful controls; dispose the generated labels/sliders/buttons each rebuild.
            Control[] keep = { _enable, _mode, _algorithm, _weights, _smooth, _status, _values, _advanced, _graph };
            foreach (Control c in keep) c.Parent?.Controls.Remove(c);
            foreach (Control c in _rows.Controls.Cast<Control>().ToArray())
            { _rows.Controls.Remove(c); if (!keep.Contains(c)) c.Dispose(); }
            foreach (Control c in _advanced.Controls.Cast<Control>().ToArray())
            { _advanced.Controls.Remove(c); c.Dispose(); }
            _rows.RowStyles.Clear(); _rows.RowCount = 0;
            BackColor = _rows.BackColor = _advanced.BackColor = Theme.Surface;
            Font = Dpi.Font("Segoe UI", 10f); ForeColor = Theme.Text;
            int pad = Scale(12); _rows.Padding = new Padding(pad);
            AddWide(TextLabel("cc_control_help"));
            Add("cc_enable", _enable); _enable.Checked = _d.CoolingStatus().Enabled;
            Fill(_mode, new[] { "cc_off", "cc_cpu_gpu", "cc_gpu_cpu", "cc_both" }, (int)Settings.Mode);
            Fill(_algorithm, new[] { "cc_max", "cc_average", "cc_weighted", "cc_priority" }, (int)Settings.Algorithm);
            Add("cc_mode", _mode); Add("cc_algorithm", _algorithm);
            var expand = new Button { Text = Lang.T("cc_advanced"), AutoSize = true };
            Ui.StyleGhost(expand); expand.Click += (_, _) => _advanced.Visible = !_advanced.Visible;
            AddWide(expand);
            var advancedRows = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
            advancedRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            advancedRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            _advanced.Controls.Add(advancedRows);
            void Setting(string key, int min, int max, int val, Action<int> change, string suffix = "")
            {
                var slider = new Slider(min, max, val, suffix: suffix) { Dock = DockStyle.Fill, AccessibleName = Lang.T(key) };
                if (key is "cc_cpu_weight" or "cc_gpu_weight") slider.Enabled = Settings.Algorithm == CoolingMix.WeightedAverage;
                if (key == "cc_boost") slider.Enabled = Settings.Algorithm == CoolingMix.PriorityBoost;
                slider.ValueChanged += v => { change(v); Save(); };
                Row(advancedRows, TextLabel(key), slider);
            }
            Setting("cc_sampling", 1, 10, Settings.SamplingSeconds, v => { Settings.SamplingSeconds = v; _timer.Interval = v * 1000; }, " s");
            Setting("cc_interval", 5, 60, Settings.UpdateSeconds, v => Settings.UpdateSeconds = v, " s");
            Setting("cc_threshold", 1, 20, Settings.Threshold, v => Settings.Threshold = v, " pp");
            Setting("cc_increase", 0, 10, Settings.IncreaseHysteresis, v => Settings.IncreaseHysteresis = v, " pp");
            Setting("cc_decrease", 0, 20, Settings.DecreaseHysteresis, v => Settings.DecreaseHysteresis = v, " pp");
            Setting("cc_step", 1, 20, Settings.DecreaseStep, v => Settings.DecreaseStep = v, " pp");
            _smooth.Text = Lang.T("cc_smoothing"); _smooth.Checked = Settings.Smoothing;
            Row(advancedRows, _smooth, new Label { AutoSize = true });
            int preset = (Settings.CpuWeight, Settings.GpuWeight) switch { (75, 25) => 0, (25, 75) => 1, (50, 50) => 2, _ => 3 };
            Fill(_weights, new[] { "cc_cpu_priority", "cc_gpu_priority", "cc_balanced", "cc_custom" }, preset);
            Row(advancedRows, TextLabel("cc_weights"), _weights); _weights.Enabled = Settings.Algorithm == CoolingMix.WeightedAverage;
            void Weight(bool cpu, int v)
            {
                if ((cpu ? Settings.GpuWeight : Settings.CpuWeight) + v == 0) { Rebuild(); return; }
                if (cpu) Settings.CpuWeight = v; else Settings.GpuWeight = v;
                Save();
                _sync = true; _weights.SelectedIndex = 3; _sync = false;
            }
            Setting("cc_cpu_weight", 0, 100, Settings.CpuWeight, v => Weight(true, v), "%");
            Setting("cc_gpu_weight", 0, 100, Settings.GpuWeight, v => Weight(false, v), "%");
            Setting("cc_boost", 0, 100, Settings.BoostPercent, v => Settings.BoostPercent = v, "%");
            var reset = new Button { Text = Lang.T("cc_reset"), AutoSize = true };
            Ui.StyleGhost(reset); reset.Click += (_, _) => { _d.Settings.CrossCooling = new(); Save(false); Rebuild(); };
            Row(advancedRows, reset, new Label { AutoSize = true });
            AddWide(_advanced);
            AddWide(TextLabel("cc_startup")); AddWide(TextLabel("cc_emergency"));
            AddWide(_status); AddWide(_values);
            AddWide(TextLabel("cc_graph")); _graph.Height = Scale(100); AddWide(_graph);
            _status.ForeColor = Theme.Amber; _values.ForeColor = Theme.Text;
            _timer.Interval = Settings.SamplingSeconds * 1000;
        }
        finally { _rows.ResumeLayout(true); ResumeLayout(true); _sync = false; }
        UpdateStatus();
    }

    private int Scale(int px) => (int)Math.Ceiling(px * Dpi.Of(this) / 96f);
    private Label TextLabel(string key) => new() { Text = Lang.T(key), AutoSize = true, ForeColor = Theme.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private void Add(string key, Control control) { control.AccessibleName = Lang.T(key); Row(_rows, TextLabel(key), control); }
    private void AddWide(Control c) { int r = _rows.RowCount++; _rows.Controls.Add(c, 0, r); _rows.SetColumnSpan(c, 2); c.Margin = new Padding(Scale(4), Scale(8), Scale(4), Scale(8)); c.Dock = DockStyle.Top; }
    private void Row(TableLayoutPanel table, Control label, Control value)
    {
        int r = table.RowCount++; table.Controls.Add(label, 0, r); table.Controls.Add(value, 1, r);
        label.Margin = value.Margin = new Padding(Scale(4), Scale(6), Scale(4), Scale(6));
        value.Dock = value is ToggleSwitch ? DockStyle.None : DockStyle.Fill;
        if (value is ToggleSwitch) value.Anchor = AnchorStyles.Left;
    }
    private static void Fill(ThemedComboBox c, string[] keys, int selected)
    { c.Items.Clear(); c.Items.AddRange(keys.Select(Lang.T).Cast<object>().ToArray()); c.SelectedIndex = selected; }

    public void UpdateStatus()
    {
        if (_sync || IsDisposed) return;
        var status = _d.CoolingStatus();
        _enable.Checked = status.Enabled;
        _status.Text = Lang.T(status.Key) + (status.Error.Length == 0 ? "" : "\n" + status.Error);
        var dev = _device();
        _graph.SetState(status.Demand, dev?.FanCurve?.MaxFanPct ?? 100);
        var sample = _sample();
        var reading = status.Reading;
        int cpu = reading?.Hardware.CpuTemp ?? sample.CpuTemp, gpu = reading?.Hardware.GpuTemp ?? sample.GpuTemp;
        var time = reading?.Time ?? (sample.Time == default ? DateTimeOffset.MinValue : new DateTimeOffset(sample.Time));
        bool fresh = DateTimeOffset.UtcNow - time <= TimeSpan.FromSeconds(Math.Max(6, Settings.SamplingSeconds * 3));
        string values = Lang.T("cc_no_sample");
        if (fresh && CrossCooling.ValidTemperature(cpu) && CrossCooling.ValidTemperature(gpu))
        {
            string pct(double x) => x.ToString("0.#", Lang.Culture) + "%";
            values = status.Demand is { } demand
                ? string.Format(Lang.T("cc_values"), cpu, gpu, pct(demand.CpuOriginal), pct(demand.GpuOriginal),
                    pct(demand.Mixed), pct(demand.CpuTarget), pct(demand.GpuTarget))
                : string.Format(Lang.T("cc_temperatures"), cpu, gpu);
            int cf = reading?.Hardware.CpuFan ?? sample.CpuFan, gf = reading?.Hardware.GpuFan ?? sample.GpuFan;
            int cr = reading?.Hardware.CpuRpm ?? sample.CpuRpm, gr = reading?.Hardware.GpuRpm ?? sample.GpuRpm;
            values += "\n" + (dev != null && Ec.HasTach(dev)
                ? string.Format(Lang.T("cc_actual"), cf, cr, gf, gr)
                : string.Format(Lang.T("cc_duty"), cf, gf));
        }
        _values.Text = values + "\n" + string.Format(Lang.T("cc_plan"),
            status.LastWrite?.ToLocalTime().ToString("T", Lang.Culture) ?? "-", _d.Current(),
            _d.Settings.CurveName.Length == 0 ? Lang.T("cc_custom") : _d.Settings.CurveName) + "\n" +
            string.Format(Lang.T("cc_effective_weights"), (100.0 * Settings.CpuWeight / (Settings.CpuWeight + Settings.GpuWeight)).ToString("0.#", Lang.Culture),
                (100.0 * Settings.GpuWeight / (Settings.CpuWeight + Settings.GpuWeight)).ToString("0.#", Lang.Culture));
    }

    protected override Point ScrollToControl(Control activeControl) => AutoScrollPosition;
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}

internal sealed class CrossCoolingGraph : Control
{
    private CoolingDemand? _demand;
    private int _max = 100;
    public CrossCoolingGraph() { DoubleBuffered = true; ResizeRedraw = true; }
    public void SetState(CoolingDemand? demand, int max) { _demand = demand; _max = max; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
        if (_demand is not { } d) return;
        int s(int n) => (int)Math.Ceiling(n * Dpi.Of(this) / 96f);
        var font = Dpi.Font("Segoe UI", 10f);
        int h = Math.Max(s(42), font.Height + s(20));
        void Fan(string name, double own, double target, int y, Color color)
        {
            TextRenderer.DrawText(e.Graphics, $"{name}  {own:0.#}% → {target:0.#}%", font, new Point(0, y), Theme.Text);
            int top = y + font.Height + s(3), w = Math.Max(1, Width - s(4));
            using var track = new SolidBrush(Theme.Border);
            using var original = new SolidBrush(Theme.Muted);
            using var mixed = new SolidBrush(color);
            e.Graphics.FillRectangle(track, 0, top, w, s(10));
            e.Graphics.FillRectangle(mixed, 0, top, (int)(w * target / _max), s(10));
            e.Graphics.FillRectangle(original, 0, top, (int)(w * own / _max), s(4));
        }
        Fan("CPU", d.CpuOriginal, d.CpuTarget, 0, Theme.AccentFill);
        Fan("GPU", d.GpuOriginal, d.GpuTarget, h, Theme.Violet);
    }
}
