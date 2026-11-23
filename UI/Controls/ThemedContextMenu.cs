using System.ComponentModel;

namespace GhostDeck;

/// <summary>App-palette context menus, including nested drop-downs and live theme changes.</summary>
public sealed class ThemedContextMenu : ContextMenuStrip
{
    public ThemedContextMenu()
    {
        Renderer = new MenuRenderer();
        Theme.Changed += OnThemeChanged;
        ApplyTheme(this);
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        ApplyTheme(this);
        base.OnOpening(e);
        // Opening handlers may populate dynamic items.
        if (!IsDisposed) ApplyTheme(this);
    }

    private void OnThemeChanged()
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired)
        {
            try { BeginInvoke(OnThemeChanged); } catch (InvalidOperationException) { }
            return;
        }
        ApplyTheme(this);
    }

    private void ApplyTheme(ToolStrip strip)
    {
        strip.Renderer = Renderer;
        strip.BackColor = Theme.Surface;
        strip.ForeColor = Theme.Text;
        foreach (ToolStripItem item in strip.Items)
            if (item is ToolStripDropDownItem { HasDropDownItems: true } parent)
                ApplyTheme(parent.DropDown);
        strip.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }

    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            // Visual styles can substitute the Windows highlight despite the color table.
            if (e.Item.Enabled && (e.Item.Selected || e.Item.Pressed))
            {
                var bounds = new Rectangle(Point.Empty, e.Item.Size);
                bounds.Width--; bounds.Height--;
                using var fill = new SolidBrush(Theme.AccentSoft);
                using var border = new Pen(Theme.BorderStrong);
                e.Graphics.FillRectangle(fill, bounds);
                e.Graphics.DrawRectangle(border, bounds);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled == false ? Theme.Muted : Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Draw text directly: the base renderer substitutes system highlight/disabled colors.
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle,
                e.Item.Enabled ? e.Item.ForeColor : Theme.Muted, e.TextFormat);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            ControlPaint.DrawMenuGlyph(e.Graphics, e.ImageRectangle, MenuGlyph.Checkmark,
                e.Item.Enabled ? Theme.Text : Theme.Muted, Theme.AccentSoft);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public MenuColors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.BorderStrong;
        public override Color MenuItemBorder => Theme.BorderStrong;
        public override Color MenuItemSelected => Theme.AccentSoft;
        public override Color MenuItemSelectedGradientBegin => Theme.AccentSoft;
        public override Color MenuItemSelectedGradientEnd => Theme.AccentSoft;
        public override Color MenuItemPressedGradientBegin => Theme.AccentSoft;
        public override Color MenuItemPressedGradientMiddle => Theme.AccentSoft;
        public override Color MenuItemPressedGradientEnd => Theme.AccentSoft;
        public override Color CheckBackground => Theme.AccentSoft;
        public override Color CheckSelectedBackground => Theme.AccentSoft;
        public override Color CheckPressedBackground => Theme.AccentSoft;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Surface;
    }
}
