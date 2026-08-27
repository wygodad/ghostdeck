using System.Runtime.InteropServices;
using System.Text;

namespace GhostDeck;

/// <summary>
/// Windows power-plan control for the "Windows power" card (discussion #141; roadmap #109 + #36):
/// the processor turbo-boost setting (PERFBOOSTMODE) of a power plan, and the Windows power
/// mode (the "Power mode" slider in Settings). Everything here is user-mode powrprof API;
/// nothing touches the EC, so it works on machines the EC side does not support.
///
/// Two properties shape the whole design. First, a power-plan write is PERSISTENT until
/// something changes it back - unlike the app's volatile EC writes - so turbo is only ever
/// disabled after snapshotting the previous AC/DC values PER PLAN GUID (writing plan A's old
/// values into plan B would be wrong), and a snapshot is taken only on a non-zero -> 0
/// transition so an original can never be overwritten with zeros. Second, the newer exports
/// (user-configured power mode, effective-mode notifications) exist only on Windows 11, so
/// they are resolved dynamically and every caller degrades gracefully when they are absent.
/// </summary>
public static class PowerPlan
{
    // ---------------- GUIDs ----------------
    private static Guid _subProcessor = new("54533251-82be-4824-96c1-47b60b740d00");   // SUB_PROCESSOR
    private static Guid _perfBoost = new("be337238-0d82-4146-a960-4f3749d470c7");      // PERFBOOSTMODE

    // The three documented user-configured power modes (PowerSetUserConfiguredACPowerMode).
    public static readonly Guid ModeBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid ModeBalanced = Guid.Empty;   // GUID_POWER_MODE_NONE
    public static readonly Guid ModeBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    private const uint AttribHide = 1;   // POWER_ATTRIBUTE_HIDE

    // ---------------- documented imports (present since Windows 7) ----------------
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadPossibleValue(IntPtr root, ref Guid sub, ref Guid setting, ref uint type, uint index, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadPossibleFriendlyName(IntPtr root, ref Guid sub, ref Guid setting, uint index, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadSettingAttributes(ref Guid sub, ref Guid setting);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteSettingAttributes(ref Guid sub, ref Guid setting, uint attributes);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    // ---------------- Windows 11 exports, resolved dynamically ----------------
    private delegate uint SetModeFn(ref Guid mode);
    private delegate uint GetModeFn(out Guid mode);
    private delegate uint OverlayFn(Guid mode);
    private delegate void EffectiveCb(int mode, IntPtr ctx);
    private delegate int RegisterEffectiveFn(uint version, EffectiveCb callback, IntPtr ctx, out IntPtr handle);

    private static readonly SetModeFn? _setAcMode = Resolve<SetModeFn>("PowerSetUserConfiguredACPowerMode");
    private static readonly SetModeFn? _setDcMode = Resolve<SetModeFn>("PowerSetUserConfiguredDCPowerMode");
    private static readonly GetModeFn? _getAcMode = Resolve<GetModeFn>("PowerGetUserConfiguredACPowerMode");
    private static readonly GetModeFn? _getDcMode = Resolve<GetModeFn>("PowerGetUserConfiguredDCPowerMode");
    private static readonly OverlayFn? _setOverlay = Resolve<OverlayFn>("PowerSetActiveOverlayScheme");
    private static readonly RegisterEffectiveFn? _registerEffective = Resolve<RegisterEffectiveFn>("PowerRegisterForEffectivePowerModeNotifications");

    private static T? Resolve<T>(string name) where T : class
    {
        try
        {
            if (!NativeLibrary.TryLoad("powrprof.dll", out var lib)) return null;
            return NativeLibrary.TryGetExport(lib, name, out var p)
                ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
        }
        catch { return null; }
    }

    /// <summary>The official user-configured power-mode API (Windows 11).</summary>
    public static bool UserModeApiPresent => _setAcMode != null && _setDcMode != null;

    // ---------------- schemes ----------------

    public static bool TryGetActiveScheme(out Guid scheme)
    {
        scheme = Guid.Empty;
        IntPtr p = IntPtr.Zero;
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0 || p == IntPtr.Zero) return false;
            scheme = Marshal.PtrToStructure<Guid>(p);
            return true;
        }
        catch { return false; }
        finally { if (p != IntPtr.Zero) LocalFree(p); }
    }

    /// <summary>Localized plan name from Windows; "" when unavailable.</summary>
    public static string SchemeName(Guid scheme)
    {
        try
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size == 0 || size > 4096) return "";
            var buf = new byte[size];
            if (PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) != 0) return "";
            return Encoding.Unicode.GetString(buf).TrimEnd('\0');
        }
        catch { return ""; }
    }

    /// <summary>A plan the boost setting can be read from still exists (deleted custom plans fail here).</summary>
    public static bool SchemeExists(Guid scheme) => TryReadBoost(scheme, out _, out _);

    // ---------------- turbo boost (PERFBOOSTMODE) ----------------

    public static bool TryReadBoost(Guid scheme, out uint ac, out uint dc)
    {
        ac = dc = 0;
        try
        {
            return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoost, out ac) == 0
                && PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoost, out dc) == 0;
        }
        catch { return false; }
    }

    /// <summary>Writes both values to the given plan; refreshes only when that plan is active.</summary>
    public static bool WriteBoost(Guid scheme, uint ac, uint dc)
    {
        try
        {
            if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoost, ac) != 0) return false;
            if (PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref _subProcessor, ref _perfBoost, dc) != 0) return false;
            // The documented apply step - but only for the plan that is actually active. Restoring
            // values into other (inactive) plans must NEVER switch the machine onto them.
            if (TryGetActiveScheme(out var active) && active == scheme)
                PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            return true;
        }
        catch { return false; }
    }

    public sealed record BoostMode(uint Value, string Name);

    /// <summary>
    /// The boost modes this Windows actually offers, enumerated dynamically (no hardcoded
    /// 0..6 - the list differs between builds). Names come localized from Windows itself.
    /// </summary>
    public static List<BoostMode> BoostModes()
    {
        var list = new List<BoostMode>();
        try
        {
            for (uint i = 0; i < 16; i++)
            {
                uint type = 0, vSize = 8;
                var vBuf = new byte[8];
                if (PowerReadPossibleValue(IntPtr.Zero, ref _subProcessor, ref _perfBoost, ref type, i, vBuf, ref vSize) != 0)
                    break;
                uint val = BitConverter.ToUInt32(vBuf, 0);
                string name = val.ToString();
                uint nSize = 0;
                PowerReadPossibleFriendlyName(IntPtr.Zero, ref _subProcessor, ref _perfBoost, i, null, ref nSize);
                if (nSize is > 0 and <= 4096)
                {
                    var nBuf = new byte[nSize];
                    if (PowerReadPossibleFriendlyName(IntPtr.Zero, ref _subProcessor, ref _perfBoost, i, nBuf, ref nSize) == 0)
                        name = Encoding.Unicode.GetString(nBuf).TrimEnd('\0');
                }
                list.Add(new BoostMode(val, name));
            }
        }
        catch { }
        return list;
    }

    public static string BoostName(uint value) =>
        BoostModes().FirstOrDefault(m => m.Value == value)?.Name ?? value.ToString();

    /// <summary>
    /// The app's own fallback for "turn turbo on with no snapshot", validated against the
    /// enumeration: Aggressive (2) if this Windows offers it, else Enabled (1), else the
    /// first non-zero mode. Null = nothing sensible exists; write nothing.
    /// This is GHOSTDECK's default, not "the system default" - OEMs ship different ones.
    /// </summary>
    public static BoostMode? FallbackBoost()
    {
        var modes = BoostModes();
        return modes.FirstOrDefault(m => m.Value == 2)
            ?? modes.FirstOrDefault(m => m.Value == 1)
            ?? modes.FirstOrDefault(m => m.Value != 0);
    }

    // ---------------- turbo operations shared by the Settings card and the CLI ----------------
    // Return "" on success or an English machine-readable reason (CLI output stays English by
    // design; the card wraps failures in its own localized text).

    private static string Key(Guid scheme) => scheme.ToString("D").ToLowerInvariant();

    public static string TurboOff(AppSettings s)
    {
        if (!TryGetActiveScheme(out var scheme)) return "cannot read the active power plan";
        if (!TryReadBoost(scheme, out uint ac, out uint dc)) return "cannot read the boost setting";
        if (ac == 0 && dc == 0) return "";   // already off; nothing to snapshot (never snapshot zeros)
        // Snapshot BEFORE the write, per plan GUID, only on a non-zero -> 0 transition.
        s.TurboSnapshots[Key(scheme)] = new[] { (int)ac, (int)dc };
        s.Save();
        return WriteBoost(scheme, 0, 0) ? "" : "Windows refused the boost write";
    }

    public static string TurboOn(AppSettings s)
    {
        if (!TryGetActiveScheme(out var scheme)) return "cannot read the active power plan";
        uint ac, dc;
        if (s.TurboSnapshots.TryGetValue(Key(scheme), out var snap) && snap is { Length: 2 })
        {
            ac = (uint)snap[0]; dc = (uint)snap[1];
        }
        else if (FallbackBoost() is { } fb) { ac = dc = fb.Value; }
        else return "this Windows enumerates no usable boost mode";
        if (!WriteBoost(scheme, ac, dc)) return "Windows refused the boost write";
        s.TurboSnapshots.Remove(Key(scheme));   // consumed only after a successful write
        s.Save();
        return "";
    }

    public static string TurboStatus()
    {
        if (!TryGetActiveScheme(out var scheme) || !TryReadBoost(scheme, out uint ac, out uint dc))
            return "boost state unavailable";
        string state = ac == 0 && dc == 0 ? "off" : ac != 0 && dc != 0 ? "on" : "mixed";
        return $"turbo boost: {state} (AC {BoostName(ac)}, battery {BoostName(dc)})";
    }

    /// <summary>
    /// "Restore Windows settings": puts every snapshotted plan back and reports what happened.
    /// Never activates another plan - values are written per GUID and the active plan is
    /// refreshed once at the end. A deleted plan drops its stale snapshot; a failed write
    /// keeps its snapshot so the user can retry.
    /// </summary>
    public static (int restored, int missing, int failed) RestoreAll(AppSettings s)
    {
        int restored = 0, missing = 0, failed = 0;
        foreach (var (key, snap) in s.TurboSnapshots.ToArray())
        {
            if (snap is not { Length: 2 } || !Guid.TryParse(key, out var scheme)) { s.TurboSnapshots.Remove(key); continue; }
            if (!SchemeExists(scheme)) { s.TurboSnapshots.Remove(key); missing++; continue; }
            if (WriteBoost(scheme, (uint)snap[0], (uint)snap[1])) { s.TurboSnapshots.Remove(key); restored++; }
            else failed++;
        }
        s.Save();
        return (restored, missing, failed);
    }

    // ---------------- the hidden-in-Control-Panel attribute ----------------

    public static uint? ReadBoostAttributes()
    {
        try { return PowerReadSettingAttributes(ref _subProcessor, ref _perfBoost); }
        catch { return null; }
    }

    public static bool HiddenInControlPanel() => (ReadBoostAttributes() ?? AttribHide) % 2 == 1;

    /// <summary>
    /// Reveal/re-hide PERFBOOSTMODE in the Windows power options UI. Only the HIDE bit is
    /// touched: revealing stores the full original DWORD in settings and clears bit 0;
    /// re-hiding writes the exact original back, so no other attribute bit is ever lost.
    /// </summary>
    public static bool SetRevealed(AppSettings s, bool reveal)
    {
        var attrs = ReadBoostAttributes();
        if (attrs == null) return false;
        try
        {
            if (reveal)
            {
                if (s.BoostAttrOriginal < 0) { s.BoostAttrOriginal = attrs.Value; s.Save(); }
                return PowerWriteSettingAttributes(ref _subProcessor, ref _perfBoost, attrs.Value & ~AttribHide) == 0;
            }
            uint back = s.BoostAttrOriginal >= 0 ? (uint)s.BoostAttrOriginal : attrs.Value | AttribHide;
            bool ok = PowerWriteSettingAttributes(ref _subProcessor, ref _perfBoost, back) == 0;
            if (ok) { s.BoostAttrOriginal = -1; s.Save(); }
            return ok;
        }
        catch { return false; }
    }

    // ---------------- Windows power mode (#36) ----------------

    /// <summary>Sets the user-configured power mode for BOTH power sources (the "vote";
    /// Windows may temporarily override it - see the effective watch below).</summary>
    public static bool TrySetPowerMode(Guid mode)
    {
        try
        {
            if (_setAcMode != null && _setDcMode != null)
            {
                var m = mode;
                return _setAcMode(ref m) == 0 & _setDcMode(ref m) == 0;
            }
            // Pre-Win11 fallback: the (undocumented, long-lived) overlay call.
            return _setOverlay != null && _setOverlay(mode) == 0;
        }
        catch { return false; }
    }

    public static bool TryGetUserPowerMode(out Guid ac, out Guid dc)
    {
        ac = dc = Guid.Empty;
        try { return _getAcMode != null && _getDcMode != null && _getAcMode(out ac) == 0 && _getDcMode(out dc) == 0; }
        catch { return false; }
    }

    public static Guid ModeForProfile(ProfileId id) => id switch
    {
        ProfileId.Silent or ProfileId.SuperBattery => ModeBestEfficiency,
        ProfileId.Extreme => ModeBestPerformance,
        _ => ModeBalanced,
    };

    /// <summary>Lang key for one of the three requested modes.</summary>
    public static string ModeKey(Guid mode) =>
        mode == ModeBestEfficiency ? "pwm_req_eff" : mode == ModeBestPerformance ? "pwm_req_perf" : "pwm_req_bal";

    // ---------------- effective power mode (documented callback, Win10 1809+) ----------------

    /// <summary>-1 until the first callback (or when the API is missing).</summary>
    public static int EffectiveMode { get; private set; } = -1;
    public static event Action? EffectiveModeChanged;

    private static EffectiveCb? _effectiveCb;   // rooted so the GC never collects the thunk
    private static bool _watchStarted;

    public static void EnsureEffectiveWatch()
    {
        if (_watchStarted || _registerEffective == null) return;
        _watchStarted = true;
        try
        {
            _effectiveCb = (mode, _) =>
            {
                EffectiveMode = mode;
                try { EffectiveModeChanged?.Invoke(); } catch { }
            };
            // EFFECTIVE_POWER_MODE_V2 = 2; the callback also fires once right away with the
            // current state. The registration lives for the whole process - no unregister.
            _registerEffective(2, _effectiveCb, IntPtr.Zero, out _);
        }
        catch { }
    }

    /// <summary>Lang key for an EFFECTIVE_POWER_MODE value; "" when unknown.
    /// NOT the same value space as the three requested modes - never compare 1:1.</summary>
    public static string EffectiveKey(int mode) => mode switch
    {
        0 => "pwm_eff_saver", 1 => "pwm_eff_better", 2 => "pwm_eff_bal", 3 => "pwm_eff_high",
        4 => "pwm_eff_max", 5 => "pwm_eff_game", 6 => "pwm_eff_mr", _ => "",
    };
}
