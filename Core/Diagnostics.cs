using System.IO.Compression;
using System.Text;

namespace GhostDeck;

/// <summary>
/// One-zip diagnostic package (#30): report.txt, a read-only EC dump (or the exact failure it
/// produced - that failure IS diagnostic data, see #48), the vendor WMI blocks, and copies of
/// settings/changelog/errors. Shared by the Settings button and CLI --diag; collection is
/// strictly read-only.
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
        sb.AppendLine("settings.json, changelog.json, errors.log (only when it exists).");
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

        foreach (var name in new[] { "settings.json", "changelog.json", "errors.log" })
        {
            var p = Path.Combine(AppSettings.Dir, name);
            if (File.Exists(p)) zip.CreateEntryFromFile(p, name);
        }
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var w = new StreamWriter(e.Open());
        w.Write(content);
    }
}
