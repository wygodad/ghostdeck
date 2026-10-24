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
    public const int Keep = 50;

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
                if (newest != null && SameBytes(newest.Path, bytes)) return null;
            }
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}-{KindTag(kind)}.json");
            for (int n = 2; File.Exists(path); n++)   // two copies within one second
                path = Path.Combine(Dir, $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}-{KindTag(kind)}-{n}.json");
            File.WriteAllBytes(path, bytes);
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

    private static bool SameBytes(string path, byte[] bytes)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists && fi.Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch { return false; }
    }
}
