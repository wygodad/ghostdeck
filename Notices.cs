using System.Net.Http;
using System.Text.Json;

namespace MSIProfileSwitcher;

/// <summary>
/// Lightweight one-way announcement channel: fetches a static <c>announcements.json</c> from the
/// project repo (same cadence / opt-out as the update check) and returns notices the running version
/// hasn't seen yet. Read-only, no identifiers sent (a plain GET — same privacy footprint as the update
/// check), any failure is swallowed. Lets us warn users in advance (e.g. the upcoming rename).
///
/// Each entry: { id, severity, minVersion, maxVersion, title, body, url } with optional per-language
/// overrides (title_pl / body_pl …) falling back to the default title/body.
/// </summary>
public static class Notices
{
    private const string FeedUrl = "https://raw.githubusercontent.com/wygodad/msi-profile-switcher/main/announcements.json";

    public readonly record struct Notice(string Id, string Severity, string Title, string Body, string Url);

    public static async Task<List<Notice>> FetchAsync(Version current, IReadOnlyCollection<string> seen)
    {
        var list = new List<Notice>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MSIProfileSwitcher-Notices");

            string json = await http.GetStringAsync(FeedUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

            string lang = Lang.CurrentCode;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string id = Str(e, "id");
                if (id.Length == 0 || seen.Contains(id)) continue;
                if (!InRange(current, Str(e, "minVersion"), Str(e, "maxVersion"))) continue;

                string title = Localized(e, "title", lang);
                string body = Localized(e, "body", lang);
                if (title.Length == 0 && body.Length == 0) continue;
                list.Add(new Notice(id, Str(e, "severity"), title, body, Str(e, "url")));
            }
        }
        catch { }
        return list;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // Prefer a per-language field ("title_pl") then fall back to the default ("title").
    private static string Localized(JsonElement e, string name, string lang)
    {
        var loc = Str(e, $"{name}_{lang}");
        return loc.Length > 0 ? loc : Str(e, name);
    }

    // Show when current is within [min,max]; blank bound = open-ended. Compares major.minor.build.
    private static bool InRange(Version current, string min, string max)
    {
        var cur = Norm(current);
        if (Parse(min) is { } lo && cur < lo) return false;
        if (Parse(max) is { } hi && cur > hi) return false;
        return true;
    }

    private static Version? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string t = s.Trim().TrimStart('v', 'V');
        return Version.TryParse(t, out var v) ? Norm(v) : null;
    }

    private static Version Norm(Version v) => new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
