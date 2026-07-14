using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Update check + in-app self-update against GitHub Releases. The check queries the public
/// "latest release" endpoint and compares the tag to the running assembly version. Install
/// downloads the GhostDeck.exe asset next to the running exe as GhostDeck.update.exe, starts
/// it with <c>--finish-update &lt;pid&gt; &lt;target&gt;</c> and exits; the updater waits for
/// this process to die, swaps the files (old exe kept as .bak) and relaunches. Any check
/// failure is swallowed (offline, rate-limited, etc.) so it never disrupts the app.
/// </summary>
public static class Updater
{
    private const string LatestApi   = "https://api.github.com/repos/wygodad/ghostdeck/releases/latest";
    private const string ListApi     = "https://api.github.com/repos/wygodad/ghostdeck/releases?per_page=";
    public  const string ReleasesUrl = "https://github.com/wygodad/ghostdeck/releases/latest";
    private const string AssetName   = "GhostDeck.exe";
    private const string UpdateFile  = "GhostDeck.update.exe";

    public readonly record struct Result(Version Version, string Tag, string Url, string AssetUrl, long AssetSize);
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

            // exe asset for the in-app install (size doubles as an integrity check after download)
            string assetUrl = ""; long assetSize = 0;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                    if (a.TryGetProperty("name", out var an) && an.GetString() == AssetName)
                    {
                        assetUrl = a.TryGetProperty("browser_download_url", out var au) ? au.GetString() ?? "" : "";
                        assetSize = a.TryGetProperty("size", out var asz) ? asz.GetInt64() : 0;
                        break;
                    }

            var latest = ParseTag(tag);
            if (latest == null || Normalize(latest) <= Normalize(current)) return null;
            return new Result(latest, tag, url, assetUrl, assetSize);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the release exe next to the running one as GhostDeck.update.exe.
    /// Returns the path, or null on failure (caller falls back to the release page).
    /// </summary>
    public static async Task<string?> DownloadAsync(Result r, IProgress<int>? progress)
    {
        if (string.IsNullOrEmpty(r.AssetUrl)) return null;
        string dest;
        try { dest = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, UpdateFile); }
        catch { return null; }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostDeck-UpdateCheck");
            using var resp = await http.GetAsync(r.AssetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? r.AssetSize;
            await using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report((int)(done * 100 / total));
                }
                if (r.AssetSize > 0 && done != r.AssetSize) throw new IOException("size mismatch");
            }
            return dest;
        }
        catch
        {
            try { File.Delete(dest); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Kicks off the swap and returns; the caller must exit the app. A hidden cmd script (not the
    /// downloaded exe!) waits for this process to die, swaps the files (old exe kept as .bak) and
    /// relaunches - so the flow works no matter which app version was downloaded.
    /// </summary>
    public static bool StartSelfUpdate(string updateExe)
    {
        try
        {
            string target = Environment.ProcessPath!;
            string pid = Environment.ProcessId.ToString();
            string script = Path.Combine(Path.GetTempPath(), "ghostdeck-update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                ":wait\r\n" +
                // /fo csv quotes every field, so find """<pid>""" (escaped quotes) matches the PID exactly
                $"tasklist /fi \"PID eq {pid}\" /fo csv 2>nul | find \"\"\"{pid}\"\"\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"if exist \"{target}.bak\" del /f /q \"{target}.bak\"\r\n" +
                $"move /y \"{target}\" \"{target}.bak\" >nul\r\n" +
                $"move /y \"{updateExe}\" \"{target}\" >nul\r\n" +
                $"start \"\" \"{target}\"\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Deletes leftover update files (delayed: the updater exe may still be exiting).</summary>
    public static void CleanupAfterUpdate()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000).ConfigureAwait(false);
            try
            {
                string exe = Environment.ProcessPath!;
                foreach (var f in new[] { Path.Combine(Path.GetDirectoryName(exe)!, UpdateFile), exe + ".bak" })
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
            catch { }
        });
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
