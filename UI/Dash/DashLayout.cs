namespace GhostDeck;

/// <summary>A layout item placed on the grid: its cell rectangle (X = column, Y = row, in cells) and the widget that paints it.</summary>
internal sealed record DashPlaced(int Index, DashItem Item, IDashWidget Widget, Rectangle Cells);

/// <summary>
/// Turns the ordered list of items into positions on a grid of a given width. Placement is
/// the CSS "sparse" auto-flow: a cursor walks the grid row by row and every item takes the
/// first free spot at or after the cursor, so items stay in their order on screen and a later
/// item never jumps back into a hole before an earlier one. The default layout fills three
/// rows of six columns exactly; in a narrower grid the same list wraps further down.
/// </summary>
internal static class DashLayout
{
    /// <summary>The items to show: the saved layout, or the default one for a settings object without a layout yet (the preview tool).</summary>
    public static List<DashItem> ItemsOf(AppSettings s) => s.DashboardVersion < 1 ? DashItem.DefaultLayout() : s.Dashboard;

    public static List<DashPlaced> Place(IReadOnlyList<DashItem> items, IReadOnlyList<IDashWidget> widgets, int cols)
    {
        var placed = new List<DashPlaced>(items.Count);
        var rows = new List<bool[]>();
        int curRow = 0, curCol = 0;
        bool[] Row(int r) { while (rows.Count <= r) rows.Add(new bool[cols]); return rows[r]; }
        bool Free(int r, int c, int w, int h)
        {
            for (int y = r; y < r + h; y++)
            {
                var row = Row(y);
                for (int x = c; x < c + w; x++) if (row[x]) return false;
            }
            return true;
        }
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int w = Math.Clamp(it.W, 1, cols), h = Math.Max(1, it.H);
            int r = curRow, c = curCol;
            while (true)
            {
                if (c + w > cols) { r++; c = 0; continue; }
                if (Free(r, c, w, h)) break;
                c++;
            }
            for (int y = r; y < r + h; y++) { var row = Row(y); for (int x = c; x < c + w; x++) row[x] = true; }
            placed.Add(new DashPlaced(i, it, widgets[i], new Rectangle(c, r, w, h)));
            curRow = r; curCol = c + w;
            if (curCol >= cols) { curRow++; curCol = 0; }
        }
        return placed;
    }

    /// <summary>Number of grid rows the placement uses.</summary>
    public static int Rows(IReadOnlyList<DashPlaced> placed)
    {
        int rows = 0;
        foreach (var p in placed) rows = Math.Max(rows, p.Cells.Bottom);
        return rows;
    }
}
