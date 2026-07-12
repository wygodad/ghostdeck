using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;

namespace GhostDeck;

/// <summary>
/// In-tab "Report my model" wizard: two columns (info left, capture right), themed,
/// scrollable. Read-only EC dump per MSI Center scenario, then a pre-filled GitHub issue.
/// </summary>
public sealed class ReportPage : ThemedPage
{
    private const string RepoUrl = "https://github.com/wygodad/ghostdeck";
    private const int Pad = 28, Gutter = 44;
    private int _leftW = 430;

    private static readonly (ProfileId id, string msiName)[] Steps =
    {
        (ProfileId.Silent, "SILENT"), (ProfileId.Balanced, "BALANCED"),
        (ProfileId.Extreme, "EXTREME PERFORMANCE"), (ProfileId.SuperBattery, "SUPER BATTERY"),
    };
    private static readonly byte[] SnapshotAddrs = { 0x34, 0xD2, 0xD4, 0xEB, 0xF2, 0xF4, 0xD7, 0xEF };

    private readonly byte[]?[] _dumps = new byte[Steps.Length][];
    private readonly StepRowT[] _rows;
    private readonly InfoCardT _card;
    private readonly Button _capture = new();
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private int _step;
    private bool _capturing;
    private int _lastPct = -1;
    private float _barValue;
    private string? _savedPath;
    private int _rightX, _barY, _introY, _contentTop, _rowsTop, _introH, _instrTop, _instrH;
    private static readonly Font IntroFont = new("Segoe UI", 10.5f);

    // ---- sub-tabs: 0 = profiles (default), 1 = fan curve ----
    private readonly SubTabs _subTabs = new(Lang.T("subtab_profiles"), Lang.T("subtab_curve"));
    private int _sub;
    private int _subTop;

    // ---- fan-curve verification flow ----
    // The user sets these exact, distinctive speeds in MSI Center (Extreme → Advanced). We read the EC
    // back and search the full dump for the sequences: finding them locates the per-model curve tables.
    private static readonly int[] CpuTracer = { 25, 35, 45, 55, 65, 75 };
    private static readonly int[] GpuTracer = { 20, 30, 40, 50, 60, 70 };
    private readonly Button _curveBtn = new();
    private InfoCardT _curveCard = null!;
    private int _curveStepsTop;
    private byte[]? _curveDump;
    private bool _curveCapturing;
    private int _curvePct = -1;
    private float _curveBar;
    private (int cpu, int gpu)? _curveFound;
    private string? _curveMsg;
    private bool _curveMatch;
    private string? _curveSavedPath;
    private int _curveTop, _curveBtnY, _curveBarY;

    public ReportPage(MainDeps d) : base(d)
    {
        _subTabs.Changed += i => { _sub = i; SyncSub(); Relayout(); Invalidate(); };
        Controls.Add(_subTabs);

        Ui.StylePrimary(_curveBtn);
        _curveBtn.Click += OnCurveCapture;
        Controls.Add(_curveBtn);

        _curveCard = new InfoCardT("⚠", new (string, string?)[]
        {
            (Lang.T("rep_curve_warn"), null),
            (Lang.T("rep_curve_why"), null),
        }, _leftW);
        Controls.Add(_curveCard);

        _card = new InfoCardT("ⓘ", new (string, string?)[]
        {
            (Lang.T("rep_need_msi"), null),
            (Lang.T("rep_msi_tip"), null),
            (Lang.T("rep_msi_download"), null),
            (Lang.T("rep_dl_version"), "https://msi-center.en.uptodown.com/windows/download/1045738268"),
            (Lang.T("rep_dl_repo"), "https://msi-center.en.uptodown.com/windows/versions"),
            (Lang.T("rep_msi_clean"), null),
            (Lang.T("rep_uninstaller_link"), "https://download.msi.com/uti_exe/nb/CleanCenterMaster.zip"),
        }, _leftW);
        Controls.Add(_card);

        _rows = Steps.Select((s, i) => new StepRowT(i + 1, s.msiName, Theme.Profile(D.ColorOf(s.id)))).ToArray();
        foreach (var r in _rows) Controls.Add(r);

        Ui.StylePrimary(_capture);
        _capture.Click += OnCapture;
        Controls.Add(_capture);

        _anim.Tick += (_, _) => OnAnim();
        Resize += (_, _) => Relayout();
        RefreshSteps();
        SyncSub();
    }

    /// <summary>Open a specific sub-tab (0 = profiles, 1 = fan curve). Used by deep links.</summary>
    public void SetSubTab(int sub)
    {
        _sub = Math.Clamp(sub, 0, 1);
        _subTabs.SetActive(_sub);
        SyncSub(); Relayout(); Invalidate();
    }

    // Show only the active sub-tab's controls (the rest are hand-painted, gated in OnPaint).
    private void SyncSub()
    {
        bool prof = _sub == 0;
        _card.Visible = prof;
        foreach (var r in _rows) r.Visible = prof;
        _capture.Visible = prof;
        _curveBtn.Visible = !prof;
        _curveCard.Visible = !prof;
        if (!prof) RefreshCurve();
    }

    public override void OnEnter() { Relayout(); RefreshSteps(); SyncSub(); Invalidate(); }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _card.ApplyTheme();
        for (int i = 0; i < _rows.Length; i++) { _rows[i].Tint = Theme.Profile(D.ColorOf(Steps[i].id)); _rows[i].Invalidate(); }
        Ui.StylePrimary(_capture);
        Ui.StylePrimary(_curveBtn);
        _curveCard.ApplyTheme();
        _subTabs.Invalidate();
    }

    private void Relayout()
    {
        int titleH = new Font("Segoe UI", 18f, FontStyle.Bold).Height;
        _subTop = 24 + titleH + 18;
        _subTabs.SetBounds(Pad, _subTop, _subTabs.PreferredWidth, _subTabs.Height);

        if (_sub == 0) LayoutProfiles(_subTabs.Bottom + 26);
        else LayoutCurve(_subTabs.Bottom + 26);
    }

    private void LayoutProfiles(int top)
    {
        // equal-width columns
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);

        int secH = new Font("Segoe UI", 9.5f, FontStyle.Bold).Height;
        _introY = top;
        _introH = TextRenderer.MeasureText(Lang.T("rep_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _introY + _introH + 18;
        _rowsTop = _contentTop + secH + 14;

        _card.Location = new Point(Pad, _contentTop);
        _card.SetWidth(_leftW);

        int ry = _rowsTop;
        foreach (var r in _rows) { r.SetBounds(_rightX, ry, rightW, 52); ry += 60; }
        _barY = ry + 26;
        _instrTop = _barY + 58;
        var instrFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _instrH = TextRenderer.MeasureText(Lang.T("rep_all_done"), instrFont, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
        _capture.SetBounds(_rightX, _instrTop + _instrH + 18, Math.Min(320, rightW), 44);

        int leftBottom = _card.Bottom + 70;       // + firmware pill
        int rightBottom = _capture.Bottom + 80;   // + wrapped saved-path line
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    // Two-column layout mirroring the profiles flow: left = intro + info card + firmware pill,
    // right = section label + numbered steps + capture button + result.
    private void LayoutCurve(int top)
    {
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        int secH = new Font("Segoe UI", 9.5f, FontStyle.Bold).Height;

        _curveTop = top;   // intro (left)
        _introH = TextRenderer.MeasureText(Lang.T("rep_curve_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _curveTop + _introH + 18;
        _curveCard.Location = new Point(Pad, _contentTop);
        _curveCard.SetWidth(_leftW);

        // right column: section label + 5 steps + button
        _curveStepsTop = _contentTop + secH + 14;
        _curveBtnY = _curveStepsTop + 34 * 5 + 18;
        _curveBarY = _curveBtnY + 62;
        _curveBtn.SetBounds(_rightX, _curveBtnY, Math.Min(320, rightW), 44);

        int leftBottom = _curveCard.Bottom + 70;   // + firmware pill
        int rightBottom = _curveBarY + 80;
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ApplyScroll(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        TextRenderer.DrawText(g, Lang.T("menu_report"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);

        if (_sub == 1) { PaintCurve(g); return; }

        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        // left: intro under title, info card (child) already placed, firmware pill below it
        TextRenderer.DrawText(g, Lang.T("rep_intro"), IntroFont,
            new Rectangle(Pad, _introY, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        var pill = new RectangleF(Pad, _card.Bottom + 14, _leftW, 44);
        using (var path = Theme.RoundRect(pill, 11))
        { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
        var lf = new Font("Segoe UI", 10f);
        TextRenderer.DrawText(g, Lang.T("st_firmware"), lf, new Rectangle(Pad + 16, (int)pill.Y, 180, 44), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        int lw = TextRenderer.MeasureText(Lang.T("st_firmware"), lf).Width;
        TextRenderer.DrawText(g, string.IsNullOrEmpty(D.Firmware) ? "—" : D.Firmware, new Font("Consolas", 11f, FontStyle.Bold),
            new Rectangle(Pad + 16 + lw + 12, (int)pill.Y, _leftW - lw - 40, 44), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        // right: section label
        TextRenderer.DrawText(g, Lang.T("rep_section"), new Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(_rightX, _contentTop), Theme.Muted);

        // right: progress (only while capturing)
        if (_capturing)
        {
            TextRenderer.DrawText(g, Lang.T("rep_capturing") + $"  {_lastPct}%", new Font("Segoe UI", 10f, FontStyle.Bold),
                new Point(_rightX, _barY - 30), Theme.Accent);
            var track = new RectangleF(_rightX, _barY, rightW, 12);
            using (var path = Theme.RoundRect(track, 6)) { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            float w = Math.Max(12, rightW * _barValue);
            using (var path = Theme.RoundRect(new RectangleF(_rightX, _barY, w, 12), 6)) { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
        }

        // right: instruction
        bool done = _step >= Steps.Length;
        string instr = done ? "✓  " + Lang.T("rep_all_done")
                            : string.Format(Lang.T("rep_step"), _step + 1, Steps.Length) + " — " + string.Format(Lang.T("rep_set_scenario"), Steps[_step].msiName);
        TextRenderer.DrawText(g, instr, new Font("Segoe UI", 11.5f, FontStyle.Bold),
            new Rectangle(_rightX, _instrTop, rightW, _instrH + 6), done ? Theme.Green : Theme.Text, TextFormatFlags.WordBreak);
        if (done && _savedPath != null)
            TextRenderer.DrawText(g, string.Format(Lang.T("rep_saved_to"), _savedPath), new Font("Segoe UI", 9f),
                new Rectangle(_rightX, _capture.Bottom + 10, rightW, 60), Theme.Muted, TextFormatFlags.WordBreak);
    }

    // =================================================================
    //  fan-curve verification sub-tab
    // =================================================================
    private void RefreshCurve() => _curveBtn.Text = _curveDump == null ? Lang.T("rep_curve_capture") : Lang.T("rep_curve_finish");

    private void PaintCurve(Graphics g)
    {
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);

        // ---- left column: intro + info card (child) + firmware pill ----
        TextRenderer.DrawText(g, Lang.T("rep_curve_intro"), IntroFont,
            new Rectangle(Pad, _curveTop, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        var pill = new RectangleF(Pad, _curveCard.Bottom + 14, _leftW, 44);
        using (var path = Theme.RoundRect(pill, 11))
        { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
        var lf = new Font("Segoe UI", 10f);
        TextRenderer.DrawText(g, Lang.T("st_firmware"), lf, new Rectangle(Pad + 16, (int)pill.Y, 180, 44), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        int lw = TextRenderer.MeasureText(Lang.T("st_firmware"), lf).Width;
        TextRenderer.DrawText(g, string.IsNullOrEmpty(D.Firmware) ? "—" : D.Firmware, new Font("Consolas", 11f, FontStyle.Bold),
            new Rectangle(Pad + 16 + lw + 12, (int)pill.Y, _leftW - lw - 40, 44), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        // ---- right column: section label + numbered steps ----
        TextRenderer.DrawText(g, Lang.T("rep_curve_steps"), new Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(_rightX, _contentTop), Theme.Muted);
        string[] steps = { Lang.T("rep_curve_s1"), Lang.T("rep_curve_s2"), Lang.T("rep_curve_s3"), Lang.T("rep_curve_s4"), Lang.T("rep_curve_s5") };
        var numFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        var stFont = new Font("Segoe UI", 10.5f);
        for (int i = 0; i < steps.Length; i++)
        {
            int ry = _curveStepsTop + i * 34;
            var circ = new RectangleF(_rightX, ry, 24, 24);
            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, circ);
            TextRenderer.DrawText(g, (i + 1).ToString(), numFont, Rectangle.Round(circ), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, steps[i], stFont, new Rectangle(_rightX + 36, ry - 4, rightW - 40, 34), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }

        // ---- right column: progress / result (under the capture button) ----
        if (_curveCapturing)
        {
            TextRenderer.DrawText(g, Lang.T("rep_capturing") + $"  {_curvePct}%", new Font("Segoe UI", 10f, FontStyle.Bold), new Point(_rightX, _curveBarY - 4), Theme.Accent);
            var track = new RectangleF(_rightX, _curveBarY + 20, rightW, 12);
            using (var path = Theme.RoundRect(track, 6)) { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            float fw = Math.Max(12, rightW * _curveBar);
            using (var path = Theme.RoundRect(new RectangleF(_rightX, _curveBarY + 20, fw, 12), 6)) { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
        }
        else if (_curveMsg != null)
        {
            var col = _curveFound != null ? (_curveMatch ? Theme.Green : Theme.Amber) : Theme.Red;
            var mf = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            int mh = TextRenderer.MeasureText(_curveMsg, mf, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, _curveMsg, mf, new Rectangle(_rightX, _curveBarY, rightW, mh + 6), col, TextFormatFlags.WordBreak);
            if (_curveSavedPath != null)
                TextRenderer.DrawText(g, string.Format(Lang.T("rep_saved_to"), _curveSavedPath), new Font("Segoe UI", 9f),
                    new Rectangle(_rightX, _curveBarY + mh + 10, rightW, 60), Theme.Muted, TextFormatFlags.WordBreak);
        }
    }

    private void OnCurveCapture(object? sender, EventArgs e)
    {
        if (_curveDump != null) { FinishCurve(); return; }
        if (_curveCapturing) return;
        _curveCapturing = true; _curveBtn.Enabled = false; _curvePct = 0; _curveBar = 0; _curveMsg = null; Invalidate();
        Task.Run(() =>
        {
            try { var d = Ec.DumpAll(CurveProgress); BeginInvoke(() => CurveDone(d, null)); }
            catch (Exception ex) { BeginInvoke(() => CurveDone(null, ex)); }
        });
    }

    private void CurveProgress(int byteIdx)
    {
        int pct = (int)((byteIdx + 1) / 256f * 100);
        if (pct == _curvePct) return;
        _curvePct = pct;
        BeginInvoke(() => { _curveBar = pct / 100f; Invalidate(); });
    }

    private void CurveDone(byte[]? dump, Exception? ex)
    {
        _curveCapturing = false; _curveBtn.Enabled = true;
        if (ex != null || dump == null)
        {
            MessageBox.Show(string.Format(Lang.T("rep_read_fail"), ex?.Message ?? ""), Lang.T("err"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Invalidate(); return;
        }
        _curveDump = dump;
        int cpu = FindTracer(dump, CpuTracer), gpu = FindTracer(dump, GpuTracer);
        if (cpu >= 0 && gpu >= 0)
        {
            _curveFound = (cpu, gpu);
            var fc = Devices.Detect(D.Firmware)?.FanCurve;
            _curveMatch = fc != null && cpu == fc.CpuSpeedBase && gpu == fc.GpuSpeedBase;
            _curveMsg = string.Format(Lang.T("rep_curve_found"), cpu, gpu) + "  " + Lang.T(_curveMatch ? "rep_curve_match" : "rep_curve_nomatch");
        }
        else { _curveFound = null; _curveMatch = false; _curveMsg = Lang.T("rep_curve_notfound"); }
        PrepareCurveReport();
        RefreshCurve();
        Invalidate();
    }

    // Scan the 256-byte dump for the tracer speeds. Exact 6-value run first; fall back to the first 5.
    private static int FindTracer(byte[] dump, int[] seq)
    {
        for (int len = seq.Length; len >= 5; len--)
            for (int a = 0; a + len <= 256; a++)
            {
                bool ok = true;
                for (int i = 0; i < len; i++) if (dump[a + i] != seq[i]) { ok = false; break; }
                if (ok) return a;
            }
        return -1;
    }

    private string BuildCurveReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== GhostDeck — fan-curve verification report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY, no EC writes)");
        sb.AppendLine($"App version: {D.AppVersion()}");
        sb.AppendLine($"EC firmware: {(string.IsNullOrEmpty(D.Firmware) ? "(unknown)" : D.Firmware)}");
        sb.AppendLine($"Detected in app: {(string.IsNullOrEmpty(ModelName()) ? "(unsupported / unknown)" : ModelName())}");
        sb.AppendLine();
        sb.AppendLine("Test curve set in MSI Center (Extreme → Advanced):");
        sb.AppendLine($"  Fan 1 (CPU): {string.Join(" ", CpuTracer)}");
        sb.AppendLine($"  Fan 2 (GPU): {string.Join(" ", GpuTracer)}");
        sb.AppendLine();
        if (_curveFound is { } f)
        {
            sb.AppendLine($"Located in EC dump:  CPU speed table @ 0x{f.cpu:X2}   GPU speed table @ 0x{f.gpu:X2}");
            var fc = Devices.Detect(D.Firmware)?.FanCurve;
            if (fc != null) sb.AppendLine($"Shipped map for this model:  CPU 0x{fc.CpuSpeedBase:X2}  GPU 0x{fc.GpuSpeedBase:X2}  → {(_curveMatch ? "MATCH" : "DIFFERENT")}");
            else sb.AppendLine("Shipped map for this model:  (none — model not recognised)");
        }
        else sb.AppendLine("Test curve NOT located in the dump (was the curve Saved in MSI Center?).");
        sb.AppendLine();
        sb.AppendLine("--- Full EC dump (256 bytes) ---");
        for (int row = 0; row < 256; row += 16)
        {
            sb.Append($"{row:X2}: ");
            for (int c = 0; c < 16; c++) sb.Append($"{_curveDump![row + c]:X2} ");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void PrepareCurveReport()
    {
        string report = BuildCurveReport();
        try { Clipboard.SetText(report); } catch { }
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fwTag = string.IsNullOrEmpty(D.Firmware) ? "unknown" : D.Firmware.Replace('.', '_');
            _curveSavedPath = Path.Combine(dir, $"ghostdeck-curve-report-{fwTag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(_curveSavedPath, report, new UTF8Encoding(false));
        }
        catch { _curveSavedPath = null; }
    }

    private void FinishCurve()
    {
        try { Process.Start(new ProcessStartInfo(BuildCurveIssueUrl()) { UseShellExecute = true }); } catch { }
    }

    private string BuildCurveIssueUrl()
    {
        string title = $"[Curve] {ModelName()} ({D.Firmware})";
        string found = _curveFound is { } f
            ? $"CPU @ 0x{f.cpu:X2}, GPU @ 0x{f.gpu:X2}" + (_curveMatch ? " (matches shipped map)" : " (differs from shipped map)")
            : "not located in dump";
        // NB: the paste field (id "dump") is deliberately NOT prefilled — the full report is on the
        // clipboard / saved to file, and any reload of a prefilled URL would wipe what the user pasted.
        return RepoUrl + "/issues/new?template=curve-support.yml&labels=curve-support"
            + "&title=" + Uri.EscapeDataString(title)
            + "&model=" + Uri.EscapeDataString(ModelName())
            + "&firmware=" + Uri.EscapeDataString(D.Firmware)
            + "&found=" + Uri.EscapeDataString(found);
    }

    // ---- step state ----
    private void RefreshSteps()
    {
        for (int i = 0; i < Steps.Length; i++) _rows[i].SetState(_dumps[i] != null, i == _step);
        _capture.Text = _step >= Steps.Length ? Lang.T("rep_finish") : Lang.T("rep_capture");
        EnsureAnim();
        Invalidate();
    }

    private void EnsureAnim() { if (!_anim.Enabled) _anim.Start(); }
    private void OnAnim() { bool busy = _capturing; foreach (var r in _rows) busy |= r.Animate(); if (!busy) _anim.Stop(); }

    // ---- capture ----
    private void OnCapture(object? sender, EventArgs e)
    {
        if (_step >= Steps.Length) { Finish(); return; }
        if (_capturing) return;
        _capturing = true; _capture.Enabled = false; _lastPct = 0; _barValue = 0;
        EnsureAnim(); Invalidate();
        int idx = _step;
        Task.Run(() =>
        {
            try { var dump = Ec.DumpAll(ReportProgress); BeginInvoke(() => CaptureDone(idx, dump, null)); }
            catch (Exception ex) { BeginInvoke(() => CaptureDone(idx, null, ex)); }
        });
    }

    private void ReportProgress(int byteIdx)
    {
        int pct = (int)((byteIdx + 1) / 256f * 100);
        if (pct == _lastPct) return;
        _lastPct = pct;
        BeginInvoke(() => { _barValue = pct / 100f; Invalidate(); });
    }

    private void CaptureDone(int idx, byte[]? dump, Exception? ex)
    {
        _capturing = false; _capture.Enabled = true;
        if (ex != null || dump == null)
        {
            MessageBox.Show(string.Format(Lang.T("rep_read_fail"), ex?.Message ?? ""), Lang.T("err"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Invalidate(); return;
        }
        _dumps[idx] = dump; _step++;
        RefreshSteps();
        if (_step >= Steps.Length) PrepareReport();
    }

    private void PrepareReport()
    {
        string report = BuildReport();
        try { Clipboard.SetText(report); } catch { }
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fwTag = string.IsNullOrEmpty(D.Firmware) ? "unknown" : D.Firmware.Replace('.', '_');
            _savedPath = Path.Combine(dir, $"msi-model-report-{fwTag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(_savedPath, report, new UTF8Encoding(false));
        }
        catch { _savedPath = null; }
        Invalidate();
    }

    private void Finish()
    {
        try { Process.Start(new ProcessStartInfo(BuildIssueUrl()) { UseShellExecute = true }); } catch { }
    }

    protected override void Dispose(bool disposing) { if (disposing) _anim.Dispose(); base.Dispose(disposing); }

    // ---- report building (read-only) ----
    private string ModelName() { var s = D.Status(); return s.Known ? s.Device : ""; }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MSI Profile Switcher — model support report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY, no EC writes)");
        sb.AppendLine($"App version: {D.AppVersion()}");
        sb.AppendLine($"EC firmware: {(string.IsNullOrEmpty(D.Firmware) ? "(unknown)" : D.Firmware)}");
        sb.AppendLine($"Detected in app: {(string.IsNullOrEmpty(ModelName()) ? "(unsupported / unknown)" : ModelName())}");
        sb.AppendLine();
        sb.AppendLine("--- Diff: addresses that change between scenarios ---");
        sb.AppendLine("(temps/fans naturally fluctuate — ignore sensor-looking single-value drift)");
        sb.Append("Addr   ");
        foreach (var (_, name) in Steps) sb.Append(name.PadRight(20));
        sb.AppendLine();
        for (int a = 0; a < 256; a++)
        {
            if (AllEqualAt(a)) continue;
            sb.Append($"0x{a:X2}   ");
            foreach (var dump in _dumps) sb.Append((dump == null ? "--" : $"{dump[a]:X2}").PadRight(20));
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("--- Full EC dumps (256 bytes each) ---");
        for (int i = 0; i < Steps.Length; i++)
        {
            sb.AppendLine(); sb.AppendLine($"[{Steps[i].msiName}]");
            var dump = _dumps[i];
            if (dump == null) { sb.AppendLine("(not captured)"); continue; }
            for (int row = 0; row < 256; row += 16)
            {
                sb.Append($"{row:X2}: ");
                for (int c = 0; c < 16; c++) sb.Append($"{dump[row + c]:X2} ");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private bool AllEqualAt(int addr)
    {
        byte? first = null;
        foreach (var dump in _dumps)
        {
            if (dump == null) continue;
            if (first == null) first = dump[addr];
            else if (dump[addr] != first) return false;
        }
        return true;
    }

    private string BuildSnapshot()
    {
        var sb = new StringBuilder();
        sb.Append("Addr ");
        foreach (var (_, name) in Steps) sb.Append(name.PadRight(9));
        sb.AppendLine();
        foreach (var a in SnapshotAddrs)
        {
            sb.Append($"{a:X2}   ");
            foreach (var dump in _dumps) sb.Append((dump == null ? "--" : $"{dump[a]:X2}").PadRight(9));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string BuildIssueUrl()
    {
        string title = $"[Model] {ModelName()} ({D.Firmware})";
        // NB: the paste field (id "fulldump") is deliberately NOT prefilled — the full report is on the
        // clipboard / saved to file, and any reload of a prefilled URL would wipe what the user pasted.
        string Base() => RepoUrl + "/issues/new?template=model-support.yml&labels=model-support"
            + "&title=" + Uri.EscapeDataString(title)
            + "&model=" + Uri.EscapeDataString(ModelName())
            + "&firmware=" + Uri.EscapeDataString(D.Firmware);
        string url = Base() + "&snapshot=" + Uri.EscapeDataString(BuildSnapshot());
        return url.Length > 7000 ? Base() : url;
    }

    // =================================================================
    //  themed custom controls
    // =================================================================
    private sealed class StepRowT : Control
    {
        private const int StatusW = 150, Circle = 26, Cx = 28;
        private readonly int _num;
        private readonly string _name;
        public Color Tint;
        private bool _done, _current;
        private float _doneA, _glowA;

        public StepRowT(int num, string name, Color color)
        { _num = num; _name = name; Tint = color; DoubleBuffered = true; ResizeRedraw = true; }

        public void SetState(bool done, bool current) { _done = done; _current = current; Invalidate(); }

        public bool Animate()
        {
            bool a = Approach(ref _doneA, _done ? 1 : 0, _done ? 0.10f : 0.20f);
            bool b = Approach(ref _glowA, _current ? 1 : 0, _current ? 0.12f : 0.18f);
            if (a || b) Invalidate();
            return a || b;
        }
        private static bool Approach(ref float v, float t, float s)
        { if (v == t) return false; v += (t - v) * s; if (Math.Abs(t - v) < 0.005f) { v = t; return false; } return true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Surface);
            if (_glowA > 0.01f)
            {
                using var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12);
                int al = (int)(_glowA * 255);
                using var b = new SolidBrush(Color.FromArgb((int)(al * 0.12), Theme.Accent)); g.FillPath(b, path);
                using var pen = new Pen(Color.FromArgb((int)(al * 0.6), Theme.Accent), 1.4f); g.DrawPath(pen, path);
            }
            int cy = Height / 2;
            var circle = new RectangleF(Cx - Circle / 2f, cy - Circle / 2f, Circle, Circle);
            if (_done)
            {
                float pop = 1 - (1 - _doneA) * (1 - _doneA);
                float dd = Circle * (0.85f + 0.15f * pop);
                using (var b = new SolidBrush(Tint)) g.FillEllipse(b, Cx - dd / 2f, cy - dd / 2f, dd, dd);
                using var pen = new Pen(System.Drawing.Color.White, Math.Max(2f, dd * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float ss = dd * 0.26f * pop;
                g.DrawLines(pen, new[] { new PointF(Cx - ss, cy + ss * 0.1f), new PointF(Cx - ss * 0.2f, cy + ss * 0.8f), new PointF(Cx + ss, cy - ss * 0.7f) });
            }
            else if (_current)
            {
                using var pen = new Pen(Theme.Accent, 2.5f); g.DrawEllipse(pen, circle);
                using var b = new SolidBrush(System.Drawing.Color.FromArgb(60, Theme.Accent));
                float id = Circle * 0.42f; g.FillEllipse(b, Cx - id / 2f, cy - id / 2f, id, id);
            }
            else
            {
                using var pen = new Pen(Theme.BorderStrong, 2f); g.DrawEllipse(pen, circle);
                TextRenderer.DrawText(g, _num.ToString(), new Font("Segoe UI", 9f, FontStyle.Bold), Rectangle.Round(circle), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            int nx = Cx + Circle / 2 + 16;
            TextRenderer.DrawText(g, _name, new Font("Segoe UI", 11f, FontStyle.Bold),
                new Rectangle(nx, 6, Width - nx - StatusW - 6, Height - 12), _done || _current ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Lang.T(_done ? "rep_captured" : "rep_pending"), new Font("Segoe UI", 9.5f, _done ? FontStyle.Bold : FontStyle.Regular),
                new Rectangle(Width - StatusW, 6, StatusW - 10, Height - 12), _done ? Theme.Green : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    private sealed class InfoCardT : Control
    {
        private const int LeftPad = 46, RightPad = 18, TopPad = 15;
        private readonly string _icon;
        private readonly (string text, string? url)[] _items;
        private readonly List<(Rectangle rect, string text)> _paras = new();
        private readonly List<LinkLabel> _links = new();
        private readonly Font _font = new("Segoe UI", 10.5f);

        public InfoCardT(string icon, (string text, string? url)[] items, int width)
        {
            _icon = icon; _items = items; DoubleBuffered = true; ResizeRedraw = true; Width = width;
            Build();
        }

        private void Build()
        {
            foreach (var l in _links) Controls.Remove(l);
            _links.Clear(); _paras.Clear();
            int innerW = Width - LeftPad - RightPad;
            var linkFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            int y = TopPad, gap = 0;
            foreach (var (text, url) in _items)
            {
                if (url == null)
                {
                    int h = TextRenderer.MeasureText(text, _font, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
                    _paras.Add((new Rectangle(LeftPad, y, innerW, h), text));
                    y += h; gap = 10;
                }
                else
                {
                    int h = TextRenderer.MeasureText(text, linkFont, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
                    var link = new LinkLabel { Text = text, AutoSize = false, Size = new Size(innerW, h), Location = new Point(LeftPad, y), Font = linkFont, LinkBehavior = LinkBehavior.HoverUnderline };
                    string target = url;
                    link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } };
                    Controls.Add(link); _links.Add(link);
                    y += h; gap = 6;
                }
                y += gap;
            }
            Height = y - gap + TopPad;
            ApplyTheme();
        }

        public void SetWidth(int w) { if (Width == w) return; Width = w; Build(); }

        public void ApplyTheme()
        {
            var (bg, _, _) = Colors();
            foreach (var l in _links) { l.BackColor = bg; l.LinkColor = Theme.Accent; l.ActiveLinkColor = Theme.Accent; }
            Invalidate();
        }

        private static (Color bg, Color bd, Color fg) Colors() => Theme.Dark
            ? (Color.FromArgb(0x33, 0x2C, 0x1A), Color.FromArgb(0x5A, 0x4A, 0x22), Color.FromArgb(0xE0, 0xB0, 0x55))
            : (Color.FromArgb(0xFE, 0xF6, 0xE7), Color.FromArgb(0xF3, 0xDC, 0xA9), Color.FromArgb(0xB0, 0x6A, 0x10));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Surface);
            var (bg, bd, fg) = Colors();
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, 12)) { using var b = new SolidBrush(bg); g.FillPath(b, path); using var p = new Pen(bd); g.DrawPath(p, path); }
            TextRenderer.DrawText(g, _icon, new Font("Segoe UI", 13f, FontStyle.Bold), new Rectangle(14, TopPad - 1, 26, 22), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            foreach (var (rect, text) in _paras)
                TextRenderer.DrawText(g, text, _font, rect, fg, TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left);
        }
    }
}
