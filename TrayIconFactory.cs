using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Draws the app/tray icons. Four user-selectable styles (Settings → Application icon):
/// 0 = GhostDeck logo (embedded app.ico for windows, white ghost on a profile-coloured squircle
/// in the tray), 1 = ghost on a dark rounded tile (default; brand-cyan ghost for windows,
/// profile-coloured ghost in the tray), 2 = the same on a light tile, 3 = the classic pre-1.18
/// gauge (tachometer squircle). Tray icons always follow the active profile colour.
/// </summary>
public static class TrayIconFactory
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    // brand colours of the original logotype outline / tiles
    private static readonly Color BrandCyan = Color.FromArgb(0x3D, 0xE3, 0xFF);
    private static readonly Color BrandBlue = Color.FromArgb(0x3C, 0x7D, 0xFF);
    private static readonly Color DarkTile  = Color.FromArgb(0x0A, 0x0D, 0x14);
    private static readonly Color LightTile = Color.FromArgb(0xF2, 0xF6, 0xFE);

    /// <summary>Active icon style (AppSettings.IconStyle); set at startup and on settings change.</summary>
    public static int Style = 1;

    private static readonly Dictionary<int, Icon> _appIcons = new();

    /// <summary>Window/taskbar icon for the active style (cached per style).</summary>
    public static Icon AppIcon()
    {
        int s = Style;
        if (_appIcons.TryGetValue(s, out var cached)) return cached;
        Icon icon = s switch
        {
            0 => LoadLogoIcon(),
            2 => BuildIcon((g, sz) => DrawTile(g, sz, LightTile, BrandBlue)),
            3 => BuildIcon((g, sz) => DrawGauge(g, sz, BrandBlue)),
            _ => BuildIcon((g, sz) => DrawTile(g, sz, DarkTile, BrandCyan)),
        };
        return _appIcons[s] = icon;
    }

    /// <summary>Tray icon in the active profile colour, per the active style.</summary>
    public static Icon Create(Color color)
    {
        const int S = 32;
        using var bmp = new Bitmap(S, S);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            switch (Style)
            {
                case 0: DrawSquircleGhost(g, S, color); break;                                  // white ghost, profile squircle
                case 2: DrawTile(g, S, LightTile, ControlPaint.Dark(color, 0.02f)); break;      // profile ghost, light tile
                case 3: DrawGauge(g, S, color); break;                                          // classic gauge, profile squircle
                default: DrawTile(g, S, DarkTile, Theme.Profile(color)); break;                 // profile ghost, dark tile
            }
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

    /// <summary>Preview of a style for the Settings picker (brand colours, not profile-tinted).</summary>
    public static void DrawStylePreview(Graphics g, int style, float x, float y, float size)
    {
        if (style == 0)
        {
            if (!_appIcons.TryGetValue(0, out var logo)) _appIcons[0] = logo = LoadLogoIcon();
            try
            {
                using var sized = new Icon(logo, new Size((int)size, (int)size));
                using var bmp = sized.ToBitmap();
                g.DrawImage(bmp, x, y, size, size);
                return;
            }
            catch { /* fall through to the vector ghost */ }
        }
        var t = g.Transform;
        g.TranslateTransform(x, y);
        switch (style)
        {
            case 2: DrawTile(g, size, LightTile, BrandBlue); break;
            case 3: DrawGauge(g, size, BrandBlue); break;
            default: DrawTile(g, size, DarkTile, BrandCyan); break;
        }
        g.Transform = t;
    }

    // ---- style painters (all on the 32-unit grid, scaled by size) ----

    /// <summary>Rounded tile + ghost (rounded corners so the taskbar doesn't show a hard black square).</summary>
    private static void DrawTile(Graphics g, float size, Color tile, Color ghost)
    {
        float k = size / 32f;
        using (var path = Theme.RoundRect(new RectangleF(0.5f * k, 0.5f * k, size - k, size - k), 7f * k))
        {
            using var b = new SolidBrush(tile);
            g.FillPath(b, path);
        }
        DrawGhost(g, 0, 0, size, ghost, tile);
    }

    /// <summary>Profile-coloured gradient squircle with the white ghost (the v1.18.0 tray look).</summary>
    private static void DrawSquircleGhost(Graphics g, float size, Color color)
    {
        using (var path = Squircle(g, size, color)) { }
        DrawGhost(g, 0, 0, size, Color.White, color);
    }

    /// <summary>The classic pre-1.18 icon: gradient squircle + white tachometer.</summary>
    private static void DrawGauge(Graphics g, float size, Color color)
    {
        using (var path = Squircle(g, size, color)) { }
        float k = size / 32f;
        float cx = size / 2f, cy = size / 2f + k, r = size * 0.26f;
        using var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 3f * k)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, 135, 270);

        double ang = 215 * Math.PI / 180.0;
        using var needle = new Pen(Color.White, 2.4f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(needle, cx, cy, (float)(cx + Math.Cos(ang) * r * 0.8), (float)(cy + Math.Sin(ang) * r * 0.8));
        g.FillEllipse(Brushes.White, cx - 2f * k, cy - 2f * k, 4f * k, 4f * k);
    }

    /// <summary>Fills the gradient squircle background and returns its (disposed-by-caller) path.</summary>
    private static GraphicsPath Squircle(Graphics g, float size, Color color)
    {
        float k = size / 32f;
        var rect = new RectangleF(1f * k, 1f * k, size - 2f * k, size - 2f * k);
        float rad = 9f * k, d = rad * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var bg = new LinearGradientBrush(rect,
            ControlPaint.Light(color, 0.3f), ControlPaint.Dark(color, 0.10f), 60f);
        g.FillPath(bg, path);
        return path;
    }

    // ---- icon assembly ----

    private static Icon LoadLogoIcon()
    {
        try
        {
            using var s = typeof(TrayIconFactory).Assembly.GetManifestResourceStream("GhostDeck.app.ico");
            if (s != null) return new Icon(s);
        }
        catch { }
        return BuildIcon((g, sz) => DrawTile(g, sz, DarkTile, BrandCyan));
    }

    /// <summary>Builds a multi-size icon (PNG-compressed ICO in memory) from a vector painter.</summary>
    private static Icon BuildIcon(Action<Graphics, int> draw)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>(sizes.Length);
        foreach (int s in sizes)
        {
            using var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                draw(g, s);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        var ico = new MemoryStream();
        using (var w = new BinaryWriter(ico, System.Text.Encoding.Default, leaveOpen: true))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);   // ICONDIR
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s == 256 ? 0 : s));   // width (0 = 256)
                w.Write((byte)(s == 256 ? 0 : s));   // height
                w.Write((byte)0); w.Write((byte)0);  // palette, reserved
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length);
                w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        ico.Position = 0;
        return new Icon(ico);
    }
}
