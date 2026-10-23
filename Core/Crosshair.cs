using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GhostDeck;

/// <summary>The built-in crosshair shapes; <see cref="Image"/> = the user's own picture.</summary>
public enum CrosshairKind
{
    Cross = 0, GapCross = 1, GapCrossDot = 2, Dot = 3, Circle = 4, CircleDot = 5, T = 6,
    X = 7, Chevron = 8, SquareDot = 9, Reticle = 10, Image = 11,
}

/// <summary>What the crosshair does on a left click (the mouse hook is installed only when this is not None).</summary>
public enum CrosshairClickFx { None = 0, Spread = 1, Pulse = 2, Flash = 3 }

/// <summary>What the right button does to the crosshair: nothing, hidden while held, or toggled per click.</summary>
public enum CrosshairAds { None = 0, HideWhileHeld = 1, ToggleOnClick = 2 }

/// <summary>
/// Everything that decides how the crosshair looks and behaves (roadmap #118, discussion #247).
/// One instance is the live style (<see cref="AppSettings.Crosshair"/>); the named presets
/// (<see cref="AppSettings.CrosshairPresets"/>) are copies of it with a <see cref="Name"/>.
/// Sizes are logical pixels (scaled by the screen's DPI when drawn, like the overlay's text).
/// </summary>
public sealed class CrosshairStyle
{
    public const int MinSize = 8, MaxSize = 256, MaxThickness = 8, MaxOutline = 3, MinOpacity = 20,
                     MaxOffset = 500, MaxSoftness = 8;

    public string Name { get; set; } = "";              // presets only; the live style has none
    public int Kind { get; set; } = (int)CrosshairKind.Cross;
    public string Color { get; set; } = "#61E7A4";      // Super Battery green: visible on most games
    public int Size { get; set; } = 32;
    public int Thickness { get; set; } = 2;
    public int Outline { get; set; } = 1;               // dark rim, 0 = none
    public int Opacity { get; set; } = 100;             // 20..100 %
    public int OffsetX { get; set; }                    // from the screen centre (or the cursor)
    public int OffsetY { get; set; }
    public int Rotation { get; set; }                   // degrees, 0..359
    public int Softness { get; set; }                   // blur radius, 0..8
    public int ClickFx { get; set; } = (int)CrosshairClickFx.None;
    public int Ads { get; set; } = (int)CrosshairAds.None;
    public bool FollowCursor { get; set; }

    [JsonIgnore] public CrosshairKind KindId => Enum.IsDefined(typeof(CrosshairKind), Kind) ? (CrosshairKind)Kind : CrosshairKind.Cross;
    [JsonIgnore] public CrosshairClickFx ClickFxId => Enum.IsDefined(typeof(CrosshairClickFx), ClickFx) ? (CrosshairClickFx)ClickFx : CrosshairClickFx.None;
    [JsonIgnore] public CrosshairAds AdsId => Enum.IsDefined(typeof(CrosshairAds), Ads) ? (CrosshairAds)Ads : CrosshairAds.None;
    /// <summary>True when a left click or the right button has to be watched.</summary>
    [JsonIgnore] public bool NeedsMouseHook => ClickFxId != CrosshairClickFx.None || AdsId != CrosshairAds.None;

    public System.Drawing.Color ColorValue
    {
        get { try { return ColorTranslator.FromHtml(Color); } catch { return ColorTranslator.FromHtml("#61E7A4"); } }
    }

    // Style fields a newer version added: kept and written back unchanged (see AppSettings.Extra).
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public CrosshairStyle Clone() => (CrosshairStyle)MemberwiseClone();

    /// <summary>Copies the look (everything but the name) from another style.</summary>
    public void CopyFrom(CrosshairStyle o)
    {
        Kind = o.Kind; Color = o.Color; Size = o.Size; Thickness = o.Thickness; Outline = o.Outline;
        Opacity = o.Opacity; OffsetX = o.OffsetX; OffsetY = o.OffsetY; Rotation = o.Rotation;
        Softness = o.Softness; ClickFx = o.ClickFx; Ads = o.Ads; FollowCursor = o.FollowCursor;
    }

    /// <summary>Clamps every field into its range (settings files are edited by hand, too).</summary>
    public void Normalize()
    {
        Kind = (int)KindId;
        Size = Math.Clamp(Size, MinSize, MaxSize);
        Thickness = Math.Clamp(Thickness, 1, MaxThickness);
        Outline = Math.Clamp(Outline, 0, MaxOutline);
        Opacity = Math.Clamp(Opacity, MinOpacity, 100);
        OffsetX = Math.Clamp(OffsetX, -MaxOffset, MaxOffset);
        OffsetY = Math.Clamp(OffsetY, -MaxOffset, MaxOffset);
        Rotation = ((Rotation % 360) + 360) % 360;
        Softness = Math.Clamp(Softness, 0, MaxSoftness);
        ClickFx = (int)ClickFxId;
        Ads = (int)AdsId;
        _ = ColorValue;
        Color = ColorTranslator.ToHtml(ColorValue);
    }

    /// <summary>Same look (the name does not count).</summary>
    public bool LooksLike(CrosshairStyle o) => Encode() == o.Encode();

    // ---------------- share code ----------------
    // "GDX1." + base64url of 20 bytes: kind, r, g, b, size (2), thickness, outline, opacity,
    // offset x (2), offset y (2), rotation (2), softness, click fx, ads, flags, checksum. Version
    // 1 is the prefix; a later layout gets a new prefix and this decoder keeps reading the old one.
    private const string CodePrefix = "GDX1.";

    public string Encode()
    {
        var c = ColorValue;
        var b = new byte[20];
        b[0] = (byte)KindId; b[1] = c.R; b[2] = c.G; b[3] = c.B;
        Put16(b, 4, Math.Clamp(Size, MinSize, MaxSize));
        b[6] = (byte)Math.Clamp(Thickness, 1, MaxThickness);
        b[7] = (byte)Math.Clamp(Outline, 0, MaxOutline);
        b[8] = (byte)Math.Clamp(Opacity, MinOpacity, 100);
        Put16(b, 9, Math.Clamp(OffsetX, -MaxOffset, MaxOffset) + 32768);
        Put16(b, 11, Math.Clamp(OffsetY, -MaxOffset, MaxOffset) + 32768);
        Put16(b, 13, ((Rotation % 360) + 360) % 360);
        b[15] = (byte)Math.Clamp(Softness, 0, MaxSoftness);
        b[16] = (byte)ClickFxId;
        b[17] = (byte)AdsId;
        b[18] = (byte)(FollowCursor ? 1 : 0);
        int sum = 0; for (int i = 0; i < 19; i++) sum += b[i];
        b[19] = (byte)(sum & 0xFF);
        return CodePrefix + Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Null = not a GhostDeck crosshair code (wrong prefix, length or checksum).</summary>
    public static CrosshairStyle? Decode(string? text)
    {
        if (text == null) return null;
        string t = text.Trim();
        if (!t.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        string body = t[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
        while (body.Length % 4 != 0) body += "=";
        byte[] b;
        try { b = Convert.FromBase64String(body); } catch { return null; }
        if (b.Length != 20) return null;
        int sum = 0; for (int i = 0; i < 19; i++) sum += b[i];
        if ((byte)(sum & 0xFF) != b[19]) return null;
        var s = new CrosshairStyle
        {
            Kind = b[0], Color = ColorTranslator.ToHtml(System.Drawing.Color.FromArgb(b[1], b[2], b[3])),
            Size = Get16(b, 4), Thickness = b[6], Outline = b[7], Opacity = b[8],
            OffsetX = Get16(b, 9) - 32768, OffsetY = Get16(b, 11) - 32768, Rotation = Get16(b, 13),
            Softness = b[15], ClickFx = b[16], Ads = b[17], FollowCursor = (b[18] & 1) != 0,
        };
        s.Normalize();
        return s;
    }

    private static void Put16(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }
    private static int Get16(byte[] b, int at) => (b[at] << 8) | b[at + 1];

    // ---------------- SVG export ----------------
    /// <summary>
    /// The built-in shape as an SVG document (lines, circles and rectangles; the rim as a wider
    /// dark stroke underneath, the softness as a Gaussian blur filter, the rotation as a
    /// transform). Null for the custom image, which has no vector form.
    /// </summary>
    public string? ToSvg()
    {
        if (KindId == CrosshairKind.Image) return null;
        var geo = CrosshairPainter.Geometry(this, 1f, 0f);
        float m = Thickness + Outline * 2 + Softness * 3 + 2;
        float half = Size / 2f + m;
        float w = half * 2;
        var inv = CultureInfo.InvariantCulture;
        string F(float v) => v.ToString("0.##", inv);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(w)).Append("\" height=\"").Append(F(w))
          .Append("\" viewBox=\"").Append(F(-half)).Append(' ').Append(F(-half)).Append(' ').Append(F(w)).Append(' ').Append(F(w)).Append("\">\n");
        sb.Append("<!-- GhostDeck crosshair ").Append(Encode()).Append(" -->\n");
        if (Softness > 0)
            sb.Append("<defs><filter id=\"soft\" x=\"-50%\" y=\"-50%\" width=\"200%\" height=\"200%\"><feGaussianBlur stdDeviation=\"")
              .Append(F(Softness * 0.6f)).Append("\"/></filter></defs>\n");
        sb.Append("<g opacity=\"").Append(F(Math.Clamp(Opacity, MinOpacity, 100) / 100f)).Append('"');
        if (Rotation != 0) sb.Append(" transform=\"rotate(").Append(Rotation.ToString(inv)).Append(")\"");
        if (Softness > 0) sb.Append(" filter=\"url(#soft)\"");
        sb.Append(">\n");
        string col = ColorTranslator.ToHtml(ColorValue);
        void Pass(string stroke, float width, bool fillDots)
        {
            foreach (var (a, b) in geo.Lines)
                sb.Append("<line x1=\"").Append(F(a.X)).Append("\" y1=\"").Append(F(a.Y)).Append("\" x2=\"").Append(F(b.X)).Append("\" y2=\"").Append(F(b.Y))
                  .Append("\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(F(width)).Append("\" stroke-linecap=\"butt\"/>\n");
            foreach (var (c, r) in geo.Rings)
                sb.Append("<circle cx=\"").Append(F(c.X)).Append("\" cy=\"").Append(F(c.Y)).Append("\" r=\"").Append(F(r))
                  .Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(F(width)).Append("\"/>\n");
            foreach (var r in geo.Boxes)
                sb.Append("<rect x=\"").Append(F(r.X)).Append("\" y=\"").Append(F(r.Y)).Append("\" width=\"").Append(F(r.Width)).Append("\" height=\"").Append(F(r.Height))
                  .Append("\" fill=\"none\" stroke=\"").Append(stroke).Append("\" stroke-width=\"").Append(F(width)).Append("\"/>\n");
            foreach (var (c, r) in geo.Dots)
            {
                float rr = fillDots ? r : r + (width - Thickness) / 2f;
                sb.Append("<circle cx=\"").Append(F(c.X)).Append("\" cy=\"").Append(F(c.Y)).Append("\" r=\"").Append(F(rr))
                  .Append("\" fill=\"").Append(stroke).Append("\"/>\n");
            }
        }
        if (Outline > 0) Pass("#000000", Thickness + Outline * 2, fillDots: false);
        Pass(col, Thickness, fillDots: true);
        sb.Append("</g>\n</svg>\n");
        return sb.ToString();
    }
}

/// <summary>
/// Draws a <see cref="CrosshairStyle"/> into a bitmap: the shape as a path (a wider dark pass for
/// the rim, then the colour), rotated about the centre, blurred for the softness, faded to the
/// opacity. The same routine feeds the screen window, the Settings preview, the shape tiles and
/// the PNG export, so what the preview shows is what the game gets.
/// </summary>
public static class CrosshairPainter
{
    /// <summary>Shape primitives in logical units around (0,0), before rotation.</summary>
    public sealed class Shape
    {
        public List<(PointF a, PointF b)> Lines { get; } = new();
        public List<(PointF c, float r)> Rings { get; } = new();
        public List<RectangleF> Boxes { get; } = new();
        public List<(PointF c, float r)> Dots { get; } = new();   // filled
    }

    /// <summary>Animation length of the click effects.</summary>
    public const int FxMs = 180;

    /// <summary>
    /// The primitives of a style at the given scale. <paramref name="fx"/> (0..1) is the click
    /// effect's progress: Spread pushes the arms outwards, Pulse grows everything, Flash changes
    /// nothing here (it brightens the colour in <see cref="Render"/>).
    /// </summary>
    public static Shape Geometry(CrosshairStyle st, float k, float fx)
    {
        var s = new Shape();
        var kind = st.KindId;
        float wave = fx <= 0f || fx >= 1f ? 0f : MathF.Sin(fx * MathF.PI);   // up and back down
        float grow = st.ClickFxId == CrosshairClickFx.Pulse ? 1f + 0.25f * wave : 1f;
        // the shapes without arms spread by growing their ring, box or dot; the centre dot of a
        // ring stays put, so the ring visibly moves away from it
        float sg = st.ClickFxId == CrosshairClickFx.Spread ? 1f + 0.3f * wave : 1f;
        float S = st.Size * k * grow;                     // full extent
        float h = S / 2f;
        float t = st.Thickness * k;
        float gap = S * 0.18f + (st.ClickFxId == CrosshairClickFx.Spread ? S * 0.25f * wave : 0f);
        float dotR = Math.Max(1.2f * k, t * 0.9f);
        void Line(float x1, float y1, float x2, float y2) => s.Lines.Add((new PointF(x1, y1), new PointF(x2, y2)));
        void Arms(float from)
        {
            Line(-h, 0, -from, 0); Line(from, 0, h, 0);
            Line(0, -h, 0, -from); Line(0, from, 0, h);
        }
        switch (kind)
        {
            case CrosshairKind.Cross:
                if (st.ClickFxId == CrosshairClickFx.Spread && wave > 0f) Arms(gap - S * 0.18f); else { Line(-h, 0, h, 0); Line(0, -h, 0, h); }
                break;
            case CrosshairKind.GapCross: Arms(gap); break;
            case CrosshairKind.GapCrossDot: Arms(gap); s.Dots.Add((PointF.Empty, dotR)); break;
            case CrosshairKind.Dot: s.Dots.Add((PointF.Empty, Math.Max(1.5f * k, S * 0.18f) * sg)); break;
            case CrosshairKind.Circle: s.Rings.Add((PointF.Empty, (h - t / 2f) * sg)); break;
            case CrosshairKind.CircleDot: s.Rings.Add((PointF.Empty, (h - t / 2f) * sg)); s.Dots.Add((PointF.Empty, dotR)); break;
            case CrosshairKind.T:
                Line(-h, 0, -gap, 0); Line(gap, 0, h, 0); Line(0, gap, 0, h);
                break;
            case CrosshairKind.X:
            {
                float d = h * 0.7071f, g = gap * 0.7071f;
                if (st.ClickFxId == CrosshairClickFx.Spread && wave > 0f) g = (gap - S * 0.18f) * 0.7071f; else g = 0f;
                Line(-d, -d, -g, -g); Line(g, g, d, d); Line(d, -d, g, -g); Line(-g, g, -d, d);
                break;
            }
            case CrosshairKind.Chevron:
            {
                // apex at the centre, arms down at 40 degrees from the vertical
                float dx = h * MathF.Sin(40f * MathF.PI / 180f), dy = h * MathF.Cos(40f * MathF.PI / 180f);
                float g = st.ClickFxId == CrosshairClickFx.Spread ? S * 0.25f * wave : 0f;
                Line(0, g, -dx, g + dy); Line(0, g, dx, g + dy);
                break;
            }
            case CrosshairKind.SquareDot:
            {
                float side = S * 0.72f * sg;
                s.Boxes.Add(new RectangleF(-side / 2f + t / 2f, -side / 2f + t / 2f, side - t, side - t));
                s.Dots.Add((PointF.Empty, dotR));
                break;
            }
            case CrosshairKind.Reticle:
            {
                float r = S * 0.3f * sg;
                s.Rings.Add((PointF.Empty, r));
                float from = r + S * 0.18f * 0.3f, to = h * sg;
                Line(-to, 0, -from, 0); Line(from, 0, to, 0); Line(0, -to, 0, -from); Line(0, from, 0, to);
                s.Dots.Add((PointF.Empty, dotR));
                break;
            }
            case CrosshairKind.Image:
                break;   // the picture is drawn by Render
        }
        return s;
    }

    /// <summary>Pixel size of the bitmap a style needs at the given scale (shape plus rim and blur margin).</summary>
    public static int Extent(CrosshairStyle st, float k)
    {
        float grow = st.ClickFxId == CrosshairClickFx.Pulse ? 1.25f : st.ClickFxId == CrosshairClickFx.Spread ? 1.3f : 1f;
        float margin = (st.Thickness + st.Outline * 2) * k + st.Softness * k * 3f + 4f * k;
        return (int)MathF.Ceiling(st.Size * k * grow + margin * 2f) | 1;   // odd: a true centre pixel
    }

    /// <summary>
    /// Renders the style into a premultiplied-ARGB bitmap, centred. <paramref name="k"/> is
    /// the DPI scale (1 at 100 %), <paramref name="fx"/> the click effect's progress (0 = at rest),
    /// <paramref name="custom"/> the user's picture for <see cref="CrosshairKind.Image"/> (null
    /// falls back to a plain cross, so a missing file never leaves a blank crosshair).
    /// </summary>
    public static Bitmap Render(CrosshairStyle st, Image? custom, float k, float fx = 0f)
    {
        int n = Extent(st, k);
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
        float wave = fx <= 0f || fx >= 1f ? 0f : MathF.Sin(fx * MathF.PI);
        var col = st.ColorValue;
        if (st.ClickFxId == CrosshairClickFx.Flash && wave > 0f)
            col = System.Drawing.Color.FromArgb((int)(col.R + (255 - col.R) * wave), (int)(col.G + (255 - col.G) * wave), (int)(col.B + (255 - col.B) * wave));
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(System.Drawing.Color.Transparent);
            g.TranslateTransform(n / 2f, n / 2f);
            if (st.Rotation != 0) g.RotateTransform(st.Rotation);
            if (st.KindId == CrosshairKind.Image && custom != null)
            {
                float grow = st.ClickFxId == CrosshairClickFx.Pulse ? 1f + 0.25f * wave : st.ClickFxId == CrosshairClickFx.Spread ? 1f + 0.3f * wave : 1f;
                float S = st.Size * k * grow;
                // fit the picture into the size box, keeping its proportions
                float sc = Math.Min(S / Math.Max(1, custom.Width), S / Math.Max(1, custom.Height));
                float w = custom.Width * sc, h = custom.Height * sc;
                if (st.ClickFxId == CrosshairClickFx.Flash && wave > 0f)
                {
                    var m = new ColorMatrix { Matrix40 = wave * 0.6f, Matrix41 = wave * 0.6f, Matrix42 = wave * 0.6f };
                    using var ia = new ImageAttributes();
                    ia.SetColorMatrix(m);
                    g.DrawImage(custom, new Rectangle((int)(-w / 2f), (int)(-h / 2f), (int)w, (int)h), 0, 0, custom.Width, custom.Height, GraphicsUnit.Pixel, ia);
                }
                else g.DrawImage(custom, new RectangleF(-w / 2f, -h / 2f, w, h));
            }
            else
            {
                var geo = Geometry(st.KindId == CrosshairKind.Image ? Fallback(st) : st, k, fx);
                float t = st.Thickness * k;
                if (st.Outline > 0)
                {
                    float rim = st.Outline * k;
                    using var dark = new Pen(System.Drawing.Color.FromArgb(220, 0, 0, 0), t + rim * 2f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
                    using var darkB = new SolidBrush(System.Drawing.Color.FromArgb(220, 0, 0, 0));
                    Strokes(g, geo, dark);
                    foreach (var (c, r) in geo.Dots) g.FillEllipse(darkB, c.X - r - rim, c.Y - r - rim, (r + rim) * 2f, (r + rim) * 2f);
                }
                using var pen = new Pen(col, t) { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
                using var brush = new SolidBrush(col);
                Strokes(g, geo, pen);
                foreach (var (c, r) in geo.Dots) g.FillEllipse(brush, c.X - r, c.Y - r, r * 2f, r * 2f);
            }
        }
        int blur = (int)MathF.Round(st.Softness * k);
        float alpha = Math.Clamp(st.Opacity, CrosshairStyle.MinOpacity, 100) / 100f;
        if (blur > 0 || alpha < 1f) PostProcess(bmp, blur, alpha);
        return bmp;
    }

    private static CrosshairStyle Fallback(CrosshairStyle st) { var c = st.Clone(); c.Kind = (int)CrosshairKind.Cross; return c; }

    private static void Strokes(Graphics g, Shape geo, Pen pen)
    {
        foreach (var (a, b) in geo.Lines) g.DrawLine(pen, a, b);
        foreach (var (c, r) in geo.Rings) g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2f, r * 2f);
        foreach (var r in geo.Boxes) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    // Three box-blur passes approximate a Gaussian; the data is premultiplied, so blurring all
    // four channels alike is correct, and the opacity is one multiply per channel.
    private static void PostProcess(Bitmap bmp, int radius, float alpha)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int n = w * h;
            var src = new int[n];
            var tmp = new int[n];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, src, 0, n);
            if (radius > 0)
            {
                int r = Math.Max(1, (int)MathF.Round(radius / 1.7f));   // three passes of this radius ~ a Gaussian of 'radius'
                for (int pass = 0; pass < 3; pass++) { BoxH(src, tmp, w, h, r); BoxV(tmp, src, w, h, r); }
            }
            if (alpha < 1f)
                for (int i = 0; i < n; i++)
                {
                    uint p = (uint)src[i];
                    uint a = (uint)(((p >> 24) & 0xFF) * alpha), rr = (uint)(((p >> 16) & 0xFF) * alpha), gg = (uint)(((p >> 8) & 0xFF) * alpha), bb = (uint)((p & 0xFF) * alpha);
                    src[i] = (int)((a << 24) | (rr << 16) | (gg << 8) | bb);
                }
            System.Runtime.InteropServices.Marshal.Copy(src, 0, data.Scan0, n);
        }
        finally { bmp.UnlockBits(data); }
    }

    private static void BoxH(int[] src, int[] dst, int w, int h, int r)
    {
        int div = r * 2 + 1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            long a = 0, rr = 0, g = 0, b = 0;
            for (int x = -r; x <= r; x++) Add(src[row + Math.Clamp(x, 0, w - 1)], 1, ref a, ref rr, ref g, ref b);
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = Pack(a / div, rr / div, g / div, b / div);
                Add(src[row + Math.Clamp(x + r + 1, 0, w - 1)], 1, ref a, ref rr, ref g, ref b);
                Add(src[row + Math.Clamp(x - r, 0, w - 1)], -1, ref a, ref rr, ref g, ref b);
            }
        }
    }

    private static void BoxV(int[] src, int[] dst, int w, int h, int r)
    {
        int div = r * 2 + 1;
        for (int x = 0; x < w; x++)
        {
            long a = 0, rr = 0, g = 0, b = 0;
            for (int y = -r; y <= r; y++) Add(src[Math.Clamp(y, 0, h - 1) * w + x], 1, ref a, ref rr, ref g, ref b);
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = Pack(a / div, rr / div, g / div, b / div);
                Add(src[Math.Clamp(y + r + 1, 0, h - 1) * w + x], 1, ref a, ref rr, ref g, ref b);
                Add(src[Math.Clamp(y - r, 0, h - 1) * w + x], -1, ref a, ref rr, ref g, ref b);
            }
        }
    }

    private static void Add(int p, int sign, ref long a, ref long r, ref long g, ref long b)
    {
        uint u = (uint)p;
        a += sign * ((u >> 24) & 0xFF); r += sign * ((u >> 16) & 0xFF); g += sign * ((u >> 8) & 0xFF); b += sign * (u & 0xFF);
    }

    private static int Pack(long a, long r, long g, long b) => (int)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b);

    // ---------------- the custom picture ----------------
    public const int MaxImagePx = 512;
    public const long MaxImageBytes = 1024 * 1024;
    public static string ImageDir => Path.Combine(AppSettings.Dir, "crosshair");
    public static string ImagePath => Path.Combine(ImageDir, "custom.png");

    /// <summary>
    /// Checks a picked file and copies it into the settings folder as PNG (one custom picture at
    /// a time; the next import replaces it). Returns null when stored, else the reason: "size"
    /// (over the pixel or byte limit) or "read" (not an image this Windows can decode).
    /// </summary>
    public static string? ImportImage(string path, out Size dims)
    {
        dims = Size.Empty;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return "read";
            if (fi.Length > MaxImageBytes) return "size";
            using var img = Image.FromFile(path);
            dims = img.Size;
            if (img.Width > MaxImagePx || img.Height > MaxImagePx || img.Width < 1 || img.Height < 1) return "size";
            Directory.CreateDirectory(ImageDir);
            using var copy = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy)) { g.Clear(System.Drawing.Color.Transparent); g.DrawImageUnscaled(img, 0, 0); }
            copy.Save(ImagePath, ImageFormat.Png);   // always PNG, so a JPG/BMP import is stored losslessly from here on
            return null;
        }
        catch { return "read"; }
    }

    /// <summary>
    /// The picture carried inside a settings backup, stored with exactly the checks of
    /// <see cref="ImportImage"/> (size cap, pixel cap, decoded and re-saved as PNG): the bytes go
    /// to a temporary file next to the stored picture and take the normal path from there.
    /// </summary>
    public static string? ImportImageBytes(byte[] bytes, out Size dims)
    {
        dims = Size.Empty;
        if (bytes.LongLength > MaxImageBytes) return "size";
        string tmp = Path.Combine(ImageDir, "import.tmp");
        try
        {
            Directory.CreateDirectory(ImageDir);
            File.WriteAllBytes(tmp, bytes);
            return ImportImage(tmp, out dims);
        }
        catch { return "read"; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    /// <summary>The stored picture, or null when there is none or it cannot be read.</summary>
    public static Bitmap? LoadImage()
    {
        try
        {
            if (!File.Exists(ImagePath)) return null;
            using var fs = File.OpenRead(ImagePath);
            using var img = Image.FromStream(fs);
            return new Bitmap(img);   // detached from the file, so the next import can overwrite it
        }
        catch { return null; }
    }

    public static bool HasImage => File.Exists(ImagePath);

    public static void DeleteImage() { try { File.Delete(ImagePath); } catch { } }
}
