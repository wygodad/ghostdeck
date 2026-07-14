using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Primary-display refresh-rate control via user32 (EnumDisplaySettings / ChangeDisplaySettingsEx).
/// Pure Windows display API - no EC involvement - so unlike profiles it works on EVERY model,
/// including unrecognised firmware (not gated by Writable). Only the frequency is changed
/// (resolution and colour depth are kept), only modes the panel actually reports are requested,
/// and v1 deliberately touches the PRIMARY display only (the laptop panel; external monitors
/// are left alone). See TECHNICAL.md §28.
/// </summary>
public static class Display
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint CDS_UPDATEREGISTRY = 0x01;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;

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

    /// <summary>Current refresh rate of the primary display in Hz (0 when unknown).</summary>
    public static int Current()
    {
        try
        {
            var dm = NewMode();
            return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) ? (int)dm.dmDisplayFrequency : 0;
        }
        catch { return 0; }
    }

    /// <summary>Refresh rates the primary display supports at its CURRENT resolution, ascending.</summary>
    public static List<int> SupportedRates()
    {
        var rates = new SortedSet<int>();
        try
        {
            var cur = NewMode();
            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur)) return rates.ToList();
            for (int i = 0; ; i++)
            {
                var dm = NewMode();
                if (!EnumDisplaySettings(null, i, ref dm)) break;
                if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight &&
                    dm.dmBitsPerPel == cur.dmBitsPerPel && dm.dmDisplayFrequency > 1)
                    rates.Add((int)dm.dmDisplayFrequency);
            }
        }
        catch { }
        return rates.ToList();
    }

    /// <summary>
    /// Switch the primary display to the given refresh rate (keeping resolution/depth).
    /// Refuses modes the panel does not report. Returns true when the mode is active.
    /// </summary>
    public static bool SetRefresh(int hz)
    {
        try
        {
            if (hz <= 0) return false;
            var dm = NewMode();
            if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) return false;
            if ((int)dm.dmDisplayFrequency == hz) return true;
            if (!SupportedRates().Contains(hz)) return false;
            dm.dmDisplayFrequency = (uint)hz;
            dm.dmFields = DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            return ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
        }
        catch { return false; }
    }
}
