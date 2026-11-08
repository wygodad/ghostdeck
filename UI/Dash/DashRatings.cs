namespace GhostDeck;

/// <summary>
/// Widget ratings, shared by every place that shows stars (the catalogue panel, the Katalog
/// view, the widget card): your rating lives in <see cref="AppSettings.WidgetRatings"/> and goes
/// out with the weekly report; the average and the votes come from the weekly summary. With
/// statistics off a click on a star opens a card that explains this and offers to turn them on.
/// </summary>
internal static class DashRatings
{
    /// <summary>Your rating of a widget type, 1..5, or 0 when you have not rated it.</summary>
    public static int Own(MainDeps d, string type) => d.Settings.WidgetRatings.TryGetValue(type, out int r) ? r : 0;

    /// <summary>
    /// Sets the rating; the same star again clears it. With statistics off the card
    /// "[Turn on and rate] / [Cancel]" comes first. <paramref name="onDone"/> runs after a change.
    /// </summary>
    public static void Rate(MainDeps d, string type, int stars, Form? owner, Action onDone)
    {
        var s = d.Settings;
        if (!s.StatsEnabled)
        {
            var dlg = new GhostCardForm("//STATS", Lang.T("dash_rate_title"), Lang.T("dash_rate_body"), Lang.T("dash_rate_on"), Lang.T("gen_cancel"), () =>
            {
                s.StatsEnabled = true;
                s.StatsAsked = true;
                s.WidgetRatings[type] = stars;
                try { d.SaveSettings(); } catch { }
                d.StatsChanged?.Invoke();
                onDone();
            });
            dlg.ShowOver(owner);
            return;
        }
        if (s.WidgetRatings.TryGetValue(type, out int cur) && cur == stars) s.WidgetRatings.Remove(type);
        else s.WidgetRatings[type] = stars;
        try { d.SaveSettings(); } catch { }
        onDone();
    }

    /// <summary>Five stars of <paramref name="size"/> px from the left of <paramref name="at"/>; the first <paramref name="filled"/> in <paramref name="fill"/>.</summary>
    public static void PaintStars(Graphics g, Point at, int size, int filled, Color fill, Color empty, Color? clearWith = null)
    {
        using var starF = Dpi.Font("Segoe UI Symbol", size * 0.86f, FontStyle.Regular, GraphicsUnit.Pixel);
        if (clearWith is { } bg) { using var b = new SolidBrush(bg); g.FillRectangle(b, new Rectangle(at.X, at.Y, size * 5, size)); }
        for (int k = 1; k <= 5; k++)
            TextRenderer.DrawText(g, "★", starF, new Rectangle(at.X + (k - 1) * size, at.Y, size, size), k <= filled ? fill : empty,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping);
    }

    /// <summary>The rectangle of star <paramref name="k"/> (1..5) in a row painted at <paramref name="at"/>.</summary>
    public static Rectangle StarRect(Point at, int size, int k) => new(at.X + (k - 1) * size, at.Y, size, size);

    /// <summary>"4.7 (61)" from the summary, or "" without votes.</summary>
    public static string Votes(Stats.WidgetStat? st) =>
        st?.Rating is { } av && st.Votes > 0 ? av.ToString("0.0", Lang.Culture) + " (" + st.Votes.ToString(Lang.Culture) + ")" : "";
}
