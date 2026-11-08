using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GhostDeck;

/// <summary>
/// Anonymous weekly usage statistics (plan §8.3, DOC/GhostDeck-statystyki-ochrona.md). Off until
/// the user switches it on. When on: one report per ISO week, posted with a small proof of work;
/// the signed weekly summary (<c>stats.signed.json</c>) is fetched once a day, verified with the
/// public key below and kept as a file in the settings folder. Nothing in a report identifies a
/// person or a computer: no identifier, no names, no paths, no serial numbers, no exact times.
/// </summary>
public static class Stats
{
    /// <summary>A newer verified summary arrived (the catalogue repaints its badges).</summary>
    public static event Action? SummaryChanged;
    public static void RaiseSummaryChanged() => SummaryChanged?.Invoke();

    public const string ReportUrl = "https://stats.ghostdeck.dev/report.php";
    public const string SummaryUrl = "https://stats.ghostdeck.dev/stats.signed.json";
    /// <summary>ECDSA P-256 public key of the statistics server (repo ghostdeck-stats, docs/stats-public-key.pem). Not the model-database key.</summary>
    private const string PublicKeySpki = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcnhSwX5TcTLFwSe2+Bbbw+AQQJtHeHvjCkem72+LCp4hqR3hbFyTXIgJTaXURA8RIvUa993ohH5X30vG96plGw==";
    public const int DefaultPowBits = 20;
    public const int ReportVersion = 1;

    public static string SummaryPath => Path.Combine(AppSettings.Dir, "stats.signed.json");

    // ---------------- weeks ----------------

    /// <summary>ISO week of a moment, "2026-W45", the same format the server uses.</summary>
    public static string IsoWeek(DateTime utc)
    {
        int year = ISOWeek.GetYear(utc);
        int week = ISOWeek.GetWeekOfYear(utc);
        return $"{year:D4}-W{week:D2}";
    }

    public static string CurrentWeek() => IsoWeek(DateTime.UtcNow);

    // ---------------- weekly counters ----------------
    // Kept in the settings so a restart inside the week loses nothing; cleared after a report.
    // Counted only while statistics are on (nothing is collected "just in case").

    /// <summary>A deliberate profile change; <paramref name="from"/> gets the time since it was set.</summary>
    public static void CountSwitch(AppSettings s, ProfileId from, ProfileId to, DateTime since)
    {
        if (!s.StatsEnabled) return;
        RollWeek(s);
        if (from != to) s.StatsSwitches = Math.Min(99999, s.StatsSwitches + 1);
        AddUsage(s, from, since);
    }

    // the moment up to which the running profile's time was already counted (a report or a switch)
    private static DateTime _usageMark = DateTime.MinValue;

    /// <summary>Time spent in <paramref name="profile"/> since <paramref name="since"/> (or since it was last counted), added to its share.</summary>
    public static void AddUsage(AppSettings s, ProfileId profile, DateTime since)
    {
        if (!s.StatsEnabled) return;
        RollWeek(s);
        if (_usageMark > since) since = _usageMark;
        double sec = (DateTime.Now - since).TotalSeconds;
        _usageMark = DateTime.Now;
        if (sec <= 0 || sec > 14 * 86400) return;
        string key = profile.ToString().ToLowerInvariant();
        s.StatsUsage[key] = s.StatsUsage.GetValueOrDefault(key) + (long)sec;
    }

    /// <summary>One error of a kind (a short camelCase key, never a message): "profileApply", "statsSend", ...</summary>
    public static void CountError(AppSettings s, string kind)
    {
        if (!s.StatsEnabled || !Regex.IsMatch(kind, "^[A-Za-z0-9_]{1,24}$")) return;
        RollWeek(s);
        s.StatsErrors[kind] = Math.Min(99999, s.StatsErrors.GetValueOrDefault(kind) + 1);
    }

    /// <summary>A feature used this week (Fan Boost switched on, a scene run, ...).</summary>
    public static void CountUse(AppSettings s, string feature)
    {
        if (!s.StatsEnabled || !Regex.IsMatch(feature, "^[A-Za-z0-9_]{1,24}$")) return;
        RollWeek(s);
        if (!s.StatsUsed.Contains(feature)) s.StatsUsed.Add(feature);
    }

    /// <summary>Counters belong to one week; a new week starts them over (the old week's report has gone out or was missed).</summary>
    private static void RollWeek(AppSettings s)
    {
        string w = CurrentWeek();
        if (s.StatsCountersWeek == w) return;
        ResetCounters(s);
        s.StatsCountersWeek = w;
    }

    public static void ResetCounters(AppSettings s)
    {
        s.StatsSwitches = 0;
        s.StatsUsage.Clear();
        s.StatsErrors.Clear();
        s.StatsUsed.Clear();
        _usageMark = DateTime.Now;
    }

    // ---------------- the report ----------------

    /// <summary>
    /// The report as the server expects it (and validates it: every field whitelisted, bounded,
    /// anchored). Built from the settings and a few facts about the machine; the same function
    /// feeds "show what will be sent", so what the user reads is what goes out.
    /// </summary>
    public static JsonObject BuildReport(AppSettings s, string firmware, string appVersion, ProfileId current, DateTime currentSince)
    {
        RollWeek(s);
        if (s.StatsEnabled) AddUsage(s, current, currentSince);   // the running profile up to this moment

        var features = new JsonObject
        {
            ["overlay"] = s.OverlayEnabled ? 1 : 0,
            ["crosshair"] = s.CrosshairEnabled ? 1 : 0,
            ["hotkeys"] = s.HotkeysEnabled && s.Hotkeys.Values.Any(h => h.IsSet && h.Enabled) ? 1 : 0,
            ["automation"] = s.AutoSwitchEnabled ? 1 : 0,
            ["chargeLimit"] = s.ChargeLimit > 0 ? 1 : 0,
            ["customCurve"] = s.ProfileCurves.Count > 0 ? 1 : 0,
            ["tempAlerts"] = s.TempAlertEnabled || s.SsdAlertEnabled ? 1 : 0,
            ["refreshSwitch"] = s.RefreshSwitchEnabled ? 1 : 0,
            ["powerModeSync"] = s.PowerModeSync ? 1 : 0,
            ["trueColor"] = s.TrueColorEnabled ? 1 : 0,
            ["sessionPopup"] = s.SessionPopupEnabled ? 1 : 0,
            ["restoreOnResume"] = s.RestoreProfileOnResume ? 1 : 0,
            ["trayWheel"] = s.TrayWheelMode != (int)GhostDeck.TrayWheelMode.None ? 1 : 0,
            ["protocolLinks"] = s.ProtocolLinks ? 1 : 0,
            ["scenes"] = s.Scenes.Count > 0 ? 1 : 0,
            ["fanBoost"] = s.StatsUsed.Contains("fanBoost") ? 1 : 0,
            ["apex"] = s.StatsUsed.Contains("apex") ? 1 : 0,
        };

        var widgets = new JsonArray();
        foreach (var it in s.Dashboard.Take(40))
            if (Regex.IsMatch(it.Type, "^[a-z]{1,16}\\.[A-Za-z0-9_]{1,40}$"))
                widgets.Add(new JsonObject { ["t"] = it.Type, ["s"] = $"{Math.Clamp(it.W, 1, 6)}x{Math.Clamp(it.H, 1, 4)}" });

        var ratings = new JsonObject();
        foreach (var kv in s.WidgetRatings.Take(40))
            if (kv.Value >= 1 && kv.Value <= 5 && Regex.IsMatch(kv.Key, "^[a-z]{1,16}\\.[A-Za-z0-9_]{1,40}$")) ratings[kv.Key] = kv.Value;

        var errors = new JsonObject();
        foreach (var kv in s.StatsErrors.Take(20)) errors[kv.Key] = Math.Clamp(kv.Value, 0, 99999);

        // profile shares in whole percents of the counted time; nothing when nothing was counted
        JsonObject? usage = null;
        long total = s.StatsUsage.Values.Sum();
        if (total > 0)
        {
            usage = new JsonObject();
            foreach (var kv in s.StatsUsage.OrderByDescending(k => k.Value).Take(8))
                if (Regex.IsMatch(kv.Key, "^[a-z]{1,16}$")) usage[kv.Key] = (int)Math.Round(100.0 * kv.Value / total);
        }

        var report = new JsonObject
        {
            ["v"] = ReportVersion,
            ["week"] = CurrentWeek(),
            ["app"] = AppVersionForReport(appVersion),
            ["lang"] = s.Language,
            ["model"] = ModelPrefix(firmware),
            ["win"] = WindowsVersion(),
            ["dpi"] = SystemDpiPercent(),
            ["mode"] = Ec.OnBackupPath ? "EC" : "WMI",
            ["start"] = s.StartTab == 1 ? "dashboard" : "scenarios",
            ["features"] = features,
            ["scenes"] = Math.Clamp(s.Scenes.Count, 0, 999),
            ["widgets"] = widgets,
            ["ratings"] = ratings,
            ["addons"] = new JsonArray(),
            ["errors"] = errors,
            ["bios"] = Clean(SafeBios(), 24, "A-Za-z0-9._-"),
            ["screen"] = ScreenText(),
            ["switches"] = Math.Clamp(s.StatsSwitches, 0, 99999),
            ["autostart"] = s.Autostart ? 1 : 0,
            ["gpu"] = Clean(GpuName(), 48),
            ["cpu"] = Clean(SysInfo.CpuShortName(), 48),
            ["salt"] = RandomHex(16),
        };
        if (usage != null) report["usage"] = usage;
        // optional strings the server would refuse when empty are simply left out
        foreach (var k in new[] { "model", "win", "bios", "screen", "gpu", "cpu" })
            if (report[k] is JsonValue v && v.GetValue<string>() == "") report.Remove(k);
        return report;
    }

    /// <summary>The exact bytes that go on the wire (compact JSON); the proof of work covers them.</summary>
    public static byte[] Serialize(JsonObject report) =>
        Encoding.UTF8.GetBytes(report.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

    /// <summary>Readable form for the "show what will be sent" card: one top-level field per line, values compact.</summary>
    public static string Pretty(JsonObject report)
    {
        var opt = new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var sb = new StringBuilder();
        foreach (var kv in report)
            sb.Append(kv.Key).Append(": ").Append((kv.Value?.ToJsonString(opt) ?? "null").Replace(",", ", ").Replace(",  ", ", ")).Append('\n');   // a space after commas: the card wraps at spaces
        return sb.ToString().TrimEnd('\n');
    }

    // ---------------- proof of work ----------------

    /// <summary>
    /// Finds a nonce such that SHA-256(body + "\n" + nonce) starts with <paramref name="bits"/> zero
    /// bits: about 2^bits hashes on average, well under a second at 20 bits, done once a week on a
    /// worker thread. Checking it costs the server one hash. No secret anywhere, nothing to extract.
    /// </summary>
    public static string SolvePow(byte[] body, int bits, CancellationToken ct)
    {
        bits = Math.Clamp(bits, 1, 40);
        var prefix = new byte[body.Length + 1];
        Buffer.BlockCopy(body, 0, prefix, 0, body.Length);
        prefix[body.Length] = (byte)'\n';
        using var sha = SHA256.Create();
        Span<byte> hash = stackalloc byte[32];
        for (long n = 0; ; n++)
        {
            if ((n & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            byte[] nonce = Encoding.ASCII.GetBytes(n.ToString(CultureInfo.InvariantCulture));
            sha.TransformBlock(prefix, 0, prefix.Length, null, 0);
            sha.TransformFinalBlock(nonce, 0, nonce.Length);
            if (LeadingZeroBits(sha.Hash!) >= bits) return n.ToString(CultureInfo.InvariantCulture);
            sha.Initialize();
        }
    }

    internal static int LeadingZeroBits(ReadOnlySpan<byte> h)
    {
        int n = 0;
        foreach (byte b in h)
        {
            if (b == 0) { n += 8; continue; }
            for (int i = 7; i >= 0 && (b & (1 << i)) == 0; i--) n++;
            break;
        }
        return n;
    }

    // ---------------- sending ----------------

    public enum SendResult { Sent, Duplicate, Rejected, Unreachable }

    /// <summary>
    /// Posts one report. 204 = counted (also for a repeated identical body: the server stores it
    /// once, so a retry after a lost answer is safe). 400 = the server refused the shape: our bug,
    /// logged, not retried this week. Anything else (no network, 429, 5xx) = try again next start.
    /// </summary>
    public static async Task<(SendResult result, string info)> SendAsync(byte[] body, int powBits, CancellationToken ct)
    {
        try
        {
            string nonce = await Task.Run(() => SolvePow(body, powBits, ct), ct).ConfigureAwait(false);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostDeck-Stats");
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, ReportUrl) { Content = content };
            req.Headers.TryAddWithoutValidation("X-GhostDeck-Pow", nonce);
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            int code = (int)resp.StatusCode;
            string text = (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
            if (code == 204) return (SendResult.Sent, "");
            if (code == 400) return (SendResult.Rejected, text);
            return (SendResult.Unreachable, $"HTTP {code}");
        }
        catch (OperationCanceledException) { return (SendResult.Unreachable, "cancelled"); }
        catch (Exception ex) { return (SendResult.Unreachable, ex.GetType().Name); }
    }

    // ---------------- the signed summary ----------------

    public sealed class WidgetStat
    {
        public double Share;          // 0..1 of dashboards that have the widget
        public double? Trend;         // change of the share against the previous week
        public double? Rating;        // 1..5
        public int Votes;
        public Dictionary<string, double> Sizes = new();
    }

    public sealed class Summary
    {
        public string Week = "";
        public DateTime Generated;
        public int ReportEvery = 7;
        public int PowBits = DefaultPowBits;
        public int ActiveWeek, ActivePrevWeek, TypicalWidgets;
        public Dictionary<string, WidgetStat> Widgets = new();
        public List<string> Top = new();
        public string? Rising;
        public List<(string week, int active)> History = new();

        public double? ShareOf(string type) => Widgets.TryGetValue(type, out var w) ? w.Share : null;
    }

    /// <summary>Verifies a downloaded container (payload + DER signature, base64) and parses the payload. Null = not trusted.</summary>
    public static Summary? Parse(byte[] containerBytes)
    {
        try
        {
            var c = JsonNode.Parse(containerBytes) as JsonObject;
            if (c == null || (int?)c["v"] != 1) return null;
            byte[] payload = Convert.FromBase64String((string?)c["payload"] ?? "");
            byte[] sig = Convert.FromBase64String((string?)c["sig"] ?? "");
            if (payload.Length == 0 || payload.Length > 512 * 1024 || !VerifySignature(payload, sig)) return null;
            var p = JsonNode.Parse(payload) as JsonObject;
            if (p == null || (int?)p["v"] != 1) return null;
            var s = new Summary
            {
                Week = (string?)p["week"] ?? "",
                ReportEvery = Math.Clamp((int?)p["reportEvery"] ?? 7, 1, 30),
                PowBits = Math.Clamp((int?)p["powBits"] ?? DefaultPowBits, 1, 32),
                ActiveWeek = (int?)p["active"]?["week"] ?? 0,
                ActivePrevWeek = (int?)p["active"]?["prevWeek"] ?? 0,
                TypicalWidgets = (int?)p["typicalWidgets"] ?? 0,
                Rising = (string?)p["rising"],
            };
            DateTime.TryParse((string?)p["generated"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out s.Generated);
            if (p["widgets"] is JsonObject ws)
                foreach (var kv in ws)
                {
                    if (kv.Value is not JsonObject w) continue;
                    var st = new WidgetStat
                    {
                        Share = (double?)w["share"] ?? 0,
                        Trend = (double?)w["trend"],
                        Rating = (double?)w["rating"],
                        Votes = (int?)w["votes"] ?? 0,
                    };
                    if (w["sizes"] is JsonObject sz) foreach (var z in sz) st.Sizes[z.Key] = (double?)z.Value ?? 0;
                    s.Widgets[kv.Key] = st;
                }
            if (p["top"] is JsonArray top) foreach (var t in top) if ((string?)t is { } ts) s.Top.Add(ts);
            if (p["history"] is JsonArray hist) foreach (var h in hist) s.History.Add(((string?)h?["week"] ?? "", (int?)h?["active"] ?? 0));
            return s;
        }
        catch { return null; }
    }

    public static bool VerifySignature(byte[] data, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeySpki), out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch { return false; }
    }

    private static Summary? _current;
    private static bool _currentLoaded;

    /// <summary>
    /// The summary in effect: the verified file read once, replaced by a fresh download
    /// (<see cref="SummaryChanged"/> follows). The preview tool sets it to a sample of its own.
    /// </summary>
    public static Summary? Current
    {
        get { if (!_currentLoaded) { _current = LoadCached(); _currentLoaded = true; } return _current; }
        set { _current = value; _currentLoaded = true; }
    }

    /// <summary>The last verified summary kept on disk, or null. Verified again on every load.</summary>
    public static Summary? LoadCached()
    {
        try { return File.Exists(SummaryPath) ? Parse(File.ReadAllBytes(SummaryPath)) : null; }
        catch { return null; }
    }

    /// <summary>
    /// Downloads the summary and replaces the cached file only when the new one verifies; a bad
    /// or unreachable download leaves the previous good file in place. Returns the summary now in
    /// effect (new or cached) and whether anything new arrived.
    /// </summary>
    public static async Task<(Summary? summary, bool fresh)> FetchSummaryAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GhostDeck-Stats");
            byte[] data = await http.GetByteArrayAsync(SummaryUrl, ct).ConfigureAwait(false);
            var s = Parse(data);
            if (s != null)
            {
                Directory.CreateDirectory(AppSettings.Dir);
                string tmp = SummaryPath + ".tmp";
                File.WriteAllBytes(tmp, data);
                File.Move(tmp, SummaryPath, true);
                return (s, true);
            }
        }
        catch { }
        return (LoadCached(), false);
    }

    // ---------------- facts about the machine (no identifiers) ----------------

    /// <summary>"17S1EMS1.114" -> "17S1": the board family, the same prefix the model database keys on.</summary>
    internal static string ModelPrefix(string firmware)
    {
        var m = Regex.Match(firmware ?? "", "^[A-Za-z0-9]{2,8}");
        if (!m.Success) return "";
        // ASCII only (the regex saw to that): a..z folded by hand, so no culture (Turkish i) can touch a firmware id
        var chars = m.Value.ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (chars[i] >= 'a' && chars[i] <= 'z') chars[i] = (char)(chars[i] - 32);
        string p = new string(chars);
        return p.Length > 4 ? p[..4] : p;
    }

    /// <summary>"1.44.0" from the assembly version (three parts, the build number dropped).</summary>
    internal static string AppVersionForReport(string v)
    {
        var m = Regex.Match(v ?? "", @"^(\d{1,3})\.(\d{1,3})(?:\.(\d{1,4}))?");
        return m.Success ? m.Groups[1].Value + "." + m.Groups[2].Value + (m.Groups[3].Success ? "." + m.Groups[3].Value : "") : "0.0";
    }

    /// <summary>"11 24H2": the marketing version and the feature update, from the registry; never the build or the edition key.</summary>
    internal static string WindowsVersion()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string disp = k?.GetValue("DisplayVersion") as string ?? k?.GetValue("ReleaseId") as string ?? "";
            int build = int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out int b) ? b : 0;
            string major = build >= 22000 ? "11" : "10";
            return Clean((major + " " + disp).Trim(), 16, "A-Za-z0-9 ");
        }
        catch { return ""; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForSystem();

    /// <summary>The system scale in percent (the primary monitor's at log-on): a fact about the machine, not a control's DPI.</summary>
    internal static int SystemDpiPercent()
    {
        try { return Math.Clamp((int)Math.Round(GetDpiForSystem() / 96.0 * 100), 50, 600); }
        catch { return 100; }
    }

    /// <summary>"2560x1440, 1": the primary screen's size in pixels and how many screens there are.</summary>
    internal static string ScreenText()
    {
        try
        {
            var b = Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
            if (b.Width <= 0) return "";
            return Clean($"{b.Width}x{b.Height}, {Screen.AllScreens.Length}", 24, "0-9x, ");
        }
        catch { return ""; }
    }

    private static string SafeBios()
    {
        try { return EcBlocks.BiosVersion(); } catch { return ""; }
    }

    /// <summary>The discrete GPU's marketing name without the vendor prefix ("RTX 4070 Laptop GPU").</summary>
    internal static string GpuName()
    {
        try
        {
            var names = new List<string>();
            using var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject o in s.Get()) using (o) if (o["Name"]?.ToString() is { } n && n.Length > 0) names.Add(n.Trim());
            string pick = names.FirstOrDefault(n => n.Contains("RTX") || n.Contains("GTX") || n.Contains("Radeon RX") || n.Contains("Arc"))
                ?? names.FirstOrDefault() ?? "";
            foreach (var p in new[] { "NVIDIA GeForce ", "NVIDIA ", "AMD ", "Intel(R) ", "Intel " })
                if (pick.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { pick = pick[p.Length..]; break; }
            return pick;
        }
        catch { return ""; }
    }

    /// <summary>Trims, drops control characters (and anything outside <paramref name="allowed"/> when given), cuts to <paramref name="max"/>.</summary>
    internal static string Clean(string? v, int max, string? allowed = null)
    {
        v = (v ?? "").Trim();
        v = Regex.Replace(v, allowed == null ? "[\\x00-\\x1F\\x7F]" : "[^" + allowed + "]", "");
        v = Regex.Replace(v, " {2,}", " ").Trim();
        return v.Length > max ? v[..max].Trim() : v;
    }

    private static string RandomHex(int chars) => Convert.ToHexString(RandomNumberGenerator.GetBytes(chars / 2)).ToLowerInvariant();
}
