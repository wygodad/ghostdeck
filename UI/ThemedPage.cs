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

    /// <summary>
    /// The user clicked the tab they are already on. Pages with sub-tabs use it to go back to
    /// their own start view, so the tab icon doubles as a "home" button (discussion #9).
    /// </summary>
    public virtual void OnReenter() { }
    // Lightweight refresh after external state changes (profile/cooler/overlay). Unlike OnEnter it must
    // NOT re-run layout — a re-layout on a scrolled page (Settings) yanks the scroll position to the top.
    public virtual void LiveRefresh() { }

    /// <summary>
    /// The UI language changed while the app was running. Text painted through Lang.T follows by
    /// itself on the next paint; this hook is for text captured into controls at construction
    /// (sub-tab labels, button captions), which would otherwise stay in the old language.
    /// </summary>
    public virtual void OnLanguageChanged() { }

    /// <summary>The model database changed while the app was running: re-read anything derived
    /// from Devices (the detected profile, the fan-curve layout, the catalogue).</summary>
    public virtual void OnDeviceDbChanged() { }

    /// <summary>The display topology changed while the app was running (dock/undock, display-mode
    /// switch, resolution change): re-read anything derived from Display (rate lists, the
    /// controlled-display label). Debounced by MainForm.</summary>
    public virtual void OnDisplayChanged() { }
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
