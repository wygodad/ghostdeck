using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;

namespace MSIProfileSwitcher;

/// <summary>
/// In-tab "Report my model" wizard: two columns (info left, capture right), themed,
/// scrollable. Read-only EC dump per MSI Center scenario, then a pre-filled GitHub issue.
/// </summary>
public sealed class ReportPage : ThemedPage
{
    private const string RepoUrl = "https://github.com/wygodad/msi-profile-switcher";
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

    public ReportPage(MainDeps d) : base(d)
    {
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
    }

    public override void OnEnter() { Relayout(); RefreshSteps(); Invalidate(); }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _card.ApplyTheme();
        for (int i = 0; i < _rows.Length; i++) { _rows[i].Tint = Theme.Profile(D.ColorOf(Steps[i].id)); _rows[i].Invalidate(); }
        Ui.StylePrimary(_capture);
    }

    private void Relayout()
    {
        // equal-width columns
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);

        int titleH = new Font("Segoe UI", 18f, FontStyle.Bold).Height;
        int secH = new Font("Segoe UI", 9.5f, FontStyle.Bold).Height;
        _introY = 24 + titleH + 10;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ApplyScroll(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);

        TextRenderer.DrawText(g, Lang.T("menu_report"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);

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
        string fulldump = _savedPath != null
            ? $"Full report copied to clipboard and saved to:\n{_savedPath}\n\nPlease paste it here with Ctrl+V."
            : "Full report copied to clipboard — please paste it here with Ctrl+V.";
        string Base() => RepoUrl + "/issues/new?template=model-support.yml&labels=model-support"
            + "&title=" + Uri.EscapeDataString(title)
            + "&model=" + Uri.EscapeDataString(ModelName())
            + "&firmware=" + Uri.EscapeDataString(D.Firmware)
            + "&fulldump=" + Uri.EscapeDataString(fulldump);
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
