using System.Drawing.Drawing2D;

namespace GhostDeck;

// =====================================================================
//  List: filter chips + the table (rows grow, zebra) + expandable details
// =====================================================================
/// <summary>
/// The model table. Rows grow to fit the name, firmware list and thank-you list (no "..."),
/// alternate rows get Theme.RowAlt, the detected model keeps its accent wash. A click opens the
/// row's details under it (all prefixes, dates, fourth mode, backup path, the report link, every
/// thanked login as its own link, copy row). Row heights are cached per width/rows/language and
/// computed from cached word widths; only rows inside the repaint area are drawn.
/// </summary>
internal sealed class ModelsListView : ModelsView
{
    // Model and Thanks get the widest columns: both wrap onto extra lines instead of ending in "..."
    private static readonly float[] Lefts = { 0f, .25f, .35f, .43f, .53f, .67f, .72f, .83f };
    private const int HeadH = 34, RowH = 32;

    private int[] _rowH = Array.Empty<int>();
    private string[][] _cells = Array.Empty<string[]>();
    private int _rowHWidth = -1;
    private IReadOnlyList<DeviceProfile>? _rowHFor;
    private string _rowHLang = "";

    public ModelsListView(ModelsPage p) : base(p) { }

    private static Font FHead => ModelsPage.FHead;
    private static Font FCell => ModelsPage.FCell;
    private static Font FMono => ModelsPage.FMono;

    private int HeadHS => Math.Max(S(HeadH), FHead.Height + S(16));
    private int BaseRowH => Math.Max(S(RowH), FCell.Height + S(14));
    private int TextTop => Math.Max(S(4), (BaseRowH - ModelsText.LineH(FCell)) / 2);

    private static string[] Headers() => new[]
    {
        Lang.T("st_model"), Lang.T("st_firmware"), Lang.T("mdl_c_family"),
        Lang.T("mdl_c_status"), Lang.T("mdl_c_curve"), Lang.T("mdl_c_sb"), Lang.T("mdl_c_rpm"),
        Lang.T("mdl_c_thanks"),
    };

    // Family and SB hold two letters or a tick, so their width comes from the header plus its help
    // dot (long in ru/de/fr). Status holds one of two words that must never break mid-word
    // ("Eksperymentalny"), so it is as wide as the longer one. The rest share what is left.
    private static bool FixedCol(int i) => i == 2 || i == 3 || i == 5;
    private int[] Cols(int width)
    {
        int avail = width - Pad * 2, inner = avail - 36;
        int n = Lefts.Length;
        var head = Headers();
        var w = new float[n];
        float fixedW = 0, weightSum = 0;
        for (int i = 0; i < n; i++)
        {
            if (i == 2 || i == 5) w[i] = ModelsText.NaturalW(head[i], FHead) + S(30) + 10;
            else if (i == 3)
                w[i] = Math.Max(ModelsText.NaturalW(head[i], FHead),
                    Math.Max(ModelsText.NaturalW(Lang.T("tier_tested"), FCell), ModelsText.NaturalW(Lang.T("tier_experimental"), FCell))) + 10;
            if (FixedCol(i)) fixedW += w[i];
            else weightSum += (i + 1 < n ? Lefts[i + 1] : 1f) - Lefts[i];
        }
        float rest = Math.Max(0, inner - fixedW);
        for (int i = 0; i < n; i++)
            if (!FixedCol(i)) w[i] = rest * ((i + 1 < n ? Lefts[i + 1] : 1f) - Lefts[i]) / weightSum;
        int[] cx = new int[n + 1];
        float x = Pad + 18;
        for (int i = 0; i < n; i++) { cx[i] = (int)x; x += w[i]; }
        cx[n] = Pad + avail - 14;
        return cx;
    }
    private static int ColW(int[] cx, int c) => cx[c + 1] - cx[c] - 10;

    private sealed record RowText(string Name, string Fw, string Family, string Status, Color StatusCol,
        string Curve, Color CurveCol, bool Sb, string Rpm, bool HasRpm, string Credit);

    private static RowText Texts(DeviceProfile m)
    {
        bool tested = m.Tier == Tier.Tested;
        string curve; Color curveCol;
        if (m.FanCurve is { } fc)
        {
            // Same distinction as docs/SUPPORTED_MODELS.md: a verified curve on a model still
            // awaiting its profile checks is editable only after the opt-in.
            if (fc.Verified) { curve = "✓ " + Lang.T(tested ? "mdl_curve_edit" : "mdl_curve_vopt"); curveCol = Theme.Green; }
            else { curve = "◉ " + Lang.T("mdl_curve_prev"); curveCol = Theme.Accent; }
        }
        else { curve = "-"; curveCol = Theme.Muted; }
        // spaces around "/" let a two-fan pair wrap instead of being cut
        string rpm = m.CpuRpmAddr16 != 0
            ? (m.GpuRpmAddr16 == 0 ? $"✓ 0x{m.CpuRpmAddr16:X2}:{m.CpuRpmAddr16 + 1:X2}"   // wide (16-bit) pairs
                                   : $"✓ 0x{m.CpuRpmAddr16:X2}:{m.CpuRpmAddr16 + 1:X2} / {m.GpuRpmAddr16:X2}:{m.GpuRpmAddr16 + 1:X2}")
            : m.CpuRpmAddr == 0 ? "-"
            : m.GpuRpmAddr == 0 ? $"✓ 0x{m.CpuRpmAddr:X2}"   // single-tach boards (iGPU)
            : $"✓ 0x{m.CpuRpmAddr:X2} / 0x{m.GpuRpmAddr:X2}";
        return new RowText(m.Name, string.Join(", ", m.FirmwarePrefixes), m.ShiftMode == 0xF2 ? "G1" : "G2",
            Lang.T(tested ? "tier_tested" : "tier_experimental"), tested ? Theme.Green : Theme.Amber,
            curve, curveCol, ModelsStats.HasSuperBattery(m), rpm, ModelsStats.HasRpm(m),
            m.Credit.Length > 0 ? "@" + m.Credit : "-");
    }

    private static readonly int[] WrapCols = { 0, 1, 3, 4, 6, 7 };
    private static Font CellFont(int k, RowText t) => k == 1 || (k == 4 && t.HasRpm) ? FMono : FCell;
    private int BadgeW() => ModelsText.NaturalW(Lang.T("mdl_badge_new"), ModelsPage.FSmall) + S(12);

    private int[] RowHeights(int width)
    {
        var rows = P.Rows;
        if (width == _rowHWidth && ReferenceEquals(rows, _rowHFor) && _rowH.Length == rows.Count
            && _rowHLang == Lang.CurrentCode) return _rowH;
        int[] cx = Cols(width);
        int baseH = BaseRowH, top = TextTop;
        var hs = new int[rows.Count];
        var cells = new string[rows.Count][];
        for (int i = 0; i < rows.Count; i++)
        {
            var t = Texts(rows[i]);
            string[] raw = { t.Name, t.Fw, t.Status, t.Curve, t.Rpm, t.Credit };
            var row = cells[i] = new string[raw.Length];
            int need = 0;
            for (int k = 0; k < raw.Length; k++)
            {
                var font = CellFont(k, t);
                int w = ColW(cx, WrapCols[k]);
                if (k == 0 && P.IsNew(rows[i])) w -= BadgeW() + S(6);   // the NEW badge sits at the cell's right
                row[k] = ModelsText.BreakLongWords(raw[k], font, w);
                need = Math.Max(need, ModelsText.TextH(row[k], font, w));
            }
            hs[i] = Math.Max(baseH, need + top * 2);
        }
        _rowH = hs; _cells = cells; _rowHWidth = width; _rowHFor = rows; _rowHLang = Lang.CurrentCode;
        return hs;
    }

    // ---- toolbar (filter chips + count) ----
    private static (string Key, string Label)[] Chips() => new[]
    {
        ("t", Lang.T("mdl_fl_tested")), ("e", Lang.T("mdl_fl_exp")), ("g1", "G1"), ("g2", "G2"),
        ("c", Lang.T("mdl_fl_curve")), ("r", Lang.T("mdl_fl_rpm")), ("a", Lang.T("mdl_f_apex")), ("n", Lang.T("mdl_fl_new")),
    };

    private int Toolbar(Graphics? g, int width, int y)
    {
        int x = Pad, right = width - Pad;
        string count = string.Format(Lang.T("mdl_count"), P.Rows.Count, P.All.Count);
        int countW = ModelsText.NaturalW(count, ModelsPage.FSmall) + S(8);
        int cx = x, cy = y;
        foreach (var (key, label) in Chips())
        {
            if (key == "n" && P.NewCount == 0) continue;
            int cw = ModelsText.NaturalW(label, ModelsPage.FSmall) + S(16);
            if (cx + cw > right - countW && cx > x) { cx = x; cy += ChipH() + S(6); }
            bool on = P.Filters.Contains(key);
            Chip(g, cx, cy, label, Theme.Accent, false, filled: on);
            string k = key;
            Zone(g, new Rectangle(cx, cy, cw, ChipH()), () => P.ToggleFilter(k), null, HoverKind.Chip);
            cx += cw + S(6);
        }
        T(g, count, ModelsPage.FSmall, new Rectangle(right - countW, y, countW, ChipH()), Theme.Faint,
          TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return cy + ChipH() + S(12) - y;
    }

    // ---- details under an opened row ----
    private int Details(Graphics? g, DeviceProfile m, int x, int y, int w)
    {
        var f = FCell;
        string Dash = "-";
        var lines = new List<(string Label, string Value)>
        {
            (Lang.T("mdl_d_prefixes"), string.Join(", ", m.FirmwarePrefixes)),
            (Lang.T("mdl_c_family"), (m.ShiftMode == 0xF2 ? "G1" : "G2") + "  (msi-ec)"),
            (Lang.T("mdl_d_added"), ModelHistory.Date(m.Added) is { } a ? ModelsStats.Day(a) : Dash),
            (Lang.T("mdl_d_testedon"), ModelHistory.Date(m.TestedOn) is { } t ? ModelsStats.Day(t) : Dash),
            (Lang.T("mdl_f_apex"), m.FourthMode != null ? Lang.T("mdl_d_known") + " (" + m.FourthMode.Name + ")" : Lang.T("mdl_d_none")),
            (Lang.T("mdl_f_backup"), Devices.BlockPathOf(m) != null ? Lang.T("mdl_d_backup_yes") : Lang.T("mdl_d_none")),
        };
        int labelW = lines.Select(l => l.Label).Append(Lang.T("mdl_d_people")).Max(l => ModelsText.NaturalW(l, ModelsPage.FSmall)) + S(16);
        int vx = x + labelW, vw = w - labelW;
        int cy = y;
        foreach (var (label, value) in lines)
        {
            T(g, label, ModelsPage.FSmall, new Rectangle(x, cy + 1, labelW, ModelsText.LineH(f)), Theme.Faint,
              TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            cy += Para(g, value, f, vx, cy, vw, Theme.Text) + S(4);
        }
        // every thanked person with every one of their reports (each a link)
        T(g, Lang.T("mdl_d_people"), ModelsPage.FSmall, new Rectangle(x, cy + 1, labelW, ChipH()), Theme.Faint,
          TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        int ph = People(g, m, vx, cy, vw);
        if (ph == 0) ph = Para(g, Lang.T("mdl_d_noissue"), f, vx, cy, vw, Theme.Muted);
        cy += ph + S(10);
        // copy row, with the result on the button for two seconds
        bool justCopied = ReferenceEquals(_copied, m);
        string copyLabel = !justCopied ? Lang.T("mdl_d_copy") : _copyOk ? "✓ " + Lang.T("mdl_d_copied") : Lang.T("mdl_d_copy_failed");
        Button(g, vx, cy, copyLabel, justCopied && _copyOk, () =>
        {
            var t = Texts(m);
            string people = string.Join("; ", ModelsStats.Logins(m.Credit).Select(l =>
                "@" + l + (m.CreditIssues.TryGetValue(l, out var n) && n.Length > 0 ? " " + string.Join(" ", n.Select(k => "#" + k)) : "")));
            string text = $"{m.Name} | {t.Fw} | {t.Status} | {t.Curve} | SB {(t.Sb ? "yes" : "no")} | RPM {t.Rpm}"
                        + (people.Length > 0 ? " | " + people : "") + $" | db {Devices.EffectiveDataVersion}";
            _copyOk = Ui.CopyText(text);
            _copied = m;
            Invalidate();
            _copyTimer ??= new System.Windows.Forms.Timer { Interval = 2000 };
            _copyTimer.Tick -= CopyDone; _copyTimer.Tick += CopyDone;
            _copyTimer.Stop(); _copyTimer.Start();
        });
        cy += ButtonH() + S(6);
        return cy - y;
    }

    private DeviceProfile? _copied;
    private bool _copyOk;
    private System.Windows.Forms.Timer? _copyTimer;
    private void CopyDone(object? sender, EventArgs e) { _copyTimer?.Stop(); _copied = null; Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) _copyTimer?.Dispose(); base.Dispose(disposing); }

    private int DetailH(DeviceProfile m, int width)
    {
        bool col = Collecting;
        Collecting = false;                       // measuring pass: no zones
        try { return S(12) + Details(null, m, Pad + S(46), 0, width - Pad * 2 - S(46) - S(24)) + S(10); }
        finally { Collecting = col; }
    }

    protected override int Render(Graphics? g, int width, Rectangle clip)
    {
        int y = S(2);
        y += Toolbar(g, width, y);
        int avail = width - Pad * 2;
        int[] cx = Cols(width);
        int[] rowH = RowHeights(width);
        int headH = HeadHS, top = TextTop;
        var rows = P.Rows;
        int exp = P.Expanded == null ? -1 : IndexOf(rows, P.Expanded);
        int detH = exp >= 0 ? DetailH(rows[exp], width) : 0;

        int tableTop = y;
        int tableH = headH + (rows.Count == 0 ? BaseRowH : rowH.Sum()) + detH + 10;
        var headers = Headers();
        if (Draw(g, new Rectangle(Pad, tableTop, avail, tableH)))
        {
            Ui.FillCard(g, new RectangleF(Pad, tableTop, avail, tableH));
            for (int c = 0; c < headers.Length; c++)
                T(g, headers[c], FHead, new Rectangle(cx[c], tableTop + S(8), ColW(cx, c), headH - S(8)), Theme.Muted,
                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);   // dpi-ok: model table headers in columns sized to their content; last resort
            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, cx[0], tableTop + headH, Pad + avail - 14, tableTop + headH);
        }
        // Family and SB carry the app's help dot: click to open the bubble, click again to dismiss.
        // Family is there because G1 / G2 is msi-ec's grouping, not an MSI designation.
        int dot = S(18), dotY = tableTop + S(8) + (headH - S(8) - dot) / 2;
        foreach (var (col, key) in new[] { (2, "mdl_family_tip"), (5, "mdl_sb_tip") })
        {
            int tw = ModelsText.NaturalW(headers[col], FHead);
            if (g != null) HelpDot.Render(g, new RectangleF(cx[col] + tw + S(6), dotY, dot, dot));
            var hr = new Rectangle(cx[col], tableTop, tw + S(30), headH);
            string k = key;
            Zone(g, hr, () => HelpPopup.Toggle(this, hr, Lang.T(k), this), null, HoverKind.None);
        }

        int ry = tableTop + headH;
        for (int i = 0; i < rows.Count; i++)
        {
            var m = rows[i];
            int h = rowH[i];
            bool open = i == exp;
            int fullH = h + (open ? detH : 0);
            if (g != null && ry < clip.Bottom && ry + fullH > clip.Top)
            {
                // zebra striping keeps the eye on its row across eight columns; the detected model
                // keeps its accent wash and bar; an opened row and its details share one tint
                Color? fill = ReferenceEquals(m, P.Detected) ? Theme.AccentSoft : open ? Theme.RowAlt : i % 2 == 1 ? Theme.RowAlt : null;
                if (fill is { } fc) { using var b = new SolidBrush(fc); g.FillRectangle(b, Pad + 8, ry, avail - 16, fullH); }
                if (ReferenceEquals(m, P.Detected)) { using var bar = new SolidBrush(Theme.Accent); g.FillRectangle(bar, Pad + 8, ry, S(4), fullH); }

                var t = Texts(m);
                var c = _cells[i];
                int nameW = ColW(cx, 0) - (P.IsNew(m) ? BadgeW() + S(6) : 0);
                Cell(g, cx[0], ry, top, h, nameW, c[0], FCell, Theme.Text);
                if (P.IsNew(m))
                {
                    var br = new Rectangle(cx[0] + nameW + S(6), ry + top, BadgeW(), ModelsText.LineH(ModelsPage.FSmall));
                    using var path = Theme.RoundRect(new RectangleF(br.X, br.Y, br.Width, br.Height), br.Height / 2f);
                    using var bb = new SolidBrush(Theme.Accent); g.FillPath(bb, path);
                    T(g, Lang.T("mdl_badge_new"), ModelsPage.FSmall, br, Theme.Surface,
                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
                Cell(g, cx[1], ry, top, h, ColW(cx, 1), c[1], FMono, Theme.Text);
                Cell(g, cx[2], ry, top, h, ColW(cx, 2), t.Family, FCell, Theme.Muted);
                Cell(g, cx[3], ry, top, h, ColW(cx, 3), c[2], FCell, t.StatusCol);
                Cell(g, cx[4], ry, top, h, ColW(cx, 4), c[3], FCell, t.CurveCol);
                Cell(g, cx[5], ry, top, h, ColW(cx, 5), t.Sb ? "✓" : "-", FCell, t.Sb ? Theme.Text : Theme.Muted);
                Cell(g, cx[6], ry, top, h, ColW(cx, 6), c[4], t.HasRpm ? FMono : FCell, t.HasRpm ? Theme.Text : Theme.Muted);
                Cell(g, cx[7], ry, top, h, ColW(cx, 7), c[5], FCell, m.Credit.Length > 0 ? Theme.Accent : Theme.Muted);
                if (open) Details(g, m, Pad + S(46), ry + h + S(4), avail - S(46) - S(24));
            }
            else if (g != null && open)
                Details(g, m, Pad + S(46), ry + h + S(4), avail - S(46) - S(24));   // zones of an open row off screen
            var mm = m;
            Zone(g, new Rectangle(Pad + 8, ry, avail - 16, h), () => P.ToggleExpanded(mm), Lang.T("mdl_d_click"));
            ry += fullH;
        }
        if (rows.Count == 0)
            T(g, "-", FCell, new Rectangle(cx[0], ry, avail, BaseRowH), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        // "not on the list?" box under the table
        int by = tableTop + tableH + S(14);
        bool collect = Collecting;
        Collecting = false;                       // measuring pass: no zones
        int bh = MissingBox(null, by, width);
        Collecting = collect;
        if (g != null) MissingBox(g, by, width);
        return by + bh + S(14);
    }

    private int MissingBox(Graphics? g, int y, int width)
    {
        int x = Pad, w = width - Pad * 2;
        string btn = Lang.T("mdl_btn_report");
        int bw = ModelsText.NaturalW(btn, ModelsPage.FBold) + S(24);
        int tw = w - S(36) - bw - S(16);
        var (t, th) = ModelsText.Fit(Lang.T("mdl_missing"), FCell, tw);
        int h = Math.Max(th, ButtonH()) + S(24);
        if (Draw(g, new Rectangle(x, y, w, h)))
        {
            using var path = Theme.RoundRect(new RectangleF(x + 0.5f, y + 0.5f, w - 1, h - 1), 8);
            using var pen = new Pen(Theme.BorderStrong) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, path);
            T(g, t, FCell, new Rectangle(x + S(18), y + (h - th) / 2, tw, th + 2), Theme.Muted, ModelsText.Wrap);
        }
        Button(g, x + w - S(18) - bw, y + (h - ButtonH()) / 2, btn, false, P.Verify);
        return h;
    }

    private static int IndexOf(IReadOnlyList<DeviceProfile> rows, DeviceProfile m)
    {
        for (int i = 0; i < rows.Count; i++) if (ReferenceEquals(rows[i], m)) return i;
        return -1;
    }

    private void Cell(Graphics g, int x, int ry, int top, int h, int w, string text, Font font, Color color)
    {
        // cap the box at MaxLines lines so the ellipsis lands on the last allowed line; a shorter
        // font (the monospaced firmware and RPM cells) is centred on the first line
        int lh = ModelsText.LineH(font), dy = Math.Max(0, (ModelsText.LineH(FCell) - lh) / 2);
        int boxH = Math.Min(h - top - dy, lh * ModelsText.MaxLines);
        TextRenderer.DrawText(g, text, font, new Rectangle(x, ry + top + dy, w, boxH), color, ModelsText.Wrap);
    }
}
