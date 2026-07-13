using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Central light/dark palette for the tabbed UI. WinForms has no built-in theming,
/// so controls read these colours and re-apply on <see cref="Changed"/>.
/// Accent follows the ghostdeck.dev brand: neon cyan in dark mode, blue in light mode.
/// </summary>
public static class Theme
{
    public static bool Dark { get; private set; }

    public static event Action? Changed;

    public static void Set(bool dark)
    {
        if (Dark == dark) return;
        Dark = dark;
        Changed?.Invoke();
    }

    public static void Toggle() => Set(!Dark);

    // brand accent - ghostdeck.dev palette. Accent (neon cyan on dark, blue on light) is for
    // indicators drawn on the page: icons, tab underlines, rings, links. AccentFill is for filled
    // interactive controls that carry white text/knobs (buttons, checkboxes, toggles, sliders);
    // white on cyan has too little contrast, so fills stay blue in both modes.
    public static Color Accent     => Dark ? Color.FromArgb(0x3D, 0xE3, 0xFF) : Color.FromArgb(0x3C, 0x7D, 0xFF);
    public static Color AccentFill => Color.FromArgb(0x3C, 0x7D, 0xFF);
    public static Color AccentText => Color.White;
    public static Color AccentSoft => Dark ? Color.FromArgb(0x11, 0x2A, 0x38) : Color.FromArgb(0xE9, 0xF0, 0xFE);

    public static Color Page    => Dark ? Color.FromArgb(0x05, 0x07, 0x0B) : Color.FromArgb(0xF4, 0xF4, 0xF7);
    public static Color Surface => Dark ? Color.FromArgb(0x0A, 0x0D, 0x14) : Color.White;
    public static Color Card    => Dark ? Color.FromArgb(0x11, 0x16, 0x22) : Color.FromArgb(0xFA, 0xFA, 0xFC);
    public static Color Border  => Dark ? Color.FromArgb(0x23, 0x2C, 0x40) : Color.FromArgb(0xE5, 0xE7, 0xEB);
    public static Color BorderStrong => Dark ? Color.FromArgb(0x32, 0x3E, 0x58) : Color.FromArgb(0xD7, 0xDA, 0xE0);

    public static Color Text   => Dark ? Color.FromArgb(0xF3, 0xF7, 0xFF) : Color.FromArgb(0x1F, 0x24, 0x30);
    public static Color Muted  => Dark ? Color.FromArgb(0xA4, 0xAD, 0xBD) : Color.FromArgb(0x6B, 0x72, 0x80);
    public static Color Faint  => Dark ? Color.FromArgb(0x77, 0x82, 0x96) : Color.FromArgb(0x9A, 0xA0, 0xAA);

    public static Color Green  => Dark ? Color.FromArgb(0x61, 0xE7, 0xA4) : Color.FromArgb(0x2E, 0xA0, 0x43);
    public static Color Amber  => Dark ? Color.FromArgb(0xFF, 0xC1, 0x5D) : Color.FromArgb(0xCC, 0x7A, 0x12);
    public static Color Red    => Dark ? Color.FromArgb(0xFF, 0x2F, 0x7D) : Color.FromArgb(0xD6, 0x1F, 0x69);

    // secondary data colour from the ghostdeck.dev palette (--violet), e.g. GPU-side gauges
    public static Color Violet => Color.FromArgb(0x8D, 0x63, 0xFF);

    /// <summary>Profile colour adjusted for legibility on the current surface.</summary>
    public static Color Profile(Color c)
    {
        if (!Dark) return c;
        return ControlPaint.Light(c, 0.25f);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
