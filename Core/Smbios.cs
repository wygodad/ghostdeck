using Microsoft.Win32;

namespace GhostDeck;

/// <summary>
/// The laptop's own model name as the firmware reports it to Windows (SMBIOS "System Product
/// Name", e.g. "Raider A18 HX A9WJG"). Several retail models share one EC firmware and so one
/// database entry; this is the only place that says WHICH of them this machine is. Read once
/// from the registry copy Windows keeps (no WMI, no elevation, microseconds).
/// </summary>
public static class Smbios
{
    private static string? _product;

    /// <summary>"Raider A18 HX A9WJG", or "" when Windows has nothing (virtual machines, odd firmware).</summary>
    public static string ProductName()
    {
        if (_product != null) return _product;
        string v = "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            v = (k?.GetValue("SystemProductName") as string ?? "").Trim();
        }
        catch { }
        // test hook for the preview tool: GHOSTDECK_PRODUCT=<name> renders as that machine
        try { var env = Environment.GetEnvironmentVariable("GHOSTDECK_PRODUCT"); if (!string.IsNullOrWhiteSpace(env)) v = env.Trim(); } catch { }
        return _product = v;
    }
}
