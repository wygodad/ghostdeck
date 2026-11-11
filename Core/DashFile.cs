using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>A whole Dashboard layout kept under a name in settings.json (1.45): the widgets with their sizes and settings, and the column count.</summary>
public sealed class DashSavedLayout
{
    public string Name { get; set; } = "";
    public int Cols { get; set; }
    public List<DashItem> Items { get; set; } = new();

    public const int MaxLayouts = 12, MaxName = 40;

    public DashSavedLayout Clone() => new() { Name = Name, Cols = Cols, Items = Items.Select(i => i.Clone()).ToList() };

    /// <summary>What settings.json may hold: at most <see cref="MaxLayouts"/> layouts, each with a clean name and a sane list of widgets.</summary>
    public static void Sanitize(List<DashSavedLayout> list)
    {
        list.RemoveAll(l => l == null || l.Items == null);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in list)
        {
            l.Name = DashOptions.Clean(l.Name ?? "", MaxName);
            if (l.Cols is not (0 or 4 or 5 or 6 or 8)) l.Cols = 0;
            DashItem.Sanitize(l.Items);
        }
        list.RemoveAll(l => l.Name.Length == 0 || l.Items.Count == 0 || !seen.Add(l.Name));
        if (list.Count > MaxLayouts) list.RemoveRange(MaxLayouts, list.Count - MaxLayouts);
    }
}

/// <summary>
/// The Dashboard's files (1.45): one widget's style, or a whole layout, as a small JSON file a
/// user can keep, move to another machine or pass on.
///
///   { "ghostdeck": "dashboard", "v": 1, "kind": "widget" | "layout", "cols": 0,
///     "items": [ { "Type": "core.cpuTemp", "W": 1, "H": 1, "Opt": { "color": "teal" } } ] }
///
/// A file comes from anywhere, so reading one trusts nothing in it:
/// - it is read whole or not at all: one thing out of place rejects the file and the Dashboard
///   stays as it was;
/// - it is small (<see cref="MaxBytes"/>), shallow (the parser stops at <see cref="MaxDepth"/>
///   levels) and short (<see cref="DashItem.MaxItems"/> widgets);
/// - only the fields named above are read. Nothing else in the file reaches the settings: every
///   item is built anew from checked values, and nothing of the file is carried over as it stands;
/// - a widget type is an id of a fixed shape, looked up in the app's own list (an unknown one
///   shows as a grey field, as an unknown type in settings.json does); sizes are whole numbers
///   in the grid's range;
/// - a setting is a short text, a whole number, yes/no or a short list of short texts, under a
///   plain key. Texts carry no control characters and no characters that reorder what is shown;
/// - the file holds data only. No text in it is ever used as a path, a command, an address or
///   code: a text is painted as a label or compared with what the app already knows.
///
/// The format is a promise: a file written by 1.45 loads in every later version. A later
/// version that has to change the shape raises "v", and this one refuses such a file by name.
/// </summary>
public static class DashFile
{
    public const int Version = 1;
    public const int MaxBytes = 256 * 1024;
    public const int MaxDepth = 6;
    public const string Widget = "widget", Layout = "layout";

    /// <summary>What a file held, once every bit of it was checked.</summary>
    public sealed record Content(string Kind, List<DashItem> Items, int Cols);

    public enum Error
    {
        None,
        /// <summary>The file could not be read or written.</summary>
        Io,
        /// <summary>Larger than <see cref="MaxBytes"/>.</summary>
        TooLarge,
        /// <summary>Not JSON, or nested deeper than a Dashboard file ever is.</summary>
        NotJson,
        /// <summary>JSON, but not a GhostDeck Dashboard file.</summary>
        NotOurs,
        /// <summary>Written by a newer GhostDeck in a shape this one does not know.</summary>
        Newer,
        /// <summary>A Dashboard file with something out of place.</summary>
        Damaged,
        /// <summary>More widgets than a Dashboard holds.</summary>
        TooMany,
        /// <summary>A sound file of the other kind: a widget's style where a layout was asked for, or the other way round.</summary>
        WrongKind,
    }

    private static readonly Regex TypeId = new("^[a-z]{1,16}\\.[A-Za-z0-9_]{1,40}\\z", RegexOptions.CultureInvariant);
    private static readonly Regex OptKey = new("^[A-Za-z][A-Za-z0-9_]{0,23}\\z", RegexOptions.CultureInvariant);
    // \z, not $: in .NET "$" also matches just before a line break that ends the text, so an id
    // with a line break at its end would pass (the hostile-file tests caught exactly that)

    // ------------------------------------------------------------------ reading

    /// <summary>Reads and checks a file; null and the reason when anything is wrong. Never throws.</summary>
    public static Content? Read(string path, out Error error)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { error = Error.Io; return null; }
            if (info.Length > MaxBytes) { error = Error.TooLarge; return null; }
            // read no more than the limit even if the file grows under us
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes = new byte[(int)Math.Min(info.Length, MaxBytes) + 1];
            int n = 0, got;
            while (n < bytes.Length && (got = fs.Read(bytes, n, bytes.Length - n)) > 0) n += got;
            if (n > MaxBytes) { error = Error.TooLarge; return null; }
            Array.Resize(ref bytes, n);
        }
        catch { error = Error.Io; return null; }
        return Parse(bytes, out error);
    }

    /// <summary>Checks the bytes of a file; null and the reason when anything is wrong. Never throws.</summary>
    public static Content? Parse(byte[] utf8, out Error error)
    {
        try { return ParseCore(utf8, out error); }
        catch { error = Error.Damaged; return null; }   // whatever a file manages to trip, it is not loaded
    }

    private static Content? ParseCore(byte[] utf8, out Error error)
    {
        error = Error.None;
        if (utf8 == null || utf8.Length > MaxBytes) { error = Error.TooLarge; return null; }
        var data = new ReadOnlyMemory<byte>(utf8);
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF) data = data[3..];   // a byte order mark from an editor

        JsonDocument doc;
        try { doc = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = MaxDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false }); }
        catch { error = Error.NotJson; return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = Error.NotOurs; return null; }
            if (!Fields(root, out var f)) { error = Error.Damaged; return null; }
            if (!f.TryGetValue("ghostdeck", out var mark) || mark.ValueKind != JsonValueKind.String || mark.GetString() != "dashboard") { error = Error.NotOurs; return null; }
            if (!f.TryGetValue("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int ver) || ver < 1) { error = Error.Damaged; return null; }
            if (ver > Version) { error = Error.Newer; return null; }
            if (!f.TryGetValue("kind", out var k) || k.ValueKind != JsonValueKind.String || k.GetString() is not (Widget or Layout)) { error = Error.Damaged; return null; }
            string kind = k.GetString()!;

            int cols = 0;
            if (f.TryGetValue("cols", out var c))
            {
                if (c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out cols) || cols is not (0 or 4 or 5 or 6 or 8)) { error = Error.Damaged; return null; }
            }

            if (!f.TryGetValue("items", out var arr) || arr.ValueKind != JsonValueKind.Array) { error = Error.Damaged; return null; }
            int count = arr.GetArrayLength();
            if (count > DashItem.MaxItems) { error = Error.TooMany; return null; }
            if (count == 0 || (kind == Widget && count != 1)) { error = Error.Damaged; return null; }

            var items = new List<DashItem>(count);
            foreach (var e in arr.EnumerateArray())
            {
                var item = Item(e);
                if (item == null) { error = Error.Damaged; return null; }
                items.Add(item);
            }
            return new Content(kind, items, kind == Layout ? cols : 0);
        }
    }

    /// <summary>An object's fields by name; false when a name comes twice (which one counts would be a guess).</summary>
    private static bool Fields(JsonElement obj, out Dictionary<string, JsonElement> fields)
    {
        fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var p in obj.EnumerateObject())
            if (!fields.TryAdd(p.Name, p.Value)) return false;
        return true;
    }

    /// <summary>One widget, built anew from checked values; null when anything about it is out of place.</summary>
    private static DashItem? Item(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !Fields(e, out var f)) return null;
        if (!f.TryGetValue("Type", out var t) || t.ValueKind != JsonValueKind.String || t.GetString() is not { } type || !TypeId.IsMatch(type)) return null;
        if (!f.TryGetValue("W", out var we) || we.ValueKind != JsonValueKind.Number || !we.TryGetInt32(out int w) || w < 1 || w > DashItem.MaxW) return null;
        if (!f.TryGetValue("H", out var he) || he.ValueKind != JsonValueKind.Number || !he.TryGetInt32(out int h) || h < 1 || h > DashItem.MaxH) return null;
        var item = new DashItem(type, w, h);
        if (!f.TryGetValue("Opt", out var o)) return item;
        if (o.ValueKind != JsonValueKind.Object) return null;

        var opt = new DashOptions();
        int keys = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in o.EnumerateObject())
        {
            if (++keys > DashItem.MaxOptKeys || !OptKey.IsMatch(p.Name) || !names.Add(p.Name)) return null;
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.String:
                    if (p.Value.GetString() is not { } s || !CleanText(s)) return null;
                    opt.Set(p.Name, s);
                    break;
                case JsonValueKind.Number:
                    if (!p.Value.TryGetInt32(out int n)) return null;
                    opt.Set(p.Name, n);
                    break;
                case JsonValueKind.True: opt.Set(p.Name, true); break;
                case JsonValueKind.False: opt.Set(p.Name, false); break;
                case JsonValueKind.Array:
                {
                    if (p.Value.GetArrayLength() is 0 or > DashOptions.MaxList) return null;
                    var list = new List<string>();
                    foreach (var x in p.Value.EnumerateArray())
                    {
                        if (x.ValueKind != JsonValueKind.String || x.GetString() is not { } xs || !CleanText(xs) || list.Contains(xs)) return null;
                        list.Add(xs);
                    }
                    opt.Set(p.Name, list);
                    break;
                }
                default: return null;   // an object, a null: not a setting
            }
        }
        var map = opt.ToMap();
        if (map != null && DashOptions.SizeOf(map) > DashItem.MaxOptChars) return null;
        item.Opt = map;
        return item;
    }

    /// <summary>
    /// A text a file may carry: not empty, at most <see cref="DashOptions.MaxText"/> characters,
    /// no spaces at its ends, no control characters, no half of a surrogate pair, and none of
    /// the characters that change the direction text is shown in (they make a label read as
    /// something it is not).
    /// </summary>
    internal static bool CleanText(string s)
    {
        if (s.Length == 0 || s.Length > DashOptions.MaxText || s != s.Trim()) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (char.IsControl(ch)) return false;
            if (ch is (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩') or '‎' or '‏' or '؜' or '﻿' or '￾' or '￿') return false;
            if (char.IsHighSurrogate(ch)) { if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return false; i++; }
            else if (char.IsLowSurrogate(ch)) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// The file for these items. Only what <see cref="Parse"/> accepts is written (a setting of a
    /// shape this version does not know is left out), so a file GhostDeck wrote always loads.
    /// Null when there is nothing a file could hold.
    /// </summary>
    public static byte[]? Write(string kind, IReadOnlyList<DashItem> items, int cols)
    {
        if (kind is not (Widget or Layout) || items.Count == 0 || items.Count > DashItem.MaxItems || (kind == Widget && items.Count != 1)) return null;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("ghostdeck", "dashboard");
            w.WriteNumber("v", Version);
            w.WriteString("kind", kind);
            if (kind == Layout) w.WriteNumber("cols", cols is 0 or 4 or 5 or 6 or 8 ? cols : 0);
            w.WriteStartArray("items");
            foreach (var it in items)
            {
                if (!TypeId.IsMatch(it.Type ?? "")) return null;
                w.WriteStartObject();
                w.WriteString("Type", it.Type);
                w.WriteNumber("W", Math.Clamp(it.W, 1, DashItem.MaxW));
                w.WriteNumber("H", Math.Clamp(it.H, 1, DashItem.MaxH));
                if (it.Opt is { Count: > 0 } opt)
                {
                    w.WriteStartObject("Opt");
                    int keys = 0;
                    foreach (var kv in opt)
                    {
                        if (keys >= DashItem.MaxOptKeys || !OptKey.IsMatch(kv.Key)) continue;
                        var val = kv.Value;
                        switch (val.ValueKind)
                        {
                            case JsonValueKind.String when val.GetString() is { } s && CleanText(s): w.WriteString(kv.Key, s); keys++; break;
                            case JsonValueKind.Number when val.TryGetInt32(out int n): w.WriteNumber(kv.Key, n); keys++; break;
                            case JsonValueKind.True: w.WriteBoolean(kv.Key, true); keys++; break;
                            case JsonValueKind.False: w.WriteBoolean(kv.Key, false); keys++; break;
                            case JsonValueKind.Array:
                            {
                                var list = val.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "")
                                              .Where(CleanText).Distinct().Take(DashOptions.MaxList).ToList();
                                if (list.Count == 0) break;
                                w.WriteStartArray(kv.Key);
                                foreach (var x in list) w.WriteStringValue(x);
                                w.WriteEndArray();
                                keys++;
                                break;
                            }
                        }
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var bytes = ms.ToArray();
        // what was written must load: a file this version cannot read back is not handed out
        return bytes.Length <= MaxBytes && Parse(bytes, out _) != null ? bytes : null;
    }

    /// <summary>Writes the file; <see cref="Error.Damaged"/> when the items cannot make one, <see cref="Error.Io"/> when the disk says no.</summary>
    public static Error Save(string path, string kind, IReadOnlyList<DashItem> items, int cols)
    {
        var bytes = Write(kind, items, cols);
        if (bytes == null) return Error.Damaged;
        try { File.WriteAllBytes(path, bytes); return Error.None; }
        catch { return Error.Io; }
    }

    /// <summary>A file name from a user's text: letters, digits, spaces, dashes and underscores only, never empty.</summary>
    public static string SafeFileName(string text, string fallback)
    {
        var sb = new StringBuilder();
        foreach (char ch in text ?? "")
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_') sb.Append(ch);
        string t = sb.ToString().Trim();
        if (t.Length > 40) t = t[..40].Trim();
        return t.Length == 0 ? fallback : t;
    }
}
