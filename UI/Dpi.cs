namespace GhostDeck;

/// <summary>
/// The one place every scale-dependent size and font comes from. Controls never read
/// <c>DeviceDpi</c> themselves: they ask <see cref="Of"/>, which returns the monitor's real DPI.
///
/// <see cref="Override"/> exists for the preview tool only (DOC/tools/render-harness, FAKEDPI):
/// it makes the whole UI lay out and draw as on a monitor at that scale - 200 % on a 140 %
/// laptop - without touching the Windows display settings. The application never sets it, so
/// for users nothing changes. Fonts given in points are converted to pixels by Windows at the
/// real DPI, so while an override is active <see cref="Font(string, float)"/> enlarges them by
/// Override / real DPI; fonts given in pixels are sized from the scaled geometry already and
/// are left alone.
/// </summary>
public static class Dpi
{
    /// <summary>Preview tool only. Null in the application.</summary>
    public static int? Override { get; set; }

    /// <summary>The DPI a control lays out and draws at (96 = 100 %).</summary>
    public static int Of(Control c) => Override ?? c.DeviceDpi;

    /// <summary>Scale factor of a control (1.0 = 100 %).</summary>
    public static float K(Control c) => Of(c) / 96f;

    private static int? _real;

    /// <summary>The DPI Windows converts point-sized fonts at (the system DPI).</summary>
    public static int Real
    {
        get
        {
            if (_real is int r) return r;
            try { using var g = Graphics.FromHwnd(IntPtr.Zero); _real = (int)Math.Round(g.DpiY); }
            catch { _real = 96; }
            return _real.Value;
        }
    }

    /// <summary>1 in the application; Override / real DPI in the preview tool.</summary>
    public static float FontScale => Override is int o ? o / (float)Real : 1f;

    public static Font Font(string family, float emSize) =>
        new(family, emSize * FontScale);

    public static Font Font(string family, float emSize, FontStyle style) =>
        new(family, emSize * FontScale, style);

    public static Font Font(string family, float emSize, GraphicsUnit unit) =>
        new(family, unit == GraphicsUnit.Pixel ? emSize : emSize * FontScale, unit);

    public static Font Font(string family, float emSize, FontStyle style, GraphicsUnit unit) =>
        new(family, unit == GraphicsUnit.Pixel ? emSize : emSize * FontScale, style, unit);
}
