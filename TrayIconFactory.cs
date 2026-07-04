using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>Rysuje ikone tray w kolorze aktywnego profilu (squircle + tachometr).</summary>
public static class TrayIconFactory
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

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

            float cx = S / 2f, cy = S / 2f + 1f, r = S * 0.26f;
            using var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 3f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, 135, 270);

            double ang = 215 * Math.PI / 180.0;
            using var needle = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(needle, cx, cy, (float)(cx + Math.Cos(ang) * r * 0.8), (float)(cy + Math.Sin(ang) * r * 0.8));
            g.FillEllipse(Brushes.White, cx - 2f, cy - 2f, 4f, 4f);
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
