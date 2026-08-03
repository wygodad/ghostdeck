using System.Management;

namespace GhostDeck;

/// <summary>
/// Internal-panel brightness via WMI (root\wmi WmiMonitorBrightness / WmiMonitorBrightnessMethods).
/// Covers laptop panels driven by the display driver; external monitors (DDC/CI) are out of scope.
/// No elevation or EC needed, so this also works on unsupported hardware.
/// </summary>
public static class Brightness
{
    private static int _supported = -1;   // -1 unknown, 0 no, 1 yes (probed once)

    /// <summary>Whether the machine exposes the WMI brightness classes (has a controllable panel).</summary>
    public static bool Supported
    {
        get
        {
            if (_supported < 0) _supported = Get() >= 0 ? 1 : 0;
            return _supported == 1;
        }
    }

    /// <summary>Current brightness 0-100, or -1 when no controllable internal panel exists.</summary>
    public static int Get()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi",
                "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            foreach (ManagementObject o in s.Get())
                using (o) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return -1;
    }

    /// <summary>Set brightness 0-100 on every active internal panel. Throws when none accepts it.</summary>
    public static void Set(int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        using var s = new ManagementObjectSearcher(@"root\wmi",
            "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE");
        bool any = false;
        foreach (ManagementObject o in s.Get())
            using (o)
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)pct });
                any = true;
            }
        if (!any) throw new InvalidOperationException("no controllable display");
    }
}
