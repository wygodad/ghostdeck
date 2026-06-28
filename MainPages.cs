using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;

namespace MSIProfileSwitcher;

/// <summary>Shared themed widgets / drawing helpers for the tab pages.</summary>
internal static class Ui
{
    public static void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Theme.Accent;
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.Height = 40;
    }

    public static void StyleGhost(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Theme.BorderStrong;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = Theme.Surface;
        b.ForeColor = Theme.Text;
        b.Font = new Font("Segoe UI", 10.5f);
        b.Cursor = Cursors.Hand;
        b.Height = 40;
    }

    public static void FillCard(Graphics g, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundRect(r, 12);
        using var b = new SolidBrush(Theme.Card);
        g.FillPath(b, path);
        using var pen = new Pen(Theme.Border);
        g.DrawPath(pen, path);
    }

    public static void Pill(Graphics g, string text, Point at, Color fg)
    {
        var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        var sz = TextRenderer.MeasureText(text, font);
        int w = sz.Width + 32, h = sz.Height + 14;       // bigger padding
        var r = new RectangleF(at.X, at.Y, w, h);
        using (var path = Theme.RoundRect(r, 5))         // smaller corner radius
        using (var b = new SolidBrush(Color.FromArgb(40, fg)))
            g.FillPath(b, path);
        TextRenderer.DrawText(g, text, font, Rectangle.Round(r), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// =====================================================================
//  Scenarios
// =====================================================================
public sealed class ScenariosPage : ThemedPage
{
    private const int TileH = 280, Gap = 16, Pad = 28;
    private readonly Tile[] _tiles;
    private readonly SegControl _charge;
    private readonly ToggleSwitch _auto;
    private int _cardTop, _cardH, _headH, _subY;

    public ScenariosPage(MainDeps d) : base(d)
    {
        _tiles = Profiles.Order.Select(id => new Tile(d, id)).ToArray();
        foreach (var t in _tiles) Controls.Add(t);

        _charge = new SegControl(new[] { Lang.T("gen_off_short"), "60%", "80%", "100%" }, ChargeIndex());
        _charge.SelectedChanged += i => D.SetChargeLimit(i switch { 1 => 60, 2 => 80, 3 => 100, _ => 0 });
        Controls.Add(_charge);

        _auto = new ToggleSwitch { Checked = D.Settings.AutoSwitchEnabled };
        _auto.Toggled += v => D.SetAutoSwitch(v);
        Controls.Add(_auto);

        Resize += (_, _) => Relayout();
    }

    private int ChargeIndex() => D.Settings.ChargeLimit switch { 60 => 1, 80 => 2, 100 => 3, _ => 0 };

    public override void OnEnter()
    {
        _charge.Selected = ChargeIndex();
        _auto.Checked = D.Settings.AutoSwitchEnabled;
        Relayout();
        Invalidate();
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        foreach (var t in _tiles) t.Invalidate();
        _charge.Invalidate(); _auto.Invalidate();
    }

    private void Relayout()
    {
        // header height from real font metrics (DPI-safe)
        int titleH = new Font("Segoe UI", 18f, FontStyle.Bold).Height;
        int subH = new Font("Segoe UI", 10.5f).Height;
        _subY = 24 + titleH + 20;                       // title -> subtitle gap
        _headH = _subY + subH + 28;                     // subtitle -> tiles gap

        int avail = ClientSize.Width - Pad * 2;
        int tw = (avail - Gap * 3) / 4;                 // 4 in a row
        for (int i = 0; i < _tiles.Length; i++)
            _tiles[i].SetBounds(Pad + i * (tw + Gap), _headH, tw, TileH);

        _cardTop = _headH + TileH + 28;
        int rowGap = 30, rowH = 40, cardPad = 22;
        _cardH = cardPad * 2 + rowH * 2 + rowGap;
        int segW = Math.Min(360, avail - 220);
        _charge.SetBounds(Pad + avail - segW - cardPad, _cardTop + cardPad, segW, rowH);
        _auto.SetBounds(Pad + avail - _auto.Width - cardPad, _cardTop + cardPad + rowH + rowGap + (rowH - _auto.Height) / 2, _auto.Width, _auto.Height);
        AutoScrollMinSize = new Size(820, _cardTop + _cardH + 28);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ApplyScroll(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var info = D.Status();
        TextRenderer.DrawText(g, Lang.T("scen_title"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);
        string sub = info.Device + (string.IsNullOrEmpty(D.Firmware) ? "" : "  ·  " + D.Firmware);
        TextRenderer.DrawText(g, sub, new Font("Segoe UI", 10.5f), new Point(Pad, _subY), Theme.Muted);
        var bf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        int bw = TextRenderer.MeasureText(info.TierText, bf).Width + 32;
        Ui.Pill(g, info.TierText, new Point(ClientSize.Width - Pad - bw, 26), info.TierColor);

        int avail = ClientSize.Width - Pad * 2;
        var card = new RectangleF(Pad, _cardTop, avail, _cardH);
        Ui.FillCard(g, card);
        const int cardPad = 22, rowH = 40, rowGap = 30;
        int r1 = _cardTop + cardPad, r2 = r1 + rowH + rowGap;
        var lf = new Font("Segoe UI", 11f);
        TextRenderer.DrawText(g, Lang.T("st_charge"), lf,
            new Rectangle(Pad + cardPad, r1, 360, rowH), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        TextRenderer.DrawText(g, Lang.T("scen_autoswitch"), lf,
            new Rectangle(Pad + cardPad, r2, 460, rowH), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, Pad + cardPad, r1 + rowH + rowGap / 2, Pad + avail - cardPad, r1 + rowH + rowGap / 2);
    }

    private sealed class Tile : Control
    {
        private readonly MainDeps _d;
        private readonly ProfileId _id;
        private bool _hover;
        public Tile(MainDeps d, ProfileId id)
        {
            _d = d; _id = id; DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand;
            Click += (_, _) => { if (_d.Writable()) { _d.SetProfile(_id); Parent?.Invalidate(true); } };
        }
        public void Refresh2() => Invalidate();
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            bool active = _d.Writable() && _d.Current() == _id;
            var def = Profiles.Get(_id);
            var col = Theme.Profile(_d.ColorOf(_id));
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(outer, 6))     // mało zaokrąglone
            {
                using var b = new SolidBrush(active ? Theme.AccentSoft : Theme.Card);
                g.FillPath(b, path);
                using var pen = new Pen(active ? Theme.Accent : (_hover ? Theme.BorderStrong : Theme.Border), active ? 2f : 1f);
                g.DrawPath(pen, path);
            }
            // icon centred on top, text stacked below (font-height stacked = DPI-safe)
            int iconBox = 76;
            var nameFont = new Font("Segoe UI", 15f, FontStyle.Bold);
            var subFont = new Font("Segoe UI", 10.5f);
            int nameH = nameFont.Height, subH = subFont.Height, g1 = 16, g2 = 6;
            int blockH = iconBox + g1 + nameH + g2 + subH;
            int top = Math.Max(20, (Height - blockH) / 2);
            IconPainter.Scenario(g, _id, new RectangleF((Width - iconBox) / 2f, top, iconBox, iconBox), col, 4f);
            int textW = Width - 24;
            TextRenderer.DrawText(g, def.Label, nameFont,
                new Rectangle(12, top + iconBox + g1, textW, nameH), Theme.Text,
                TextFormatFlags.Top | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Lang.T(def.SubKey), subFont,
                new Rectangle(12, top + iconBox + g1 + nameH + g2, textW, subH), Theme.Muted,
                TextFormatFlags.Top | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            if (active)
            {
                int cx = Width - 26, cy = 24;
                using var bb = new SolidBrush(Theme.Accent);
                g.FillEllipse(bb, cx - 11, cy - 11, 22, 22);
                using var pen = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(pen, new[] { new Point(cx - 5, cy), new Point(cx - 1, cy + 4), new Point(cx + 6, cy - 5) });
            }
        }
    }
}

// =====================================================================
//  Status
// =====================================================================
public sealed class StatusPage : ThemedPage
{
    private const int Pad = 28;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private static readonly (string key, Func<StatusInfo, HwSnapshot, string> val, bool mono)[] Rows =
    {
        ("st_model",     (s, h) => s.Device, false),
        ("st_firmware",  (s, h) => string.IsNullOrEmpty(h.Firmware) ? "—" : h.Firmware, true),
        ("st_charge",    (s, h) => h.ChargeLimit is >= 10 and <= 100 ? $"{h.ChargeLimit} %" : "—", false),
        ("st_switches",  (s, h) => s.Switches.ToString(), false),
        ("st_in_profile",(s, h) => FmtTs(s.InProfile), false),
        ("st_autostart", (s, h) => s.Autostart ? Lang.T("yes") : Lang.T("no"), false),
        ("st_app_ver",   (s, h) => s.AppVersion, false),
    };

    public StatusPage(MainDeps d) : base(d)
    {
        _timer.Tick += (_, _) => Invalidate();
        VisibleChanged += (_, _) => { if (Visible) _timer.Start(); else _timer.Stop(); };
        Resize += (_, _) => { SetScroll(); Invalidate(); };
    }

    private const int RingTop = 84, RingToTable = 112, RowH = 50;
    private int RingSize()
    {
        int avail = ClientSize.Width - Pad * 2, gap = 28;
        return Math.Max(170, Math.Min(286, (avail - gap * 3) / 4));
    }
    private void SetScroll() => AutoScrollMinSize = new Size(760, RingTop + RingSize() + RingToTable + RowH * Rows.Length + 40);
    public override void OnEnter() { SetScroll(); Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ApplyScroll(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var info = D.Status();
        HwSnapshot hw; try { hw = D.Hw(); } catch { hw = default; }

        TextRenderer.DrawText(g, Lang.T("menu_status"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);
        var bf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        int bw = TextRenderer.MeasureText(info.TierText, bf).Width + 32;
        Ui.Pill(g, info.TierText, new Point(ClientSize.Width - Pad - bw, 28), info.TierColor);

        int avail = ClientSize.Width - Pad * 2;
        int ringGap = 28;
        int ring = RingSize();
        int top = RingTop;
        DrawRing(g, Pad + 0 * (ring + ringGap), top, ring, hw.CpuTemp, 100, "°C", Lang.T("st_cpu_temp"), TempColor(hw.CpuTemp), info.Known);
        DrawRing(g, Pad + 1 * (ring + ringGap), top, ring, hw.GpuTemp, 100, "°C", Lang.T("st_gpu_temp"), TempColor(hw.GpuTemp), info.Known);
        DrawRing(g, Pad + 2 * (ring + ringGap), top, ring, info.Known ? hw.CpuFan : 0, 100, "%", Lang.T("st_cpu_fan"), Theme.Accent, info.Known);
        DrawRing(g, Pad + 3 * (ring + ringGap), top, ring, info.Known ? hw.GpuFan : 0, 100, "%", Lang.T("st_gpu_fan"), Theme.Accent, info.Known);

        int cardTop = top + ring + RingToTable;
        int rowH = RowH;
        var card = new RectangleF(Pad, cardTop, avail, rowH * Rows.Length + 14);
        Ui.FillCard(g, card);
        int y = cardTop + 7;
        for (int i = 0; i < Rows.Length; i++)
        {
            var (key, val, mono) = Rows[i];
            TextRenderer.DrawText(g, Lang.T(key), new Font("Segoe UI", 10.5f),
                new Rectangle(Pad + 22, y, avail - 44, rowH), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            var font = mono ? new Font("Consolas", 11f, FontStyle.Bold) : new Font("Segoe UI", 11f, FontStyle.Bold);
            TextRenderer.DrawText(g, val(info, hw), font,
                new Rectangle(Pad, y, avail - 22, rowH), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            if (i < Rows.Length - 1)
            {
                using var pen = new Pen(Theme.Border);
                g.DrawLine(pen, Pad + 22, y + rowH, Pad + avail - 22, y + rowH);
            }
            y += rowH;
        }
    }

    private static void DrawRing(Graphics g, int x, int y, int size, int value, int max, string unit, string label, Color color, bool known)
    {
        bool ok = known && value > 0 && value < 130;
        float frac = ok ? Math.Clamp(value / (float)max, 0, 1) : 0;
        IconPainter.Ring(g, new RectangleF(x, y, size, size), frac, color, ok ? value.ToString() : "—", unit, label);
    }

    private static Color TempColor(int t) =>
        t <= 0 ? Theme.Muted : t < 70 ? Theme.Green : t < 85 ? Theme.Amber : Theme.Red;

    private static string FmtTs(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{t.Minutes} min";
        return $"{t.Seconds} s";
    }
}

// =====================================================================
//  Updates
// =====================================================================
public sealed class UpdatesPage : ThemedPage
{
    private readonly Button _check = new();
    private readonly Label _status = new();
    private readonly Label _lastChecked = new();
    private readonly FlowLayoutPanel _history = new();
    private bool _loaded;

    public UpdatesPage(MainDeps d) : base(d)
    {
        _check.Text = Lang.T("upd_check_now");
        Ui.StylePrimary(_check);
        _check.Width = 150;
        _check.Click += async (_, _) => await CheckNow();

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
        _status.Location = new Point(28, VersionY + new Font("Segoe UI", 16f, FontStyle.Bold).Height + 6);
        _lastChecked.Location = new Point(ClientSize.Width - 28 - _lastChecked.PreferredWidth, _check.Bottom + 12);
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
        Version cur = Version.TryParse(D.AppVersion(), out var v) ? v : new Version(0, 0, 0);
        var res = await Updater.CheckAsync(cur);
        D.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
        D.SaveSettings();
        ApplyThemeText();
        if (res is { } r)
        {
            _status.ForeColor = Theme.Accent;
            _status.Text = string.Format(Lang.T("upd_available"), r.Version);
            try { Process.Start(new ProcessStartInfo(r.Url) { UseShellExecute = true }); } catch { }
        }
        else
        {
            _status.ForeColor = Theme.Green;
            _status.Text = "✓  " + Lang.T("upd_latest_ok");
        }
        _check.Enabled = true;
        await LoadHistory();
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

// =====================================================================
//  Settings (grouped cards; real controls = DPI-safe text)
// =====================================================================
public sealed class SettingsPage : ThemedPage
{
    private static readonly (string key, string label)[] Acts =
    {
        ("Silent", "Silent"), ("Balanced", "Balanced"),
        ("Extreme", "Extreme"), ("SuperBattery", "Super Battery"), ("Cycle", "Cycle"),
    };
    private static readonly int[] ChargeVals = { 0, 60, 80, 100 };
    private const int Pad = 28, Gutter = 24, TitleTop = 22;

    private readonly List<CardSection> _left = new();
    private readonly List<CardSection> _right = new();
    private readonly Dictionary<string, HotkeyBox> _boxes = new();
    private readonly Dictionary<string, List<Panel>> _swatches = new();

    public SettingsPage(MainDeps d) : base(d) { BuildForm(); Resize += (_, _) => Layout2(); }

    public override void OnEnter() { Layout2(); Invalidate(); }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        foreach (var c in _left.Concat(_right)) c.ApplyTheme();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; ApplyScroll(g);
        TextRenderer.DrawText(g, Lang.T("menu_settings"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, TitleTop), Theme.Text);
    }

    private void Layout2()
    {
        if (_left.Count == 0) return;
        int colW = Math.Max(320, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        int top = TitleTop + new Font("Segoe UI", 18f, FontStyle.Bold).Height + 18;
        int yL = top, yR = top;
        foreach (var c in _left) { c.Relayout(colW); c.Location = new Point(Pad, yL); yL += c.Height + 16; }
        foreach (var c in _right) { c.Relayout(colW); c.Location = new Point(Pad + colW + Gutter, yR); yR += c.Height + 16; }
        AutoScrollMinSize = new Size(Pad * 2 + colW * 2 + Gutter, Math.Max(yL, yR) + 20);
    }

    // ---------------- build ----------------
    private void BuildForm()
    {
        foreach (var c in _left.Concat(_right)) Controls.Remove(c);
        _left.Clear(); _right.Clear(); _boxes.Clear(); _swatches.Clear();

        // ---- left column ----
        var look = new CardSection(Lang.T("set_grp_look"));
        var theme = new SegControl(new[] { Lang.T("set_theme_light"), Lang.T("set_theme_dark") }, Theme.Dark ? 1 : 0) { Size = new Size(220, 34) };
        theme.SelectedChanged += i => { Theme.Set(i == 1); D.Settings.DarkMode = Theme.Dark; D.SaveSettings(); };
        look.AddRow(Lang.T("set_theme"), theme);
        var lang = Combo(Lang.Names, Math.Max(0, Array.IndexOf(Lang.Codes, D.Settings.Language)));
        lang.SelectedIndexChanged += (_, _) =>
        {
            D.Settings.Language = Lang.Codes[Math.Max(0, lang.SelectedIndex)];
            Lang.Set(D.Settings.Language); D.SaveSettings(); D.SettingsChanged();
            BuildForm(); Layout2();
        };
        look.AddRow(Lang.T("set_language"), lang);
        foreach (var id in Profiles.Order) look.AddRow(Profiles.Get(id).Label, BuildSwatches(id));
        _left.Add(look);

        var start = new CardSection(Lang.T("set_grp_start"));
        start.AddRow(Lang.T("set_autostart"), Toggle(D.Settings.Autostart, v => { D.Settings.Autostart = v; try { Autostart.Set(v); } catch { } D.SaveSettings(); }));
        start.AddRow(Lang.T("experimental_enable"), Toggle(D.Settings.ExperimentalEnabled, v => { D.Settings.ExperimentalEnabled = v; D.SaveSettings(); D.SettingsChanged(); }));
        _left.Add(start);

        // ---- right column ----
        var power = new CardSection(Lang.T("set_grp_power"));
        var charge = new SegControl(new[] { Lang.T("gen_off_short"), "60%", "80%", "100%" }, Math.Max(0, Array.IndexOf(ChargeVals, D.Settings.ChargeLimit))) { Size = new Size(280, 34) };
        charge.SelectedChanged += i => D.SetChargeLimit(ChargeVals[i]);
        power.AddRow(Lang.T("set_charge"), charge);
        power.AddRow(Lang.T("set_autoswitch"), Toggle(D.Settings.AutoSwitchEnabled, v => D.SetAutoSwitch(v)));
        var ac = Combo(Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray(), ProfileIndex(D.Settings.ProfileOnAC));
        ac.SelectedIndexChanged += (_, _) => { D.Settings.ProfileOnAC = Profiles.Get(Profiles.Order[ac.SelectedIndex]).Key; D.SaveSettings(); };
        power.AddRow(Lang.T("on_ac"), ac);
        var bat = Combo(Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray(), ProfileIndex(D.Settings.ProfileOnBattery));
        bat.SelectedIndexChanged += (_, _) => { D.Settings.ProfileOnBattery = Profiles.Get(Profiles.Order[bat.SelectedIndex]).Key; D.SaveSettings(); };
        power.AddRow(Lang.T("on_battery"), bat);
        _right.Add(power);

        var upd = new CardSection(Lang.T("set_grp_updates"));
        upd.AddRow(Lang.T("set_check_updates"), Toggle(D.Settings.UpdateCheckEnabled, v => { D.Settings.UpdateCheckEnabled = v; D.SaveSettings(); }));
        _right.Add(upd);

        var hk = new CardSection(Lang.T("set_hotkeys"));
        foreach (var (key, label) in Acts)
        {
            var box = new HotkeyBox { Width = 220 };
            box.SetValue(D.Settings.Hotkeys.TryGetValue(key, out var hd) ? hd : new HotkeyDef());
            string k = key;
            box.Leave += (_, _) => { D.Settings.Hotkeys[k] = box.Value.Clone(); D.SaveSettings(); D.SettingsChanged(); };
            _boxes[key] = box;
            hk.AddRow(key == "Cycle" ? Lang.T("cycle") : label, box);
        }
        var reset = new Button { Text = Lang.T("set_default"), AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Ui.StyleGhost(reset);
        reset.Click += (_, _) => ResetHotkeys();
        hk.AddRow(null, reset);
        _right.Add(hk);

        foreach (var c in _left.Concat(_right)) Controls.Add(c);
        Layout2(); ApplyTheme();
    }

    private void ResetHotkeys()
    {
        var def = new AppSettings(); def.EnsureDefaults();
        foreach (var (key, box) in _boxes)
        {
            box.SetValue(def.Hotkeys[key]);
            D.Settings.Hotkeys[key] = def.Hotkeys[key].Clone();
        }
        D.SaveSettings(); D.SettingsChanged();
    }

    private int ProfileIndex(string key)
    {
        for (int i = 0; i < Profiles.Order.Length; i++)
            if (Profiles.Get(Profiles.Order[i]).Key == key) return i;
        return 1;
    }

    private FlowLayoutPanel BuildSwatches(ProfileId id)
    {
        string key = Profiles.Get(id).Key;
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = true, MaximumSize = new Size(300, 0) };
        var list = new List<Panel>(); _swatches[key] = list;
        string sel = ColorTranslator.ToHtml(D.Settings.ColorFor(id));
        foreach (var hex in Profiles.Palette)
        {
            var sw = new Panel { Size = new Size(26, 22), BackColor = ColorTranslator.FromHtml(hex), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 5, 5), Tag = hex };
            string ph = hex;
            sw.Paint += (s, e) =>
            {
                if (string.Equals(D.Settings.Colors.TryGetValue(key, out var cur) ? cur : sel, ph, StringComparison.OrdinalIgnoreCase))
                {
                    using var p1 = new Pen(Color.White, 2); e.Graphics.DrawRectangle(p1, 2, 2, sw.Width - 5, sw.Height - 5);
                    using var p2 = new Pen(Color.FromArgb(80, 0, 0, 0), 1); e.Graphics.DrawRectangle(p2, 0, 0, sw.Width - 1, sw.Height - 1);
                }
            };
            sw.Click += (s, e) => { D.Settings.Colors[key] = ph; D.SaveSettings(); D.SettingsChanged(); foreach (var p in list) p.Invalidate(); };
            flow.Controls.Add(sw); list.Add(sw);
        }
        return flow;
    }

    private ComboBox Combo(string[] items, int sel)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        c.Items.AddRange(items);
        c.SelectedIndex = Math.Clamp(sel, 0, items.Length - 1);
        return c;
    }

    private ToggleSwitch Toggle(bool on, Action<bool> onChange)
    {
        var t = new ToggleSwitch { Checked = on };
        t.Toggled += v => onChange(v);
        return t;
    }

    // ---------------- card ----------------
    private sealed class CardSection : Panel
    {
        private readonly Label _head;
        private readonly List<(Label? label, Control ctl)> _rows = new();

        public CardSection(string title)
        {
            DoubleBuffered = true;
            BackColor = Theme.Card;
            _head = new Label { Text = title.ToUpperInvariant(), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            Controls.Add(_head);
        }

        public void AddRow(string? label, Control ctl)
        {
            Label? l = null;
            if (label != null) { l = new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 10.5f) }; Controls.Add(l); }
            Controls.Add(ctl);
            _rows.Add((l, ctl));
        }

        public void Relayout(int width)
        {
            Width = width;
            const int pad = 18;
            int y = 16;
            _head.Location = new Point(pad, y);
            y += _head.Height + 14;
            foreach (var (l, ctl) in _rows)
            {
                int rowH = Math.Max(l?.Height ?? 0, ctl.Height);
                if (l != null) l.Location = new Point(pad, y + (rowH - l.Height) / 2);
                int cx = l != null ? Width - pad - ctl.Width : pad;
                ctl.Location = new Point(Math.Max(pad, cx), y + (rowH - ctl.Height) / 2);
                y += rowH + 16;
            }
            Height = y + 2;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Card;
            _head.ForeColor = Theme.Accent; _head.BackColor = Theme.Card;
            foreach (var (l, ctl) in _rows)
            {
                if (l != null) { l.ForeColor = Theme.Text; l.BackColor = Theme.Card; }
                if (ctl is FlowLayoutPanel fp) { fp.BackColor = Theme.Card; foreach (Control _ in fp.Controls) { } }
                if (ctl is HotkeyBox hb) { hb.BackColor = Theme.Surface; hb.ForeColor = Theme.Text; }
                ctl.Invalidate();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Card);
            using var pen = new Pen(Theme.Border);
            using var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>Small segmented control (themed).</summary>
public sealed class SegControl : Control
{
    private readonly string[] _items;
    private int _sel;
    public event Action<int>? SelectedChanged;
    public int Selected { get => _sel; set { _sel = value; Invalidate(); } }

    public SegControl(string[] items, int sel) { _items = items; _sel = sel; DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int seg = Math.Clamp(e.X * _items.Length / Math.Max(1, Width), 0, _items.Length - 1);
        if (seg != _sel) { _sel = seg; Invalidate(); SelectedChanged?.Invoke(_sel); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(outer, 8))
        {
            using var b = new SolidBrush(Theme.Card); g.FillPath(b, path);
            using var pen = new Pen(Theme.Border); g.DrawPath(pen, path);
        }
        float seg = (float)Width / _items.Length;
        for (int i = 0; i < _items.Length; i++)
        {
            var r = new RectangleF(i * seg, 0, seg, Height);
            if (i == _sel)
            {
                using var path = Theme.RoundRect(new RectangleF(r.X + 2, 2, r.Width - 4, Height - 4), 7);
                using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, _items[i], new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Rectangle.Round(r), i == _sel ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>iOS-style themed on/off switch.</summary>
public sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event Action<bool>? Toggled;
    public bool Checked { get => _checked; set { _checked = value; Invalidate(); } }

    public ToggleSwitch() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; Size = new Size(52, 28); }

    protected override void OnClick(EventArgs e) { _checked = !_checked; Invalidate(); Toggled?.Invoke(_checked); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using (var path = Theme.RoundRect(r, Height / 2f))
        {
            using var b = new SolidBrush(_checked ? Theme.Accent : Theme.BorderStrong);
            g.FillPath(b, path);
        }
        float d = Height - 8;
        float kx = _checked ? Width - d - 4 : 4;
        using var kb = new SolidBrush(Color.White);
        g.FillEllipse(kb, kx, 4, d, d);
    }
}
