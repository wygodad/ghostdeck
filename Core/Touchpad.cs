using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Precision-touchpad on/off - programmatically the same operation as disabling the
/// "HID-compliant touch pad" node in Device Manager: enumerate the HID interfaces, find the
/// collection whose top-level usage is Touch Pad (usage page 0x0D, usage 0x05) and
/// CM_Disable/CM_Enable its devnode. Needs admin, which the app already has. The device
/// state persists (including across reboots), so the hotkey and the panic reset both
/// re-enable it as keyboard-only escape hatches. The devnode is resolved once and cached;
/// a failed status read re-resolves.
/// </summary>
public static class Touchpad
{
    private const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    private const uint DN_STARTED = 0x08;
    private const uint CM_PROB_DISABLED = 22;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int CbSize; public Guid Guid; public uint Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public int CbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    // Only the two leading fields matter; Size covers the full HIDP_CAPS so the API can write it.
    [StructLayout(LayoutKind.Sequential, Size = 72)]
    private struct HIDP_CAPS { public ushort Usage; public ushort UsagePage; }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr file, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devInfo, IntPtr devInfoData, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int required, ref SP_DEVINFO_DATA devInfoData);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    [DllImport("cfgmgr32.dll")] private static extern int CM_Disable_DevNode(uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Enable_DevNode(uint devInst, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private static uint _devInst;
    private static bool _resolved;

    /// <summary>Devnode of the touch-pad HID collection, resolved once; null = none present.</summary>
    private static uint? Resolve()
    {
        if (_resolved) return _devInst != 0 ? _devInst : null;
        _resolved = true;
        _devInst = 0;
        try
        {
            HidD_GetHidGuid(out var guid);
            IntPtr set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == (IntPtr)(-1)) return null;
            try
            {
                var ifd = new SP_DEVICE_INTERFACE_DATA { CbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref ifd); i++)
                {
                    var dev = new SP_DEVINFO_DATA { CbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    SetupDiGetDeviceInterfaceDetailW(set, ref ifd, IntPtr.Zero, 0, out int need, ref dev);
                    if (need <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(need);
                    try
                    {
                        // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64 (4 + one WCHAR + padding)
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetailW(set, ref ifd, buf, need, out _, ref dev)) continue;
                        string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                        if (path.Length == 0) continue;
                        if (!IsTouchpad(path)) continue;
                        _devInst = dev.DevInst;
                        return _devInst;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
        catch { }
        return null;
    }

    private static bool IsTouchpad(string path)
    {
        IntPtr h = CreateFileW(path, 0, 3 /* read+write share */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (h == (IntPtr)(-1)) return false;
        try
        {
            if (!HidD_GetPreparsedData(h, out IntPtr prep)) return false;
            try
            {
                var caps = new HIDP_CAPS();
                return HidP_GetCaps(prep, ref caps) == 0x110000 /* HIDP_STATUS_SUCCESS */
                       && caps.UsagePage == 0x0D && caps.Usage == 0x05;
            }
            finally { HidD_FreePreparsedData(prep); }
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Whether a precision touchpad exists on this machine.</summary>
    public static bool Present() => Resolve() != null;

    /// <summary>1 = enabled, 0 = disabled, -1 = no touchpad / status unavailable.</summary>
    public static int State()
    {
        if (Resolve() is not { } inst) return -1;
        if (CM_Get_DevNode_Status(out uint status, out uint problem, inst, 0) != 0)
        {
            _resolved = false;   // stale devnode (driver reinstall?) - re-resolve next time
            return -1;
        }
        if ((status & DN_STARTED) != 0) return 1;
        return problem == CM_PROB_DISABLED ? 0 : -1;
    }

    /// <summary>Enable/disable the touchpad devnode. Throws when the config manager refuses.</summary>
    public static void Set(bool on)
    {
        if (Resolve() is not { } inst) throw new InvalidOperationException("no precision touchpad found");
        int cr = on ? CM_Enable_DevNode(inst, 0) : CM_Disable_DevNode(inst, 0);
        if (cr != 0) throw new InvalidOperationException($"config manager refused (CR_{cr})");
    }
}
