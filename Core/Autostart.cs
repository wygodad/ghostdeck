using System.Diagnostics;

namespace GhostDeck;

/// <summary>
/// Autostart przez zadanie Harmonogramu (ONLOGON + RL HIGHEST) — uruchamia
/// elevowany .exe przy logowaniu BEZ promptu UAC. Tworzone/usuwane z Ustawien.
/// </summary>
public static class Autostart
{
    private const string TaskName = "GhostDeck";
    private const string OldTaskName = "MSIProfileSwitcher";   // pre-rename task, migrated away on startup

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>
    /// One-time rename migration: if the old "MSIProfileSwitcher" autostart task exists, delete it
    /// (it points at the old exe) and, since it means autostart was enabled, recreate it under the new
    /// name pointing at the current exe. Safe/idempotent — a no-op once the old task is gone.
    /// </summary>
    public static void Migrate()
    {
        try
        {
            if (Run($"/Query /TN \"{OldTaskName}\"") != 0) return;   // no old task -> nothing to do
            Run($"/Delete /TN \"{OldTaskName}\" /F");
            Set(true);                                               // recreate as "GhostDeck" at the current exe
        }
        catch { }
    }

    private static int Run(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string RunRead(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    /// <summary>
    /// The task stores the exe path from the moment autostart was enabled, so moving the exe
    /// (e.g. into Program Files) silently broke autostart (discussion #9). If the task exists
    /// but points elsewhere, recreate it at the current location. Runs on every startup.
    /// </summary>
    public static void Heal()
    {
        try
        {
            if (!IsEnabled()) return;
            string xml = RunRead($"/Query /TN \"{TaskName}\" /XML");
            var m = System.Text.RegularExpressions.Regex.Match(xml, @"<Command>\s*""?([^""<]+?)""?\s*</Command>");
            if (m.Success && !string.Equals(m.Groups[1].Value.Trim(), ExePath, StringComparison.OrdinalIgnoreCase))
                Set(true);
        }
        catch { }
    }

    public static bool IsEnabled()
    {
        try { return Run($"/Query /TN \"{TaskName}\"") == 0; }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        if (enabled)
            Run($"/Create /TN \"{TaskName}\" /TR \"\\\"{ExePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
        else
            Run($"/Delete /TN \"{TaskName}\" /F");
    }
}
