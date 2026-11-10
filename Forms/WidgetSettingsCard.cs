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
/// texts, numbers and switches in two columns under it. A colour that belongs to a field (the
/// label's colour, the colour of a warning) is a small colour button at the end of that field;
/// it opens the same palette in a drop-down (<see cref="CardPopupPalette"/>).
///
/// A value equal to its default is not stored, and an empty text or number field means the
/// default, shown in grey. A setting switched off by another one is dimmed and keeps its value.
/// Enter saves, Esc cancels, Tab moves between the text fields; "Defaults" puts every setting of
/// the card back.
///
/// The card is a layered window: everything is painted and hit-tested here, and the texts are
/// typed into real text boxes riding over the painted fields (<see cref="CardTextHost"/>). The
/// card paints the field texts too, so a picture of the card without its text boxes (the
/// preview tool) is complete.
/// </summary>
internal sealed class WidgetSettingsCard : GhostCardForm
{
    private enum ZoneKind { Swatch, Cell, Select, Toggle, Field, Chip }
    private readonly record struct Zone(Rectangle R, ZoneKind Kind, int Def, int Idx);

    private static readonly Color Dim = Color.FromArgb(0x5E, 0x66, 0x75);   // a switched-off field's texts

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

    // ---------------- card hooks ----------------

    protected override int CardWidth => 640;
    protected override int SideInset => 8;
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
        _badText.Clear();
        _loading = true;
        foreach (var o in _defs)
        {
            if (!_text.ContainsKey(o.Key)) continue;
            _text[o.Key] = TextOf(o);
            if (_hosts.TryGetValue(o.Key, out var h)) h.Box.Text = _text[o.Key];
        }
        _loading = false;
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
            if (GreyOf(o).Length > 0) h.Box.PlaceholderText = GreyOf(o);
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
        Rerender();
    }

    // ---------------- the picture ----------------

    /// <summary>The widget painted with the settings being edited and the current data sample, at the size the card gives it.</summary>
    private Bitmap Preview(Size size)
    {
        int w = size.Width, h = size.Height;
        if (!_previewStale && _preview != null && _preview.Width == w && _preview.Height == h) return _preview;
        _preview?.Dispose();
        var widget = _def.New(_work.Clone());
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
        using var dimB = new SolidBrush(Dim);
        using var redB = new SolidBrush(SoftRed);
        bool tight = _tight;
        int labelH = Ce(LabelFont.GetHeight(g)), gapLabel = Ce((tight ? 5 : 8) * k), fieldH = Ce((tight ? 32 : 36) * k),
            gapField = Ce((tight ? 10 : 16) * k), hintGap = Ce((tight ? 4 : 6) * k);
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

        // ---- the picture: beside the first settings when the widget is narrow, across the card when it is wide ----
        bool side = _item.W <= 2;
        int pad = Ce((tight ? 10 : 14) * k), cellGap = _ctx.S(16);
        int cell = side ? Ce((_item.W == 1 && _item.H == 1 ? (tight ? 136 : 150) : (tight ? 92 : 104)) * k)
                        : Math.Min(Ce((tight ? 100 : 120) * k), (width - pad * 2 - cellGap * (_item.W - 1)) / Math.Max(1, _item.W));
        var pv = new Size(_item.W * cell + (_item.W - 1) * cellGap, _item.H * cell + (_item.H - 1) * cellGap);
        var field = side ? new Rectangle(x, y0, pv.Width + pad * 2, pv.Height + pad * 2) : new Rectangle(x, y0, width, pv.Height + pad * 2);
        if (paint)
        {
            using var path = RoundPath(field, Ce(10 * k));
            using var bg = new SolidBrush(FieldBg);
            g.FillPath(bg, path);
            // the widget is painted only here: measuring needs its size, not its picture
            g.DrawImage(Preview(pv), new Rectangle(field.X + (field.Width - pv.Width) / 2, field.Y + pad, pv.Width, pv.Height));
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
                    CardPalette.Paint(g, LabelFont, plan, k, AutoOf(o), defText, _work.Choice(o.Key, o.Default, DashPaint.PaletteChoices),
                        hot >= 0 && hot < plan.Items.Length ? hot : -1);
                    for (int j = 0; j < plan.Items.Length; j++) _zones.Add(new Zone(Win(plan.Items[j]), ZoneKind.Swatch, i, j));
                }
                cy += plan.Height;
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
        if (narrow.Count == 0) return y - y0;
        y += Ce((tight ? 12 : 20) * k);
        int colGap = Ce(20 * k), colW = (width - colGap) / 2;
        int chipW = Ce(68 * k), chipGap = Ce(8 * k), tgW = Ce(40 * k), tgH = Ce(22 * k);
        string HintOf(DashOptionDef o) =>
            o.Kind == DashOptKind.Number && Bad(o) ? (o.Min.ToString(CultureInfo.InvariantCulture) + "-" + o.Max.ToString(CultureInfo.InvariantCulture) + " " + o.Unit).Trim() : o.Hint;

        // the colour button of a field: a dot of the colour in use, the palette on a click
        void Chip(Rectangle r, int i, bool on)
        {
            var o = _defs[i];
            var c = DashPaint.Pick(_work.Choice(o.ColorKey, "auto", DashPaint.PaletteChoices), AutoOf(o));
            PaintSelect(g, r, "", on, on && HotZone == _zones.Count, false, (gg, box, _) => CardPalette.Disc(gg, box, on ? c : Color.FromArgb(90, c)));
            if (on) _zones.Add(new Zone(Win(r), ZoneKind.Chip, i, 0));
        }

        for (int n = 0; n < narrow.Count; n += 2)
        {
            // one label height for the row, so the controls of both cells stand on one line
            int rowLabel = labelH, rowHint = 0;
            for (int c = 0; c < 2 && n + c < narrow.Count; c++)
            {
                var o = _defs[narrow[n + c]];
                rowLabel = Math.Max(rowLabel, LabelH(o.Label, colW));
                rowHint = Math.Max(rowHint, HintH(HintOf(o), colW));
            }
            int rowY = y + rowLabel + gapLabel;
            for (int c = 0; c < 2 && n + c < narrow.Count; c++)
            {
                int i = narrow[n + c];
                var o = _defs[i];
                int fx = x + c * (colW + colGap);
                bool on = Live(o), chip = o.ColorKey.Length > 0;
                PutLabel(o.Label, fx, y, colW, rowLabel, on);
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
        return y - gapField - y0;
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
                    _work.Choice(o.ColorKey, "auto", DashPaint.PaletteChoices), UiScale, o, id => Pick(o.ColorKey, id, "auto"));
                break;
            case ZoneKind.Toggle:
            {
                bool on = !_work.Flag(o.Key, o.DefaultOn);
                if (on == o.DefaultOn) _work.Remove(o.Key); else _work.Set(o.Key, on);
                Changed();
                break;
            }
            case ZoneKind.Field:
                if (_hosts.TryGetValue(o.Key, out var h)) FocusField(h);
                break;
        }
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
