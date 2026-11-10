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
    /// <summary>Most entries a list getter returns (which profiles, which switches).</summary>
    public const int MaxList = 16;

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
        if (!_map.TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.String) return def;
        return Clean(v.GetString() ?? "", MaxText);
    }

    /// <summary>A whole number within [min, max]; a number written as text is accepted too.</summary>
    public int Int(string key, int def, int min, int max)
    {
        if (!_map.TryGetValue(key, out var v)) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
            return (int)Math.Clamp(Math.Round(d), min, max);
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
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
        if (!_map.TryGetValue(key, out var v) || v.ValueKind != JsonValueKind.String) return def;
        string t = v.GetString() ?? "";
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
            if (e.ValueKind != JsonValueKind.String) continue;
            string t = Clean(e.GetString() ?? "", MaxText);
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

    /// <summary>Control characters out, outer spaces off, cut at <paramref name="max"/> without splitting a surrogate pair.</summary>
    public static string Clean(string s, int max)
    {
        if (s.Length == 0) return s;
        var sb = new StringBuilder(Math.Min(s.Length, max + 8));
        foreach (char ch in s)
            if (!char.IsControl(ch)) sb.Append(ch);
        string t = sb.ToString().Trim();
        if (t.Length > max) t = char.IsHighSurrogate(t[max - 1]) ? t[..(max - 1)] : t[..max];
        return t;
    }
}
