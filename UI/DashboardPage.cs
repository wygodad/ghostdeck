namespace GhostDeck;

// =====================================================================
//  Dashboard: the user's own layout of widgets on a grid
// =====================================================================
/// <summary>
/// Hosts the one drawn surface (<see cref="DashCanvas"/>) and sizes it to its content, so the
/// page scrolls the canvas natively like the Status page does. Nothing else lives here: the
/// widgets, the edit mode and the catalogue are all zones on the canvas.
/// </summary>
public sealed class DashboardPage : ThemedPage
{
    private readonly DashCanvas _canvas;

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

    public override void OnEnter() => Relayout();
    public override void OnDisplayChanged() => Relayout();
    public override void OnLanguageChanged() => _canvas.Rebuild();

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _canvas.BackColor = Theme.Surface;
        _canvas.Rebuild();
    }
}
