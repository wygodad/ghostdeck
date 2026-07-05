using System.Drawing.Drawing2D;

namespace GhostDeck;

// =====================================================================
//  Models — full list of recognised firmware IDs (from Devices.All)
// =====================================================================
/// <summary>
/// Read-only catalogue of every <see cref="DeviceProfile"/> the app knows, rendered live from
/// <see cref="Devices.All"/> so it never drifts from the code. A fixed header (title + detected
/// model + search box) sits above a scrolling table; the user's detected model is highlighted.
/// Mirrors docs/SUPPORTED_MODELS.md (same columns).
/// </summary>
public sealed class ModelsPage : ThemedPage
{
    private const int Pad = 28;
    private readonly DeviceProfile? _detected;
    private readonly List<DeviceProfile> _all;
    private List<DeviceProfile> _rows;

    private readonly Panel _scrollHost = new() { AutoScroll = true };
    private readonly Table _table;
    private readonly TextBox _search = new();
    private readonly Button _verify = new();
    private readonly Panel _footer = new();
    private readonly ToolTip _tip = new() { InitialDelay = 250, AutoPopDelay = 8000 };

    public ModelsPage(MainDeps d) : base(d)
    {
        AutoScroll = false;   // the inner _scrollHost scrolls the table; the header stays fixed
        _detected = Devices.Detect(d.Firmware);
        _all = Devices.All
            .OrderBy(x => x.Tier == Tier.Tested ? 0 : (x.ShiftMode == 0xF2 ? 2 : 1))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _rows = _all;

        _table = new Table(this);
        _scrollHost.Controls.Add(_table);
        Controls.Add(_scrollHost);

        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Font = new Font("Segoe UI", 10.5f);
        _search.Width = 300;
        _search.PlaceholderText = Lang.T("mdl_search");
        _search.TextChanged += (_, _) => ApplyFilter();
        Controls.Add(_search);
        _search.BringToFront();

        // Bottom footer bar (white): table legend on the left, "Verify my model" CTA on the right.
        _footer.Paint += PaintFooter;
        Controls.Add(_footer);
        Ui.StylePrimary(_verify);
        _verify.Text = Lang.T("models_verify_btn");
        _verify.AutoSize = false;
        _verify.Click += (_, _) => (FindForm() as MainForm)?.ShowReport(0);
        _tip.SetToolTip(_verify, Lang.T("models_verify_desc"));
        _footer.Controls.Add(_verify);

        ClientSizeChanged += (_, _) => LayoutBits();
    }

    private void PaintFooter(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, 0, _footer.Width, 0);
        int legendRight = _verify.Left - 20;
        TextRenderer.DrawText(g, Lang.T("mdl_legend"), FLegend,
            new Rectangle(Pad, 10, Math.Max(60, legendRight - Pad), _footer.Height - 16), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    public override void OnEnter() { StyleSearch(); LayoutBits(); Invalidate(); _table.Invalidate(); }
    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _scrollHost.BackColor = Theme.Surface;
        _table.BackColor = Theme.Surface;
        _footer.BackColor = Theme.Surface;
        StyleSearch();
        Ui.StylePrimary(_verify);
        Invalidate(); _table.Invalidate(); _footer.Invalidate();
    }

    private void StyleSearch() { _search.BackColor = Theme.Card; _search.ForeColor = Theme.Text; }

    internal DeviceProfile? Detected => _detected;
    internal IReadOnlyList<DeviceProfile> Rows => _rows;
    internal ToolTip Tip => _tip;

    private void ApplyFilter()
    {
        string q = _search.Text.Trim();
        _rows = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(x =>
                x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.FirmwarePrefixes.Any(f => f.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        LayoutTable();
        _table.Invalidate();
    }

    // ---- fonts / metrics ----
    private static readonly Font FTitle = new("Segoe UI", 18f, FontStyle.Bold);
    private static readonly Font FSub = new("Segoe UI", 11f, FontStyle.Bold);
    private static readonly Font FIntro = new("Segoe UI", 10f);
    internal static readonly Font FHead = new("Segoe UI", 9.5f, FontStyle.Bold);
    internal static readonly Font FCell = new("Segoe UI", 10.5f);
    internal static readonly Font FMono = new("Consolas", 10.5f);
    internal static readonly Font FLegend = new("Segoe UI", 9f);
    internal static readonly Font FIcon = new("Segoe UI Symbol", 10.5f, FontStyle.Bold);

    internal const int HeadH = 34, RowH = 32;
    internal static readonly float[] Lefts = { 0f, .30f, .48f, .56f, .68f, .82f, .89f };

    private int HeaderBandH() => 24 + FTitle.Height + 14 + FSub.Height + 10 + FIntro.Height * 2 + 18;

    private void LayoutBits()
    {
        int hb = HeaderBandH();
        // search box: top-right of the fixed header band
        _search.Location = new Point(Math.Max(Pad, ClientSize.Width - Pad - _search.Width), 26);

        // Fixed bottom footer: legend (left) + Verify CTA (right). The table scrolls between header and footer.
        int footerH = Math.Max(66, FLegend.Height * 3 + 26);
        _footer.SetBounds(0, ClientSize.Height - footerH, ClientSize.Width, footerH);
        int vw = TextRenderer.MeasureText(_verify.Text, _verify.Font).Width + 44, vh = 42;
        _verify.SetBounds(_footer.Width - Pad - vw, (footerH - vh) / 2, vw, vh);

        _scrollHost.SetBounds(0, hb, ClientSize.Width, Math.Max(0, ClientSize.Height - hb - footerH));
        _footer.Invalidate();
        LayoutTable();
    }

    private void LayoutTable()
    {
        int w = _scrollHost.ClientSize.Width;
        _table.Width = w;
        _table.Height = Math.Max(_table.ContentHeight(w), _scrollHost.ClientSize.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int avail = ClientSize.Width - Pad * 2;

        TextRenderer.DrawText(g, Lang.T("tab_models"), FTitle, new Point(Pad, 24), Theme.Text);

        int y = 24 + FTitle.Height + 14;
        if (_detected is { } det)
        {
            string sub = Lang.T("mdl_you") + ":  " + det.Name + "   ·   " + D.Firmware;
            TextRenderer.DrawText(g, sub, FSub, new Rectangle(Pad, y, avail - _search.Width - 20, FSub.Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            string sub = string.Format(Lang.T("mdl_unknown"), string.IsNullOrEmpty(D.Firmware) ? "?" : D.Firmware);
            TextRenderer.DrawText(g, sub, FSub, new Point(Pad, y), Color.FromArgb(0xB0, 0x4A, 0x3A));
        }
        y += FSub.Height + 10;
        TextRenderer.DrawText(g, Lang.T("mdl_intro"), FIntro, new Rectangle(Pad, y, avail, FIntro.Height * 2 + 4),
            Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.WordEllipsis);
    }

    protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }

    // =================================================================
    //  Inner scrolling table
    // =================================================================
    private sealed class Table : Control
    {
        private readonly ModelsPage _p;
        private Rectangle _sbHeaderRect;
        private bool _tipOn;

        public Table(ModelsPage p) { _p = p; DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Surface; }

        public int ContentHeight(int width)
        {
            int rows = _p.Rows.Count;
            return HeadH + rows * RowH + 10 + 14;   // legend moved to the page footer
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = _sbHeaderRect.Contains(e.Location);
            if (over && !_tipOn) { _tipOn = true; _p.Tip.Show(Lang.T("mdl_sb_tip"), this, e.X + 14, e.Y + 16, 8000); }
            else if (!over && _tipOn) { _tipOn = false; _p.Tip.Hide(this); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { if (_tipOn) { _tipOn = false; _p.Tip.Hide(this); } base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            int width = Width;
            int avail = width - Pad * 2;

            int n = Lefts.Length;
            int[] cx = new int[n + 1];
            for (int i = 0; i < n; i++) cx[i] = Pad + 18 + (int)(Lefts[i] * (avail - 36));
            cx[n] = Pad + avail - 14;
            int ColW(int c) => cx[c + 1] - cx[c] - 10;

            int rows = _p.Rows.Count;
            int tableH = HeadH + rows * RowH + 10;
            Ui.FillCard(g, new RectangleF(Pad, 0, avail, tableH));

            var headers = new[]
            {
                Lang.T("st_model"), Lang.T("st_firmware"), Lang.T("mdl_c_family"),
                Lang.T("mdl_c_status"), Lang.T("mdl_c_curve"), Lang.T("mdl_c_sb"), Lang.T("mdl_c_rpm"),
            };
            for (int c = 0; c < headers.Length; c++)
                TextRenderer.DrawText(g, headers[c], FHead, new Rectangle(cx[c], 8, ColW(c), HeadH - 8), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            int sbTextW = TextRenderer.MeasureText(headers[5], FHead).Width;
            var qRect = new Rectangle(cx[5] + sbTextW + 5, 6, 22, HeadH - 6);
            TextRenderer.DrawText(g, "ⓘ", FIcon, qRect, Theme.Accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            _sbHeaderRect = new Rectangle(cx[5], 0, sbTextW + 30, HeadH);

            int ry = HeadH;
            foreach (var m in _p.Rows)
            {
                bool active = ReferenceEquals(m, _p.Detected);
                if (active)
                {
                    using var b = new SolidBrush(Theme.AccentSoft);
                    g.FillRectangle(b, Pad + 8, ry, avail - 16, RowH);
                    using var bar = new SolidBrush(Theme.Accent);
                    g.FillRectangle(bar, Pad + 8, ry, 4, RowH);
                }
                else
                {
                    using var pen = new Pen(Theme.Border);
                    g.DrawLine(pen, cx[0], ry, Pad + avail - 14, ry);
                }

                bool tested = m.Tier == Tier.Tested;
                string family = m.ShiftMode == 0xF2 ? "G1" : "G2";
                string status = Lang.T(tested ? "tier_tested" : "tier_experimental");
                var statusCol = tested ? Theme.Green : Theme.Amber;

                string curve; Color curveCol;
                if (m.FanCurve is { } fc)
                {
                    if (fc.Verified) { curve = "✓ " + Lang.T("mdl_curve_edit"); curveCol = Theme.Green; }
                    else { curve = "◉ " + Lang.T("mdl_curve_prev"); curveCol = Theme.Accent; }
                }
                else { curve = "—"; curveCol = Theme.Muted; }

                bool sb = m.Recipes.TryGetValue(ProfileId.SuperBattery, out var sr) && sr.Any(x => x.val == 0x0F);
                string sbStr = sb ? "✓" : "—";

                string rpm = m.CpuRpmAddr != 0 ? $"✓ 0x{m.CpuRpmAddr:X2}/0x{m.GpuRpmAddr:X2}" : "—";
                string fw = string.Join(", ", m.FirmwarePrefixes);

                Cell(g, cx, 0, ry, ColW(0), m.Name, FCell, Theme.Text);
                Cell(g, cx, 1, ry, ColW(1), fw, FMono, Theme.Text);
                Cell(g, cx, 2, ry, ColW(2), family, FCell, Theme.Muted);
                Cell(g, cx, 3, ry, ColW(3), status, FCell, statusCol);
                Cell(g, cx, 4, ry, ColW(4), curve, FCell, curveCol);
                Cell(g, cx, 5, ry, ColW(5), sbStr, FCell, sb ? Theme.Text : Theme.Muted);
                Cell(g, cx, 6, ry, ColW(6), rpm, m.CpuRpmAddr != 0 ? FMono : FCell, m.CpuRpmAddr != 0 ? Theme.Text : Theme.Muted);

                ry += RowH;
            }

            if (rows == 0)
                TextRenderer.DrawText(g, "—", FCell, new Rectangle(cx[0], HeadH, avail, RowH), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private static void Cell(Graphics g, int[] cx, int c, int ry, int w, string text, Font font, Color color) =>
            TextRenderer.DrawText(g, text, font, new Rectangle(cx[c], ry, w, RowH), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
