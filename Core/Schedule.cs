namespace GhostDeck;

/// <summary>
/// One scene-schedule rule: on the picked weekdays, between Start and End, the given scene
/// is applied when the window BEGINS (edge-triggered - the engine acts on transitions only,
/// so a manual change inside a window is respected). Overnight ranges are fine; the day is
/// matched against the window's START day (Fri 22:00-07:00 = Friday night into Saturday).
/// When windows overlap, the first matching rule in list order wins.
/// </summary>
public sealed class ScheduleRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; set; } = true;
    public string SceneId { get; set; } = "";
    public int Days { get; set; } = 0x1F;          // bit 0 = Monday ... bit 6 = Sunday
    public string Start { get; set; } = "08:00";   // HH:mm
    public string End { get; set; } = "16:00";

    public ScheduleRule Clone() => (ScheduleRule)MemberwiseClone();

    public static int MinutesOf(string t) =>
        TimeSpan.TryParse(t, out var ts) && ts.TotalMinutes is >= 0 and < 1440 ? (int)ts.TotalMinutes : -1;

    /// <summary>Whether the window containing <paramref name="now"/> is active for this rule.</summary>
    public bool ActiveAt(DateTime now)
    {
        int s = MinutesOf(Start), e = MinutesOf(End);
        if (s < 0 || e < 0 || s == e) return false;
        int t = now.Hour * 60 + now.Minute;
        if (s < e) return DayOn(now.DayOfWeek) && t >= s && t < e;
        // overnight: the active window started yesterday when we are before End
        return (DayOn(now.DayOfWeek) && t >= s) || (DayOn(now.AddDays(-1).DayOfWeek) && t < e);
    }

    // DayOfWeek has Sunday = 0; our bits start at Monday, so rotate by six.
    public bool DayOn(DayOfWeek d) => (Days & (1 << ((int)d + 6) % 7)) != 0;
}
