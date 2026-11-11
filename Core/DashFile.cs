using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

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
///   levels), short (<see cref="DashItem.MaxItems"/> widgets) and of a bounded area
///   (<see cref="DashItem.MaxCells"/> grid cells in all), so no file makes a Dashboard taller
///   than the app is built for;
/// - only the fields named above are read. Nothing else in the file reaches the settings: every
///   item is built anew from checked values, and nothing of the file is carried over as it
///   stands. The "no name twice" rule covers the objects that are read (the file, an item, its
///   settings); a field that is ignored is ignored with whatever is in it;
/// - a widget type is an id of a fixed shape, looked up in the app's own list (an unknown one
///   shows as a grey field, as an unknown type in settings.json does); sizes are whole numbers
///   in the grid's range;
/// - a setting is a short text, a whole number, yes/no or a short list of short texts, under a
///   plain key, and the settings of one widget take at most <see cref="DashItem.MaxOptChars"/>
///   characters. A text is exactly what <see cref="DashOptions.Clean"/> would leave of it: no
///   control characters, no direction characters, no invisible ones, composed;
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
        /// <summary>Larger than <see cref="MaxBytes"/> (a file to read, or what a layout would take written out).</summary>
        TooLarge,
        /// <summary>Not JSON, or nested deeper than a Dashboard file ever is.</summary>
        NotJson,
        /// <summary>JSON, but not a GhostDeck Dashboard file.</summary>
        NotOurs,
        /// <summary>Written by a newer GhostDeck in a shape this one does not know.</summary>
        Newer,
        /// <summary>A Dashboard file with something out of place.</summary>
        Damaged,
        /// <summary>More widgets, or a larger area, than a Dashboard holds.</summary>
        TooMany,
        /// <summary>A sound file of the other kind: a widget's style where a layout was asked for, or the other way round.</summary>
        WrongKind,
        /// <summary>Nothing to write: a Dashboard without widgets.</summary>
        Empty,
    }

    // ------------------------------------------------------------------ reading

    /// <summary>Reads and checks a file; null and the reason when anything is wrong. Never throws.</summary>
    public static Content? Read(string path, out Error error)
    {
        byte[] bytes;
        try
        {
            // The size is the open file's own, and the read goes to the end of that same file: a
            // size asked of the path and a read from it could be two different files.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length > MaxBytes) { error = Error.TooLarge; return null; }
            bytes = new byte[MaxBytes + 1];
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
            if (DashItem.Cells(items) > DashItem.MaxCells) { error = Error.TooMany; return null; }
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
        if (!f.TryGetValue("Type", out var t) || t.ValueKind != JsonValueKind.String || t.GetString() is not { } type || !DashItem.TypeOk(type)) return null;
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
            if (++keys > DashItem.MaxOptKeys || !DashOptions.KeyOk(p.Name) || !names.Add(p.Name)) return null;
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
    /// A text a file may carry: not empty and exactly what <see cref="DashOptions.Clean"/> leaves
    /// of it, so it has nothing that the app would take out anyway (control characters, halves of
    /// pairs, direction characters, invisible ones, an uncomposed form, spaces at the ends, more
    /// than <see cref="DashOptions.MaxText"/> characters). One rule for what is typed, stored,
    /// written and read.
    /// </summary>
    internal static bool CleanText(string s) => s.Length > 0 && s.Length <= DashOptions.MaxText && DashOptions.Clean(s, DashOptions.MaxText) == s;

    // ------------------------------------------------------------------ writing

    /// <summary>
    /// The file for these items, or null and why. Everything a widget keeps is written (the
    /// settings are cleaned the way settings.json's are, so nothing a user set is left out), in
    /// plain UTF-8, indented while that fits the size limit and compact when it does not; a
    /// layout that does not fit even then is too large for a file. What comes out always loads:
    /// it is read back before it is handed over. Never throws.
    /// </summary>
    public static byte[]? Write(string kind, IReadOnlyList<DashItem> items, int cols, out Error error)
    {
        error = Error.None;
        try
        {
            if (kind is not (Widget or Layout) || (kind == Widget && items.Count != 1)) { error = Error.Damaged; return null; }
            if (items.Count == 0) { error = Error.Empty; return null; }
            if (items.Count > DashItem.MaxItems || DashItem.Cells(items) > DashItem.MaxCells) { error = Error.TooMany; return null; }
            foreach (var it in items) if (!DashItem.TypeOk(it.Type)) { error = Error.Damaged; return null; }
            byte[] bytes = Bytes(kind, items, cols, indented: true);
            if (bytes.Length > MaxBytes) bytes = Bytes(kind, items, cols, indented: false);
            if (bytes.Length > MaxBytes) { error = Error.TooLarge; return null; }
            if (Parse(bytes, out _) == null) { error = Error.Damaged; return null; }   // a file this version cannot read back is not handed out
            return bytes;
        }
        catch { error = Error.Damaged; return null; }
    }

    public static byte[]? Write(string kind, IReadOnlyList<DashItem> items, int cols) => Write(kind, items, cols, out _);

    /// <summary>
    /// A text as a JSON string in the shortest form there is: the letters of every script and
    /// emoji as they are, only the quote, the backslash and control characters escaped. The
    /// library's writers turn letters outside ASCII (the default one) or at least every emoji
    /// (the relaxed one) into escapes of six or twelve bytes, and a layout written that way can
    /// come out larger than the file it was read from, past the size limit. Written this way the
    /// compact form is never larger than a file that loaded, so whatever loads can be written.
    /// </summary>
    private static string Quoted(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char ch in s)
        {
            if (ch == '"') sb.Append("\\\"");
            else if (ch == '\\') sb.Append("\\\\");
            else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }

    private static byte[] Bytes(string kind, IReadOnlyList<DashItem> items, int cols, bool indented)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("ghostdeck", "dashboard");
            w.WriteNumber("v", Version);
            w.WriteString("kind", kind);
            if (kind == Layout && cols is 4 or 5 or 6 or 8) w.WriteNumber("cols", cols);   // absent: the window decides
            w.WriteStartArray("items");
            foreach (var it in items)
            {
                w.WriteStartObject();
                w.WriteString("Type", it.Type);
                w.WriteNumber("W", Math.Clamp(it.W, 1, DashItem.MaxW));
                w.WriteNumber("H", Math.Clamp(it.H, 1, DashItem.MaxH));
                // the settings as settings.json would keep them after a restart: cleaned, within the limits
                if (DashOptions.Sanitized(it.Opt, DashItem.MaxOptKeys, DashItem.MaxOptChars) is { Count: > 0 } opt)
                {
                    w.WriteStartObject("Opt");
                    foreach (var kv in opt)
                    {
                        switch (kv.Value.ValueKind)
                        {
                            case JsonValueKind.String: w.WritePropertyName(kv.Key); w.WriteRawValue(Quoted(kv.Value.GetString()!)); break;
                            case JsonValueKind.Number: w.WriteNumber(kv.Key, kv.Value.GetInt32()); break;
                            case JsonValueKind.True: w.WriteBoolean(kv.Key, true); break;
                            case JsonValueKind.False: w.WriteBoolean(kv.Key, false); break;
                            case JsonValueKind.Array:
                                w.WriteStartArray(kv.Key);
                                foreach (var x in kv.Value.EnumerateArray()) w.WriteRawValue(Quoted(x.GetString()!));
                                w.WriteEndArray();
                                break;
                        }
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Writes the file. It goes to a temporary file beside the target first and takes the target's
    /// place only when it is whole, so a full disk or a crash never leaves half a file where a good
    /// one was. Never throws.
    /// </summary>
    public static Error Save(string path, string kind, IReadOnlyList<DashItem> items, int cols)
    {
        var bytes = Write(kind, items, cols, out var error);
        if (bytes == null) return error;
        string? temp = null;
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
            temp = Path.Combine(dir, ".ghostdeck-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return Error.None;
        }
        catch
        {
            try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch { }
            return Error.Io;
        }
    }

    /// <summary>
    /// The name an export is offered under: English, without spaces, with the local date and time,
    /// the same in every language of the app, e.g. "ghostdeck-widget-cpu-temp_2026-11-11_14-05-09.json"
    /// for a widget's style and "ghostdeck-dashboard_2026-11-11_14-05-09.json" for a layout
    /// (<paramref name="type"/> null). The widget's part is its type id after the dot, split at
    /// capitals: only ASCII letters and digits of it are used.
    /// </summary>
    public static string ExportName(string? type, DateTime now)
    {
        var sb = new StringBuilder("ghostdeck-");
        if (type == null) sb.Append("dashboard");
        else
        {
            sb.Append("widget");
            string id = type[(type.IndexOf('.') + 1)..];
            bool dash = true;   // a dash is due before the next letter or digit
            foreach (char ch in id.Length > 40 ? id[..40] : id)
            {
                if (!char.IsAsciiLetterOrDigit(ch)) { dash = true; continue; }
                if (dash || char.IsAsciiLetterUpper(ch)) sb.Append('-');
                sb.Append(char.ToLowerInvariant(ch));
                dash = false;
            }
        }
        return sb.Append('_').Append(now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)).Append(".json").ToString();
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
