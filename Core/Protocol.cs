using Microsoft.Win32;

namespace GhostDeck;

/// <summary>
/// ghostdeck:// links (roadmap #89): the CLI reachable from a browser, a Stream Deck "open"
/// action, AutoHotkey or a plain shortcut, without a plugin. The grammar maps onto the CLI one
/// to one: ghostdeck://scene/Gaming is "--scene Gaming", ghostdeck://fanboost/on/300 is
/// "--fanboost on 300". Only state-changing commands are reachable this way - a web page must
/// not be able to write a file on the user's disk (--diag, the model dumps), and console-only
/// commands (--status, --help) have nowhere to print from a link. The registration lives in
/// the user's own registry hive (HKCU\Software\Classes), so it needs no administrator, and it
/// is re-pointed on every start when the exe moved - the same healing the autostart task has.
/// </summary>
public static class Protocol
{
    public const string Scheme = "ghostdeck";
    private const string KeyPath = @"Software\Classes\" + Scheme;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "profile", "cycle", "fanboost", "overlay", "curve", "refresh", "charge", "travel", "kbd",
        "webcam", "fnswap", "brightness", "hdr", "touchpad", "mic", "turbo", "winlock", "scene", "panic",
    };

    public static bool IsLink(string arg) => arg.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "ghostdeck://scene/Gaming%20night/" -> ["--scene", "Gaming night"]. Null = not a usable
    /// link (unknown or forbidden command, too many parts, an empty or oversized part).
    /// </summary>
    public static string[]? ToArgs(string url)
    {
        if (!IsLink(url)) return null;
        string rest = url[(Scheme.Length + 1)..].TrimStart('/');   // with or without the "//"
        int cut = rest.IndexOfAny(new[] { '?', '#' });               // nothing after a query or fragment
        if (cut >= 0) rest = rest[..cut];
        var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 3) return null;
        string cmd = parts[0].ToLowerInvariant();
        if (!Allowed.Contains(cmd)) return null;
        var args = new string[parts.Length];
        args[0] = "--" + cmd;
        for (int i = 1; i < parts.Length; i++)
        {
            string v;
            try { v = Uri.UnescapeDataString(parts[i]).Trim(); }
            catch { return null; }
            // the pipe carries the arguments tab-separated, so a control character is never valid
            if (v.Length is 0 or > 100 || v.Any(char.IsControl)) return null;
            args[i] = v;
        }
        return args;
    }

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
    private static string Command => $"\"{ExePath}\" \"%1\"";

    /// <summary>True when the scheme is registered for this user (any exe path).</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
            return k?.GetValue("") is string;
        }
        catch { return false; }
    }

    /// <summary>Registers the scheme, or re-points it at this exe when it moved. Idempotent.</summary>
    public static void Register()
    {
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (root.GetValue("") as string != "URL:GhostDeck") root.SetValue("", "URL:GhostDeck");
            if (root.GetValue("URL Protocol") == null) root.SetValue("URL Protocol", "");
            using (var icon = root.CreateSubKey("DefaultIcon"))
                if (icon.GetValue("") as string != ExePath + ",0") icon.SetValue("", ExePath + ",0");
            using var cmd = root.CreateSubKey(@"shell\open\command");
            if (cmd.GetValue("") as string != Command) cmd.SetValue("", Command);
        }
        catch { }
    }

    public static void Unregister()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false); }
        catch { }
    }
}
