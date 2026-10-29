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

    // msiName is the tile as MSI Center 2.0.48 names it, the version the capture was designed
    // around; altKey names the same tile in the other vendor apps (MSI Center Pro on the business
    // line, MSI Center 2.0.49+ with its renamed bottom tile), because an owner reading "EXTREME
    // PERFORMANCE" with a "High Performance" tile in front of him has no way to know they are the
    // same thing (#170). Short alternatives go into the instruction line; the long one only
    // into the row's second line and tooltip.
    private static readonly (ProfileId id, string msiName, string? altKey, bool inline)[] Steps =
    {
        (ProfileId.Silent, "SILENT", "rep_alt_silent", false), (ProfileId.Balanced, "BALANCED", null, false),
        (ProfileId.Extreme, "EXTREME PERFORMANCE", "rep_alt_extreme", true), (ProfileId.SuperBattery, "SUPER BATTERY", "rep_alt_sb", true),
    };
    private readonly ToolTip _tip = new() { AutoPopDelay = 12000 };
    private static readonly byte[] SnapshotAddrs = { 0x34, 0xD2, 0xD4, 0xEB, 0xF2, 0xF4, 0xD7, 0xEF };

    private readonly byte[]?[] _dumps = new byte[Steps.Length][];
    // A machine with no EC image to capture (backup path, or monitoring-only with the blocks
    // answering): the scenarios are captured slot by slot from the data blocks instead.
    private readonly Dictionary<string, int>?[] _blockDumps = new Dictionary<string, int>?[Steps.Length];
    private bool UseBlocks => Ec.OnBackupPath || (!D.Status().Known && MsiTelemetry.Available());
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
    private static readonly Font IntroFont = Dpi.Font("Segoe UI", 10.5f);

    // ---- sub-tabs: segment 0 is the start screen, segments 1..4 map to _sub 0..3 ----
    // (_sub keeps its 0 = profiles / 1 = curve / 2 = power / 3 = two tables meaning everywhere else on the page)
    private readonly SubTabs _subTabs = new(
        new[] { Lang.T("subtab_start"), Lang.T("subtab_profiles"), Lang.T("subtab_curve"), Lang.T("subtab_power"), Lang.T("tt_title") },
        new[] { "\uE80F", "\uE8AB", "\uE9D2", "\uE945", "\uE7E8" });
    private int _sub;
    private int _subTop;
    // The page opens on a start screen that says what the three tests are for and which one to
    // run; picking a tile (or a sub-tab) leaves it, and the Start segment brings it back.
    private bool _landing = true;
    private readonly Rectangle[] _landCards = new Rectangle[4];
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
    private static readonly Font WritesFont = Dpi.Font("Consolas", 11f, FontStyle.Bold);

    // ---- two-table test (#293): the measurement is Core/TwoTableTest.cs, the same one the Fan curve
    // page's diagnostics run; this sub-tab wraps it the way the power test is wrapped - explanation,
    // consent, progress, result, the report on the clipboard and the GitHub form ----
    private readonly Button _ttStart = new();
    private readonly Button _ttSecondary = new();     // Cancel while running, Start over once there is a result
    private readonly CheckItem _ttConsent = new(Lang.T("tt_consent_box"), false);
    private InfoCardT _ttCard = null!;
    private readonly StepRowT[] _ttRows = new StepRowT[TwoTableTest.Pairs.Length];
    private CancellationTokenSource? _ttCts;
    private Task<TwoTableTest.Result?>? _ttTask;      // the EC restore runs on the worker; app exit waits for it
    private bool _ttRunning;
    private TwoTableTest.Result? _ttResult;
    private string? _ttSavedPath;
    private string? _ttMsg;                           // blocked reason / missing consent / EC failure
    private string _ttLive = "";
    private int _ttStep;
    private DateTime _ttPairStart;                    // the bar advances with the clock between the per-pair callbacks
    private float _ttBar;
    private bool _ttCopied;
    private int _ttTop, _ttBtnY, _ttBarY, _ttWritesY, _ttWritesH, _ttConsentY;
    private bool TtHasReport => _ttResult is { Steps.Count: > 0 };

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
    // #172: rows and buttons that held text in fixed pixels (34 px step rows, 44 px buttons and
    // firmware pill) keep those values as the floor and grow with the text above about 150 %
    private int[] _stepH = new int[5];
    private static readonly Font StepF = Dpi.Font("Segoe UI", 10.5f);
    private static readonly Font PillMonoF = Dpi.Font("Consolas", 11f, FontStyle.Bold);
    private static int PillH => Math.Max(44, PillMonoF.Height + 14);
    // Space a section label ("Addresses the test may write") takes above what follows it: its
    // own font height, not a fixed 20 px that the label outgrows above 150 %.
    private static int SecH => Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold).Height + 4;
    // The progress bar sits under the stage line ("Pair 3 of 6 - ...") by that line's font height,
    // not a fixed 20 px (the two overlapped above 125 %).
    private static int StageH => Dpi.Font("Segoe UI", 10f, FontStyle.Bold).Height + 4;
    private int BtnH => Math.Max(44, Ui.ButtonHeight(_capture.Font));
    private static string[] CurveSteps() =>
        new[] { Lang.T("rep_curve_s1"), Lang.T("rep_curve_s2"), Lang.T("rep_curve_s3"), Lang.T("rep_curve_s4"), Lang.T("rep_curve_s5") };

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

        _curveCard = new InfoCardT("", CurveCardItems(), _leftW);
        Controls.Add(_curveCard);

        _card = new InfoCardT("", CardItems(), _leftW);
        Controls.Add(_card);

        _rows = Steps.Select((s, i) => new StepRowT(i + 1, s.msiName, Theme.Profile(D.ColorOf(s.id)))).ToArray();
        for (int i = 0; i < _rows.Length; i++)
        {
            if (Steps[i].altKey is { } ak) { _rows[i].Alt = Lang.T(ak); _tip.SetToolTip(_rows[i], Lang.T(ak)); }
            Controls.Add(_rows[i]);
        }

        Ui.StylePrimary(_capture);
        _capture.UseMnemonic = false;   // "Finish & open GitHub" carries a literal &
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
            Array.Clear(_blockDumps, 0, _blockDumps.Length);
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
        _ptCard = new InfoCardT("", PtCardItems(), _leftW);
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
        _ptStart.UseMnemonic = false;   // "Finish & open GitHub" carries a literal &
        _ptStart.Click += OnPowerStart;
        Controls.Add(_ptStart);
        Ui.StyleGhost(_ptSecondary);
        _ptSecondary.Click += OnPowerSecondary;
        Controls.Add(_ptSecondary);
        // Ticking the box answers whatever the page was complaining about, so the complaint goes.
        _ptConsent.Toggled += _ => { _ptMsg = null; SyncPower(); Invalidate(); };
        Controls.Add(_ptConsent);

        // ---- two-table test ----
        _ttCard = new InfoCardT("", TtCardItems(), _leftW);
        Controls.Add(_ttCard);
        for (int i = 0; i < _ttRows.Length; i++)
        {
            var (t1, t2) = TwoTableTest.Pairs[i];
            _ttRows[i] = new StepRowT(i + 1, $"{t1} % / {t2} %", Theme.Accent) { Alt = Lang.T(TtPairKey(i)) };
            Controls.Add(_ttRows[i]);
        }
        Ui.StylePrimary(_ttStart);
        _ttStart.UseMnemonic = false;   // "Finish & open GitHub" carries a literal &
        _ttStart.Click += OnTtStart;
        Controls.Add(_ttStart);
        Ui.StyleGhost(_ttSecondary);
        _ttSecondary.Click += OnTtSecondary;
        Controls.Add(_ttSecondary);
        _ttConsent.Toggled += _ => { _ttMsg = null; SyncTt(); Invalidate(); };
        Controls.Add(_ttConsent);

        _anim.Tick += (_, _) => OnAnim();
        Resize += (_, _) => Relayout();
        RefreshSteps();
        RefreshPowerRows();
        RefreshTtRows();
        SyncSub();
    }

    // Silent + Balanced + Extreme + fourth mode + restore. The fourth row is hidden on boards
    // whose database entry records no fourth shift value.
    private const int PtRowCount = 5;
    private const int PtRestoreRow = 4;

    /// <summary>Open a specific sub-tab (0 = profiles, 1 = fan curve, 2 = power test, 3 = two-table test). Used by deep links.</summary>
    public void SetSubTab(int sub)
    {
        _landing = false;
        _sub = Math.Clamp(sub, 0, 3);
        _subTabs.SetActive(_sub + 1);
        SyncSub(); Relayout(); Invalidate();
    }

    // Show only the active sub-tab's controls (the rest are hand-painted, gated in OnPaint).
    private void SyncSub()
    {
        bool prof = !_landing && _sub == 0, curve = !_landing && _sub == 1, power = !_landing && _sub == 2, tt = !_landing && _sub == 3;
        _ttCard.Visible = tt;
        _ttStart.Visible = tt;
        _ttConsent.Visible = tt && !_ttRunning;
        foreach (var r in _ttRows) r.Visible = tt;
        if (tt) { RefreshTtRows(); SyncTt(); } else _ttSecondary.Visible = false;
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
        if (!_ttRunning) _ttMsg = null;
        Relayout(); RefreshSteps(); SyncSub(); Invalidate();
    }

    // A newer model database can add (or correct) the fourth shift value while the page is open.
    // Rebuilding the checklist is all that is needed; a run in progress holds the swap gate, so
    // this can never land mid-test.
    public override void OnDeviceDbChanged() { RefreshPowerRows(); SyncSub(); Relayout(); Invalidate(); }

    // The three info cards capture their texts when built, so a language change has to hand
    // them the new ones; the lists live here so the constructor and OnLanguageChanged agree.
    private static (string, string?)[] CardItems() => new (string, string?)[]
    {
        (Lang.T("rep_need_msi"), null),
        (Lang.T("rep_msi_pro"), null),
        (Lang.T("rep_msi_tip"), null),
        (Lang.T("rep_msi_download"), null),
        (Lang.T("rep_dl_version"), "https://msi-center.en.uptodown.com/windows/download/1045738268"),
        (Lang.T("rep_dl_repo"), "https://msi-center.en.uptodown.com/windows/versions"),
        (Lang.T("rep_msi_clean"), null),
        (Lang.T("rep_uninstaller_link"), "https://download.msi.com/uti_exe/nb/CleanCenterMaster.zip"),
    };
    private static (string, string?)[] CurveCardItems() => new (string, string?)[]
    {
        (Lang.T("rep_curve_warn"), null),
        (Lang.T("rep_curve_why"), null),
    };
    private static (string, string?)[] PtCardItems() => new (string, string?)[]
    {
        (Lang.T("pt_warn_write"), null),
        (Lang.T("pt_warn_fourth"), null),
        (Lang.T("pt_warn_heat"), null),
        (Lang.T("pt_warn_restore"), null),
    };

    public override void OnLanguageChanged()
    {
        _subTabs.SetLabels(new[]
        {
            Lang.T("subtab_start"), Lang.T("subtab_profiles"), Lang.T("subtab_curve"), Lang.T("subtab_power"), Lang.T("tt_title"),
        });
        // Everything that captured a text at build time: the four cards, the "start over"
        // captions, the consent lines and the rows' second names (#170: switching the language
        // left the cards and these captions in the old one).
        _card.SetItems(CardItems()); _curveCard.SetItems(CurveCardItems()); _ptCard.SetItems(PtCardItems()); _ttCard.SetItems(TtCardItems());
        _restart.Text = _curveRestart.Text = Lang.T("rep_restart");
        _ptConsent.Text = Lang.T("pt_consent");
        _ttConsent.Text = Lang.T("tt_consent_box");
        for (int i = 0; i < _rows.Length; i++)
            if (Steps[i].altKey is { } ak) { _rows[i].Alt = Lang.T(ak); _tip.SetToolTip(_rows[i], Lang.T(ak)); }
        for (int i = 0; i < _ttRows.Length; i++) _ttRows[i].Alt = Lang.T(TtPairKey(i));
        // Button captions and checklist rows are re-derived by the same refreshes OnEnter uses.
        RefreshSteps(); RefreshCurve(); RefreshPowerRows(); RefreshTtRows(); SyncSub(); Relayout(); Invalidate();
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
        Ui.StylePrimary(_ttStart);
        Ui.StyleGhost(_ttSecondary);
        _ttCard.ApplyTheme();
        foreach (var r in _ttRows) { r.Tint = Theme.Accent; r.Invalidate(); }
        _ttConsent.Invalidate();
        _subTabs.Invalidate();
    }

    // Wrapped so the scroll extent is recomputed afterwards - see ThemedPage.LayoutAndSyncScroll.
    private void Relayout() => LayoutAndSyncScroll(RelayoutPass);

    private void RelayoutPass()
    {
        // Content coordinates offset by AutoScrollPosition when they reach a child's Location -
        // WinForms treats those as client coords and shifts children by the scroll delta, while
        // OnPaint draws through ApplyScroll. Same rule as SettingsPage/ScenariosPage.
        int ox = AutoScrollPosition.X, oy = AutoScrollPosition.Y;
        int titleH = Dpi.Font("Segoe UI", 18f, FontStyle.Bold).Height;
        _subTop = 24 + titleH + 18;
        _subTabs.SetBounds(Pad + ox, _subTop + oy, _subTabs.FitTo(ClientSize.Width - Pad * 2), _subTabs.Height);

        // NB: content coord, NOT _subTabs.Bottom - that one is already shifted by the scroll.
        int top = _subTop + _subTabs.Height + 26;
        if (_landing) LayoutLanding(top);
        else if (_sub == 0) LayoutProfiles(top, ox, oy);
        else if (_sub == 1) LayoutCurve(top, ox, oy);
        else if (_sub == 2) LayoutPower(top, ox, oy);
        else LayoutTt(top, ox, oy);
    }

    // =================================================================
    //  start screen
    // =================================================================
    private static readonly Font LandTitleF = Dpi.Font("Segoe UI", 14f, FontStyle.Bold);
    private static readonly Font LandQF = Dpi.Font("Segoe UI", 11.5f, FontStyle.Bold);
    private static readonly Font LandDF = Dpi.Font("Segoe UI", 10f);
    private static readonly Font LandFootF = Dpi.Font("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font LandGlyphF = Dpi.Font("Segoe MDL2 Assets", 16f);
    private static readonly string[] LandGlyphs = { "\uE8AB", "\uE9D2", "\uE945", "\uE7E8" };
    private const int LandPad = 20, LandGap = 14, LandIconBox = 42;

    private static string LandIntro12 => Lang.T("rep_home_intro1") + "\n\n" + Lang.T("rep_home_intro2");
    // Four cards, one per sub-tab: the three report wizards and, fourth, the two-table test (#293).
    private const int LandN = 4;
    private static string LandQ(int i) => Lang.T(i == 0 ? "rep_home_q1" : i == 1 ? "rep_home_q2" : i == 2 ? "rep_home_q3" : "rep_home_q4");
    private static string LandD(int i) => Lang.T(i == 0 ? "rep_home_d1" : i == 1 ? "rep_home_d2" : i == 2 ? "rep_home_d3" : "rep_home_d4");
    private static string LandF(int i) => Lang.T(i == 2 ? "rep_home_f_power" : i == 3 ? "rep_home_f_tt" : "rep_home_f_read");
    private string LandTitle(int i) => Lang.T(i == 0 ? "subtab_profiles" : i == 1 ? "subtab_curve" : i == 2 ? "subtab_power" : "tt_title");

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

        // Equal tiles: four in a row on a wide window, two by two below that, stacked once even
        // two would drop under a readable width.
        int cols = avail >= LandN * 280 + (LandN - 1) * LandGap ? LandN : avail >= 2 * 300 + LandGap ? 2 : 1;
        int cardW = (avail - (cols - 1) * LandGap) / cols;
        int innerW = cardW - LandPad * 2;

        // Same height for all, decided by the tallest: a ragged row reads as unrelated boxes
        // rather than one choice.
        int bodyH = 0;
        for (int i = 0; i < LandN; i++)
        {
            int qH = TextRenderer.MeasureText(LandQ(i), LandQF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            int dH = TextRenderer.MeasureText(LandD(i), LandDF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            int fH = TextRenderer.MeasureText(LandF(i), LandFootF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            bodyH = Math.Max(bodyH, qH + 10 + dH + 16 + fH);
        }
        int cardH = LandPad + LandIconBox + 14 + bodyH + LandPad;

        int rows = (LandN + cols - 1) / cols;
        for (int i = 0; i < LandN; i++)
            _landCards[i] = new Rectangle(Pad + (i % cols) * (cardW + LandGap), _landCardsY + (i / cols) * (cardH + LandGap), cardW, cardH);

        int bottom = _landCardsY + rows * cardH + (rows - 1) * LandGap;
        AutoScrollMinSize = new Size(Pad * 2 + 2 * 300 + LandGap, bottom + 40);
    }

    private void PaintLanding(Graphics g)
    {
        int avail = ClientSize.Width - Pad * 2;
        Ui.DrawText(g, Lang.T("rep_home_title"), LandTitleF, new Point(Pad, _landTitleY), Theme.Text);
        int introH = _landI3Y - _landIntroY;
        if (_landTwoCol)
        {
            Ui.DrawText(g, Lang.T("rep_home_intro1"), IntroFont,
                new Rectangle(Pad, _landIntroY, _landColW, introH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
            Ui.DrawText(g, Lang.T("rep_home_intro2"), IntroFont,
                new Rectangle(Pad + _landColW + Gutter, _landIntroY, _landColW, introH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
        else
        {
            Ui.DrawText(g, LandIntro12, IntroFont,
                new Rectangle(Pad, _landIntroY, avail, introH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
        Ui.DrawText(g, Lang.T("rep_home_intro3"), IntroFont,
            new Rectangle(Pad, _landI3Y, avail, _landCardsY - _landI3Y), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);

        for (int i = 0; i < LandN; i++)
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

            Ui.DrawText(g, LandTitle(i), Dpi.Font("Segoe UI", 12f, FontStyle.Bold),
                new Rectangle(iconR.Right + 14, iconR.Y, r.Width - LandIconBox - LandPad * 2 - 14, LandIconBox),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);   // dpi-ok: short step names in a column sized with the page; last resort for a very long translation

            int innerW = r.Width - LandPad * 2;
            int y = iconR.Bottom + 14;
            int qH = TextRenderer.MeasureText(LandQ(i), LandQF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            Ui.DrawText(g, LandQ(i), LandQF, new Rectangle(r.X + LandPad, y, innerW, qH + 2),
                hover ? Theme.Accent : Theme.Text, TextFormatFlags.WordBreak);
            y += qH + 10;
            Ui.DrawText(g, LandD(i), LandDF,
                new Rectangle(r.X + LandPad, y, innerW, r.Bottom - LandPad - y), Theme.Muted, TextFormatFlags.WordBreak);

            int fH = TextRenderer.MeasureText(LandF(i), LandFootF, new Size(innerW, 0), TextFormatFlags.WordBreak).Height;
            Ui.DrawText(g, LandF(i), LandFootF,
                new Rectangle(r.X + LandPad, r.Bottom - LandPad - fH, innerW, fH + 2), Theme.Faint, TextFormatFlags.WordBreak);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_landing) return;
        var p = ContentPoint(e.Location);
        int h = -1;
        for (int i = 0; i < LandN; i++) if (_landCards[i].Contains(p)) { h = i; break; }
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
        for (int i = 0; i < LandN; i++)
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

        int secH = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold).Height;
        _introY = top;
        _introH = TextRenderer.MeasureText(Lang.T("rep_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _introY + _introH + 18;
        _rowsTop = _contentTop + secH + 14;

        _card.Location = new Point(Pad + ox, _contentTop + oy);
        _card.SetWidth(_leftW);

        int ry = _rowsTop;
        // Two lines of text per row since the rows name the tile in the other MSI apps as well.
        foreach (var r in _rows) { r.SetBounds(_rightX + ox, ry + oy, rightW, StepRowT.RowH(true)); ry += StepRowT.RowH(true) + 8; }
        _barY = ry + 26;
        _instrTop = _barY + 58;
        var instrFont = Dpi.Font("Segoe UI", 11.5f, FontStyle.Bold);
        _instrH = TextRenderer.MeasureText(Lang.T("rep_all_done"), instrFont, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
        int capW = Math.Min(320, rightW - 180);
        int capY = _capY = _instrTop + _instrH + 18;      // content coord; children get + oy
        _capture.SetBounds(_rightX + ox, capY + oy, capW, BtnH);
        _restart.SetBounds(_rightX + capW + 10 + ox, capY + oy, 170, BtnH);

        // bottoms in CONTENT coords (child .Bottom is client-side once the page is scrolled)
        int leftBottom = _contentTop + _card.Height + 26 + PillH;   // + firmware pill
        int rightBottom = capY + BtnH + 80 + (_copied ? 0 : ClipWarnHeight(rightW));
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    // Two-column layout mirroring the profiles flow: left = intro + info card + firmware pill,
    // right = section label + numbered steps + capture button + result.
    private void LayoutCurve(int top, int ox, int oy)
    {
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        int secH = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold).Height;

        _curveTop = top;   // intro (left)
        _introH = TextRenderer.MeasureText(Lang.T("rep_curve_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _curveTop + _introH + 18;
        _curveCard.Location = new Point(Pad + ox, _contentTop + oy);
        _curveCard.SetWidth(_leftW);

        // right column: section label + 5 steps + button
        _curveStepsTop = _contentTop + secH + 14;
        var steps = CurveSteps();
        for (int i = 0; i < steps.Length; i++)
            _stepH[i] = Math.Max(34, TextRenderer.MeasureText(steps[i], StepF, new Size(rightW - 40, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 6);
        _curveBtnY = _curveStepsTop + _stepH.Sum() + 18;
        _curveBarY = _curveBtnY + BtnH + 18;
        int cbW = Math.Min(320, rightW - 180);
        _curveBtn.SetBounds(_rightX + ox, _curveBtnY + oy, cbW, BtnH);
        _curveRestart.SetBounds(_rightX + cbW + 10 + ox, _curveBtnY + oy, 170, BtnH);

        int leftBottom = _contentTop + _curveCard.Height + 26 + PillH;   // content coords (+ firmware pill)
        int rightBottom = _curveBarY + 80 + (_curveCopied ? 0 : ClipWarnHeight(rightW));
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ApplyScroll(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Ui.DrawText(g, Lang.T("menu_report"), Dpi.Font("Segoe UI", 18f, FontStyle.Bold), new Point(Pad, 24), Theme.Text);

        if (_landing) { PaintLanding(g); return; }
        if (_sub == 1) { PaintCurve(g); return; }
        if (_sub == 2) { PaintPower(g); return; }
        if (_sub == 3) { PaintTt(g); return; }

        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        // left: intro under title, info card (child) already placed, firmware pill below it
        Ui.DrawText(g, Lang.T("rep_intro"), IntroFont,
            new Rectangle(Pad, _introY, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        // content coord, NOT _card.Bottom - a child's Bottom already carries the scroll offset,
        // and OnPaint adds it again through ApplyScroll (see docs/RENDERING.md §5.1)
        PaintFirmwarePill(g, _contentTop + _card.Height + 14);

        // right: section label
        Ui.DrawText(g, Lang.T("rep_section"), Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(_rightX, _contentTop), Theme.Muted);

        // right: progress (only while capturing)
        if (_capturing)
        {
            Ui.DrawText(g, Lang.T("rep_capturing") + $"  {_lastPct}%", Dpi.Font("Segoe UI", 10f, FontStyle.Bold),
                new Point(_rightX, _barY - 30), Theme.Accent);
            var track = new RectangleF(_rightX, _barY, rightW, 12);
            using (var path = Theme.RoundRect(track, 6)) { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            float w = Math.Max(12, rightW * _barValue);
            using (var path = Theme.RoundRect(new RectangleF(_rightX, _barY, w, 12), 6)) { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
        }

        // right: instruction
        bool done = _step >= Steps.Length;
        var cur = done ? default : Steps[_step];
        string tile = done ? "" : cur.inline && cur.altKey is { } ak ? $"{cur.msiName} ({Lang.T(ak)})" : cur.msiName;
        string instr = done ? "✓  " + Lang.T("rep_all_done")
                            : string.Format(Lang.T("rep_step"), _step + 1, Steps.Length) + " - " + string.Format(Lang.T("rep_set_scenario"), tile);
        Ui.DrawText(g, instr, Dpi.Font("Segoe UI", 11.5f, FontStyle.Bold),
            new Rectangle(_rightX, _instrTop, rightW, _instrH + 6), done ? Theme.Green : Theme.Text, TextFormatFlags.WordBreak);
        if (done) PaintSaved(g, _rightX, _capY + BtnH + 10, rightW, _savedPath, _copied);
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
        Ui.DrawText(g, Lang.T("rep_curve_intro"), IntroFont,
            new Rectangle(Pad, _curveTop, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        PaintFirmwarePill(g, _contentTop + _curveCard.Height + 14);

        // ---- right column: section label + numbered steps ----
        Ui.DrawText(g, Lang.T("rep_curve_steps"), Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold), new Point(_rightX, _contentTop), Theme.Muted);
        string[] steps = CurveSteps();
        var numFont = Dpi.Font("Segoe UI", 9f, FontStyle.Bold);
        int circD = Math.Max(24, numFont.Height);
        int ry = _curveStepsTop;
        for (int i = 0; i < steps.Length; i++)
        {
            var circ = new RectangleF(_rightX, ry, circD, circD);
            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, circ);
            Ui.DrawText(g, (i + 1).ToString(), numFont, Rectangle.Round(circ), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Ui.DrawText(g, steps[i], StepF, new Rectangle(_rightX + circD + 12, ry - 4, rightW - circD - 16, _stepH[i]), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            ry += _stepH[i];
        }

        // ---- right column: progress / result (under the capture button) ----
        if (_curveCapturing)
        {
            Ui.DrawText(g, Lang.T("rep_capturing") + $"  {_curvePct}%", Dpi.Font("Segoe UI", 10f, FontStyle.Bold), new Point(_rightX, _curveBarY - 4), Theme.Accent);
            var track = new RectangleF(_rightX, _curveBarY + 20, rightW, 12);
            using (var path = Theme.RoundRect(track, 6)) { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            float fw = Math.Max(12, rightW * _curveBar);
            using (var path = Theme.RoundRect(new RectangleF(_rightX, _curveBarY + 20, fw, 12), 6)) { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
        }
        else if (_curveMsg != null)
        {
            var col = _curveCpuAt >= 0 || _curveGpuAt >= 0 ? (_curveMatch ? Theme.Green : Theme.Amber) : Theme.Red;
            var mf = Dpi.Font("Segoe UI", 10.5f, FontStyle.Bold);
            int mh = TextRenderer.MeasureText(_curveMsg, mf, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
            Ui.DrawText(g, _curveMsg, mf, new Rectangle(_rightX, _curveBarY, rightW, mh + 6), col, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
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
            // design - report the found half instead of a blanket "not located" (#22). The one
            // slider lands in the Fan 1 table whichever tracer the owner typed into it (#293:
            // the Fan 2 values at 0x72), so the table is named by where the curve sits.
            int at = _curveCpuAt >= 0 ? _curveCpuAt : _curveGpuAt;
            bool fan1Table = OneCurveInFan1Table(fcs) >= 0 || (fcs == null && _curveCpuAt >= 0);
            _curveMatch = fcs != null && (fan1Table || at == fcs.GpuSpeedBase);
            _curveMsg = string.Format(Lang.T(fan1Table ? "rep_curve_cpuonly" : "rep_curve_gpuonly"), at)
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

    // Exactly one tracer found, sitting at the shipped Fan 1 (CPU) table: its address, else -1.
    private int OneCurveInFan1Table(FanCurveSpec? fc)
    {
        if (fc == null || (_curveCpuAt >= 0) == (_curveGpuAt >= 0)) return -1;
        int at = _curveCpuAt >= 0 ? _curveCpuAt : _curveGpuAt;
        return at == fc.CpuSpeedBase ? at : -1;
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
        sb.AppendLine("=== GhostDeck - fan-curve verification report ===");
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
            var fc = Devices.Detect(D.Firmware())?.FanCurve;
            int one = OneCurveInFan1Table(fc);
            if (one >= 0)
                sb.AppendLine($"Located in EC dump:  one test curve ({(_curveCpuAt >= 0 ? "the Fan 1" : "the Fan 2")} values) in the Fan 1 / CPU table @ 0x{one:X2};"
                              + " no test curve in the Fan 2 / GPU table (single-fan model or Fan 2 not set)");
            else
            {
                string cpuPart = _curveCpuAt >= 0 ? $"CPU speed table @ 0x{_curveCpuAt:X2}" : "CPU test curve not found";
                string gpuPart = _curveGpuAt >= 0 ? $"GPU speed table @ 0x{_curveGpuAt:X2}"
                                                  : "GPU test curve not found (single-fan model or Fan 2 not set)";
                sb.AppendLine($"Located in EC dump:  {cpuPart}   {gpuPart}");
            }
            if (fc != null) sb.AppendLine($"Shipped map for this model:  CPU 0x{fc.CpuSpeedBase:X2}  GPU 0x{fc.GpuSpeedBase:X2}  → {(_curveMatch ? "MATCH" : "DIFFERENT")}");
            else sb.AppendLine("Shipped map for this model:  (none - model not recognised)");
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
        int one = OneCurveInFan1Table(Devices.Detect(D.Firmware())?.FanCurve);
        string found =
            one >= 0 ? $"one curve @ 0x{one:X2}, the Fan 1 table" + suffix + "; Fan 2 table not set (single-fan model?)"
            : _curveCpuAt >= 0 && _curveGpuAt >= 0 ? $"CPU @ 0x{_curveCpuAt:X2}, GPU @ 0x{_curveGpuAt:X2}" + suffix
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
        bool busy = _capturing || _ptRunning || _ttRunning;
        foreach (var r in _rows) busy |= r.Animate();
        foreach (var r in _ptRows) busy |= r.Animate();
        foreach (var r in _ttRows) busy |= r.Animate();
        if (_ttRunning)
        {
            // The test reports pair boundaries only; between them the bar follows the clock. Only
            // the bar's own strip is repainted, and only when it moved a visible amount.
            double within = Math.Clamp((DateTime.Now - _ttPairStart).TotalSeconds / (TwoTableTest.SettleSeconds + TwoTableTest.SamplesPerStep), 0, 1);
            float bar = (float)((_ttStep + within) / _ttRows.Length);
            if (bar - _ttBar > 0.004f)
            {
                _ttBar = bar;
                Invalidate(new Rectangle(_rightX + AutoScrollPosition.X - 2, _ttBarY + StageH - 2 + AutoScrollPosition.Y, ClientSize.Width - _rightX, 16));
            }
        }
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
            try
            {
                if (UseBlocks)
                {
                    var snap = EcBlocks.Snapshot((done, total) => ReportProgress(done * 256 / total - 1));
                    BeginInvoke(() => { _blockDumps[idx] = snap; CaptureDone(idx, Array.Empty<byte>(), null); });
                }
                else
                {
                    var dump = Ec.DumpAll(ReportProgress);
                    BeginInvoke(() => CaptureDone(idx, dump, null));
                }
            }
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
        if (_blockDumps.Any(d => d != null)) return BuildBlockReport();
        var sb = new StringBuilder();
        sb.AppendLine("=== GhostDeck - model support report ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY, no EC writes)");
        sb.AppendLine($"App version: {D.AppVersion()}");
        sb.AppendLine($"EC firmware: {(string.IsNullOrEmpty(D.Firmware()) ? "(unknown)" : D.Firmware())}");
        sb.AppendLine($"Detected in app: {(string.IsNullOrEmpty(ModelName()) ? "(unsupported / unknown)" : ModelName())}");
        sb.AppendLine();
        sb.AppendLine("--- Diff: addresses that change between scenarios ---");
        sb.AppendLine("(temps/fans naturally fluctuate - ignore sensor-looking single-value drift)");
        sb.Append("Addr   ");
        foreach (var st in Steps) sb.Append(st.msiName.PadRight(20));
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

    // ---- the same report from the data blocks (backup path / monitoring-only machines) ----
    private static readonly string[] BlockSnapshotSlots =
        { "MSI_System[7]", "MSI_System[8]", "MSI_System[9]", "MSI_System[10]", "MSI_CPU[1]", "MSI_CPU[2]", "MSI_AP[2]", "MSI_AP[4]" };

    private string BuildBlockReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== GhostDeck - model support report (WMI data blocks) ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (READ-ONLY, nothing written)");
        sb.AppendLine($"App version: {D.AppVersion()}");
        sb.AppendLine($"Firmware: {(string.IsNullOrEmpty(D.Firmware()) ? "(unknown)" : D.Firmware())}   BIOS: {EcBlocks.BiosVersion()}");
        sb.AppendLine($"Detected in app: {(string.IsNullOrEmpty(ModelName()) ? "(unsupported / unknown)" : ModelName())}");
        sb.AppendLine("This machine's firmware does not answer on the MSI_ACPI methods; the values below are the device's WMI data blocks, slot by slot (class[index]).");
        sb.AppendLine();
        var keys = _blockDumps.Where(d => d != null).SelectMany(d => d!.Keys).Distinct().OrderBy(k => k, new SlotOrder()).ToList();
        sb.AppendLine("--- Diff: slots that change between scenarios ---");
        sb.AppendLine("(temperatures, fan duty and tachometers naturally fluctuate - ignore sensor-looking drift)");
        sb.Append("Slot".PadRight(22));
        foreach (var st in Steps) sb.Append(st.msiName.PadRight(20));
        sb.AppendLine();
        foreach (var k in keys)
        {
            if (AllEqualSlot(k)) continue;
            sb.Append(k.PadRight(22));
            foreach (var d in _blockDumps) sb.Append((d != null && d.TryGetValue(k, out int v) ? $"{v} (0x{v:X2})" : "--").PadRight(20));
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine("--- Full block snapshots ---");
        for (int i = 0; i < Steps.Length; i++)
        {
            sb.AppendLine(); sb.AppendLine($"[{Steps[i].msiName}]");
            var d = _blockDumps[i];
            if (d == null) { sb.AppendLine("(not captured)"); continue; }
            foreach (var k in keys) if (d.TryGetValue(k, out int v)) sb.AppendLine($"{k.PadRight(22)} {v,3}  (0x{v:X2})");
        }
        return sb.ToString();
    }

    private bool AllEqualSlot(string key)
    {
        int? first = null;
        foreach (var d in _blockDumps)
        {
            if (d == null) continue;
            int v = d.TryGetValue(key, out int x) ? x : -1;
            if (first == null) first = v; else if (v != first) return false;
        }
        return true;
    }

    private string BuildBlockSnapshot()
    {
        var sb = new StringBuilder();
        sb.Append("Slot".PadRight(16));
        foreach (var st in Steps) sb.Append(st.msiName.PadRight(9));
        sb.AppendLine();
        foreach (var k in BlockSnapshotSlots)
        {
            sb.Append(k.PadRight(16));
            foreach (var d in _blockDumps) sb.Append((d != null && d.TryGetValue(k, out int v) ? $"{v:X2}" : "--").PadRight(9));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>"MSI_CPU[2]" before "MSI_CPU[10]": class name first, then the numeric index.</summary>
    private sealed class SlotOrder : IComparer<string>
    {
        public int Compare(string? a, string? b)
        {
            static (string Cls, int Idx) Split(string? s) { int o = s!.IndexOf('['); return (s[..o], int.Parse(s[(o + 1)..^1])); }
            var (ca, ia) = Split(a); var (cb, ib) = Split(b);
            int c = string.CompareOrdinal(ca, cb);
            return c != 0 ? c : ia.CompareTo(ib);
        }
    }

    private string BuildSnapshot()
    {
        if (_blockDumps.Any(d => d != null)) return BuildBlockSnapshot();
        var sb = new StringBuilder();
        sb.Append("Addr ");
        foreach (var st in Steps) sb.Append(st.msiName.PadRight(9));
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
        // The two-table test restores the controller on its worker too, so it gets the same wait.
        if (_ptRunning)
        {
            try { _ptCts?.Cancel(); } catch { }
            if (wait) { try { _ptTask?.Wait(6000); } catch { } }
        }
        if (_ttRunning)
        {
            try { _ttCts?.Cancel(); } catch { }
            if (wait) { try { _ttTask?.Wait(6000); } catch { } }
        }
    }

    // Every address the run may write, from the model's own tables. The recipes are the run itself;
    // the curve tables belong here too, because the restore goes through the normal profile path
    // and that re-applies whatever fan curve is assigned to the profile it returns to.
    private string PtWriteList(DeviceProfile? dev)
    {
        if (dev == null) return "-";
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
                // The run's own restore threw. The re-apply above is the second attempt; the user
                // is told to check the tile rather than trust that it went through.
                if (_ptResult is { RestoreError: { } re })
                    _ptMsg = string.Format(Lang.T("pt_restore_failed"), re, Profiles.Get(back).Label);
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
        int secH = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold).Height;

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
            _ptRows[i].SetBounds(_rightX + ox, ry + oy, rightW, StepRowT.RowH(false));
            if (PtRowShown(i)) ry += StepRowT.RowH(false) + 8;
        }
        // The consent sentence is long in every language and must never be cut, so it wraps across
        // the whole column and everything below it follows its measured height.
        _ptConsentY = ry + 8;
        int boxW = (int)Math.Ceiling(18 * Dpi.Of(this) / 96f) + 10;
        int consentH = Math.Max((int)Math.Ceiling(26 * Dpi.Of(this) / 96f),
            TextRenderer.MeasureText(_ptConsent.Text, Dpi.Font("Segoe UI", 10.5f),
                new Size(Math.Max(40, rightW - boxW - 6), 0), TextFormatFlags.WordBreak).Height + 8);
        _ptConsent.Wrap = true;
        _ptConsent.SetBounds(_rightX + ox, _ptConsentY + oy, rightW, consentH);

        _ptBtnY = _ptConsentY + consentH + 16;
        int bw = Math.Min(320, rightW - 180);
        // While running, Cancel takes the primary slot because Start is hidden.
        if (_ptRunning) _ptSecondary.SetBounds(_rightX + ox, _ptBtnY + oy, bw, BtnH);
        else
        {
            _ptStart.SetBounds(_rightX + ox, _ptBtnY + oy, bw, BtnH);
            _ptSecondary.SetBounds(_rightX + bw + 10 + ox, _ptBtnY + oy, 170, BtnH);
        }
        _ptBarY = _ptBtnY + BtnH + 16;

        // bottoms in CONTENT coords (a child's .Bottom is client-side once the page is scrolled)
        int leftBottom = _ptWritesY + SecH + _ptWritesH + 14 + PillH;   // address block + firmware pill
        int rightBottom = _ptBarY + 180 + (_ptCopied ? 0 : ClipWarnHeight(rightW));
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    private void PaintPower(Graphics g)
    {
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        var dev = Dev;
        var secFont = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);

        // ---- left column: intro + warning card (child) + the exact addresses + firmware pill ----
        Ui.DrawText(g, Lang.T("pt_intro"), IntroFont,
            new Rectangle(Pad, _ptTop, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        Ui.DrawText(g, Lang.T("pt_writes"), secFont, new Point(Pad, _ptWritesY), Theme.Muted);
        Ui.DrawText(g, PtWriteList(dev), WritesFont,
            new Rectangle(Pad, _ptWritesY + SecH, _leftW, _ptWritesH + 4), Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);
        PaintFirmwarePill(g, _ptWritesY + SecH + _ptWritesH + 14);

        // ---- right column: checklist label ----
        Ui.DrawText(g, Lang.T("pt_steps"), secFont, new Point(_rightX, _contentTop), Theme.Muted);

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
            Ui.DrawText(g, stage + (_ptLive.Length > 0 ? "   " + _ptLive : ""),
                Dpi.Font("Segoe UI", 10f, FontStyle.Bold), new Point(_rightX, y - 4), Theme.Accent);
            var track = new RectangleF(_rightX, y + StageH, rightW, 12);
            using (var path = Theme.RoundRect(track, 6))
            { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            using (var path = Theme.RoundRect(new RectangleF(_rightX, y + StageH, Math.Max(12, rightW * _ptBar), 12), 6))
            { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
            y += StageH + 28;
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
            var vf = Dpi.Font("Segoe UI", 11.5f, FontStyle.Bold);
            int vh = TextRenderer.MeasureText(verdict, vf, new Size(rightW, 0), TextFormatFlags.WordBreak).Height;
            Ui.DrawText(g, verdict, vf, new Rectangle(_rightX, y, rightW, vh + 6), col,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += vh + 10;
            y += PaintSaved(g, _rightX, y, rightW, _ptSavedPath, _ptCopied) + 8;
        }

        if (_ptMsg != null)
            Ui.DrawText(g, _ptMsg, StepF, new Rectangle(_rightX, y, rightW,
                    TextRenderer.MeasureText(_ptMsg, StepF, new Size(rightW, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 4),
                Theme.Amber, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    // =================================================================
    //  two-table test (#293) - the sub-tab around Core/TwoTableTest.cs
    // =================================================================
    private static string TtPairKey(int i) => "tt_pair_" + (char)('a' + i);
    private static (string, string?)[] TtCardItems() => new (string, string?)[]
    {
        (Lang.T("tt_warn_tables"), null),
        (Lang.T("tt_warn_temps"), null),
        (Lang.T("tt_warn_restore2"), null),
    };

    private void RefreshTtRows()
    {
        // A cancelled or aborted run ticks only the pairs that were measured.
        int reached = _ttResult?.Steps.Count ?? 0;
        for (int i = 0; i < _ttRows.Length; i++)
            _ttRows[i].SetState(_ttRunning ? i < _ttStep : TtHasReport && i < reached, _ttRunning && i == _ttStep);
        EnsureAnim();
    }

    private void SyncTt()
    {
        bool on = !_landing && _sub == 3;
        bool done = TtHasReport && !_ttRunning;
        _ttStart.Text = done ? Lang.T("rep_finish") : Lang.T("pt_start");
        _ttStart.Visible = on && !_ttRunning;
        _ttConsent.Visible = on && !_ttRunning && !TtHasReport;
        _ttSecondary.Visible = on && (_ttRunning || done);
        _ttSecondary.Text = _ttRunning ? Lang.T("pt_cancel") : Lang.T("rep_restart");
    }

    private void OnTtSecondary(object? sender, EventArgs e)
    {
        if (_ttRunning) { _ttCts?.Cancel(); return; }
        _ttResult = null; _ttSavedPath = null; _ttMsg = null; _ttBar = 0; _ttStep = 0; _ttLive = "";
        _ttConsent.Checked = false;
        RefreshTtRows(); SyncTt(); Relayout(); Invalidate();
    }

    // Why the run must not start now, in the user's language; null when it may. The power test's
    // reasons apply here too (no entry, preview mode, writes locked), then the test's own gates
    // (no curve tables on this model, battery, too hot).
    private string? TtBlocked(DeviceProfile? dev)
    {
        if (dev == null) return Lang.T("pt_block_unknown");
        if (D.Simulating()) return Lang.T("pt_block_sim");
        if (!D.Writable()) return Lang.T("pt_block_locked");
        if (!Ec.TryReadHw(dev, out var hw)) hw = default;
        return TwoTableTest.Blocked(dev, hw) is { } key ? Lang.T(key) : null;
    }

    private async void OnTtStart(object? sender, EventArgs e)
    {
        if (_ttRunning) return;
        if (TtHasReport) { OpenTtIssue(); return; }

        var dev = Dev;
        if (TtBlocked(dev) is { } blocked) { _ttMsg = blocked; Invalidate(); return; }
        if (!_ttConsent.Checked) { _ttMsg = Lang.T("pt_need_consent"); Invalidate(); return; }

        _ttRunning = true;
        _ttResult = null; _ttMsg = null; _ttSavedPath = null; _ttCopied = true; _ttBar = 0; _ttStep = 0; _ttLive = "";
        _ttPairStart = DateTime.Now;
        _ttCts = new CancellationTokenSource();
        var ct = _ttCts.Token;
        RefreshTtRows(); SyncTt(); Relayout(); Invalidate();

        // Silent cannot hold the custom-curve fan mode (its power cap shares the register), so the
        // run happens in Balanced and Silent comes back at the end - as on the Fan curve page. The
        // gate keeps a model-database swap from changing the register map under a run.
        bool wasSilent = D.Current() == ProfileId.Silent;
        if (wasSilent) D.SetProfile(ProfileId.Balanced);
        var gate = D.EcSession();
        bool ecRestored = false;
        ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_tt_start"));
        _ttTask = Task.Run<TwoTableTest.Result?>(() =>
        {
            // What to put back: both tables and the mode byte exactly as the controller holds them
            // now, read before anything is written. The restore stays on this worker, so closing
            // the window or the app cannot leave a flat curve behind (StopPowerTest waits for it).
            (int[] cpuTemp, int[] cpuSpeed, int[] gpuTemp, int[] gpuSpeed)? before = null;
            byte modeBefore = 0;
            TwoTableTest.Result? r = null;
            try
            {
                try { before = Ec.ReadFanCurve(dev!); modeBefore = Ec.ReadByte(dev!.FanMode); } catch { }
                r = TwoTableTest.Run(dev!,
                    (i, n, msg) => { try { BeginInvoke(() => OnTtProgress(i, n, msg)); } catch { } },
                    ct);
            }
            finally
            {
                if (before is { } b)
                {
                    try
                    {
                        Ec.WriteFanCurve(dev!, b.cpuTemp, b.cpuSpeed, b.gpuTemp, b.gpuSpeed, bothTables: true);
                        Ec.SetFanMode(dev!, modeBefore);
                        ecRestored = true;
                    }
                    catch { }
                }
                gate.Dispose();
            }
            return r;
        });
        try { _ttResult = await _ttTask; }
        catch (Exception ex) { _ttMsg = string.Format(Lang.T("rep_read_fail"), AppLifecycle.DescribeEcFailure(ex)); }
        finally
        {
            _ttRunning = false;
            _ttCts?.Dispose(); _ttCts = null;
            _ttTask = null;
            _ttStep = _ttRows.Length - 1;
            _ttBar = 1;
            // The read before the run failed, so the tables could not be put back byte for byte:
            // the profile path re-applies whatever curve is assigned, the second-best restore.
            if (!ecRestored) { try { D.RestoreActiveCurve(); } catch { } }
            if (wasSilent) { try { D.SetProfile(ProfileId.Silent); } catch { } }
            ChangeLog.Add(ChangeSource.FanCurve, Lang.T("log_tt_end"), _ttResult == null ? "" : $"{_ttResult.Steps.Count} pairs");
            if (_ttResult is { Error: { } err }) _ttMsg = string.Format(Lang.T("rep_read_fail"), err);
            if (TtHasReport)
            {
                // The same history the Fan curve page's picker lists, marked 2× there.
                FanSweepHistory.AddTwoTable(_ttResult!, dev!.Name, D.Firmware(), D.AppVersion(), TtLines(_ttResult!));
                (FindForm() as MainForm)?.OnTwoTableRunSaved();
                PrepareTtReport(dev!);
            }
            RefreshTtRows(); SyncTt(); Relayout(); Invalidate();
        }
    }

    private void OnTtProgress(int i, int n, string live)
    {
        _ttStep = i; _ttLive = live; _ttPairStart = DateTime.Now;
        _ttBar = (float)i / Math.Max(1, n);
        RefreshTtRows();
        Invalidate();
    }

    // The findings with the fan named in the user's language, as the Fan curve page shows them.
    private IEnumerable<string> TtLines(TwoTableTest.Result r) =>
        TwoTableTest.Findings(r).Select(f =>
        {
            var args = (object[])f.args.Clone();
            if (args.Length > 0 && args[0] is int fi && TwoTableTest.IsFanScoped(f.key))
                args[0] = Lang.T(fi == 1 ? (r.SingleFan ? "fc_fan_single" : "fc_fan_cpu") : "fc_fan_gpu");
            return string.Format(Lang.T(f.key), args);
        });

    private static bool TtIsVerdict(string key) => key is "tt_find_higher" or "tt_find_table1" or "tt_find_table2" or "tt_find_flat" or "tt_find_undecided";

    private void PrepareTtReport(DeviceProfile dev)
    {
        if (_ttResult is not { } r) return;
        string report = TwoTableTest.Report(r, dev, D.Firmware(), D.AppVersion(), TtLines(r));
        _ttCopied = Ui.CopyText(report);
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fwTag = string.IsNullOrEmpty(D.Firmware()) ? "unknown" : D.Firmware().Replace('.', '_');
            _ttSavedPath = Path.Combine(dir, $"ghostdeck-two-table-test-{fwTag}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(_ttSavedPath, report, new UTF8Encoding(false));
        }
        catch { _ttSavedPath = null; }
    }

    private void OpenTtIssue()
    {
        try { Process.Start(new ProcessStartInfo(BuildTtIssueUrl()) { UseShellExecute = true }); } catch { }
    }

    private string BuildTtIssueUrl()
    {
        string title = $"[Two tables] {ModelName()} ({D.Firmware()})";
        string verdict = "";
        if (_ttResult is { } r)
        {
            var keys = TwoTableTest.Findings(r).Select(f => f.key).ToList();
            var texts = TtLines(r).ToList();
            verdict = string.Join(" ", texts.Where((_, i) => TtIsVerdict(keys[i])));
        }
        // As in the other three forms, the paste field is NOT prefilled: the full report is on the
        // clipboard and in a file, and reloading a prefilled URL would wipe what was pasted.
        return RepoUrl + "/issues/new?template=two-table-test.yml&labels=two-table-test"
            + "&title=" + Uri.EscapeDataString(title)
            + "&model=" + Uri.EscapeDataString(ModelName())
            + "&firmware=" + Uri.EscapeDataString(D.Firmware())
            + "&verdict=" + Uri.EscapeDataString(verdict);
    }

    // Every address the run writes: both tables, temperatures and speeds, and the fan-mode byte.
    private static string TtWriteList(DeviceProfile? dev)
    {
        if (dev?.FanCurve is not { } fc) return "-";
        var addrs = new SortedSet<byte> { dev.FanMode };
        foreach (var b in new[] { fc.CpuTempBase, fc.CpuSpeedBase, fc.GpuTempBase, fc.GpuSpeedBase })
            for (int i = 0; i < fc.Points; i++) addrs.Add((byte)(b + i));
        return string.Join("   ", addrs.Select(a => $"0x{a:X2}"));
    }

    private void LayoutTt(int top, int ox, int oy)
    {
        _leftW = Math.Max(360, (ClientSize.Width - Pad * 2 - Gutter) / 2);
        _rightX = Pad + _leftW + Gutter;
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        int secH = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold).Height;

        _ttTop = top;
        _introH = TextRenderer.MeasureText(Lang.T("tt_rep_intro"), IntroFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;
        _contentTop = _ttTop + _introH + 18;
        _ttCard.Location = new Point(Pad + ox, _contentTop + oy);
        _ttCard.SetWidth(_leftW);
        _ttWritesY = _contentTop + _ttCard.Height + 16;
        _ttWritesH = TextRenderer.MeasureText(TtWriteList(Dev), WritesFont, new Size(_leftW, 0), TextFormatFlags.WordBreak).Height;

        int ry = _contentTop + secH + 14;
        for (int i = 0; i < _ttRows.Length; i++) { _ttRows[i].SetBounds(_rightX + ox, ry + oy, rightW, StepRowT.RowH(true)); ry += StepRowT.RowH(true) + 8; }
        _ttConsentY = ry + 8;
        int boxW = (int)Math.Ceiling(18 * Dpi.Of(this) / 96f) + 10;
        int consentH = Math.Max((int)Math.Ceiling(26 * Dpi.Of(this) / 96f),
            TextRenderer.MeasureText(_ttConsent.Text, Dpi.Font("Segoe UI", 10.5f),
                new Size(Math.Max(40, rightW - boxW - 6), 0), TextFormatFlags.WordBreak).Height + 8);
        _ttConsent.Wrap = true;
        _ttConsent.SetBounds(_rightX + ox, _ttConsentY + oy, rightW, consentH);

        // Once there is a result the consent box is gone and the buttons move up into its place.
        _ttBtnY = (_ttConsent.Visible ? _ttConsentY + consentH : _ttConsentY - 8) + 16;
        int bw = Math.Min(320, rightW - 180);
        if (_ttRunning) _ttSecondary.SetBounds(_rightX + ox, _ttBtnY + oy, bw, BtnH);
        else
        {
            _ttStart.SetBounds(_rightX + ox, _ttBtnY + oy, bw, BtnH);
            _ttSecondary.SetBounds(_rightX + bw + 10 + ox, _ttBtnY + oy, 170, BtnH);
        }
        _ttBarY = _ttBtnY + BtnH + 16;

        int leftBottom = _ttWritesY + SecH + _ttWritesH + 14 + PillH;
        int rightBottom = _ttBarY + (_ttRunning ? StageH + 28 : 0) + DrawTtResult(null, _rightX, _ttBarY, rightW) + 20;
        AutoScrollMinSize = new Size(_rightX + 360 + Pad, Math.Max(leftBottom, rightBottom) + 20);
    }

    private void PaintTt(Graphics g)
    {
        int rightW = Math.Max(360, ClientSize.Width - _rightX - Pad);
        var secFont = Dpi.Font("Segoe UI", 9.5f, FontStyle.Bold);

        // ---- left column: intro + warning card (child) + the exact addresses + firmware pill ----
        Ui.DrawText(g, Lang.T("tt_rep_intro"), IntroFont,
            new Rectangle(Pad, _ttTop, _leftW, _introH + 4), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        Ui.DrawText(g, Lang.T("pt_writes"), secFont, new Point(Pad, _ttWritesY), Theme.Muted);
        Ui.DrawText(g, TtWriteList(Dev), WritesFont,
            new Rectangle(Pad, _ttWritesY + SecH, _leftW, _ttWritesH + 4), Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.WordBreak);
        PaintFirmwarePill(g, _ttWritesY + SecH + _ttWritesH + 14);

        // ---- right column: the pairs, then the run, then what it said ----
        Ui.DrawText(g, Lang.T("tt_pairs"), secFont, new Point(_rightX, _contentTop), Theme.Muted);

        int y = _ttBarY;
        if (_ttRunning)
        {
            string stage = string.Format(Lang.T("tt_step"), _ttStep + 1, _ttRows.Length, _ttLive);
            Ui.DrawText(g, stage, Dpi.Font("Segoe UI", 10f, FontStyle.Bold), new Point(_rightX, y - 4), Theme.Accent);
            var track = new RectangleF(_rightX, y + StageH, rightW, 12);
            using (var path = Theme.RoundRect(track, 6))
            { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
            using (var path = Theme.RoundRect(new RectangleF(_rightX, y + StageH, Math.Max(12, rightW * _ttBar), 12), 6))
            { using var b = new SolidBrush(Theme.Accent); g.FillPath(b, path); }
            y += StageH + 28;
        }
        DrawTtResult(g, _rightX, y, rightW);
    }

    // The result block: the one-line outcome, the findings, the table of pairs, where the report
    // went, and whatever the page has to complain about. Draws when g is given, and in both cases
    // returns the height it takes, so the layout can reserve exactly that much.
    private int DrawTtResult(Graphics? g, int x, int y, int w)
    {
        int y0 = y;
        if (_ttResult is { } r && !_ttRunning && r.Steps.Count > 0)
        {
            // Green only for a run that went the distance; a stopped one keeps its numbers but is
            // not a verdict.
            var vf = Dpi.Font("Segoe UI", 11.5f, FontStyle.Bold);
            string outcome = r.Aborted ? Lang.T(r.AbortReason ?? "tt_aborted") : Lang.T("pt_res_done");
            int vh = TextRenderer.MeasureText(outcome, vf, new Size(w, 0), TextFormatFlags.WordBreak).Height;
            if (g != null) Ui.DrawText(g, outcome, vf, new Rectangle(x, y, w, vh + 6), r.Aborted ? Theme.Amber : Theme.Green, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += vh + 10;

            var keys = TwoTableTest.Findings(r).Select(f => f.key).ToList();
            var texts = TtLines(r).ToList();
            for (int i = 0; i < texts.Count; i++)
            {
                string line = "•  " + texts[i];
                int lh = TextRenderer.MeasureText(line, StepF, new Size(w, 0), TextFormatFlags.WordBreak).Height;
                var col = TtIsVerdict(keys[i]) && keys[i] is not "tt_find_flat" and not "tt_find_undecided" ? Theme.Text
                        : keys[i] is "tt_find_mode" or "tt_find_flat" ? Theme.Amber : Theme.Muted;
                if (g != null) Ui.DrawText(g, line, StepF, new Rectangle(x, y, w, lh + 2), col, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += lh + 3;
            }
            y += 8;

            // the pairs as measured, in the report's own columns
            var mono = Dpi.Font("Consolas", 9.5f);
            string unit = r.HasTach ? " rpm" : " %";
            var heads = new List<string> { Lang.T("tt_col_t1"), Lang.T("tt_col_t2"), (r.SingleFan ? Lang.T("fc_fan_single") : Lang.T("fc_fan_cpu")) + unit };
            if (!r.SingleFan) heads.Add(Lang.T("fc_fan_gpu") + unit);
            heads.Add(Lang.T("tt_col_cpu")); heads.Add(Lang.T("tt_col_gpu"));
            var rows = new List<string[]>();
            foreach (var st in r.Steps)
            {
                var cells = new List<string> { st.Table1 + "%", st.Table2 + "%", r.HasTach ? (st.CpuRpm > 0 ? st.CpuRpm.ToString() : "--") : st.CpuDuty.ToString() };
                if (!r.SingleFan) cells.Add(r.HasTach ? (st.GpuRpm > 0 ? st.GpuRpm.ToString() : "--") : st.GpuDuty.ToString());
                cells.Add(st.CpuTemp.ToString()); cells.Add(st.GpuTemp > 0 ? st.GpuTemp.ToString() : "--");
                rows.Add(cells.ToArray());
            }
            var colW = new int[heads.Count];
            for (int i = 0; i < heads.Count; i++)
            {
                int cw = TextRenderer.MeasureText(heads[i], mono, Size.Empty, TextFormatFlags.NoPadding).Width;
                foreach (var row in rows) cw = Math.Max(cw, TextRenderer.MeasureText(row[i], mono, Size.Empty, TextFormatFlags.NoPadding).Width);
                colW[i] = cw;
            }
            int gap = Math.Max(12, mono.Height);
            void Row(string[] cells, Color c, int ry)
            {
                int cx = x;
                for (int i = 0; i < cells.Length; i++)
                {
                    Ui.DrawText(g!, cells[i], mono, new Rectangle(cx, ry, colW[i] + 2, mono.Height + 2), c, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    cx += colW[i] + gap;
                }
            }
            if (g != null) Row(heads.ToArray(), Theme.Muted, y);
            y += mono.Height + 4;
            foreach (var cells in rows) { if (g != null) Row(cells, Theme.Text, y); y += mono.Height + 2; }
            y += 10;

            if (g != null) y += PaintSaved(g, x, y, w, _ttSavedPath, _ttCopied) + 8;
            else
            {
                if (_ttSavedPath != null)
                    y += TextRenderer.MeasureText(string.Format(Lang.T("rep_saved_to"), _ttSavedPath), Dpi.Font("Segoe UI", 9f), new Size(w, 0), TextFormatFlags.WordBreak).Height + 6;
                y += (_ttCopied ? 0 : ClipWarnHeight(w) + 6) + 8;
            }
        }
        if (_ttMsg != null)
        {
            int mh = TextRenderer.MeasureText(_ttMsg, StepF, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 4;
            if (g != null) Ui.DrawText(g, _ttMsg, StepF, new Rectangle(x, y, w, mh), Theme.Amber, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += mh + 6;
        }
        return y - y0;
    }

    // Where the report went, and - when the clipboard refused it - the line that stops the user
    // pasting whatever happened to be there already. Returns the height it used.
    private int PaintSaved(Graphics g, int x, int y, int w, string? path, bool copied)
    {
        int sh = 0;
        if (path != null)
        {
            string saved = string.Format(Lang.T("rep_saved_to"), path);
            var sf = Dpi.Font("Segoe UI", 9f);
            sh = TextRenderer.MeasureText(saved, sf, new Size(w, 0), TextFormatFlags.WordBreak).Height;
            Ui.DrawText(g, saved, sf, new Rectangle(x, y, w, sh + 4), Theme.Muted, TextFormatFlags.WordBreak);
            sh += 6;
        }
        // Deliberately NOT tied to the path: a run that lost the clipboard AND the file is the one
        // case where saying nothing would leave the user pasting whatever was there before.
        if (copied) return sh;
        var wf = Dpi.Font("Segoe UI", 10f, FontStyle.Bold);
        string warn = Lang.T("rep_clip_fail");
        int wh = ClipWarnHeight(w);
        Ui.DrawText(g, warn, wf, new Rectangle(x, y + sh + 2, w, wh), Theme.Amber,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return sh + wh + 6;
    }

    // Layout has to reserve this too, or the warning lands below the scrollable area and the one
    // person who needs to read it is the one who cannot reach it.
    private static int ClipWarnHeight(int w) =>
        TextRenderer.MeasureText(Lang.T("rep_clip_fail"), Dpi.Font("Segoe UI", 10f, FontStyle.Bold),
            new Size(Math.Max(40, w), 0), TextFormatFlags.WordBreak).Height + 10;

    // The firmware pill closes the left column on every sub-tab.
    private void PaintFirmwarePill(Graphics g, int y)
    {
        int ph = PillH;
        var pill = new RectangleF(Pad, y, _leftW, ph);
        using (var path = Theme.RoundRect(pill, 11))
        { using var b = new SolidBrush(Theme.Card); g.FillPath(b, path); using var p = new Pen(Theme.Border); g.DrawPath(p, path); }
        var lf = Dpi.Font("Segoe UI", 10f);
        int lw = TextRenderer.MeasureText(Lang.T("st_firmware"), lf).Width;
        Ui.DrawText(g, Lang.T("st_firmware"), lf, new Rectangle(Pad + 16, (int)pill.Y, Math.Max(180, lw + 4), ph), Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        Ui.DrawText(g, string.IsNullOrEmpty(D.Firmware()) ? "-" : D.Firmware(), PillMonoF,
            new Rectangle(Pad + 16 + lw + 12, (int)pill.Y, _leftW - lw - 40, ph), Theme.Accent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    // =================================================================
    //  themed custom controls
    // =================================================================
    private sealed class StepRowT : Control
    {
        private const int Circle = 26, Cx = 28;
        // The status column holds "captured" / "pending" in the row's font, so it scales with it.
        private int StatusW => (int)Math.Ceiling(150 * Dpi.Of(this) / 96f);
        private int _num;

        /// <summary>
        /// Row height from the fonts (#172: a fixed 52 / 58 px clipped the second line and made the
        /// rows overlap above 150 %), with the old values as the floor so 100 % looks as before.
        /// </summary>
        public static int RowH(bool twoLine)
        {
            int h = Dpi.Font("Segoe UI", 11f, FontStyle.Bold).Height + 16;
            if (twoLine) h += Dpi.Font("Segoe UI", 9.75f).Height - 2;
            return Math.Max(twoLine ? 58 : 52, h);
        }
        private string _name;
        private string? _alt;
        public Color Tint;
        private bool _done, _current;
        private float _doneA, _glowA;

        public StepRowT(int num, string name, Color color)
        { _num = num; _name = name; Tint = color; DoubleBuffered = true; ResizeRedraw = true; }

        /// <summary>The power test names its rows from the model database, which can change at runtime.</summary>
        public string Label { set { if (_name == value) return; _name = value; Invalidate(); } }

        /// <summary>The same tile under another vendor app's name, drawn as a muted second line.</summary>
        public string? Alt { set { if (_alt == value) return; _alt = value; Invalidate(); } }

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
                Ui.DrawText(g, _num.ToString(), Dpi.Font("Segoe UI", 9f, FontStyle.Bold), Rectangle.Round(circle), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            int nx = Cx + Circle / 2 + 16;
            int textW = Width - nx - StatusW - 6;
            var nameFont = Dpi.Font("Segoe UI", 11f, FontStyle.Bold);
            var nameColor = _done || _current ? Theme.Text : Theme.Muted;
            if (string.IsNullOrEmpty(_alt))
                Ui.DrawText(g, _name, nameFont, new Rectangle(nx, 6, textW, Height - 12), nameColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);   // dpi-ok: short step names in a column sized with the page; last resort for a very long translation
            else
            {
                // Two lines share the row: the tile name as the capture knows it, and the name the
                // owner may be looking at in another vendor app. Heights come from the fonts, so
                // the pair stays centred at any DPI.
                var altFont = Dpi.Font("Segoe UI", 9.75f);
                int h1 = nameFont.Height, h2 = altFont.Height, top = (Height - h1 - h2 + 2) / 2;
                Ui.DrawText(g, _name, nameFont, new Rectangle(nx, top, textW, h1), nameColor,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);   // dpi-ok: short step names in a column sized with the page; last resort for a very long translation
                Ui.DrawText(g, _alt, altFont, new Rectangle(nx, top + h1 - 2, textW, h2), _current ? Theme.Text : Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);   // dpi-ok: short step names in a column sized with the page; last resort for a very long translation
            }
            Ui.DrawText(g, Lang.T(_done ? "rep_captured" : "rep_pending"), Dpi.Font("Segoe UI", 9.5f, _done ? FontStyle.Bold : FontStyle.Regular),
                new Rectangle(Width - StatusW, 6, StatusW - 10, Height - 12), _done ? Theme.Green : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    private sealed class InfoCardT : Control
    {
        private const int LeftPad = 46, RightPad = 18, TopPad = 15;
        private static readonly Font IconFont = Dpi.Font("Segoe MDL2 Assets", 13f);
        private readonly string _icon;
        private (string text, string? url)[] _items;
        private readonly List<(Rectangle rect, string text)> _paras = new();
        private readonly List<LinkLabel> _links = new();
        private readonly Font _font = Dpi.Font("Segoe UI", 10.5f);

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
            var linkFont = Dpi.Font("Segoe UI", 10.5f, FontStyle.Bold);
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

        /// <summary>The same card in another language: the texts are captured at build time.</summary>
        public void SetItems((string text, string? url)[] items) { _items = items; Build(); }

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
            Ui.DrawText(g, _icon, IconFont, new Rectangle(12, TopPad, 30, ih + 2), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            foreach (var (rect, text) in _paras)
                Ui.DrawText(g, text, _font, rect, fg, TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left);
        }
    }
}
