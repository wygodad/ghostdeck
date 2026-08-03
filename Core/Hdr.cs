using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// HDR (advanced color) toggle via the DisplayConfig API - the same switch as Windows
/// Settings → Display → HDR. Enumerates the active video paths; Supported = at least one
/// path reports advanced-color capability; Set flips every capable path (scene semantics:
/// one machine state, not a per-monitor picker). Pure user-mode Windows API, no EC, so it
/// works on any laptop; unsupported machines simply never show the scene row.
/// </summary>
public static class Hdr
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_ADVANCED_COLOR = 9;    // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO
    private const int SET_ADVANCED_COLOR = 10;   // DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathSource { public LUID AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rational { public uint Numerator, Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathTarget
    {
        public LUID AdapterId; public uint Id; public uint ModeInfoIdx;
        public uint OutputTechnology, Rotation, Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering; public int TargetAvailable; public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }   // 72 bytes

    // Header (16) + the biggest union member, DISPLAYCONFIG_TARGET_MODE's video signal (48).
    [StructLayout(LayoutKind.Sequential)]
    private struct ModeInfo { public uint InfoType; public uint Id; public LUID AdapterId; public ulong U0, U1, U2, U3, U4, U5; }   // 64 bytes

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoHeader { public int Type; public int Size; public LUID AdapterId; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GetAdvancedColor
    {
        public DeviceInfoHeader Header;
        public uint Value;                 // bit0 = supported, bit1 = enabled
        public uint ColorEncoding;
        public int BitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SetAdvancedColor { public DeviceInfoHeader Header; public uint EnableAdvancedColor; }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths, ref uint numModes, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref GetAdvancedColor info);
    [DllImport("user32.dll")] private static extern int DisplayConfigSetDeviceInfo(ref SetAdvancedColor info);

    private static List<(LUID Adapter, uint Id, bool Supported, bool Enabled)> Paths()
    {
        var list = new List<(LUID, uint, bool, bool)>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0 || np == 0) return list;
            var paths = new PathInfo[np];
            var modes = new ModeInfo[Math.Max(1u, nm)];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;
            for (int i = 0; i < np; i++)
            {
                var q = new GetAdvancedColor
                {
                    Header = new DeviceInfoHeader
                    {
                        Type = GET_ADVANCED_COLOR,
                        Size = Marshal.SizeOf<GetAdvancedColor>(),
                        AdapterId = paths[i].Target.AdapterId,
                        Id = paths[i].Target.Id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref q) != 0) continue;
                list.Add((q.Header.AdapterId, q.Header.Id, (q.Value & 1) != 0, (q.Value & 2) != 0));
            }
        }
        catch { }
        return list;
    }

    // MSIPS_FORCE_HDR=1: pretend an HDR-capable display exists (UI testing / screenshots on
    // panels without HDR, same idea as MSIPS_FORCE_FIRMWARE). Set() then only tracks state.
    private static readonly bool Sim =
        Environment.GetEnvironmentVariable("MSIPS_FORCE_HDR") is "1" or "on" or "true";
    private static bool _simState;

    public static bool Supported() { if (Sim) return true; try { return Paths().Any(p => p.Supported); } catch { return false; } }
    public static bool Enabled() { if (Sim) return _simState; try { return Paths().Any(p => p.Supported && p.Enabled); } catch { return false; } }

    /// <summary>Flip HDR on every capable display. False = no display accepted the change.</summary>
    public static bool Set(bool on)
    {
        if (Sim) { _simState = on; return true; }
        bool any = false;
        foreach (var p in Paths().Where(p => p.Supported))
        {
            var s = new SetAdvancedColor
            {
                Header = new DeviceInfoHeader
                {
                    Type = SET_ADVANCED_COLOR,
                    Size = Marshal.SizeOf<SetAdvancedColor>(),
                    AdapterId = p.Adapter,
                    Id = p.Id,
                },
                EnableAdvancedColor = on ? 1u : 0u,
            };
            if (DisplayConfigSetDeviceInfo(ref s) == 0) any = true;
        }
        return any;
    }
}
