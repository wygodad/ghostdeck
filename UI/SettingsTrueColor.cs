namespace GhostDeck;

// =====================================================================
//  Settings -> System -> "MSI True Color (experimental)" (roadmap #119, discussions #150, #204)
//  One card: what is installed (version, running, confirmed), the opt-in switch with its
//  one-time consent card, the standing disclaimer, seven profile tiles, two honest state lines
//  (what True Color recorded, what GhostDeck last asked for) and, for a version not on the
//  confirmed list, the owner's own test. Without True Color installed the card is one sentence.
//  Every change refreshes the controls in place (_syncTrueColor); the card is never rebuilt.
// =====================================================================
public sealed partial class SettingsPage
{
    private Action? _syncTrueColor;

    private CardSection BuildTrueColorCard()
    {
        var card = new CardSection(Lang.T("tc_title") + " " + Lang.T("tc_exp"), "");   // MDL2 Color
        static Label Note(string text) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(360, 0), Font = Dpi.Font("Segoe UI", 9f), Tag = "muted",
        };
        var info = TrueColor.Detect(D.Settings.TrueColorUserVerified);
        if (!info.Installed)
        {
            card.AddRow(null, Note(Lang.T("tc_not_installed")));
            _syncTrueColor = null;
            return card;
        }

        var status = Note("");
        card.AddRow(null, status);
        var toggle = Toggle(D.Settings.TrueColorEnabled, SetTrueColorEnabled);
        card.AddRow(Lang.T("tc_enable"), toggle);
        card.AddRow(null, Note(Lang.T("tc_disclaimer")));
        var tiles = new TrueColorTiles();
        tiles.Pick += id => D.SetTrueColor(id);
        card.AddRow(null, tiles);
        var state = Note("");
        card.AddRow(null, state);
        var testBtn = new Button { Text = Lang.T("tc_check_btn"), AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        Ui.StyleGhost(testBtn);
        testBtn.Click += (_, _) => StartTrueColorTest();
        card.AddRow(null, testBtn);

        bool built = false;
        _syncTrueColor = () =>
        {
            var i = TrueColor.Detect(D.Settings.TrueColorUserVerified);
            bool warn = i.Status is TrueColor.Status.NotRunning or TrueColor.Status.Unconfirmed;
            status.Text = i.Status switch
            {
                TrueColor.Status.Ready => string.Format(Lang.T(i.UserVerified ? "tc_status_user_verified" : "tc_status_ok"), i.Version),
                TrueColor.Status.NotRunning => string.Format(Lang.T("tc_status_not_running"), i.Version),
                TrueColor.Status.Unconfirmed => string.Format(Lang.T("tc_status_unconfirmed"), i.Version),
                _ => Lang.T("tc_not_installed"),
            };
            status.Tag = warn ? "warn" : "muted";
            status.ForeColor = warn ? Theme.Amber : Theme.Muted;
            toggle.Enabled = i.Confirmed;
            toggle.Checked = D.Settings.TrueColorEnabled;
            tiles.Recorded = i.Recorded;
            tiles.Enabled = D.Settings.TrueColorEnabled && i.Status == TrueColor.Status.Ready;
            tiles.Invalidate();
            string recorded = i.Recorded is { } r ? string.Format(Lang.T("tc_recorded"), TrueColor.Name(r)) : Lang.T("tc_recorded_unknown");
            string last = D.Settings.TrueColorLast is { } l
                ? string.Format(Lang.T("tc_last"), TrueColor.Name(l) + ", " + D.Settings.TrueColorLastUtc.ToLocalTime().ToString("g"))
                : Lang.T("tc_last_never");
            state.Text = recorded + "\n" + last;
            bool showTest = !i.Confirmed;
            testBtn.Visible = showTest;
            // a text that wrapped onto another line, or the test row coming or going, moves the
            // rows below: one layout pass, once the page exists
            if (built) Layout2();
        };
        _syncTrueColor();
        built = true;
        return card;
    }

    /// <summary>The switch: the first enable anywhere shows the consent card (then never again); "Later" leaves it off.</summary>
    private void SetTrueColorEnabled(bool on)
    {
        if (on && !D.Settings.TrueColorConfirmed)
        {
            var dlg = new GhostCardForm("//TRUECOLOR", Lang.T("tc_consent_title"),
                Lang.T("tc_consent_text") + "\n\n" + Lang.T("tc_disclaimer"),
                Lang.T("tc_consent_on"), Lang.T("fw_dlg_later"),
                () => { D.Settings.TrueColorConfirmed = true; ApplyTrueColorEnabled(true); });
            dlg.FormClosed += (_, _) => _syncTrueColor?.Invoke();   // "Later": the toggle goes back to off
            dlg.Show();
            dlg.Activate();
            return;
        }
        ApplyTrueColorEnabled(on);
    }

    private void ApplyTrueColorEnabled(bool on)
    {
        D.Settings.TrueColorEnabled = on;
        D.SaveSettings();
        ChangeLog.Add(ChangeSource.Panel, Lang.T(on ? "tc_log_on" : "tc_log_off"));
        D.SettingsChanged();   // the tray adds or drops its True Color submenu
        _syncTrueColor?.Invoke();
    }

    /// <summary>
    /// The owner's test of a True Color version not on the confirmed list: a consent card, one
    /// switch to another profile, five seconds, a switch back, then the question. "Yes" records
    /// the version as verified on this machine and unlocks the switch; "No" records nothing.
    /// </summary>
    private void StartTrueColorTest()
    {
        var info = TrueColor.Detect(D.Settings.TrueColorUserVerified);
        if (!info.Installed || info.Confirmed) return;
        if (!info.Running) { TrueColorNote(Lang.T("tc_err_not_running")); return; }
        if (info.Recorded is not { } from) { TrueColorNote(Lang.T("tc_recorded_unknown")); return; }
        int target = from == 4 ? 3 : 4;   // Anti-Blue is the most visible change; from Anti-Blue, Movie
        var dlg = new GhostCardForm("//TRUECOLOR", Lang.T("tc_check_title"),
            string.Format(Lang.T("tc_check_text"), info.Version, TrueColor.Name(target), TrueColor.Name(from)),
            Lang.T("tc_check_go"), Lang.T("gen_cancel"), () => RunTrueColorTest(info.Version, from, target));
        dlg.Show();
        dlg.Activate();
    }

    private void RunTrueColorTest(string version, int from, int target)
    {
        ChangeLog.Add(ChangeSource.Panel, string.Format(Lang.T("tc_log_test"), version), TrueColor.Name(target) + " -> " + TrueColor.Name(from));
        Task.Run(() =>
        {
            var r1 = TrueColor.Set(target, null, out string d1, allowUnconfirmed: true);
            if (r1 != TrueColor.Result.Ok)
            {
                Post(() => TrueColorNote(TrueColor.ReasonText(r1, d1)));
                return;
            }
            Thread.Sleep(5000);
            var r2 = TrueColor.Set(from, null, out string d2, allowUnconfirmed: true);
            Post(() =>
            {
                string body = string.Format(Lang.T("tc_verify_text"), version, TrueColor.Name(target), TrueColor.Name(from));
                if (r2 != TrueColor.Result.Ok) body += "\n\n" + Lang.T("tc_verify_restore_failed") + " (" + TrueColor.ReasonText(r2, d2) + ")";
                var ask = new GhostCardForm("//TRUECOLOR", Lang.T("tc_verify_title"), body,
                    Lang.T("tc_verify_yes"), Lang.T("tc_verify_no"), () =>
                    {
                        D.Settings.TrueColorUserVerified = version;
                        D.SaveSettings();
                        ChangeLog.Add(ChangeSource.Panel, string.Format(Lang.T("tc_log_verified"), version));
                        D.SettingsChanged();
                        _syncTrueColor?.Invoke();
                    });
                ask.Show();
                ask.Activate();
            });
        });
    }

    private void Post(Action a)
    {
        try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }
        catch { }
    }

    private static void TrueColorNote(string body)
    {
        var dlg = new GhostCardForm("//TRUECOLOR", Lang.T("tc_title"), body, Lang.T("gen_ok"), "", () => { });
        dlg.Show();
        dlg.Activate();
    }

    /// <summary>
    /// Seven profile tiles on one drawn surface (one system window, like WinPowerBody): four per
    /// row, the recorded profile outlined in the accent, hover on the others. Disabled = greyed,
    /// no hover, no clicks. Width comes from the card ("wp-wide"); the height follows.
    /// </summary>
    private sealed class TrueColorTiles : Control
    {
        private static readonly Font TileF = Dpi.Font("Segoe UI", 9.5f);
        private readonly Rectangle[] _rects = new Rectangle[TrueColor.Profiles.Length];
        private int _hover = -1;

        public int? Recorded { get; set; }
        public event Action<int>? Pick;

        public TrueColorTiles()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Name = "wp-wide";
            Width = 360;
        }

        private void Place()
        {
            float k = Dpi.Of(this) / 96f;
            int gap = (int)(8 * k), h = (int)(34 * k), cols = 4;
            int w = Math.Max(10, (Width - gap * (cols - 1)) / cols);
            int rows = (_rects.Length + cols - 1) / cols;
            for (int i = 0; i < _rects.Length; i++)
                _rects[i] = new Rectangle((i % cols) * (w + gap), (i / cols) * (h + gap), w, h);
            int want = rows * h + (rows - 1) * gap;
            if (Height != want) Height = want;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Place(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); _hover = -1; Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Card);

        protected override void OnPaint(PaintEventArgs e)
        {
            Place();
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float k = Dpi.Of(this) / 96f;
            for (int i = 0; i < _rects.Length; i++)
            {
                var r = _rects[i];
                bool on = Recorded == TrueColor.Profiles[i].Id;
                Color fill = !Enabled ? Theme.Card : on ? Theme.AccentSoft : i == _hover ? Theme.RowAlt : Theme.Surface;
                Color line = on && Enabled ? Theme.Accent : Theme.Border;
                using var path = Theme.RoundRect(new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), 7 * k);
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(line, on ? 1.6f * k : 1f)) g.DrawPath(p, path);
                if (on && Enabled)
                {
                    int d = (int)(6 * k);
                    using var db = new SolidBrush(Theme.Accent);
                    g.FillEllipse(db, r.X + (int)(8 * k), r.Y + (r.Height - d) / 2, d, d);
                }
                Color text = !Enabled ? Theme.Faint : Theme.Text;
                TextRenderer.DrawText(g, TrueColor.Profiles[i].Name, TileF, r, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);   // dpi-ok: True Color mode names (data from MSI) in equal tiles; last resort
            }
        }

        private int Hit(Point p)
        {
            for (int i = 0; i < _rects.Length; i++) if (_rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Enabled ? Hit(e.Location) : -1;
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            int h = Hit(e.Location);
            if (h >= 0) Pick?.Invoke(TrueColor.Profiles[h].Id);
        }
    }
}
