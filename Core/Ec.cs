using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace GhostDeck;

public readonly record struct HwSnapshot(
    int CpuTemp, int GpuTemp, int CpuFan, int GpuFan, int ChargeLimit, string Firmware,
    int CpuRpm = 0, int GpuRpm = 0);

/// <summary>
/// Verdict of the startup firmware identification. Success only says the firmware STRING was
/// read - whether the model database knows it is decided by the caller (Devices.Detect).
/// </summary>
public enum FirmwareProbeStatus
{
    Success,
    ClassMissing,     // MSI_ACPI schema not registered - fresh Windows install (discussion #56)
    InstanceMissing,  // class registered but no instance behind it
    NotSupported,     // interface present, firmware refuses the call (issue #48)
    AccessDenied,
    EmptyPayload,     // Get_EC answered but carried no firmware string
    TransientFailure, // WMI momentarily out, failed twice on a fresh session - retry later
    Other,
}

public readonly record struct FirmwareProbe(FirmwareProbeStatus Status, string Firmware, Exception? Error);

/// <summary>
/// EC access via MSI WMI (root\wmi MSI_ACPI): Get_Data / Set_Data, Package_32 buffer.
/// Bytes[0]=address; write Bytes[1]=value; read -> result in Bytes[1]. Requires admin.
/// </summary>
public static class Ec
{
    private static string? _firmwareCache;

    // ---------------- shared WMI session ----------------
    // Every EC call used to open its own session - a WQL query for MSI_ACPI plus a fresh
    // Package_32 class - and the 3 s poll did that several times per tick for the life of the
    // process. One session is reused instead. A cached COM object goes stale whenever the WMI
    // provider host recycles (that happens during normal work, and on resume from sleep), so EVERY
    // operation drops the session on any error and retries once on a fresh one: a caller can never
    // get stuck talking to a dead session.
    private static ManagementObject? _inst;
    private static ManagementClass? _pkg;
    // Monitor is re-entrant, so an operation that must not be cut in half (a profile recipe, a
    // curve write, a read-modify-write, one poll sample) holds it for its whole run while the
    // per-byte primitives below take it again harmlessly. The long read loops - DumpAll, ReadMany,
    // ReadFanCurve, RpmScan - deliberately do NOT: they take it per byte, so a 256-byte dump on a
    // background thread cannot block a UI-thread read for a second.
    private static readonly object _wmiLock = new();

    private static (ManagementObject inst, ManagementClass pkg) SessionLocked()
    {
        if (_inst != null && _pkg != null) return (_inst, _pkg);
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSI_ACPI");
        foreach (ManagementObject mo in searcher.Get()) { _inst = mo; break; }
        if (_inst == null) throw new InvalidOperationException("MSI_ACPI WMI interface not found.");
        _pkg = new ManagementClass(@"root\wmi", "Package_32", null);
        return (_inst, _pkg);
    }

    private static void DropLocked()
    {
        _inst?.Dispose(); _inst = null;
        _pkg?.Dispose(); _pkg = null;
    }

    /// <summary>Force a reconnect on the next EC call (used after sleep/resume).</summary>
    public static void DropSession() { lock (_wmiLock) DropLocked(); }

    /// <summary>Run one EC operation on the shared session, healing a stale one exactly once.</summary>
    private static T WithSession<T>(Func<ManagementObject, ManagementClass, T> body)
    {
        lock (_wmiLock)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var (inst, pkg) = SessionLocked();
                    return body(inst, pkg);
                }
                catch
                {
                    DropLocked();               // never keep a session that just failed
                    if (attempt >= 1) throw;    // one rebuild+retry, then it is a real failure
                }
            }
        }
    }

    private static void WithSession(Action<ManagementObject, ManagementClass> body) =>
        WithSession<object?>((i, p) => { body(i, p); return null; });

    /// <summary>Read one EC byte (own session handling).</summary>
    private static byte ReadRaw(byte addr) => WithSession((inst, pkg) => ReadWith(inst, pkg, addr));

    /// <summary>Write one EC byte (own session handling).</summary>
    private static void WriteRaw(byte addr, byte val) => WithSession((inst, pkg) => WriteWith(inst, pkg, addr, val));

    private static void WriteWith(ManagementObject inst, ManagementClass pkg, byte addr, byte val)
    {
        using var p = pkg.CreateInstance();   // COM-backed, dispose instead of leaving it to the finalizer
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
        using var p = pkg.CreateInstance();   // COM-backed, dispose instead of leaving it to the finalizer
        var bytes = new byte[32];
        bytes[0] = addr;
        p["Bytes"] = bytes;
        using var inParams = inst.GetMethodParameters("Get_Data");
        inParams["Data"] = p;
        using var outParams = inst.InvokeMethod("Get_Data", inParams, null);
        var outPkg = (ManagementBaseObject)outParams["Data"];
        return ((byte[])outPkg["Bytes"])[1];
    }

    public static string ReadFirmware() => ProbeFirmware().Firmware;

    /// <summary>
    /// Firmware identification with an explicit verdict instead of a swallowed "".
    /// Classification runs on the exception that escapes WithSession AFTER its one
    /// rebuild+retry, so TransientFailure means "failed twice on a fresh session" - the
    /// right trigger for a delayed retry, while single blips never surface at all.
    /// </summary>
    public static FirmwareProbe ProbeFirmware()
    {
        try
        {
            string s = WithSession((inst, _) => ReadFirmware(inst));
            return new FirmwareProbe(
                s.Length > 0 ? FirmwareProbeStatus.Success : FirmwareProbeStatus.EmptyPayload, s, null);
        }
        catch (Exception ex)
        {
            var status = ex switch
            {
                ManagementException { ErrorCode: ManagementStatus.InvalidClass or ManagementStatus.NotFound }
                    => FirmwareProbeStatus.ClassMissing,
                InvalidOperationException => FirmwareProbeStatus.InstanceMissing,
                ManagementException { ErrorCode: ManagementStatus.NotSupported } => FirmwareProbeStatus.NotSupported,
                ManagementException { ErrorCode: ManagementStatus.AccessDenied } => FirmwareProbeStatus.AccessDenied,
                COMException com when (uint)com.HResult == 0x80070005 => FirmwareProbeStatus.AccessDenied,
                _ when AppLifecycle.IsTransient(ex) => FirmwareProbeStatus.TransientFailure,
                _ => FirmwareProbeStatus.Other,
            };
            return new FirmwareProbe(status, "", ex);
        }
    }

    private static string ReadFirmware(ManagementObject inst)
    {
        if (_firmwareCache != null) return _firmwareCache;
        using var outParams = inst.InvokeMethod("Get_EC", null, null);
        var pkg = (ManagementBaseObject)outParams["Data"];
        var b = (byte[])pkg["Bytes"];
        var sb = new StringBuilder();
        for (int i = 2; i < b.Length && b[i] != 0; i++)
            if (b[i] is >= 32 and < 127) sb.Append((char)b[i]);
        var s = sb.ToString();
        if (s.Length > 12) s = s[..12];
        // cache only a non-empty answer: an empty payload stays retryable (the probe reports
        // it as EmptyPayload), and a call that threw leaves the cache unset so a later tick
        // can still fill it. Exceptions escape to the caller - the probe classifies them and
        // the per-tick path (TryReadHw) already swallows them.
        if (s.Length > 0) _firmwareCache = s;
        return s;
    }

    /// <summary>
    /// READ-ONLY dump of the whole EC (0x00..0xFF) in a single WMI session.
    /// Used by the in-app "Report my model" wizard — same data the diagnostic
    /// scripts produce, no writes.
    /// </summary>
    public static byte[] DumpAll(Action<int>? onByte = null)
    {
        var dump = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            dump[i] = ReadRaw((byte)i);
            onByte?.Invoke(i);
        }
        return dump;
    }

    public static byte ReadByte(byte addr) => ReadRaw(addr);

    // READ-ONLY: read several EC addresses in one WMI session (for the Status byte matrix).
    public static byte[] ReadMany(byte[] addrs)
    {
        var r = new byte[addrs.Length];
        for (int i = 0; i < addrs.Length; i++) r[i] = ReadRaw(addrs[i]);
        return r;
    }

    // READ-ONLY: current fan-curve point tables (temps + speeds) for CPU (Fan 1) and GPU (Fan 2).
    public static (int[] cpuTemp, int[] cpuSpeed, int[] gpuTemp, int[] gpuSpeed)? ReadFanCurve(DeviceProfile dev)
    {
        if (dev.FanCurve is not { } fc) return null;
        int[] Read(byte baseAddr)
        {
            var arr = new int[fc.Points];
            for (int i = 0; i < fc.Points; i++) arr[i] = ReadRaw((byte)(baseAddr + i));
            return arr;
        }
        return (Read(fc.CpuTempBase), Read(fc.CpuSpeedBase), Read(fc.GpuTempBase), Read(fc.GpuSpeedBase));
    }

    // Write the fan-curve point tables (temps + speeds) for CPU (Fan 1) and GPU (Fan 2).
    public static void WriteFanCurve(DeviceProfile dev, int[] cpuTemp, int[] cpuSpeed, int[] gpuTemp, int[] gpuSpeed)
    {
        if (dev.FanCurve is not { } fc) return;
        void W(byte baseAddr, int[] vals)
        {
            for (int i = 0; i < fc.Points && i < vals.Length; i++)
                WriteRaw((byte)(baseAddr + i), (byte)Math.Clamp(vals[i], 0, 255));
        }
        lock (_wmiLock)
        {
            W(fc.CpuTempBase, cpuTemp); W(fc.CpuSpeedBase, cpuSpeed);
            // single-curve boards (e.g. GF63 12VE): the GPU tables are a dead field the firmware
            // never reads - don't write them at all
            if (!fc.SingleFan) { W(fc.GpuTempBase, gpuTemp); W(fc.GpuSpeedBase, gpuSpeed); }
        }
    }

    public static void SetFanMode(DeviceProfile dev, byte value) => WriteRaw(dev.FanMode, value);

    public static void Apply(IEnumerable<(byte addr, byte val)> recipe)
    {
        lock (_wmiLock)
            foreach (var (addr, val) in recipe)
                WriteRaw(addr, val);
    }

    public static void SetChargeLimit(DeviceProfile dev, int percent)
    {
        if (percent < 10 || percent > 100) return;
        WriteRaw(dev.ChargeCtrl, (byte)(0x80 | percent));
    }

    // Cooler Boost (max fans) — msi-ec bit 7 of 0x98. Read-modify-write so we touch only that bit.
    public static bool GetCoolerBoost(DeviceProfile dev) =>
        (ReadRaw(dev.CoolerBoost) & dev.CoolerBoostMask) != 0;

    public static void SetCoolerBoost(DeviceProfile dev, bool on)
    {
        lock (_wmiLock)
        {
            byte cur = ReadRaw(dev.CoolerBoost);
            byte next = on ? (byte)(cur | dev.CoolerBoostMask) : (byte)(cur & ~dev.CoolerBoostMask);
            WriteRaw(dev.CoolerBoost, next);
        }
    }

    // (#26) Keyboard-backlight level, 0-3 = off/low/mid/high. msi-ec kbd_bl: the register holds
    // 0x80 | level (state_base_value 0x80); the level itself lives in the low 2 bits. The address
    // is per-family (0xF3 / 0xD3), resolved by Devices.KbdBacklightFor.
    public static int GetKbdBacklight(byte addr) => ReadByte(addr) & 0x03;

    public static void SetKbdBacklight(byte addr, int level) =>
        WriteRaw(addr, (byte)(0x80 | Math.Clamp(level, 0, 3)));

    // (#27) Webcam switch + block, msi-ec 0x2E / 0x2F bit 1 (identical across every conf).
    // 0x2E is the same switch the Fn camera key flips: bit set = camera on the USB bus.
    // 0x2F is a lock ABOVE that switch and is INVERTED: bit set = switching allowed,
    // bit clear = camera stays off and the Fn key / soft switch stop working.
    private const byte WebcamAddr = 0x2E, WebcamBlockAddr = 0x2F, WebcamMask = 0x02;

    public static bool GetWebcam() => (ReadByte(WebcamAddr) & WebcamMask) != 0;
    public static void SetWebcam(bool on) => SetMaskedBit(WebcamAddr, WebcamMask, on);
    public static bool GetWebcamBlock() => (ReadByte(WebcamBlockAddr) & WebcamMask) == 0;
    public static void SetWebcamBlock(bool blocked) => SetMaskedBit(WebcamBlockAddr, WebcamMask, !blocked);

    // Fn/Windows key swap, msi-ec fn_win_swap: bit 4 at a per-family address with a per-family
    // direction invert (Devices.FnWinSwapFor). Normalized like msi-ec's fn_key attribute:
    // fn-left = !(raw bit ^ invert). Persisted by the EC itself, survives reboots.
    private const byte FnWinSwapMask = 0x10;

    public static bool GetFnLeft((byte Addr, bool Invert) fs) =>
        !(((ReadByte(fs.Addr) & FnWinSwapMask) != 0) ^ fs.Invert);

    public static void SetFnLeft((byte Addr, bool Invert) fs, bool left) =>
        SetMaskedBit(fs.Addr, FnWinSwapMask, !left ^ fs.Invert);

    private static void SetMaskedBit(byte addr, byte mask, bool set)
    {
        lock (_wmiLock)
        {
            byte cur = ReadRaw(addr);
            byte next = set ? (byte)(cur | mask) : (byte)(cur & ~mask);
            WriteRaw(addr, next);
        }
    }

    public static ProfileId GetCurrent(DeviceProfile dev)
    {
        try
        {
            var shift = ReadRaw(dev.ShiftMode);
            if (shift == dev.ShiftTurboValue) return ProfileId.Extreme;
            // A board's fourth shift value sits on top of the turbo state rather than beside it, so
            // it reports as Extreme. Without this the comfort branch below would claim it, and the
            // 3 s poll would log a profile change every time the vendor software set that mode.
            if (dev.FourthMode is { } fm && shift == fm.ShiftValue) return ProfileId.Extreme;
            if (shift == dev.ShiftEcoValue) return ProfileId.SuperBattery;
            // comfort shift -> Silent vs Balanced is told apart ONLY by the fan byte (0x34 is the
            // same in both). 0x1D = Silent; anything else (0x0D auto, or 0x8D custom curve) = Balanced.
            // This is correct by design: a custom curve overwrites 0x1D, which really does drop the
            // Silent power policy, so the machine genuinely becomes Balanced + custom fans.
            return ReadRaw(dev.FanMode) == dev.FanSilentValue ? ProfileId.Silent : ProfileId.Balanced;
        }
        catch { return ProfileId.Balanced; }
    }

    /// <summary>
    /// The one entry point for periodic hardware sampling. A WMI call can be refused for reasons
    /// unrelated to the EC (provider host recycling, sleep/resume, service restart, system
    /// shutdown); that is a missing sample, not an app error, so the failure is absorbed HERE -
    /// callers get false, keep their last good data and simply try again on their next tick.
    /// </summary>
    public static bool TryReadHw(DeviceProfile dev, out HwSnapshot hw)
    {
        try { hw = ReadHw(dev); return true; }
        catch (Exception ex) when (ex is ManagementException or COMException
            or ObjectDisposedException or InvalidOperationException)
        {
            AppLifecycle.Report(ex, "ec");   // transient codes are dropped there; oddities land in errors.log
            hw = default;
            return false;
        }
    }

    private static HwSnapshot ReadHw(DeviceProfile dev)
    {
        // One lock for the whole sample: these reads belong to the same tick, and holding it keeps
        // a scene/profile write from landing in the middle of them.
        lock (_wmiLock)
        {
            int cpuT = ReadRaw(dev.CpuTemp);
            int gpuT = ReadRaw(dev.GpuTemp);
            // Fan duty is a raw PWM value whose ceiling can read slightly above 100; clamp for display.
            int cpuF = Math.Min(100, (int)ReadRaw(dev.CpuFan));
            int gpuF = Math.Min(100, (int)ReadRaw(dev.GpuFan));
            int chg = ReadRaw(dev.ChargeCtrl) & 0x7F;
            int cpuRpm = RpmFrom(dev.CpuRpmAddr, dev.RpmConst);
            int gpuRpm = RpmFrom(dev.GpuRpmAddr, dev.RpmConst);
            string fw = WithSession((inst, _) => ReadFirmware(inst));
            return new HwSnapshot(cpuT, gpuT, cpuF, gpuF, chg, fw, cpuRpm, gpuRpm);
        }
    }

    // MSI EC stores fan tach as a divisor: RPM = const / raw (raw 0 -> stopped). A raw value in the
    // low single digits is not a fan speed, it is the register caught between updates: raw 2 reads
    // as 239000 RPM, and that lands in Status, the overlay and a power-test report as if it meant
    // something. Past what a laptop fan can physically do we report nothing instead.
    //
    // The ceiling is 8000, not the arbitrary 12000 it started as (issue #92). Two measurements
    // set it: the divisor is a single byte, so the LOWEST speed this register can express at all
    // is 478000/255 = 1874 RPM - once a fan slows past that, whatever stays in the register is
    // not a reading; and the fastest fan we have ever logged on any model is 7206 RPM (GE66 under
    // load with Fan Boost). A slow fan was therefore reported as ~9958 RPM, which passed 12000
    // and reached Status as a number. No reading beats a wrong one.
    private const int MaxPlausibleRpm = 8000;

    private static int RpmFrom(byte addr, int rpmConst)
    {
        if (addr == 0) return 0;
        int raw = ReadRaw(addr);
        if (raw == 0) return 0;
        int rpm = rpmConst / raw;
        return rpm <= MaxPlausibleRpm ? rpm : 0;
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
