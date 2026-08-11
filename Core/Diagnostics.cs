using System.IO.Compression;
using System.Management;
using System.Text;

namespace GhostDeck;

/// <summary>
/// One-zip diagnostic package (#30): report.txt, a read-only EC dump (or the exact failure it
/// produced - that failure IS diagnostic data, see #48), the vendor WMI blocks, the WMI
/// plumbing behind the EC interface (#56), and copies of settings/changelog/errors. Shared by
/// the Settings button and CLI --diag; collection is strictly read-only.
/// </summary>
public static class Diagnostics
{
    /// <summary>Write the package to <paramref name="path"/>. Throws on an IO failure.</summary>
    public static void Save(string path, string appVersion, string firmware, string model, string tier)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var sb = new StringBuilder();
        sb.AppendLine("=== GhostDeck diagnostic package ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  (read-only, no EC writes)");
        sb.AppendLine($"App version: {appVersion}");
        sb.AppendLine($"EC firmware: {(firmware.Length > 0 ? firmware : "-")}");
        sb.AppendLine($"Detected model: {model}   Tier: {tier}");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString}   64-bit: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine();
        sb.AppendLine("Contents: ec-dump.txt (read-only EC snapshot, or the exact error it produced),");
        sb.AppendLine("wmi-interface.txt (schema registration and its source file), settings.json,");
        sb.AppendLine("changelog.json, errors.log (only when it exists).");
        AddText(zip, "report.txt", sb.ToString());

        string dump;
        try
        {
            var d = Ec.DumpAll();
            var ds = new StringBuilder();
            for (int r = 0; r < 256; r += 16)
            {
                ds.Append($"{r:X2}: ");
                for (int i = 0; i < 16; i++) ds.Append($"{d[r + i]:X2} ");
                ds.AppendLine();
            }
            dump = ds.ToString();
        }
        catch (Exception ex)
        {
            dump = "EC dump failed: " + AppLifecycle.DescribeEcFailure(ex) + "\r\nRaw error: " + ex.Message;
        }
        AddText(zip, "ec-dump.txt", dump);
        AddText(zip, "msi-wmi-blocks.txt", MsiTelemetry.Dump());   // (#48) telemetry-mode triage
        AddText(zip, "wmi-interface.txt", WmiInterfaceInfo());     // (#56) schema/plumbing triage

        foreach (var name in new[] { "settings.json", "changelog.json", "errors.log" })
        {
            var p = Path.Combine(AppSettings.Dir, name);
            if (File.Exists(p)) zip.CreateEntryFromFile(p, name);
        }
    }

    /// <summary>
    /// The WMI plumbing behind the EC interface (discussion #56): the probe verdict, whether
    /// the MSI_ACPI schema is registered, where Windows says it comes from, whether the vendor
    /// schema file is deployed and signed, and the ACPI-WMI mapper devices. Answers "why is
    /// this machine unsupported" from one zip instead of a round of scripts. Read-only.
    /// </summary>
    private static string WmiInterfaceInfo()
    {
        var sb = new StringBuilder();
        void Line(string label, Func<string> get)
        {
            try { sb.AppendLine(label + get()); }
            catch (Exception ex) { sb.AppendLine(label + "query failed: " + ex.Message); }
        }

        var probe = Ec.ProbeFirmware();
        sb.AppendLine($"Firmware probe: {probe.Status}"
                      + (probe.Firmware.Length > 0 ? $"  ({probe.Firmware})" : "")
                      + (probe.Error != null ? $"  [{probe.Error.GetType().Name}: {probe.Error.Message}]" : ""));
        sb.AppendLine();

        Line("MSI_ACPI class: ", () =>
        {
            using var cls = new ManagementClass(@"root\wmi", "MSI_ACPI", null);
            cls.Get();
            return $"present, methods={cls.Methods.Count}";
        });
        Line("MSI_ACPI instances: ", () =>
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName FROM MSI_ACPI");
            var names = s.Get().Cast<ManagementBaseObject>()
                .Select(o => o["InstanceName"]?.ToString() ?? "?").ToList();
            return $"{names.Count}  [{string.Join(", ", names)}]";
        });
        Line("Schema source (WDMClassesOfDriver): ", () =>
        {
            using var s = new ManagementObjectSearcher(@"root\wmi",
                "SELECT ClassName, Driver FROM WDMClassesOfDriver WHERE ClassName='MSI_ACPI' OR ClassName='Package_32'");
            var rows = s.Get().Cast<ManagementBaseObject>()
                .Select(o => $"{o["ClassName"]} <- {o["Driver"]}").ToList();
            return rows.Count > 0 ? string.Join("; ", rows) : "no rows";
        });
        Line("WmiAcpi MofImagePath: ", () =>
            Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\WmiAcpi",
                "MofImagePath", null) as string ?? "(not set)");
        Line("msiapcfg.dll: ", () =>
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                 "SysWOW64", "msiapcfg.dll");
            if (!File.Exists(p)) return "NOT PRESENT (" + p + ")";
            var fi = new FileInfo(p);
            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
            string signer;
            try
            {
                using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                    System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(p));
                signer = cert.GetNameInfo(
                    System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
            }
            catch { signer = "(unsigned or unreadable)"; }
            return $"present, size={fi.Length}, fileVersion={vi.FileVersion}, signer={signer}";
        });
        Line("PNP0C14 devices: ", () =>
        {
            using var s = new ManagementObjectSearcher(
                "SELECT DeviceID, Status, Service FROM Win32_PnPEntity WHERE DeviceID LIKE '%PNP0C14%'");
            var rows = s.Get().Cast<ManagementBaseObject>()
                .Select(o => $"{o["DeviceID"]} status={o["Status"]} service={o["Service"]}").ToList();
            return rows.Count > 0 ? string.Join("; ", rows) : "none";
        });
        return sb.ToString();
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var w = new StreamWriter(e.Open());
        w.Write(content);
    }
}
