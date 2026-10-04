namespace GhostDeck;

/// <summary>
/// (#21) Scene editor, as a GhostDeck card: name + icon, then one row per orchestrated setting.
/// Each row has a switch ("set this") and a value picker - rows left off stay null on the
/// SceneDef, meaning the scene leaves that setting alone. Rows for hardware the model lacks
/// (curve tables, keyboard backlight, webcam) are simply not shown.
///
/// The card is a layered window, so everything is painted and hit-tested here: the switches,
/// the pickers (segments when every choice fits side by side, otherwise a select field that
/// opens a <see cref="CardPopupList"/>), and the frames of the two text fields. The text
/// itself is typed into real text boxes riding over those frames (<see cref="CardTextHost"/>).
/// Touching a picker of a row that is off switches the row on - one click, not two.
/// Keyboard: Enter saves, Esc cancels, Tab moves between the text fields; with the card
/// itself focused ↑/↓ pick a row, Space flips its switch, ←/→ change its value.
/// </summary>
public sealed class SceneEditForm : GhostCardForm
{
    private sealed class RowDef
    {
        public required string Label { get; init; }
        public required string[] Items { get; init; }
        public required Action<bool, int> Commit { get; init; }
        public bool On { get; set; }
        public int Sel { get; set; }
        /// <summary>
        /// Optional icon per item (the profile row shows the scenario icons); the flag says the
        /// icon sits on the selection fill, where it has to be white.
        /// </summary>
        public Func<int, Action<Graphics, RectangleF, bool>>? Icon { get; init; }
    }

    private enum ZoneKind { Toggle, Cell, Select, Name, Glyph }
    private readonly record struct Zone(Rectangle R, ZoneKind Kind, int Row, int Idx);

    private readonly SceneDef _scene;
    private readonly bool _allowDelete;
    private readonly List<RowDef> _rows = new();
    private readonly List<Zone> _zones = new();
    private CardTextHost? _nameHost, _glyphHost;
    private Font? _hostFont;
    private Rectangle _nameRect, _glyphRect;   // text areas of the two fields, window coordinates
    private float _k = 1f;
    private int _focus;                        // 1 = name, 2 = icon, 0 = neither
    private int _kbRow = -1;                   // keyboard row; its marker appears only once the arrows are used
    private bool _armed, _nameMissing;

    /// <summary>Set when the user pressed the delete button (only offered for existing scenes).</summary>
    public bool DeleteRequested { get; private set; }

    public SceneEditForm(MainDeps d, SceneDef scene, bool allowDelete = false)
        : base("//SCENE", Lang.T(allowDelete ? "scene_edit_title" : "scene_add"), Lang.T("scene_hint_unchecked"),
               Lang.T("set_save"), Lang.T("gen_cancel"), () => { })
    {
        _scene = scene;
        _allowDelete = allowDelete;

        int profSel = Math.Max(0, Array.FindIndex(Profiles.Order, id => Profiles.Get(id).Key == scene.Profile));
        _rows.Add(new RowDef
        {
            Label = Lang.T("sc_profile"), Items = Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray(),
            On = scene.Profile != null, Sel = profSel,
            Commit = (on, i) => _scene.Profile = on ? Profiles.Get(Profiles.Order[i]).Key : null,
            Icon = i => (g, r, onFill) => IconPainter.Scenario(g, Profiles.Order[i], r, onFill ? Color.White : d.ColorOf(Profiles.Order[i]), 1.6f * _k),
        });

        if (d.HasFanCurve())
        {
            var curveItems = new List<string> { Lang.T("fc_preset_auto") };
            curveItems.AddRange(d.Settings.CurvePresets.Select(p => p.Name));
            int cSel = scene.CurvePreset is { Length: > 0 } cp ? Math.Max(0, curveItems.IndexOf(cp)) : 0;
            Row(Lang.T("fc_title"), curveItems.ToArray(), scene.CurvePreset != null, cSel,
                (on, i) => _scene.CurvePreset = !on ? null : i == 0 ? "" : curveItems[i]);
        }

        var rates = Display.SupportedRates();
        if (rates.Count > 0)
        {
            int rSel = scene.RefreshHz is { } hz ? Math.Max(0, rates.IndexOf(hz)) : rates.Count - 1;
            Row(Lang.T("ref_title"), rates.Select(r => r + " Hz").ToArray(), scene.RefreshHz != null, rSel,
                (on, i) => { _scene.RefreshHz = on ? rates[i] : null; _scene.RefreshTarget = on ? Display.TargetPath() : null; });
        }

        if (Brightness.Supported)
        {
            var briVals = Enumerable.Range(1, 20).Select(i => i * 5).ToArray();   // 5..100 %
            int bSel = Math.Clamp((int)Math.Round((scene.BrightnessPct ?? 50) / 5.0) - 1, 0, briVals.Length - 1);
            Row(Lang.T("bri_title"), briVals.Select(v => v + " %").ToArray(), scene.BrightnessPct != null, bSel,
                (on, i) => _scene.BrightnessPct = on ? briVals[i] : null);
        }

        // "on" / "off" are lower-case words in the status texts they come from; a picker cell is a caption
        static string Cap(string t) => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];
        var onOff = new[] { Cap(Lang.T("st_on")), Cap(Lang.T("st_off")) };
        if (Hdr.Supported())
            Row("HDR", onOff, scene.Hdr != null, scene.Hdr == true ? 0 : 1, (on, i) => _scene.Hdr = on ? i == 0 : null);

        Row(Lang.T("overlay_title"), onOff, scene.Overlay != null, scene.Overlay == false ? 1 : 0,
            (on, i) => _scene.Overlay = on ? i == 0 : null);

        // The three presets plus, when one is in play, the custom threshold: the scene the user is
        // editing may already carry one, or their current setting may be custom - either way it has
        // to be selectable here, or saving the scene would silently round it to a preset.
        var chargeList = new List<int> { 0, 60, 80, 100 };
        int chargeExtra = scene.ChargeLimit is { } sc && !AppSettings.ChargeVerified(sc) && sc != 0 ? sc
                        : AppSettings.ChargeManaged(d.Settings.ChargeLimit) && !AppSettings.ChargeVerified(d.Settings.ChargeLimit) ? d.Settings.ChargeLimit
                        : 0;
        if (chargeExtra != 0) chargeList.Add(chargeExtra);
        int[] chargeVals = chargeList.ToArray();
        int chSel = scene.ChargeLimit is { } cl ? Math.Max(0, Array.IndexOf(chargeVals, cl)) : 2;
        Row(Lang.T("st_charge"), chargeVals.Select(v => v == 0 ? Lang.T("gen_off_short") : v + "%").ToArray(), scene.ChargeLimit != null, chSel,
            (on, i) => _scene.ChargeLimit = on ? chargeVals[i] : null);

        if (d.KbdLevel() >= 0)
            Row(Lang.T("kbd_title"), new[] { Lang.T("kbd_off"), Lang.T("kbd_low"), Lang.T("kbd_mid"), Lang.T("kbd_high") },
                scene.KbdLight != null, scene.KbdLight ?? 0, (on, i) => _scene.KbdLight = on ? i : null);

        if (d.WebcamState() >= 0)
            Row(Lang.T("webcam_title"), onOff, scene.Webcam != null, scene.Webcam == false ? 1 : 0,
                (on, i) => _scene.Webcam = on ? i == 0 : null);

        Row(Lang.T("winlock_title"), onOff, scene.WinLock != null, scene.WinLock == true ? 0 : 1,
            (on, i) => _scene.WinLock = on ? i == 0 : null);

        if (d.TouchpadState() >= 0)
            Row(Lang.T("tp_title"), onOff, scene.Touchpad != null, scene.Touchpad == false ? 1 : 0,
                (on, i) => _scene.Touchpad = on ? i == 0 : null);

        Row(Lang.T("cooler_boost"), onOff, scene.FanBoost != null, scene.FanBoost == true ? 0 : 1,
            (on, i) => _scene.FanBoost = on ? i == 0 : null);
    }

    /// <summary>Runs the editor as a modal card centred over <paramref name="owner"/>.</summary>
    public DialogResult ShowOver(Form? owner)
    {
        HostWindow = owner;
        return ShowDialog(owner);
    }

    private void Row(string label, string[] items, bool set, int sel, Action<bool, int> commit) =>
        _rows.Add(new RowDef { Label = label, Items = items, On = set, Sel = Math.Clamp(sel, 0, items.Length - 1), Commit = commit });

    // ---------------- card hooks ----------------

    protected override int CardWidth => 600;

    protected override string AuxLabel => !_allowDelete ? "" : Lang.T(_armed ? "scene_del_arm" : "scene_delete");
    protected override Color AuxColor => _armed ? Amber : SoftRed;

    /// <summary>Two-step delete, no popup: the first click arms the button, the second one deletes.</summary>
    protected override void AuxClick()
    {
        if (!_armed) { _armed = true; Rerender(); return; }
        DeleteRequested = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool Acknowledge()
    {
        string n = (_nameHost?.Box.Text ?? _scene.Name).Trim();
        if (n.Length == 0)
        {
            // a scene needs a name: mark the field and put the caret there instead of closing
            _nameMissing = true;
            Rerender();
            FocusHost(_nameHost);
            return false;
        }
        _scene.Name = n;
        _scene.Glyph = (_glyphHost?.Box.Text ?? _scene.Glyph).Trim();
        foreach (var r in _rows) r.Commit(r.On, r.Sel);
        DialogResult = DialogResult.OK;
        return true;
    }

    private static int Ce(float v) => (int)Math.Ceiling(v);
    private static int CapH(float k) => Ce(15 * k);
    private static int FieldH(float k) => Ce(36 * k);
    private static int NameBlockH(float k) => CapH(k) + Ce(6 * k) + FieldH(k) + Ce(16 * k);

    /// <summary>Rows shrink a little on a low screen so the whole card always fits.</summary>
    private int RowH(float k)
    {
        int screenH = Screen.FromRectangle(HostWindow?.Bounds ?? Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1600, 900)).WorkingArea.Height;
        int roomForRows = screenH - Ce(330 * k);   // header, heading, hint, name block, buttons, margins
        return Math.Clamp(roomForRows / Math.Max(1, _rows.Count), Ce(32 * k), Ce(40 * k));
    }

    protected override int MeasureContent(Graphics g, float k, int width) => NameBlockH(k) + _rows.Count * RowH(k) + Ce(4 * k);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        _k = k;
        _zones.Clear();
        Rectangle Win(Rectangle r) => new(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height);

        using var capF = new Font("Segoe UI", 10.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelF = new Font("Segoe UI Semibold", 13.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var cellF = new Font("Segoe UI", 12.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var center = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var left = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var whiteB = new SolidBrush(White);
        using var mutedB = new SolidBrush(Muted);
        using var faintB = new SolidBrush(Color.FromArgb(150, Muted));
        using var hair = new Pen(Color.FromArgb(22, 243, 247, 255), 1f);

        // ---- name + icon: caption above, field below ----
        string capName = Lang.T("scene_name").ToUpperInvariant(), capGlyph = Lang.T("scene_glyph").ToUpperInvariant();
        int glyphW = Ce(56 * k), capGlyphW = Ce(g.MeasureString(capGlyph, capF).Width);
        int glyphX = area.Right - Math.Max(glyphW, capGlyphW);
        int fy = area.Y + CapH(k) + Ce(6 * k), fh = FieldH(k);
        g.DrawString(capName, capF, mutedB, area.X, area.Y);
        g.DrawString(capGlyph, capF, mutedB, glyphX, area.Y);
        var nameField = new Rectangle(area.X, fy, glyphX - Ce(20 * k) - area.X, fh);
        var glyphField = new Rectangle(glyphX, fy, glyphW, fh);
        void Field(Rectangle r, bool focused, bool missing, int padX, ref Rectangle textRect)
        {
            using var fp = RoundPath(r, Ce(8 * k));
            using (var fb = new SolidBrush(FieldBg)) g.FillPath(fb, fp);
            using var ep = new Pen(missing ? SoftRed : focused ? Color.FromArgb(190, Cyan) : Color.FromArgb(40, 243, 247, 255), 1f);
            g.DrawPath(ep, fp);
            textRect = Win(Rectangle.Inflate(r, -padX, -Ce(4 * k)));
        }
        Field(nameField, _focus == 1, _nameMissing, Ce(12 * k), ref _nameRect);
        Field(glyphField, _focus == 2, false, Ce(6 * k), ref _glyphRect);
        _zones.Add(new Zone(Win(nameField), ZoneKind.Name, -1, 0));
        _zones.Add(new Zone(Win(glyphField), ZoneKind.Glyph, -1, 0));

        // ---- rows: [switch] label ........ [picker] ----
        int rowH = RowH(k), y0 = area.Y + NameBlockH(k);
        int tgW = Ce(40 * k), tgH = Ce(22 * k), labelX = area.X + tgW + Ce(12 * k);
        int pickW = Ce(250 * k), pickX = area.Right - pickW, inset = Ce(3 * k);
        int trackH = Math.Min(Ce(32 * k), rowH - Ce(6 * k));
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            int y = y0 + i * rowH;
            g.DrawLine(hair, area.X, y, area.Right, y);

            // the switch
            var tg = new Rectangle(area.X, y + (rowH - tgH) / 2, tgW, tgH);
            using (var tp = RoundPath(tg, tgH / 2))
            using (var tb = new SolidBrush(row.On ? Fill : Color.FromArgb(0x2E, 0x36, 0x46)))
                g.FillPath(tb, tp);
            float kd = tgH - 6 * k, kx = row.On ? tg.Right - kd - 3 * k : tg.X + 3 * k;
            using (var kb = new SolidBrush(row.On ? Color.White : Color.FromArgb(0xAE, 0xB8, 0xC9)))
                g.FillEllipse(kb, kx, tg.Y + (tgH - kd) / 2f, kd, kd);
            // the whole left part of the row flips the switch - a bigger target than the knob
            _zones.Add(new Zone(Win(new Rectangle(area.X, y, pickX - Ce(8 * k) - area.X, rowH)), ZoneKind.Toggle, i, 0));

            g.DrawString(row.Label, labelF, row.On ? whiteB : mutedB, new RectangleF(labelX, y, pickX - Ce(12 * k) - labelX, rowH), left);

            // the picker
            var track = new Rectangle(pickX, y + (rowH - trackH) / 2, pickW, trackH);
            int n = row.Items.Length;
            int cellW = (pickW - inset * 2) / Math.Max(1, n);
            bool segments = n is >= 2 and <= 4 && row.Items.All(t => g.MeasureString(t, cellF).Width + 8 * k <= cellW);
            bool hotSelect = !segments && HotZone == _zones.Count;
            using (var tp = RoundPath(track, Ce(9 * k)))
            {
                using var tb = new SolidBrush(Color.FromArgb(row.On ? 12 : 7, 255, 255, 255));
                g.FillPath(tb, tp);
                using var te = new Pen(i == _kbRow ? Color.FromArgb(170, Cyan) : Color.FromArgb(hotSelect ? 90 : row.On ? 34 : 22, 243, 247, 255), 1f);
                g.DrawPath(te, tp);
            }
            if (segments)
            {
                for (int j = 0; j < n; j++)
                {
                    var rc = new Rectangle(track.X + inset + j * cellW, track.Y + inset, cellW, trackH - inset * 2);
                    bool sel = row.Sel == j, hot = HotZone == _zones.Count;
                    if (sel || hot)
                    {
                        using var cp = RoundPath(rc, Ce(7 * k));
                        using var cb = new SolidBrush(sel && row.On ? Fill : Color.FromArgb(sel ? 30 : 22, 255, 255, 255));
                        g.FillPath(cb, cp);
                    }
                    g.DrawString(row.Items[j], cellF, row.On ? (sel || hot ? whiteB : mutedB) : (sel ? mutedB : faintB), rc, center);
                    _zones.Add(new Zone(Win(rc), ZoneKind.Cell, i, j));
                }
            }
            else
            {
                int x = track.X + Ce(12 * k), chev = Ce(8 * k), right = track.Right - Ce(12 * k) - chev;
                if (row.Icon != null)
                {
                    int ic = Ce(18 * k);
                    row.Icon(row.Sel)(g, new RectangleF(x, track.Y + (trackH - ic) / 2f, ic, ic), false);
                    x += ic + Ce(9 * k);
                }
                g.DrawString(row.Items[row.Sel], labelF, row.On ? whiteB : mutedB, new RectangleF(x, track.Y, right - Ce(6 * k) - x, trackH), left);
                using var cp = new Pen(row.On ? Ink : Color.FromArgb(150, Muted), 1.4f * k) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                float cy = track.Y + trackH / 2f;
                g.DrawLines(cp, new[] { new PointF(right, cy - chev * 0.25f), new PointF(right + chev / 2f, cy + chev * 0.25f), new PointF(right + chev, cy - chev * 0.25f) });
                _zones.Add(new Zone(Win(track), ZoneKind.Select, i, 0));
            }
        }
    }

    protected override int ContentHit(Point p)
    {
        // pickers sit on top of the row-wide switch zone, so look from the end
        for (int i = _zones.Count - 1; i >= 0; i--) if (_zones[i].R.Contains(p)) return i;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        if (zone < 0 || zone >= _zones.Count) return;
        var z = _zones[zone];
        switch (z.Kind)
        {
            case ZoneKind.Name: FocusHost(_nameHost); break;
            case ZoneKind.Glyph: FocusHost(_glyphHost); break;
            case ZoneKind.Toggle:
                _rows[z.Row].On = !_rows[z.Row].On;
                Rerender();
                break;
            case ZoneKind.Cell:
                _rows[z.Row].Sel = z.Idx;
                _rows[z.Row].On = true;
                Rerender();
                break;
            case ZoneKind.Select:
                OpenList(z.Row, z.R);
                break;
        }
    }

    private void OpenList(int rowIndex, Rectangle field)
    {
        var row = _rows[rowIndex];
        var items = row.Items.Select((t, i) => new CardPopupList.Item(t, row.Icon?.Invoke(i))).ToArray();
        CardPopupList.Open(this, new Rectangle(Left + field.X, Top + field.Y, field.Width, field.Height), items, row.Sel, _k, row, i =>
        {
            row.Sel = i;
            row.On = true;
            Rerender();
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up: case Keys.Down:
                _kbRow = _kbRow < 0 ? 0 : (_kbRow + (keyData == Keys.Down ? 1 : _rows.Count - 1)) % _rows.Count;
                Rerender();
                return true;
            case Keys.Left: case Keys.Right:
                if (_kbRow < 0) _kbRow = 0;
                else
                {
                    var r = _rows[_kbRow];
                    r.Sel = Math.Clamp(r.Sel + (keyData == Keys.Right ? 1 : -1), 0, r.Items.Length - 1);
                    r.On = true;
                }
                Rerender();
                return true;
            case Keys.Space:
                if (_kbRow < 0) _kbRow = 0;
                else _rows[_kbRow].On = !_rows[_kbRow].On;
                Rerender();
                return true;
            case Keys.Tab:
                FocusHost(_nameHost);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------- the two text boxes riding on the card ----------------

    // The boxes come up in the same call that shows the card (not a message-loop turn later
    // in OnShown), so the fields are never seen empty.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible || _nameHost != null) return;
        _nameHost = MakeHost(_scene.Name, HorizontalAlignment.Left, 1);
        _glyphHost = MakeHost(_scene.Glyph, HorizontalAlignment.Center, 2);
        _nameHost.Box.TextChanged += (_, _) => { if (_nameMissing) { _nameMissing = false; Rerender(); } };
        PlaceHosts();
        _nameHost.Show(this);
        _glyphHost.Show(this);
        FocusHost(_nameHost);
    }

    // The modal loop activates the card itself after it becomes visible, so the caret is put
    // into the name field once more when the card is fully up - typing works at once.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FocusHost(_nameHost);
    }

    private CardTextHost MakeHost(string text, HorizontalAlignment align, int focusId)
    {
        var h = new CardTextHost(FieldBg, White, align);
        h.Box.Text = text;
        h.Activated += (_, _) => SetFocus(focusId);
        h.Deactivate += (_, _) => { if (_focus == focusId) SetFocus(0); };
        h.CommandKey += key =>
        {
            if (key == Keys.Enter) TriggerAck();
            else if (key == Keys.Escape) Close();
            else FocusHost(focusId == 1 ? _glyphHost : _nameHost);   // Tab
        };
        return h;
    }

    private void SetFocus(int id)
    {
        if (_focus == id || IsDisposed) return;
        _focus = id;
        Rerender();
    }

    private static void FocusHost(CardTextHost? h)
    {
        if (h == null || h.IsDisposed) return;
        h.Activate();
        h.Box.Focus();
        h.Box.SelectionStart = h.Box.TextLength;
    }

    private void PlaceHosts()
    {
        if (_nameHost == null || _glyphHost == null || _nameRect.IsEmpty) return;
        float px = 14f * _k;
        if (_hostFont == null || Math.Abs(_hostFont.Size - px) > 0.01f)
        {
            _hostFont?.Dispose();
            _hostFont = new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        _nameHost.Place(new Rectangle(Left + _nameRect.X, Top + _nameRect.Y, _nameRect.Width, _nameRect.Height), _hostFont);
        _glyphHost.Place(new Rectangle(Left + _glyphRect.X, Top + _glyphRect.Y, _glyphRect.Width, _glyphRect.Height), _hostFont);
    }

    protected override void OnRendered() => PlaceHosts();

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        PlaceHosts();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _nameHost?.Dispose();
        _glyphHost?.Dispose();
        _hostFont?.Dispose();
    }
}
