namespace GhostDeck;

/// <summary>
/// (#21) Scene editor: name + icon, then one row per orchestrated setting. Each row has a
/// toggle ("set this") and a value combo - rows left off stay null on the SceneDef, meaning
/// the scene leaves that setting alone. Rows for hardware the model lacks (curve tables,
/// keyboard backlight, webcam) are simply not shown.
/// </summary>
public sealed class SceneEditForm : Form
{
    private readonly SceneDef _scene;
    private readonly TextBox _name = new(), _glyph = new();
    private int _y = 14;
    private readonly List<Action> _commit = new();

    /// <summary>Set when the user pressed the delete button (only offered for existing scenes).</summary>
    public bool DeleteRequested { get; private set; }

    public SceneEditForm(MainDeps d, SceneDef scene, bool allowDelete = false)
    {
        _scene = scene;
        Text = Lang.T("scene_title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        ClientSize = new Size(600, 200);   // height finalized after the rows are built
        BackColor = Theme.Surface;
        Icon = TrayIconFactory.AppIcon();

        // name + optional glyph on one line
        AddLabel(Lang.T("scene_name"), 16, _y + 4);
        _name.SetBounds(150, _y, 200, 28);
        StyleBox(_name);
        _name.Text = scene.Name;
        AddLabel(Lang.T("scene_glyph"), 372, _y + 4, muted: true);
        _glyph.SetBounds(372, _y + 22, 52, 28);
        StyleBox(_glyph);
        _glyph.Text = scene.Glyph;
        Controls.Add(_name);
        Controls.Add(_glyph);
        _y += 58;

        var hint = new Label
        {
            Text = Lang.T("scene_hint_unchecked"), AutoSize = true, MaximumSize = new Size(ClientSize.Width - 32, 0),
            ForeColor = Theme.Muted, BackColor = Theme.Surface, Font = new Font("Segoe UI", 8.5f),
            Location = new Point(16, _y),
        };
        Controls.Add(hint);
        _y += hint.PreferredHeight + 12;

        // ---- rows ----
        var profNames = Profiles.Order.Select(id => Profiles.Get(id).Label).ToArray();
        int profSel = Math.Max(0, Array.FindIndex(Profiles.Order, id => Profiles.Get(id).Key == scene.Profile));
        Row(Lang.T("sc_profile"), profNames, scene.Profile != null, profSel,
            (on, i) => _scene.Profile = on ? Profiles.Get(Profiles.Order[i]).Key : null);

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
            var rateItems = rates.Select(r => r + " Hz").ToArray();
            int rSel = scene.RefreshHz is { } hz ? Math.Max(0, rates.IndexOf(hz)) : rates.Count - 1;
            Row(Lang.T("ref_title"), rateItems, scene.RefreshHz != null, rSel,
                (on, i) => { _scene.RefreshHz = on ? rates[i] : null; _scene.RefreshTarget = on ? Display.TargetPath() : null; });
        }

        if (Brightness.Supported)
        {
            var briVals = Enumerable.Range(1, 20).Select(i => i * 5).ToArray();   // 5..100 %
            var briItems = briVals.Select(v => v + " %").ToArray();
            int bSel = Math.Clamp((int)Math.Round((scene.BrightnessPct ?? 50) / 5.0) - 1, 0, briVals.Length - 1);
            Row(Lang.T("bri_title"), briItems, scene.BrightnessPct != null, bSel,
                (on, i) => _scene.BrightnessPct = on ? briVals[i] : null);
        }

        if (Hdr.Supported())
            Row("HDR", new[] { Lang.T("st_on"), Lang.T("st_off") },
                scene.Hdr != null, scene.Hdr == true ? 0 : 1,
                (on, i) => _scene.Hdr = on ? i == 0 : null);

        Row(Lang.T("overlay_title"), new[] { Lang.T("st_on"), Lang.T("st_off") },
            scene.Overlay != null, scene.Overlay == false ? 1 : 0,
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
        var chargeItems = chargeVals.Select(v => v == 0 ? Lang.T("gen_off_short") : v + "%").ToArray();
        int chSel = scene.ChargeLimit is { } cl ? Math.Max(0, Array.IndexOf(chargeVals, cl)) : 2;
        Row(Lang.T("st_charge"), chargeItems, scene.ChargeLimit != null, chSel,
            (on, i) => _scene.ChargeLimit = on ? chargeVals[i] : null);

        if (d.KbdLevel() >= 0)
            Row(Lang.T("kbd_title"), new[] { Lang.T("kbd_off"), Lang.T("kbd_low"), Lang.T("kbd_mid"), Lang.T("kbd_high") },
                scene.KbdLight != null, scene.KbdLight ?? 0,
                (on, i) => _scene.KbdLight = on ? i : null);

        if (d.WebcamState() >= 0)
            Row(Lang.T("webcam_title"), new[] { Lang.T("st_on"), Lang.T("st_off") },
                scene.Webcam != null, scene.Webcam == false ? 1 : 0,
                (on, i) => _scene.Webcam = on ? i == 0 : null);

        Row(Lang.T("winlock_title"), new[] { Lang.T("st_on"), Lang.T("st_off") },
            scene.WinLock != null, scene.WinLock == true ? 0 : 1,
            (on, i) => _scene.WinLock = on ? i == 0 : null);

        if (d.TouchpadState() >= 0)
            Row(Lang.T("tp_title"), new[] { Lang.T("st_on"), Lang.T("st_off") },
                scene.Touchpad != null, scene.Touchpad == false ? 1 : 0,
                (on, i) => _scene.Touchpad = on ? i == 0 : null);

        Row(Lang.T("cooler_boost"), new[] { Lang.T("st_on"), Lang.T("st_off") },
            scene.FanBoost != null, scene.FanBoost == true ? 0 : 1,
            (on, i) => _scene.FanBoost = on ? i == 0 : null);

        // ---- buttons ----
        _y += 8;
        var ok = new Button { Text = Lang.T("gen_ok"), AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
        var cancel = new Button { Text = Lang.T("gen_cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
        Ui.StylePrimary(ok); ok.Height = 32;
        Ui.StyleGhost(cancel); cancel.Height = 32;
        ok.Click += (_, _) =>
        {
            string n = _name.Text.Trim();
            if (n.Length == 0) { _name.Focus(); return; }   // a scene needs a name
            _scene.Name = n;
            _scene.Glyph = _glyph.Text.Trim();
            foreach (var c in _commit) c();
            DialogResult = DialogResult.OK;
        };
        ClientSize = new Size(600, _y + 52);
        ok.Location = new Point(ClientSize.Width - 16 - 90 - 8 - ok.PreferredSize.Width, _y);
        cancel.Location = new Point(ClientSize.Width - 16 - cancel.PreferredSize.Width, _y);
        Controls.Add(ok);
        Controls.Add(cancel);
        if (allowDelete)
        {
            // two-step delete, no popup: the first click arms the button (amber confirm text),
            // the second one deletes - same pattern as the camera-block confirm
            var del = new Button { Text = Lang.T("scene_delete"), AutoSize = true, Padding = new Padding(14, 2, 14, 2), Height = 32 };
            Ui.StyleGhost(del);
            del.ForeColor = Theme.Red;
            del.Location = new Point(16, _y);
            bool armed = false;
            del.Click += (_, _) =>
            {
                if (!armed) { armed = true; del.Text = Lang.T("scene_del_arm"); del.ForeColor = Theme.Amber; return; }
                DeleteRequested = true;
                DialogResult = DialogResult.OK;
            };
            Controls.Add(del);
        }
        AcceptButton = ok; CancelButton = cancel;
        Shown += (_, _) => _name.Focus();
    }

    private void AddLabel(string text, int x, int y, bool muted = false)
    {
        Controls.Add(new Label
        {
            Text = text, AutoSize = true, Location = new Point(x, y),
            ForeColor = muted ? Theme.Muted : Theme.Text, BackColor = Theme.Surface,
            Font = new Font("Segoe UI", muted ? 8.5f : 10f),
        });
    }

    private void StyleBox(TextBox b)
    {
        b.BackColor = Theme.Card;
        b.ForeColor = Theme.Text;
        b.BorderStyle = BorderStyle.FixedSingle;
        b.Font = new Font("Segoe UI", 10.5f);
    }

    // One orchestrated setting: [toggle] label ......... [value combo]
    private void Row(string label, string[] items, bool set, int sel, Action<bool, int> commit)
    {
        var tg = new ToggleSwitch { Checked = set };
        var combo = new ThemedComboBox { Width = 190, Enabled = set };
        combo.Items.AddRange(items);
        combo.SelectedIndex = Math.Clamp(sel, 0, items.Length - 1);
        tg.Toggled += v => combo.Enabled = v;

        tg.Location = new Point(16, _y + 3);
        var lbl = new Label
        {
            Text = label, AutoSize = true, Location = new Point(16 + tg.Width + 10, _y + 5),
            ForeColor = Theme.Text, BackColor = Theme.Surface, Font = new Font("Segoe UI", 10f),
        };
        combo.Location = new Point(ClientSize.Width - 16 - combo.Width, _y);
        Controls.Add(tg);
        Controls.Add(lbl);
        Controls.Add(combo);
        _commit.Add(() => commit(tg.Checked, Math.Max(0, combo.SelectedIndex)));
        _y += 40;
    }
}
