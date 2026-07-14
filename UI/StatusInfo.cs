namespace GhostDeck;

/// <summary>
/// Snapshot of app/device state for the Status tab. (The old standalone StatusForm
/// window was replaced by <see cref="StatusPage"/> in the tabbed MainForm.)
/// </summary>
public sealed record StatusInfo(
    ProfileId Profile, bool Active, bool Known, string Device,
    string TierText, Color TierColor,
    int Switches, TimeSpan InProfile, bool Autostart, string AppVersion);
