using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace GhostDeck;

/// <summary>
/// The .NET Desktop Runtime a GhostDeck release needs, and the one exception to "GhostDeck
/// installs nothing" (TECHNICAL 19.10): when an update needs a newer .NET major than the
/// computer has, the user may choose to install it together with the update. Nothing here
/// runs on its own; the Updates page calls it only after that choice on the .NET card.
///
/// Where the installer comes from: Microsoft's release metadata for the channel
/// (<see cref="MetadataUrl"/>), which names the current Windows Desktop Runtime installer, its
/// download address and its SHA-512. The file is accepted only when the address is an https
/// one on a microsoft.com host, the hash matches, and the Authenticode signature verifies with
/// Microsoft Corporation as the signer. It is downloaded next to the running exe and held open
/// for reading from the check until the installer has finished, for the same reason the update
/// file is (<see cref="Updater"/>): between the check and the run, nothing can swap it.
/// </summary>
public static class DotNetRuntime
{
    /// <summary>Microsoft's release metadata of one .NET channel, e.g. 10.0.</summary>
    public static string MetadataUrl(int major) => $"https://builds.dotnet.microsoft.com/dotnet/release-metadata/{major}.0/releases.json";

    /// <summary>Microsoft's download page of one .NET channel, for "Download .NET myself".</summary>
    public static string DownloadPage(int major) => $"https://dotnet.microsoft.com/download/dotnet/{major}.0";

    private const string InstallerRid = "win-x64";
    private const string InstallerName = "windowsdesktop-runtime-win-x64.exe";
    // Documented switches of the .NET runtime installer: no user interface, no restart.
    private const string InstallerArgs = "/install /quiet /norestart";

    /// <summary>
    /// Whether a .NET Desktop Runtime of <paramref name="major"/> (any patch) is installed for
    /// x64. Read from the folder the runtime host uses (the install location in the registry,
    /// or Program Files\dotnet), the same place the GhostDeck exe will be looked up in.
    /// </summary>
    public static bool HasDesktop(int major)
    {
        try
        {
            foreach (string root in Roots())
            {
                string dir = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
                if (!Directory.Exists(dir)) continue;
                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    int dash = name.IndexOf('-');   // 10.0.0-rc.1 counts as 10
                    if (Version.TryParse(dash >= 0 ? name[..dash] : name, out var v) && v.Major == major) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static IEnumerable<string> Roots()
    {
        string? fromRegistry = null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
            fromRegistry = k?.GetValue("InstallLocation") as string;
        }
        catch { }
        if (!string.IsNullOrWhiteSpace(fromRegistry)) yield return fromRegistry;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
    }

    /// <summary>Which step of <see cref="InstallAsync"/> stopped it.</summary>
    public enum Step { None, Find, Download, Verify, Install, Missing }

    /// <summary>The result; <see cref="Detail"/> is English, for the log.</summary>
    public readonly record struct Outcome(Step FailedAt, string Detail)
    {
        public bool Ok => FailedAt == Step.None;
    }

    /// <summary>What Microsoft's metadata says about the current installer of a channel.</summary>
    public readonly record struct Installer(string Version, string Url, string Sha512);

    /// <summary>
    /// The current Windows Desktop Runtime installer of <paramref name="major"/>, from the
    /// metadata text. Null when the text is not that channel's, or names no usable installer.
    /// </summary>
    public static Installer? ParseInstaller(string json, int major)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("channel-version", out var ch) || ch.GetString() != $"{major}.0") return null;
            if (!root.TryGetProperty("releases", out var releases)) return null;
            // newest first by Microsoft's convention; the first release with a desktop installer wins
            foreach (var rel in releases.EnumerateArray())
            {
                if (!rel.TryGetProperty("windowsdesktop", out var wd) || wd.ValueKind != JsonValueKind.Object) continue;
                string ver = wd.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                if (!wd.TryGetProperty("files", out var files)) continue;
                foreach (var f in files.EnumerateArray())
                {
                    string name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string rid = f.TryGetProperty("rid", out var r) ? r.GetString() ?? "" : "";
                    if (name != InstallerName || rid != InstallerRid) continue;
                    string url = f.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    string hash = f.TryGetProperty("hash", out var h) ? h.GetString() ?? "" : "";
                    if (!TrustedUrl(url) || hash.Length != 128) return null;
                    return new Installer(ver, url, hash);
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>An https address on a microsoft.com host; nothing else is downloaded.</summary>
    public static bool TrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("microsoft.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds, downloads, checks and runs the .NET <paramref name="major"/> Desktop Runtime
    /// installer, then confirms the runtime is there. <paramref name="progress"/> gets the
    /// download percent, <paramref name="stage"/> "verify" and "install" as those steps start.
    /// Any failure leaves the computer as it was (apart from a runtime the installer itself may
    /// have put in place) and deletes the downloaded file.
    /// </summary>
    public static async Task<Outcome> InstallAsync(int major, IProgress<int>? progress, IProgress<string>? stage)
    {
        // 1. which installer: Microsoft's metadata for the channel
        Installer inst;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostDeck-UpdateCheck");
            string json = await http.GetStringAsync(MetadataUrl(major)).ConfigureAwait(false);
            if (ParseInstaller(json, major) is not { } found) return Fail(Step.Find, $"no .NET {major} desktop installer in {MetadataUrl(major)}");
            inst = found;
        }
        catch (Exception ex) { return Fail(Step.Find, ex.Message); }
        AppLifecycle.Note($".NET {major}: installer {inst.Version} from {inst.Url}");

        // 2. download next to the exe (the folder has the exe's own protection, see Updater)
        string dest;
        try { dest = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, $"dotnet-windowsdesktop-runtime-{inst.Version}-win-x64.exe"); }
        catch (Exception ex) { return Fail(Step.Download, ex.Message); }
        var clock = Stopwatch.StartNew();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostDeck-UpdateCheck");
            using var resp = await http.GetAsync(inst.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            int lastPct = -1;
            await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            var buf = new byte[81920]; long done = 0; int n;
            while ((n = await src.ReadAsync(buf).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
                done += n;
                int pct = total > 0 ? (int)(done * 100 / total) : -1;
                if (pct >= 0 && pct != lastPct) { lastPct = pct; progress?.Report(pct); }
            }
            AppLifecycle.Note($".NET {major}: downloaded {done} bytes in {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            TryDelete(dest);
            return Fail(Step.Download, ex.Message);
        }

        // 3. check and run through one read handle that keeps writers out until the installer is done
        stage?.Report("verify");
        FileStream? held = null;
        try
        {
            held = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.Read);
            string sha;
            using (var h = SHA512.Create()) sha = Convert.ToHexString(h.ComputeHash(held));
            if (!sha.Equals(inst.Sha512, StringComparison.OrdinalIgnoreCase))
                return Fail(Step.Verify, "the file's SHA-512 does not match Microsoft's release metadata");
            if (!Authenticode.IsMicrosoft(dest, out string why))
                return Fail(Step.Verify, why);
            AppLifecycle.Note($".NET {major}: installer verified (SHA-512 and {Authenticode.MicrosoftSigner} signature)");

            stage?.Report("install");
            clock.Restart();
            using var p = Process.Start(new ProcessStartInfo(dest, InstallerArgs) { UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return Fail(Step.Install, "the installer did not start");
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20)))
                await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            int code = p.ExitCode;
            AppLifecycle.Note($".NET {major}: installer exit code {code} after {clock.ElapsedMilliseconds} ms");
            // 0 = done, 3010 = done, restart suggested, 1641 = done, restart started by the installer
            if (code is not (0 or 3010 or 1641)) return Fail(Step.Install, $"the installer ended with code {code}");
        }
        catch (Exception ex) { return Fail(Step.Install, ex.Message); }
        finally
        {
            try { held?.Dispose(); } catch { }
            TryDelete(dest);
        }

        // 4. the runtime the update needs must now be there
        return HasDesktop(major) ? new Outcome(Step.None, "") : Fail(Step.Missing, $"the installer finished, but no .NET {major} desktop runtime was found");

        static Outcome Fail(Step s, string why)
        {
            AppLifecycle.Note($".NET install stopped at {s}: {why}");
            return new Outcome(s, why);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
