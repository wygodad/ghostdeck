using System.Management;
using System.Text;

namespace MSIProfileSwitcher;

public readonly record struct HwSnapshot(
    int CpuTemp, int GpuTemp, int CpuFan, int GpuFan, int ChargeLimit, string Firmware);

/// <summary>
/// EC access via MSI WMI (root\wmi MSI_ACPI): Get_Data / Set_Data, Package_32 buffer.
/// Bytes[0]=address; write Bytes[1]=value; read -> result in Bytes[1]. Requires admin.
/// </summary>
public static class Ec
{
    private static string? _firmwareCache;

    private static ManagementObject GetInstance()
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSI_ACPI");
        foreach (ManagementObject mo in searcher.Get())
            return mo;
        throw new InvalidOperationException("MSI_ACPI WMI interface not found.");
    }

    private static void WriteWith(ManagementObject inst, ManagementClass pkg, byte addr, byte val)
    {
        var p = pkg.CreateInstance();
        var bytes = new byte[32];
        bytes[0] = addr;
        bytes[1] = val;
        p["Bytes"] = bytes;
        using var inParams = inst.GetMethodParameters("Set_Data");
        inParams["Data"] = p;
        inst.InvokeMethod("Set_Data", inParams, null);
    }

    private static byte ReadWith(ManagementObject inst, ManagementClass pkg, byte addr)
    {
        var p = pkg.CreateInstance();
        var bytes = new byte[32];
        bytes[0] = addr;
        p["Bytes"] = bytes;
        using var inParams = inst.GetMethodParameters("Get_Data");
        inParams["Data"] = p;
        using var outParams = inst.InvokeMethod("Get_Data", inParams, null);
        var outPkg = (ManagementBaseObject)outParams["Data"];
        return ((byte[])outPkg["Bytes"])[1];
    }

    public static string ReadFirmware()
    {
        try
        {
            using var inst = GetInstance();
            return ReadFirmware(inst);
        }
        catch { return ""; }
    }

    private static string ReadFirmware(ManagementObject inst)
    {
        if (_firmwareCache != null) return _firmwareCache;
        try
        {
            using var outParams = inst.InvokeMethod("Get_EC", null, null);
            var pkg = (ManagementBaseObject)outParams["Data"];
            var b = (byte[])pkg["Bytes"];
            var sb = new StringBuilder();
            for (int i = 2; i < b.Length && b[i] != 0; i++)
                if (b[i] is >= 32 and < 127) sb.Append((char)b[i]);
            var s = sb.ToString();
            _firmwareCache = s.Length >= 12 ? s[..12] : s;
        }
        catch { _firmwareCache = ""; }
        return _firmwareCache;
    }

    public static void Apply(IEnumerable<(byte addr, byte val)> recipe)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        foreach (var (addr, val) in recipe)
            WriteWith(inst, pkg, addr, val);
    }

    public static void SetChargeLimit(DeviceProfile dev, int percent)
    {
        if (percent < 10 || percent > 100) return;
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        WriteWith(inst, pkg, dev.ChargeCtrl, (byte)(0x80 | percent));
    }

    public static ProfileId GetCurrent(DeviceProfile dev)
    {
        try
        {
            using var inst = GetInstance();
            using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
            if (ReadWith(inst, pkg, dev.FanMode) == dev.FanSilentValue) return ProfileId.Silent;
            var shift = ReadWith(inst, pkg, dev.ShiftMode);
            if (shift == dev.ShiftTurboValue) return ProfileId.Extreme;
            if (shift == dev.ShiftEcoValue) return ProfileId.SuperBattery;
            return ProfileId.Balanced;
        }
        catch { return ProfileId.Balanced; }
    }

    public static HwSnapshot ReadHw(DeviceProfile dev)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        int cpuT = ReadWith(inst, pkg, dev.CpuTemp);
        int gpuT = ReadWith(inst, pkg, dev.GpuTemp);
        int cpuF = ReadWith(inst, pkg, dev.CpuFan);
        int gpuF = ReadWith(inst, pkg, dev.GpuFan);
        int chg = ReadWith(inst, pkg, dev.ChargeCtrl) & 0x7F;
        return new HwSnapshot(cpuT, gpuT, cpuF, gpuF, chg, ReadFirmware(inst));
    }
}
