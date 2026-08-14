using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
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
    // A failed release fetch must not be terminal (it used to stick until app restart): _loaded
    // stays false, the tab retries on every entry, the error row gets a "Try again" button and
    // this timer re-checks on its own while the tab stays open.
    private readonly System.Windows.Forms.Timer _retry = new() { Interval = 30_000 };
    private bool _loaded;
    private bool _loading;
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

        _retry.Tick += async (_, _) => { if (Visible && !_loaded) await LoadHistory(); };

        Controls.Add(_check);
        Controls.Add(_status);
        Controls.Add(_lastChecked);
        Controls.Add(_history);
        Resize += (_, _) => LayoutBits();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _retry.Dispose();
        base.Dispose(disposing);
    }

    public override async void OnEnter()
    {
        LayoutBits();
        ApplyThemeText();
        if (!_loaded) await LoadHistory();
    }

    // Language can switch from the tray menu while this tab is visible - refresh the texts live.
    public override void LiveRefresh() { ApplyThemeText(); Invalidate(); }

    private string? _focusTag;   // release to auto-expand once the list is (or gets) loaded

    /// <summary>Deep link (Settings Start "What's new" / update chip): expand the given release's notes.</summary>
    public void FocusRelease(string? tag)
    {
        _focusTag = tag;
        TryFocusRelease();
    }

    private void TryFocusRelease()
    {
        if (_focusTag == null) return;
        foreach (Control c in _history.Controls)
            if (c is ReleaseRow rr && rr.MatchesTag(_focusTag))
            {
                rr.Expand();
                _history.ScrollControlIntoView(rr);
                _focusTag = null;
                return;
            }
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        Ui.StylePrimary(_check);
        Ui.StylePrimary(_install);
        ApplyThemeText();
    }

    // Texts live here (not in the ctor) so a language switched from the tray menu shows up on
    // the next entry / theme pass instead of sticking to the build-time language.
    private void ApplyThemeText()
    {
        _check.Text = Lang.T("upd_check_now");
        if (_avail is { } a) _install.Text = string.Format(Lang.T("upd_install"), a.Tag);
        _lastChecked.ForeColor = Theme.Muted;
        if (_status.ForeColor != Theme.Green && _status.ForeColor != Theme.Accent)
            _status.ForeColor = Theme.Text;
        _status.BackColor = _lastChecked.BackColor = Theme.Surface;
        var d = D.Settings.LastUpdateCheckUtc;
        _lastChecked.Text = string.Format(Lang.T("upd_last_checked"),
            d == DateTime.MinValue ? Lang.T("upd_never") : d.ToLocalTime().ToString("g"));
        _lastChecked.Location = new Point(ClientSize.Width - 28 - _lastChecked.PreferredWidth, _check.Bottom + 12);
        foreach (Control c in _history.Controls) if (c is ReleaseRow rr) rr.Restyle();
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
        if (_loading) return;
        _loading = true;
        try
        {
            var list = await Updater.RecentAsync(20);
            _history.Controls.Clear();
            if (list.Count == 0)
            {
                // failed (offline, rate-limited): keep _loaded false so the next tab entry
                // retries, offer a manual retry and let the timer re-check on its own
                _loaded = false;
                var err = new Label
                {
                    Text = Lang.T("upd_offline"), AutoSize = true,
                    ForeColor = Theme.Muted, BackColor = Theme.Surface,
                    Margin = new Padding(2, 8, 0, 0),
                };
                var again = new Button { Text = Lang.T("upd_retry"), AutoSize = true, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(2, 10, 0, 0) };
                Ui.StyleGhost(again);
                again.Click += async (_, _) => await LoadHistory();
                _history.Controls.Add(err);
                _history.Controls.Add(again);
                _retry.Start();
                return;
            }
            _loaded = true;
            _retry.Stop();
            int rw = RowWidth();
            foreach (var rel in list)
                _history.Controls.Add(new ReleaseRow(rel, rw));
            TryFocusRelease();   // a pending "What's new" deep link waits for the list
        }
        finally { _loading = false; }
    }

    // base on the control's full width minus the vertical scrollbar, so a horizontal
    // scrollbar never appears whether or not the vertical one is shown.
    private int RowWidth() => Math.Max(200, _history.Width - SystemInformation.VerticalScrollBarWidth - 6);

    private void SetRowWidths()
    {
        int w = RowWidth();
        foreach (Control c in _history.Controls) if (c is ReleaseRow) c.Width = w;
    }

    /// <summary>
    /// One release: header (tag, date, download count, a real "Details" button that opens
    /// GitHub) + a two-line preview. Clicking the row expands the FULL release notes inline,
    /// rendered from markdown (section headers, bullets, **bold**); clicking again collapses.
    /// </summary>
    private sealed class ReleaseRow : Control
    {
        private const TextFormatFlags F = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        private static readonly Font TitleF = new("Segoe UI", 11.5f, FontStyle.Bold);
        private static readonly Font MetaF  = new("Segoe UI", 9.5f);
        private static readonly Font BodyF  = new("Segoe UI", 9.5f);
        private static readonly Font BodyB  = new("Segoe UI", 9.5f, FontStyle.Bold);
        private static readonly Font HeadF  = new("Segoe UI", 10f, FontStyle.Bold);
        private static readonly Font ChevF  = new("Segoe UI", 9f);

        private enum NoteKind { Header, Bullet, Para, Gap }
        private readonly record struct Note(NoteKind Kind, List<(string text, bool bold)> Runs);

        private readonly Updater.ReleaseInfo _r;
        private readonly Button _details = new();
        private readonly Button _wiki = new();     // illustrated what's-new tour on the wiki
        private readonly List<Note> _notes;                       // full formatted notes
        private readonly List<(string text, bool bold)> _preview; // collapsed two-liner
        private bool _open;
        private int _notesHeight;
        private int _lastW;

        private const int TitleY = 14;
        private static int HeaderBottom => TitleY + TitleF.Height;

        public ReleaseRow(Updater.ReleaseInfo r, int width)
        {
            _r = r;
            DoubleBuffered = true; ResizeRedraw = true;
            Width = _lastW = width;
            Margin = new Padding(0, 0, 0, 12);
            Height = CollapsedHeight;
            Cursor = Cursors.Hand;
            _notes = ParseNotes(r.Body);
            _preview = ParseRuns(CleanBody(r.Body));

            _details.AutoSize = true;
            _details.Padding = new Padding(10, 2, 10, 2);
            _details.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(_r.Url) { UseShellExecute = true }); } catch { } };
            Controls.Add(_details);

            _wiki.AutoSize = true;
            _wiki.Padding = new Padding(10, 2, 10, 2);
            _wiki.Click += (_, _) =>
            {
                string ver = (_r.Tag ?? "").TrimStart('v', 'V');
                if (ver.Length == 0) return;
                try { Process.Start(new ProcessStartInfo("https://github.com/wygodad/ghostdeck/wiki/Whats-new-in-GhostDeck-" + ver) { UseShellExecute = true }); } catch { }
            };
            Controls.Add(_wiki);
            Restyle();

            Click += (_, _) => Toggle();
        }

        private static int CollapsedHeight => HeaderBottom + 8 + BodyF.Height * 2 + 14;

        public void Restyle()
        {
            Ui.StyleGhost(_details);
            _details.Text = Lang.T("upd_details") + "  ↗";
            Ui.StyleGhost(_wiki);
            _wiki.Text = Lang.T("upd_wiki") + "  ↗";
            _wiki.Visible = !string.IsNullOrEmpty(_r.Tag);   // the wiki page name derives from the tag
            PlaceButton();
            Invalidate();
        }

        private void PlaceButton()
        {
            _details.Location = new Point(Width - 16 - _details.Width, 9);
            _wiki.Location = new Point(_details.Left - 8 - _wiki.Width, 9);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width != _lastW)
            {
                _lastW = Width;
                PlaceButton();
                if (_open) RecalcHeight();
            }
        }

        private void Toggle()
        {
            if (_notes.Count == 0) return;   // nothing beyond the preview - stay collapsed
            _open = !_open;
            RecalcHeight();
            Invalidate();
        }

        public bool MatchesTag(string tag) =>
            string.Equals(_r.Tag.TrimStart('v', 'V'), tag.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);

        public void Expand() { if (!_open) Toggle(); }

        private void RecalcHeight()
        {
            if (!_open) { Height = CollapsedHeight; return; }
            using var g = CreateGraphics();
            _notesHeight = Math.Max(BodyF.Height, LayoutNotes(g, new Rectangle(18, 0, Width - 36, 0), draw: false));
            Height = HeaderBottom + 10 + _notesHeight + 14;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            Ui.FillCard(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1));

            // chevron shows the row itself is clickable (expand/collapse)
            if (_notes.Count > 0)
                TextRenderer.DrawText(g, _open ? "▾" : "▸", ChevF, new Point(16, TitleY + 3), Theme.Accent, F);
            string title = string.IsNullOrEmpty(_r.Tag) ? _r.Name : _r.Tag;
            TextRenderer.DrawText(g, title, TitleF, new Point(32, TitleY), Theme.Text, F);

            // right of the title, against the Details button: "date · Downloads: N"
            // (date stays muted, the download count gets the accent - two facts, two colors)
            string date = (_r.Published?.ToLocalTime().ToString("yyyy-MM-dd") ?? "") + "   ·   ";
            string dl = string.Format(Lang.T("upd_downloads"), _r.Downloads.ToString("N0"));
            int dateW = TextRenderer.MeasureText(g, date, MetaF, Size.Empty, F).Width;
            int dlW = TextRenderer.MeasureText(g, dl, MetaF, Size.Empty, F).Width;
            int metaX = (_wiki.Visible ? _wiki.Left : _details.Left) - 14 - dateW - dlW;
            if (metaX > 32 + TextRenderer.MeasureText(g, title, TitleF, Size.Empty, F).Width + 10)
            {
                TextRenderer.DrawText(g, date, MetaF, new Point(metaX, TitleY + 3), Theme.Muted, F);
                TextRenderer.DrawText(g, dl, MetaF, new Point(metaX + dateW, TitleY + 3), Theme.Accent, F);
            }

            if (_open)
                LayoutNotes(g, new Rectangle(18, HeaderBottom + 10, Width - 36, 0), draw: true);
            else
                DrawRich(g, _preview, new Rectangle(18, HeaderBottom + 8, Width - 36, BodyF.Height * 2 + 2),
                    BodyF, BodyB, Theme.Muted, 2);
        }

        // ---------------- full notes: parse + measure/draw ----------------

        private static readonly Regex MdLink = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);

        private static List<Note> ParseNotes(string body)
        {
            var notes = new List<Note>();
            if (string.IsNullOrWhiteSpace(body)) return notes;
            foreach (var raw in body.Replace("\r", "").Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0)
                {
                    if (notes.Count > 0 && notes[^1].Kind != NoteKind.Gap) notes.Add(new(NoteKind.Gap, new()));
                    continue;
                }
                if (t.StartsWith("Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase)) continue;
                if (t.StartsWith("#"))
                {
                    var h = MdLink.Replace(t.TrimStart('#', ' ').Trim(), "$1").Replace("**", "");
                    if (h.Length > 0) notes.Add(new(NoteKind.Header, ParseRuns(h)));
                    continue;
                }
                bool bullet = t.StartsWith("- ") || t.StartsWith("* ");
                var l = MdLink.Replace(bullet ? t[2..].Trim() : t, "$1");
                if (l.Length == 0) continue;
                if (!bullet && IsSection(l)) { notes.Add(new(NoteKind.Header, ParseRuns(l.TrimEnd(':')))); continue; }
                notes.Add(new(bullet ? NoteKind.Bullet : NoteKind.Para, ParseRuns(l)));
            }
            while (notes.Count > 0 && notes[^1].Kind == NoteKind.Gap) notes.RemoveAt(notes.Count - 1);
            while (notes.Count > 0 && notes[0].Kind == NoteKind.Gap) notes.RemoveAt(0);
            return notes;
        }

        /// <summary>Measure (draw=false) or draw the full notes; returns the used height.</summary>
        private int LayoutNotes(Graphics g, Rectangle rect, bool draw)
        {
            int y = rect.Top;
            foreach (var n in _notes)
            {
                switch (n.Kind)
                {
                    case NoteKind.Gap:
                        y += 6;
                        break;
                    case NoteKind.Header:
                        y = DrawWrapped(g, n.Runs, new Rectangle(rect.Left, y, rect.Width, 0), HeadF, HeadF, Theme.Accent, draw) + 4;
                        break;
                    case NoteKind.Bullet:
                        if (draw) TextRenderer.DrawText(g, "•", BodyF, new Point(rect.Left + 2, y), Theme.Muted, F);
                        y = DrawWrapped(g, n.Runs, new Rectangle(rect.Left + 16, y, rect.Width - 16, 0), BodyF, BodyB, Theme.Muted, draw) + 3;
                        break;
                    default:
                        y = DrawWrapped(g, n.Runs, new Rectangle(rect.Left, y, rect.Width, 0), BodyF, BodyB, Theme.Muted, draw) + 3;
                        break;
                }
            }
            return y - rect.Top;
        }

        // word-wrap runs with no line limit; returns the bottom y (draw=false only measures)
        private static int DrawWrapped(Graphics g, List<(string text, bool bold)> runs, Rectangle rect,
                                       Font reg, Font bold, Color color, bool draw)
        {
            int spaceW = TextRenderer.MeasureText(g, " ", reg, Size.Empty, F).Width;
            int lineH = reg.Height;
            int x = rect.Left, y = rect.Top;
            foreach (var (text, b) in runs)
            {
                var f = b ? bold : reg;
                foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int ww = TextRenderer.MeasureText(g, w, f, Size.Empty, F).Width;
                    if (x > rect.Left && x + ww > rect.Right) { x = rect.Left; y += lineH; }
                    if (draw) TextRenderer.DrawText(g, w, f, new Point(x, y), color, F);
                    x += ww + spaceW;
                }
            }
            return y + lineH;
        }

        // ---------------- collapsed preview (two joined lines) ----------------

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

        // word-wrap runs across up to maxLines, switching font for bold words (preview only)
        private static void DrawRich(Graphics g, List<(string text, bool bold)> runs, Rectangle rect,
                                     Font reg, Font bold, Color color, int maxLines)
        {
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
