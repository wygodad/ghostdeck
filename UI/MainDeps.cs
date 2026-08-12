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
    // A getter, not a captured string: the startup probe may fill the firmware in a few
    // seconds late (transient-WMI retry), and pages re-detect via this on OnDeviceDbChanged.
    public required Func<string> Firmware { get; init; }
    public required Func<string> AppVersion { get; init; }
    public required Action SaveSettings { get; init; }
    public required Action CheckNoticesNow { get; init; }   // manual "Check now" also surfaces announcements
    // Model database, two entry points. The button forces a fetch and reports back: >0 = applied
    // that version, 0 = already current, -1 = failed, -2 = valid but deferred (the curve editor
    // or an EC write is busy). The Models tab only nudges, and is debounced.
    public required Action<Action<int>> CheckModelDbNow { get; init; }
    public required Action PollModelDb { get; init; }
    public required Action SettingsChanged { get; init; }     // tray rebuilds menu / hotkeys
    public required Action StartReportWizard { get; init; }    // interim: report wizard dialog
    public required Action<int> SetChargeLimit { get; init; }  // 0 = off, else 60/80/100
    public required Action<int> SetTravelDays { get; init; }   // >0 = charge to 100% until today+N; 0 = end now (previous limit returns)
    public required Action<bool> SetAutoSwitch { get; init; }
    public required Func<bool> CoolerBoost { get; init; }          // current Cooler Boost (max fans) state
    public required Action<bool> SetCoolerBoost { get; init; }     // turn Cooler Boost on/off (gated on writable)
    public required Func<int> KbdLevel { get; init; }              // (#26) backlight level 0-3, -1 = no support
    public required Action<int> SetKbdLevel { get; init; }
    public required Func<int> WebcamState { get; init; }           // (#27) 1 = on, 0 = off, -1 = no support
    public required Action<bool> SetWebcam { get; init; }
    public required Func<bool> WebcamBlocked { get; init; }        // (#27) hard block (0x2F) active
    public required Action<bool> SetWebcamBlock { get; init; }
    public required Func<int> FnLeft { get; init; }                // Fn key side: 1 = left, 0 = right, -1 = no support
    public required Action<bool> SetFnLeft { get; init; }
    public required Func<bool> WinLockOn { get; init; }            // software Windows-key lock (hook)
    public required Action<bool> SetWinLock { get; init; }
    public required Func<int> TouchpadState { get; init; }         // 1 = on, 0 = disabled, -1 = none
    public required Action<bool> SetTouchpad { get; init; }
    public required Action OpenScenSettings { get; init; }         // gear on Scenarios -> Settings visibility card (flashed)
    public required Action<SceneDef> RunScene { get; init; }       // (#21) apply a scene now
    public required Func<bool> HasFanCurve { get; init; }          // model exposes editable fan-curve tables
    public required Action PanicReset { get; init; }               // one press back to a safe stock state
    public required Action<Action<DeviceProfile>> WithEcWrite { get; init; }  // runs only if writable + not simulating
    public required Func<bool> Simulating { get; init; }        // MSIPS_FORCE_FIRMWARE preview: EC writes are skipped
    // Holds the model-database swap gate for a long composed EC operation (the power test). Create
    // and dispose it on the UI thread - it is the same counter the tray's own write paths use.
    public required Func<IDisposable> EcSession { get; init; }
    // Re-assert the fan curve the settings record as live. Anything that rewrites the fan-mode byte
    // (a profile recipe) drops it, and a curve applied from the editor has no preset to fall back on.
    public required Action RestoreActiveCurve { get; init; }
    public required Func<bool> OverlayOn { get; init; }
    public required Action<bool> SetOverlay { get; init; }
    public required Action ApplyOverlaySettings { get; init; }   // re-read overlay options after a settings edit
    public required Action<int> SnapOverlay { get; init; }       // 0=TL 1=TR 2=BL 3=BR — snap overlay to a screen corner
    public required Action<bool> SetFpsViewer { get; init; }     // Status → Gaming visible: keeps FpsMonitor running
    public required Func<Updater.Result?> UpdateAvail { get; init; }  // newer release found by the daily check (null = none)
    public required Action<string?> OpenUpdates { get; init; }        // jump to the Updates tab; arg = release tag to expand
}
