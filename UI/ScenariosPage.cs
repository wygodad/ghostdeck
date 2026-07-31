using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

// =====================================================================
//  Scenarios
// =====================================================================
public sealed class ScenariosPage : ThemedPage
{
    private const int TileH = 280, Gap = 16, Pad = 28;
    private readonly Tile[] _tiles;
    private readonly SegControl _charge;
    private readonly ToggleSwitch _auto;
    private readonly SegControl? _kbd;    // (#26) only on models with the backlight register
    private readonly SegControl? _refreshSeg;      // panel refresh rate (SegControl when few modes...)
    private readonly ThemedComboBox? _refreshCombo; // ...combo when the panel reports many
    private readonly List<int> _rates;
    private readonly (string key, FeatureBrick b)[] _bricks;   // key -> Settings visibility (ScenHidden)
    private readonly List<SceneCard> _sceneCards = new();   // (#21)
    private readonly Button _addScene = new(), _addExamples = new();
    private int _headH, _subY, _bricksTop, _scenesHeadY;
    private Button? _panicBtn;
    private bool ScenesVisible => !D.Settings.ScenHidden.Contains("scenes");

    private void SetPanelRate(int hz)
    {
        int before = Display.Current();
        if (before == hz || !Display.SetRefresh(hz)) return;
        ChangeLog.Add(ChangeSource.Display, $"{before} Hz → {hz} Hz");
    }

    // Follow the current panel rate (a scene, the AC/battery switch or Windows may change it).
    private void SyncRefreshBrick()
    {
        if (_rates.Count == 0) return;
        int idx = _rates.IndexOf(Display.Current());
        if (idx < 0) return;
        if (_refreshSeg != null) _refreshSeg.Selected = idx;                       // silent setter
        else if (_refreshCombo != null && _refreshCombo.SelectedIndex != idx)
            _refreshCombo.SelectedIndex = idx;                                     // fires, but SetPanelRate no-ops on equal rate
    }

    public ScenariosPage(MainDeps d) : base(d)
    {
        _tiles = Profiles.Order.Select(id => new Tile(d, id)).ToArray();
        foreach (var t in _tiles) Controls.Add(t);

        _charge = new SegControl(new[] { Lang.T("gen_off_short"), "60%", "80%", "100%" }, ChargeIndex()) { Size = new Size(280, 34) };
        _charge.SelectedChanged += i => D.SetChargeLimit(i switch { 1 => 60, 2 => 80, 3 => 100, _ => 0 });

        _auto = new ToggleSwitch { Checked = D.Settings.AutoSwitchEnabled };
        _auto.Toggled += v => D.SetAutoSwitch(v);

        // One uniform "brick" per feature (discussion feedback): toggles and the charge-limit
        // segment all live in matching boxes under the profile tiles. Each brick has a key the
        // user can hide it by (Settings → General → Scenarios tab).
        var bricks = new List<(string, FeatureBrick)>
        {
            ("fanboost", new FeatureBrick("cooler_boost_short", "❄", "cooler_boost_hint",
                             () => D.Writable() && D.CoolerBoost(), v => D.SetCoolerBoost(v))),
            ("overlay", new FeatureBrick("overlay_title", "▦", "overlay_hint",
                             () => D.OverlayOn(), v => D.SetOverlay(v))),
            ("charge", new FeatureBrick("st_charge", "⚡", _charge)),
            ("autoswitch", new FeatureBrick("scen_autoswitch", "⇄", _auto)),
        };
        // panel refresh rate: works on every laptop (pure Windows display API, no EC)
        _rates = Display.SupportedRates();
        if (_rates.Count > 1)
        {
            int cur = Math.Max(0, _rates.IndexOf(Display.Current()));
            if (_rates.Count <= 4)
            {
                _refreshSeg = new SegControl(_rates.Select(r => r + " Hz").ToArray(), cur) { Size = new Size(280, 34) };
                _refreshSeg.SelectedChanged += i => SetPanelRate(_rates[i]);
                bricks.Add(("refresh", new FeatureBrick("ref_title", "▣", _refreshSeg)));
            }
            else
            {
                _refreshCombo = new ThemedComboBox { Width = 200 };
                _refreshCombo.Items.AddRange(_rates.Select(r => (object)(r + " Hz")).ToArray());
                _refreshCombo.SelectedIndex = cur;
                _refreshCombo.SelectedIndexChanged += (_, _) => SetPanelRate(_rates[Math.Max(0, _refreshCombo.SelectedIndex)]);
                bricks.Add(("refresh", new FeatureBrick("ref_title", "▣", _refreshCombo)));
            }
        }
        else _rates = new List<int>();
        if (D.KbdLevel() >= 0)   // (#26) keyboard-backlight level (off/low/mid/high)
        {
            _kbd = new SegControl(new[] { Lang.T("kbd_off"), Lang.T("kbd_low"), Lang.T("kbd_mid"), Lang.T("kbd_high") },
                                  Math.Max(0, D.KbdLevel())) { Size = new Size(280, 34) };
            _kbd.SelectedChanged += i => D.SetKbdLevel(i);
            bricks.Add(("kbd", new FeatureBrick("kbd_title", "⌨", _kbd)));
        }
        if (D.WebcamState() >= 0)   // (#27) EC-level webcam switch
            bricks.Add(("webcam", new FeatureBrick("webcam_title", "◉", "webcam_hint",
                                        () => D.WebcamState() == 1, v => D.SetWebcam(v))));
        // panic reset: same safe-stock action as the hotkey; styled like the fan-curve
        // preset Delete button (filled red, white text, no border)
        var panicBtn = new Button { AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        Ui.StylePrimary(panicBtn);
        panicBtn.Text = Lang.T("hk_panic");
        panicBtn.BackColor = Theme.Red;
        panicBtn.ForeColor = Color.White;
        panicBtn.FlatAppearance.BorderSize = 0;
        panicBtn.Click += (_, _) => D.PanicReset();
        _panicBtn = panicBtn;
        bricks.Add(("panic", new FeatureBrick("hk_panic", "↺", panicBtn)));
        _bricks = bricks.ToArray();
        foreach (var (_, b) in _bricks) Controls.Add(b);

        // (#21) scenes: add / example buttons + one card per scene (built in RebuildScenes)
        _addScene.Text = "+  " + Lang.T("scene_add");
        _addScene.AutoSize = true;
        _addScene.Padding = new Padding(10, 4, 10, 4);
        Ui.StyleGhost(_addScene);
        _addScene.Click += (_, _) => EditScene(null);
        Controls.Add(_addScene);
        _addExamples.Text = Lang.T("scene_add_examples");
        _addExamples.AutoSize = true;
        _addExamples.Padding = new Padding(10, 4, 10, 4);
        Ui.StyleGhost(_addExamples);
        _addExamples.Click += (_, _) => AddExampleScenes();
        Controls.Add(_addExamples);
        RebuildScenes();

        Resize += (_, _) => Relayout();
    }

    // ---------------- scenes (#21) ----------------
    private void RebuildScenes()
    {
        foreach (var c in _sceneCards) { Controls.Remove(c); c.Dispose(); }
        _sceneCards.Clear();
        foreach (var s in D.Settings.Scenes)
        {
            var scene = s;
            var card = new SceneCard(scene,
                run: () => D.RunScene(scene),
                edit: () => EditScene(scene),
                del: () => DeleteScene(scene),
                move: d2 => MoveScene(scene, d2));
            _sceneCards.Add(card);
            Controls.Add(card);
        }
        _addExamples.Visible = D.Settings.Scenes.Count == 0;
        Relayout();
        Invalidate();
    }

    private void EditScene(SceneDef? existing)
    {
        var copy = existing?.Clone() ?? new SceneDef();
        using var dlg = new SceneEditForm(D, copy, allowDelete: existing != null);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (dlg.DeleteRequested && existing != null) { DeleteScene(existing); return; }
        if (existing == null) D.Settings.Scenes.Add(copy);
        else
        {
            int i = D.Settings.Scenes.FindIndex(x => x.Id == existing.Id);
            if (i >= 0) D.Settings.Scenes[i] = copy; else D.Settings.Scenes.Add(copy);
        }
        D.SaveSettings();
        D.SettingsChanged();   // tray submenu + per-scene hotkeys follow the edit
        RebuildScenes();
    }

    // Confirmation is inline at the call sites (armed ✕ on the card, two-step button in the
    // editor) - no popup, same pattern as the camera-block confirm.
    private void DeleteScene(SceneDef s)
    {
        D.Settings.Scenes.RemoveAll(x => x.Id == s.Id);
        D.Settings.Hotkeys.Remove(s.HotkeyKey);
        D.SaveSettings();
        D.SettingsChanged();
        RebuildScenes();
    }

    private void MoveScene(SceneDef s, int dir)
    {
        var list = D.Settings.Scenes;
        int i = list.FindIndex(x => x.Id == s.Id);
        int j = i + dir;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        D.SaveSettings();
        D.SettingsChanged();
        RebuildScenes();
    }

    // Localized starter set ("Current" / "Gaming" / "Work" / "Travel") so the feature explains
    // itself; rates and per-model extras (backlight) are only included when actually available.
    // "Current" freezes the machine's state as it is right now, so after trying the examples
    // one click brings everything back.
    private void AddExampleScenes()
    {
        var rates = Display.SupportedRates();
        int maxHz = rates.Count > 0 ? rates.Max() : 0;
        int lowHz = rates.Contains(60) ? 60 : (rates.Count > 0 ? rates.Min() : 0);
        bool kbd = D.KbdLevel() >= 0;
        var list = D.Settings.Scenes;
        int curHz = Display.Current();
        list.Add(new SceneDef
        {
            Name = Lang.T("scene_example_current"), Glyph = "⭐",
            Profile = D.Current().ToString(),
            Overlay = D.OverlayOn(),
            RefreshHz = curHz > 0 ? curHz : null,
            ChargeLimit = D.Settings.ChargeLimit is 60 or 80 or 100 ? D.Settings.ChargeLimit : 0,
            KbdLight = D.KbdLevel() >= 0 ? D.KbdLevel() : null,
            Webcam = D.WebcamState() >= 0 ? D.WebcamState() == 1 : null,
            CurvePreset = D.Settings.CurveActive && D.Settings.CurveName.Length > 0 ? D.Settings.CurveName : null,
        });
        list.Add(new SceneDef { Name = "Gaming", Glyph = "🎮", Profile = "Extreme", Overlay = true,
                                RefreshHz = maxHz > 0 ? maxHz : null });
        list.Add(new SceneDef { Name = Lang.T("scene_example_work"), Glyph = "💼", Profile = "Silent", Overlay = false,
                                RefreshHz = lowHz > 0 ? lowHz : null, ChargeLimit = 80 });
        list.Add(new SceneDef { Name = Lang.T("scene_example_travel"), Glyph = "✈", Profile = "SuperBattery", Overlay = false,
                                RefreshHz = lowHz > 0 ? lowHz : null, KbdLight = kbd ? 0 : null });
        D.SaveSettings();
        D.SettingsChanged();
        RebuildScenes();
    }

    private int ChargeIndex() => D.Settings.ChargeLimit switch { 60 => 1, 80 => 2, 100 => 3, _ => 0 };

    public override void OnEnter()
    {
        _addScene.Text = "+  " + Lang.T("scene_add");          // follow a language change
        _addExamples.Text = Lang.T("scene_add_examples");
        _charge.Selected = ChargeIndex();
        _auto.Checked = D.Settings.AutoSwitchEnabled;
        if (_kbd != null && D.KbdLevel() is >= 0 and var kl) _kbd.Selected = kl;   // follows the Fn key too
        SyncRefreshBrick();
        foreach (var (_, b) in _bricks) b.SyncState();
        Relayout();
        Invalidate();
    }

    // External state changed (profile/cooler/overlay/visibility) — refresh bricks; a Relayout is
    // included because the Settings → Scenarios-tab visibility toggles land here too.
    public override void LiveRefresh()
    {
        _charge.Selected = ChargeIndex();
        _auto.Checked = D.Settings.AutoSwitchEnabled;
        if (_kbd != null && D.KbdLevel() is >= 0 and var kl) _kbd.Selected = kl;
        SyncRefreshBrick();
        foreach (var (_, b) in _bricks) b.SyncState();
        Relayout();
        Invalidate(true);
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        foreach (var t in _tiles) t.Invalidate();
        _charge.Invalidate(); _auto.Invalidate();
        _kbd?.Invalidate();
        _refreshSeg?.Invalidate();
        _refreshCombo?.Invalidate();
        foreach (var (_, b) in _bricks) b.ApplyTheme();
        foreach (var c in _sceneCards) c.Invalidate();
        Ui.StyleGhost(_addScene);       // ghost styling reads Theme at call time
        Ui.StyleGhost(_addExamples);
        if (_panicBtn != null) { _panicBtn.BackColor = Theme.Red; _panicBtn.ForeColor = Color.White; }
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

        // Uniform feature bricks under the tiles (mockup W5 layout): two per row, three when
        // the window is wide enough for the 280 px segments to still fit. Bricks the user hid
        // (Settings → General → Scenarios tab) are skipped entirely.
        _bricksTop = _headH + TileH + 24;
        int cols = avail >= 1080 ? 3 : 2;
        const int brickH = 82;
        int bw = (avail - Gap * (cols - 1)) / cols;
        var visBricks = new List<FeatureBrick>();
        foreach (var (key, b) in _bricks)
        {
            bool vis = !D.Settings.ScenHidden.Contains(key);
            if (b.Visible != vis) b.Visible = vis;
            if (vis) visBricks.Add(b);
        }
        int rows = 0;
        for (int i = 0; i < visBricks.Count; i++)
        {
            int r = i / cols, c = i % cols;
            visBricks[i].SetBounds(Pad + c * (bw + Gap), _bricksTop + r * (brickH + Gap), bw, brickH);
            rows = r + 1;
        }
        int bricksBottom = _bricksTop + rows * (brickH + Gap);

        // (#21) scenes: section header, cards, then the add / examples buttons; the whole
        // section can be hidden ("scenes" key)
        bool scenes = ScenesVisible;
        foreach (var c in _sceneCards) if (c.Visible != scenes) c.Visible = scenes;
        if (_addScene.Visible != scenes) _addScene.Visible = scenes;
        _addExamples.Visible = scenes && D.Settings.Scenes.Count == 0;
        if (!scenes)
        {
            _scenesHeadY = bricksBottom + 10;
            AutoScrollMinSize = new Size(820, bricksBottom + 12);
            return;
        }
        _scenesHeadY = bricksBottom + 10;
        int headFontH = new Font("Segoe UI", 13f, FontStyle.Bold).Height;
        int y = _scenesHeadY + headFontH + 12;
        int sCols = cols;
        const int cardH = 74;
        int cw = (avail - Gap * (sCols - 1)) / sCols;
        for (int i = 0; i < _sceneCards.Count; i++)
        {
            int r = i / sCols, c = i % sCols;
            _sceneCards[i].SetBounds(Pad + c * (cw + Gap), y + r * (cardH + Gap), cw, cardH);
        }
        if (_sceneCards.Count > 0) y += ((_sceneCards.Count + sCols - 1) / sCols) * (cardH + Gap);
        else y += new Font("Segoe UI", 9.5f).Height + 14;   // room for the empty-state hint text
        _addScene.Location = new Point(Pad, y);
        _addExamples.Location = new Point(Pad + _addScene.PreferredSize.Width + 10, y);
        int bottom = y + _addScene.PreferredSize.Height + 8;
        AutoScrollMinSize = new Size(820, bottom + 12);
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
        // (the tier badge lives in the header strip now, next to the version)

        // (#21) scenes section header + empty-state hint (unless the section is hidden)
        if (ScenesVisible)
        {
            TextRenderer.DrawText(g, Lang.T("scene_title"), new Font("Segoe UI", 13f, FontStyle.Bold),
                new Point(Pad, _scenesHeadY), Theme.Text);
            if (_sceneCards.Count == 0)
                TextRenderer.DrawText(g, Lang.T("scene_empty"), new Font("Segoe UI", 9.5f),
                    new Point(Pad, _scenesHeadY + new Font("Segoe UI", 13f, FontStyle.Bold).Height + 10), Theme.Muted);
        }
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
            if (active)
            {
                // soft inner neon (ghostdeck.dev card style): fading strokes just inside the border
                for (int i = 1; i <= 3; i++)
                {
                    using var gp = Theme.RoundRect(new RectangleF(0.5f + i * 2, 0.5f + i * 2, Width - 1 - i * 4, Height - 1 - i * 4), 6);
                    using var pen = new Pen(Color.FromArgb(46 - i * 12, Theme.Accent), 2f);
                    g.DrawPath(pen, gp);
                }
            }
            // icon centred on top, text stacked below, SELECT/ACTIVE footer (font-height stacked = DPI-safe)
            int iconBox = 76;
            var nameFont = new Font("Segoe UI", 15f, FontStyle.Bold);
            var subFont = new Font("Segoe UI", 10.5f);
            var footFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            int nameH = nameFont.Height, subH = subFont.Height, g1 = 16, g2 = 6, g3 = 18, footH = footFont.Height + 12;
            int blockH = iconBox + g1 + nameH + g2 + subH + g3 + footH;
            int top = Math.Max(16, (Height - blockH) / 2);
            IconPainter.Scenario(g, _id, new RectangleF((Width - iconBox) / 2f, top, iconBox, iconBox), col, 4f);
            int textW = Width - 24;
            TextRenderer.DrawText(g, def.Label, nameFont,
                new Rectangle(12, top + iconBox + g1, textW, nameH), Theme.Text,
                TextFormatFlags.Top | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Lang.T(def.SubKey), subFont,
                new Rectangle(12, top + iconBox + g1 + nameH + g2, textW, subH), Theme.Muted,
                TextFormatFlags.Top | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            int footY = top + iconBox + g1 + nameH + g2 + subH + g3;
            if (active)
            {
                string t = Lang.T("scen_active");
                int tw = TextRenderer.MeasureText(t, new Font("Segoe UI", 9.5f, FontStyle.Bold)).Width;   // same font Ui.Pill sizes with
                Ui.Pill(g, t, new Point((Width - (tw + 32)) / 2, footY), Theme.Accent);
            }
            else
            {
                TextRenderer.DrawText(g, Lang.T("scen_select"), footFont,
                    new Rectangle(12, footY + 6, textW, footFont.Height + 2),
                    _hover ? Theme.Muted : Theme.Faint,
                    TextFormatFlags.Top | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    /// <summary>
    /// (#21) One scene as a clickable card: glyph + name + a summary of what it sets.
    /// Click = run. The pencil hotspot (top-right) edits; right-click offers run / edit /
    /// reorder / delete.
    /// </summary>
    private sealed class SceneCard : Control
    {
        private readonly SceneDef _scene;
        private readonly Action _run, _edit, _del;
        private readonly Action<int> _move;
        private bool _hover;
        private int _hotHover = -1;          // which action hotspot the mouse is over
        private bool _armDelete;             // ✕ was clicked once; the next ✕ click deletes
        private readonly System.Windows.Forms.Timer _armTimer = new() { Interval = 3500 };
        private const int HotW = 26, HotGap = 2, HotCount = 5;   // ▶ ↑ ↓ ✎ ✕
        private static readonly string[] HotGlyphs = { "▶", "↑", "↓", "✎", "✕" };

        public SceneCard(SceneDef scene, Action run, Action edit, Action del, Action<int> move)
        {
            _scene = scene; _run = run; _edit = edit; _del = del; _move = move;
            DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand;
            _armTimer.Tick += (_, _) => Disarm();

            var menu = new ContextMenuStrip();
            var mRun = new ToolStripMenuItem(Lang.T("scene_run")); mRun.Click += (_, _) => run();
            var mEdit = new ToolStripMenuItem(Lang.T("scene_edit")); mEdit.Click += (_, _) => edit();
            var mUp = new ToolStripMenuItem(Lang.T("scene_up")); mUp.Click += (_, _) => move(-1);
            var mDown = new ToolStripMenuItem(Lang.T("scene_down")); mDown.Click += (_, _) => move(1);
            var mDel = new ToolStripMenuItem(Lang.T("scene_delete")); mDel.Click += (_, _) => Arm();   // arms the same inline confirm
            menu.Items.AddRange(new ToolStripItem[] { mRun, mEdit, new ToolStripSeparator(), mUp, mDown, new ToolStripSeparator(), mDel });
            ContextMenuStrip = menu;

            MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                int hot = HotAt(e.Location);
                switch (hot)
                {
                    case 0: _run(); break;
                    case 1: _move(-1); break;
                    case 2: _move(1); break;
                    case 3: _edit(); break;
                    case 4:
                        if (_armDelete) { _armTimer.Stop(); _del(); }
                        else Arm();
                        break;
                    default:
                        if (_armDelete) Disarm();
                        else _run();   // the card body itself still runs the scene
                        break;
                }
            };
            MouseMove += (_, e) =>
            {
                int h = HotAt(e.Location);
                if (h != _hotHover) { _hotHover = h; Invalidate(); }
            };
        }

        private void Arm() { _armDelete = true; _armTimer.Stop(); _armTimer.Start(); Invalidate(); }
        private void Disarm() { _armTimer.Stop(); if (_armDelete) { _armDelete = false; Invalidate(); } }

        private Rectangle HotRect(int i) =>
            new(Width - (HotCount - i) * (HotW + HotGap) - 8, (Height - HotW) / 2, HotW, HotW);

        private int HotAt(Point p)
        {
            for (int i = 0; i < HotCount; i++) if (HotRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _hotHover = -1; Disarm(); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _armTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(outer, 6))
            {
                using var b = new SolidBrush(Theme.Card);
                g.FillPath(b, path);
                using var pen = new Pen(_armDelete ? Theme.Red : _hover ? Theme.Accent : Theme.Border, _hover || _armDelete ? 1.4f : 1f);
                g.DrawPath(pen, path);
            }
            // glyph box on the left (falls back to a play marker when the scene has no icon)
            string glyph = _scene.Glyph.Length > 0 ? _scene.Glyph : "▶";
            TextRenderer.DrawText(g, glyph, new Font("Segoe UI Emoji", 15f),
                new Rectangle(12, 0, 40, Height), Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            int tx = 58, tw = Width - tx - HotCount * (HotW + HotGap) - 16;
            var nameFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            var subFont = new Font("Segoe UI", 8.75f);
            int nameH = nameFont.Height, subH = subFont.Height;
            int top = (Height - nameH - 4 - subH) / 2;
            TextRenderer.DrawText(g, _scene.Name, nameFont, new Rectangle(tx, top, tw, nameH),
                Theme.Text, TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            // armed delete replaces the summary with the confirm hint (amber), like the camera block
            TextRenderer.DrawText(g, _armDelete ? Lang.T("scene_del_arm") : _scene.Summary(), subFont,
                new Rectangle(tx, top + nameH + 4, tw, subH),
                _armDelete ? Theme.Amber : Theme.Muted, TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            // action hotspots: ▶ run, ↑ ↓ reorder, ✎ edit, ✕ delete (armed = red)
            for (int i = 0; i < HotCount; i++)
            {
                Color c = i == 4 && _armDelete ? Theme.Red
                        : i == _hotHover ? (i == 4 ? Theme.Red : Theme.Accent)
                        : _hover ? Theme.Muted : Theme.Faint;
                var f = new Font("Segoe UI", i == 4 && _armDelete ? 12f : 10.5f, i == 4 && _armDelete ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(g, HotGlyphs[i], f, HotRect(i), c,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    /// <summary>
    /// Small reusable "feature card": rounded card with an icon box, a label and a right-side
    /// toggle switch — styled after MSI Center's feature tiles. Cooler Boost is the first; more
    /// on/off features (e.g. Windows key, display features) can be added to the grid the same way.
    /// </summary>
    private sealed class FeatureBrick : Control
    {
        private readonly string _labelKey;
        private readonly string _glyph;
        private readonly Func<bool>? _get;
        private readonly ToggleSwitch? _toggle;
        private readonly Control _right;
        private readonly HelpDot? _help;
        private readonly ToolTip _tip = new() { InitialDelay = 250, AutoPopDelay = 15000, ReshowDelay = 100 };
        private bool _hover;

        public FeatureBrick(string labelKey, string glyph, string tipKey, Func<bool> get, Action<bool> set)
        {
            _labelKey = labelKey; _glyph = glyph; _get = get;
            DoubleBuffered = true; ResizeRedraw = true;
            BackColor = Theme.Card;                       // so the child controls blend with the card interior
            var toggle = new ToggleSwitch { Checked = get() };
            toggle.Toggled += v => set(v);
            _toggle = toggle; _right = toggle;
            _help = new HelpDot();
            _tip.SetToolTip(_help, Ui.Wrap(Lang.T(tipKey), 46));
            Controls.Add(_right);
            Controls.Add(_help);
            Resize += (_, _) => LayoutInner();
        }

        /// <summary>Brick hosting an arbitrary right-side control (e.g. a SegControl) instead of a toggle.</summary>
        public FeatureBrick(string labelKey, string glyph, Control right)
        {
            _labelKey = labelKey; _glyph = glyph; _right = right;
            DoubleBuffered = true; ResizeRedraw = true;
            BackColor = Theme.Card;
            Controls.Add(_right);
            Resize += (_, _) => LayoutInner();
        }

        public void SyncState() { if (_toggle != null && _get != null) _toggle.Checked = _get(); }
        public void ApplyTheme() { BackColor = Theme.Card; _right.Invalidate(); _help?.Invalidate(); Invalidate(); }

        private void LayoutInner()
        {
            _right.Location = new Point(Width - _right.Width - 18, (Height - _right.Height) / 2);
            _help?.SetBounds(_right.Left - _help.Width - 14, (Height - _help.Height) / 2, _help.Width, _help.Height);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(outer, 6))
            {
                using var b = new SolidBrush(Theme.Card);
                g.FillPath(b, path);
                using var pen = new Pen(_hover ? Theme.BorderStrong : Theme.Border, 1f);
                g.DrawPath(pen, path);
            }
            // icon box (outlined square + glyph, like the MSI Center feature cards)
            int box = 32, bx = 18, by = (Height - box) / 2;
            using (var pen = new Pen(Theme.Accent, 1.6f))
            using (var ip = Theme.RoundRect(new RectangleF(bx + 0.5f, by + 0.5f, box - 1, box - 1), 6))
                g.DrawPath(pen, ip);
            using (var gf = new Font("Segoe UI Symbol", 12f))
                Ui.CenterGlyph(g, _glyph, gf, Theme.Accent, new RectangleF(bx, by, box, box));
            // label (stops before the help dot + toggle)
            int lx = bx + box + 14, rightPad = _right.Width + 14 + (_help != null ? _help.Width + 14 : 0) + 12;
            TextRenderer.DrawText(g, Lang.T(_labelKey), new Font("Segoe UI", 11.5f, FontStyle.Bold),
                new Rectangle(lx, 0, Math.Max(20, Width - lx - rightPad), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Circled "?" help marker; shows an explanatory tooltip on hover (used by feature bricks).</summary>
    private sealed class HelpDot : Control
    {
        public HelpDot() { DoubleBuffered = true; ResizeRedraw = true; Size = new Size(22, 22); Cursor = Cursors.Help; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Card);
            using (var pen = new Pen(Theme.Muted, 1.4f))
                g.DrawEllipse(pen, 1f, 1f, Width - 2f, Height - 2f);
            using var f = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            Ui.CenterGlyph(g, "?", f, Theme.Muted, new RectangleF(0, 0, Width, Height));
        }
    }
}
