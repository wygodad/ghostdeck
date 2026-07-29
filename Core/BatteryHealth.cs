using System.Management;

namespace GhostDeck;

/// <summary>
/// Battery wear data (roadmap #14) from the root\wmi battery classes - plain WMI, admin only,
/// no drivers. Values are in mWh; 0 = the firmware does not report that field (common for
/// CycleCount). Read on demand by the Settings → Power card; the numbers change so slowly
/// that no caching or refreshing is needed.
/// </summary>
public static class BatteryHealth
{
    public readonly record struct Info(int DesignMWh, int FullMWh, int Cycles)
    {
        public int WearPct => DesignMWh > 0 && FullMWh > 0
            ? Math.Clamp(100 - (int)((long)FullMWh * 100 / DesignMWh), 0, 100) : -1;
    }

    public static Info Read()
    {
        int design = ReadFirst("BatteryStaticData", "DesignedCapacity");
        int full = ReadFirst("BatteryFullChargedCapacity", "FullChargedCapacity");
        int cycles = ReadFirst("BatteryCycleCount", "CycleCount");
        return new Info(design, full, cycles);
    }

    private static int ReadFirst(string cls, string prop)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT {prop} FROM {cls}");
            foreach (var o in searcher.Get())
            {
                var v = o[prop];
                if (v != null) return Convert.ToInt32(Convert.ToUInt32(v));
            }
        }
        catch { }
        return 0;
    }
}
