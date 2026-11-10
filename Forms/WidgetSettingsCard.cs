using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace GhostDeck;

/// <summary>
/// The settings of one widget on the Dashboard (1.45), as a GhostDeck card. The card is drawn
/// from the list of settings the widget declares (<see cref="DashOptionDef"/>), so a widget type
/// needs no card of its own. The picture is the widget itself, painted by the same code as on
/// the Dashboard with the settings being edited, so every change shows at once; the Dashboard
/// gets them on Save, in one repaint. Colour and one-of-a-few settings sit beside the picture,
/// texts, numbers and switches in two columns under it. A value equal to its default is not
/// stored. Enter saves, Esc cancels, Tab moves between the text fields; "Defaults" puts every
/// setting of the card back.
///
/// The card is a layered window: everything is painted and hit-tested here, and the texts are
/// typed into real text boxes riding over the painted fields (<see cref="CardTextHost"/>). The
/// card paints the field texts too, so a picture of the card without its text boxes (the
/// preview tool) is complete.
/// </summary>
internal sealed class WidgetSettingsCard : GhostCardForm
{
    private enum ZoneKind { Swatch, Cell, Select, Toggle, Field }
    private readonly record struct Zone(Rectangle R, ZoneKind Kind, int Def, int Idx);

    private readonly DashWidgetDef _def;
    private readonly DashItem _item;
    private readonly IReadOnlyList<DashOptionDef> _defs;
    private readonly DashContext _ctx;
    private readonly Action<DashOptions> _save;
    private readonly DashOptions _work;                                   // the settings being edited
    private readonly List<Zone> _zones = new();
    private readonly Dictionary<string, string> _text = new();            // what every text and number field holds now
    private readonly Dictionary<string, CardTextHost> _hosts = new();
    private readonly Dictionary<string, Rectangle> _rects = new();        // the fields' text areas, window coordinates
    private readonly HashSet<string> _badText = new();                    // number fields whose text is not a number in range
    private Bitmap? _preview;
    private bool _previewStale = true;
    private bool _loading;                                                // the card is filling the boxes itself

    public WidgetSettingsCard(DashWidgetDef def, DashItem item, IReadOnlyList<DashOptionDef> defs, DashContext ctx, Action<DashOptions> onSave)
        : base("//WIDGET", Lang.T(def.NameKey), Lang.T("dash_set_hint"), Lang.T("set_save"), Lang.T("gen_cancel"), () => { })
    {
        _def = def; _item = item; _defs = defs; _ctx = ctx; _save = onSave;
        _work = DashOptions.Of(item);
        foreach (var o in defs)
            if (o.Kind is DashOptKind.Text or DashOptKind.Number) _text[o.Key] = TextOf(o);
    }

    /// <summary>What a field shows for the setting's current value.</summary>
    private string TextOf(DashOptionDef o)
    {
        if (o.Kind == DashOptKind.Text) return _work.Text(o.Key);
        int v = o.NumberIn(_work);
        return o.EmptyValue == v ? "" : v.ToString(CultureInfo.InvariantCulture);
    }

    private bool Bad(DashOptionDef o) => _badText.Contains(o.Key) || (o.Rule != null && !o.Rule(_work));

    // ---------------- card hooks ----------------

    protected override int CardWidth => 640;
    protected override int SideInset => 8;
    protected override string AuxLabel => Lang.T("set_default");
    protected override Color AuxColor => Ink;

    /// <summary>"Defaults": every setting of this card back to its default; keys the card does not show stay.</summary>
    protected override void AuxClick()
    {
        foreach (var o in _defs) _work.Remove(o.Key);
        _badText.Clear();
        _loading = true;
        foreach (var o in _defs)
        {
            if (!_text.ContainsKey(o.Key)) continue;
            _text[o.Key] = TextOf(o);
            if (_hosts.TryGetValue(o.Key, out var h)) h.Box.Text = _text[o.Key];
        }
        _loading = false;
        _previewStale = true;
        Rerender();
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
            if (o.Placeholder.Length > 0) h.Box.PlaceholderText = o.Placeholder;
            h.Box.TextChanged += (_, _) => { if (!_loading) Typed(opt, h.Box.Text); };
            _hosts[o.Key] = h;
        }
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
            string t = text.Trim();
            int? v = t.Length == 0 ? o.EmptyValue
                   : int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= o.Min && n <= o.Max ? n : null;
            if (v is { } val)
            {
                _badText.Remove(o.Key);
                if (val == o.DefaultInt) _work.Remove(o.Key); else _work.Set(o.Key, val);
            }
            else _badText.Add(o.Key);
        }
        _previewStale = true;
        Rerender();
    }

    // ---------------- the picture ----------------

    /// <summary>The widget at its size on the Dashboard, painted with the settings being edited and the current data sample.</summary>
    private Bitmap Preview(int cell)
    {
        int gap = _ctx.S(16);
        int w = _item.W * cell + (_item.W - 1) * gap, h = _item.H * cell + (_item.H - 1) * gap;
        if (!_previewStale && _preview != null && _preview.Width == w && _preview.Height == h) return _preview;
        _preview?.Dispose();
        var widget = _def.New(_work.Clone());
        _previewStale = false;
        return _preview = DashPaint.Snapshot(_ctx.Owner, w, h, FieldBg, (g, r) => { widget.PaintStatic(g, r, _ctx); widget.PaintLive(g, r, _ctx); });
    }

    // ---------------- layout and painting ----------------

    protected override int MeasureContent(Graphics g, float k, int width) => Flow(g, k, width, null);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Flow(g, k, area.Width, (area.Location, origin));
        g.TextRenderingHint = hint;
    }

    private static bool Wide(DashOptionDef o) => o.Kind is DashOptKind.Color or DashOptKind.Choice;

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
        using var redB = new SolidBrush(SoftRed);
        int labelH = Ce(LabelFont.GetHeight(g)), gapLabel = Ce(8 * k), fieldH = Ce(36 * k), gapField = Ce(16 * k), hintGap = Ce(6 * k);

        int HintH(string s, int w) => s.Length == 0 ? 0 : hintGap + Ce(g.MeasureString(s, hintF, w, fmt).Height);
        void Hint(string s, Brush b, int hx, int hy, int w)
        {
            if (paint && s.Length > 0) g.DrawString(s, hintF, b, new RectangleF(hx, hy + hintGap, w, 4000), fmt);
        }
        int LabelRow(string s, int lx, int ly, int w)
        {
            // a label longer than its column wraps instead of running into the neighbour
            int h = Math.Max(labelH, Ce(g.MeasureString(s, LabelFont, w, fmt).Height));
            if (paint) g.DrawString(s, LabelFont, whiteB, new RectangleF(lx, ly, w, h + 2 * k), fmt);
            return h;
        }

        // ---- the picture: beside the first settings when the widget is narrow, across the card when it is wide ----
        bool side = _item.W <= 2;
        int pad = Ce(14 * k), cellGap = _ctx.S(16);
        int cell = side ? Ce((_item.W == 1 && _item.H == 1 ? 150 : 104) * k)
                        : Math.Min(Ce(120 * k), (width - pad * 2 - cellGap * (_item.W - 1)) / Math.Max(1, _item.W));
        var pv = Preview(cell);
        var field = side ? new Rectangle(x, y0, pv.Width + pad * 2, pv.Height + pad * 2) : new Rectangle(x, y0, width, pv.Height + pad * 2);
        if (paint)
        {
            using var path = RoundPath(field, Ce(10 * k));
            using var bg = new SolidBrush(FieldBg);
            g.FillPath(bg, path);
            g.DrawImage(pv, new Rectangle(field.X + (field.Width - pv.Width) / 2, field.Y + pad, pv.Width, pv.Height));
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
            cy += LabelRow(o.Label, cx, cy, cw) + gapLabel;
            if (o.Kind == DashOptKind.Color)
            {
                int d = Ce(30 * k), sg = Ce(10 * k), sx = cx, sy = cy;
                string cur = _work.Choice(o.Key, o.Default, DashPaint.PaletteChoices);
                for (int j = 0; j < DashPaint.PaletteChoices.Length; j++)
                {
                    if (sx + d > cx + cw) { sx = cx; sy += d + sg; }
                    var sr = new Rectangle(sx, sy, d, d);
                    if (paint)
                    {
                        string id = DashPaint.PaletteChoices[j];
                        var colors = id == "auto" ? (o.AutoColors.Count > 0 ? o.AutoColors : new[] { Cyan }) : new[] { DashPaint.PaletteColor(id) };
                        PaintSwatch(g, sr, colors, id == "auto", id == cur, HotZone == _zones.Count, k);
                        _zones.Add(new Zone(Win(sr), ZoneKind.Swatch, i, j));
                    }
                    sx += d + sg;
                }
                cy = sy + d;
                string hint = cur == "auto" ? o.AutoHint : o.Hint;
                Hint(hint, mutedB, cx, cy, cw);
                cy += HintH(hint, cw);
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
                Hint(o.Hint, mutedB, cx, cy, cw);
                cy += HintH(o.Hint, cw);
            }
            cy += gapField;
        }
        int wideBottom = anyWide ? cy - gapField : (side ? y0 : field.Bottom);
        int y = Math.Max(field.Bottom, wideBottom);

        // ---- texts, numbers and switches: two columns ----
        var narrow = new List<int>();
        for (int i = 0; i < _defs.Count; i++) if (!Wide(_defs[i])) narrow.Add(i);
        if (narrow.Count == 0) return y - y0;
        y += Ce(20 * k);
        int colGap = Ce(20 * k), colW = (width - colGap) / 2;
        string HintOf(DashOptionDef o) =>
            o.Kind == DashOptKind.Number && Bad(o) ? (o.Min.ToString(CultureInfo.InvariantCulture) + "-" + o.Max.ToString(CultureInfo.InvariantCulture) + " " + o.Unit).Trim() : o.Hint;
        int CellH(DashOptionDef o)
        {
            int lh = o.Kind == DashOptKind.Toggle ? labelH : Math.Max(labelH, Ce(g.MeasureString(o.Label, LabelFont, colW, fmt).Height));
            return lh + gapLabel + fieldH + HintH(HintOf(o), colW);
        }
        for (int n = 0; n < narrow.Count; n += 2)
        {
            int rowH = 0;
            for (int c = 0; c < 2 && n + c < narrow.Count; c++)
            {
                int i = narrow[n + c];
                var o = _defs[i];
                int fx = x + c * (colW + colGap);
                rowH = Math.Max(rowH, CellH(o));
                if (o.Kind == DashOptKind.Toggle)
                {
                    // the switch sits on the line of the neighbouring field, its label beside it
                    var row = new Rectangle(fx, y + labelH + gapLabel, colW, fieldH);
                    int tgW = Ce(40 * k), tgH = Ce(22 * k);
                    if (paint)
                    {
                        bool on = _work.Flag(o.Key, o.DefaultOn);
                        g.DrawString(o.Label, LabelFont, whiteB, new RectangleF(row.X, row.Y, row.Width - tgW - Ce(10 * k), row.Height), LeftText);
                        PaintSwitch(g, new Rectangle(row.Right - tgW, row.Y + (row.Height - tgH) / 2, tgW, tgH), on);
                        _zones.Add(new Zone(Win(row), ZoneKind.Toggle, i, 0));
                    }
                    Hint(o.Hint, mutedB, fx, row.Bottom, colW);
                    continue;
                }
                int lh = LabelRow(o.Label, fx, y, colW);
                var fr = new Rectangle(fx, y + lh + gapLabel, colW, fieldH);
                bool bad = Bad(o);
                if (paint)
                {
                    _hosts.TryGetValue(o.Key, out var host);
                    var inner = PaintField(g, fr, IsFocused(host), bad);
                    int unitW = o.Unit.Length > 0 ? Ce(g.MeasureString(o.Unit, LabelFont).Width) + Ce(8 * k) : 0;
                    if (unitW > 0) g.DrawString(o.Unit, LabelFont, mutedB, new RectangleF(inner.Right - unitW, fr.Y, unitW, fr.Height), CenterText);
                    var textArea = new Rectangle(inner.X, inner.Y, inner.Width - unitW, inner.Height);
                    _rects[o.Key] = Win(textArea);
                    // the text itself, for a picture of the card without its text boxes; the box covers it otherwise
                    string t = _text[o.Key];
                    g.DrawString(t.Length > 0 ? t : o.Placeholder, fieldF, t.Length > 0 ? whiteB : mutedB,
                        new RectangleF(textArea.X, textArea.Y, textArea.Width, textArea.Height), LeftText);
                    _zones.Add(new Zone(Win(fr), ZoneKind.Field, i, 0));
                }
                string hint = HintOf(o);
                Hint(hint, bad && o.Kind == DashOptKind.Number ? redB : mutedB, fx, fr.Bottom, colW);
            }
            y += rowH + gapField;
        }
        return y - gapField - y0;
    }

    private static int IndexOf(DashOptionDef o, string id)
    {
        for (int i = 0; i < o.Items.Count; i++) if (o.Items[i].Id == id) return i;
        return -1;
    }

    /// <summary>
    /// A colour to pick: a disc, ringed when picked. The "automatic" swatch never looks like a
    /// palette colour: three slices for the temperature colours, a hollow ring for a widget whose
    /// own colour is a single one (which may well be in the palette too).
    /// </summary>
    private static void PaintSwatch(Graphics g, Rectangle r, IReadOnlyList<Color> colors, bool auto, bool selected, bool hot, float k)
    {
        float inset = 3.5f * k;
        var disc = new RectangleF(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
        if (colors.Count <= 1 && auto)
        {
            float w = Math.Max(2f, 4.5f * k);
            using var p = new Pen(colors.Count == 1 ? colors[0] : Muted, w);
            g.DrawEllipse(p, disc.X + w / 2, disc.Y + w / 2, disc.Width - w, disc.Height - w);
        }
        else if (colors.Count <= 1)
        {
            using var b = new SolidBrush(colors.Count == 1 ? colors[0] : Muted);
            g.FillEllipse(b, disc);
        }
        else
        {
            float sweep = 360f / colors.Count;
            for (int i = 0; i < colors.Count; i++)
            {
                using var b = new SolidBrush(colors[i]);
                g.FillPie(b, disc.X, disc.Y, disc.Width, disc.Height, -90 + i * sweep, sweep + 0.5f);
            }
        }
        if (!selected && !hot) return;
        using var pen = new Pen(selected ? White : Color.FromArgb(110, White), Math.Max(1.5f, 2f * k));
        float half = pen.Width / 2f;
        g.DrawEllipse(pen, r.X + half, r.Y + half, r.Width - pen.Width, r.Height - pen.Width);
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
                Pick(o, DashPaint.PaletteChoices[z.Idx]);
                break;
            case ZoneKind.Cell:
                Pick(o, o.Items[z.Idx].Id);
                break;
            case ZoneKind.Select:
            {
                var ids = o.Items.Select(t => t.Id).ToArray();
                int sel = Math.Max(0, IndexOf(o, _work.Choice(o.Key, o.Default, ids)));
                var items = o.Items.Select(t => new CardPopupList.Item(t.Caption)).ToArray();
                CardPopupList.Open(this, new Rectangle(Left + z.R.X, Top + z.R.Y, z.R.Width, z.R.Height), items, sel, UiScale, o, i => Pick(o, o.Items[i].Id));
                break;
            }
            case ZoneKind.Toggle:
            {
                bool on = !_work.Flag(o.Key, o.DefaultOn);
                if (on == o.DefaultOn) _work.Remove(o.Key); else _work.Set(o.Key, on);
                _previewStale = true;
                Rerender();
                break;
            }
            case ZoneKind.Field:
                if (_hosts.TryGetValue(o.Key, out var h)) FocusField(h);
                break;
        }
    }

    private void Pick(DashOptionDef o, string id)
    {
        if (id == o.Default) _work.Remove(o.Key); else _work.Set(o.Key, id);
        _previewStale = true;
        Rerender();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview?.Dispose();
        base.Dispose(disposing);
    }
}
