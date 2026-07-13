using System.Drawing.Drawing2D;

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
        Restyle();

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
        RefreshMode();
        LayoutButtons();
        Invalidate();
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
    }

    private void LayoutButtons()
    {
        int by = Height - 62, bh = 42;
        _enable.Location = new Point(Pad, by + (bh - _enable.Height) / 2);
        _enableLabel.Location = new Point(Pad + _enable.Width + 12, by + (bh - _enableLabel.Height) / 2);
        _default.SetBounds(Width - Pad - 170, by, 170, bh);
        int rw = TextRenderer.MeasureText(_report.Text, _report.Font).Width + 40;
        _report.SetBounds(_default.Left - 14 - rw, by, rw, bh);
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
        int top = 128, bottom = Height - 86, gap = 40;
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
