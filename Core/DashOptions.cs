using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// A widget's own settings, as its layout item stores them (<see cref="DashItem.Opt"/>): a small
/// map of key to text, number, yes/no or a list of short texts. Every getter takes the default
/// and the allowed range and never throws: a value of the wrong kind reads as the default, a
/// number out of range is clamped, a text is cleaned and cut. A hand-edited file or a file from
/// a newer version therefore cannot break a widget. Keys this version does not know are carried
/// along untouched. Only what differs from the defaults is stored (<see cref="Remove"/>), so a
/// widget nobody has touched has no map at all and looks as it did in 1.44.
/// </summary>
public sealed class DashOptions
{
    /// <summary>Longest text a getter returns (a label), in UTF-16 units.</summary>
    public const int MaxText = 64;
    /// <summary>
    /// Most entries a list holds (which profiles, which switches, which scenes). A list of what
    /// to show cannot say "all but one" in fewer entries than there are elements, so the bound is
    /// far above what a widget usually offers; the settings card never stores a longer pick cut
    /// short, it keeps it in the card and says so (WidgetSettingsCard).
    /// </summary>
    public const int MaxList = 64;

    private readonly Dictionary<string, JsonElement> _map;

    public DashOptions(Dictionary<string, JsonElement>? map = null) { _map = map == null ? new() : new(map); }

    public static DashOptions Of(DashItem item) => new(item.Opt);

    public bool IsEmpty => _map.Count == 0;
    public bool Has(string key) => _map.ContainsKey(key);
    public DashOptions Clone() => new(_map);

    /// <summary>What goes into the layout item: a copy of the map, or null when nothing is set.</summary>
    public Dictionary<string, JsonElement>? ToMap() => _map.Count == 0 ? null : new(_map);

    public string Text(string key, string def = "")
    {
        if (!_map.TryGetValue(key, out var v) || Str(v) is not { } s) return def;
        return Clean(s, MaxText);
    }

    /// <summary>
    /// A JSON string's text, or null when it is not a string or cannot be read. A settings file
    /// edited by hand can hold a string that is no text at all (half of a surrogate pair written
    /// as an escape): reading it throws, and a setting must never take the Dashboard down.
    /// </summary>
    internal static string? Str(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String) return null;
        try { return v.GetString(); } catch { return null; }
    }

    /// <summary>A whole number within [min, max]; a number written as text is accepted too.</summary>
    public int Int(string key, int def, int min, int max)
    {
        if (!_map.TryGetValue(key, out var v)) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
            return (int)Math.Clamp(Math.Round(d), min, max);
        if (Str(v) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return Math.Clamp(n, min, max);
        return def;
    }

    public bool Flag(string key, bool def)
    {
        if (!_map.TryGetValue(key, out var v)) return def;
        return v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => def };
    }

    /// <summary>One of <paramref name="allowed"/> (exact match), else the default.</summary>
    public string Choice(string key, string def, IReadOnlyList<string> allowed)
    {
        if (!_map.TryGetValue(key, out var v) || Str(v) is not { } t) return def;
        foreach (var a in allowed) if (a == t) return a;
        return def;
    }

    /// <summary>
    /// The list under a key: distinct cleaned texts, at most <see cref="MaxList"/>. Null when the key
    /// is absent or not a list, which a widget reads as "its default selection" (usually everything).
    /// </summary>
    public IReadOnlyList<string>? List(string key)
    {
        if (!_map.TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var e in v.EnumerateArray())
        {
            if (Str(e) is not { } raw) continue;
            string t = Clean(raw, MaxText);
            if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            if (list.Count >= MaxList) break;
        }
        return list;
    }

    public void Set(string key, string value) => _map[key] = JsonSerializer.SerializeToElement(Clean(value, MaxText));
    public void Set(string key, int value) => _map[key] = JsonSerializer.SerializeToElement(value);
    public void Set(string key, bool value) => _map[key] = JsonSerializer.SerializeToElement(value);
    public void Set(string key, IEnumerable<string> values) =>
        _map[key] = JsonSerializer.SerializeToElement(values.Select(x => Clean(x, MaxText)).Where(x => x.Length > 0).Distinct().Take(MaxList).ToArray());

    /// <summary>Back to the default for this key.</summary>
    public void Remove(string key) => _map.Remove(key);

    /// <summary>The same settings, key by key (the order of keys does not matter).</summary>
    public bool SameAs(DashOptions other)
    {
        if (_map.Count != other._map.Count) return false;
        foreach (var kv in _map)
            if (!other._map.TryGetValue(kv.Key, out var o) || o.GetRawText() != kv.Value.GetRawText()) return false;
        return true;
    }

    /// <summary>Characters the map takes in the file, for the size guard in <see cref="DashItem.Sanitize"/>.</summary>
    public static int SizeOf(Dictionary<string, JsonElement> map)
    {
        int n = 0;
        foreach (var kv in map) n += kv.Key.Length + kv.Value.GetRawText().Length;
        return n;
    }

    /// <summary>
    /// The one rule for a text a widget keeps (a label, an id in a list, a layout's name), applied
    /// where it is typed, where settings are read and where a file is checked, so what is stored
    /// can always be written to a file and read back the same. Out go control characters, halves
    /// of surrogate pairs, the characters that change the direction text is shown in, and the
    /// invisible ones a name could hide a difference in (zero-width space, word joiner, byte order
    /// mark). The rest is composed (NFC), so one name typed two ways is one name, trimmed, and cut
    /// at <paramref name="max"/> without splitting a pair. The zero-width joiner stays: emoji are
    /// built with it.
    /// </summary>
    public static string Clean(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, max + 8));
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (char.IsControl(ch) || Hidden(ch)) continue;
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(ch).Append(s[++i]); }
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            sb.Append(ch);
        }
        string t = sb.ToString();
        try { t = t.Normalize(NormalizationForm.FormC); } catch { }
        t = t.Trim();
        if (t.Length > max) t = (char.IsHighSurrogate(t[max - 1]) ? t[..(max - 1)] : t[..max]).TrimEnd();
        return t;
    }

    /// <summary>Direction marks, overrides and isolates, and the invisible characters that carry no text.</summary>
    private static bool Hidden(char ch) =>
        ch is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069') or '\u200E' or '\u200F' or '\u061C'
           or '\u200B' or '\u2060' or '\uFEFF' or '\uFFFE' or '\uFFFF';

    /// <summary>A key a setting may have: a letter, then letters, digits and underscores, 24 characters at most.</summary>
    public static bool KeyOk(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 24 || !char.IsAsciiLetter(key[0])) return false;
        foreach (char ch in key) if (!char.IsAsciiLetterOrDigit(ch) && ch != '_') return false;
        return true;
    }

    /// <summary>
    /// What may stay of a settings map read from settings.json, value by value: a clean text, a
    /// whole number, yes/no, a list of clean texts. A value of another shape, a text that cannot
    /// be read, a bad key go; the rest stays. Then the limits (<paramref name="maxKeys"/>,
    /// <paramref name="maxChars"/>): what does not fit is left out from the end, never the whole
    /// map. Null when nothing is left. Never throws.
    /// </summary>
    public static Dictionary<string, JsonElement>? Sanitized(Dictionary<string, JsonElement>? map, int maxKeys, int maxChars)
    {
        if (map == null || map.Count == 0) return null;
        var o = new DashOptions();
        int chars = 0;
        foreach (var kv in map)
        {
            if (o._map.Count >= maxKeys) break;
            if (!KeyOk(kv.Key)) continue;
            JsonElement? keep = null;
            try
            {
                switch (kv.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        if (Str(kv.Value) is { } s && Clean(s, MaxText) is { Length: > 0 } t) keep = JsonSerializer.SerializeToElement(t);
                        break;
                    case JsonValueKind.Number:
                        if (kv.Value.TryGetInt32(out int n)) keep = JsonSerializer.SerializeToElement(n);
                        else if (kv.Value.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
                            keep = JsonSerializer.SerializeToElement((int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue));
                        break;
                    case JsonValueKind.True: keep = JsonSerializer.SerializeToElement(true); break;
                    case JsonValueKind.False: keep = JsonSerializer.SerializeToElement(false); break;
                    case JsonValueKind.Array:
                    {
                        var list = new List<string>();
                        foreach (var e in kv.Value.EnumerateArray())
                        {
                            if (Str(e) is not { } es || Clean(es, MaxText) is not { Length: > 0 } et || list.Contains(et)) continue;
                            list.Add(et);
                            if (list.Count >= MaxList) break;
                        }
                        if (list.Count > 0) keep = JsonSerializer.SerializeToElement(list);
                        break;
                    }
                }
            }
            catch { keep = null; }
            if (keep is not { } value) continue;
            int cost = kv.Key.Length + value.GetRawText().Length;
            if (chars + cost > maxChars) continue;
            chars += cost;
            o._map[kv.Key] = value;
        }
        return o.ToMap();
    }

    /// <summary>
    /// Makes the settings fit what one widget keeps before they are saved. A key the widget does
    /// not declare (<paramref name="own"/>: one that came with a style file or from a newer
    /// version) is dropped first, the widget's own settings last, so the user's own choice is
    /// never what a limit takes away.
    /// </summary>
    public void FitTo(int maxKeys, int maxChars, IReadOnlyCollection<string> own)
    {
        bool Fits() => _map.Count <= maxKeys && SizeOf(_map) <= maxChars;
        if (Fits()) return;
        foreach (string key in _map.Keys.Where(k => !own.Contains(k)).Reverse().ToList())
        {
            _map.Remove(key);
            if (Fits()) return;
        }
        foreach (string key in _map.Keys.Reverse().ToList())
        {
            _map.Remove(key);
            if (Fits()) return;
        }
    }
}
