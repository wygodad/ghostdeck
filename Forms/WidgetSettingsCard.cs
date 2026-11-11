using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace GhostDeck;

/// <summary>
/// The settings of one widget on the Dashboard (1.45), as a GhostDeck card. The card is drawn
/// from the list of settings the widget declares (<see cref="DashOptionDef"/>), so a widget type
/// needs no card of its own. The picture is the widget itself, painted by the same code as on
/// the Dashboard with the settings being edited, so every change shows at once; the Dashboard
/// gets them on Save, in one repaint. Colours, one-of-a-few settings and "which to show" lists
/// sit beside the picture (under it for a widget with none of them, or a wide one), texts,
/// numbers and switches in two columns under it. The picture has the size the widget will take
/// with the settings being edited: fewer profiles to show can mean a smaller widget. A colour that belongs to a field (the
/// label's colour, the colour of a warning) is a small colour button at the end of that field;
/// it opens the same palette in a drop-down (<see cref="CardPopupPalette"/>).
///
/// A value equal to its default is not stored, and an empty text or number field means the
/// default, shown in grey. A setting switched off by another one is dimmed and keeps its value.
/// Enter saves, Esc cancels, Tab moves between the text fields; "Defaults" puts every setting of
/// the card back. Two buttons under the settings write them to a file as the widget's style
/// and load one (<see cref="DashFile"/>); a loaded style lands in the card, to be looked at and
/// saved or cancelled like any other change.
///
/// The card is a layered window: everything is painted and hit-tested here, and the texts are
/// typed into real text boxes riding over the painted fields (<see cref="CardTextHost"/>). The
/// card paints the field texts too, so a picture of the card without its text boxes (the
/// preview tool) is complete.
/// </summary>
internal sealed class WidgetSettingsCard : GhostCardForm
{
    private enum ZoneKind { Swatch, Cell, Select, Toggle, Field, Chip, Check, Link }
    private readonly record struct Zone(Rectangle R, ZoneKind Kind, int Def, int Idx);

    private static readonly Color Dim = Color.FromArgb(0x5E, 0x66, 0x75);   // a switched-off field's texts

    private readonly DashWidgetDef _def;
    private readonly DashItem _item;
    private readonly IReadOnlyList<DashOptionDef> _defs;
    private readonly DashContext _ctx;
    private readonly Action<DashOptions> _save;
    private DashOptions _work;                                            // the settings being edited
    private string _status = "";                                          // what exporting or loading a style did
    private bool _statusBad;
    private readonly List<Zone> _zones = new();
    private readonly Dictionary<string, string> _text = new();            // what every text and number field holds now
    private readonly Dictionary<string, CardTextHost> _hosts = new();
    private readonly Dictionary<string, Rectangle> _rects = new();        // the fields' text areas, window coordinates
    private readonly HashSet<string> _badText = new();                    // number fields whose text is not a number in range
    private Bitmap? _preview;
    private bool _previewStale = true;
    private IDashWidget? _widget;                                         // the widget with the settings being edited
    private bool _loading;                                                // the card is filling the boxes itself
    private bool _tight;                                                  // the screen is too low for the roomy layout

    public WidgetSettingsCard(DashWidgetDef def, DashItem item, IReadOnlyList<DashOptionDef> defs, DashContext ctx, Action<DashOptions> onSave)
        : base("//WIDGET", Lang.T(def.NameKey), Lang.T("dash_set_hint"), Lang.T("set_save"), Lang.T("gen_cancel"), () => { })
    {
        _def = def; _item = item; _defs = defs; _ctx = ctx; _save = onSave;
        _work = DashOptions.Of(item);
        foreach (var o in defs)
            if (o.Kind is DashOptKind.Text or DashOptKind.Number) _text[o.Key] = TextOf(o);
    }

    /// <summary>What a field shows for the setting's current value; nothing for the default (the grey text says what that is).</summary>
    private string TextOf(DashOptionDef o)
    {
        if (o.Kind == DashOptKind.Text) return _work.Text(o.Key);
        if (!_work.Has(o.Key)) return "";
        int v = o.NumberIn(_work);
        return v == o.DefaultInt ? "" : v.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The grey text of an empty field: what the widget uses then.</summary>
    private static string GreyOf(DashOptionDef o) => o.Kind == DashOptKind.Number ? o.DefaultInt.ToString(CultureInfo.InvariantCulture) : o.Placeholder;

    private bool Live(DashOptionDef o) => o.EnabledWhen?.Invoke(_work) ?? true;

    private bool Bad(DashOptionDef o) => Live(o) && (_badText.Contains(o.Key) || (o.Rule != null && !o.Rule(_work)));

    private Color AutoOf(DashOptionDef o) => o.Auto?.Invoke(_work) ?? Cyan;

    /// <summary>The key a field's colour button edits: the field's own for a colour on its own, else the colour that goes with the field.</summary>
    private static string ColorKeyOf(DashOptionDef o) => o.Kind == DashOptKind.Tint ? o.Key : o.ColorKey;

    // ---------------- card hooks ----------------

    protected override int CardWidth => 640;
    protected override int SideInset => 8;
    // the label is one setting among many: the card opens with nothing being typed into
    protected override bool FocusFirstFieldOnOpen => false;
    protected override string AuxLabel => Lang.T("set_default");
    protected override Color AuxColor => Ink;

    /// <summary>"Defaults": every setting of this card back to its default; keys the card does not show stay.</summary>
    protected override void AuxClick()
    {
        foreach (var o in _defs)
        {
            _work.Remove(o.Key);
            if (o.ColorKey.Length > 0) _work.Remove(o.ColorKey);
        }
        ReloadTexts();
        _status = "";
        Changed();
    }

    protected override bool Acknowledge()
    {
        foreach (var o in _defs)
        {
            if (!Bad(o)) continue;
            // a field that cannot be saved keeps the card open, with the caret in it
            Rerender();
            if (_hosts.TryGetValue(o.Key, out var h)) FocusField(h, selectAll: true);
            return false;
        }
        _save(_work);
        DialogResult = DialogResult.OK;
        return true;
    }

    protected override void CreateTextFields()
    {
        foreach (var o in _defs)
        {
            if (o.Kind is not (DashOptKind.Text or DashOptKind.Number)) continue;
            var opt = o;
            var h = AddTextField(_text[o.Key], HorizontalAlignment.Left, () => _rects.TryGetValue(opt.Key, out var r) ? r : Rectangle.Empty);
            h.Box.MaxLength = o.Kind == DashOptKind.Number ? 4 : DashOptions.MaxText;
            h.SetCue(GreyOf(o));
            h.Box.TextChanged += (_, _) => { if (!_loading) Typed(opt, h.Box.Text); };
            _hosts[o.Key] = h;
        }
        SyncBoxes();
    }

    /// <summary>A switched-off field's box cannot be typed into and shows its text dimmed, like the field under it.</summary>
    private void SyncBoxes()
    {
        foreach (var o in _defs)
        {
            if (!_hosts.TryGetValue(o.Key, out var h) || h.IsDisposed) continue;
            bool on = Live(o);
            if (h.Box.ReadOnly != on) continue;
            h.Box.ReadOnly = !on;
            h.Box.ForeColor = on ? White : Dim;
        }
    }

    /// <summary>A setting changed: the picture, the boxes that depend on it, and the card.</summary>
    private void Changed()
    {
        _previewStale = true;
        _widget = null;
        SyncBoxes();
        Rerender();
    }

    /// <summary>A field's text changed: the setting follows when the text is valid, the field is marked when it is not.</summary>
    private void Typed(DashOptionDef o, string text)
    {
        _text[o.Key] = text;
        if (o.Kind == DashOptKind.Text)
        {
            string t = DashOptions.Clean(text, DashOptions.MaxText);
            if (t.Length == 0) _work.Remove(o.Key); else _work.Set(o.Key, t);
        }
        else
        {
            // empty = the default
            string t = text.Trim();
            int? v = t.Length == 0 ? o.DefaultInt
                   : int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= o.Min && n <= o.Max ? n : null;
            if (v is { } val)
            {
                _badText.Remove(o.Key);
                if (val == o.DefaultInt) _work.Remove(o.Key); else _work.Set(o.Key, val);
            }
            else _badText.Add(o.Key);
        }
        _previewStale = true;
        _widget = null;
        Rerender();
    }

    // ---------------- the picture ----------------

    /// <summary>The widget with the settings being edited; a new one after every change (an instance keeps its settings for life).</summary>
    private IDashWidget Widget() => _widget ??= _def.New(_work.Clone());

    /// <summary>The cells the widget will take once saved: its size now when the settings still allow it, else the nearest allowed.</summary>
    private (int w, int h) Cells() => DashItem.Fit(Widget().Sizes, _item.W, _item.H);

    /// <summary>The widget painted with the settings being edited and the current data sample, at the size the card gives it.</summary>
    private Bitmap Preview(Size size)
    {
        int w = size.Width, h = size.Height;
        if (!_previewStale && _preview != null && _preview.Width == w && _preview.Height == h) return _preview;
        _preview?.Dispose();
        var widget = Widget();
        _previewStale = false;
        return _preview = DashPaint.Snapshot(_ctx.Owner, w, h, FieldBg, (g, r) => { widget.PaintStatic(g, r, _ctx); widget.PaintLive(g, r, _ctx); });
    }

    // ---------------- layout and painting ----------------

    /// <summary>
    /// A card has no scroll bar, so on a screen too low for it (a small panel at a high scale) the
    /// same fields are packed tighter: smaller picture, lower fields, less air between the rows.
    /// </summary>
    protected override int MeasureContent(Graphics g, float k, int width)
    {
        _tight = false;
        int h = Flow(g, k, width, null);
        if (h <= ContentRoom) return h;
        _tight = true;
        return Flow(g, k, width, null);
    }

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Flow(g, k, area.Width, (area.Location, origin));
        g.TextRenderingHint = hint;
    }

    private static bool Wide(DashOptionDef o) => o.Kind is DashOptKind.Color or DashOptKind.Choice or DashOptKind.Checks;

    /// <summary>Whether a box of a "which to show" list is ticked: nothing stored = all are.</summary>
    private bool Ticked(DashOptionDef o, int j) =>
        _work.List(o.Key) is not { } sel || sel.Contains(DashOptions.Clean(o.Items[j].Id, DashOptions.MaxText));

    /// <summary>One pass for both measuring and painting, so the two can never disagree. Returns the height used.</summary>
    private int Flow(Graphics g, float k, int width, (Point at, Point origin)? paintAt)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        bool paint = paintAt != null;
        int x = paintAt?.at.X ?? 0, y0 = paintAt?.at.Y ?? 0;
        var org = paintAt?.origin ?? Point.Empty;
        Rectangle Win(Rectangle r) => new(r.X + org.X, r.Y + org.Y, r.Width, r.Height);
        if (paint) { _zones.Clear(); _rects.Clear(); }

        using var hintF = Dpi.Font("Segoe UI", 12f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fieldF = Dpi.Font("Segoe UI", 14f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.Word };
        using var whiteB = new SolidBrush(White);
        using var mutedB = new SolidBrush(Muted);
        using var dimB = new SolidBrush(Dim);
        using var redB = new SolidBrush(SoftRed);
        bool tight = _tight;
        int labelH = Ce(LabelFont.GetHeight(g)), gapLabel = Ce((tight ? 4 : 8) * k), fieldH = Ce((tight ? 30 : 36) * k),
            gapField = Ce((tight ? 8 : 16) * k), hintGap = Ce((tight ? 4 : 6) * k);
        string defText = Lang.T("dash_opt_default");

        int HintH(string s, int w) => s.Length == 0 ? 0 : hintGap + Ce(g.MeasureString(s, hintF, w, fmt).Height);
        void Hint(string s, Brush b, int hx, int hy, int w)
        {
            if (paint && s.Length > 0) g.DrawString(s, hintF, b, new RectangleF(hx, hy + hintGap, w, 4000), fmt);
        }
        // a label longer than its column wraps instead of running into the neighbour
        int LabelH(string s, int w) => Math.Max(labelH, Ce(g.MeasureString(s, LabelFont, w, fmt).Height));
        void PutLabel(string s, int lx, int ly, int w, int h, bool on = true)
        {
            if (paint) g.DrawString(s, LabelFont, on ? whiteB : dimB, new RectangleF(lx, ly, w, h + 2 * k), fmt);
        }

        // the style as a file: under a line that sets them apart from the settings, two buttons in
        // the look of the Layouts card's, and, after one was used, what it did
        int Links(int top)
        {
            int btnH = Ce((tight ? 28 : 32) * k), padX = Ce(14 * k), bgap = Ce(8 * k);
            int ly = top + Ce((tight ? 8 : 18) * k);
            if (paint)
            {
                using var line = new Pen(Color.FromArgb(30, 243, 247, 255), 1f);
                g.DrawLine(line, x, ly, x + width, ly);
            }
            ly += Ce((tight ? 8 : 14) * k);
            int bx = x, by = ly;
            foreach (var (text, idx) in new[] { (Lang.T("dash_style_export"), 0), (Lang.T("dash_style_import"), 1) })
            {
                int w = Ce(g.MeasureString(text, CellFont).Width) + padX * 2;
                if (bx > x && bx + w > x + width) { bx = x; by += btnH + bgap; }   // a long language: the second button goes under the first, never over the edge
                var br = new Rectangle(bx, by, w, btnH);
                if (paint)
                {
                    bool hot = HotZone == _zones.Count;
                    using (var path = RoundPath(br, Ce(8 * k)))
                    {
                        using var fill = new SolidBrush(Color.FromArgb(hot ? 34 : 13, 255, 255, 255));
                        g.FillPath(fill, path);
                        using var edge = new Pen(Color.FromArgb(hot ? 90 : 40, 243, 247, 255), 1f);
                        g.DrawPath(edge, path);
                    }
                    using var tb = new SolidBrush(hot ? White : Ink);
                    g.DrawString(text, CellFont, tb, br, CenterText);
                    _zones.Add(new Zone(Win(br), ZoneKind.Link, 0, idx));
                }
                bx += w + bgap;
            }
            int bottom = by + btnH;
            if (_status.Length > 0)
            {
                int sh = Ce(g.MeasureString(_status, hintF, width, fmt).Height);
                if (paint)
                {
                    using var sb = new SolidBrush(_statusBad ? SoftRed : Ink);
                    g.DrawString(_status, hintF, sb, new RectangleF(x, bottom + Ce(8 * k), width, sh + 2 * k), fmt);
                }
                bottom += Ce(8 * k) + sh;
            }
            return bottom - top;
        }

        // ---- the picture: beside the first settings when the widget is narrow and has such settings, across the card otherwise ----
        var (cellsW, cellsH) = Cells();
        bool side = cellsW <= 2 && _defs.Any(Wide);
        int pad = Ce((tight ? 10 : 14) * k), cellGap = _ctx.S(16);
        int cell = side ? Ce((cellsW == 1 && cellsH == 1 ? (tight ? 136 : 150) : (tight ? 92 : 104)) * k)
                        : Math.Min(Ce((tight ? (cellsH >= 2 ? 64 : 100) : 120) * k), (width - pad * 2 - cellGap * (cellsW - 1)) / Math.Max(1, cellsW));   // a two-row widget across the card is the tallest thing on it
        var pv = new Size(cellsW * cell + (cellsW - 1) * cellGap, cellsH * cell + (cellsH - 1) * cellGap);
        var field = side ? new Rectangle(x, y0, pv.Width + pad * 2, pv.Height + pad * 2) : new Rectangle(x, y0, width, pv.Height + pad * 2);
        if (paint)
        {
            using var path = RoundPath(field, Ce(10 * k));
            using var bg = new SolidBrush(FieldBg);
            g.FillPath(bg, path);
            // The widget is painted only here: measuring needs its size, not its picture. It is
            // painted at the size it has on the Dashboard and scaled into its place, so its
            // texts break as they do there.
            int real = Math.Max(Ce(150 * k), _ctx.CellPx());
            var shot = Preview(new Size(cellsW * real + (cellsW - 1) * cellGap, cellsH * real + (cellsH - 1) * cellGap));
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(shot, new Rectangle(field.X + (field.Width - pv.Width) / 2, field.Y + pad, pv.Width, pv.Height));
            g.InterpolationMode = old;
        }

        // ---- colour and one-of-a-few ----
        int cx = side ? field.Right + Ce(22 * k) : x, cw = side ? x + width - cx : width;
        int cy = side ? y0 : field.Bottom + Ce(18 * k);
        bool anyWide = false;
        for (int i = 0; i < _defs.Count; i++)
        {
            var o = _defs[i];
            if (!Wide(o)) continue;
            anyWide = true;
            int wl = LabelH(o.Label, cw);
            PutLabel(o.Label, cx, cy, cw, wl);
            cy += wl + gapLabel;
            if (o.Kind == DashOptKind.Color)
            {
                var plan = CardPalette.Layout(g, LabelFont, cx, cy, cw, k, defText);
                if (paint)
                {
                    int first = _zones.Count, hot = HotZone - first;
                    CardPalette.Paint(g, LabelFont, plan, k, o.AutoPie.Count > 1 ? o.AutoPie : new[] { AutoOf(o) }, defText, _work.Choice(o.Key, o.Default, DashPaint.PaletteChoices),
                        hot >= 0 && hot < plan.Items.Length ? hot : -1);
                    for (int j = 0; j < plan.Items.Length; j++) _zones.Add(new Zone(Win(plan.Items[j]), ZoneKind.Swatch, i, j));
                }
                cy += plan.Height;
            }
            else if (o.Kind == DashOptKind.Checks)
            {
                // a box to tick for every element, as many on a line as fit
                int chipH = Ce(32 * k), chipGap2 = Ce(8 * k), box = Ce(16 * k), padX = Ce(10 * k);
                int px = cx, py = cy;
                for (int j = 0; j < o.Items.Count; j++)
                {
                    // measured the way it is drawn, so a name is cut only when it is wider than the whole column
                    int tw = Ce(g.MeasureString(o.Items[j].Caption, CellFont, 100000, LeftText).Width);
                    int wChip = Math.Min(cw, padX + box + Ce(8 * k) + tw + padX);
                    if (px > cx && px + wChip > cx + cw) { px = cx; py += chipH + chipGap2; }
                    var rc = new Rectangle(px, py, wChip, chipH);
                    if (paint)
                    {
                        PaintCheck(g, rc, o.Items[j].Caption, Ticked(o, j), HotZone == _zones.Count, k, box, padX);
                        _zones.Add(new Zone(Win(rc), ZoneKind.Check, i, j));
                    }
                    px += wChip + chipGap2;
                }
                cy = py + chipH;
            }
            else
            {
                int n = o.Items.Count, inset = Ce(3 * k);
                var track = new Rectangle(cx, cy, Math.Min(cw, Ce(360 * k)), fieldH);
                int cellW = (track.Width - inset * 2) / Math.Max(1, n);
                string cur = _work.Choice(o.Key, o.Default, o.Items.Select(t => t.Id).ToArray());
                int sel = Math.Max(0, IndexOf(o, cur));
                bool segments = n is >= 2 and <= 4 && o.Items.All(t => g.MeasureString(t.Caption, CellFont).Width + 8 * k <= cellW);
                if (paint && segments)
                {
                    PaintTrack(g, track, true);
                    for (int j = 0; j < n; j++)
                    {
                        var rc = new Rectangle(track.X + inset + j * cellW, track.Y + inset, cellW, fieldH - inset * 2);
                        PaintCell(g, rc, o.Items[j].Caption, sel == j, true, HotZone == _zones.Count);
                        _zones.Add(new Zone(Win(rc), ZoneKind.Cell, i, j));
                    }
                }
                else if (paint)
                {
                    PaintSelect(g, track, n > 0 ? o.Items[sel].Caption : "", true, HotZone == _zones.Count);
                    _zones.Add(new Zone(Win(track), ZoneKind.Select, i, 0));
                }
                cy += fieldH;
            }
            Hint(o.Hint, mutedB, cx, cy, cw);
            cy += HintH(o.Hint, cw) + gapField;
        }
        int wideBottom = anyWide ? cy - gapField : (side ? y0 : field.Bottom);
        int y = Math.Max(field.Bottom, wideBottom);

        // ---- texts, numbers and switches: two columns, every cell a label over its control ----
        var narrow = new List<int>();
        for (int i = 0; i < _defs.Count; i++) if (!Wide(_defs[i])) narrow.Add(i);
        if (narrow.Count == 0) return y + Links(y) - y0;
        y += Ce((tight ? 8 : 20) * k);
        int colGap = Ce(20 * k), half = (width - colGap) / 2;
        int chipW = Ce(68 * k), chipGap = Ce(8 * k), tgW = Ce(40 * k), tgH = Ce(22 * k);

        // the lines: fields that name a row stand together on it (up to three), the others pair up
        var rows = new List<List<int>>();
        foreach (int i in narrow)
        {
            string row = _defs[i].Row;
            var last = rows.Count > 0 ? rows[^1] : null;
            bool joins = last != null && _defs[last[0]].Row == row && last.Count < (row.Length > 0 ? 3 : 2);
            if (joins) last!.Add(i); else rows.Add(new List<int> { i });
        }
        // a line of one or two is cut in halves; in a line of three the switch takes less than a field
        int[] Widths(List<int> row)
        {
            if (row.Count < 3) return row.Select(_ => half).ToArray();
            float[] share = row.Select(i => _defs[i].Kind == DashOptKind.Toggle ? 0.8f : 1f).ToArray();
            float all = width - colGap * (row.Count - 1), sum = share.Sum();
            return share.Select(s => (int)(all * s / sum)).ToArray();
        }
        string HintOf(DashOptionDef o) =>
            o.Kind == DashOptKind.Number && Bad(o) ? (o.Min.ToString(CultureInfo.InvariantCulture) + "-" + o.Max.ToString(CultureInfo.InvariantCulture) + " " + o.Unit).Trim() : o.Hint;

        // the colour button of a field: a dot of the colour in use, the palette on a click
        void Chip(Rectangle r, int i, bool on)
        {
            var o = _defs[i];
            var c = DashPaint.Pick(_work.Choice(ColorKeyOf(o), "auto", DashPaint.PaletteChoices), AutoOf(o));
            PaintSelect(g, r, "", on, on && HotZone == _zones.Count, false, (gg, box, _) => CardPalette.Disc(gg, box, on ? c : Color.FromArgb(90, c)));
            if (on) _zones.Add(new Zone(Win(r), ZoneKind.Chip, i, 0));
        }

        foreach (var row in rows)
        {
            // one label height for the line, so the controls of all its cells stand level
            var widths = Widths(row);
            int rowLabel = labelH, rowHint = 0;
            for (int c = 0; c < row.Count; c++)
            {
                var o = _defs[row[c]];
                rowLabel = Math.Max(rowLabel, LabelH(o.Label, widths[c]));
                rowHint = Math.Max(rowHint, HintH(HintOf(o), widths[c]));
            }
            int rowY = y + rowLabel + gapLabel, fx = x - colGap;
            for (int c = 0; c < row.Count; c++)
            {
                int i = row[c], colW = widths[c];
                var o = _defs[i];
                fx += (c > 0 ? widths[c - 1] : 0) + colGap;
                bool on = Live(o), chip = o.ColorKey.Length > 0;
                PutLabel(o.Label, fx, y, colW, rowLabel, on);
                if (o.Kind == DashOptKind.Tint)
                {
                    if (paint) Chip(new Rectangle(fx, rowY, chipW, fieldH), i, on);
                    continue;
                }
                if (o.Kind == DashOptKind.Toggle)
                {
                    bool set = _work.Flag(o.Key, o.DefaultOn);
                    var sw = new Rectangle(fx, rowY + (fieldH - tgH) / 2, tgW, tgH);
                    if (paint)
                    {
                        PaintSwitch(g, sw, set);
                        // the switch with some air around it, and its label
                        _zones.Add(new Zone(Win(new Rectangle(fx, rowY, tgW + Ce(10 * k), fieldH)), ZoneKind.Toggle, i, 0));
                        _zones.Add(new Zone(Win(new Rectangle(fx, y, colW, rowLabel)), ZoneKind.Toggle, i, 0));
                        // the colour of what the switch shows, only while it shows it
                        if (chip && set) Chip(new Rectangle(sw.Right + Ce(16 * k), rowY, chipW, fieldH), i, on);
                    }
                    Hint(o.Hint, mutedB, fx, rowY + fieldH, colW);
                    continue;
                }
                var fr = new Rectangle(fx, rowY, colW - (chip ? chipW + chipGap : 0), fieldH);
                bool bad = Bad(o);
                if (paint)
                {
                    _hosts.TryGetValue(o.Key, out var host);
                    var inner = PaintField(g, fr, on && IsFocused(host), bad);
                    int unitW = o.Unit.Length > 0 ? Ce(g.MeasureString(o.Unit, LabelFont).Width) + Ce(8 * k) : 0;
                    if (unitW > 0) g.DrawString(o.Unit, LabelFont, on ? mutedB : dimB, new RectangleF(inner.Right - unitW, fr.Y, unitW, fr.Height), CenterText);
                    var textArea = new Rectangle(inner.X, inner.Y, inner.Width - unitW, inner.Height);
                    _rects[o.Key] = Win(textArea);
                    // the text itself, for a picture of the card without its text boxes; the box covers it otherwise
                    string t = _text[o.Key];
                    g.DrawString(t.Length > 0 ? t : GreyOf(o), fieldF, !on ? dimB : t.Length > 0 ? whiteB : mutedB,
                        new RectangleF(textArea.X, textArea.Y, textArea.Width, textArea.Height), LeftText);
                    if (on) _zones.Add(new Zone(Win(fr), ZoneKind.Field, i, 0));
                    if (chip) Chip(new Rectangle(fr.Right + chipGap, rowY, chipW, fieldH), i, on);
                }
                Hint(HintOf(o), bad && o.Kind == DashOptKind.Number ? redB : mutedB, fx, fr.Bottom, colW);
            }
            y = rowY + fieldH + rowHint + gapField;
        }
        y -= gapField;
        return y + Links(y) - y0;
    }

    /// <summary>One element of a "which to show" list: a tick box and its name in a rounded cell.</summary>
    private void PaintCheck(Graphics g, Rectangle rc, string text, bool on, bool hot, float k, int box, int padX)
    {
        PaintTrack(g, rc, on, hot);
        var br = new RectangleF(rc.X + padX, rc.Y + (rc.Height - box) / 2f, box, box);
        using (var bp = RoundPath(br, (int)Math.Ceiling(4 * k)))
        {
            if (on) { using var fb = new SolidBrush(Fill); g.FillPath(fb, bp); }
            else { using var ep = new Pen(Color.FromArgb(hot ? 200 : 130, Muted), Math.Max(1f, 1.2f * k)); g.DrawPath(ep, bp); }
        }
        if (on)
        {
            using var tick = new Pen(Color.White, Math.Max(1.5f, 1.8f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(tick, new[]
            {
                new PointF(br.X + br.Width * 0.24f, br.Y + br.Height * 0.52f),
                new PointF(br.X + br.Width * 0.43f, br.Y + br.Height * 0.70f),
                new PointF(br.X + br.Width * 0.77f, br.Y + br.Height * 0.32f),
            });
        }
        float tx = br.Right + 8 * k;
        using var tb = new SolidBrush(on || hot ? White : Muted);
        g.DrawString(text, CellFont, tb, new RectangleF(tx, rc.Y, Math.Max(1, rc.Right - padX / 2f - tx), rc.Height), LeftText);
    }

    private static int IndexOf(DashOptionDef o, string id)
    {
        for (int i = 0; i < o.Items.Count; i++) if (o.Items[i].Id == id) return i;
        return -1;
    }

    // ---------------- mouse ----------------

    protected override int ContentHit(Point p)
    {
        for (int i = _zones.Count - 1; i >= 0; i--) if (_zones[i].R.Contains(p)) return i;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        if (zone < 0 || zone >= _zones.Count) return;
        var z = _zones[zone];
        var o = _defs[z.Def];
        switch (z.Kind)
        {
            case ZoneKind.Swatch:
                Pick(o.Key, DashPaint.PaletteChoices[z.Idx], o.Default);
                break;
            case ZoneKind.Cell:
                Pick(o.Key, o.Items[z.Idx].Id, o.Default);
                break;
            case ZoneKind.Select:
            {
                var ids = o.Items.Select(t => t.Id).ToArray();
                int sel = Math.Max(0, IndexOf(o, _work.Choice(o.Key, o.Default, ids)));
                var items = o.Items.Select(t => new CardPopupList.Item(t.Caption)).ToArray();
                CardPopupList.Open(this, new Rectangle(Left + z.R.X, Top + z.R.Y, z.R.Width, z.R.Height), items, sel, UiScale, o, i => Pick(o.Key, o.Items[i].Id, o.Default));
                break;
            }
            case ZoneKind.Chip:
                CardPopupPalette.Open(this, new Rectangle(Left + z.R.X, Top + z.R.Y, z.R.Width, z.R.Height), AutoOf(o), Lang.T("dash_opt_default"),
                    _work.Choice(ColorKeyOf(o), "auto", DashPaint.PaletteChoices), UiScale, o, id => Pick(ColorKeyOf(o), id, "auto"));
                break;
            case ZoneKind.Toggle:
            {
                bool on = !_work.Flag(o.Key, o.DefaultOn);
                // the last switch of its group that is on stays on
                if (!on && o.KeepOne.Length > 0 && !_defs.Any(d => d != o && d.Kind == DashOptKind.Toggle && d.KeepOne == o.KeepOne && _work.Flag(d.Key, d.DefaultOn))) break;
                if (on == o.DefaultOn) _work.Remove(o.Key); else _work.Set(o.Key, on);
                Changed();
                break;
            }
            case ZoneKind.Check:
            {
                // the ticked ids after this click, in the list's order; at least one stays ticked,
                // and "all of them" is the default, which is not stored
                var ticked = new List<string>();
                for (int j = 0; j < o.Items.Count; j++)
                    if (Ticked(o, j) != (j == z.Idx)) ticked.Add(o.Items[j].Id);
                if (ticked.Count == 0) break;
                if (ticked.Count == o.Items.Count) _work.Remove(o.Key); else _work.Set(o.Key, ticked);
                Changed();
                break;
            }
            case ZoneKind.Field:
                if (_hosts.TryGetValue(o.Key, out var h)) FocusField(h);
                break;
            case ZoneKind.Link:
                if (z.Idx == 0) ExportStyle(); else ImportStyle();
                break;
        }
    }

    // ---------------- the style as a file ----------------

    private void Say(string text, bool bad)
    {
        _status = text; _statusBad = bad;
        Changed();
    }

    /// <summary>The settings being edited, as a widget style file.</summary>
    private void ExportStyle()
    {
        using var dlg = new SaveFileDialog { Filter = DashFileText.Filter, OverwritePrompt = true, FileName = "ghostdeck-" + DashFile.SafeFileName(Lang.T(_def.NameKey), "widget") + ".json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var (w, h) = Cells();
        var err = DashFile.Save(dlg.FileName, DashFile.Widget, new[] { new DashItem(_item.Type, w, h) { Opt = _work.ToMap() } }, 0);
        Say(err == DashFile.Error.None ? Lang.T("dash_lay_ok_exported") : DashFileText.Of(err, wantLayout: false), err != DashFile.Error.None);
    }

    /// <summary>
    /// A style from a file into the card. The file is checked whole first, it must be a style
    /// (not a layout) and of this widget's type; then its settings take the place of the ones
    /// being edited. Nothing reaches the Dashboard until Save, and Cancel drops it.
    /// </summary>
    private void ImportStyle()
    {
        using var dlg = new OpenFileDialog { Filter = DashFileText.Filter, CheckFileExists = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var c = DashFile.Read(dlg.FileName, out var err);
        if (c == null) { Say(DashFileText.Of(err, wantLayout: false), true); return; }
        if (c.Kind != DashFile.Widget) { Say(DashFileText.Of(DashFile.Error.WrongKind, wantLayout: false), true); return; }
        if (c.Items[0].Type != _item.Type)
        {
            string other = DashRegistry.Find(c.Items[0].Type) is { } d ? Lang.T(d.NameKey) : c.Items[0].Type;
            Say(string.Format(Lang.T("dash_file_err_type_fmt"), other), true);
            return;
        }
        _work = DashOptions.Of(c.Items[0]);
        ReloadTexts();
        Say(Lang.T("dash_style_loaded"), false);
    }

    /// <summary>The text boxes follow the settings (after Defaults, after a loaded style).</summary>
    private void ReloadTexts()
    {
        _badText.Clear();
        _loading = true;
        foreach (var o in _defs)
        {
            if (!_text.ContainsKey(o.Key)) continue;
            _text[o.Key] = TextOf(o);
            if (_hosts.TryGetValue(o.Key, out var h)) h.Box.Text = _text[o.Key];
        }
        _loading = false;
    }

    private void Pick(string key, string id, string def)
    {
        if (id == def) _work.Remove(key); else _work.Set(key, id);
        Changed();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview?.Dispose();
        base.Dispose(disposing);
    }
}
