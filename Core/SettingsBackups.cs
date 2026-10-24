namespace GhostDeck;

/// <summary>
/// Copies of settings.json kept in the app's own folder (<c>%AppData%\GhostDeck\backups</c>), so
/// a file that was replaced by defaults, overwritten by an import or edited by hand can be put
/// back from inside the app (Settings → System → Backup). A copy is taken at start when the
/// file differs from the newest copy, before every import and restore, by hand from the Backup
/// card, and when the file could not be read (that copy keeps the unreadable bytes for a look).
/// The newest <see cref="Keep"/> copies stay; older ones are deleted. The files are small (10-20
/// KB each), so the whole set stays under 1 MB.
/// </summary>
public static class SettingsBackups
{
    /// <summary>The choices the Backup card offers for <see cref="Keep"/>.</summary>
    public static readonly int[] KeepChoices = { 50, 75, 100, 125, 150 };

    private static int _keep = 50;
    /// <summary>How many copies stay (Settings → System → Backup); older ones are deleted when a new one comes.</summary>
    public static int Keep
    {
        get => _keep;
        set => _keep = Math.Clamp(value, KeepChoices[0], KeepChoices[^1]);
    }

    // Fields that change on their own between two starts without the owner changing a setting:
    // they are left out of the "did the file change" comparison, so a start copy is taken when a
    // preference changed, not because the update check ran or the window was moved.
    private static readonly string[] Volatile =
    {
        "LastUpdateCheckUtc", "LastProfile", "LastFirmware", "ModelsSeen",
        "SettingsSubTab", "SettingsGamingSub", "WinX", "WinY", "WinW", "WinH", "WinMaximized",
    };

    /// <summary>Set by a test harness to work in a folder of its own; null = the app's folder.</summary>
    public static string? DirOverride { get; set; }

    public static string Dir => DirOverride ?? Path.Combine(AppSettings.Dir, "backups");

    /// <summary>Why the copy was taken; the name carries it, the Backup card shows it.</summary>
    public enum Kind { Start, Import, Manual, Unreadable }

    public sealed record Entry(string Path, DateTime Time, Kind Kind, long Size);

    private const string Prefix = "settings-";

    /// <summary>
    /// Copies <paramref name="source"/> (default: the live settings.json) into the folder.
    /// With <paramref name="onlyIfChanged"/> the copy is skipped when the bytes equal the newest
    /// copy's bytes, so a hundred starts of an unchanged file leave one copy, not a hundred.
    /// Returns the copy's path, or null when nothing was copied (no source, same bytes, or an
    /// error, which is noted in the log). Never throws: a backup must not stop the app.
    /// </summary>
    public static string? Take(Kind kind, string? source = null, bool onlyIfChanged = false)
    {
        try
        {
            source ??= AppSettings.FilePath;
            if (!File.Exists(source)) return null;
            byte[] bytes = File.ReadAllBytes(source);
            if (onlyIfChanged)
            {
                var newest = List().FirstOrDefault();
                if (newest != null && SameSettings(newest.Path, bytes)) return null;
            }
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}-{KindTag(kind)}.json");
            for (int n = 2; File.Exists(path); n++)   // two copies within one second
                path = Path.Combine(Dir, $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}-{KindTag(kind)}-{n}.json");
            var top = List().FirstOrDefault();
            File.WriteAllBytes(path, bytes);
            // Two copies within one timer tick get the same write time (Windows updates file times
            // coarsely); the new one must sort first, so its time is pushed just past the newest.
            if (top != null)
            {
                var prev = File.GetLastWriteTimeUtc(top.Path);
                if (File.GetLastWriteTimeUtc(path) <= prev) File.SetLastWriteTimeUtc(path, prev.AddMilliseconds(1));
            }
            Rotate();
            return path;
        }
        catch (Exception ex)
        {
            AppLifecycle.Note("settings copy not taken: " + ex.Message);
            return null;
        }
    }

    /// <summary>Every copy in the folder, newest first.</summary>
    public static List<Entry> List()
    {
        // Newest first by the file's own write time, which has sub-second precision: two copies
        // taken within one second (a start copy and an import copy) keep their real order, which
        // the name's seconds alone cannot tell.
        var list = new List<(Entry Entry, DateTime Written)>();
        try
        {
            if (!Directory.Exists(Dir)) return new List<Entry>();
            foreach (var f in Directory.GetFiles(Dir, Prefix + "*.json"))
            {
                if (Parse(Path.GetFileName(f)) is not { } p) continue;
                var fi = new FileInfo(f);
                list.Add((new Entry(f, p.Time, p.Kind, fi.Length), fi.LastWriteTimeUtc));
            }
        }
        catch (Exception ex) { AppLifecycle.Note("settings copies could not be listed: " + ex.Message); }
        return list.OrderByDescending(x => x.Written).ThenByDescending(x => x.Entry.Path, StringComparer.Ordinal).Select(x => x.Entry).ToList();
    }

    /// <summary>Deletes everything beyond the newest <see cref="Keep"/> copies.</summary>
    public static void Rotate()
    {
        foreach (var old in List().Skip(Keep))
        {
            try { File.Delete(old.Path); } catch { }
        }
    }

    private static string KindTag(Kind k) => k switch
    {
        Kind.Start => "start",
        Kind.Import => "import",
        Kind.Manual => "manual",
        _ => "unreadable",
    };

    private static (DateTime Time, Kind Kind)? Parse(string name)
    {
        // settings-20261024-011718-import.json, settings-20261024-011718-import-2.json
        if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = name[Prefix.Length..^5].Split('-');
        if (parts.Length < 3) return null;
        if (!DateTime.TryParseExact(parts[0] + parts[1], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var t)) return null;
        var kind = parts[2] switch
        {
            "start" => Kind.Start,
            "import" => Kind.Import,
            "manual" => Kind.Manual,
            "unreadable" => Kind.Unreadable,
            _ => (Kind?)null,
        };
        return kind is { } k ? (t, k) : null;
    }

    /// <summary>Deletes one copy. Returns false (and notes the reason) when the file would not go.</summary>
    public static bool Delete(Entry e)
    {
        try { File.Delete(e.Path); return true; }
        catch (Exception ex) { AppLifecycle.Note("settings copy could not be deleted: " + ex.Message); return false; }
    }

    /// <summary>
    /// The same settings, ignoring the volatile fields and the formatting: both files parsed as
    /// JSON, the volatile keys dropped, the rest compared key by key. Files that do not parse are
    /// compared byte for byte.
    /// </summary>
    private static bool SameSettings(string path, byte[] bytes)
    {
        try
        {
            byte[] other = File.ReadAllBytes(path);
            string? a = Fingerprint(bytes), b = Fingerprint(other);
            return a != null && b != null ? a == b : other.AsSpan().SequenceEqual(bytes);
        }
        catch { return false; }
    }

    /// <summary>A compact, key-sorted rendering of the file without the volatile fields; null when it is not a JSON object.</summary>
    public static string? Fingerprint(byte[] json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            var sb = new System.Text.StringBuilder();
            foreach (var p in doc.RootElement.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (Array.IndexOf(Volatile, p.Name) >= 0) continue;
                sb.Append(p.Name).Append('=').Append(p.Value.GetRawText()).Append('\n');
            }
            return sb.ToString();
        }
        catch { return null; }
    }
}
