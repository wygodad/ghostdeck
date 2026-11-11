namespace GhostDeck;

// =====================================================================
//  Dashboard: the user's own layout of widgets on a grid
// =====================================================================
/// <summary>
/// Hosts the one drawn surface (<see cref="DashCanvas"/>) and sizes it to its content, so the
/// page scrolls the canvas natively like the Status page does. Nothing else lives here: the
/// widgets, the edit mode and the catalogue are all zones on the canvas. The page owns the
/// tick: one shared sample every 1.5 s (the Status interval; one EC read per tick, and the two
/// pages are never visible at once), or every 3 or 5 s when the user chose so in edit mode
/// (1.45), read off the window thread, and no tick while hidden.
/// </summary>
public sealed class DashboardPage : ThemedPage
{
    private readonly DashCanvas _canvas;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private int _refreshing;   // 1 while a background sample is in flight

    public DashboardPage(MainDeps d) : base(d)
    {
        _canvas = new DashCanvas(d) { Location = Point.Empty };
        Controls.Add(_canvas);

        _timer.Tick += (_, _) => RefreshAsync();
        _timer.Interval = Every();
        _canvas.ContentChanged += Relayout;   // edit mode, the catalogue and layout edits change the content height
        _canvas.ContentChanged += () => { if (_timer.Interval != Every()) _timer.Interval = Every(); };   // the refresh setting is changed on the canvas
        ClientSizeChanged += (_, _) => Relayout();
        // Hidden page: no tick, and the static buffer is dropped (pages are never disposed, so
        // it would stay allocated for the whole session). OnPaint renders it again on return.
        VisibleChanged += (_, _) =>
        {
            if (Visible) { Relayout(); _timer.Start(); RefreshAsync(); }
            else { _timer.Stop(); _canvas.ReleaseBuffer(); }
        };
    }

    /// <summary>The tick: the user's choice (1.45), never faster than the 1.5 s every reading was measured at.</summary>
    private int Every() => D.Settings.DashRefreshMs is 3000 or 5000 ? D.Settings.DashRefreshMs : 1500;

    // Wrapped so the scroll extent is recomputed after the canvas is resized
    // (ThemedPage.LayoutAndSyncScroll: a resized child does not refresh the bars by itself).
    private void Relayout() => LayoutAndSyncScroll(RelayoutPass);

    private void RelayoutPass()
    {
        int w = ClientSize.Width;
        if (w <= 0) return;
        int h = Math.Max(ClientSize.Height, _canvas.ContentHeight(w));
        if (_canvas.Width != w || _canvas.Height != h) _canvas.Size = new Size(w, h);
    }

    public override void OnEnter() { _canvas.Sync(); Relayout(); RefreshAsync(); }
    public override void LiveRefresh() => RefreshAsync();
    public override void OnDisplayChanged() => Relayout();
    public override void OnLanguageChanged() => _canvas.Rebuild();

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _canvas.BackColor = Theme.Surface;
        _canvas.Rebuild();
    }

    // The EC/WMI snapshot takes tens of milliseconds; read on a worker and complete the sample
    // on the window thread (the Status page does the same, see StatusPage.RefreshAsync).
    private bool _refreshOnHandle;

    private void RefreshAsync()
    {
        // No window handle yet (shown before the host created it): the completion could not be
        // posted back. Run the first read once the handle exists instead of dropping it.
        if (!IsHandleCreated)
        {
            if (!_refreshOnHandle) { _refreshOnHandle = true; HandleCreated += (_, _) => RefreshAsync(); }
            return;
        }
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        Task.Run(() =>
        {
            var hw = DashSample.ReadSlow(D);
            try
            {
                BeginInvoke(() =>
                {
                    _refreshing = 0;
                    if (IsDisposed) return;
                    _canvas.SetSample(DashSample.Complete(D, hw));
                });
            }
            catch { _refreshing = 0; return; }   // page disposed mid-flight
            DashSample.WarmCounters();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
