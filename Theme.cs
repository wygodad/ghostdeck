using System.Drawing.Drawing2D;

namespace GhostDeck;

/// <summary>
/// Central light/dark palette for the tabbed UI. WinForms has no built-in theming,
/// so controls read these colours and re-apply on <see cref="Changed"/>.
/// Accent stays the app's brand purple in both modes (lightened slightly in dark).
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

    // brand accent (purple) - same hue both modes, a touch lighter on dark
    public static Color Accent     => Dark ? Color.FromArgb(0x9B, 0x8C, 0xF0) : Color.FromArgb(0x7C, 0x3A, 0xED);
    public static Color AccentText => Color.White;
    public static Color AccentSoft => Dark ? Color.FromArgb(0x2A, 0x25, 0x50) : Color.FromArgb(0xEE, 0xED, 0xFE);

    public static Color Page    => Dark ? Color.FromArgb(0x1B, 0x1D, 0x22) : Color.FromArgb(0xF4, 0xF4, 0xF7);
    public static Color Surface => Dark ? Color.FromArgb(0x23, 0x26, 0x2D) : Color.White;
    public static Color Card    => Dark ? Color.FromArgb(0x2A, 0x2E, 0x36) : Color.FromArgb(0xFA, 0xFA, 0xFC);
    public static Color Border  => Dark ? Color.FromArgb(0x3A, 0x3F, 0x49) : Color.FromArgb(0xE5, 0xE7, 0xEB);
    public static Color BorderStrong => Dark ? Color.FromArgb(0x4A, 0x50, 0x5C) : Color.FromArgb(0xD7, 0xDA, 0xE0);

    public static Color Text   => Dark ? Color.FromArgb(0xE7, 0xE9, 0xEE) : Color.FromArgb(0x1F, 0x24, 0x30);
    public static Color Muted  => Dark ? Color.FromArgb(0x9A, 0xA0, 0xAA) : Color.FromArgb(0x6B, 0x72, 0x80);
    public static Color Faint  => Dark ? Color.FromArgb(0x6C, 0x72, 0x7C) : Color.FromArgb(0x9A, 0xA0, 0xAA);

    public static Color Green  => Dark ? Color.FromArgb(0x4F, 0xC0, 0x6A) : Color.FromArgb(0x2E, 0xA0, 0x43);
    public static Color Amber  => Dark ? Color.FromArgb(0xE0, 0x9A, 0x2E) : Color.FromArgb(0xCC, 0x7A, 0x12);
    public static Color Red    => Dark ? Color.FromArgb(0xD8, 0x5A, 0x52) : Color.FromArgb(0xB0, 0x4A, 0x3A);

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
