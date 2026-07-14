using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>Base for tab pages. Re-themes on demand; refreshes data on enter; supports scrolling.</summary>
public abstract class ThemedPage : UserControl
{
    protected readonly MainDeps D;
    protected ThemedPage(MainDeps d)
    {
        D = d;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoScroll = true;
        BackColor = Theme.Surface;
        Resize += (_, _) => Invalidate();
    }

    // NB: WS_EX_COMPOSITED was tried here against scroll tearing (discussion #9) and REVERTED -
    // it made every tab visibly slow to paint and flashed white during startup. Do not re-add;
    // the children are individually double-buffered instead.

    // Faint brand grid under every tab's content (cards and controls paint over it).
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (D.Settings.ShowGrid) Ui.DrawGrid(e.Graphics, ClientRectangle);
    }
    public virtual void OnEnter() { }
    // Lightweight refresh after external state changes (profile/cooler/overlay). Unlike OnEnter it must
    // NOT re-run layout — a re-layout on a scrolled page (Settings) yanks the scroll position to the top.
    public virtual void LiveRefresh() { }
    public virtual void ApplyTheme() { BackColor = Theme.Surface; Invalidate(true); }

    /// <summary>Translate painting to honour the scroll offset (call at the top of OnPaint).</summary>
    protected void ApplyScroll(Graphics g) => g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

    // We paint the whole surface ourselves, so a partial scroll blit leaves ghosting.
    // Force a full repaint whenever the scroll position changes.
    protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

    // Stop WinForms from auto-scrolling the page when a child control gains focus (clicking a toggle
    // deep in Settings otherwise yanked the scroll back to the top). Keep the current scroll position.
    protected override Point ScrollToControl(Control activeControl) => AutoScrollPosition;
}
