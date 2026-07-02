using System.Text;

namespace MSIProfileSwitcher;

/// <summary>
/// Advanced test / discovery dialog (opened from the Status tab):
///  - RPM finder: two read-only EC scans at different fan speeds; the tach register is
///    the address whose value changes (matched against MSI Center).
///  - EC dump to file: read-only full dump, used to locate fan-curve table addresses.
///  - Silent + Advanced fan experiment: does the EC obey advanced fan control outside Extreme?
/// All EC writes are gated on Writable (Tested, or Experimental opted in) and easily reverted.
/// </summary>
public sealed class TestDialog : Form
{
    private readonly MainDeps _d;
    private readonly ListBox _rpm = new();
    private readonly Label _live = new();
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 1000 };
    private byte[]? _dumpA;
    private byte[]? _cbA;

    private static string Rpm(byte raw) => raw == 0 ? "—" : $"{478000 / raw} RPM";

    public TestDialog(MainDeps d)
    {
        _d = d;
        Text = Lang.T("test_title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(580, 700);
        Font = new Font("Segoe UI", 9.5f);
        Icon = TrayIconFactory.Create(Theme.Accent);

        var note = new Label { Text = Lang.T("test_note"), AutoSize = false };
        note.SetBounds(20, 14, 540, 56);

        var btnA = new Button { Text = Lang.T("test_rpm_a"), Width = 250, Height = 34 };
        btnA.SetBounds(20, 80, 250, 34);
        btnA.Click += (_, _) => { _dumpA = SafeDump(); _rpm.Items.Clear(); _rpm.Items.Add(_dumpA == null ? "ERROR" : "OK — " + Lang.T("test_rpm_b")); };

        var btnB = new Button { Text = Lang.T("test_rpm_b"), Width = 250, Height = 34 };
        btnB.SetBounds(282, 80, 250, 34);
        btnB.Click += (_, _) => CompareRpm();

        var hint = new Label { Text = Lang.T("test_rpm_hint2"), AutoSize = false };
        hint.SetBounds(20, 122, 540, 52);

        _rpm.SetBounds(20, 178, 540, 210);
        _rpm.Font = new Font("Consolas", 11f);
        _rpm.SelectionMode = SelectionMode.MultiExtended;
        _rpm.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C && _rpm.SelectedItems.Count > 0)
            {
                Clipboard.SetText(string.Join("\r\n", _rpm.SelectedItems.Cast<object>().Select(o => o!.ToString())));
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _rpm.DoubleClick += (_, _) => { if (_rpm.SelectedItem != null) Clipboard.SetText(_rpm.SelectedItem.ToString()!); };

        _live.SetBounds(20, 392, 540, 56);
        _live.Font = new Font("Consolas", 11.5f, FontStyle.Bold);
        _live.Text = "Fan 1 / CPU (0xC9): —\r\nFan 2 / GPU (0xCB): —";

        var dumpBtn = new Button { Text = Lang.T("test_dump_btn"), Width = 250, Height = 34 };
        dumpBtn.SetBounds(20, 456, 250, 34);
        dumpBtn.Click += (_, _) => SaveDump();

        var curveBtn = new Button { Text = Lang.T("test_curve_btn"), Width = 250, Height = 34 };
        curveBtn.SetBounds(290, 456, 250, 34);
        curveBtn.Click += (_, _) => ShowCurve();

        var advOn = new Button { Text = Lang.T("test_adv_on"), Width = 300, Height = 36 };
        advOn.SetBounds(20, 504, 300, 36);
        advOn.Click += (_, _) => DoWrite(dev => { Ec.Apply(dev.Recipes[ProfileId.Silent]); Ec.Apply(new[] { (dev.FanMode, (byte)0x8D) }); });

        var advOff = new Button { Text = Lang.T("test_adv_off"), Width = 200, Height = 36 };
        advOff.SetBounds(20, 548, 200, 36);
        advOff.Click += (_, _) => DoWrite(dev => Ec.Apply(dev.Recipes[ProfileId.Silent]));

        // Cooler Boost register finder: snapshot, then toggle Cooler Boost with the MSI hardware
        // hotkey (Fn+Up), then compare — the byte(s) that flip are the real Cooler Boost register.
        var cbA = new Button { Text = "Cooler Boost: snapshot A", Width = 250, Height = 34 };
        cbA.SetBounds(20, 596, 250, 34);
        cbA.Click += (_, _) => { _cbA = SafeDump(); _rpm.Items.Clear(); _rpm.Items.Add(_cbA == null ? "ERROR" : "OK — now toggle Cooler Boost (Fn+Up), then click Compare B"); };

        var cbB = new Button { Text = "Cooler Boost: compare B", Width = 250, Height = 34 };
        cbB.SetBounds(290, 596, 250, 34);
        cbB.Click += (_, _) => CompareCooler();

        var close = new Button { Text = Lang.T("set_close"), Width = 120, Height = 34, DialogResult = DialogResult.OK };
        close.SetBounds(440, 652, 120, 34);

        Controls.AddRange(new Control[] { note, btnA, btnB, hint, _rpm, _live, dumpBtn, curveBtn, advOn, advOff, cbA, cbB, close });

        _liveTimer.Tick += (_, _) =>
        {
            try { _live.Text = $"Fan 1 / CPU (0xC9): {Rpm(Ec.ReadByte(0xC9))}\r\nFan 2 / GPU (0xCB): {Rpm(Ec.ReadByte(0xCB))}"; }
            catch { }
        };
        Load += (_, _) => _liveTimer.Start();
        FormClosed += (_, _) => _liveTimer.Stop();
    }

    private static DeviceProfile? Dev() => Devices.Detect(Ec.ReadFirmware());

    private byte[]? SafeDump() { try { return Ec.DumpAll(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Lang.T("err")); return null; } }

    // Tach register = address whose raw value changed between the two speeds AND yields a plausible RPM.
    private void CompareRpm()
    {
        _rpm.Items.Clear();
        if (_dumpA == null) { _rpm.Items.Add("(do " + Lang.T("test_rpm_a") + ")"); return; }
        var b = SafeDump();
        if (b == null) return;
        int shown = 0;
        for (int a = 0; a < 256; a++)
        {
            int ra = _dumpA[a], rb = b[a];
            if (ra == rb || ra == 0 || rb == 0) continue;
            int rpmA = 478000 / ra, rpmB = 478000 / rb;
            if (rpmA is >= 1200 and <= 7000 && rpmB is >= 1200 and <= 7000)
            {
                _rpm.Items.Add($"0x{a:X2}   {rpmA} → {rpmB} RPM");
                shown++;
            }
        }
        if (shown == 0) _rpm.Items.Add("(no changed candidates — change fan speed more, then retry)");
    }

    // List every non-sensor EC byte that changed between snapshot A and now. Sensor / dynamic
    // addresses (temps, fan duty, tach RPM, the floating 0x34) are excluded to cut the noise, so
    // the Cooler Boost flag stands out. The current device's configured CoolerBoost addr is flagged.
    private void CompareCooler()
    {
        _rpm.Items.Clear();
        if (_cbA == null) { _rpm.Items.Add("(do 'snapshot A' first)"); return; }
        var b = SafeDump();
        if (b == null) return;
        var dev = Dev();
        var skip = new HashSet<int> { 0x68, 0x80, 0x71, 0x89, 0xF4, 0xC9, 0xCB, 0x34 };
        int shown = 0;
        for (int a = 0; a < 256; a++)
        {
            if (skip.Contains(a) || _cbA[a] == b[a]) continue;
            string tag = dev != null && a == dev.CoolerBoost ? "   <= configured CoolerBoost addr" : "";
            _rpm.Items.Add($"0x{a:X2}: {_cbA[a]:X2} -> {b[a]:X2}{tag}");
            shown++;
        }
        if (shown == 0) _rpm.Items.Add("(no non-sensor byte changed — did Cooler Boost actually toggle?)");
    }

    private void SaveDump()
    {
        var dump = SafeDump();
        if (dump == null) return;
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fw = Ec.ReadFirmware();
            string path = Path.Combine(dir, $"msi-ec-dump-{(string.IsNullOrEmpty(fw) ? "unknown" : fw.Replace('.', '_'))}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"EC dump  fw={fw}  {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY)");
            for (int row = 0; row < 256; row += 16)
            {
                sb.Append($"{row:X2}: ");
                for (int c = 0; c < 16; c++) sb.Append($"{dump[row + c]:X2} ");
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            MessageBox.Show(this, string.Format(Lang.T("test_dump_saved"), path), Lang.T("test_title"));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Lang.T("err")); }
    }

    private void ShowCurve()
    {
        var dev = Dev();
        if (dev == null) { System.Media.SystemSounds.Beep.Play(); return; }
        var c = Ec.ReadFanCurve(dev);
        if (c == null) { MessageBox.Show(this, Lang.T("test_curve_none"), Lang.T("test_title")); return; }
        var (cpuT, cpuS, gpuT, gpuS) = c.Value;
        string Line(int[] t, int[] s) => string.Join("   ", t.Zip(s, (a, b) => $"{a}°C→{b}%"));
        MessageBox.Show(this,
            $"CPU / Fan 1:\n{Line(cpuT, cpuS)}\n\nGPU / Fan 2:\n{Line(gpuT, gpuS)}\n\n" + Lang.T("test_curve_hint"),
            Lang.T("test_curve_btn"));
    }

    // Route every write through the central gate so it honours BOTH Writable and _simulate
    // (MSIPS_FORCE_FIRMWARE). In simulate mode this is a no-op: no writes reach the real EC.
    private void DoWrite(Action<DeviceProfile> action)
    {
        try { _d.WithEcWrite(action); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Lang.T("err")); }
    }
}
