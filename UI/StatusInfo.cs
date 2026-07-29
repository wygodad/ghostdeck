namespace GhostDeck;

/// <summary>
/// Snapshot of app/device state for the Status tab. (The old standalone StatusForm
/// window was replaced by <see cref="StatusPage"/> in the tabbed MainForm.)
/// </summary>
public sealed record StatusInfo(
    ProfileId Profile, bool Active, bool Known, string Device,
    string TierText, Color TierColor,
    int Switches, TimeSpan InProfile, bool Autostart, string AppVersion,
    // Telemetry-only machines (issue #48): no EC interface in firmware, but the vendor WMI
    // data blocks report live CPU/GPU temperature. Temperatures are real, everything the EC
    // would provide (fans, RPM, profiles) is not.
    bool Telemetry = false);
