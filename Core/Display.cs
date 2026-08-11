using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Refresh-rate control for the laptop's built-in panel via user32
/// (EnumDisplaySettings / ChangeDisplaySettingsEx). Pure Windows display API - no EC
/// involvement - so unlike profiles it works on EVERY model, including unrecognised
/// firmware (not gated by Writable). Only the frequency is changed (resolution and colour
/// depth are kept), and only modes the panel actually reports are requested.
/// The target is resolved fresh on every call through DisplayConfig: the active path whose
/// output technology is embedded (internal / eDP / embedded UDI / LVDS) names the panel's
/// GDI device, so the controls stay on the laptop screen even when an external monitor is
/// the primary display (#69). With no active internal panel (lid closed, desktop) they fall
/// back to the primary display. See TECHNICAL.md §28.
/// </summary>
public static class Display
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint CDS_UPDATEREGISTRY = 0x01;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint DM_POSITION = 0x20, DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;                     // display union (POINTL + orientation)
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    private static DEVMODE NewMode() => new() { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

    // ---- built-in panel resolution (DisplayConfig) ------------------------------------
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1;   // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
    private const int GET_TARGET_NAME = 2;   // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
    // DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY values that mean "built into the machine"
    private const uint OT_LVDS = 6, OT_DP_EMBEDDED = 11, OT_UDI_EMBEDDED = 13, OT_INTERNAL = 0x80000000;

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;   // "\\.\DISPLAY1"
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId, EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }

    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PathInfo[] paths, ref uint numModes, [Out] ModeInfo[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceName info);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TargetName info);

    private static string? SourceGdiName(LUID adapter, uint id)
    {
        var src = new SourceName
        {
            Header = new DeviceInfoHeader
            {
                Type = GET_SOURCE_NAME, Size = Marshal.SizeOf<SourceName>(),
                AdapterId = adapter, Id = id,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref src) != 0 || string.IsNullOrEmpty(src.ViewGdiDeviceName)) return null;
        return src.ViewGdiDeviceName;
    }

    private static (string? Name, string? Path) TargetInfo(LUID adapter, uint id)
    {
        var tgt = new TargetName
        {
            Header = new DeviceInfoHeader
            {
                Type = GET_TARGET_NAME, Size = Marshal.SizeOf<TargetName>(),
                AdapterId = adapter, Id = id,
            },
        };
        if (DisplayConfigGetDeviceInfo(ref tgt) != 0) return (null, null);
        return (string.IsNullOrWhiteSpace(tgt.MonitorFriendlyDeviceName) ? null : tgt.MonitorFriendlyDeviceName.Trim(),
                string.IsNullOrWhiteSpace(tgt.MonitorDevicePath) ? null : tgt.MonitorDevicePath.Trim());
    }

    /// <summary>
    /// The display the controls act on. Device = the panel's GDI name ("\\.\DISPLAY1"), or
    /// null in the primary-display fallback (no active internal panel - user32 then targets
    /// the primary itself). Name = EDID name (often empty on laptop panels). Path =
    /// monitorDevicePath, the stable physical-display identity used by the scene guard - in
    /// the fallback it is read off the primary path, recognised by its (0,0) desktop origin.
    /// Resolved fresh every time because dock/undock remaps devices.
    /// </summary>
    private static (string? Device, string? Name, string? Path, bool Internal) Resolve()
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0 || np == 0) return (null, null, null, false);
            var paths = new PathInfo[np];
            var modes = new ModeInfo[Math.Max(1u, nm)];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return (null, null, null, false);
            for (int i = 0; i < np; i++)
            {
                uint ot = paths[i].Target.OutputTechnology;
                if (ot != OT_INTERNAL && ot != OT_DP_EMBEDDED && ot != OT_UDI_EMBEDDED && ot != OT_LVDS) continue;
                if (SourceGdiName(paths[i].Source.AdapterId, paths[i].Source.Id) is not { } gdi) continue;
                var (name, path) = TargetInfo(paths[i].Target.AdapterId, paths[i].Target.Id);
                return (gdi, name, path, true);
            }
            for (int i = 0; i < np; i++)   // no internal panel: identify the primary path instead
            {
                if (SourceGdiName(paths[i].Source.AdapterId, paths[i].Source.Id) is not { } gdi) continue;
                var dm = NewMode();
                if (!EnumDisplaySettings(gdi, ENUM_CURRENT_SETTINGS, ref dm)) continue;
                // the primary sits at the desktop origin; trust the position only when the
                // driver actually filled it in (NewMode zero-inits, so (0,0) alone proves nothing)
                if ((dm.dmFields & DM_POSITION) == 0 || dm.dmPositionX != 0 || dm.dmPositionY != 0) continue;
                var (name, path) = TargetInfo(paths[i].Target.AdapterId, paths[i].Target.Id);
                return (null, name, path, false);
            }
        }
        catch { }
        return (null, null, null, false);
    }

    /// <summary>
    /// Which display the refresh controls act on, for the Settings label:
    /// Internal = an active built-in panel was found (Name = its EDID name when reported);
    /// false = the primary-display fallback is in effect.
    /// </summary>
    public static (bool Internal, string? Name) Target()
    {
        var r = Resolve();
        return (r.Internal, r.Name);
    }

    /// <summary>Stable identity (monitorDevicePath) of the display the controls currently act
    /// on; stored into a scene next to its Hz so the rate never lands on a different display.</summary>
    public static string? TargetPath() => Resolve().Path;

    /// <summary>Current refresh rate of the target display in Hz (0 when unknown).</summary>
    public static int Current()
    {
        try
        {
            var dm = NewMode();
            return EnumDisplaySettings(Resolve().Device, ENUM_CURRENT_SETTINGS, ref dm) ? (int)dm.dmDisplayFrequency : 0;
        }
        catch { return 0; }
    }

    /// <summary>Refresh rates the target display supports at its CURRENT resolution, ascending.</summary>
    public static List<int> SupportedRates()
    {
        try { return Rates(Resolve().Device); } catch { return new List<int>(); }
    }

    private static List<int> Rates(string? dev)
    {
        var rates = new SortedSet<int>();
        var cur = NewMode();
        if (!EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref cur)) return rates.ToList();
        for (int i = 0; ; i++)
        {
            var dm = NewMode();
            if (!EnumDisplaySettings(dev, i, ref dm)) break;
            if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight &&
                dm.dmBitsPerPel == cur.dmBitsPerPel && dm.dmDisplayFrequency > 1)
                rates.Add((int)dm.dmDisplayFrequency);
        }
        return rates.ToList();
    }

    /// <summary>
    /// Switch the target display to the given refresh rate (keeping resolution/depth).
    /// Refuses modes the panel does not report. Returns true when the mode is active.
    /// With <paramref name="expectedPath"/> set (a scene's stored display identity), the
    /// change is also refused when the resolved target is a DIFFERENT physical display -
    /// a scene saved against an external monitor must never retune the panel. An unreadable
    /// identity on either side leaves the guard open (best effort, older scenes keep working).
    /// </summary>
    public static bool SetRefresh(int hz, string? expectedPath = null)
    {
        try
        {
            if (hz <= 0) return false;
            var r = Resolve();   // one resolve for the whole operation
            if (expectedPath != null && r.Path != null &&
                !string.Equals(expectedPath, r.Path, StringComparison.OrdinalIgnoreCase)) return false;
            var dm = NewMode();
            if (!EnumDisplaySettings(r.Device, ENUM_CURRENT_SETTINGS, ref dm)) return false;
            if ((int)dm.dmDisplayFrequency == hz) return true;
            if (!Rates(r.Device).Contains(hz)) return false;
            dm.dmDisplayFrequency = (uint)hz;
            dm.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            return ChangeDisplaySettingsEx(r.Device, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
        }
        catch { return false; }
    }
}
