using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

// =====================================================================
//  Updates
// =====================================================================
public sealed class UpdatesPage : ThemedPage
{
    private readonly Button _check = new();
    private readonly Button _install = new();
    private readonly Label _status = new();
    private readonly Label _lastChecked = new();
    private readonly ThinBar _bar = new();     // download progress, next to the status text
    private readonly FlowLayoutPanel _history = new();
    private bool _loaded;
    private Updater.Result? _avail;   // newer release found by the last check

    /// <summary>Rounded progress bar (0..1), styled like the report wizard's capture bar.</summary>
    private sealed class ThinBar : Control
    {
        private float _value;
        public float Value { get => _value; set { _value = Math.Clamp(value, 0, 1); Invalidate(); } }
        public ThinBar() { DoubleBuffered = true; Size = new Size(320, 12); Visible = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(track, Height / 2f))
            {
                using var b = new SolidBrush(Theme.Card); g.FillPath(b, p);
                using var pen = new Pen(Theme.Border); g.DrawPath(pen, p);
            }
            if (_value > 0)
            {
                float w = Math.Max(Height, (Width - 1) * _value);
                var fr = new RectangleF(0, 0, w, Height - 1);
                using var p = Theme.RoundRect(fr, Height / 2f);
                using var b = new LinearGradientBrush(new RectangleF(0, 0, Width, Height),
                    ControlPaint.Light(Theme.Accent, 0.2f), Theme.Accent, 0f);
                g.FillPath(b, p);
            }
        }
    }

    public UpdatesPage(MainDeps d) : base(d)
    {
        // No outer scrolling: the release list has its own scroll panel and everything else is
        // fixed-height. With base AutoScroll on, a vertical bar appearing shrank ClientSize.Width
        // AFTER LayoutBits ran, the right-anchored children stuck out ~17 px and a horizontal
        // scrollbar popped up even though everything visually fit.
        AutoScroll = false;

        _check.Text = Lang.T("upd_check_now");
        Ui.StylePrimary(_check);
        _check.Width = 150;
        _check.Click += async (_, _) => await CheckNow();

        Ui.StylePrimary(_install);
        _install.Width = 220;
        _install.Visible = false;
        _install.Click += async (_, _) => await InstallNow();
        Controls.Add(_install);
        Controls.Add(_bar);

        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI", 10.5f);
        _lastChecked.Font = new Font("Segoe UI", 9.5f);
        _lastChecked.AutoSize = true;

        _history.FlowDirection = FlowDirection.TopDown;
        _history.WrapContents = false;
        _history.AutoScroll = true;
        _history.ClientSizeChanged += (_, _) => SetRowWidths();

        Controls.Add(_check);
        Controls.Add(_status);
        Controls.Add(_lastChecked);
        Controls.Add(_history);
        Resize += (_, _) => LayoutBits();
    }

    public override async void OnEnter()
    {
        LayoutBits();
        ApplyThemeText();
        if (!_loaded) { _loaded = true; await LoadHistory(); }
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        Ui.StylePrimary(_check);
        Ui.StylePrimary(_install);
        ApplyThemeText();
        foreach (Control c in _history.Controls) c.Invalidate();
    }

    private void ApplyThemeText()
    {
        _lastChecked.ForeColor = Theme.Muted;
        if (_status.ForeColor != Theme.Green && _status.ForeColor != Theme.Accent)
            _status.ForeColor = Theme.Text;
        _status.BackColor = _lastChecked.BackColor = Theme.Surface;
        var d = D.Settings.LastUpdateCheckUtc;
        _lastChecked.Text = string.Format(Lang.T("upd_last_checked"),
            d == DateTime.MinValue ? Lang.T("upd_never") : d.ToLocalTime().ToString("g"));
        _lastChecked.Location = new Point(ClientSize.Width - 28 - _lastChecked.PreferredWidth, _check.Bottom + 12);
    }

    // y positions derived from real font metrics (DPI-safe)
    private int InstalledY => 24 + new Font("Segoe UI", 18f, FontStyle.Bold).Height + 16;
    private int VersionY => InstalledY + new Font("Segoe UI", 10f).Height + 4;
    private int HistoryLabelY => VersionY + new Font("Segoe UI", 16f, FontStyle.Bold).Height + 26;
    private int HistoryTop => HistoryLabelY + new Font("Segoe UI", 10f, FontStyle.Bold).Height + 12;

    private void LayoutBits()
    {
        int w = ClientSize.Width - 56;
        _check.Location = new Point(ClientSize.Width - 28 - _check.Width, 66);
        _install.Location = new Point(_check.Left - _install.Width - 10, 66);
        _lastChecked.Location = new Point(ClientSize.Width - 28 - _lastChecked.PreferredWidth, _check.Bottom + 12);
        if (_bar.Visible)
        {
            // download mode: buttons hidden, "Downloading… X%" label stacked ABOVE the bar,
            // right-aligned in the button area (like the report wizard's capture bar)
            int bx = ClientSize.Width - 28 - _bar.Width;
            _status.Location = new Point(bx, 66);
            _bar.Location = new Point(bx, _status.Bottom + 8);
        }
        else
        {
            // idle: status ("new version…" / "up to date") sits to the LEFT of the buttons
            int rowLeft = _install.Visible ? _install.Left : _check.Left;
            _status.Location = new Point(rowLeft - _status.PreferredWidth - 14, 66 + (_check.Height - _status.PreferredHeight) / 2);
        }
        _history.SetBounds(28, HistoryTop, w, Math.Max(120, ClientSize.Height - HistoryTop - 24));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, Lang.T("tab_updates"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(28, 24), Theme.Text);
        TextRenderer.DrawText(g, Lang.T("upd_installed"), new Font("Segoe UI", 10f), new Point(28, InstalledY), Theme.Muted);
        TextRenderer.DrawText(g, "v" + D.AppVersion(), new Font("Segoe UI", 16f, FontStyle.Bold), new Point(28, VersionY), Theme.Text);
        TextRenderer.DrawText(g, Lang.T("upd_history"), new Font("Segoe UI", 10f, FontStyle.Bold), new Point(28, HistoryLabelY), Theme.Muted);
    }

    private async Task CheckNow()
    {
        _check.Enabled = false;
        _status.ForeColor = Theme.Accent;
        _status.Text = Lang.T("upd_checking");
        LayoutBits();
        Version cur = Version.TryParse(D.AppVersion(), out var v) ? v : new Version(0, 0, 0);
        var res = await Updater.CheckAsync(cur);
        D.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
        D.SaveSettings();
        ApplyThemeText();
        if (res is { } r)
        {
            // in-app install instead of bouncing the user to the browser (discussion #9)
            _avail = r;
            _status.ForeColor = Theme.Accent;
            _status.Text = string.Format(Lang.T("upd_available"), r.Version);
            _install.Text = string.Format(Lang.T("upd_install"), r.Tag);
            _install.Visible = true;
            LayoutBits();
        }
        else
        {
            _avail = null;
            _install.Visible = false;
            _status.ForeColor = Theme.Green;
            _status.Text = "✓  " + Lang.T("upd_latest_ok");
            LayoutBits();
        }
        _check.Enabled = true;
        D.CheckNoticesNow();     // manual check also refreshes announcements (banner + tray balloon)
        await LoadHistory();
    }

    private async Task InstallNow()
    {
        if (_avail is not { } r) return;
        // download mode: hide the buttons, show the stacked label + progress bar
        _install.Visible = _check.Visible = false;
        _status.ForeColor = Theme.Accent;
        _bar.Visible = true;
        _bar.Value = 0;
        _status.Text = string.Format(Lang.T("upd_downloading"), 0);
        LayoutBits();
        var progress = new Progress<int>(p =>
        {
            _bar.Value = p / 100f;
            _status.Text = string.Format(Lang.T("upd_downloading"), p);
            LayoutBits();
        });
        string? path = await Updater.DownloadAsync(r, progress);
        if (path == null || !Updater.StartSelfUpdate(path))
        {
            // no exe asset / download or launch failed - fall back to the release page
            _bar.Visible = false;
            _install.Visible = _check.Visible = true;
            _status.ForeColor = Theme.Red;
            _status.Text = Lang.T("upd_dl_failed");
            LayoutBits();
            try { Process.Start(new ProcessStartInfo(r.Url) { UseShellExecute = true }); } catch { }
            return;
        }
        _status.Text = Lang.T("upd_restarting");
        LayoutBits();
        Application.Exit();   // the hidden updater script waits for this process, swaps the exe and relaunches
    }

    private async Task LoadHistory()
    {
        var list = await Updater.RecentAsync(5);
        _history.Controls.Clear();
        if (list.Count == 0)
        {
            _history.Controls.Add(new Label { Text = Lang.T("upd_offline"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(2, 8, 0, 0) });
            return;
        }
        int rw = RowWidth();
        foreach (var rel in list)
            _history.Controls.Add(new ReleaseRow(rel, rw));
    }

    // base on the control's full width minus the vertical scrollbar, so a horizontal
    // scrollbar never appears whether or not the vertical one is shown.
    private int RowWidth() => Math.Max(200, _history.Width - SystemInformation.VerticalScrollBarWidth - 6);

    private void SetRowWidths()
    {
        int w = RowWidth();
        foreach (Control c in _history.Controls) if (c is ReleaseRow) c.Width = w;
    }

    private sealed class ReleaseRow : Control
    {
        private readonly Updater.ReleaseInfo _r;
        public ReleaseRow(Updater.ReleaseInfo r, int width)
        {
            _r = r; DoubleBuffered = true; ResizeRedraw = true; Width = width; Margin = new Padding(0, 0, 0, 12);
            var titleF = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            var bodyF = new Font("Segoe UI", 9.5f);
            Height = 16 + titleF.Height + 8 + bodyF.Height * 2 + 16;   // title + two body lines
            Cursor = Cursors.Hand;
            Click += (_, _) => { try { Process.Start(new ProcessStartInfo(_r.Url) { UseShellExecute = true }); } catch { } };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            Ui.FillCard(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1));
            var titleF = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            var dateF = new Font("Segoe UI", 9.5f);
            var bodyF = new Font("Segoe UI", 9.5f);
            var linkF = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            string title = string.IsNullOrEmpty(_r.Tag) ? _r.Name : _r.Tag;
            int titleY = 16;
            TextRenderer.DrawText(g, title, titleF, new Rectangle(18, titleY, Width / 2, titleF.Height), Theme.Text, TextFormatFlags.Left);

            // top-right: "details ↗" link, with the date to its left (each measured -> no overlap)
            string link = Lang.T("upd_details") + " ↗";
            int linkW = TextRenderer.MeasureText(link, linkF).Width;
            TextRenderer.DrawText(g, link, linkF, new Rectangle(Width - 18 - linkW, titleY, linkW + 2, titleF.Height), Theme.Accent, TextFormatFlags.Left);
            string date = _r.Published?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
            int dateW = TextRenderer.MeasureText(date, dateF).Width;
            TextRenderer.DrawText(g, date, dateF, new Rectangle(Width - 18 - linkW - 16 - dateW, titleY + 2, dateW + 4, dateF.Height), Theme.Muted, TextFormatFlags.Left);

            // body: up to two changelog lines, with **bold** rendered and links removed
            int by = titleY + titleF.Height + 8;
            var bodyB = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            DrawRich(g, ParseRuns(CleanBody(_r.Body)), new Rectangle(18, by, Width - 36, bodyF.Height * 2 + 2),
                bodyF, bodyB, Theme.Muted, 2);
        }

        private static readonly Regex MdLink = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);

        // join up to 2 content lines; drop headers, section words, "Full Changelog", and md links
        private static string CleanBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            var lines = new List<string>();
            foreach (var raw in body.Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                if (t.StartsWith("Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase)) continue;
                bool bullet = t.StartsWith("-") || t.StartsWith("*");
                var l = t.TrimStart('-', '*', ' ').Trim();
                l = MdLink.Replace(l, "$1");                 // [text](url) -> text
                if (l.Length == 0) continue;
                if (!bullet && IsSection(l)) continue;
                lines.Add(l);
                if (lines.Count >= 2) break;
            }
            return string.Join("   ·   ", lines);
        }

        private static bool IsSection(string s) => s.TrimEnd(':') is
            "Added" or "Fixed" or "Changed" or "Removed" or "Deprecated" or "Security";

        // split on ** markers into (text, bold) runs
        private static List<(string text, bool bold)> ParseRuns(string s)
        {
            var runs = new List<(string, bool)>();
            var sb = new StringBuilder(); bool bold = false;
            for (int i = 0; i < s.Length;)
            {
                if (i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*')
                { if (sb.Length > 0) { runs.Add((sb.ToString(), bold)); sb.Clear(); } bold = !bold; i += 2; }
                else { sb.Append(s[i]); i++; }
            }
            if (sb.Length > 0) runs.Add((sb.ToString(), bold));
            return runs;
        }

        // word-wrap runs across up to maxLines, switching font for bold words
        private static void DrawRich(Graphics g, List<(string text, bool bold)> runs, Rectangle rect,
                                     Font reg, Font bold, Color color, int maxLines)
        {
            const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int spaceW = TextRenderer.MeasureText(g, " ", reg, Size.Empty, F).Width;
            int lineH = reg.Height;
            int x = rect.Left, y = rect.Top, line = 1;
            foreach (var (text, b) in runs)
            {
                var f = b ? bold : reg;
                foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int ww = TextRenderer.MeasureText(g, w, f, Size.Empty, F).Width;
                    if (x > rect.Left && x + ww > rect.Right)
                    {
                        if (line >= maxLines) { TextRenderer.DrawText(g, "…", reg, new Point(x, y), color, F); return; }
                        line++; x = rect.Left; y += lineH;
                    }
                    TextRenderer.DrawText(g, w, f, new Point(x, y), color, F);
                    x += ww + spaceW;
                }
            }
        }
    }
}
