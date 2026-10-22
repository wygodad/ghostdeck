using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Body of the "Windows power" settings card (discussion #141; roadmap #109 + #36 + #117): the
/// CPU turbo-boost switch and the turbo mode per power source of the active power plan, the
/// Windows power mode, and what both do on a profile switch.
///
/// ONE double-buffered, owner-drawn control. The first builds composed this card from ~35
/// nested AutoSize containers (FlowLayoutPanel / TableLayoutPanel); every layout pass made
/// them re-measure each other and repaint one by one, which showed as the tab drawing itself
/// element by element for about a second. Here a single <see cref="Flow"/> routine owns the
/// whole geometry - it measures when called without a Graphics and paints when called with
/// one, so layout and paint cannot disagree - and only the genuinely interactive pieces
/// (the ToggleSwitch, two ThemedComboBoxes, the SegControl, the HelpDot) are real child controls. Hotspots
/// (mapping edit/reset, restore, the reveal link) are painted and hit-tested.
///
/// Layout = the owner-approved flat design (2026-10-04, variant "Lines"): no filled frames,
/// no expanders, everything open; section rows separated by hairlines; the two groups set
/// apart by extra space; profile rows carry the scenario-tile icon in the profile colour.
/// </summary>
public sealed class WinPowerBody : Control
{
    private readonly MainDeps _d;
    private readonly ToggleSwitch _turbo = new();
    private readonly ThemedComboBox _cbAc = new(), _cbDc = new();   // (#117) turbo mode per source
    private readonly List<PowerPlan.BoostMode> _modes;
    private bool _syncing;                                           // Sync sets the lists; their event must not write back
    private bool _autonomous;                                        // PERFAUTONOMOUS on for both sources: modes 1-4 are one group
    private readonly HelpDot _help;
    private readonly SegControl _seg;
    private readonly ToolTip _tip = new();

    /// <summary>Raised when a state change (not a width change) altered the height.</summary>
    public event Action? ContentHeightChanged;

    // ---- model, refreshed by Sync() ----
    private string _plan = "-";
    private bool _haveBoost;
    private uint _ac, _dc;
    private int[]? _snap;
    private string _statusText = "";
    private int _statusKind;               // 0 plain, 1 amber (off, or the two sources differ)
    private bool _auto;
    private string _autoLine = "", _overrideLine = "";
    private readonly List<string> _restoreLines = new();
    private bool _hidden = true;

    // ---- painted hotspots ----
    private Rectangle _rEdit, _rReset, _rRestore, _rLink;
    private int _hot = -1;                 // 0 edit, 1 reset, 2 restore, 3 link
    private bool _inLayout;
    private PowerMapForm? _mapDlg;
    private WinPowerHelpForm? _helpDlg;

    private static readonly (ProfileId Id, string Name)[] MapRows =
    {
        (ProfileId.SuperBattery, "Super Battery"), (ProfileId.Silent, "Silent"),
        (ProfileId.Balanced, "Balanced"), (ProfileId.Extreme, "Extreme"),
    };
    private static readonly string[] ModeKeys = { "pwm_req_eff", "pwm_req_bal", "pwm_req_perf" };

    private static readonly Font FCap = Dpi.Font("Segoe UI", 8.25f, FontStyle.Bold);
    private static readonly Font FRow = Dpi.Font("Segoe UI Semibold", 11f);
    private static readonly Font FBody = Dpi.Font("Segoe UI", 10f);
    private static readonly Font FBodyB = Dpi.Font("Segoe UI Semibold", 10f);
    private static readonly Font FSub = Dpi.Font("Segoe UI", 8f, FontStyle.Bold);
    private static readonly Font FTitle = Dpi.Font("Segoe UI Semibold", 10.5f);
    private static readonly Font FGlyph = Dpi.Font("Segoe MDL2 Assets", 11f);

    private const TextFormatFlags FmtWrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

    public WinPowerBody(MainDeps d)
    {
        _d = d;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;

        _help = new HelpDot { OpenCard = OpenHelp };   // the whole card explained: a GhostDeck card, not a bubble
        _seg = new SegControl(new[] { Lang.T("pw_seg_eff"), Lang.T("pw_seg_bal"), Lang.T("pw_seg_perf"), Lang.T("pw_auto_seg") }, 1);
        _seg.MinimumSize = Size.Empty;   // the body sizes it; SegControl ellipsizes if a language outgrows a cell
        // the list Windows offers on THIS machine, names in its own language (read once, cached)
        _modes = PowerPlan.BoostModes();
        foreach (var cb in new[] { _cbAc, _cbDc })
        {
            cb.BeginUpdate();
            foreach (var m in _modes) cb.Items.Add(PowerPlan.BoostName(m.Value));
            cb.EndUpdate();
            cb.MaxDropDownItems = Math.Max(8, _modes.Count);
            cb.WrapSelected = true;   // a long Windows name wraps and the field grows; never "..."
            // each mode with the icon of what it really does, its group named at the right of the open list
            cb.ItemIcon = (g, i, r, hot) => { if (i < _modes.Count) GroupIcon(g, _modes[i].Value, r, hot); };
            cb.ItemNote = i => i < _modes.Count ? PowerPlan.GroupLabel(PowerPlan.GroupOf(_modes[i].Value, _autonomous)) : "";
        }
        Controls.AddRange(new Control[] { _turbo, _help, _cbAc, _cbDc, _seg });

        _turbo.Toggled += v => Apply(v ? PowerPlan.TurboOn(_d.Settings) : PowerPlan.TurboOff(_d.Settings), "CPU turbo boost: " + (v ? "on" : "off"));
        _cbAc.SelectedIndexChanged += (_, _) => OnPick(acSide: true);
        _cbDc.SelectedIndexChanged += (_, _) => OnPick(acSide: false);
        _seg.SelectedChanged += OnSegment;
    }

    private int S(float v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);

    private void GroupIcon(Graphics g, uint value, RectangleF r, bool hot = false)
    {
        var grp = PowerPlan.GroupOf(value, _autonomous);
        IconPainter.BoostGroup(g, grp, r, PowerPlan.GroupColor(grp), hot ? Color.White : null);
    }

    private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // ---------------- actions ----------------

    private void Apply(string err, string log)
    {
        if (err.Length > 0) Note(Lang.T("pw_err_write") + "\n" + err);
        else ChangeLog.Add(ChangeSource.Panel, log);
        Sync();
    }

    /// <summary>An acknowledge-only card in the app's own style (never MessageBox).</summary>
    private static void Note(string body)
    {
        var dlg = new GhostCardForm("//WIN-POWER", Lang.T("pw_grp"), body, "OK", "", () => { });
        dlg.Show(); dlg.Activate();
    }

    private static void Ask(string heading, string body, string ack, Action onAck)
    {
        var dlg = new GhostCardForm("//WIN-POWER", heading, body, ack, Lang.T("fw_dlg_later"), onAck);
        dlg.Show(); dlg.Activate();
    }

    /// <summary>(#117) A mode picked in one of the lists; picking one while turbo is off turns that source on.</summary>
    private void OnPick(bool acSide)
    {
        if (_syncing) return;
        var cb = acSide ? _cbAc : _cbDc;
        if (cb.SelectedIndex < 0 || cb.SelectedIndex >= _modes.Count) return;
        var m = _modes[cb.SelectedIndex];
        int v = (int)m.Value;
        Apply(PowerPlan.TurboSetModes(_d.Settings, acSide ? v : -1, acSide ? -1 : v, out _),
              $"CPU turbo boost ({(acSide ? "plugged in" : "battery")}): {PowerPlan.BoostName(m.Value)}");
    }

    private void OnSegment(int i)
    {
        var s = _d.Settings;
        PowerPlan.RememberModeIfFirst(s);   // so Restore can bring the user's own mode back
        if (i == 3)
        {
            // Auto: follow the GhostDeck profile from now on, and apply it right away
            s.PowerModeSync = true; s.Save();
            Apply(PowerPlan.TrySetPowerMode(PowerPlan.ModeForProfile(s, _d.Current())) ? "" : "Windows refused the power-mode write",
                  "Windows power mode: follow the profile");
        }
        else
        {
            // a manual pick always wins: it switches Auto off (nothing fights the user)
            if (s.PowerModeSync) { s.PowerModeSync = false; s.Save(); }
            Apply(PowerPlan.TrySetPowerMode(PowerPlan.GroupMode(i)) ? "" : "Windows refused the power-mode write",
                  "Windows power mode: " + (i == 0 ? "best efficiency" : i == 2 ? "best performance" : "balanced"));
        }
    }

    private void OpenHelp()
    {
        if (_helpDlg is { IsDisposed: false }) { _helpDlg.Activate(); return; }   // one help card at a time
        _helpDlg = new WinPowerHelpForm();
        _helpDlg.Show(); _helpDlg.Activate();
    }

    private void EditMapping()
    {
        if (_mapDlg is { IsDisposed: false }) { _mapDlg.Activate(); return; }   // one editor at a time
        _mapDlg = new PowerMapForm(_d.Settings, _d.ColorOf, () =>
        {
            ChangeLog.Add(ChangeSource.Panel, "Windows power: profile mapping edited");
            ReapplyAuto();
            Sync();
        }, _d.Current());
        _mapDlg.Show(); _mapDlg.Activate();
    }

    private void ResetMapping() => Ask(Lang.T("pw_mapping"), Lang.T("pw_map_reset_confirm"), Lang.T("pw_restore_ack"), () =>
    {
        _d.Settings.PowerModeMap.Clear(); _d.Settings.TurboMap.Clear(); _d.Settings.Save();
        ChangeLog.Add(ChangeSource.Panel, "Windows power: default profile mapping restored");
        ReapplyAuto();
        Sync();
    });

    /// <summary>A changed mapping takes effect at once: the mode while Auto drives it, the turbo modes the current profile sets.</summary>
    private void ReapplyAuto()
    {
        if (_d.Settings.PowerModeSync) PowerPlan.TrySetPowerMode(PowerPlan.ModeForProfile(_d.Settings, _d.Current()));
        PowerPlan.ApplyProfileTurbo(_d.Settings, _d.Current());
    }

    private void Restore() => Ask(Lang.T("pw_restore_btn"), Lang.T("pw_restore_confirm") + "\n\n" + string.Join("\n", _restoreLines), Lang.T("pw_restore_ack"), () =>
    {
        var s = _d.Settings;
        var (ok, missing, failed) = PowerPlan.RestoreAll(s);
        if (s.PowerModeSync) { s.PowerModeSync = false; s.Save(); }
        // the mode GhostDeck changed goes back too; the memory is consumed only on success
        if (s.PowerModePrev.Length > 0 && Guid.TryParse(s.PowerModePrev, out var prev))
        {
            if (PowerPlan.TrySetPowerMode(prev)) { s.PowerModePrev = ""; s.Save(); }
            else failed++;
        }
        ChangeLog.Add(ChangeSource.Panel, $"Windows power restore: {ok} restored, {missing} plans gone, {failed} failed");
        // a clean restore shows itself (the block disappears); a card only when something needs attention
        if (missing > 0 || failed > 0) Note(string.Format(Lang.T("pw_restore_result_fmt"), ok, missing, failed));
        Sync();
    });

    private void ToggleReveal()
    {
        bool hidden = PowerPlan.HiddenInControlPanel();
        Ask(Lang.T("pw_grp"), Lang.T(hidden ? "pw_show_confirm" : "pw_hide_confirm"), Lang.T(hidden ? "pw_show_ack" : "pw_hide_ack"), () =>
        {
            if (!PowerPlan.SetRevealed(_d.Settings, hidden)) Note(Lang.T("pw_err_write"));
            else ChangeLog.Add(ChangeSource.Panel, "PERFBOOSTMODE " + (hidden ? "revealed in" : "re-hidden from") + " Windows power options");
            Sync();
        });
    }

    // ---------------- model ----------------

    /// <summary>Re-read the live Windows state. Cheap: boost names are cached in PowerPlan.</summary>
    public void Sync()
    {
        if (IsDisposed) return;
        if (BackColor != Theme.Card) BackColor = Theme.Card;   // children clear to Parent.BackColor; follow a theme switch
        var s = _d.Settings;
        bool haveScheme = PowerPlan.TryGetActiveScheme(out var scheme);
        bool autonomous = PowerPlan.AutonomousOn(haveScheme ? scheme : null);
        if (autonomous != _autonomous) { _autonomous = autonomous; _cbAc.Invalidate(); _cbDc.Invalidate(); }   // the groups of 2 and 4 follow it
        _plan = haveScheme ? PowerPlan.SchemeName(scheme) : "-";
        _haveBoost = haveScheme && PowerPlan.TryReadBoost(scheme, out _ac, out _dc);
        _snap = haveScheme && s.TurboSnapshots.TryGetValue(scheme.ToString("D").ToLowerInvariant(), out var sn) && sn is { Length: 2 } ? sn : null;

        _turbo.Enabled = _cbAc.Enabled = _cbDc.Enabled = _haveBoost && _modes.Count > 0;
        _syncing = true;
        try
        {
            _cbAc.SelectedIndex = _haveBoost ? _modes.FindIndex(m => m.Value == _ac) : -1;
            _cbDc.SelectedIndex = _haveBoost ? _modes.FindIndex(m => m.Value == _dc) : -1;
        }
        finally { _syncing = false; }
        if (_haveBoost)
        {
            string an = PowerPlan.BoostName(_ac), dn = PowerPlan.BoostName(_dc);
            _turbo.Checked = !(_ac == 0 && _dc == 0);   // the Checked setter never fires Toggled
            if (_ac == 0 && _dc == 0)
            {
                // two honest OFF texts: "comes back" ONLY when a snapshot really exists
                if (_snap != null) { _statusText = string.Format(Lang.T("pw_turbo_off_snap_fmt"), PowerPlan.BoostName((uint)_snap[0]), PowerPlan.BoostName((uint)_snap[1])); _statusKind = 1; }
                else { _statusText = string.Format(Lang.T("pw_turbo_off_nosnap_fmt"), PowerPlan.FallbackBoost() is { } fbm ? PowerPlan.BoostName(fbm.Value) : "-"); _statusKind = 1; }
            }
            else if (_ac != 0 && _dc != 0) { _statusText = string.Format(Lang.T("pw_turbo_on_fmt"), an, dn); _statusKind = 0; }
            else { _statusText = string.Format(Lang.T("pw_turbo_mixed_fmt"), an, dn); _statusKind = 1; }   // a real MIXED state, named
        }
        else { _statusText = Lang.T("pw_unavailable"); _statusKind = 0; }

        // segment: Auto wins; otherwise the user-configured mode when both sources agree
        _auto = s.PowerModeSync;
        var prof = _d.Current();
        bool haveMode = PowerPlan.TryGetUserPowerMode(out var mAc, out var mDc);
        int userGroup = haveMode && mAc == mDc ? PowerPlan.ModeGroup(mAc) : -1;
        _seg.Selected = _auto ? 3 : userGroup;
        _autoLine = _auto
            ? string.Format(Lang.T("pw_auto_fmt"), prof == ProfileId.SuperBattery ? "Super Battery" : prof.ToString(), Lang.T(ModeKeys[PowerPlan.ModeGroupFor(s, prof)]))
            : "";
        // the override note: ONLY on a real cross-group mismatch (the effective enum is richer
        // than the three requested modes, so groups are compared, never names)
        int reqGroup = _auto ? PowerPlan.ModeGroupFor(s, prof) : userGroup;
        int effGroup = PowerPlan.EffectiveGroup(PowerPlan.EffectiveMode);
        string effKey = PowerPlan.EffectiveKey(PowerPlan.EffectiveMode);
        _overrideLine = reqGroup >= 0 && effGroup >= 0 && reqGroup != effGroup && effKey.Length > 0
            ? string.Format(Lang.T("pw_override_fmt"), Lang.T(effKey)) : "";

        _restoreLines.Clear();
        foreach (var (g2, v) in PowerPlan.RestoreTargets(s))
            _restoreLines.Add(string.Format(Lang.T("pw_restore_turbo_fmt"), PowerPlan.SchemeName(g2), PowerPlan.BoostName((uint)v[0]), PowerPlan.BoostName((uint)v[1])));
        if (s.PowerModePrev.Length > 0 && Guid.TryParse(s.PowerModePrev, out var pm))
            _restoreLines.Add(string.Format(Lang.T("pw_restore_mode_fmt"), Lang.T(PowerPlan.ModeKey(pm))));
        if (_auto) _restoreLines.Add(Lang.T("pw_restore_auto"));

        _hidden = PowerPlan.HiddenInControlPanel();
        Relayout(notify: true);
        Invalidate();
    }

    // ---------------- layout + paint (one routine) ----------------

    private void Relayout(bool notify)
    {
        if (_inLayout || Width <= 0) return;
        _inLayout = true;
        try
        {
            int h = Flow(null);
            if (h != Height)
            {
                Height = h;
                if (notify) ContentHeightChanged?.Invoke();
            }
        }
        finally { _inLayout = false; }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Relayout(notify: false);   // width-driven: the host (CardSection) reads Height right after
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Card);
        Flow(e.Graphics);
    }

    // The layout pass measures the same strings at the same widths on every Sync (the mode names,
    // the short lines, the By profile cells); remembering the heights keeps a Sync close to what it
    // cost before the turbo lists (harness, 2026-10-20: layout pass 1.9 ms -> 0.08 ms, Sync 3.0 -> 1.0 ms).
    private static readonly Dictionary<(string, Font, int), int> _textH = new();
    private static int TextH(string text, Font f, int width)
    {
        var key = (text, f, Math.Max(10, width));
        if (_textH.TryGetValue(key, out int h)) return h;
        if (_textH.Count > 400) _textH.Clear();   // widths change on resize; never let it grow without bound
        return _textH[key] = TextRenderer.MeasureText(text, f, new Size(key.Item3, 0), FmtWrap).Height;
    }

    private int _ddwFor = -1, _ddw;   // the drop-down width, computed once per autonomous state and DPI

    /// <summary>Measures (g == null, also positions the child controls) or paints. Returns the height.</summary>
    private int Flow(Graphics? g)
    {
        int W = Width, y = 0;
        using var line = new Pen(Theme.Border);
        if (g != null) g.SmoothingMode = SmoothingMode.AntiAlias;

        // ---- group 1: CPU turbo boost ----
        y = Caption(g, y, Lang.T("pw_turbo_label"), Theme.Accent);
        string turboLabel = Lang.T("pw_turbo_label");
        int rowH = Math.Max(S(38), TextH(turboLabel, FRow, W - S(110)) + S(8));
        if (g != null) Cell(g, turboLabel, FRow, 0, y, W - S(110), rowH, Theme.Text);
        else
        {
            _help.Location = new Point(W - _help.Width, y + (rowH - _help.Height) / 2);
            _turbo.Location = new Point(_help.Left - S(8) - _turbo.Width, y + (rowH - _turbo.Height) / 2);
        }
        y += rowH + S(6);
        y = Strip(g, y, _statusText, _statusKind == 0 ? null : Theme.Amber) + S(8);
        string hint = string.Format(Lang.T("pw_toggle_hint"), PowerPlan.BoostName(0));
        int hintH = TextH(hint, FBody, W);
        if (g != null) TextRenderer.DrawText(g, hint, FBody, new Rectangle(0, y, W, hintH), Theme.Faint, FmtWrap);
        y += hintH + S(18);

        // (#117) turbo mode per source: a list each, the plain description of the pick under it
        int x1 = Math.Max(S(170), (int)(W * 0.36f)), x2 = x1 + (W - x1) / 2, trH = S(44);
        int colA = x2 - x1 - S(14), colB = W - x2;
        y = Sub(g, y, Lang.T("pw_turbo_mode"));
        int thH = ThH((Lang.T("pw_ac"), colA), (Lang.T("pw_dc"), colB));
        if (g != null)
        {
            DrawTh(g, Lang.T("pw_ac"), x1, y, colA, thH);
            DrawTh(g, Lang.T("pw_dc"), x2, y, colB, thH);
            g.DrawLine(line, 0, y + thH, W, y + thH);
        }
        y += thH + S(10);
        // both fields as tall as the taller one needs (a long name takes two lines)
        int cbH = Math.Max(_cbAc.FieldHeightFor(Math.Max(S(60), colA)), _cbDc.FieldHeightFor(Math.Max(S(60), colB)));
        string modeLbl = Lang.T("pw_mode_lbl");
        cbH = Math.Max(cbH, TextH(modeLbl, FBody, x1 - S(10)));
        if (g != null) Cell(g, modeLbl, FBody, 0, y, x1 - S(10), cbH, Theme.Muted);
        else
        {
            _cbAc.SetBounds(x1, y + (cbH - _cbAc.FieldHeightFor(Math.Max(S(60), colA))) / 2, Math.Max(S(60), colA), 0);
            _cbDc.SetBounds(x2, y + (cbH - _cbDc.FieldHeightFor(Math.Max(S(60), colB))) / 2, Math.Max(S(60), colB), 0);
            // the open list fits the longest name plus its icon and the longest group note
            int ddwKey = Dpi.Of(this) * 2 + (_autonomous ? 1 : 0);
            if (_ddwFor != ddwKey)
            {
                int nameMax = 0, noteMax = 0;
                foreach (var m in _modes)
                {
                    nameMax = Math.Max(nameMax, TextRenderer.MeasureText(PowerPlan.BoostName(m.Value), _cbAc.Font).Width);
                    noteMax = Math.Max(noteMax, TextRenderer.MeasureText(PowerPlan.GroupLabel(PowerPlan.GroupOf(m.Value, _autonomous)), _cbAc.Font).Width);
                }
                _ddw = nameMax + noteMax + ThemedComboBox.IconSide(_cbAc.ItemHeight) + S(64);
                _ddwFor = ddwKey;
            }
            int ddw = _ddw;
            _cbAc.DropDownWidth = Math.Max(_cbAc.Width, ddw);
            _cbDc.DropDownWidth = Math.Max(_cbDc.Width, ddw);
        }
        y += cbH + S(8);
        // one short line per list; the same mode on both sources gets one line across both columns
        if (_haveBoost && _ac == _dc)
        {
            string both = string.Format(Lang.T("pw_bmshort_both"), PowerPlan.ShortDescription(_ac, _autonomous));
            int hBoth = TextH(both, FBody, W - x1);
            if (g != null) TextRenderer.DrawText(g, both, FBody, new Rectangle(x1, y, W - x1, hBoth), Theme.Muted, FmtWrap);
            y += hBoth + S(22);
        }
        else
        {
            string dA = _haveBoost ? PowerPlan.ShortDescription(_ac, _autonomous) : "", dB = _haveBoost ? PowerPlan.ShortDescription(_dc, _autonomous) : "";
            int hA = dA.Length > 0 ? TextH(dA, FBody, colA) : 0, hB = dB.Length > 0 ? TextH(dB, FBody, colB) : 0;
            if (g != null)
            {
                if (hA > 0) TextRenderer.DrawText(g, dA, FBody, new Rectangle(x1, y, colA, hA), Theme.Muted, FmtWrap);
                if (hB > 0) TextRenderer.DrawText(g, dB, FBody, new Rectangle(x2, y, colB, hB), Theme.Muted, FmtWrap);
            }
            y += Math.Max(hA, hB) + S(22);
        }

        // technical details: an open table, rows separated by hairlines
        y = Sub(g, y, Lang.T("pw_details"));
        string planTh = string.Format(Lang.T("pw_plan_fmt"), _plan);
        thH = ThH((planTh, x1 - S(10)), (Lang.T("pw_ac"), x2 - x1 - S(8)), (Lang.T("pw_dc"), W - x2));
        if (g != null)
        {
            DrawTh(g, planTh, 0, y, x1 - S(10), thH);
            DrawTh(g, Lang.T("pw_ac"), x1, y, x2 - x1 - S(8), thH);
            DrawTh(g, Lang.T("pw_dc"), x2, y, W - x2, thH);
            g.DrawLine(line, 0, y + thH, W, y + thH);
        }
        y += thH + 1;
        string none = Lang.T("pw_none");
        var rows = new (string Label, string A, string B, Color Col)[]
        {
            (Lang.T("pw_now"), _haveBoost ? PowerPlan.BoostName(_ac) : "-", _haveBoost ? PowerPlan.BoostName(_dc) : "-", Theme.Text),
            (Lang.T("pw_saved"), _snap != null ? PowerPlan.BoostName((uint)_snap[0]) : none, _snap != null ? PowerPlan.BoostName((uint)_snap[1]) : none, _snap != null ? Theme.Amber : Theme.Faint),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            // a long Windows name ("Wydajność: agresywnie - gwarantowane (6)") wraps; the row grows
            int rh = Math.Max(trH, Math.Max(TextH(r.Label, FBody, x1 - S(10)),
                     Math.Max(TextH(r.A, FBody, x2 - x1 - S(8)), TextH(r.B, FBody, W - x2))) + S(16));
            if (g != null)
            {
                Cell(g, r.Label, FBody, 0, y, x1 - S(10), rh, Theme.Muted);
                Cell(g, r.A, FBody, x1, y, x2 - x1 - S(8), rh, r.Col);
                Cell(g, r.B, FBody, x2, y, W - x2, rh, r.Col);
                if (i < rows.Length - 1) g.DrawLine(line, 0, y + rh, W, y + rh);
            }
            y += rh + 1;
        }

        // ---- group 2: Windows power mode (extra air above - the owner's F1 note) ----
        y += S(30);
        y = Caption(g, y, Lang.T("pw_grp_mode"), Theme.AccentFill);
        if (g == null) _seg.SetBounds(0, y + S(2), W, S(40));
        y += S(2) + S(40) + S(12);
        if (_autoLine.Length > 0)
        {
            int h = TextH(_autoLine, FBody, W);
            if (g != null) TextRenderer.DrawText(g, _autoLine, FBody, new Rectangle(0, y, W, h), Theme.Muted, FmtWrap);
            y += h + S(10);
        }
        if (_overrideLine.Length > 0) y = Strip(g, y, _overrideLine, Theme.Amber) + S(10);

        // ---- group 3: by profile (#36 mode + #117 turbo), the edit and reset glyphs on the caption line ----
        y += S(22);
        int gb = S(28);
        if (g == null)
        {
            _rReset = new Rectangle(W - gb, y + (S(22) - gb) / 2, gb, gb);
            _rEdit = new Rectangle(W - gb * 2 - S(4), _rReset.Y, gb, gb);
        }
        else
        {
            DrawGlyph(g, "", _rEdit, _hot == 0);    // MDL2 Edit
            DrawGlyph(g, "", _rReset, _hot == 1);   // MDL2 UpdateRestore
        }
        y = Caption(g, y, Lang.T("pw_grp_profile"), Theme.Violet, W - gb * 2 - S(16));
        int ib = S(20), nameW = 0;
        foreach (var r in MapRows) nameW = Math.Max(nameW, TextRenderer.MeasureText(r.Name, FBodyB).Width);
        int nx = ib + S(12), c0 = nx + nameW + S(18), cw = Math.Max(S(40), (W - c0) / 3);
        int[] cx = { c0, c0 + cw, c0 + cw * 2 };
        thH = ThH((Lang.T("pw_map_profile"), c0 - S(8)), (Lang.T("pw_mode_now"), cw - S(10)), (Lang.T("pw_map_turbo_ac"), cw - S(10)), (Lang.T("pw_map_turbo_dc"), W - cx[2]));
        if (g != null)
        {
            DrawTh(g, Lang.T("pw_map_profile"), 0, y, c0 - S(8), thH);
            DrawTh(g, Lang.T("pw_mode_now"), cx[0], y, cw - S(10), thH);
            DrawTh(g, Lang.T("pw_map_turbo_ac"), cx[1], y, cw - S(10), thH);
            DrawTh(g, Lang.T("pw_map_turbo_dc"), cx[2], y, W - cx[2], thH);
            g.DrawLine(line, 0, y + thH, W, y + thH);
        }
        y += thH + 1;
        string unchanged = Lang.T("pw_unchanged");
        for (int i = 0; i < MapRows.Length; i++)
        {
            var (id, name) = MapRows[i];
            var (ta, td) = PowerPlan.TurboFor(_d.Settings, id);
            var cells = new (string Text, int Mode)[]
            {
                (Lang.T(ModeKeys[PowerPlan.ModeGroupFor(_d.Settings, id)]), -2),   // -2 = not a turbo cell
                (ta >= 0 ? PowerPlan.BoostName((uint)ta) : unchanged, ta),
                (td >= 0 ? PowerPlan.BoostName((uint)td) : unchanged, td),
            };
            // a set turbo mode carries its group icon; long Windows mode names wrap to a second line
            int gi = S(18), gGap = S(8);
            int CellW(int j) => (j < 2 ? cw - S(10) : W - cx[2]) - (cells[j].Mode >= 0 ? gi + gGap : 0);
            int rh = S(42);
            for (int j = 0; j < 3; j++) rh = Math.Max(rh, TextH(cells[j].Text, FBody, CellW(j)) + S(16));
            if (g != null)
            {
                IconPainter.Scenario(g, id, new RectangleF(0, y + (rh - ib) / 2f, ib, ib), _d.ColorOf(id), 1.7f * Dpi.Of(this) / 96f);
                Cell(g, name, FBodyB, nx, y, nameW + S(8), rh, Theme.Text);
                for (int j = 0; j < 3; j++)
                {
                    int w = CellW(j), tx = cx[j];
                    if (cells[j].Mode >= 0)
                    {
                        GroupIcon(g, (uint)cells[j].Mode, new RectangleF(tx, y + (rh - gi) / 2f, gi, gi));
                        tx += gi + gGap;
                    }
                    int th = TextH(cells[j].Text, FBody, w);
                    TextRenderer.DrawText(g, cells[j].Text, FBody, new Rectangle(tx, y + (rh - th) / 2, w, th),
                        cells[j].Mode != -1 ? Theme.Text : Theme.Faint, FmtWrap);
                }
                if (i < MapRows.Length - 1) g.DrawLine(line, 0, y + rh, W, y + rh);
            }
            y += rh + 1;
        }
        string note = string.Format(Lang.T("pw_map_note"), Lang.T("pw_auto_seg"), unchanged);
        int noteH = TextH(note, FBody, W);
        y += S(8);
        if (g != null) TextRenderer.DrawText(g, note, FBody, new Rectangle(0, y, W, noteH), Theme.Faint, FmtWrap);
        y += noteH;

        // ---- restore (exists only while something is held) ----
        y += S(14);
        if (g != null) g.DrawLine(line, 0, y, W, y);
        y += S(18);
        if (_restoreLines.Count > 0)
        {
            string btn = Lang.T("pw_restore_ack");
            int bw = TextRenderer.MeasureText(btn, FBodyB).Width + S(40), bh = S(36);
            int padX = S(18), padY = S(14);
            int textW = W - padX * 2 - bw - S(16);
            int titleH = TextH(Lang.T("pw_restore_btn"), FTitle, textW);
            string body = string.Join("\n", _restoreLines);
            int bodyH = TextH(body, FBody, textW);
            int blockH = padY * 2 + titleH + S(6) + bodyH;
            var block = new Rectangle(0, y, W, blockH);
            if (g == null) _rRestore = new Rectangle(W - padX - bw, y + (blockH - bh) / 2, bw, bh);
            else
            {
                using (var bb = new SolidBrush(Mix(Theme.Card, Theme.Amber, 0.08f))) g.FillRectangle(bb, block);
                using (var rb = new SolidBrush(Theme.Amber)) g.FillRectangle(rb, 0, y, 3, blockH);
                TextRenderer.DrawText(g, Lang.T("pw_restore_btn"), FTitle, new Rectangle(padX, y + padY, textW, titleH), Theme.Text, FmtWrap);
                TextRenderer.DrawText(g, body, FBody, new Rectangle(padX, y + padY + titleH + S(6), textW, bodyH), Theme.Amber, FmtWrap);
                using var bp = Theme.RoundRect(new RectangleF(_rRestore.X + 0.5f, _rRestore.Y + 0.5f, _rRestore.Width - 1, _rRestore.Height - 1), 8);
                if (_hot == 2) { using var hb = new SolidBrush(Mix(Theme.Card, Theme.Amber, 0.22f)); g.FillPath(hb, bp); }
                using var pen = new Pen(Theme.Amber);
                g.DrawPath(pen, bp);
                TextRenderer.DrawText(g, btn, FBodyB, _rRestore, Theme.Amber, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            y += blockH + S(18);
        }
        else if (g == null) _rRestore = Rectangle.Empty;

        // ---- footer: the hidden Windows setting, with its live show/hide link ----
        string foot = Lang.T("pw_footer");
        int fh = TextH(foot, FBody, W);
        if (g != null) TextRenderer.DrawText(g, foot, FBody, new Rectangle(0, y, W, fh), Theme.Faint, FmtWrap);
        y += fh + S(6);
        string link = Lang.T(_hidden ? "pw_show_link" : "pw_hide_link");
        var ls = TextRenderer.MeasureText(link, FBody, Size.Empty, TextFormatFlags.NoPadding);
        if (g == null) _rLink = new Rectangle(0, y, ls.Width, ls.Height + 2);
        else
        {
            TextRenderer.DrawText(g, link, FBody, new Point(0, y), _hot == 3 ? Theme.Text : Theme.Accent, TextFormatFlags.NoPadding);
            using var up = new Pen(_hot == 3 ? Theme.Text : Theme.Accent);
            g.DrawLine(up, 0, y + ls.Height, ls.Width, y + ls.Height);
        }
        y += ls.Height + S(4);
        return y;
    }

    /// <summary>Group caption: colored marker + caption in the SAME colour + a hairline to the edge (or to <paramref name="lineEnd"/>).</summary>
    private int Caption(Graphics? g, int y, string text, Color color, int lineEnd = -1)
    {
        int h = S(22);
        if (g != null)
        {
            string t = text.ToUpperInvariant();
            using var rb = new SolidBrush(color);
            g.FillRectangle(rb, 0, y + (h - S(14)) / 2, 3, S(14));
            var ts = TextRenderer.MeasureText(t, FCap, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, t, FCap, new Point(S(11), y + (h - ts.Height) / 2), color, TextFormatFlags.NoPadding);
            using var lp = new Pen(Theme.Border);
            int lx = S(11) + ts.Width + S(12);
            int le = lineEnd < 0 ? Width : lineEnd;
            if (lx < le - 4) g.DrawLine(lp, lx, y + h / 2, le, y + h / 2);
        }
        return y + h + S(10);
    }

    /// <summary>Small-caps section caption inside a group; a long one wraps.</summary>
    private int Sub(Graphics? g, int y, string text)
    {
        string t = text.ToUpperInvariant();
        int h = Math.Max(S(20), TextH(t, FSub, Width - S(70)));
        if (g != null) Cell(g, t, FSub, 0, y, Width - S(70), h, Theme.Faint);
        return y + h + S(8);
    }

    /// <summary>Height of a table header row: one line, or what its longest caption needs to wrap.</summary>
    private int ThH(params (string Text, int Width)[] cells)
    {
        int h = S(28);
        foreach (var (text, w) in cells) h = Math.Max(h, TextH(text.ToUpperInvariant(), FSub, w) + S(10));
        return h;
    }

    private void DrawTh(Graphics g, string text, int x, int y, int w, int h) =>
        Cell(g, text.ToUpperInvariant(), FSub, x, y, w, h, Theme.Faint);

    /// <summary>
    /// A text in a cell: wrapped, never cut and never ending in "...", centred in the row height
    /// (the caller measured the row with <see cref="TextH"/> so it fits).
    /// </summary>
    private static void Cell(Graphics g, string text, Font f, int x, int y, int w, int rowH, Color c)
    {
        int h = TextH(text, f, w);
        TextRenderer.DrawText(g, text, f, new Rectangle(x, y + Math.Max(0, (rowH - h) / 2), Math.Max(10, w), h), c, FmtWrap);
    }

    /// <summary>
    /// Status sentence. Plain = muted text; with a colour = a tinted strip with a 3 px rail,
    /// the "!" chip and generous padding (the owner asked for more air in the yellow notes).
    /// </summary>
    private int Strip(Graphics? g, int y, string text, Color? color)
    {
        if (color is not { } c)
        {
            int ph = TextH(text, FBody, Width);
            if (g != null) TextRenderer.DrawText(g, text, FBody, new Rectangle(0, y, Width, ph), Theme.Muted, FmtWrap);
            return y + ph;
        }
        int padY = S(14), chip = S(18), tx = S(18) + chip + S(12);
        int th = TextH(text, FBody, Width - tx - S(18));
        int h = Math.Max(th, chip) + padY * 2;
        if (g != null)
        {
            using (var bb = new SolidBrush(Mix(Theme.Card, c, 0.10f))) g.FillRectangle(bb, 0, y, Width, h);
            using (var rb = new SolidBrush(c)) g.FillRectangle(rb, 0, y, 3, h);
            var cr = new Rectangle(S(18), y + (h - chip) / 2, chip, chip);
            using (var cp = Theme.RoundRect(new RectangleF(cr.X + 0.5f, cr.Y + 0.5f, cr.Width - 1, cr.Height - 1), 5))
            using (var pen = new Pen(c, 1.4f)) g.DrawPath(pen, cp);
            TextRenderer.DrawText(g, "!", FBodyB, cr, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, FBody, new Rectangle(tx, y + (h - th) / 2, Width - tx - S(18), th), c, FmtWrap);
        }
        return y + h;
    }

    private void DrawGlyph(Graphics g, string glyph, Rectangle r, bool hot)
    {
        if (hot)
        {
            using var hb = new SolidBrush(Theme.AccentSoft);
            using var hp = Theme.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), 6);
            g.FillPath(hb, hp);
        }
        TextRenderer.DrawText(g, glyph, FGlyph, r, hot ? Theme.Accent : Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // ---------------- mouse ----------------

    private int HitTest(Point p) =>
        _rEdit.Contains(p) ? 0 : _rReset.Contains(p) ? 1 : _rRestore.Contains(p) ? 2 : _rLink.Contains(p) ? 3 : -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hot = HitTest(e.Location);
        if (hot == _hot) return;
        _hot = hot;
        Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
        _tip.SetToolTip(this, hot == 0 ? Lang.T("pw_map_edit_tip") : hot == 1 ? Lang.T("pw_map_reset_tip") : "");
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hot == -1) return;
        _hot = -1; Cursor = Cursors.Default; Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        switch (HitTest(e.Location))
        {
            case 0: EditMapping(); break;
            case 1: ResetMapping(); break;
            case 2: Restore(); break;
            case 3: ToggleReveal(); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
