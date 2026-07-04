using System.Net.Http;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Lightweight update check against GitHub Releases. Read-only: queries the public
/// "latest release" endpoint, compares the tag to the running assembly version, and
/// returns the newer version + a download page URL. No auto-download — the caller
/// just notifies the user and opens the page on click. Any failure is swallowed
/// (offline, rate-limited, etc.) so it never disrupts the app.
/// </summary>
public static class Updater
{
    private const string LatestApi   = "https://api.github.com/repos/wygodad/ghostdeck/releases/latest";
    private const string ListApi     = "https://api.github.com/repos/wygodad/ghostdeck/releases?per_page=";
    public  const string ReleasesUrl = "https://github.com/wygodad/ghostdeck/releases/latest";

    public readonly record struct Result(Version Version, string Tag, string Url);
    public readonly record struct ReleaseInfo(string Tag, string Name, string Body, string Url, DateTime? Published);

    /// <summary>Last <paramref name="count"/> published releases (newest first), for the changelog list.</summary>
    public static async Task<List<ReleaseInfo>> RecentAsync(int count)
    {
        var list = new List<ReleaseInfo>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostDeck-UpdateCheck");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await http.GetStringAsync(ListApi + count).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                string tag  = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string name = r.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
                string body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                string url  = r.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesUrl : ReleasesUrl;
                DateTime? pub = r.TryGetProperty("published_at", out var pa) && pa.TryGetDateTime(out var dt) ? dt : null;
                list.Add(new ReleaseInfo(tag, name, body, url, pub));
                if (list.Count >= count) break;
            }
        }
        catch { }
        return list;
    }

    public static async Task<Result?> CheckAsync(Version current)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostDeck-UpdateCheck");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await http.GetStringAsync(LatestApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return null;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesUrl : ReleasesUrl;

            var latest = ParseTag(tag);
            if (latest == null || Normalize(latest) <= Normalize(current)) return null;
            return new Result(latest, tag, url);
        }
        catch
        {
            return null;
        }
    }

    private static Version? ParseTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        int dash = t.IndexOf('-');               // drop pre-release suffixes like "-beta"
        if (dash >= 0) t = t[..dash];
        return Version.TryParse(t, out var v) ? v : null;
    }

    // Compare on major.minor.build only (ignore unspecified/-1 revision components).
    private static Version Normalize(Version v) =>
        new(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
