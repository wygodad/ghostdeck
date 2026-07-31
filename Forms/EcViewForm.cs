using System.Text;

namespace GhostDeck;

/// <summary>
/// Live EC viewer (default hotkey Ctrl+Shift+E; also reachable from the Ctrl+Shift+T test
/// dialog): the full 256-byte EC dump refreshed every ~1.5 s, with bytes that just changed
/// highlighted in amber and every change appended to a log ("0xF3: 80 → 82"). Made for one
/// job: press an Fn key (backlight, camera, fans) and see immediately which EC register
/// reacted - no diagnostic zips to diff by hand. Read-only by construction (Ec.DumpAll
/// never writes).
/// Two measures keep the log readable on a live machine:
///  - the model's known sensor registers (temps, fan speeds, tachometers) are muted from
///    the log up front - they change constantly and are never the answer;
///  - any other address that keeps changing tick after tick gets muted by a frequency gate.
/// Muted addresses stay highlighted in the grid and are listed in the "muted" line. The
/// Marker button drops a separator: click Marker, press the key once, and the first
/// non-muted line below the marker is the register that reacted.
/// All sizes derive from font metrics, so the whole 16x16 grid fits at any DPI.
/// </summary>
public sealed class EcViewForm : Form
{
    private static EcViewForm? _open;

    public static void ShowSingleton()
    {
        if (_open is { IsDisposed: false }) { _open.BringToFront(); _open.Activate(); return; }
        _open = new EcViewForm();
        _open.FormClosed += (_, _) => _open = null;
        _open.Show();
    }

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private readonly TextBox _log = new();
    private readonly Button _marker = new();
    private readonly Label _noiseLbl = new();
    private byte[]? _prev, _cur;
    private readonly int[] _age = new int[256];   // >0 = changed recently (fades over a few ticks)
    private readonly int[] _noise = new int[256]; // change-frequency score; >=5 = muted from the log
    private readonly int[] _total = new int[256]; // lifetime change count; >=10 = muted for good
    private readonly HashSet<int> _sensors = new();   // known sensor registers of this model, muted up front
    private readonly Dictionary<int, string> _labels = new();   // address -> what it is (for the log)
    private int _busy, _tick;
    private readonly Font _mono = new("Consolas", 9.5f);
    private readonly Font _monoBold = new("Consolas", 9.5f, FontStyle.Bold);
    private readonly Font _hintFont = new("Segoe UI", 9f);
    private readonly int _gridTop, _cellW, _rowH, _hdrW;

    private EcViewForm()
    {
        Text = "GhostDeck — " + Lang.T("ec_view_title");
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Theme.Surface;
        Icon = TrayIconFactory.AppIcon();
        DoubleBuffered = true;

        // The model map tells us what many registers ARE - mute the realtime ones from the log
        // and label everything known, so a log line reads "0xC9: A0 → A3  (fan 1 tachometer)".
        // Labels are English on purpose: this log is what gets pasted into GitHub issues.
        void L(byte a, string s) { if (a != 0) _labels.TryAdd(a, s); }
        try
        {
            string fw = Ec.ReadFirmware();
            if (Devices.Detect(fw) is { } dev)
            {
                foreach (byte a in new[] { dev.CpuTemp, dev.GpuTemp, dev.CpuFan, dev.GpuFan, dev.CpuRpmAddr, dev.GpuRpmAddr })
                    if (a != 0) _sensors.Add(a);
                L(dev.CpuTemp, "CPU temperature");
                L(dev.GpuTemp, "GPU temperature");
                L(dev.CpuFan, "CPU fan duty");
                L(dev.GpuFan, "GPU fan duty");
                L(dev.CpuRpmAddr, "fan 1 tachometer");
                L(dev.GpuRpmAddr, "fan 2 tachometer");
                L(dev.ShiftMode, "shift mode (profile)");
                L(dev.FanMode, "fan mode / curve mode");
                L(dev.ChargeCtrl, "battery charge limit");
                L(dev.CoolerBoost, "Fan Boost register");
                if (dev.FanCurve is { } fc)
                    for (int p = 0; p < fc.Points; p++)
                    {
                        L((byte)(fc.CpuTempBase + p), $"CPU curve temp pt {p + 1}");
                        L((byte)(fc.CpuSpeedBase + p), $"CPU curve speed pt {p + 1}");
                        L((byte)(fc.GpuTempBase + p), $"GPU curve temp pt {p + 1}");
                        L((byte)(fc.GpuSpeedBase + p), $"GPU curve speed pt {p + 1}");
                    }
                foreach (var recipe in dev.Recipes.Values)
                    foreach (var (addr, _) in recipe)
                        L(addr, "profile recipe byte");
            }
            L(Devices.KbdBacklightFor(fw), "keyboard backlight level");
        }
        catch { }
        L(0x2E, "webcam switch");
        L(0x2F, "webcam block");
        for (byte a = 0xA0; a <= 0xBB; a++) L(a, "firmware id string");

        // metric-driven sizing: the full grid must fit regardless of DPI
        _cellW = TextRenderer.MeasureText("00", _monoBold).Width + 12;
        _rowH = _mono.Height + 4;
        _hdrW = TextRenderer.MeasureText("F0:", _mono).Width + 10;
        _gridTop = 10 + _hintFont.Height * 2 + 12;
        int gridW = 16 + _hdrW + 16 * _cellW + 16;
        int gridBottom = _gridTop + 17 * _rowH + 8;
        ClientSize = new Size(Math.Max(700, gridW), gridBottom + 40 + 230);

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.BackColor = Theme.Card;
        _log.ForeColor = Theme.Text;
        _log.Font = new Font("Consolas", 9f);
        Controls.Add(_log);

        _marker.Text = Lang.T("ec_view_marker");
        _marker.AutoSize = true;
        _marker.Padding = new Padding(12, 2, 12, 2);
        Ui.StylePrimary(_marker);
        _marker.Click += (_, _) => AppendLog("────────  MARKER  ────────");
        Controls.Add(_marker);

        _noiseLbl.AutoSize = true;
        _noiseLbl.Font = new Font("Segoe UI", 8.5f);
        _noiseLbl.ForeColor = Theme.Muted;
        _noiseLbl.BackColor = Theme.Surface;
        Controls.Add(_noiseLbl);
        UpdateNoiseLabel();

        Layout2();

        _timer.Tick += (_, _) => Sample();
        _timer.Start();
        Sample();
    }

    private void Layout2()
    {
        int gridBottom = _gridTop + 17 * _rowH + 8;
        _marker.Location = new Point(ClientSize.Width - 16 - _marker.PreferredSize.Width, gridBottom);
        _noiseLbl.MaximumSize = new Size(ClientSize.Width - 32 - _marker.PreferredSize.Width - 12, 0);
        _noiseLbl.Location = new Point(16, gridBottom + 4);
        int logTop = gridBottom + Math.Max(_marker.PreferredSize.Height, _noiseLbl.PreferredSize.Height) + 8;
        _log.SetBounds(16, logTop, ClientSize.Width - 32, ClientSize.Height - logTop - 12);
    }

    // Muted = a known sensor, a frequency-gate hit, or an address that has simply changed too
    // many times since the window opened (slow-cycling sensors like battery/thermal counters
    // change every few samples - the score alone never catches them, the total does).
    private bool Muted(int i) => _sensors.Contains(i) || _noise[i] >= 5 || _total[i] >= 10;

    private void UpdateNoiseLabel()
    {
        var muted = Enumerable.Range(0, 256).Where(Muted).Select(i => $"0x{i:X2}").ToList();
        string txt = muted.Count == 0 ? "—"
                   : muted.Count <= 16 ? string.Join(" ", muted)
                   : string.Join(" ", muted.Take(16)) + " …(+" + (muted.Count - 16) + ")";
        _noiseLbl.Text = string.Format(Lang.T("ec_view_noise"), txt);
        Layout2();   // the label can wrap to a second line - keep the log below it
    }

    private void Sample()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        Task.Run(() =>
        {
            byte[]? dump = null;
            string? err = null;
            try { dump = Ec.DumpAll(); }
            catch (Exception ex) { err = ex.Message; }
            try
            {
                BeginInvoke(() =>
                {
                    Interlocked.Exchange(ref _busy, 0);
                    if (dump == null)
                    {
                        AppendLog(Lang.T("log_read_fail") + (err != null ? "  (" + err + ")" : ""));
                        return;
                    }
                    _prev = _cur;
                    _cur = dump;
                    _tick++;
                    bool mutedChanged = false;
                    for (int i = 0; i < 256; i++)
                    {
                        if (_age[i] > 0) _age[i]--;
                        bool wasMuted = Muted(i);
                        if (_tick % 2 == 0 && _noise[i] > 0) _noise[i]--;   // slow decay: a busy byte stays muted
                        if (_prev != null && _prev[i] != _cur[i])
                        {
                            _age[i] = 3;
                            _total[i]++;
                            // A byte that flips once (an Fn key doing its thing) always gets a
                            // line; anything changing repeatedly saturates the gate or the total
                            // counter and goes quiet.
                            if (!Muted(i))
                                AppendLog($"0x{i:X2}: {_prev[i]:X2} → {_cur[i]:X2}  ("
                                          + (_labels.TryGetValue(i, out var lbl) ? lbl : "unmapped") + ")");
                            _noise[i] = Math.Min(10, _noise[i] + 2);
                        }
                        if (wasMuted != Muted(i)) mutedChanged = true;
                    }
                    if (mutedChanged) UpdateNoiseLabel();
                    Invalidate();
                });
            }
            catch { Interlocked.Exchange(ref _busy, 0); }   // form closed mid-read
        });
    }

    private void AppendLog(string line)
    {
        string stamped = DateTime.Now.ToString("HH:mm:ss") + "   " + line + Environment.NewLine;
        // keep the log bounded; newest at the bottom, caret follows
        if (_log.TextLength > 60_000) _log.Clear();
        _log.AppendText(stamped);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        TextRenderer.DrawText(g, Lang.T("ec_view_hint"), _hintFont,
            new Rectangle(16, 10, ClientSize.Width - 32, _hintFont.Height * 2 + 4), Theme.Muted,
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        int x0 = 16 + _hdrW, y0 = _gridTop;
        // column header
        for (int c = 0; c < 16; c++)
            TextRenderer.DrawText(g, c.ToString("X"), _mono, new Rectangle(x0 + c * _cellW, y0, _cellW, _rowH),
                Theme.Faint, TextFormatFlags.HorizontalCenter);
        for (int r = 0; r < 16; r++)
        {
            int y = y0 + (r + 1) * _rowH;
            TextRenderer.DrawText(g, (r * 16).ToString("X2") + ":", _mono, new Rectangle(16, y, _hdrW, _rowH),
                Theme.Faint, TextFormatFlags.Left);
            for (int c = 0; c < 16; c++)
            {
                int i = r * 16 + c;
                var rect = new Rectangle(x0 + c * _cellW, y, _cellW, _rowH);
                bool hot = _age[i] > 0;
                if (hot)
                {
                    using var b = new SolidBrush(Color.FromArgb(40 + _age[i] * 25, Theme.Amber));
                    g.FillRectangle(b, rect);
                }
                TextRenderer.DrawText(g, _cur != null ? _cur[i].ToString("X2") : "··",
                    hot ? _monoBold : _mono, rect, hot ? Theme.Amber : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _mono.Dispose();
            _monoBold.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
