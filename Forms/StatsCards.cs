using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GhostDeck;

/// <summary>
/// The anonymous-statistics consent card (plan §8.3, §12 pkt 8). Laid out to be read in a few
/// seconds: a lead sentence (support GhostDeck), a highlighted box with what you get back inside
/// the app, then two side-by-side panels "what we send" (cyan ticks) and "what we never send"
/// (red crosses), and one muted line on the country and where to switch it off. Two equal choices
/// plus a third small action that opens the report as it would go out now.
/// Item lists come from Lang keys as lines separated by "\n".
/// </summary>
public sealed class StatsConsentCard : GhostCardForm
{
    private readonly Action _sample;
    private readonly Action? _stop;
    private readonly string _lead, _gain, _sentH, _neverH, _note;
    private readonly string[] _sent, _never;

    /// <param name="infoOnly">Opened from Settings to read what is collected: one OK button and the
    /// sample report, no choice on the card (the switch next to it in Settings is the choice).</param>
    /// <param name="again">(1.46) Asked again because the report gained fields since this user said
    /// yes: the lead names what is new, the buttons are "Keep sharing" / "Stop sharing", and
    /// <paramref name="onStop"/> runs for the second one; the ✕ is "not now" (asked again next start).</param>
    public StatsConsentCard(Action onShare, Action onShowSample, bool infoOnly = false, bool again = false, Action? onStop = null)
        : base("//STATS", Lang.T(again ? "stats_reconsent_title" : "stats_consent_title"), "",
               infoOnly ? Lang.T("gen_ok") : Lang.T(again ? "stats_reconsent_yes" : "stats_consent_yes"),
               infoOnly ? "" : Lang.T(again ? "stats_reconsent_no" : "stats_consent_no"),
               infoOnly ? () => { } : onShare)
    {
        _sample = onShowSample;
        _stop = again ? onStop : null;
        _lead = Lang.T(again ? "stats_reconsent_lead" : "stats_consent_lead");
        _gain = Lang.T("stats_consent_gain");
        _sentH = Lang.Upper(Lang.T("stats_consent_sent_h"));
        _neverH = Lang.Upper(Lang.T("stats_consent_never_h"));
        _note = Lang.T("stats_consent_note");
        _sent = Split(Lang.T("stats_consent_sent"));
        _never = Split(Lang.T("stats_consent_never"));
    }

    private static string[] Split(string s) =>
        s.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    protected override int CardWidth => 780;
    protected override int SideInset => 12;   // air between the rail and the text, and on the right
    protected override string AuxLabel => Lang.T("stats_sample");
    // the brand violet (the rail's lower end), lightened for text: not the cyan of "Share", not the red of "never sent"
    protected override Color AuxColor => Color.FromArgb(0xB0, 0x96, 0xFF);
    protected override void AuxClick() => _sample();
    protected override void OnLater() => _stop?.Invoke();

    protected override int MeasureContent(Graphics g, float k, int width) => Flow(g, k, width, null);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;   // firmer strokes than plain anti-aliasing on the dark card
        Flow(g, k, area.Width, area.Location);
        g.TextRenderingHint = hint;
    }

    /// <summary>Lays the card out top to bottom; paints when <paramref name="at"/> is given. Returns the height.</summary>
    private int Flow(Graphics g, float k, int width, Point? at)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        using var leadF = Dpi.Font("Segoe UI Semibold", 18f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var gainF = Dpi.Font("Segoe UI", 16f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var headF = Dpi.Font("Segoe UI", 13.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var itemF = Dpi.Font("Segoe UI", 16f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var noteF = Dpi.Font("Segoe UI", 14.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.Word };
        bool paint = at != null;
        int x = at?.X ?? 0, y0 = at?.Y ?? 0, y = y0;
        float H(string s, Font f, int w) => g.MeasureString(s, f, w, fmt).Height;
        void Text(string s, Font f, Color c, float tx, float ty, int w, float h)
        {
            if (!paint) return;
            using var b = new SolidBrush(c);
            g.DrawString(s, f, b, new RectangleF(tx, ty, w, h + 3 * k), fmt);
        }

        // ---- lead: the one bright, heavier line ----
        float hLead = H(_lead, leadF, width);
        Text(_lead, leadF, White, x, y, width, hLead);
        y += Ce(hLead) + Ce(22 * k);

        // ---- "you will see part of it in the app" box: a soft tint and a chart icon, no extra edge line ----
        int boxPadX = Ce(20 * k), boxPadY = Ce(18 * k), icon = Ce(26 * k), iconGap = Ce(16 * k);
        int gainW = width - boxPadX * 2 - icon - iconGap;
        float hGain = H(_gain, gainF, gainW);
        int hBox = Math.Max(Ce(hGain), icon) + boxPadY * 2;
        if (paint)
        {
            var box = new Rectangle(x, y, width, hBox);
            using (var path = RoundPath(box, Ce(10 * k)))
            using (var tint = new SolidBrush(Color.FromArgb(26, Cyan)))
                g.FillPath(tint, path);
            DrawChartIcon(g, x + boxPadX, y + (hBox - icon) / 2f, icon, k);
            Text(_gain, gainF, Ink, x + boxPadX + icon + iconGap, y + (hBox - hGain) / 2f, gainW, hGain);
        }
        y += hBox + Ce(20 * k);

        // ---- two panels: what we send / what we never send ----
        int gap = Ce(16 * k), pp = Ce(22 * k), mark = Ce(28 * k), itemGap = Ce(12 * k);
        int colW = (width - gap) / 2;
        int textW = colW - pp * 2 - mark;
        int hHead = Ce(headF.GetHeight(g));
        int ListHeight(string[] items)
        {
            int h = 0;
            foreach (var it in items) h += Ce(H(it, itemF, textW)) + itemGap;
            return h - itemGap;
        }
        int hPanel = pp + hHead + Ce(16 * k) + Math.Max(ListHeight(_sent), ListHeight(_never)) + pp;
        if (paint)
        {
            Panel(x, _sentH, _sent, Cyan, true);
            Panel(x + colW + gap, _neverH, _never, SoftRed, false);
        }
        y += hPanel + Ce(20 * k);

        void Panel(int px, string head, string[] items, Color accent, bool tick)
        {
            var r = new Rectangle(px, y, colW, hPanel);
            using (var path = RoundPath(r, Ce(10 * k)))
            using (var bg = new SolidBrush(FieldBg))
                g.FillPath(bg, path);
            Text(head, headF, accent, px + pp, y + pp, colW - pp * 2, hHead);
            float iy = y + pp + hHead + Ce(16 * k);
            float lineH = itemF.GetHeight(g);
            foreach (var it in items)
            {
                float h = H(it, itemF, textW);
                float m = 12 * k, mx = px + pp + 1 * k, my = iy + (lineH - m) / 2f;
                if (tick) DrawTick(g, mx, my, m, accent, k); else DrawCross(g, mx, my, m, accent, k);
                Text(it, itemF, Ink, px + pp + mark, iy, textW, h);
                iy += Ce(h) + itemGap;
            }
        }

        // ---- note ----
        float hNote = H(_note, noteF, width);
        Text(_note, noteF, Muted, x, y, width, hNote);
        y += Ce(hNote) + Ce(8 * k);   // a little more air above the buttons than the card's usual gap
        return y - y0;
    }

    private static void DrawTick(Graphics g, float x, float y, float s, Color c, float k)
    {
        using var pen = new Pen(c, Math.Max(1.5f, 2f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(x, y + s * 0.55f), new PointF(x + s * 0.38f, y + s * 0.92f), new PointF(x + s, y + s * 0.1f) });
    }

    private static void DrawCross(Graphics g, float x, float y, float s, Color c, float k)
    {
        using var pen = new Pen(c, Math.Max(1.5f, 2f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float i = s * 0.12f;
        g.DrawLine(pen, x + i, y + i, x + s - i, y + s - i);
        g.DrawLine(pen, x + s - i, y + i, x + i, y + s - i);
    }

    /// <summary>Three rising rounded bars: "results shown in the app".</summary>
    private static void DrawChartIcon(Graphics g, float x, float y, float s, float k)
    {
        float bw = s * 0.24f, gap = (s - bw * 3) / 2f;
        float[] hs = { 0.45f, 0.72f, 1f };
        using var b = new SolidBrush(Cyan);
        for (int i = 0; i < 3; i++)
        {
            float h = s * hs[i];
            var r = new RectangleF(x + i * (bw + gap), y + s - h, bw, h);
            using var p = new GraphicsPath();
            float rad = Math.Min(bw / 2f, 2.5f * k);
            p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            g.FillPath(b, p);
        }
    }
}

/// <summary>
/// "What will be sent": the report exactly as it would go out now, laid out to be read rather than
/// parsed. Every short field is a tile in a five-column grid (a plain caption over the value), the
/// lists (features, widgets, ratings, errors, profile time) are rows of small labels "name value"
/// with the value in cyan when it is set. Values are the wire values, untouched; only the captions
/// are words instead of field names (Lang key stats_fields, "field=caption|...").
/// </summary>
public sealed class StatsReportCard : GhostCardForm
{
    private readonly System.Text.Json.Nodes.JsonObject? _report;
    private readonly string? _error;
    private readonly string _json;                       // the wire text, one top-level field per line (Stats.Pretty)
    private readonly Dictionary<string, string> _captions = new();
    private int _tab;                                    // 0 = readable tiles, 1 = the JSON as it goes out
    private readonly Rectangle[] _tabRects = new Rectangle[2];   // window coordinates, for ContentHit
    private Point _origin;

    public StatsReportCard(System.Text.Json.Nodes.JsonObject? report, string? error = null)
        : base("//STATS", Lang.T("stats_report_title"), "", Lang.T("gen_ok"), "", () => { })
    {
        _report = report;
        _error = error;
        _json = report == null ? "" : Stats.Pretty(report);
        foreach (var pair in Lang.T("stats_fields").Split('|'))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0) _captions[pair[..eq]] = pair[(eq + 1)..];
        }
    }

    protected override int CardWidth => 860;
    protected override int SideInset => 12;

    private string Caption(string key) => _captions.TryGetValue(key, out var c) ? c : key;

    private static string ValueText(System.Text.Json.Nodes.JsonNode? n) => n switch
    {
        null => "-",
        System.Text.Json.Nodes.JsonValue v when v.TryGetValue<string>(out var s) => s,
        System.Text.Json.Nodes.JsonObject o when o.Count == 0 => "-",
        System.Text.Json.Nodes.JsonArray a when a.Count == 0 => "-",
        _ => n.ToJsonString(),
    };

    private static bool IsList(System.Text.Json.Nodes.JsonNode? n) =>
        n is System.Text.Json.Nodes.JsonObject { Count: > 0 } || n is System.Text.Json.Nodes.JsonArray { Count: > 0 };

    /// <summary>
    /// The labels of a list: (name, value, value is "set", a colour disc after the value, the text
    /// after the disc). A widget shows its size and, where the report names them (1.46), its colour
    /// and its dial in words: "1x1 · Colour: (disc) · Dial: Segmented". A palette colour is the
    /// disc itself, the default colour is the word; the raw ids stay on the JSON tab.
    /// </summary>
    private static List<(string Name, string Value, bool On, Color? Dot, string Tail)> Chips(System.Text.Json.Nodes.JsonNode n)
    {
        var list = new List<(string, string, bool, Color?, string)>();
        if (n is System.Text.Json.Nodes.JsonObject o)
            foreach (var kv in o)
            {
                string v = ValueText(kv.Value);
                list.Add((kv.Key, v, v != "0", null, ""));
            }
        else if (n is System.Text.Json.Nodes.JsonArray a)
            foreach (var it in a)
            {
                if (it is System.Text.Json.Nodes.JsonObject w && w["t"] != null)
                {
                    string v = ValueText(w["s"]), tail = "";
                    Color? dot = null;
                    if (w["c"] != null)
                    {
                        string c = ValueText(w["c"]);
                        v += " · " + Lang.T("stats_w_color") + ":";
                        if (c != "auto" && Array.IndexOf(DashPaint.PaletteIds, c) >= 0) dot = DashPaint.PaletteColor(c);
                        else v += " " + Lang.T("dash_opt_default");
                    }
                    if (w["d"] != null)
                    {
                        string d = ValueText(w["d"]);
                        string word = d == "arc" ? Lang.T("dash_opt_dial_arc") : d == "seg" ? Lang.T("dash_opt_dial_seg") : d;
                        string part = " · " + Lang.T("stats_w_dial") + ": " + word;
                        if (dot != null) tail = part; else v += part;
                    }
                    list.Add((ValueText(w["t"]), v, false, dot, tail));
                }
                else list.Add((ValueText(it), "", false, null, ""));
            }
        return list;
    }

    protected override int MeasureContent(Graphics g, float k, int width) => Flow(g, k, width, null);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _origin = origin;
        Flow(g, k, area.Width, area.Location);
        g.TextRenderingHint = hint;
    }

    protected override int ContentHit(Point p)
    {
        for (int i = 0; i < _tabRects.Length; i++) if (_tabRects[i].Contains(p)) return i;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        if (zone < 0 || zone > 1 || zone == _tab) return;
        _tab = zone;
        Rerender();
    }

    /// <summary>
    /// The JSON tab: the wire text in a monospace font, coloured by a small tokenizer of our own
    /// (no library: a line is a top-level "key: ", then strings, numbers, true/false/null and
    /// punctuation). Wrapped at token boundaries with a four-column indent, like the readable tab.
    /// </summary>
    private int JsonBlock(Graphics g, float k, int x, int y, int width, bool paint)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        using var f = Dpi.Font("Consolas", 14.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
        float charW = g.MeasureString("0000000000", f, PointF.Empty, fmt).Width / 10f;
        int lineH = Ce(f.GetHeight(g) * 1.3f);
        int pad = Ce(18 * k), indent = 4;
        int cols = Math.Max(20, (int)((width - pad * 2) / charW));
        var cells = new List<(string T, Color C, int Col, int Row)>();
        int row = 0;
        foreach (var line in _json.Split('\n'))
        {
            int col = 0;
            foreach (var (t, c) in JsonTokens(line))
            {
                if (col > 0 && col + t.Length > cols) { row++; col = indent; }
                cells.Add((t, c, col, row));
                col += t.Length;
            }
            row++;
        }
        int h = pad * 2 + row * lineH;
        if (paint)
        {
            using var path = RoundPath(new Rectangle(x, y, width, h), Ce(10 * k));
            using var bg = new SolidBrush(FieldBg);
            g.FillPath(bg, path);
            foreach (var c in cells)
            {
                using var b = new SolidBrush(c.C);
                g.DrawString(c.T, f, b, x + pad + c.Col * charW, y + pad + c.Row * lineH, fmt);
            }
        }
        return h;
    }

    private static readonly Color JsonKeyword = Color.FromArgb(0xB0, 0x96, 0xFF);   // true / false / null

    /// <summary>Splits one line of the wire text into coloured runs; punctuation keeps its trailing spaces so a wrap never starts with a space.</summary>
    private static IEnumerable<(string Text, Color Colour)> JsonTokens(string line)
    {
        int i = 0;
        int colon = line.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && line[0] != '"' && line[0] != '{' && line[0] != '[')
        {
            yield return (line[..colon], White);          // the top-level field name
            yield return (": ", Muted);
            i = colon + 2;
        }
        while (i < line.Length)
        {
            char ch = line[i];
            int j;
            if (ch == '"')
            {
                j = i + 1;
                while (j < line.Length && line[j] != '"') { if (line[j] == '\\') j++; j++; }
                j = Math.Min(j + 1, line.Length);
                bool isKey = j < line.Length && line[j] == ':';
                yield return (line[i..j], isKey ? Ink : Amber);
            }
            else if (char.IsDigit(ch) || (ch == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                j = i + 1;
                while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.')) j++;
                yield return (line[i..j], Cyan);
            }
            else if (char.IsLetter(ch))
            {
                j = i;
                while (j < line.Length && char.IsLetter(line[j])) j++;
                yield return (line[i..j], JsonKeyword);
            }
            else
            {
                j = i;
                while (j < line.Length && line[j] != '"' && !char.IsLetterOrDigit(line[j]) && line[j] != '-') j++;
                yield return (line[i..j], Muted);
            }
            i = j;
        }
    }

    private int Flow(Graphics g, float k, int width, Point? at)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        using var leadF = Dpi.Font("Segoe UI", 15.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var tabF = Dpi.Font("Segoe UI", 13f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var capF = Dpi.Font("Segoe UI", 12.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var valF = Dpi.Font("Segoe UI Semibold", 15.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var headF = Dpi.Font("Segoe UI", 12.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var chipF = Dpi.Font("Segoe UI", 14f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var chipB = Dpi.Font("Segoe UI Semibold", 14f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.Word };
        bool paint = at != null;
        int x = at?.X ?? 0, y0 = at?.Y ?? 0, y = y0;
        float H(string s, Font f, int w) => g.MeasureString(s, f, w, fmt).Height;
        float W(string s, Font f) => g.MeasureString(s, f, 10000, fmt).Width;
        void Text(string s, Font f, Color c, float tx, float ty, int w, float h)
        {
            if (!paint) return;
            using var b = new SolidBrush(c);
            g.DrawString(s, f, b, new RectangleF(tx, ty, w, h + 3 * k), fmt);
        }
        void PanelBg(int py, int ph)
        {
            if (!paint) return;
            using var path = RoundPath(new Rectangle(x, py, width, ph), Ce(10 * k));
            using var bg = new SolidBrush(FieldBg);
            g.FillPath(bg, path);
        }

        string lead = _error == null ? Lang.T("stats_report_body") : _error;
        float hLead = H(lead, leadF, width);
        Text(lead, leadF, Ink, x, y, width, hLead);
        y += Ce(hLead) + Ce(16 * k);
        if (_report == null) return y - y0;

        // ---- two tabs: readable | JSON (pills; the active one tinted cyan) ----
        {
            string[] labels = { Lang.T("stats_tab_readable"), "JSON" };
            int tabH = Ce(tabF.GetHeight(g)) + Ce(12 * k), tx = x;
            for (int i = 0; i < 2; i++)
            {
                int tw = Ce(W(labels[i], tabF)) + Ce(28 * k);
                var rc = new Rectangle(tx, y, tw, tabH);
                if (paint)
                {
                    bool on = i == _tab, hot = HotZone == i;
                    using var path = RoundPath(rc, Ce(8 * k));
                    using var fill = new SolidBrush(on ? Color.FromArgb(40, Cyan) : Color.FromArgb(hot ? 28 : 13, 255, 255, 255));
                    g.FillPath(fill, path);
                    if (on) { using var edge = new Pen(Color.FromArgb(140, Cyan), 1f); g.DrawPath(edge, path); }
                    float lw = W(labels[i], tabF);
                    Text(labels[i], tabF, on ? Cyan : hot ? White : Muted, rc.X + (tw - lw) / 2f, rc.Y + Ce(6 * k), tw, tabH);
                    _tabRects[i] = new Rectangle(rc.X + _origin.X, rc.Y + _origin.Y, rc.Width, rc.Height);
                }
                tx += tw + Ce(8 * k);
            }
            y += tabH + Ce(14 * k);
        }
        if (_tab == 1) { y += JsonBlock(g, k, x, y, width, paint); return y - y0 + Ce(6 * k); }

        // ---- the short fields: a five-column grid of caption over value ----
        var tiles = _report.Where(kv => !IsList(kv.Value)).ToList();
        var lists = _report.Where(kv => IsList(kv.Value)).ToList();
        int pp = Ce(18 * k), colGap = Ce(16 * k), rowGap = Ce(14 * k), cols = 5;
        int inner = width - pp * 2;
        int colW = (inner - colGap * (cols - 1)) / cols;
        int capGap = Ce(3 * k);
        var rowH = new List<int>();
        for (int i = 0; i < tiles.Count; i += cols)
        {
            int h = 0;
            for (int j = i; j < Math.Min(i + cols, tiles.Count); j++)
                h = Math.Max(h, Ce(H(Caption(tiles[j].Key), capF, colW)) + capGap + Ce(H(ValueText(tiles[j].Value), valF, colW)));
            rowH.Add(h);
        }
        int hGrid = pp * 2 + rowH.Sum() + rowGap * Math.Max(0, rowH.Count - 1);
        PanelBg(y, hGrid);
        if (paint)
        {
            int ty = y + pp;
            for (int r = 0; r < rowH.Count; r++)
            {
                for (int j = r * cols; j < Math.Min(r * cols + cols, tiles.Count); j++)
                {
                    int tx = x + pp + (j - r * cols) * (colW + colGap);
                    string cap = Caption(tiles[j].Key), val = ValueText(tiles[j].Value);
                    float hc = H(cap, capF, colW);
                    Text(cap, capF, Muted, tx, ty, colW, hc);
                    Text(val, valF, val == "-" ? Muted : White, tx, ty + Ce(hc) + capGap, colW, H(val, valF, colW));
                }
                ty += rowH[r] + rowGap;
            }
        }
        y += hGrid;

        // ---- the lists, all in one panel: a caption, then rows of small labels "name value" ----
        int chipPadX = Ce(10 * k), chipPadY = Ce(4 * k), chipGap = Ce(8 * k), nameGap = Ce(6 * k);
        int chipH = Ce(chipF.GetHeight(g)) + chipPadY * 2;
        int hHead = Ce(headF.GetHeight(g)), headGap = Ce(10 * k), listGap = Ce(18 * k);
        int dotD = Math.Max(Ce(6 * k), (int)(chipF.GetHeight(g) * 0.55f)), dotGap = Ce(6 * k);   // the colour disc of a widget (1.46)
        var laid = new List<(string Head, List<(string Name, string Value, bool On, Color? Dot, string Tail)> Chips, List<(int X, int Row, int W)> Place, int Rows)>();
        foreach (var kv in lists)
        {
            var chips = Chips(kv.Value!);
            var place = new List<(int X, int Row, int W)>();
            int cxp = 0, row = 0;
            foreach (var c in chips)
            {
                int w = chipPadX * 2 + Ce(W(c.Name, chipF)) + (c.Value.Length > 0 ? nameGap + Ce(W(c.Value, chipB)) : 0)
                    + (c.Dot != null ? dotGap + dotD : 0) + (c.Tail.Length > 0 ? Ce(W(c.Tail, chipB)) : 0);
                if (cxp > 0 && cxp + w > inner) { cxp = 0; row++; }
                place.Add((cxp, row, w));
                cxp += w + chipGap;
            }
            laid.Add((Lang.Upper(Caption(kv.Key)), chips, place, row + 1));
        }
        if (laid.Count > 0)
        {
            y += Ce(12 * k);
            int hLists = pp * 2 + laid.Sum(l => hHead + headGap + l.Rows * chipH + (l.Rows - 1) * chipGap) + listGap * (laid.Count - 1);
            PanelBg(y, hLists);
            if (paint)
            {
                using var chipBg = new SolidBrush(Color.FromArgb(18, 255, 255, 255));
                int ly = y + pp;
                foreach (var l in laid)
                {
                    Text(l.Head, headF, Cyan, x + pp, ly, inner, hHead);
                    int top = ly + hHead + headGap;
                    for (int i = 0; i < l.Chips.Count; i++)
                    {
                        var (px, pr, pw) = l.Place[i];
                        var rc = new Rectangle(x + pp + px, top + pr * (chipH + chipGap), pw, chipH);
                        using (var path = RoundPath(rc, Ce(6 * k))) g.FillPath(chipBg, path);
                        float ty = rc.Y + chipPadY;
                        Text(l.Chips[i].Name, chipF, Ink, rc.X + chipPadX, ty, pw, chipH);
                        if (l.Chips[i].Value.Length > 0)
                        {
                            float vx = rc.X + chipPadX + W(l.Chips[i].Name, chipF) + nameGap;
                            Text(l.Chips[i].Value, chipB, l.Chips[i].On ? Cyan : Muted, vx, ty, pw, chipH);
                            vx += W(l.Chips[i].Value, chipB);
                            if (l.Chips[i].Dot is { } dot)
                            {
                                var sm = g.SmoothingMode;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                                using (var db = new SolidBrush(dot)) g.FillEllipse(db, vx + dotGap, rc.Y + (chipH - dotD) / 2f, dotD, dotD);
                                g.SmoothingMode = sm;
                                vx += dotGap + dotD;
                            }
                            if (l.Chips[i].Tail.Length > 0) Text(l.Chips[i].Tail, chipB, Muted, vx, ty, pw, chipH);
                        }
                    }
                    ly = top + l.Rows * chipH + (l.Rows - 1) * chipGap + listGap;
                }
            }
            y += hLists;
        }
        return y - y0 + Ce(6 * k);
    }
}
