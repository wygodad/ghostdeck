using System.Management;
using System.Text;

namespace MSIProfileSwitcher;

public readonly record struct HwSnapshot(
    int CpuTemp, int GpuTemp, int CpuFan, int GpuFan, int ChargeLimit, string Firmware,
    int CpuRpm = 0, int GpuRpm = 0);

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

    /// <summary>
    /// READ-ONLY dump of the whole EC (0x00..0xFF) in a single WMI session.
    /// Used by the in-app "Report my model" wizard — same data the diagnostic
    /// scripts produce, no writes.
    /// </summary>
    public static byte[] DumpAll(Action<int>? onByte = null)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        var dump = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            dump[i] = ReadWith(inst, pkg, (byte)i);
            onByte?.Invoke(i);
        }
        return dump;
    }

    public static byte ReadByte(byte addr)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        return ReadWith(inst, pkg, addr);
    }

    // READ-ONLY: read several EC addresses in one WMI session (for the Status byte matrix).
    public static byte[] ReadMany(byte[] addrs)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        var r = new byte[addrs.Length];
        for (int i = 0; i < addrs.Length; i++) r[i] = ReadWith(inst, pkg, addrs[i]);
        return r;
    }

    // READ-ONLY: current fan-curve point tables (temps + speeds) for CPU (Fan 1) and GPU (Fan 2).
    public static (int[] cpuTemp, int[] cpuSpeed, int[] gpuTemp, int[] gpuSpeed)? ReadFanCurve(DeviceProfile dev)
    {
        if (dev.FanCurve is not { } fc) return null;
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        int[] Read(byte baseAddr)
        {
            var arr = new int[fc.Points];
            for (int i = 0; i < fc.Points; i++) arr[i] = ReadWith(inst, pkg, (byte)(baseAddr + i));
            return arr;
        }
        return (Read(fc.CpuTempBase), Read(fc.CpuSpeedBase), Read(fc.GpuTempBase), Read(fc.GpuSpeedBase));
    }

    // Write the fan-curve point tables (temps + speeds) for CPU (Fan 1) and GPU (Fan 2).
    public static void WriteFanCurve(DeviceProfile dev, int[] cpuTemp, int[] cpuSpeed, int[] gpuTemp, int[] gpuSpeed)
    {
        if (dev.FanCurve is not { } fc) return;
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        void W(byte baseAddr, int[] vals)
        {
            for (int i = 0; i < fc.Points && i < vals.Length; i++)
                WriteWith(inst, pkg, (byte)(baseAddr + i), (byte)Math.Clamp(vals[i], 0, 255));
        }
        W(fc.CpuTempBase, cpuTemp); W(fc.CpuSpeedBase, cpuSpeed);
        W(fc.GpuTempBase, gpuTemp); W(fc.GpuSpeedBase, gpuSpeed);
    }

    public static void SetFanMode(DeviceProfile dev, byte value)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        WriteWith(inst, pkg, dev.FanMode, value);
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

    // Cooler Boost (max fans) — msi-ec bit 7 of 0x98. Read-modify-write so we touch only that bit.
    public static bool GetCoolerBoost(DeviceProfile dev)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        return (ReadWith(inst, pkg, dev.CoolerBoost) & dev.CoolerBoostMask) != 0;
    }

    public static void SetCoolerBoost(DeviceProfile dev, bool on)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        byte cur = ReadWith(inst, pkg, dev.CoolerBoost);
        byte next = on ? (byte)(cur | dev.CoolerBoostMask) : (byte)(cur & ~dev.CoolerBoostMask);
        WriteWith(inst, pkg, dev.CoolerBoost, next);
    }

    public static ProfileId GetCurrent(DeviceProfile dev)
    {
        try
        {
            using var inst = GetInstance();
            using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
            var shift = ReadWith(inst, pkg, dev.ShiftMode);
            if (shift == dev.ShiftTurboValue) return ProfileId.Extreme;
            if (shift == dev.ShiftEcoValue) return ProfileId.SuperBattery;
            // comfort shift -> Silent vs Balanced is told apart ONLY by the fan byte (0x34 is the
            // same in both). 0x1D = Silent; anything else (0x0D auto, or 0x8D custom curve) = Balanced.
            // This is correct by design: a custom curve overwrites 0x1D, which really does drop the
            // Silent power policy, so the machine genuinely becomes Balanced + custom fans.
            return ReadWith(inst, pkg, dev.FanMode) == dev.FanSilentValue ? ProfileId.Silent : ProfileId.Balanced;
        }
        catch { return ProfileId.Balanced; }
    }

    public static HwSnapshot ReadHw(DeviceProfile dev)
    {
        using var inst = GetInstance();
        using var pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        int cpuT = ReadWith(inst, pkg, dev.CpuTemp);
        int gpuT = ReadWith(inst, pkg, dev.GpuTemp);
        // Fan duty is a raw PWM value whose ceiling can read slightly above 100; clamp for display.
        int cpuF = Math.Min(100, (int)ReadWith(inst, pkg, dev.CpuFan));
        int gpuF = Math.Min(100, (int)ReadWith(inst, pkg, dev.GpuFan));
        int chg = ReadWith(inst, pkg, dev.ChargeCtrl) & 0x7F;
        int cpuRpm = RpmFrom(inst, pkg, dev.CpuRpmAddr, dev.RpmConst);
        int gpuRpm = RpmFrom(inst, pkg, dev.GpuRpmAddr, dev.RpmConst);
        return new HwSnapshot(cpuT, gpuT, cpuF, gpuF, chg, ReadFirmware(inst), cpuRpm, gpuRpm);
    }

    // MSI EC stores fan tach as a divisor: RPM = const / raw (raw 0 -> stopped).
    private static int RpmFrom(ManagementObject inst, ManagementClass pkg, byte addr, int rpmConst)
    {
        if (addr == 0) return 0;
        int raw = ReadWith(inst, pkg, addr);
        return raw > 0 ? rpmConst / raw : 0;
    }

    /// <summary>
    /// READ-ONLY scan to locate the fan tach registers: returns every address whose
    /// (const / raw) falls in a plausible fan range, so it can be matched against the
    /// RPM that MSI Center shows. Used by the test/discovery dialog.
    /// </summary>
    public static List<(byte addr, int rpm)> RpmScan(int rpmConst = 478000)
    {
        var dump = DumpAll();
        var hits = new List<(byte, int)>();
        for (int a = 0; a < 256; a++)
        {
            int raw = dump[a];
            if (raw == 0) continue;
            int rpm = rpmConst / raw;
            if (rpm is >= 1500 and <= 6500) hits.Add(((byte)a, rpm));
        }
        return hits;
    }
}
