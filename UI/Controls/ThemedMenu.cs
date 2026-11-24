using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace GhostDeck;

/// <summary>What a menu line says, so its text takes the matching theme colour.</summary>
public enum MenuTone { Normal, Muted, Warn, Good }

/// <summary>
/// Right-click menus in the colours of the current theme. One call, <see cref="Apply"/>, on a
/// finished <see cref="ContextMenuStrip"/> covers the strip and every submenu: background, border,
/// highlight, separators, disabled lines, check marks and submenu arrows are all drawn here from
/// <see cref="Theme"/> at paint time, so a theme change needs no rebuild - the next time the menu
/// opens it is drawn in the new colours.
///
/// The menu stays one system strip (no window per line). The markers in the image column are
/// drawn, not bitmaps: <see cref="Dot"/> is the on/off marker of a switch, <see cref="Swatch"/>
/// the colour chip of a profile. Their size comes from the menu font, so they follow the scale.
/// </summary>
public static class ThemedMenu
{
    private sealed class Look
    {
        public bool? Dot;
        public Color? Swatch;
        public MenuTone Tone;
    }

    private static readonly ConditionalWeakTable<ToolStripItem, Look> Looks = new();
    private static readonly Font MenuFont = Dpi.Font("Segoe UI", 9f);
    // Keeps the image column's slot for a line whose marker is drawn; never shown, never disposed.
    private static readonly Bitmap Blank = new(1, 1);
    private static readonly Painter Shared = new();

    /// <summary>Themes the strip and all its submenus. Call once the items are in place.</summary>
    public static void Apply(ToolStripDropDown menu)
    {
        menu.Renderer = Shared;
        if (menu is ContextMenuStrip) menu.Font = MenuFont;
        int box = Math.Max(16, MenuFont.Height);
        if (menu.ImageScalingSize.Width != box) menu.ImageScalingSize = new Size(box, box);
        foreach (ToolStripItem it in menu.Items)
            if (it is ToolStripDropDownItem { HasDropDownItems: true } parent) Apply(parent.DropDown);
    }

    /// <summary>The on/off marker of a switch line: a filled accent dot, or a dim ring.</summary>
    public static void Dot(ToolStripMenuItem it, bool on)
    {
        Looks.GetOrCreateValue(it).Dot = on;
        it.Image ??= Blank;
        it.Invalidate();
    }

    /// <summary>The colour chip of a profile line; a checked line gets a ring around it.</summary>
    public static void Swatch(ToolStripMenuItem it, Color color)
    {
        Looks.GetOrCreateValue(it).Swatch = color;
        it.Image ??= Blank;
        it.Invalidate();
    }

    /// <summary>The meaning of a line's text: secondary, a warning, good news.</summary>
    public static void Tone(ToolStripItem it, MenuTone tone)
    {
        Looks.GetOrCreateValue(it).Tone = tone;
        it.Invalidate();
    }

    private static Color TextColor(ToolStripItem it)
    {
        if (!it.Enabled) return Theme.Faint;
        return (Looks.TryGetValue(it, out var l) ? l.Tone : MenuTone.Normal) switch
        {
            MenuTone.Muted => Theme.Muted,
            MenuTone.Warn => Theme.Amber,
            MenuTone.Good => Theme.Green,
            _ => Theme.Text,
        };
    }

    private sealed class Painter : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(Theme.Card);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var p = new Pen(Theme.BorderStrong);
            e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }   // same surface as the rest

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var it = e.Item;
            if (!it.Enabled || !(it.Selected || it.Pressed)) return;
            var g = e.Graphics;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int inset = Math.Max(2, it.Height / 10);
            var r = new RectangleF(inset, 0.5f, it.Width - 2 * inset - 1, it.Height - 1);
            using (var path = Theme.RoundRect(r, Math.Max(2f, it.Height / 6f)))
            using (var b = new SolidBrush(Theme.AccentSoft))
                g.FillPath(b, path);
            g.SmoothingMode = old;
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var it = e.Item;
            int pad = Math.Max(6, it.Height);
            int y = it.Height / 2;
            using var p = new Pen(Theme.Border);
            e.Graphics.DrawLine(p, pad, y, it.Width - pad, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, TextColor(e.Item), e.TextFormat);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            // a small chevron sized from the line, so it follows the scale like the text does
            var r = e.ArrowRectangle;
            var it = e.Item;
            float h = Math.Max(6f, (it?.Height ?? r.Height) * 0.34f), w = h * 0.5f;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            var g = e.Graphics;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(it is { Enabled: false } ? Theme.Faint : Theme.Muted, Math.Max(1.4f, h / 6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - w / 2, cy - h / 2), new PointF(cx + w / 2, cy), new PointF(cx - w / 2, cy + h / 2) });
            g.SmoothingMode = old;
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // a line with a colour chip shows its tick as a ring around the chip (drawn with the chip)
            if (Looks.TryGetValue(e.Item, out var l) && (l.Swatch != null || l.Dot != null)) return;
            var r = e.ImageRectangle;
            float s = Math.Min(r.Width, r.Height) * 0.5f;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            var g = e.Graphics;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(e.Item.Enabled ? Theme.Accent : Theme.Faint, Math.Max(1.6f, s / 4.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - s / 2, cy), new PointF(cx - s / 8, cy + s * 0.38f), new PointF(cx + s / 2, cy - s * 0.36f) });
            g.SmoothingMode = old;
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (!Looks.TryGetValue(e.Item, out var l) || (l.Swatch == null && l.Dot == null))
            {
                base.OnRenderItemImage(e);
                return;
            }
            var r = e.ImageRectangle;
            float box = Math.Min(r.Width, r.Height);
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            var g = e.Graphics;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (l.Swatch is Color chip)
            {
                float s = box * 0.62f;
                var cr = new RectangleF(cx - s / 2, cy - s / 2, s, s);
                Color fill = e.Item.Enabled ? chip : Color.FromArgb(90, chip);
                using (var path = Theme.RoundRect(cr, s * 0.28f))
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, path);
                if (e.Item is ToolStripMenuItem { Checked: true })
                {
                    float gap = Math.Max(2f, box * 0.12f);
                    var rr = RectangleF.Inflate(cr, gap, gap);
                    using var path = Theme.RoundRect(rr, s * 0.28f + gap);
                    using var p = new Pen(Theme.Text, Math.Max(1.4f, box / 13f));
                    g.DrawPath(p, path);
                }
            }
            else
            {
                float d = box * 0.5f;
                var dr = new RectangleF(cx - d / 2, cy - d / 2, d, d);
                if (l.Dot == true && e.Item.Enabled) { using var b = new SolidBrush(Theme.Accent); g.FillEllipse(b, dr); }
                else if (l.Dot == true) { using var b = new SolidBrush(Theme.Faint); g.FillEllipse(b, dr); }
                else { using var p = new Pen(Theme.Faint, Math.Max(1.4f, box / 12f)); g.DrawEllipse(p, dr); }
            }
            g.SmoothingMode = old;
        }
    }
}
