using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Body of the "Windows power" settings card (discussion #141; roadmap #109 + #36): the CPU
/// turbo-boost switch of the active power plan and the Windows power mode.
///
/// ONE double-buffered, owner-drawn control. The first builds composed this card from ~35
/// nested AutoSize containers (FlowLayoutPanel / TableLayoutPanel); every layout pass made
/// them re-measure each other and repaint one by one, which showed as the tab drawing itself
/// element by element for about a second. Here a single <see cref="Flow"/> routine owns the
/// whole geometry - it measures when called without a Graphics and paints when called with
/// one, so layout and paint cannot disagree - and only the genuinely interactive pieces
/// (three ToggleSwitches, the SegControl, the HelpDot) are real child controls. Hotspots
/// (mapping edit/reset, restore, the reveal link) are painted and hit-tested.
///
/// Layout = the owner-approved flat design (2026-10-04, variant "Lines"): no filled frames,
/// no expanders, everything open; section rows separated by hairlines; the two groups set
/// apart by extra space; profile rows carry the scenario-tile icon in the profile colour.
/// </summary>
public sealed class WinPowerBody : Control
{
    private readonly MainDeps _d;
    private readonly ToggleSwitch _turbo = new(), _srcAc = new(), _srcDc = new();
    private readonly HelpDot _help;
    private readonly SegControl _seg;
    private readonly ToolTip _tip = new();

    /// <summary>Raised when a state change (not a width change) altered the height.</summary>
    public event Action? ContentHeightChanged;

    // ---- model, refreshed by Sync() ----
    private string _plan = "—";
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

    private static readonly (ProfileId Id, string Name)[] MapRows =
    {
        (ProfileId.SuperBattery, "Super Battery"), (ProfileId.Silent, "Silent"),
        (ProfileId.Balanced, "Balanced"), (ProfileId.Extreme, "Extreme"),
    };
    private static readonly string[] ModeKeys = { "pwm_req_eff", "pwm_req_bal", "pwm_req_perf" };

    private static readonly Font FCap = new("Segoe UI", 8.25f, FontStyle.Bold);
    private static readonly Font FRow = new("Segoe UI Semibold", 11f);
    private static readonly Font FBody = new("Segoe UI", 10f);
    private static readonly Font FBodyB = new("Segoe UI Semibold", 10f);
    private static readonly Font FSub = new("Segoe UI", 8f, FontStyle.Bold);
    private static readonly Font FTitle = new("Segoe UI Semibold", 10.5f);
    private static readonly Font FGlyph = new("Segoe MDL2 Assets", 11f);

    private const TextFormatFlags FmtLeft = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
    private const TextFormatFlags FmtWrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

    public WinPowerBody(MainDeps d)
    {
        _d = d;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;

        _help = new HelpDot { TextProvider = () => Lang.T("pw_turbo_help") };
        _seg = new SegControl(new[] { Lang.T("pw_seg_eff"), Lang.T("pw_seg_bal"), Lang.T("pw_seg_perf"), Lang.T("pw_auto_seg") }, 1);
        _seg.MinimumSize = Size.Empty;   // the body sizes it; SegControl ellipsizes if a language outgrows a cell
        Controls.AddRange(new Control[] { _turbo, _help, _srcAc, _srcDc, _seg });

        _turbo.Toggled += v => Apply(v ? PowerPlan.TurboOn(_d.Settings) : PowerPlan.TurboOff(_d.Settings), "CPU turbo boost: " + (v ? "on" : "off"));
        _srcAc.Toggled += v => Apply(PowerPlan.TurboSetSource(_d.Settings, acSide: true, on: v), "CPU turbo boost (plugged in): " + (v ? "on" : "off"));
        _srcDc.Toggled += v => Apply(PowerPlan.TurboSetSource(_d.Settings, acSide: false, on: v), "CPU turbo boost (battery): " + (v ? "on" : "off"));
        _seg.SelectedChanged += OnSegment;
    }

    private int S(float v) => (int)Math.Ceiling(v * DeviceDpi / 96f);

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

    private void EditMapping()
    {
        if (_mapDlg is { IsDisposed: false }) { _mapDlg.Activate(); return; }   // one editor at a time
        _mapDlg = new PowerMapForm(_d.Settings, _d.ColorOf, () =>
        {
            ChangeLog.Add(ChangeSource.Panel, "Windows power mode: profile mapping edited");
            ReapplyAuto();
            Sync();
        });
        _mapDlg.Show(); _mapDlg.Activate();
    }

    private void ResetMapping() => Ask(Lang.T("pw_mapping"), Lang.T("pw_map_reset_confirm"), Lang.T("pw_restore_ack"), () =>
    {
        _d.Settings.PowerModeMap.Clear(); _d.Settings.Save();
        ChangeLog.Add(ChangeSource.Panel, "Windows power mode: default profile mapping restored");
        ReapplyAuto();
        Sync();
    });

    /// <summary>A changed mapping takes effect at once while Auto drives the mode.</summary>
    private void ReapplyAuto()
    {
        if (_d.Settings.PowerModeSync) PowerPlan.TrySetPowerMode(PowerPlan.ModeForProfile(_d.Settings, _d.Current()));
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
        _plan = haveScheme ? PowerPlan.SchemeName(scheme) : "—";
        _haveBoost = haveScheme && PowerPlan.TryReadBoost(scheme, out _ac, out _dc);
        _snap = haveScheme && s.TurboSnapshots.TryGetValue(scheme.ToString("D").ToLowerInvariant(), out var sn) && sn is { Length: 2 } ? sn : null;

        _turbo.Enabled = _srcAc.Enabled = _srcDc.Enabled = _haveBoost;
        if (_haveBoost)
        {
            string an = PowerPlan.BoostName(_ac), dn = PowerPlan.BoostName(_dc);
            _turbo.Checked = !(_ac == 0 && _dc == 0);   // the Checked setter never fires Toggled
            _srcAc.Checked = _ac != 0; _srcDc.Checked = _dc != 0;
            if (_ac == 0 && _dc == 0)
            {
                // two honest OFF texts: "comes back" ONLY when a snapshot really exists
                if (_snap != null) { _statusText = string.Format(Lang.T("pw_turbo_off_snap_fmt"), PowerPlan.BoostName((uint)_snap[0]), PowerPlan.BoostName((uint)_snap[1])); _statusKind = 1; }
                else { _statusText = string.Format(Lang.T("pw_turbo_off_nosnap_fmt"), PowerPlan.FallbackBoost()?.Name ?? "—"); _statusKind = 1; }
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
        foreach (var (key, v) in s.TurboSnapshots)
            if (v is { Length: 2 } && Guid.TryParse(key, out var g2))
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

    private static int TextH(string text, Font f, int width) =>
        TextRenderer.MeasureText(text, f, new Size(Math.Max(10, width), 0), FmtWrap).Height;

    /// <summary>Measures (g == null, also positions the child controls) or paints. Returns the height.</summary>
    private int Flow(Graphics? g)
    {
        int W = Width, y = 0;
        using var line = new Pen(Theme.Border);
        if (g != null) g.SmoothingMode = SmoothingMode.AntiAlias;

        // ---- group 1: CPU turbo boost ----
        y = Caption(g, y, Lang.T("pw_turbo_label"), Theme.Accent);
        int rowH = S(38);
        if (g != null) TextRenderer.DrawText(g, Lang.T("pw_turbo_label"), FRow, new Rectangle(0, y, W - S(110), rowH), Theme.Text, FmtLeft);
        else
        {
            _help.Location = new Point(W - _help.Width, y + (rowH - _help.Height) / 2);
            _turbo.Location = new Point(_help.Left - S(8) - _turbo.Width, y + (rowH - _turbo.Height) / 2);
        }
        y += rowH + S(6);
        y = Strip(g, y, _statusText, _statusKind == 0 ? null : Theme.Amber) + S(18);

        // technical details: an open table, rows separated by hairlines
        y = Sub(g, y, Lang.T("pw_details"));
        int x1 = Math.Max(S(170), (int)(W * 0.36f)), x2 = x1 + (W - x1) / 2, thH = S(28), trH = S(44);
        if (g != null)
        {
            DrawTh(g, string.Format(Lang.T("pw_plan_fmt"), _plan), 0, y, x1 - S(10), thH);
            DrawTh(g, Lang.T("pw_ac"), x1, y, x2 - x1, thH);
            DrawTh(g, Lang.T("pw_dc"), x2, y, W - x2, thH);
            g.DrawLine(line, 0, y + thH, W, y + thH);
        }
        y += thH + 1;
        string none = Lang.T("pw_none");
        var rows = new (string Label, string A, string B, Color Col)[]
        {
            (Lang.T("pw_now"), _haveBoost ? PowerPlan.BoostName(_ac) : "—", _haveBoost ? PowerPlan.BoostName(_dc) : "—", Theme.Text),
            (Lang.T("pw_saved"), _snap != null ? PowerPlan.BoostName((uint)_snap[0]) : none, _snap != null ? PowerPlan.BoostName((uint)_snap[1]) : none, _snap != null ? Theme.Amber : Theme.Faint),
        };
        foreach (var r in rows)
        {
            if (g != null)
            {
                TextRenderer.DrawText(g, r.Label, FBody, new Rectangle(0, y, x1 - S(10), trH), Theme.Muted, FmtLeft);
                TextRenderer.DrawText(g, r.A, FBody, new Rectangle(x1, y, x2 - x1 - S(8), trH), r.Col, FmtLeft);
                TextRenderer.DrawText(g, r.B, FBody, new Rectangle(x2, y, W - x2, trH), r.Col, FmtLeft);
                g.DrawLine(line, 0, y + trH, W, y + trH);
            }
            y += trH + 1;
        }
        if (g != null) TextRenderer.DrawText(g, Lang.T("pw_per_source"), FBody, new Rectangle(0, y, x1 - S(10), trH), Theme.Muted, FmtLeft);
        else
        {
            _srcAc.Location = new Point(x1, y + (trH - _srcAc.Height) / 2);
            _srcDc.Location = new Point(x2, y + (trH - _srcDc.Height) / 2);
        }
        y += trH;

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

        // profile mapping: caption line with two glyph hotspots on the right, then icon rows
        y += S(8);
        int gb = S(28);
        if (g == null)
        {
            _rReset = new Rectangle(W - gb, y - S(4), gb, gb);
            _rEdit = new Rectangle(W - gb * 2 - S(4), y - S(4), gb, gb);
        }
        else
        {
            DrawGlyph(g, "", _rEdit, _hot == 0);    // MDL2 Edit
            DrawGlyph(g, "", _rReset, _hot == 1);   // MDL2 UpdateRestore
        }
        y = Sub(g, y, Lang.T("pw_mapping"));
        int mapH = S(42), ib = S(20), nameW = 0;
        foreach (var r in MapRows) nameW = Math.Max(nameW, TextRenderer.MeasureText(r.Name, FBodyB).Width);
        int nx = ib + S(14), mx = nx + nameW + S(30);
        for (int i = 0; i < MapRows.Length; i++)
        {
            if (g != null)
            {
                var (id, name) = MapRows[i];
                IconPainter.Scenario(g, id, new RectangleF(0, y + (mapH - ib) / 2f, ib, ib), _d.ColorOf(id), 1.7f * DeviceDpi / 96f);
                TextRenderer.DrawText(g, name, FBodyB, new Rectangle(nx, y, nameW + S(8), mapH), Theme.Text, FmtLeft);
                TextRenderer.DrawText(g, Lang.T(ModeKeys[PowerPlan.ModeGroupFor(_d.Settings, id)]), FBody, new Rectangle(mx, y, Math.Max(10, W - mx), mapH), Theme.Muted, FmtLeft);
                if (i < MapRows.Length - 1) g.DrawLine(line, 0, y + mapH, W, y + mapH);
            }
            y += mapH + 1;
        }

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

    /// <summary>Group caption: colored marker + caption in the SAME colour + a hairline to the edge.</summary>
    private int Caption(Graphics? g, int y, string text, Color color)
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
            if (lx < Width - 4) g.DrawLine(lp, lx, y + h / 2, Width, y + h / 2);
        }
        return y + h + S(10);
    }

    /// <summary>Small-caps section caption inside a group.</summary>
    private int Sub(Graphics? g, int y, string text)
    {
        int h = S(20);
        if (g != null) TextRenderer.DrawText(g, text.ToUpperInvariant(), FSub, new Rectangle(0, y, Width - S(70), h), Theme.Faint, FmtLeft);
        return y + h + S(8);
    }

    private void DrawTh(Graphics g, string text, int x, int y, int w, int h) =>
        TextRenderer.DrawText(g, text.ToUpperInvariant(), FSub, new Rectangle(x, y, Math.Max(10, w), h), Theme.Faint, FmtLeft);

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
