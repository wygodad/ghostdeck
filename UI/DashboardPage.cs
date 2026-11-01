namespace GhostDeck;

// =====================================================================
//  Dashboard: the user's own layout of widgets on a grid
// =====================================================================
/// <summary>
/// Hosts the one drawn surface (<see cref="DashCanvas"/>) and sizes it to its content, so the
/// page scrolls the canvas natively like the Status page does. Nothing else lives here: the
/// widgets, the edit mode and the catalogue are all zones on the canvas. The page also owns
/// the data read: one shared sample per tick, taken off the window thread.
/// </summary>
public sealed class DashboardPage : ThemedPage
{
    private readonly DashCanvas _canvas;
    private int _refreshing;   // 1 while a background sample is in flight

    public DashboardPage(MainDeps d) : base(d)
    {
        _canvas = new DashCanvas(d) { Location = Point.Empty };
        Controls.Add(_canvas);

        ClientSizeChanged += (_, _) => Relayout();
        // Hidden page: drop the offscreen buffer (pages are never disposed, so it would stay
        // allocated for the whole session). OnPaint re-renders when the buffer is null.
        VisibleChanged += (_, _) => { if (Visible) Relayout(); else _canvas.ReleaseBuffer(); };
    }

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

    public override void OnEnter() { _canvas.Sync(); Relayout(); _canvas.Rebuild(); RefreshAsync(); }
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
    private void RefreshAsync()
    {
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
            catch { _refreshing = 0; }   // page disposed mid-flight
        });
    }
}
