using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace GhostDeck;

/// <summary>
/// Update check + in-app self-update against GitHub Releases. The check queries the public
/// "latest release" endpoint and compares the tag to the running assembly version. Install
/// downloads the release asset next to the running exe as GhostDeck.update.exe, checks that it
/// is signed by the project's release identity (<see cref="Authenticode"/>), starts a small
/// script that waits for this process to die, swaps the files (old exe kept as .bak, put back
/// if the swap fails) and relaunches. Any check failure is swallowed (offline, rate-limited,
/// etc.) so it never disrupts the app.
///
/// AssetName is matched exactly. Releases up to v1.34.x shipped a self-contained
/// "GhostDeck.exe"; from v1.35.0 the build is framework-dependent and the asset is named
/// "GhostDeck-win-x64.exe". That rename is deliberate: it is what stops the updater in an
/// installed v1.34.x from replacing a self-contained copy with a build that needs a .NET
/// runtime the machine may not have. Not finding its asset, the older version leaves the
/// running exe alone and opens the release page instead, so the one-off migration is manual
/// and visible rather than a silently broken install.
/// </summary>
public static class Updater
{
    private const string LatestApi   = "https://api.github.com/repos/wygodad/ghostdeck/releases/latest";
    private const string ListApi     = "https://api.github.com/repos/wygodad/ghostdeck/releases?per_page=";
    public  const string ReleasesUrl = "https://github.com/wygodad/ghostdeck/releases/latest";
    private const string AssetName   = "GhostDeck-win-x64.exe";
    private const string UpdateFile  = "GhostDeck.update.exe";
    // Written by the swap script next to the exe when a step failed and the old version was
    // kept; read and removed by CleanupAfterUpdate on the next start.
    private const string ResultSuffix = ".update-result";

    public readonly record struct Result(Version Version, string Tag, string Url, string AssetUrl, long AssetSize);
    public readonly record struct ReleaseInfo(string Tag, string Name, string Body, string Url, DateTime? Published, long Downloads);

    /// <summary>Why a download produced no file to install.</summary>
    public enum Failure { None, NoAsset, Download, Signature }

    /// <summary>The downloaded, verified file, or which step refused it and why (English, for the log).</summary>
    public readonly record struct Download(string? Path, Failure Why, string Detail);

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
                // how many times the release was downloaded = sum of its assets' counters
                long dl = 0;
                if (r.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                        if (a.TryGetProperty("download_count", out var dc) && dc.TryGetInt64(out var v)) dl += v;
                list.Add(new ReleaseInfo(tag, name, body, url, pub, dl));
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

            // exe asset for the in-app install (size catches a truncated download; the
            // signature check after the download is what judges the content)
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

    // The verified file is held open (read, share-read) from the moment it passed the check
    // until this process exits, so nothing can replace it between the check and the swap. The
    // script waits for this process to die before it moves the file, so the handle never gets
    // in its way.
    private static FileStream? _staged;

    /// <summary>
    /// Downloads the release exe next to the running one as GhostDeck.update.exe and checks its
    /// signature. A file that is not signed by the release identity is deleted and reported as
    /// <see cref="Failure.Signature"/>; the caller must not fall back to running it.
    /// </summary>
    public static async Task<Download> DownloadAsync(Result r, IProgress<int>? progress)
    {
        if (string.IsNullOrEmpty(r.AssetUrl)) return new Download(null, Failure.NoAsset, "the release has no " + AssetName);
        string dest;
        try { dest = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, UpdateFile); }
        catch (Exception ex) { return new Download(null, Failure.Download, ex.Message); }
        DiscardStaged();
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
        }
        catch (Exception ex)
        {
            try { File.Delete(dest); } catch { }
            return new Download(null, Failure.Download, ex.Message);
        }

        // Verified through a handle that keeps everyone else out of the file for writing.
        try
        {
            _staged = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!SignedByUs(dest, out string why))
            {
                DiscardStaged();
                try { File.Delete(dest); } catch { }
                AppLifecycle.Note("update refused: " + why);
                return new Download(null, Failure.Signature, why);
            }
        }
        catch (Exception ex)
        {
            DiscardStaged();
            try { File.Delete(dest); } catch { }
            return new Download(null, Failure.Download, ex.Message);
        }
        return new Download(dest, Failure.None, "");
    }

    private static bool SignedByUs(string path, out string why)
    {
#if DEBUG
        // Debug builds only: a local, unsigned build may stand in for a release while the
        // updater itself is being worked on. Release builds have no such switch.
        if (Environment.GetEnvironmentVariable("GHOSTDECK_UPDATE_UNSIGNED") == "1") { why = ""; return true; }
#endif
        return Authenticode.IsOurs(path, out why);
    }

    /// <summary>Lets go of the staged file (a failed launch leaves it for the next start's cleanup).</summary>
    public static void DiscardStaged()
    {
        try { _staged?.Dispose(); } catch { }
        _staged = null;
    }

    /// <summary>
    /// Kicks off the swap and returns; the caller must exit the app. A hidden cmd script (not the
    /// downloaded exe!) waits for this process to die, swaps the files (old exe kept as .bak) and
    /// relaunches - so the flow works no matter which app version was downloaded. The script's
    /// text carries no path at all: every path travels in the environment, so a folder with a
    /// national letter (cmd reads the script in the console's OEM code page, not UTF-8) or a
    /// percent sign (cmd expands %NAME% even inside quotes) cannot break it. Each step checks
    /// its result: a failed swap puts the old exe back, starts it and leaves a result file that
    /// the next start reports. The script lives next to the exe, so it has exactly the
    /// protection the exe has (under Program Files only an elevated process can write there),
    /// never in the shared %TEMP% that any process of the user could rewrite while the script
    /// is still waiting for this process to exit.
    /// </summary>
    public static bool StartSelfUpdate(string updateExe)
    {
        try
        {
            string target = Environment.ProcessPath!;
            string script = WriteScript(Path.GetDirectoryName(target)!);
            Process.Start(ScriptStart(script, Environment.ProcessId, target, updateExe));
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// How the script is launched: hidden cmd.exe, no AutoRun (/d), no !delayed! expansion
    /// whatever the user's registry says (/V:OFF), and every path - the script's own included -
    /// handed over in the environment. cmd expands %NAME% on its command line as well, so even
    /// the script path is "%GD_SCRIPT%" there: one expansion pass, and what comes out is not
    /// expanded again.
    /// </summary>
    public static ProcessStartInfo ScriptStart(string script, int pid, string target, string updateExe)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            // Doubled quotes on purpose: cmd strips the OUTER pair of a /c string whose inside is
            // not (yet) the name of an existing file, which "%GD_SCRIPT%" is not before expansion.
            // The inner pair survives, is expanded once, and keeps a path with spaces together.
            Arguments = "/d /V:OFF /c \"\"%GD_SCRIPT%\"\"",
        };
        psi.Environment["GD_SCRIPT"] = script;
        psi.Environment["GD_PID"] = pid.ToString();
        psi.Environment["GD_TARGET"] = target;
        psi.Environment["GD_UPDATE"] = updateExe;
        psi.Environment["GD_RESULT"] = target + ResultSuffix;
        return psi;
    }

    /// <summary>The swap script, as text. Paths come from the environment, never from here.</summary>
    public static string ScriptText() =>
        "@echo off\r\n" +
        ":wait\r\n" +
        // /fo csv quotes every field, so find """<pid>""" (escaped quotes) matches the PID exactly.
        // System tools by full path: a "find" earlier in PATH (Git ships one) would end the wait at
        // once. The one-second pause is a ping to loopback, the batch idiom that needs no console
        // (timeout.exe refuses to run when its input is not a console and the loop would spin).
        "\"%SystemRoot%\\System32\\tasklist.exe\" /fi \"PID eq %GD_PID%\" /fo csv 2>nul | \"%SystemRoot%\\System32\\find.exe\" \"\"\"%GD_PID%\"\"\" >nul && (\"%SystemRoot%\\System32\\ping.exe\" -n 2 127.0.0.1 >nul & goto wait)\r\n" +
        "if exist \"%GD_RESULT%\" del /f /q \"%GD_RESULT%\"\r\n" +
        "if exist \"%GD_TARGET%.bak\" del /f /q \"%GD_TARGET%.bak\"\r\n" +
        // 11: the old exe could not be moved aside (still locked) - it is untouched, start it again
        "move /y \"%GD_TARGET%\" \"%GD_TARGET%.bak\" >nul || (>\"%GD_RESULT%\" echo 11 & goto restart)\r\n" +
        // 12: the new exe could not be put in place (deleted, quarantined, locked) - put the old one back
        "move /y \"%GD_UPDATE%\" \"%GD_TARGET%\" >nul || (move /y \"%GD_TARGET%.bak\" \"%GD_TARGET%\" >nul & >\"%GD_RESULT%\" echo 12 & goto restart)\r\n" +
        // 13: the new exe would not start - put the old one back and start that
        "start \"\" \"%GD_TARGET%\" && goto end\r\n" +
        "move /y \"%GD_TARGET%.bak\" \"%GD_TARGET%\" >nul\r\n" +
        ">\"%GD_RESULT%\" echo 13\r\n" +
        ":restart\r\n" +
        "start \"\" \"%GD_TARGET%\"\r\n" +
        ":end\r\n" +
        // the batch idiom for deleting oneself without "The batch file cannot be found" afterwards
        "(goto) 2>nul & del \"%~f0\"\r\n";

    /// <summary>
    /// Writes the script into <paramref name="dir"/> (the exe's folder); a folder this process
    /// cannot write to falls back to %TEMP%, which is no weaker than that exe itself.
    /// </summary>
    public static string WriteScript(string dir)
    {
        string name = $"ghostdeck-update-{Environment.ProcessId}.cmd";
        foreach (var d in new[] { dir, Path.GetTempPath() })
        {
            string path = Path.Combine(d, name);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                // ASCII only, by construction: nothing in the text depends on a code page
                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var w = new StreamWriter(fs, new System.Text.ASCIIEncoding());
                w.Write(ScriptText());
                return path;
            }
            catch { }
        }
        throw new IOException("no writable folder for the update script");
    }

    /// <summary>
    /// Step of the previous in-app update that failed (11 = old exe could not be moved aside,
    /// 12 = new exe could not be put in place, 13 = new exe did not start), 0 when the last
    /// update went through or there was none. In every failed case the old version was kept.
    /// </summary>
    public static int LastFailure { get; private set; }

    /// <summary>
    /// Reads the previous update's result and deletes leftover update files (delayed: the
    /// updater script may still be exiting).
    /// </summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            string res = Environment.ProcessPath! + ResultSuffix;
            if (File.Exists(res))
            {
                if (int.TryParse(File.ReadAllText(res).Trim(), out int code)) LastFailure = code;
                File.Delete(res);
                AppLifecycle.Note($"the previous update stopped at step {LastFailure}; this version was kept");
            }
        }
        catch { }
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
