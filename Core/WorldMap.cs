using System.Drawing;
using System.Globalization;
using System.Reflection;

namespace GhostDeck;

/// <summary>
/// The dot map of the Statystyki view: a 2 degree grid of land cells, each tagged with its
/// country code, built once from Natural Earth's 110m countries by DOC/tools/worldmap-dots.py
/// and embedded as GhostDeck.worldmap.txt (about 20 KB). Nothing is downloaded and no library
/// is involved: the view paints one dot per cell, brighter where the summary counts installs.
/// </summary>
public sealed class WorldMap
{
    public static WorldMap Instance { get; } = Load();

    public int Cols { get; }
    public int Rows { get; }
    private readonly string?[,] _cells;

    private WorldMap(int cols, int rows, string?[,] cells) { Cols = cols; Rows = rows; _cells = cells; }

    private static WorldMap Load()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("GhostDeck.worldmap.txt");
            if (s == null) return new WorldMap(0, 0, new string?[0, 0]);
            using var rd = new StreamReader(s);
            string? first;
            while ((first = rd.ReadLine()) != null && first.StartsWith('#')) { }   // the source and licence lines
            var head = (first ?? "").Split(' ');
            int cols = int.Parse(head[3], CultureInfo.InvariantCulture), rows = int.Parse(head[4], CultureInfo.InvariantCulture);
            var cells = new string?[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                string line = rd.ReadLine() ?? "";
                for (int c = 0; c < cols && c * 2 + 1 < line.Length; c++)
                {
                    string code = line.Substring(c * 2, 2);
                    if (code != "  ") cells[r, c] = string.Intern(code);
                }
            }
            return new WorldMap(cols, rows, cells);
        }
        catch { return new WorldMap(0, 0, new string?[0, 0]); }
    }

    /// <summary>Cell size and origin that fit the whole grid into <paramref name="r"/>, centred.</summary>
    private (float cell, float ox, float oy) Fit(Rectangle r)
    {
        if (Cols == 0 || Rows == 0) return (0, r.X, r.Y);
        float cell = Math.Min(r.Width / (float)Cols, r.Height / (float)Rows);
        return (cell, r.X + (r.Width - cell * Cols) / 2f, r.Y + (r.Height - cell * Rows) / 2f);
    }

    /// <summary>
    /// Paints the dots. Countries in <paramref name="counts"/> get a larger dot in the accent,
    /// brighter with the count (logarithmic, so one big country does not black out the rest);
    /// <paramref name="highlight"/> is drawn white.
    /// </summary>
    public void Paint(Graphics g, Rectangle r, IReadOnlyDictionary<string, int> counts, Color land, Color accent, string? highlight, float lit = 1f, bool landDots = true)
    {
        var (cell, ox, oy) = Fit(r);
        if (cell <= 0) return;
        // lit 0..1 fades and grows the counted countries' dots in (the Statystyki entrance);
        // landDots false paints only those, over a map whose land is already there
        float dLand = cell * 0.58f, dHot = dLand + (cell * 0.8f - dLand) * lit;
        double maxLog = counts.Count > 0 ? Math.Log(1 + counts.Values.Max()) : 1;
        using var landB = new SolidBrush(land);
        using var white = new SolidBrush(Color.White);
        for (int row = 0; row < Rows; row++)
            for (int c = 0; c < Cols; c++)
            {
                var code = _cells[row, c];
                if (code == null) continue;
                float cx = ox + c * cell + cell / 2f, cy = oy + row * cell + cell / 2f;
                if (code == highlight) { g.FillEllipse(white, cx - dHot / 2, cy - dHot / 2, dHot, dHot); continue; }
                if (counts.TryGetValue(code, out int n) && n > 0 && lit > 0f)
                {
                    float t = (float)(Math.Log(1 + n) / maxLog);
                    using var b = new SolidBrush(Color.FromArgb((int)((120 + 135 * t) * lit), accent));
                    g.FillEllipse(b, cx - dHot / 2, cy - dHot / 2, dHot, dHot);
                }
                else if (landDots) g.FillEllipse(landB, cx - dLand / 2, cy - dLand / 2, dLand, dLand);
            }
    }

    /// <summary>The country code under a point, or null over sea or outside the grid.</summary>
    public string? Hit(Rectangle r, Point p)
    {
        var (cell, ox, oy) = Fit(r);
        if (cell <= 0) return null;
        int c = (int)Math.Floor((p.X - ox) / cell), row = (int)Math.Floor((p.Y - oy) / cell);
        if (c < 0 || row < 0 || c >= Cols || row >= Rows) return null;
        return _cells[row, c];
    }

    /// <summary>The country's name in the app's language (the runtime's region data), or the code when unknown.</summary>
    public static string CountryName(string code)
    {
        try
        {
            var prev = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = Lang.Culture;
            try { return new RegionInfo(code).DisplayName; }
            finally { CultureInfo.CurrentUICulture = prev; }
        }
        catch { return code; }
    }

    /// <summary>A language's own name ("polski", "Deutsch") for a report language code, or the code.</summary>
    public static string LanguageName(string code)
    {
        try { var ci = CultureInfo.GetCultureInfo(code); return ci.NativeName.Length > 0 ? char.ToUpper(ci.NativeName[0], ci) + ci.NativeName[1..] : code; }
        catch { return code; }
    }
}
