using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;

namespace MSIProfileSwitcher;

/// <summary>
/// In-app "Report my model" wizard. Walks the user through a READ-ONLY EC dump
/// per MSI Center scenario (same data as scripts/diagnostics/), builds a report,
/// copies it to the clipboard, saves it to a file, and opens a pre-filled GitHub
/// issue. Replaces the manual PowerShell + copy-paste flow (see TECHNICAL.md §11).
///
/// Layout: two columns — left = info (intro + MSI Center notes/links + firmware),
/// right = action (capture steps + live progress + instruction). Fixed size, sized
/// so the whole flow is visible at once (no scrolling, no jump when capture starts).
/// </summary>
public sealed class ReportForm : Form
{
    private const string RepoUrl = "https://github.com/wygodad/ghostdeck";

    private static readonly (ProfileId id, string msiName)[] Steps =
    {
        (ProfileId.Silent,       "SILENT"),
        (ProfileId.Balanced,     "BALANCED"),
        (ProfileId.Extreme,      "EXTREME PERFORMANCE"),
        (ProfileId.SuperBattery, "SUPER BATTERY"),
    };

    private static readonly byte[] SnapshotAddrs = { 0x34, 0xD2, 0xD4, 0xEB, 0xF2, 0xF4, 0xD7, 0xEF };

    // ---- palette ----
    private static readonly Color Accent     = Color.FromArgb(0x7C, 0x3A, 0xED);
    private static readonly Color AccentDark = Color.FromArgb(0x6D, 0x28, 0xD9);
    private static readonly Color Ink        = Color.FromArgb(0x1F, 0x24, 0x30);
    private static readonly Color Muted      = Color.FromArgb(0x6B, 0x72, 0x80);
    private static readonly Color Green      = Color.FromArgb(0x2E, 0xA0, 0x43);

    // ---- geometry ----
    private const int HeaderH = 64, FooterH = 82;
    private const int LeftX = 32, ColW = 448, RightX = 528, RightW = 540;
    private const int DividerX = 504;

    private readonly string _firmware;
    private readonly string _detectedModel;
    private readonly string _appVersion;
    private readonly byte[]?[] _dumps = new byte[Steps.Length][];

    private int _step;
    private string? _savedPath;

    private readonly Panel _content = new();
    private readonly StepRow[] _rows = new StepRow[Steps.Length];
    private readonly Label _instruction = new();
    private readonly Label _progressLabel = new();
    private readonly RoundedBar _bar = new();
    private readonly Button _capture = new();
    private readonly Button _cancel = new();
    private readonly ToolTip _tip = new();
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };

    private bool _capturing;
    private int _lastPct = -1;

    public ReportForm(string firmware, string detectedModel, string appVersion)
    {
        _firmware = firmware;
        _detectedModel = detectedModel;
        _appVersion = appVersion;

        FormBorderStyle = FormBorderStyle.Sizable;   // height adjustable; width locked in OnLoad
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false; MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1100, 990);
        Font = new Font("Segoe UI", 10.5f);
        BackColor = Color.White;
        Text = Lang.T("rep_title");
        Icon = TrayIconFactory.Create(Accent);
        DoubleBuffered = true;

        BuildHeader();
        BuildFooter();
        BuildContent();

        _anim.Tick += (_, _) => OnAnim();
        RefreshSteps();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Lock the width (min.Width == max.Width) but allow the height to be resized.
        // NB: a 0 in MaximumSize is NOT treated as "unlimited" here, so use real bounds.
        int w = Width;
        MinimumSize = new Size(w, 480);
        MaximumSize = new Size(w, 4000);
    }

    // ---------------- header / footer ----------------
    private void BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = HeaderH };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var br = new LinearGradientBrush(header.ClientRectangle, Accent, AccentDark, 25f);
            g.FillRectangle(br, header.ClientRectangle);

            var titleFont = new Font("Segoe UI", 17f, FontStyle.Bold);
            var subFont = new Font("Segoe UI", 11f);
            string title = Lang.T("rep_title");
            int tw = TextRenderer.MeasureText(g, title, titleFont).Width;
            const int flags = (int)(TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(LeftX, 0, tw + 4, HeaderH),
                Color.White, (TextFormatFlags)flags);
            TextRenderer.DrawText(g, "·  MSI Profile Switcher", subFont,
                new Rectangle(LeftX + tw + 14, 0, 500, HeaderH),
                Color.FromArgb(220, 255, 255, 255), (TextFormatFlags)flags);
        };
        Controls.Add(header);
    }

    private void BuildFooter()
    {
        var footer = new Panel { Dock = DockStyle.Bottom, Height = FooterH, BackColor = Color.White };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(0xEC, 0xED, 0xF1));
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };

        StylePrimary(_capture);
        _capture.SetBounds(RightX, 18, RightW, 46);
        _capture.Click += OnCapture;

        _cancel.Text = Lang.T("rep_cancel");
        StyleGhost(_cancel);
        _cancel.SetBounds(LeftX, 18, 150, 46);
        _cancel.Click += (_, _) => Close();

        footer.Controls.Add(_capture);
        footer.Controls.Add(_cancel);
        Controls.Add(footer);
    }

    // ---------------- content (two columns) ----------------
    private void BuildContent()
    {
        _content.Dock = DockStyle.Fill;
        _content.BackColor = Color.White;
        _content.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(0xEC, 0xED, 0xF1));
            e.Graphics.DrawLine(pen, DividerX, 22, DividerX, _content.Height - 22);
        };

        // ---- LEFT: info ----
        int y = 24;
        var intro = Paragraph(Lang.T("rep_intro"), Muted, 10.5f, ColW);
        intro.Location = new Point(LeftX, y);
        _content.Controls.Add(intro);
        y += intro.Height + 18;

        var card = new InfoCard("ⓘ", new (string, string?)[]
        {
            (Lang.T("rep_need_msi"),         null),
            (Lang.T("rep_msi_tip"),          null),
            (Lang.T("rep_msi_download"),     null),
            (Lang.T("rep_dl_version"),       "https://msi-center.en.uptodown.com/windows/download/1045738268"),
            (Lang.T("rep_dl_repo"),          "https://msi-center.en.uptodown.com/windows/versions"),
            (Lang.T("rep_msi_clean"),        null),
            (Lang.T("rep_uninstaller_link"), "https://download.msi.com/uti_exe/nb/CleanCenterMaster.zip"),
        }, ColW)
        { Location = new Point(LeftX, y) };
        _content.Controls.Add(card);
        y += card.Height + 18;

        var pill = new Pill(Lang.T("st_firmware"), string.IsNullOrEmpty(_firmware) ? "—" : _firmware, ColW)
        { Location = new Point(LeftX, y) };
        _content.Controls.Add(pill);

        // ---- RIGHT: action ----
        var section = new Label
        {
            Text = Lang.T("rep_section"),
            AutoSize = true,
            Location = new Point(RightX, 26),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };
        _content.Controls.Add(section);

        int ry = 60;
        for (int i = 0; i < Steps.Length; i++)
        {
            _rows[i] = new StepRow(i + 1, Steps[i].msiName, Profiles.Get(Steps[i].id).DefaultColor, RightW)
            { Location = new Point(RightX, ry) };
            _content.Controls.Add(_rows[i]);
            ry += _rows[i].Height + 12;
        }

        ry += 14;
        _progressLabel.AutoSize = true;
        _progressLabel.ForeColor = Accent;
        _progressLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _progressLabel.Location = new Point(RightX, ry);
        _progressLabel.Visible = false;
        _content.Controls.Add(_progressLabel);

        _bar.SetBounds(RightX, ry + 28, RightW, 12);
        _bar.Accent = Accent;
        _bar.Visible = false;
        _content.Controls.Add(_bar);

        _instruction.AutoSize = true;
        _instruction.MaximumSize = new Size(RightW, 0);
        _instruction.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _instruction.ForeColor = Ink;
        _instruction.Location = new Point(RightX, ry + 50);
        _content.Controls.Add(_instruction);

        Controls.Add(_content);
        _content.BringToFront();
    }

    private static Label Paragraph(string text, Color color, float size, int width) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        ForeColor = color,
        Font = new Font("Segoe UI", size),
    };

    private void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentDark;
        b.FlatAppearance.MouseDownBackColor = AccentDark;
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
    }

    private void StyleGhost(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Color.FromArgb(0xD7, 0xDA, 0xE0);
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xF3, 0xF4, 0xF6);
        b.BackColor = Color.White;
        b.ForeColor = Ink;
        b.Font = new Font("Segoe UI", 10.5f);
        b.Cursor = Cursors.Hand;
    }

    // ---------------- step state ----------------
    private void RefreshSteps()
    {
        for (int i = 0; i < Steps.Length; i++)
            _rows[i].SetState(done: _dumps[i] != null, current: i == _step);

        if (_step < Steps.Length)
        {
            _instruction.ForeColor = Ink;
            _instruction.Text = string.Format(Lang.T("rep_step"), _step + 1, Steps.Length) + " — " +
                                string.Format(Lang.T("rep_set_scenario"), Steps[_step].msiName);
            _capture.Text = Lang.T("rep_capture");
        }
        else
        {
            _instruction.ForeColor = Green;
            _instruction.Text = "✓  " + Lang.T("rep_all_done");
            _capture.Text = Lang.T("rep_finish");
        }
        EnsureAnim();
    }

    private void EnsureAnim() { if (!_anim.Enabled) _anim.Start(); }

    private void OnAnim()
    {
        bool busy = _capturing;
        foreach (var r in _rows) busy |= r.Animate();
        if (!busy) _anim.Stop();
    }

    // ---------------- capture ----------------
    private void OnCapture(object? sender, EventArgs e)
    {
        if (_step >= Steps.Length) { Finish(); return; }
        if (_capturing) return;

        _capturing = true;
        _capture.Enabled = _cancel.Enabled = false;
        _lastPct = -1;
        _bar.Value = 0;
        _progressLabel.Text = Lang.T("rep_capturing") + "  0%";
        _progressLabel.Visible = _bar.Visible = true;
        EnsureAnim();

        int idx = _step;
        Task.Run(() =>
        {
            try
            {
                var dump = Ec.DumpAll(ReportProgress);
                BeginInvoke(() => CaptureDone(idx, dump, null));
            }
            catch (Exception ex)
            {
                BeginInvoke(() => CaptureDone(idx, null, ex));
            }
        });
    }

    private void ReportProgress(int byteIdx)
    {
        float v = (byteIdx + 1) / 256f;
        int pct = (int)(v * 100);
        if (pct == _lastPct) return;
        _lastPct = pct;
        BeginInvoke(() =>
        {
            _bar.Value = v;
            _progressLabel.Text = Lang.T("rep_capturing") + $"  {pct}%";
        });
    }

    private void CaptureDone(int idx, byte[]? dump, Exception? ex)
    {
        _capturing = false;
        _progressLabel.Visible = _bar.Visible = false;
        _capture.Enabled = _cancel.Enabled = true;

        if (ex != null || dump == null)
        {
            MessageBox.Show(string.Format(Lang.T("rep_read_fail"), ex?.Message ?? ""),
                            Lang.T("err"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _dumps[idx] = dump;
        _step++;
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
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fwTag = string.IsNullOrEmpty(_firmware) ? "unknown" : _firmware.Replace('.', '_');
            _savedPath = Path.Combine(dir, $"msi-model-report-{fwTag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(_savedPath, report, new UTF8Encoding(false));
            _tip.SetToolTip(_instruction, string.Format(Lang.T("rep_saved_to"), _savedPath));
        }
        catch { _savedPath = null; }
    }

    private void Finish()
    {
        try { Process.Start(new ProcessStartInfo(BuildIssueUrl()) { UseShellExecute = true }); } catch { }
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _anim.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }

    // ---------------- report building ----------------
    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MSI Profile Switcher — model support report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY, no EC writes)");
        sb.AppendLine($"App version: {_appVersion}");
        sb.AppendLine($"EC firmware: {(string.IsNullOrEmpty(_firmware) ? "(unknown)" : _firmware)}");
        sb.AppendLine($"Detected in app: {(string.IsNullOrEmpty(_detectedModel) ? "(unsupported / unknown)" : _detectedModel)}");
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
            foreach (var dump in _dumps)
                sb.Append((dump == null ? "--" : $"{dump[a]:X2}").PadRight(20));
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("--- Full EC dumps (256 bytes each) ---");
        for (int i = 0; i < Steps.Length; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"[{Steps[i].msiName}]");
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
            foreach (var dump in _dumps)
                sb.Append((dump == null ? "--" : $"{dump[a]:X2}").PadRight(9));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string BuildIssueUrl()
    {
        string title = $"[Model] {_detectedModel} ({_firmware})";
        string snapshot = BuildSnapshot();
        string fulldump = _savedPath != null
            ? $"Full report copied to clipboard and saved to:\n{_savedPath}\n\nPlease paste it here with Ctrl+V."
            : "Full report copied to clipboard — please paste it here with Ctrl+V.";

        string Base() => RepoUrl + "/issues/new" +
                         "?template=model-support.yml" +
                         "&labels=model-support" +
                         "&title=" + Uri.EscapeDataString(title) +
                         "&model=" + Uri.EscapeDataString(_detectedModel) +
                         "&firmware=" + Uri.EscapeDataString(_firmware) +
                         "&fulldump=" + Uri.EscapeDataString(fulldump);

        string url = Base() + "&snapshot=" + Uri.EscapeDataString(snapshot);
        return url.Length > 7000 ? Base() : url;
    }

    // =====================================================================
    //  Custom controls
    // =====================================================================

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// Amber rounded card: icon + a vertical sequence of blocks. Each block is either
    /// a wrapped paragraph (url == null) or a clickable link (url != null).
    /// </summary>
    private sealed class InfoCard : Control
    {
        private static readonly Color Bg = Color.FromArgb(0xFE, 0xF6, 0xE7);
        private static readonly Color Bd = Color.FromArgb(0xF3, 0xDC, 0xA9);
        private static readonly Color Fg = Color.FromArgb(0xB0, 0x6A, 0x10);
        private const int LeftPad = 46, RightPad = 18, TopPad = 15;
        private readonly string _icon;
        private readonly Font _bodyFont = new("Segoe UI", 9.5f);
        private readonly List<(Rectangle rect, string text)> _paras = new();

        public InfoCard(string icon, (string text, string? url)[] items, int width)
        {
            _icon = icon;
            DoubleBuffered = true;
            int innerW = width - LeftPad - RightPad;
            var linkFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            int y = TopPad;
            int gap = 0;

            foreach (var (text, url) in items)
            {
                if (url == null)
                {
                    int h = TextRenderer.MeasureText(text, _bodyFont, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
                    _paras.Add((new Rectangle(LeftPad, y, innerW, h), text));
                    y += h; gap = 10;
                }
                else
                {
                    int h = TextRenderer.MeasureText(text, linkFont, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
                    var link = new LinkLabel
                    {
                        Text = text,
                        AutoSize = false,
                        Size = new Size(innerW, h),
                        Location = new Point(LeftPad, y),
                        BackColor = Bg,
                        Font = linkFont,
                        LinkColor = AccentDark,
                        ActiveLinkColor = Accent,
                        LinkBehavior = LinkBehavior.HoverUnderline,
                    };
                    string target = url;
                    link.LinkClicked += (_, _) =>
                    {
                        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
                    };
                    Controls.Add(link);
                    y += h; gap = 6;
                }
                y += gap;
            }

            Size = new Size(width, y - gap + TopPad);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using var path = RoundRect(r, 12);
            using (var b = new SolidBrush(Bg)) g.FillPath(b, path);
            using (var p = new Pen(Bd)) g.DrawPath(p, path);
            TextRenderer.DrawText(g, _icon, new Font("Segoe UI", 13f, FontStyle.Bold),
                new Rectangle(14, TopPad - 1, 26, 22), Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
            foreach (var (rect, text) in _paras)
                TextRenderer.DrawText(g, text, _bodyFont, rect, Fg,
                    TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left);
        }
    }

    /// <summary>Subtle rounded pill: "label  value".</summary>
    private sealed class Pill : Control
    {
        private readonly string _label, _value;
        public Pill(string label, string value, int width)
        {
            _label = label; _value = value;
            DoubleBuffered = true;
            Size = new Size(width, 46);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using var path = RoundRect(r, 11);
            using (var b = new SolidBrush(Color.FromArgb(0xF6, 0xF5, 0xFB))) g.FillPath(b, path);
            using (var p = new Pen(Color.FromArgb(0xEA, 0xE7, 0xF5))) g.DrawPath(p, path);
            var lblFont = new Font("Segoe UI", 10f);
            TextRenderer.DrawText(g, _label, lblFont, new Rectangle(18, 0, 220, Height), Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            int lw = TextRenderer.MeasureText(_label, lblFont).Width;
            TextRenderer.DrawText(g, _value, new Font("Consolas", 11.5f, FontStyle.Bold),
                new Rectangle(18 + lw + 12, 0, Width - (18 + lw + 12) - 12, Height), Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    /// <summary>Animated step row: status circle (profile colour) + name + state.</summary>
    private sealed class StepRow : Control
    {
        private const int StatusW = 164, Circle = 28, CircleCx = 32;
        private readonly int _num;
        private readonly string _name;
        private readonly Color _color;
        private bool _done, _current;
        private float _doneAnim, _glowAnim;

        public StepRow(int num, string name, Color color, int width)
        {
            _num = num; _name = name; _color = color;
            DoubleBuffered = true;
            Size = new Size(width, 56);
        }

        public void SetState(bool done, bool current)
        {
            _done = done; _current = current;
            Invalidate();
        }

        /// <summary>Advance animations; returns true while still animating.</summary>
        public bool Animate()
        {
            bool a = Approach(ref _doneAnim, _done ? 1f : 0f, _done ? 0.10f : 0.20f);
            bool b = Approach(ref _glowAnim, _current ? 1f : 0f, _current ? 0.12f : 0.18f);
            bool moving = a || b;
            if (moving) Invalidate();
            return moving;
        }

        private static bool Approach(ref float v, float target, float step)
        {
            if (v == target) return false;
            v += (target - v) * step;
            if (Math.Abs(target - v) < 0.005f) { v = target; return false; }
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var full = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);

            if (_glowAnim > 0.01f)
            {
                using var path = RoundRect(full, 13);
                int a = (int)(_glowAnim * 255);
                using var b = new SolidBrush(Color.FromArgb((int)(a * 0.10), Accent));
                g.FillPath(b, path);
                using var pen = new Pen(Color.FromArgb((int)(a * 0.55), Accent), 1.4f);
                g.DrawPath(pen, path);
            }

            int cy = Height / 2;
            var circle = new RectangleF(CircleCx - Circle / 2f, cy - Circle / 2f, Circle, Circle);
            if (_done)
            {
                float pop = Eased(_doneAnim);
                float dd = Circle * (0.85f + 0.15f * pop);
                var c2 = new RectangleF(CircleCx - dd / 2f, cy - dd / 2f, dd, dd);
                using (var b = new SolidBrush(_color)) g.FillEllipse(b, c2);
                DrawCheck(g, CircleCx, cy, dd, pop);
            }
            else if (_current)
            {
                using var pen = new Pen(Accent, 2.6f);
                g.DrawEllipse(pen, circle);
                using var b = new SolidBrush(Color.FromArgb(50, Accent));
                float id = Circle * 0.42f;
                g.FillEllipse(b, CircleCx - id / 2f, cy - id / 2f, id, id);
            }
            else
            {
                using var pen = new Pen(Color.FromArgb(0xCF, 0xD3, 0xDA), 2f);
                g.DrawEllipse(pen, circle);
                TextRenderer.DrawText(g, _num.ToString(), new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    Rectangle.Round(circle), Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            int nameX = CircleCx + Circle / 2 + 16;
            var nameColor = _done || _current ? Ink : Muted;
            TextRenderer.DrawText(g, _name, new Font("Segoe UI", 11f, FontStyle.Bold),
                new Rectangle(nameX, 6, Width - nameX - StatusW - 6, Height - 12), nameColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            string state = Lang.T(_done ? "rep_captured" : "rep_pending");
            var stateColor = _done ? Green : Muted;
            TextRenderer.DrawText(g, state, new Font("Segoe UI", 9.5f, _done ? FontStyle.Bold : FontStyle.Regular),
                new Rectangle(Width - StatusW, 6, StatusW - 10, Height - 12), stateColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        private static void DrawCheck(Graphics g, int cx, int cy, float d, float pop)
        {
            using var pen = new Pen(Color.White, Math.Max(2f, d * 0.10f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float s = d * 0.26f * pop;
            var p1 = new PointF(cx - s, cy + s * 0.1f);
            var p2 = new PointF(cx - s * 0.2f, cy + s * 0.8f);
            var p3 = new PointF(cx + s, cy - s * 0.7f);
            g.DrawLines(pen, new[] { p1, p2, p3 });
        }

        private static float Eased(float t) => 1 - (1 - t) * (1 - t);
    }

    /// <summary>Smooth rounded progress bar.</summary>
    private sealed class RoundedBar : Control
    {
        private float _value;
        public Color Accent { get; set; } = Color.MediumPurple;

        public RoundedBar() { DoubleBuffered = true; }

        public float Value
        {
            get => _value;
            set { _value = Math.Clamp(value, 0, 1); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = Height / 2f;
            var track = new RectangleF(0, 0, Width, Height);
            using (var path = RoundRect(track, r))
            using (var b = new SolidBrush(Color.FromArgb(0xEC, 0xE9, 0xF6)))
                g.FillPath(b, path);

            float w = Math.Max(Height, Width * _value);
            var fill = new RectangleF(0, 0, w, Height);
            using (var path = RoundRect(fill, r))
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height),
                       ControlPaint.Light(Accent), Accent, 0f))
                g.FillPath(b, path);
        }
    }
}
