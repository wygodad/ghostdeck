using System.Drawing.Drawing2D;

namespace GhostDeck;

public enum MainTab { Scenarios, Status, FanCurve, Settings, Models, Report, Updates }

/// <summary>Everything the tabbed UI needs from the tray context (data + actions).</summary>
public sealed class MainDeps
{
    public required AppSettings Settings { get; init; }
    public required Func<StatusInfo> Status { get; init; }
    public required Func<HwSnapshot> Hw { get; init; }
    public required Func<ProfileId> Current { get; init; }
    public required Action<ProfileId> SetProfile { get; init; }
    public required Func<bool> Writable { get; init; }
    public required Func<ProfileId, Color> ColorOf { get; init; }
    public required string Firmware { get; init; }
    public required Func<string> AppVersion { get; init; }
    public required Action SaveSettings { get; init; }
    public required Action CheckNoticesNow { get; init; }   // manual "Check now" also surfaces announcements
    public required Action SettingsChanged { get; init; }     // tray rebuilds menu / hotkeys
    public required Action StartReportWizard { get; init; }    // interim: report wizard dialog
    public required Action<int> SetChargeLimit { get; init; }  // 0 = off, else 60/80/100
    public required Action<bool> SetAutoSwitch { get; init; }
    public required Func<bool> CoolerBoost { get; init; }          // current Cooler Boost (max fans) state
    public required Action<bool> SetCoolerBoost { get; init; }     // turn Cooler Boost on/off (gated on writable)
    public required Action<Action<DeviceProfile>> WithEcWrite { get; init; }  // runs only if writable + not simulating
    public required Func<bool> OverlayOn { get; init; }
    public required Action<bool> SetOverlay { get; init; }
    public required Action ApplyOverlaySettings { get; init; }   // re-read overlay options after a settings edit
    public required Action<int> SnapOverlay { get; init; }       // 0=TL 1=TR 2=BL 3=BR — snap overlay to a screen corner
}
