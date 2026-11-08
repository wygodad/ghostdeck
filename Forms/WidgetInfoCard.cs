using System.Drawing;
using System.Drawing.Text;

namespace GhostDeck;

/// <summary>
/// The widget card (plan 7c): what a widget is and how people rate it, two clicks from the
/// Dashboard. The picture is the widget itself, painted by the same code as on the Dashboard
/// with the current data sample, so there is no second set of images to keep. Under it the
/// description (the card's body), your five stars to click, and the community line from the
/// weekly summary (average, votes, share of dashboards). "Add" appears when the widget is not
/// on the Dashboard yet. Opened from the ⋯ of a tile, from the Katalog view and from the
/// catalogue panel.
/// </summary>
internal sealed class WidgetInfoCard : GhostCardForm
{
    private readonly MainDeps _d;
    private readonly DashWidgetDef _def;
    private readonly IDashWidget _widget;
    private readonly DashContext _ctx;
    private readonly Action _changed;
    private readonly Action? _add;
    private readonly Rectangle[] _stars = new Rectangle[5];   // window coordinates
    private Bitmap? _preview;

    public WidgetInfoCard(MainDeps d, DashWidgetDef def, IDashWidget widget, DashContext ctx, Action onChanged, Action? onAdd)
        : base("//WIDGET", Lang.T(def.NameKey), Lang.T(def.NameKey + "_d"), Lang.T("gen_ok"), "", () => { })
    {
        _d = d; _def = def; _widget = widget; _ctx = ctx; _changed = onChanged; _add = onAdd;
    }

    protected override int CardWidth => 640;
    protected override int SideInset => 8;
    protected override string AuxLabel => _add != null ? Lang.T("dash_add") : "";
    protected override Color AuxColor => Cyan;
    protected override void AuxClick() { _add?.Invoke(); Close(); }

    private int StarSize(float k) => (int)Math.Ceiling(24 * k);

    /// <summary>The widget at its default size, as large as the card allows (a 1x1 ring gets a roomier cell than on a crowded grid).</summary>
    private Bitmap Preview(int width, float k)
    {
        var size = _def.Sizes.Length > 0 ? _def.Sizes[0] : new Size(1, 1);
        int gap = _ctx.S(16);
        int cell = Math.Min((int)(170 * k), (width - gap * (size.Width - 1)) / size.Width);
        int w = size.Width * cell + (size.Width - 1) * gap, h = size.Height * cell + (size.Height - 1) * gap;
        if (_preview != null && _preview.Width == w && _preview.Height == h) return _preview;
        _preview?.Dispose();
        var bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h));
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, w, h);
            try { _widget.PaintStatic(g, r, _ctx); _widget.PaintLive(g, r, _ctx); } catch { }
        }
        return _preview = bmp;
    }

    protected override int MeasureContent(Graphics g, float k, int width) => Flow(g, k, width, null);

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Flow(g, k, area.Width, (area.Location, origin));
        g.TextRenderingHint = hint;
    }

    private int Flow(Graphics g, float k, int width, (Point at, Point origin)? paintAt)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        bool paint = paintAt != null;
        int x = paintAt?.at.X ?? 0, y0 = paintAt?.at.Y ?? 0, y = y0;
        using var capF = Dpi.Font("Segoe UI", 12.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var lineF = Dpi.Font("Segoe UI", 14.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.Word };
        float H(string s, Font f, int w) => g.MeasureString(s, f, w, fmt).Height;
        void Text(string s, Font f, Color c, float tx, float ty, int w, float h)
        {
            if (!paint) return;
            using var b = new SolidBrush(c);
            g.DrawString(s, f, b, new RectangleF(tx, ty, w, h + 3 * k), fmt);
        }

        // ---- the picture on a lighter field, so the widget's own card edge shows ----
        int pad = Ce(18 * k);
        var pv = Preview(width - pad * 2, k);
        int hField = pv.Height + pad * 2;
        if (paint)
        {
            using var path = RoundPath(new Rectangle(x, y, width, hField), Ce(10 * k));
            using var bg = new SolidBrush(FieldBg);
            g.FillPath(bg, path);
            g.DrawImage(pv, new Rectangle(x + (width - pv.Width) / 2, y + pad, pv.Width, pv.Height));
        }
        y += hField + Ce(18 * k);

        // ---- your stars ----
        int ss = StarSize(k);
        string cap = Lang.T("dash_your_rating");
        float capH = H(cap, capF, width);
        Text(cap, capF, Muted, x, y, width, capH);
        y += Ce(capH) + Ce(10 * k);
        int own = DashRatings.Own(_d, _def.TypeId);
        int hot = HotZone;   // 0..4 while the cursor is on a star
        int filled = hot >= 0 ? hot + 1 : own;
        if (paint)
        {
            var at = new Point(x, y);
            DashRatings.PaintStars(g, at, ss, filled, hot >= 0 || own > 0 ? Cyan : Muted, Color.FromArgb(70, Muted));
            for (int i = 0; i < 5; i++) { var r = DashRatings.StarRect(at, ss, i + 1); _stars[i] = new Rectangle(r.X + paintAt!.Value.origin.X, r.Y + paintAt.Value.origin.Y, r.Width, r.Height); }
            if (own > 0)
            {
                // "4 / 5" centred on the stars' middle line (the shapes are centred in their cells)
                string yours = own.ToString(Lang.Culture) + " / 5";
                float th = lineF.GetHeight(g);
                Text(yours, lineF, Ink, x + ss * 5 + Ce(14 * k), y + (ss - th) / 2f, width - ss * 5, th);
            }
        }
        y += ss + Ce(16 * k);

        // ---- the community: three small tiles (average with a star, votes, share of dashboards) ----
        var sum = _d.Settings.StatsEnabled ? Stats.Current : null;
        var st = sum != null && sum.Widgets.TryGetValue(_def.TypeId, out var w) ? w : null;
        if (st == null)
        {
            string line = Lang.T("dash_no_data");
            float lh = H(line, lineF, width);
            Text(line, lineF, Muted, x, y, width, lh);
            return y + Ce(lh) - y0;
        }
        using var valF = Dpi.Font("Segoe UI Semibold", 18f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        bool hasVotes = st.Rating is { } && st.Votes > 0;
        var tiles = new List<(string value, string cap, bool star)>();
        if (hasVotes)
        {
            tiles.Add((st.Rating!.Value.ToString("0.0", Lang.Culture), Lang.T("dash_card_avg"), true));
            tiles.Add((st.Votes.ToString("N0", Lang.Culture), Lang.T("dash_card_votes"), false));
        }
        tiles.Add((((int)Math.Round(st.Share * 100)).ToString(Lang.Culture) + " %", Lang.T("dash_card_share"), false));
        int gap = Ce(12 * k), tp = Ce(14 * k);
        int tw = (width - gap * (tiles.Count - 1)) / tiles.Count;
        int starPx = Ce(20 * k);
        int tileH = tp + Ce(valF.GetHeight(g)) + Ce(3 * k) + Ce(capF.GetHeight(g)) + tp;
        for (int i = 0; i < tiles.Count; i++)
        {
            var (value, caption, star) = tiles[i];
            var r = new Rectangle(x + i * (tw + gap), y, tw, tileH);
            if (!paint) continue;
            using (var path = RoundPath(r, Ce(8 * k)))
            using (var bg = new SolidBrush(FieldBg))
                g.FillPath(bg, path);
            float vx = r.X + tp;
            if (star)
            {
                // the star shape of the rating rows, in the accent, on the value's middle line
                DashRatings.PaintStars(g, new Point((int)vx, r.Y + tp + (Ce(valF.GetHeight(g)) - starPx) / 2), starPx, 1, Cyan, Color.Transparent, singleStar: true);
                vx += starPx + Ce(6 * k);
            }
            Text(value, valF, White, vx, r.Y + tp, r.Right - (int)vx - tp, valF.GetHeight(g));
            Text(caption, capF, Muted, r.X + tp, r.Y + tp + Ce(valF.GetHeight(g)) + Ce(3 * k), tw - tp * 2, capF.GetHeight(g));
        }
        y += tileH;
        return y - y0;
    }

    protected override int ContentHit(Point p)
    {
        for (int i = 0; i < 5; i++) if (_stars[i].Contains(p)) return i;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        if (zone < 0 || zone > 4) return;
        DashRatings.Rate(_d, _def.TypeId, zone + 1, this, () => { _changed(); Rerender(); });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview?.Dispose();
        base.Dispose(disposing);
    }
}
