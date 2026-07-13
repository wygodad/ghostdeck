using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>Rysuje ikone tray w kolorze aktywnego profilu (squircle + duszek GhostDeck).</summary>
public static class TrayIconFactory
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private static Icon? _appIcon;

    /// <summary>Multi-size ghost app icon (embedded app.ico) for window/taskbar use.</summary>
    public static Icon AppIcon()
    {
        if (_appIcon != null) return _appIcon;
        try
        {
            using var s = typeof(TrayIconFactory).Assembly.GetManifestResourceStream("GhostDeck.app.ico");
            if (s != null) return _appIcon = new Icon(s);
        }
        catch { }
        return _appIcon = Create(Theme.Accent);
    }

    /// <summary>
    /// Ghost mark scaled into a box (32-unit design grid): dome, straight sides, three feet
    /// bumps, slanted almond eyes punched in <paramref name="eyes"/> (outer corners raised).
    /// </summary>
    public static void DrawGhost(Graphics g, float x, float y, float size, Color body, Color eyes)
    {
        var old = g.Transform;
        g.TranslateTransform(x, y);
        float k = size / 32f;
        g.ScaleTransform(k, k);

        using (var ghost = new GraphicsPath())
        {
            ghost.AddArc(7f, 5f, 18f, 18f, 180f, 180f);      // dome, (7,14) over the top to (25,14)
            ghost.AddLine(25f, 14f, 25f, 23.5f);             // right side
            ghost.AddArc(19f, 20.5f, 6f, 6f, 0f, 180f);      // feet, right to left
            ghost.AddArc(13f, 20.5f, 6f, 6f, 0f, 180f);
            ghost.AddArc(7f, 20.5f, 6f, 6f, 0f, 180f);
            ghost.CloseFigure();                             // left side back up to the dome
            using var b = new SolidBrush(body);
            g.FillPath(b, ghost);
        }

        using (var eyeB = new SolidBrush(eyes))
            foreach (var (ex, rot) in new[] { (12.7f, 20f), (19.3f, -20f) })
            {
                var st = g.Save();
                g.TranslateTransform(ex, 14.5f);
                g.RotateTransform(rot);
                g.FillEllipse(eyeB, -1.7f, -2.9f, 3.4f, 5.8f);
                g.Restore(st);
            }

        g.Transform = old;
    }

    public static Icon Create(Color color)
    {
        const int S = 32;
        using var bmp = new Bitmap(S, S);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var rect = new Rectangle(1, 1, S - 2, S - 2);
            const int rad = 9, d = rad * 2;
            using var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            using var bg = new LinearGradientBrush(rect,
                ControlPaint.Light(color, 0.3f), ControlPaint.Dark(color, 0.10f), 60f);
            g.FillPath(bg, path);

            DrawGhost(g, 0, 0, S, Color.White, color);
        }

        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(h);
        }
    }
}
