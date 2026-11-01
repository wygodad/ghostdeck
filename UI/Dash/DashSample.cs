namespace GhostDeck;

/// <summary>
/// One reading of everything the widgets show, taken once per tick and shared by all of them,
/// so ten widgets cost one EC read, not ten. Read in two steps, the way the Status page does
/// it: <see cref="ReadSlow"/> on a worker (the EC/WMI snapshot takes tens of milliseconds and
/// Perf.Warm queries the counters), then <see cref="Complete"/> on the window thread (cached
/// counters and the switch states, which the other pages also read there).
/// </summary>
internal sealed record DashSample(
    HwSnapshot Hw,
    int CpuUsage,
    int CpuClockMhz,
    int RamPercent, double RamTotalGb, double RamUsedGb,
    int VramUsedMb, int VramTotalMb,
    IReadOnlyList<Perf.DiskInfo> Disks,
    int BatteryMinutes,
    ProfileId Profile,
    bool CoolerBoost,
    bool Overlay,
    int Webcam, int Mic, int Touchpad,   // 1 = on, 0 = off, -1 = not available
    bool WinLock,
    int ChargeLimit,
    DateTime At)
{
    public static readonly DashSample Empty = new(
        default, 0, 0, 0, 0, 0, 0, 0, Array.Empty<Perf.DiskInfo>(), -1,
        ProfileId.Balanced, false, false, -1, -1, -1, false, 0, DateTime.MinValue);

    public bool HasHw => Hw.CpuTemp > 0 || Hw.GpuTemp > 0;

    /// <summary>Worker thread: the EC snapshot and the counter warm-up. Never throws.</summary>
    public static HwSnapshot ReadSlow(MainDeps d)
    {
        HwSnapshot hw = default;
        try { hw = d.Hw(); } catch { }
        Perf.Warm();   // disks, counters, battery estimate: queried here, the window thread reads the cached answers
        return hw;
    }

    /// <summary>Window thread: the cached counters and the switch states around the EC snapshot.</summary>
    public static DashSample Complete(MainDeps d, HwSnapshot hw)
    {
        int cpu = 0, clock = 0, ramPct = 0, vramUsed = 0, vramTotal = 0, battery = -1;
        double ramTotal = 0, ramUsed = 0;
        IReadOnlyList<Perf.DiskInfo> disks = Array.Empty<Perf.DiskInfo>();
        try { cpu = SysInfo.CpuUsage(); } catch { }
        try { clock = Perf.CpuClockMhz(); } catch { }
        try { (ramPct, ramTotal, ramUsed) = SysInfo.Ram(); } catch { }
        try { vramUsed = Perf.VramUsedMb(); vramTotal = Perf.VramTotalMb(); } catch { }
        try { disks = Perf.DisksCached(); } catch { }
        try { battery = Perf.BatteryMinutesLeftCached(); } catch { }

        ProfileId profile = ProfileId.Balanced;
        bool boost = false, overlay = false, winLock = false;
        int webcam = -1, mic = -1, touchpad = -1;
        try { profile = d.Current(); } catch { }
        try { boost = d.CoolerBoost(); } catch { }
        try { overlay = d.OverlayOn(); } catch { }
        try { webcam = d.WebcamState(); } catch { }
        try { mic = d.MicState(); } catch { }
        try { touchpad = d.TouchpadState(); } catch { }
        try { winLock = d.WinLockOn(); } catch { }

        return new DashSample(hw, cpu, clock, ramPct, ramTotal, ramUsed, vramUsed, vramTotal, disks, battery,
            profile, boost, overlay, webcam, mic, touchpad, winLock, d.Settings.ChargeLimit, DateTime.UtcNow);
    }
}
