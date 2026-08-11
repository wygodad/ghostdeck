using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;

namespace GhostDeck;

/// <summary>
/// In-tab "Report my model" wizard: two columns (info left, capture right), themed,
/// scrollable. Read-only EC dump per MSI Center scenario, then a pre-filled GitHub issue.
/// </summary>
public sealed class ReportPage : ThemedPage
{
    private const string RepoUrl = "https://github.com/wygodad/ghostdeck";
    private const int Pad = 28, Gutter = 44;
    private int _leftW = 430;

    private static readonly (ProfileId id, string msiName)[] Steps =
    {
        (ProfileId.Silent, "SILENT"), (ProfileId.Balanced, "BALANCED"),
        (ProfileId.Extreme, "EXTREME PERFORMANCE"), (ProfileId.SuperBattery, "SUPER BATTERY"),
    };
    private static readonly byte[] SnapshotAddrs = { 0x34, 0xD2, 0xD4, 0xEB, 0xF2, 0xF4, 0xD7, 0xEF };

    private readonly byte[]?[] _dumps = new byte[Steps.Length][];
    private readonly StepRowT[] _rows;
    private readonly InfoCardT _card;
    private readonly Button _capture = new();
    private readonly Button _restart = new();        // reset the 4-profile capture
    private readonly Button _curveRestart = new();   // reset the fan-curve capture
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private int _step;
    private bool _capturing;
    private int _lastPct = -1;
    private float _barValue;
    private string? _savedPath;
    // Whether the report reached the clipboard. The whole flow tells the user to paste it, so a
    // failed copy has to be said out loud rather than leaving them to paste whatever was there.
    private bool _copied, _curveCopied, _ptCopied;
    private int _rightX, _barY, _introY, _contentTop, _rowsTop, _introH, _instrTop, _instrH, _capY;
    private static readonly Font IntroFont = new("Segoe UI", 10.5f);

    // ---- sub-tabs: segment 0 is the start screen, segments 1..3 map to _sub 0..2 ----
    // (_sub keeps its 0 = profiles / 1 = curve / 2 = power meaning everywhere else on the page)
    private readonly SubTabs _subTabs = new(
        new[] { Lang.T("subtab_start"), Lang.T("subtab_profiles"), Lang.T("subtab_curve"), Lang.T("subtab_power") },
        new[] { "\uE80F", "\uE8AB", "\uE9D2", "\uE945" });
    private int _sub;
    private int _subTop;
    // The page opens on a start screen that says what the three tests are for and which one to
    // run; picking a tile (or a sub-tab) leaves it, and the Start segment brings it back.
    private bool _landing = true;
    private readonly Rectangle[] _landCards = new Rectangle[3];
    private int _landHover = -1;
    private int _landIntroY, _landCardsY, _landTitleY, _landI3Y, _landColW;
    private bool _landTwoCol;

    // ---- power test (the only sub-tab that writes; see Core/PowerTest.cs) ----
    private readonly Button _ptStart = new();
    private readonly Button _ptSecondary = new();     // Cancel while running, Start over once there is a result
    private readonly CheckItem _ptConsent = new(Lang.T("pt_consent"), false);
    private InfoCardT _ptCard = null!;
    private readonly StepRowT[] _ptRows;
    private CancellationTokenSource? _ptCts;
    private Task<PowerTest.Result>? _ptTask;   // kept so app exit can wait for the EC restore
    private bool _ptRunning;
    private PowerTest.Result? _ptResult;
    private string? _ptSavedPath;
    private string _ptLive = "", _ptStage = "";
    private string? _ptMsg;                           // blocked reason / missing consent / EC failure
    private int _ptStep, _ptSteps = 1;
    private float _ptBar;
    private int _ptTop, _ptRowsTop, _ptBtnY, _ptBarY, _ptWritesY, _ptWritesH, _ptConsentY;
    private static readonly Font WritesFont = new("Consolas", 11f, FontStyle.Bold);

    /// <summary>The model definition in effect right now (follows a live model-database swap).</summary>
    private DeviceProfile? Dev => Devices.Detect(D.Firmware());

    /// <summary>
    /// A report exists only when a phase was actually measured. A run refused on a busy machine,
    /// or cancelled before the first phase finished, produces a Result carrying a reason and
    /// nothing else, and must not offer a file, a clipboard copy or a GitHub form.
    /// </summary>
    private bool PtHasReport => _ptResult is { Phases.Length: > 0 };

    // ---- fan-curve verification flow ----
    // The user sets these exact, distinctive speeds in MSI Center (Extreme → Advanced). We read the EC
    // back and search the full dump for the sequences: finding them locates the per-model curve tables.
    private static readonly int[] CpuTracer = { 25, 35, 45, 55, 65, 75 };
    private static readonly int[] GpuTracer = { 20, 30, 40, 50, 60, 70 };
    private readonly Button _curveBtn = new();
    private InfoCardT _curveCard = null!;
    private int _curveStepsTop;
    private byte[]? _curveDump;
    private bool _curveCapturing;
    private int _curvePct = -1;
    private float _curveBar;
    // Tracer locations found in the dump (-1 = not found). Tracked per fan so a single-curve
    // board (MSI Center exposes one slider - e.g. GF63 12VE, #22) reports "CPU found, no Fan 2"
    // instead of a blanket "not located".
    private int _curveCpuAt = -1, _curveGpuAt = -1;
    private string? _curveMsg;
    private bool _curveMatch;
    private string? _curveSavedPath;
    private int _curveTop, _curveBtnY, _curveBarY;

    public ReportPage(MainDeps d) : base(d)
    {
        _subTabs.Changed += i =>
        {
            _landing = i == 0;
            if (i > 0) _sub = i - 1;
            SyncSub(); Relayout(); Invalidate();
        };
        Controls.Add(_subTabs);

        Ui.StylePrimary(_curveBtn);
        _curveBtn.UseMnemonic = false;   // its label ("Capture & scan") contains a literal & — don't treat it as a mnemonic
        _curveBtn.Click += OnCurveCapture;
        Controls.Add(_curveBtn);

        _curveCard = new InfoCardT("", new (string, string?)[]
        {
            (Lang.T("rep_curve_warn"), null),
            (Lang.T("rep_curve_why"), null),
        }, _leftW);
        Controls.Add(_curveCard);

        _card = new InfoCardT("", new (string, string?)[]
        {
            (Lang.T("rep_need_msi"), null),
            (Lang.T("rep_msi_tip"), null),
            (Lang.T("rep_msi_download"), null),
            (Lang.T("rep_dl_version"), "https://msi-center.en.uptodown.com/windows/download/1045738268"),
            (Lang.T("rep_dl_repo"), "https://msi-center.en.uptodown.com/windows/versions"),
            (Lang.T("rep_msi_clean"), null),
            (Lang.T("rep_uninstaller_link"), "https://download.msi.com/uti_exe/nb/CleanCenterMaster.zip"),
        }, _leftW);
        Controls.Add(_card);

        _rows = Steps.Select((s, i) => new StepRowT(i + 1, s.msiName, Theme.Profile(D.ColorOf(s.id)))).ToArray();
        foreach (var r in _rows) Controls.Add(r);

        Ui.StylePrimary(_capture);
        _capture.Click += OnCapture;
        Controls.Add(_capture);

        // "Start over" for both wizards (discussion #9: a capture run with the wrong MSI Center
        // state couldn't be repeated without restarting the app).
        _restart.Text = _curveRestart.Text = Lang.T("rep_restart");
        Ui.StyleGhost(_restart);
        Ui.StyleGhost(_curveRestart);
        _restart.Visible = _curveRestart.Visible = false;
        _restart.Click += (_, _) =>
        {
            if (_capturing) return;
            Array.Clear(_dumps, 0, _dumps.Length);
            _step = 0; _savedPath = null; _barValue = 0; _lastPct = -1;
            RefreshSteps(); Invalidate();
        };
        _curveRestart.Click += (_, _) =>
        {
            if (_curveCapturing) return;
            _curveDump = null; _curveCpuAt = _curveGpuAt = -1; _curveMsg = null; _curveMatch = false;
            _curveSavedPath = null; _curvePct = -1; _curveBar = 0;
            RefreshCurve(); SyncSub(); Invalidate();
        };
        Controls.Add(_restart);
        Controls.Add(_curveRestart);

        // ---- power test ----
        _ptCard = new InfoCardT("", new (string, string?)[]
        {
            (Lang.T("pt_warn_write"), null),
            (Lang.T("pt_warn_fourth"), null),
            (Lang.T("pt_warn_heat"), null),
            (Lang.T("pt_warn_restore"), null),
        }, _leftW);
        Controls.Add(_ptCard);

        // Silent / Balanced / Extreme, then the board's fourth mode (hidden when it has none),
        // then the restore. Labels are filled in by RefreshPowerRows, which follows the model.
        _ptRows = new StepRowT[PtRowCount];
        for (int i = 0; i < _ptRows.Length; i++)
        {
            _ptRows[i] = new StepRowT(i + 1, "", Theme.Accent);
            Controls.Add(_ptRows[i]);
        }

        Ui.StylePrimary(_ptStart);
        _ptStart.Click += OnPowerStart;
        Controls.Add(_ptStart);
        Ui.StyleGhost(_ptSecondary);
        _ptSecondary.Click += OnPowerSecondary;
        Controls.Add(_ptSecondary);
        // Ticking the box answers whatever the page was complaining about, so the complaint goes.
        _ptConsent.Toggled += _ => { _ptMsg = null; SyncPower(); Invalidate(); };
        Controls.Add(_ptConsent);

        _anim.Tick += (_, _) => OnAnim();
        Resize += (_, _) => Relayout();
        RefreshSteps();
        RefreshPowerRows();
        SyncSub();
    }

    // Silent + Balanced + Extreme + fourth mode + restore. The fourth row is hidden on boards
    // whose database entry records no fourth shift value.
    private const int PtRowCount = 5;
    private const int PtRestoreRow = 4;

    /// <summary>Open a specific sub-tab (0 = profiles, 1 = fan curve, 2 = power test). Used by deep links.</summary>
    public void SetSubTab(int sub)
    {
        _landing = false;
        _sub = Math.Clamp(sub, 0, 2);
        _subTabs.SetActive(_sub + 1);
        SyncSub(); Relayout(); Invalidate();
    }

    // Show only the active sub-tab's controls (the rest are hand-painted, gated in OnPaint).
    private void SyncSub()
    {
        bool prof = !_landing && _sub == 0, curve = !_landing && _sub == 1, power = !_landing && _sub == 2;
        _card.Visible = prof;
        foreach (var r in _rows) r.Visible = prof;
        _capture.Visible = prof;
        _restart.Visible = prof && _step > 0;
        _curveBtn.Visible = curve;
        _curveRestart.Visible = curve && _curveDump != null;
        _curveCard.Visible = curve;
        _ptCard.Visible = power;
        _ptStart.Visible = power;
        _ptConsent.Visible = power && !_ptRunning;
        for (int i = 0; i < _ptRows.Length; i++) _ptRows[i].Visible = power && PtRowShown(i);
        if (curve) RefreshCurve();
        if (power) { RefreshPowerRows(); SyncPower(); } else _ptSecondary.Visible = false;
    }

    public override void OnEnter()
    {
        // A refusal ("plug the charger in") is answered by leaving and coming back, so it should not
        // survive that. A run in progress keeps whatever it is saying.
        if (!_ptRunning) _ptMsg = null;
        Relayout(); RefreshSteps(); SyncSub(); Invalidate();
    }

    // A newer model database can add (or correct) the fourth shift value while the page is open.
    // Rebuilding the checklist is all that is needed; a run in progress holds the swap gate, so
    // this can never land mid-test.
    public override void OnDeviceDbChanged() { RefreshPowerRows(); SyncSub(); Relayout(); Invalidate(); }

    public override void OnLanguageChanged()
    {
        _subTabs.SetLabels(new[]
        {
            Lang.T("subtab_start"), Lang.T("subtab_profiles"), Lang.T("subtab_curve"), Lang.T("subtab_power"),
        });
        // Button captions and checklist rows are re-derived by the same refreshes OnEnter uses.
        RefreshSteps(); RefreshCurve(); RefreshPowerRows(); SyncSub(); Relayout(); Invalidate();
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _card.ApplyTheme();
        for (int i = 0; i < _rows.Length; i++) { _rows[i].Tint = Theme.Profile(D.ColorOf(Steps[i].id)); _rows[i].Invalidate(); }
        Ui.StylePrimary(_capture);
        Ui.StylePrimary(_curveBtn);
        Ui.StyleGhost(_restart);
        Ui.StyleGhost(_curveRestart);
        _curveCard.ApplyTheme();
        Ui.StylePrimary(_ptStart);
        Ui.StyleGhost(_ptSecondary);
        _ptCard.ApplyTheme();
        RefreshPowerRows();
        _ptConsent.Invalidate();
        _subTabs.Invalidate();
    }

    private void Relayout()
    {
        // Content coordinates offset by AutoScrollPosition when they reach a child's Location -
        // WinForms treats those as client coords and shifts children by the scroll delta, while
        // OnPaint draws through ApplyScroll. Same rule as SettingsPage/ScenariosPage.
        int ox = AutoScrollPosition.X, oy = AutoScrollPosition.Y;
        int titleH = new Font("Segoe UI", 18f, FontStyle.Bold).Height;
        _subTop = 24 + titleH + 18;
        _subTabs.SetBounds(Pad + ox, _subTop + oy, _subTabs.FitTo(ClientSize.Width - Pad * 2), _subTabs.Height);

        // NB: content coord, NOT _subTabs.Bottom - that one is already shifted by the scroll.
        int top = _subTop + _subTabs.Height + 26;
        if (_landing) LayoutLanding(top);
        else if (_sub == 0) LayoutProfiles(top, ox, oy);
        else if (_sub == 1) LayoutCurve(top, ox, oy);
        else LayoutPower(top, ox, oy);
    }

    // =================================================================
    //  start screen
    // =================================================================
    private static readonly Font LandTitleF = new("Segoe UI", 14f, FontStyle.Bold);
    private static readonly Font LandQF = new("Segoe UI", 11.5f, FontStyle.Bold);
    private static readonly Font LandDF = new("Segoe UI", 10f);
    private static readonly Font LandFootF = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font LandGlyphF = new("Segoe MDL2 Assets", 16f);
    private static readonly string[] LandGlyphs = { "\uE8AB", "\uE9D2", "\uE945" };
    private const int LandPad = 20, LandGap = 14, LandIconBox = 42;

    private static string LandIntro12 => Lang.T("rep_home_intro1") + "\n\n" + Lang.T("rep_home_intro2");
    private static string LandQ(int i) => Lang.T(i == 0 ? "rep_home_q1" : i == 1 ? "rep_home_q2" : "rep_home_q3");
    private static string LandD(int i) => Lang.T(i == 0 ? "rep_home_d1" : i == 1 ? "rep_home_d2" : "rep_home_d3");
    private static string LandF(int i) => Lang.T(i == 2 ? "rep_home_f_power" : "rep_home_f_read");
    private string LandTitle(int i) => Lang.T(i == 0 ? "subtab_profiles" : i == 1 ? "subtab_curve" : "subtab_power");

    private void LayoutLanding(int top)
    {
        int avail = ClientSize.Width - Pad * 2;
        static int Measure(string s, int w) =>
            TextRenderer.MeasureText(s, IntroFont, new Size(w, 0), TextFormatFlags.WordBreak).Height;

        _landTitleY = top;
        _landIntroY = top + LandTitleF.Height + 12;
        // A maximised window gives the intro a line length nobody can read, so past ~1000 px the
        // two long paragraphs sit side by side and the short third one runs under both.
        _landTwoCol = avail >= 1000;
        _landColW = _landTwoCol ? (avail - Gutter) / 2 : avail;
        int i12H = _landTwoCol
            ? Math.Max(Measure(Lang.T("rep_home_intro1"), _landColW), Measure(Lang.T("rep_home_intro2"), _landColW))
            : Measure(LandIntro12, avail);
        _landI3Y = _landIntroY + i12H + 28;
        _landCardsY = _landI3Y + Measure(Lang.T("rep_home_intro3"), avail) + 26;

        // Three equal tiles side by side; stacked once they would drop under a readable width.
        bool row = avail >= 3 * 300 + 2 * LandGap;
        int cardW = row ? (avail - 2 * LandGap) / 3 : avail;
        int innerW = cardW - LandPad * 2;

        // Same height for all three, decided by the tallest: a ragged row reads as three
        // unrelated boxes rather than one choice.
        int bodyH = 0;
        for (int i = 0; i < 3; i++)
        {
            int qH = TextRenderer.MeasureText(LandQ(i), LandQF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            int dH = TextRenderer.MeasureText(LandD(i), LandDF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            int fH = TextRenderer.MeasureText(LandF(i), LandFootF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            bodyH = Math.Max(bodyH, qH + 10 + dH + 16 + fH);
        }
        int cardH = LandPad + LandIconBox + 14 + bodyH + LandPad;

        for (int i = 0; i < 3; i++)
            _landCards[i] = row
                ? new Rectangle(Pad + i * (cardW + LandGap), _landCardsY, cardW, cardH)
                : new Rectangle(Pad, _landCardsY + i * (cardH + LandGap), cardW, cardH);

        int bottom = row ? _landCardsY + cardH : _landCardsY + 3 * cardH + 2 * LandGap;
        AutoScrollMinSize = new Size(Pad * 2 + 3 * 300 + 2 * LandGap, bottom + 40);
    }

    private void PaintLanding(Graphics g)
    {
        int avail = ClientSize.Width - Pad * 2;
        TextRenderer.DrawText(g, Lang.T("rep_home_title"), LandTitleF, new Point(Pad, _landTitleY), Theme.Text);
        int introH = _landI3Y - _landIntroY;
        if (_landTwoCol)
        {
            TextRenderer.DrawText(g, Lang.T("rep_home_intro1"), IntroFont,
                new Rectangle(Pad, _landIntroY, _landColW, introH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
            TextRenderer.DrawText(g, Lang.T("rep_home_intro2"), IntroFont,
                new Rectangle(Pad + _landColW + Gutter, _landIntroY, _landColW, introH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
        else
        {
            TextRenderer.DrawText(g, LandIntro12, IntroFont,
                new Rectangle(Pad, _landIntroY, avail, introH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
        TextRenderer.DrawText(g, Lang.T("rep_home_intro3"), IntroFont,
            new Rectangle(Pad, _landI3Y, avail, _landCardsY - _landI3Y), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);

        for (int i = 0; i < 3; i++)
        {
            var r = _landCards[i];
            bool hover = i == _landHover;
            Ui.FillCard(g, r);
            if (hover)
            {
                using var path = Theme.RoundRect(new RectangleF(r.X + 0.7f, r.Y + 0.7f, r.Width - 1.4f, r.Height - 1.4f), 8);
                using var pen = new Pen(Theme.Accent, 1.4f);
                g.DrawPath(pen, path);
            }

            // icon in a rounded frame, the same treatment as the Settings start screen
            var iconR = new Rectangle(r.X + LandPad, r.Y + LandPad, LandIconBox, LandIconBox);
            using (var ip = Theme.RoundRect(new RectangleF(iconR.X + 0.5f, iconR.Y + 0.5f, iconR.Width - 1, iconR.Height - 1), 8))
            using (var ap = new Pen(Theme.Accent, 1.7f))
                g.DrawPath(ap, ip);
            Ui.CenterGlyph(g, LandGlyphs[i], LandGlyphF, Theme.Accent, iconR);

            TextRenderer.DrawText(g, LandTitle(i), new Font("Segoe UI", 12f, FontStyle.Bold),
                new Rectangle(iconR.Right + 14, iconR.Y, r.Width - LandIconBox - LandPad * 2 - 14, LandIconBox),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            int innerW = r.Width - LandPad * 2;
            int y = iconR.Bottom + 14;
            int qH = TextRenderer.MeasureText(LandQ(i), LandQF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, LandQ(i), LandQF, new Rectangle(r.X + LandPad, y, innerW, qH + 2),
                hover ? Theme.Accent : Theme.Text, TextFormatFlags.WordBreak);
            y += qH + 10;
            TextRenderer.DrawText(g, LandD(i), LandDF,
                new Rectangle(r.X + LandPad, y, innerW, r.Bottom - LandPad - y), Theme.Muted, TextFormatFlags.WordBreak);

            int fH = TextRenderer.MeasureText(LandF(i), LandFootF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, LandF(i), LandFootF,
                new Rectangle(r.X + LandPad, r.Bottom - LandPad - fH, innerW, fH + 2), Theme.Faint, TextFormatFlags.WordBreak);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_landing) return;
        var p = ContentPoint(e.Location);
        int h = -1;
        for (int i = 0; i < 3; i++) if (_landCards[i].Contains(p)) { h = i; break; }
        if (h != _landHover) { _landHover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_landHover != -1) { _landHover = -1; Cursor = Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_landing || e.Button != MouseButtons.Left) return;
        var p = ContentPoint(e.Location);
        for (int i = 0; i < 3; i++)
            if (_landCards[i].Contains(p)) { _subTabs.SetActive(i + 1, raise: true); return; }
    }

    // The landing rectangles live in content coordinates; mouse events arrive in client ones.
    private Point ContentPoint(Point client) =>
        new(client.X - AutoScrollPosition.X, client.Y - AutoScrollPosition.Y);

    private void LayoutProfiles(int top, int ox, int oy)
    {
        // equal-width columns
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);

        int secH = new Font("Segoe UI", 9.5f, FontStyle.Bold).Height;
        _introY = top;
        _introH = TextRenderer.MeasureText(Lang.T("rep_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _introY + _introH + 18;
        _rowsTop = _contentTop + secH + 14;

        _card.Location = new Point(Pad + ox, _contentTop + oy);
        _card.SetWidth(_leftW);

        int ry = _rowsTop;
        foreach (var r in _rows) { r.SetBounds(_rightX + ox, ry + oy, rightW, 52); ry += 60; }
        _barY = ry + 26;
        _instrTop = _barY + 58;
        var instrFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _instrH = TextRenderer.MeasureText(Lang.T("rep_all_done"), instrFont, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
        int capW = Math.Min(320, rightW - 180);
        int capY = _capY = _instrTop + _instrH + 18;      // content coord; children get + oy
        _capture.SetBounds(_rightX + ox, capY + oy, capW, 44);
        _restart.SetBounds(_rightX + capW + 10 + ox, capY + oy, 170, 44);

        // bottoms in CONTENT coords (child .Bottom is client-side once the page is scrolled)
        int leftBottom = _contentTop + _card.Height + 70;   // + firmware pill
        int rightBottom = capY + 44 + 80 + (_copied ? 0 : ClipWarnHeight(rightW));
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    // Two-column layout mirroring the profiles flow: left = intro + info card + firmware pill,
    // right = section label + numbered steps + capture button + result.
    private void LayoutCurve(int top, int ox, int oy)
    {
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        int secH = new Font("Segoe UI", 9.5f, FontStyle.Bold).Height;

        _curveTop = top;   // intro (left)
        _introH = TextRenderer.MeasureText(Lang.T("rep_curve_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _curveTop + _introH + 18;
        _curveCard.Location = new Point(Pad + ox, _contentTop + oy);
        _curveCard.SetWidth(_leftW);

        // right column: section label + 5 steps + button
        _curveStepsTop = _contentTop + secH + 14;
        _curveBtnY = _curveStepsTop + 34 * 5 + 18;
        _curveBarY = _curveBtnY + 62;
        int cbW = Math.Min(320, rightW - 180);
        _curveBtn.SetBounds(_rightX + ox, _curveBtnY + oy, cbW, 44);
        _curveRestart.SetBounds(_rightX + cbW + 10 + ox, _curveBtnY + oy, 170, 44);

        int leftBottom = _contentTop + _curveCard.Height + 70;   // content coords (+ firmware pill)
        int rightBottom = _curveBarY + 80 + (_curveCopied ? 0 : ClipWarnHeight(rightW));
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ApplyScroll(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        TextRenderer.DrawText(g, Lang.T("menu_report"), new Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);

        if (_landing) { PaintLanding(g); return; }
        if (_sub == 1) { PaintCurve(g); return; }
        if (_sub == 2) { PaintPower(g); return; }

        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        // left: intro under title, info card (child) already placed, firmware pill below it
        TextRenderer.DrawText(g, Lang.T("rep_intro"), IntroFont,
            new Rectangle(Pad, _introY, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        // content coord, NOT _card.Bottom - a child's Bottom already carries the scroll offset,
        // and OnPaint adds it again through ApplyScroll (see docs/RENDERING.md §5.1)
        PaintFirmwarePill(g, _contentTop + _card.Height + 14);

        // right: section label
        TextRenderer.DrawText(g, Lang.T("rep_section"), new Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(_rightX, _contentTop), Theme.Muted);

        // right: progress (only while capturing)
        if (_capturing)
        {
            TextRenderer.DrawText(g, Lang.T("rep_capturing") + $"  {_lastPct}%", new Font("Segoe UI", 10f, FontStyle.Bold),
                new Point(_rightX, _barY - 30), Theme.Accent);
            var track = new RectangleF(_rightX, _barY, rightW, 12);
            using (var path = Theme.RoundRect(track, 6)) { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            float w = Math.Max(12, rightW * _barValue);
            using (var path = Theme.RoundRect(new RectangleF(_rightX, _barY, w, 12), 6)) { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
        }

        // right: instruction
        bool done = _step >= Steps.Length;
        string instr = done ? "✓  " + Lang.T("rep_all_done")
                            : string.Format(Lang.T("rep_step"), _step + 1, Steps.Length) + " — " + string.Format(Lang.T("rep_set_scenario"), Steps[_step].msiName);
        TextRenderer.DrawText(g, instr, new Font("Segoe UI", 11.5f, FontStyle.Bold),
            new Rectangle(_rightX, _instrTop, rightW, _instrH + 6), done ? Theme.Green : Theme.Text, TextFormatFlags.WordBreak);
        if (done) PaintSaved(g, _rightX, _capY + 44 + 10, rightW, _savedPath, _copied);
    }

    // =================================================================
    //  fan-curve verification sub-tab
    // =================================================================
    private void RefreshCurve()
    {
        _curveBtn.Text = _curveDump == null ? Lang.T("rep_curve_capture") : Lang.T("rep_curve_finish");
        _curveRestart.Visible = _sub == 1 && _curveDump != null;
    }

    private void PaintCurve(Graphics g)
    {
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);

        // ---- left column: intro + info card (child) + firmware pill ----
        TextRenderer.DrawText(g, Lang.T("rep_curve_intro"), IntroFont,
            new Rectangle(Pad, _curveTop, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        PaintFirmwarePill(g, _contentTop + _curveCard.Height + 14);

        // ---- right column: section label + numbered steps ----
        TextRenderer.DrawText(g, Lang.T("rep_curve_steps"), new Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(_rightX, _contentTop), Theme.Muted);
        string[] steps = { Lang.T("rep_curve_s1"), Lang.T("rep_curve_s2"), Lang.T("rep_curve_s3"), Lang.T("rep_curve_s4"), Lang.T("rep_curve_s5") };
        var numFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        var stFont = new Font("Segoe UI", 10.5f);
        for (int i = 0; i < steps.Length; i++)
        {
            int ry = _curveStepsTop + i * 34;
            var circ = new RectangleF(_rightX, ry, 24, 24);
            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, circ);
            TextRenderer.DrawText(g, (i + 1).ToString(), numFont, Rectangle.Round(circ), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, steps[i], stFont, new Rectangle(_rightX + 36, ry - 4, rightW - 40, 34), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        // ---- right column: progress / result (under the capture button) ----
        if (_curveCapturing)
        {
            TextRenderer.DrawText(g, Lang.T("rep_capturing") + $"  {_curvePct}%", new Font("Segoe UI", 10f, FontStyle.Bold), new Point(_rightX, _curveBarY - 4), Theme.Accent);
            var track = new RectangleF(_rightX, _curveBarY + 20, rightW, 12);
            using (var path = Theme.RoundRect(track, 6)) { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            float fw = Math.Max(12, rightW * _curveBar);
            using (var path = Theme.RoundRect(new RectangleF(_rightX, _curveBarY + 20, fw, 12), 6)) { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
        }
        else if (_curveMsg != null)
        {
            var col = _curveCpuAt >= 0 || _curveGpuAt >= 0 ? (_curveMatch ? Theme.Green : Theme.Amber) : Theme.Red;
            var mf = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            int mh = TextRenderer.MeasureText(_curveMsg, mf, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, _curveMsg, mf, new Rectangle(_rightX, _curveBarY, rightW, mh + 6), col, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            PaintSaved(g, _rightX, _curveBarY + mh + 10, rightW, _curveSavedPath, _curveCopied);
        }
    }

    private void OnCurveCapture(object? sender, EventArgs e)
    {
        if (_curveDump != null) { FinishCurve(); return; }
        if (_curveCapturing) return;
        _curveCapturing = true; _curveBtn.Enabled = false; _curvePct = 0; _curveBar = 0; _curveMsg = null; Invalidate();
        Task.Run(() =>
        {
            try { var d = Ec.DumpAll(CurveProgress); BeginInvoke(() => CurveDone(d, null)); }
            catch (Exception ex) { BeginInvoke(() => CurveDone(null, ex)); }
        });
    }

    private void CurveProgress(int byteIdx)
    {
        int pct = (int)((byteIdx + 1) / 256f * 100);
        if (pct == _curvePct) return;
        _curvePct = pct;
        BeginInvoke(() => { _curveBar = pct / 100f; Invalidate(); });
    }

    private void CurveDone(byte[]? dump, Exception? ex)
    {
        _curveCapturing = false; _curveBtn.Enabled = true;
        if (ex != null || dump == null)
        {
            MessageBox.Show(string.Format(Lang.T("rep_read_fail"), AppLifecycle.DescribeEcFailure(ex)), Lang.T("err"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Invalidate(); return;
        }
        _curveDump = dump;
        _curveCpuAt = FindTracer(dump, CpuTracer);
        _curveGpuAt = FindTracer(dump, GpuTracer);
        var fcs = Devices.Detect(D.Firmware())?.FanCurve;
        if (_curveCpuAt >= 0 && _curveGpuAt >= 0)
        {
            _curveMatch = fcs != null && _curveCpuAt == fcs.CpuSpeedBase && _curveGpuAt == fcs.GpuSpeedBase;
            _curveMsg = string.Format(Lang.T("rep_curve_found"), _curveCpuAt, _curveGpuAt) + "  " + Lang.T(_curveMatch ? "rep_curve_match" : "rep_curve_nomatch");
        }
        else if (_curveCpuAt >= 0 || _curveGpuAt >= 0)
        {
            // one tracer only: single-fan boards (MSI Center shows one slider) land here by
            // design - report the found half instead of a blanket "not located" (#22)
            bool cpuSide = _curveCpuAt >= 0;
            int at = cpuSide ? _curveCpuAt : _curveGpuAt;
            byte shipped = cpuSide ? (fcs?.CpuSpeedBase ?? 0) : (fcs?.GpuSpeedBase ?? 0);
            _curveMatch = fcs != null && at == shipped;
            _curveMsg = string.Format(Lang.T(cpuSide ? "rep_curve_cpuonly" : "rep_curve_gpuonly"), at)
                        + "  " + Lang.T(_curveMatch ? "rep_curve_match" : "rep_curve_nomatch");
        }
        else
        {
            _curveMatch = false;
            // Common cause: the Advanced curve isn't the live EC state (e.g. the laptop is in Silent), so the
            // tables hold the default curve, not the test values. Detect that and say so, instead of "not found".
            var dev = Devices.Detect(D.Firmware());
            byte adv = dev?.FanCurve?.AdvancedModeValue ?? 0x8D;
            bool inAdvanced = dev != null && dump[dev.FanMode] == adv;
            _curveMsg = inAdvanced ? Lang.T("rep_curve_notfound") : Lang.T("rep_curve_notadvanced");
        }
        PrepareCurveReport();
        RefreshCurve();
        Invalidate();
    }

    // Scan the 256-byte dump for the tracer speeds. Exact 6-value run first; fall back to the first 5.
    private static int FindTracer(byte[] dump, int[] seq)
    {
        for (int len = seq.Length; len >= 5; len--)
            for (int a = 0; a + len <= 256; a++)
            {
                bool ok = true;
                for (int i = 0; i < len; i++) if (dump[a + i] != seq[i]) { ok = false; break; }
                if (ok) return a;
            }
        return -1;
    }

    private string BuildCurveReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== GhostDeck — fan-curve verification report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY, no EC writes)");
        sb.AppendLine($"App version: {D.AppVersion()}");
        sb.AppendLine($"EC firmware: {(string.IsNullOrEmpty(D.Firmware()) ? "(unknown)" : D.Firmware())}");
        sb.AppendLine($"Detected in app: {(string.IsNullOrEmpty(ModelName()) ? "(unsupported / unknown)" : ModelName())}");
        sb.AppendLine();
        sb.AppendLine("Test curve set in MSI Center (Extreme → Advanced):");
        sb.AppendLine($"  Fan 1 (CPU): {string.Join(" ", CpuTracer)}");
        sb.AppendLine($"  Fan 2 (GPU): {string.Join(" ", GpuTracer)}");
        sb.AppendLine();
        if (_curveCpuAt >= 0 || _curveGpuAt >= 0)
        {
            string cpuPart = _curveCpuAt >= 0 ? $"CPU speed table @ 0x{_curveCpuAt:X2}" : "CPU test curve not found";
            string gpuPart = _curveGpuAt >= 0 ? $"GPU speed table @ 0x{_curveGpuAt:X2}"
                                              : "GPU test curve not found (single-fan model or Fan 2 not set)";
            sb.AppendLine($"Located in EC dump:  {cpuPart}   {gpuPart}");
            var fc = Devices.Detect(D.Firmware())?.FanCurve;
            if (fc != null) sb.AppendLine($"Shipped map for this model:  CPU 0x{fc.CpuSpeedBase:X2}  GPU 0x{fc.GpuSpeedBase:X2}  → {(_curveMatch ? "MATCH" : "DIFFERENT")}");
            else sb.AppendLine("Shipped map for this model:  (none — model not recognised)");
        }
        else sb.AppendLine("Test curve NOT located in the dump (was the curve Saved in MSI Center?).");
        sb.AppendLine();
        sb.AppendLine("--- Full EC dump (256 bytes) ---");
        for (int row = 0; row < 256; row += 16)
        {
            sb.Append($"{row:X2}: ");
            for (int c = 0; c < 16; c++) sb.Append($"{_curveDump![row + c]:X2} ");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void PrepareCurveReport()
    {
        string report = BuildCurveReport();
        _curveCopied = Ui.CopyText(report);
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fwTag = string.IsNullOrEmpty(D.Firmware()) ? "unknown" : D.Firmware().Replace('.', '_');
            _curveSavedPath = Path.Combine(dir, $"ghostdeck-curve-report-{fwTag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(_curveSavedPath, report, new UTF8Encoding(false));
        }
        catch { _curveSavedPath = null; }
        Relayout();   // the clipboard warning changes how much room the page needs
    }

    private void FinishCurve()
    {
        try { Process.Start(new ProcessStartInfo(BuildCurveIssueUrl()) { UseShellExecute = true }); } catch { }
    }

    private string BuildCurveIssueUrl()
    {
        string title = $"[Curve] {ModelName()} ({D.Firmware()})";
        string suffix = _curveMatch ? " (matches shipped map)" : " (differs from shipped map)";
        string found =
            _curveCpuAt >= 0 && _curveGpuAt >= 0 ? $"CPU @ 0x{_curveCpuAt:X2}, GPU @ 0x{_curveGpuAt:X2}" + suffix
            : _curveCpuAt >= 0 ? $"CPU @ 0x{_curveCpuAt:X2}" + suffix + "; GPU not set (single-fan model?)"
            : _curveGpuAt >= 0 ? $"GPU @ 0x{_curveGpuAt:X2}" + suffix + "; CPU test curve not found"
            : "not located in dump";
        // NB: the paste field (id "dump") is deliberately NOT prefilled — the full report is on the
        // clipboard / saved to file, and any reload of a prefilled URL would wipe what the user pasted.
        return RepoUrl + "/issues/new?template=curve-support.yml&labels=curve-support"
            + "&title=" + Uri.EscapeDataString(title)
            + "&model=" + Uri.EscapeDataString(ModelName())
            + "&firmware=" + Uri.EscapeDataString(D.Firmware())
            + "&found=" + Uri.EscapeDataString(found);
    }

    // ---- step state ----
    private void RefreshSteps()
    {
        for (int i = 0; i < Steps.Length; i++) _rows[i].SetState(_dumps[i] != null, i == _step);
        _capture.Text = _step >= Steps.Length ? Lang.T("rep_finish") : Lang.T("rep_capture");
        _restart.Visible = _sub == 0 && _step > 0;
        EnsureAnim();
        Invalidate();
    }

    private void EnsureAnim() { if (!_anim.Enabled) _anim.Start(); }
    private void OnAnim()
    {
        bool busy = _capturing || _ptRunning;
        foreach (var r in _rows) busy |= r.Animate();
        foreach (var r in _ptRows) busy |= r.Animate();
        if (!busy) _anim.Stop();
    }

    // ---- capture ----
    private void OnCapture(object? sender, EventArgs e)
    {
        if (_step >= Steps.Length) { Finish(); return; }
        if (_capturing) return;
        _capturing = true; _capture.Enabled = false; _lastPct = 0; _barValue = 0;
        EnsureAnim(); Invalidate();
        int idx = _step;
        Task.Run(() =>
        {
            try { var dump = Ec.DumpAll(ReportProgress); BeginInvoke(() => CaptureDone(idx, dump, null)); }
            catch (Exception ex) { BeginInvoke(() => CaptureDone(idx, null, ex)); }
        });
    }

    private void ReportProgress(int byteIdx)
    {
        int pct = (int)((byteIdx + 1) / 256f * 100);
        if (pct == _lastPct) return;
        _lastPct = pct;
        BeginInvoke(() => { _barValue = pct / 100f; Invalidate(); });
    }

    private void CaptureDone(int idx, byte[]? dump, Exception? ex)
    {
        _capturing = false; _capture.Enabled = true;
        if (ex != null || dump == null)
        {
            MessageBox.Show(string.Format(Lang.T("rep_read_fail"), AppLifecycle.DescribeEcFailure(ex)), Lang.T("err"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Invalidate(); return;
        }
        _dumps[idx] = dump; _step++;
        RefreshSteps();
        if (_step >= Steps.Length) PrepareReport();
    }

    private void PrepareReport()
    {
        string report = BuildReport();
        _copied = Ui.CopyText(report);
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fwTag = string.IsNullOrEmpty(D.Firmware()) ? "unknown" : D.Firmware().Replace('.', '_');
            _savedPath = Path.Combine(dir, $"msi-model-report-{fwTag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(_savedPath, report, new UTF8Encoding(false));
        }
        catch { _savedPath = null; }
        Relayout();   // the clipboard warning changes how much room the page needs
        Invalidate();
    }

    private void Finish()
    {
        try { Process.Start(new ProcessStartInfo(BuildIssueUrl()) { UseShellExecute = true }); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _anim.Dispose();
            // A power test outlives a tab switch on purpose, but not the window closing.
            try { _ptCts?.Cancel(); } catch { }
        }
        base.Dispose(disposing);
    }

    // ---- report building (read-only) ----
    private string ModelName() { var s = D.Status(); return s.Known ? s.Device : ""; }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== MSI Profile Switcher — model support report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY, no EC writes)");
        sb.AppendLine($"App version: {D.AppVersion()}");
        sb.AppendLine($"EC firmware: {(string.IsNullOrEmpty(D.Firmware()) ? "(unknown)" : D.Firmware())}");
        sb.AppendLine($"Detected in app: {(string.IsNullOrEmpty(ModelName()) ? "(unsupported / unknown)" : ModelName())}");
        sb.AppendLine();
        sb.AppendLine("--- Diff: addresses that change between scenarios ---");
        sb.AppendLine("(temps/fans naturally fluctuate — ignore sensor-looking single-value drift)");
        sb.Append("Addr   ");
        foreach (var (_, name) in Steps) sb.Append(name.PadRight(20));
        sb.AppendLine();
        for (int a = 0; a < 256; a++)
        {
            if (AllEqualAt(a)) continue;
            sb.Append($"0x{a:X2}   ");
            foreach (var dump in _dumps) sb.Append((dump == null ? "--" : $"{dump[a]:X2}").PadRight(20));
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("--- Full EC dumps (256 bytes each) ---");
        for (int i = 0; i < Steps.Length; i++)
        {
            sb.AppendLine(); sb.AppendLine($"[{Steps[i].msiName}]");
            var dump = _dumps[i];
            if (dump == null) { sb.AppendLine("(not captured)"); continue; }
            for (int row = 0; row < 256; row += 16)
            {
                sb.Append($"{row:X2}: ");
                for (int c = 0; c < 16; c++) sb.Append($"{dump[row + c]:X2} ");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private bool AllEqualAt(int addr)
    {
        byte? first = null;
        foreach (var dump in _dumps)
        {
            if (dump == null) continue;
            if (first == null) first = dump[addr];
            else if (dump[addr] != first) return false;
        }
        return true;
    }

    private string BuildSnapshot()
    {
        var sb = new StringBuilder();
        sb.Append("Addr ");
        foreach (var (_, name) in Steps) sb.Append(name.PadRight(9));
        sb.AppendLine();
        foreach (var a in SnapshotAddrs)
        {
            sb.Append($"{a:X2}   ");
            foreach (var dump in _dumps) sb.Append((dump == null ? "--" : $"{dump[a]:X2}").PadRight(9));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string BuildIssueUrl()
    {
        string title = $"[Model] {ModelName()} ({D.Firmware()})";
        // NB: the paste field (id "fulldump") is deliberately NOT prefilled — the full report is on the
        // clipboard / saved to file, and any reload of a prefilled URL would wipe what the user pasted.
        string Base() => RepoUrl + "/issues/new?template=model-support.yml&labels=model-support"
            + "&title=" + Uri.EscapeDataString(title)
            + "&model=" + Uri.EscapeDataString(ModelName())
            + "&firmware=" + Uri.EscapeDataString(D.Firmware());
        string url = Base() + "&snapshot=" + Uri.EscapeDataString(BuildSnapshot());
        return url.Length > 7000 ? Base() : url;
    }

    // =================================================================
    //  power test sub-tab
    // =================================================================
    // The one part of the app that writes an EC value outside a profile recipe. Everything it may
    // touch is listed on screen before it starts, the fourth-mode value comes from the model
    // database rather than the code, and the profile that was live when the run began is restored
    // through the normal path afterwards. Measurement and report live in Core/PowerTest.cs.

    private bool PtRowShown(int i) => i != 3 || Dev?.FourthMode != null;

    private void RefreshPowerRows()
    {
        var dev = Dev;
        string[] labels =
        {
            Profiles.Get(ProfileId.Silent).Label,
            Profiles.Get(ProfileId.Balanced).Label,
            Profiles.Get(ProfileId.Extreme).Label,
            dev?.FourthMode?.Name ?? "",
            Lang.T("pt_row_restore"),
        };
        Color[] tints =
        {
            Theme.Profile(D.ColorOf(ProfileId.Silent)),
            Theme.Profile(D.ColorOf(ProfileId.Balanced)),
            Theme.Profile(D.ColorOf(ProfileId.Extreme)),
            Theme.Accent, Theme.Accent,
        };
        // How far the run actually got. A cancelled or aborted run must not tick steps that never
        // ran, which is the difference between "we measured this" and "we did not".
        int reached = _ptResult switch
        {
            null => 0,
            { Aborted: null } => _ptRows.Length,
            { } r => r.Phases.Length + (r.Fourth != null ? 1 : 0),
        };

        // PowerTest counts steps over the rows that are SHOWN, so a board without a fourth mode
        // maps its restore step onto row 4 even though row 3 is hidden. The visible numbering
        // follows the same counter, otherwise such a board would show 1, 2, 3, 5.
        int shown = -1;
        for (int i = 0; i < _ptRows.Length; i++)
        {
            _ptRows[i].Label = labels[i];
            _ptRows[i].Tint = tints[i];
            if (!PtRowShown(i)) { _ptRows[i].SetState(false, false); continue; }
            shown++;
            _ptRows[i].Number = shown + 1;
            // The restore is the one step that runs even on the paths that skipped everything else.
            bool done = _ptRunning ? shown < _ptStep
                                   : PtHasReport && (i == PtRestoreRow || shown < reached);
            _ptRows[i].SetState(done, _ptRunning && shown == _ptStep);
        }
        EnsureAnim();
    }

    private void SyncPower()
    {
        bool done = PtHasReport && !_ptRunning;
        _ptStart.Text = done ? Lang.T("rep_finish") : Lang.T("pt_start");
        // While a run is in flight Cancel is the only thing left to do, so Start goes away rather
        // than sitting there swallowing clicks (LayoutPower then gives Cancel the primary slot).
        _ptStart.Visible = _sub == 2 && !_ptRunning;
        _ptConsent.Visible = _sub == 2 && !_ptRunning && !PtHasReport;
        _ptSecondary.Visible = _sub == 2 && (_ptRunning || done);
        _ptSecondary.Text = _ptRunning ? Lang.T("pt_cancel") : Lang.T("rep_restart");
    }

    /// <summary>
    /// Stop a run from outside the page. The window is only ever hidden, not closed, so Dispose is
    /// not a reliable place for this. On app exit the caller waits: PowerTest's restore runs on a
    /// background thread that the process would otherwise kill mid-sequence.
    /// </summary>
    public void StopPowerTest(bool wait)
    {
        if (!_ptRunning) return;
        try { _ptCts?.Cancel(); } catch { }
        if (wait) { try { _ptTask?.Wait(6000); } catch { } }
    }

    // Every address the run may write, from the model's own tables. The recipes are the run itself;
    // the curve tables belong here too, because the restore goes through the normal profile path
    // and that re-applies whatever fan curve is assigned to the profile it returns to.
    private string PtWriteList(DeviceProfile? dev)
    {
        if (dev == null) return "—";
        var addrs = new SortedSet<byte> { dev.ShiftMode, dev.FanMode };
        foreach (var id in new[] { ProfileId.Silent, ProfileId.Balanced, ProfileId.Extreme })
            foreach (var (a, _) in dev.Recipes[id]) addrs.Add(a);
        if (dev.FanCurve is { } fc)
            foreach (var b in fc.SingleFan
                         ? new[] { fc.CpuTempBase, fc.CpuSpeedBase }
                         : new[] { fc.CpuTempBase, fc.CpuSpeedBase, fc.GpuTempBase, fc.GpuSpeedBase })
                for (int i = 0; i < fc.Points; i++) addrs.Add((byte)(b + i));
        return string.Join("   ", addrs.Select(a => $"0x{a:X2}"));
    }

    private void OnPowerSecondary(object? sender, EventArgs e)
    {
        if (_ptRunning) { _ptCts?.Cancel(); return; }
        _ptResult = null; _ptSavedPath = null; _ptMsg = null; _ptBar = 0; _ptStep = 0; _ptLive = ""; _ptStage = "";
        _ptConsent.Checked = false;
        RefreshPowerRows(); SyncPower(); Relayout(); Invalidate();
    }

    private async void OnPowerStart(object? sender, EventArgs e)
    {
        if (_ptRunning) return;
        if (PtHasReport) { OpenPowerIssue(); return; }

        var dev = Dev;
        string? blocked = PowerTest.Blocked(dev, D.Writable(), D.Simulating());
        if (blocked != null) { _ptMsg = blocked; Invalidate(); return; }
        if (!_ptConsent.Checked) { _ptMsg = Lang.T("pt_need_consent"); Invalidate(); return; }

        _ptRunning = true;
        _ptResult = null;   // only a refusal can survive to here, and it must not stack with what follows
        _ptMsg = null; _ptSavedPath = null; _ptCopied = true; _ptBar = 0; _ptStep = 0; _ptStage = ""; _ptLive = "";
        _ptSteps = PowerTest.StepCount(dev!);
        _ptCts = new CancellationTokenSource();
        RefreshPowerRows(); SyncPower(); Relayout(); Invalidate();

        // The profile to come back to, and the gate that keeps a model-database swap from changing
        // the register map underneath a run that is halfway through writing it.
        var back = D.Current();
        using var gate = D.EcSession();
        try
        {
            var sink = new Progress<PowerTest.Progress>(OnPowerProgress);
            _ptTask = PowerTest.RunAsync(dev!, D.AppVersion(), D.Firmware(), sink, _ptCts.Token);
            _ptResult = await _ptTask;
            if (PtHasReport) PreparePowerReport(dev!);
        }
        catch (OperationCanceledException)
        {
            // The user's own doing, and nothing was measured. Saying anything would be noise.
        }
        catch (Exception ex)
        {
            _ptMsg = string.Format(Lang.T("rep_read_fail"), AppLifecycle.DescribeEcFailure(ex));
        }
        finally
        {
            _ptRunning = false;
            _ptCts?.Dispose(); _ptCts = null;
            _ptTask = null;
            _ptStep = _ptSteps - 1;
            _ptBar = 1;
            // Only put the machine back if the run actually moved it. A refused or cancelled
            // pre-flight wrote nothing, and "restoring" it would write the recipe, log a profile
            // change and flash the OSD for a run that never started. A missing result means the
            // path threw, where a write may well have happened, so that one does restore.
            if (_ptResult is null or { Wrote: true })
            {
                try { D.SetProfile(back); } catch { }
                // A curve applied from the editor carries no preset name, so the profile path above
                // does not bring it back. The phase recipes necessarily overwrote the fan mode byte.
                try { D.RestoreActiveCurve(); } catch { }
            }
            RefreshPowerRows(); SyncPower(); Relayout(); Invalidate();
        }
    }

    private void OnPowerProgress(PowerTest.Progress p)
    {
        _ptStep = p.StepIndex;
        _ptSteps = Math.Max(1, p.StepCount);
        _ptStage = p.Stage;
        _ptLive = p.Live;
        // Settling takes the first fifth of a step's bar so the load does not appear to restart it.
        double within = p.Stage switch
        {
            "settle" => 0.2 * p.Fraction,
            "load" => 0.2 + 0.8 * p.Fraction,
            "check" => 0,
            _ => p.Fraction,
        };
        _ptBar = (float)Math.Clamp((p.StepIndex + within) / _ptSteps, 0, 1);
        RefreshPowerRows();
        Invalidate();
    }

    private void PreparePowerReport(DeviceProfile dev)
    {
        if (_ptResult is not { } r) return;
        string report = PowerTest.BuildReport(r, dev);
        _ptCopied = Ui.CopyText(report);
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fwTag = string.IsNullOrEmpty(D.Firmware()) ? "unknown" : D.Firmware().Replace('.', '_');
            _ptSavedPath = Path.Combine(dir, $"ghostdeck-power-test-{fwTag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(_ptSavedPath, report, new UTF8Encoding(false));
        }
        catch { _ptSavedPath = null; }
    }

    private void OpenPowerIssue()
    {
        try { Process.Start(new ProcessStartInfo(BuildPowerIssueUrl()) { UseShellExecute = true }); } catch { }
    }

    private string BuildPowerIssueUrl()
    {
        var r = _ptResult;
        string title = $"[Power] {ModelName()} ({D.Firmware()})";
        // Like the other two wizards: the paste field is deliberately NOT prefilled, because the
        // full report is on the clipboard and reloading a prefilled URL would wipe what was pasted.
        return RepoUrl + "/issues/new?template=power-test.yml&labels=power-test"
            + "&title=" + Uri.EscapeDataString(title)
            + "&model=" + Uri.EscapeDataString(ModelName())
            + "&firmware=" + Uri.EscapeDataString(D.Firmware())
            + "&verdict=" + Uri.EscapeDataString(r == null ? "" : PowerTest.Summary(r));
    }

    private void LayoutPower(int top, int ox, int oy)
    {
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        int secH = new Font("Segoe UI", 9.5f, FontStyle.Bold).Height;

        _ptTop = top;
        _introH = TextRenderer.MeasureText(Lang.T("pt_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _ptTop + _introH + 18;
        _ptCard.Location = new Point(Pad + ox, _contentTop + oy);
        _ptCard.SetWidth(_leftW);
        _ptWritesY = _contentTop + _ptCard.Height + 16;
        // Boards with curve tables list around thirty addresses, which is several lines.
        _ptWritesH = TextRenderer.MeasureText(PtWriteList(Dev), WritesFont,
            new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;

        _ptRowsTop = _contentTop + secH + 14;
        int ry = _ptRowsTop;
        for (int i = 0; i < _ptRows.Length; i++)
        {
            _ptRows[i].SetBounds(_rightX + ox, ry + oy, rightW, 52);
            if (PtRowShown(i)) ry += 60;
        }
        // The consent sentence is long in every language and must never be cut, so it wraps across
        // the whole column and everything below it follows its measured height.
        _ptConsentY = ry + 8;
        int boxW = (int)Math.Ceiling(18 * DeviceDpi / 96f) + 10;
        int consentH = Math.Max((int)Math.Ceiling(26 * DeviceDpi / 96f),
            TextRenderer.MeasureText(_ptConsent.Text, new Font("Segoe UI", 10.5f),
                new Size(Math.Max(40, rightW - boxW - 6), 0), TextFormatFlags.WordBreak).Height + 8);
        _ptConsent.Wrap = true;
        _ptConsent.SetBounds(_rightX + ox, _ptConsentY + oy, rightW, consentH);

        _ptBtnY = _ptConsentY + consentH + 16;
        int bw = Math.Min(320, rightW - 180);
        // While running, Cancel takes the primary slot because Start is hidden.
        if (_ptRunning) _ptSecondary.SetBounds(_rightX + ox, _ptBtnY + oy, bw, 44);
        else
        {
            _ptStart.SetBounds(_rightX + ox, _ptBtnY + oy, bw, 44);
            _ptSecondary.SetBounds(_rightX + bw + 10 + ox, _ptBtnY + oy, 170, 44);
        }
        _ptBarY = _ptBtnY + 60;

        // bottoms in CONTENT coords (a child's .Bottom is client-side once the page is scrolled)
        int leftBottom = _ptWritesY + 20 + _ptWritesH + 14 + 44;   // address block + firmware pill
        int rightBottom = _ptBarY + 180 + (_ptCopied ? 0 : ClipWarnHeight(rightW));
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    private void PaintPower(Graphics g)
    {
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        var dev = Dev;
        var secFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        // ---- left column: intro + warning card (child) + the exact addresses + firmware pill ----
        TextRenderer.DrawText(g, Lang.T("pt_intro"), IntroFont,
            new Rectangle(Pad, _ptTop, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        TextRenderer.DrawText(g, Lang.T("pt_writes"), secFont, new Point(Pad, _ptWritesY), Theme.Muted);
        TextRenderer.DrawText(g, PtWriteList(dev), WritesFont,
            new Rectangle(Pad, _ptWritesY + 20, _leftW, _ptWritesH + 4), Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);
        PaintFirmwarePill(g, _ptWritesY + 20 + _ptWritesH + 14);

        // ---- right column: checklist label ----
        TextRenderer.DrawText(g, Lang.T("pt_steps"), secFont, new Point(_rightX, _contentTop), Theme.Muted);

        int y = _ptBarY;
        if (_ptRunning)
        {
            string stage = _ptStage switch
            {
                "settle" => Lang.T("pt_stage_settle"),
                "load" => Lang.T("pt_stage_load"),
                "write" => Lang.T("pt_stage_write"),
                "revert" => Lang.T("pt_stage_revert"),
                "check" => Lang.T("pt_stage_check"),
                _ => Lang.T("pt_stage_read"),
            };
            TextRenderer.DrawText(g, stage + (_ptLive.Length > 0 ? "   " + _ptLive : ""),
                new Font("Segoe UI", 10f, FontStyle.Bold), new Point(_rightX, y - 4), Theme.Accent);
            var track = new RectangleF(_rightX, y + 20, rightW, 12);
            using (var path = Theme.RoundRect(track, 6))
            { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            using (var path = Theme.RoundRect(new RectangleF(_rightX, y + 20, Math.Max(12, rightW * _ptBar), 12), 6))
            { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
            y += 48;
        }

        // ---- right column: verdict, then whatever needs saying ----
        if (_ptResult is { } r && !_ptRunning)
        {
            // Green only for a clean run: a probe that was accepted but did NOT clear is the one
            // outcome that leaves a value set, and a run the machine was busy during produced
            // numbers about the wrong thing. Neither may read as success.
            var col = r.Aborted != null || PowerTest.WasBusy(r) ? Theme.Amber
                    : r.Fourth is null or { Accepted: true, Cleared: true } ? Theme.Green
                    : Theme.Amber;
            string verdict = PowerTest.Summary(r);
            var vf = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            int vh = TextRenderer.MeasureText(verdict, vf, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, verdict, vf, new Rectangle(_rightX, y, rightW, vh + 6), col,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += vh + 10;
            y += PaintSaved(g, _rightX, y, rightW, _ptSavedPath, _ptCopied) + 8;
        }

        if (_ptMsg != null)
            TextRenderer.DrawText(g, _ptMsg, new Font("Segoe UI", 10.5f), new Rectangle(_rightX, y, rightW, 70),
                Theme.Amber, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    // Where the report went, and - when the clipboard refused it - the line that stops the user
    // pasting whatever happened to be there already. Returns the height it used.
    private int PaintSaved(Graphics g, int x, int y, int w, string? path, bool copied)
    {
        int sh = 0;
        if (path != null)
        {
            string saved = string.Format(Lang.T("rep_saved_to"), path);
            var sf = new Font("Segoe UI", 9f);
            sh = TextRenderer.MeasureText(saved, sf, new Size(w, 0), TextFormatFlags.WordBreak).Height;
            TextRenderer.DrawText(g, saved, sf, new Rectangle(x, y, w, sh + 4), Theme.Muted, TextFormatFlags.WordBreak);
            sh += 6;
        }
        // Deliberately NOT tied to the path: a run that lost the clipboard AND the file is the one
        // case where saying nothing would leave the user pasting whatever was there before.
        if (copied) return sh;
        var wf = new Font("Segoe UI", 10f, FontStyle.Bold);
        string warn = Lang.T("rep_clip_fail");
        int wh = ClipWarnHeight(w);
        TextRenderer.DrawText(g, warn, wf, new Rectangle(x, y + sh + 2, w, wh), Theme.Amber,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return sh + wh + 6;
    }

    // Layout has to reserve this too, or the warning lands below the scrollable area and the one
    // person who needs to read it is the one who cannot reach it.
    private static int ClipWarnHeight(int w) =>
        TextRenderer.MeasureText(Lang.T("rep_clip_fail"), new Font("Segoe UI", 10f, FontStyle.Bold),
            new Size(Math.Max(40, w), 0), TextFormatFlags.WordBreak).Height + 10;

    // The firmware pill closes the left column on every sub-tab.
    private void PaintFirmwarePill(Graphics g, int y)
    {
        var pill = new RectangleF(Pad, y, _leftW, 44);
        using (var path = Theme.RoundRect(pill, 11))
        { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
        var lf = new Font("Segoe UI", 10f);
        TextRenderer.DrawText(g, Lang.T("st_firmware"), lf, new Rectangle(Pad + 16, (int)pill.Y, 180, 44), Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        int lw = TextRenderer.MeasureText(Lang.T("st_firmware"), lf).Width;
        TextRenderer.DrawText(g, string.IsNullOrEmpty(D.Firmware()) ? "—" : D.Firmware(), new Font("Consolas", 11f, FontStyle.Bold),
            new Rectangle(Pad + 16 + lw + 12, (int)pill.Y, _leftW - lw - 40, 44), Theme.Accent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    // =================================================================
    //  themed custom controls
    // =================================================================
    private sealed class StepRowT : Control
    {
        private const int StatusW = 150, Circle = 26, Cx = 28;
        private int _num;
        private string _name;
        public Color Tint;
        private bool _done, _current;
        private float _doneA, _glowA;

        public StepRowT(int num, string name, Color color)
        { _num = num; _name = name; Tint = color; DoubleBuffered = true; ResizeRedraw = true; }

        /// <summary>The power test names its rows from the model database, which can change at runtime.</summary>
        public string Label { set { if (_name == value) return; _name = value; Invalidate(); } }

        /// <summary>Numbering follows the rows actually shown, so a hidden row leaves no gap.</summary>
        public int Number { set { if (_num == value) return; _num = value; Invalidate(); } }

        public void SetState(bool done, bool current) { _done = done; _current = current; Invalidate(); }

        public bool Animate()
        {
            bool a = Approach(ref _doneA, _done ? 1 : 0, _done ? 0.10f : 0.20f);
            bool b = Approach(ref _glowA, _current ? 1 : 0, _current ? 0.12f : 0.18f);
            if (a || b) Invalidate();
            return a || b;
        }
        private static bool Approach(ref float v, float t, float s)
        { if (v == t) return false; v += (t - v) * s; if (Math.Abs(t - v) < 0.005f) { v = t; return false; } return true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Surface);
            if (_glowA > 0.01f)
            {
                using var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12);
                int al = (int)(_glowA * 255);
                using var b = new SolidBrush(Color.FromArgb((int)(al * 0.12), Theme.Accent)); g.FillPath(b, path);
                using var pen = new Pen(Color.FromArgb((int)(al * 0.6), Theme.Accent), 1.4f); g.DrawPath(pen, path);
            }
            int cy = Height / 2;
            var circle = new RectangleF(Cx - Circle / 2f, cy - Circle / 2f, Circle, Circle);
            if (_done)
            {
                float pop = 1 - (1 - _doneA) * (1 - _doneA);
                float dd = Circle * (0.85f + 0.15f * pop);
                using (var b = new SolidBrush(Tint)) g.FillEllipse(b, Cx - dd / 2f, cy - dd / 2f, dd, dd);
                using var pen = new Pen(System.Drawing.Color.White, Math.Max(2f, dd * 0.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float ss = dd * 0.26f * pop;
                g.DrawLines(pen, new[] { new PointF(Cx - ss, cy + ss * 0.1f), new PointF(Cx - ss * 0.2f, cy + ss * 0.8f), new PointF(Cx + ss, cy - ss * 0.7f) });
            }
            else if (_current)
            {
                using var pen = new Pen(Theme.Accent, 2.5f); g.DrawEllipse(pen, circle);
                using var b = new SolidBrush(System.Drawing.Color.FromArgb(60, Theme.Accent));
                float id = Circle * 0.42f; g.FillEllipse(b, Cx - id / 2f, cy - id / 2f, id, id);
            }
            else
            {
                using var pen = new Pen(Theme.BorderStrong, 2f); g.DrawEllipse(pen, circle);
                TextRenderer.DrawText(g, _num.ToString(), new Font("Segoe UI", 9f, FontStyle.Bold), Rectangle.Round(circle), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            int nx = Cx + Circle / 2 + 16;
            TextRenderer.DrawText(g, _name, new Font("Segoe UI", 11f, FontStyle.Bold),
                new Rectangle(nx, 6, Width - nx - StatusW - 6, Height - 12), _done || _current ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Lang.T(_done ? "rep_captured" : "rep_pending"), new Font("Segoe UI", 9.5f, _done ? FontStyle.Bold : FontStyle.Regular),
                new Rectangle(Width - StatusW, 6, StatusW - 10, Height - 12), _done ? Theme.Green : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    private sealed class InfoCardT : Control
    {
        private const int LeftPad = 46, RightPad = 18, TopPad = 15;
        private static readonly Font IconFont = new("Segoe MDL2 Assets", 13f);
        private readonly string _icon;
        private readonly (string text, string? url)[] _items;
        private readonly List<(Rectangle rect, string text)> _paras = new();
        private readonly List<LinkLabel> _links = new();
        private readonly Font _font = new("Segoe UI", 10.5f);

        public InfoCardT(string icon, (string text, string? url)[] items, int width)
        {
            _icon = icon; _items = items; DoubleBuffered = true; ResizeRedraw = true; Width = width;
            Build();
        }

        private void Build()
        {
            foreach (var l in _links) Controls.Remove(l);
            _links.Clear(); _paras.Clear();
            int innerW = Width - LeftPad - RightPad;
            var linkFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            int y = TopPad, gap = 0;
            foreach (var (text, url) in _items)
            {
                if (url == null)
                {
                    int h = TextRenderer.MeasureText(text, _font, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
                    _paras.Add((new Rectangle(LeftPad, y, innerW, h), text));
                    y += h; gap = 10;
                }
                else
                {
                    int h = TextRenderer.MeasureText(text, linkFont, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
                    var link = new LinkLabel { Text = text, AutoSize = false, Size = new Size(innerW, h), Location = new Point(LeftPad, y), Font = linkFont, LinkBehavior = LinkBehavior.HoverUnderline };
                    string target = url;
                    link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } };
                    Controls.Add(link); _links.Add(link);
                    y += h; gap = 6;
                }
                y += gap;
            }
            Height = y - gap + TopPad;
            ApplyTheme();
        }

        public void SetWidth(int w) { if (Width == w) return; Width = w; Build(); }

        public void ApplyTheme()
        {
            var (bg, _, _) = Colors();
            foreach (var l in _links) { l.BackColor = bg; l.LinkColor = Theme.Accent; l.ActiveLinkColor = Theme.Accent; }
            Invalidate();
        }

        private static (Color bg, Color bd, Color fg) Colors() => Theme.Dark
            ? (Color.FromArgb(0x33, 0x2C, 0x1A), Color.FromArgb(0x5A, 0x4A, 0x22), Color.FromArgb(0xE0, 0xB0, 0x55))
            : (Color.FromArgb(0xFE, 0xF6, 0xE7), Color.FromArgb(0xF3, 0xDC, 0xA9), Color.FromArgb(0xB0, 0x6A, 0x10));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Surface);
            var (bg, bd, fg) = Colors();
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, 12)) { using var b = new SolidBrush(bg); g.FillPath(b, path); using var p = new Pen(bd); g.DrawPath(p, path); }
            // Segoe MDL2 like the rest of the app's icons, and a MEASURED box: the fixed 22 px it
            // used to get is shorter than the glyph as soon as the display scales past 100 %, and
            // GDI clips the difference away.
            int ih = TextRenderer.MeasureText(_icon, IconFont, Size.Empty, TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(g, _icon, IconFont, new Rectangle(12, TopPad, 30, ih + 2), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            foreach (var (rect, text) in _paras)
                TextRenderer.DrawText(g, text, _font, rect, fg, TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left);
        }
    }
}
