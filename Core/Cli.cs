using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GhostDeck;

public enum CliKind { Profile, Cycle, FanBoost, Overlay, Curve, Panic, Status, Help, Kbd, Webcam, Scene, FnSwap, Brightness, WinLock, Refresh, Charge, Travel, Diag, HdrSwitch, Touchpad, DumpModels, VerifyModels, DumpSupportedMd }

public sealed record CliCommand(CliKind Kind, string Arg = "", string Arg2 = "");

/// <summary>
/// Command-line interface (GhostDeck.exe --profile Silent, --status, ...) for scripts,
/// Task Scheduler and Stream Deck. When the tray app is running the command is forwarded
/// over a named pipe and executed by the live instance (same gates as the UI); otherwise
/// a one-shot mode talks to the EC directly and exits. Output is English by design
/// (machine-readable; scripts should not depend on the UI language).
/// Exit codes: 0 = OK, 1 = failed, 2 = bad usage.
/// </summary>
public static class Cli
{
    public const string PipeName = "GhostDeck_Cli";

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    private const string Usage = """
        GhostDeck command line:
          GhostDeck.exe --profile <Silent|Balanced|Extreme|SuperBattery>
          GhostDeck.exe --cycle                 switch to the next profile
          GhostDeck.exe --fanboost <on|off> [seconds]   full fan speed; the optional auto-off (10-7200 s) needs the app running
          GhostDeck.exe --overlay <on|off>      gaming overlay (needs the app running)
          GhostDeck.exe --curve <preset|auto>   apply a saved fan-curve preset (auto = stock fans)
          GhostDeck.exe --refresh <hz|max>      panel refresh rate (works on any machine)
          GhostDeck.exe --charge <60|80|100|off>   battery charge limit (off = stop managing)
          GhostDeck.exe --travel <days|off>     charge to 100% for a trip; the previous limit returns after <days> (1-90)
          GhostDeck.exe --kbd <off|low|mid|high|0-3>   keyboard-backlight level (supported models)
          GhostDeck.exe --webcam <on|off>       EC-level webcam switch (same as the Fn camera key)
          GhostDeck.exe --fnswap <left|right>   which side the Fn key is on (EC-level Fn/Win swap)
          GhostDeck.exe --brightness <0-100>    internal-panel brightness (works on any machine)
          GhostDeck.exe --hdr <on|off>          HDR / advanced color (HDR-capable displays)
          GhostDeck.exe --touchpad <on|off>     enable/disable the precision touchpad (device level)
          GhostDeck.exe --winlock <on|off>      block both Windows keys (needs the app running)
          GhostDeck.exe --scene "<name>"        apply a saved scene (needs the app running)
          GhostDeck.exe --panic                 safe state: Fan Boost off, Balanced, fans auto
          GhostDeck.exe --status                print the current state as JSON
          GhostDeck.exe --diag [path.zip]       save the diagnostic zip (read-only, runs locally)
        Requires administrator rights (EC access), like the app itself.
        """;

    public static CliCommand? Parse(string[] a)
    {
        if (a.Length == 0) return null;
        string Arg1() => a.Length > 1 ? a[1] : "";
        switch (a[0].ToLowerInvariant())
        {
            case "--profile":
                return Enum.TryParse<ProfileId>(Arg1(), true, out var id) ? new CliCommand(CliKind.Profile, id.ToString()) : null;
            case "--cycle": return new CliCommand(CliKind.Cycle);
            case "--fanboost":
            {
                string fb = Arg1().ToLowerInvariant();
                if (fb is not ("on" or "off")) return null;
                string secs = "";
                if (a.Length > 2)   // optional auto-off, only meaningful with "on"
                {
                    if (fb != "on" || !int.TryParse(a[2], out int s2) || s2 is < 10 or > 7200) return null;
                    secs = s2.ToString();
                }
                return new CliCommand(CliKind.FanBoost, fb, secs);
            }
            case "--refresh":
                if (Arg1().Equals("max", StringComparison.OrdinalIgnoreCase)) return new CliCommand(CliKind.Refresh, "max");
                return int.TryParse(Arg1(), out int hz) && hz is > 0 and <= 1000 ? new CliCommand(CliKind.Refresh, hz.ToString()) : null;
            case "--charge":
                if (Arg1().ToLowerInvariant() == "off") return new CliCommand(CliKind.Charge, "0");
                // any threshold the register accepts (20-100); 60/80/100 are the vendor-verified ones
                return int.TryParse(Arg1(), out int ch) && AppSettings.ChargeManaged(ch) ? new CliCommand(CliKind.Charge, ch.ToString()) : null;
            case "--travel":
                if (Arg1().ToLowerInvariant() == "off") return new CliCommand(CliKind.Travel, "0");
                return int.TryParse(Arg1(), out int td) && td is >= 1 and <= 90 ? new CliCommand(CliKind.Travel, td.ToString()) : null;
            case "--diag":
                return new CliCommand(CliKind.Diag, Arg1());   // optional output path
            case "--dump-models":
                // hidden maintainer/CI command: write the COMPILED model tables as canonical
                // JSON (data/models.json). Byte-exact file output - never via the console.
                return new CliCommand(CliKind.DumpModels, Arg1());
            case "--verify-models":
                // hidden CI command: parse the given file, re-dump it (round-trip must be
                // byte-identical) and byte-compare against the compiled tables' dump.
                return Arg1().Length > 0 ? new CliCommand(CliKind.VerifyModels, Arg1()) : null;
            case "--dump-supported-md":
                // hidden maintainer/CI command: write docs/SUPPORTED_MODELS.md from the
                // compiled tables. Byte-exact file output - never via the console.
                return new CliCommand(CliKind.DumpSupportedMd, Arg1());
            case "--overlay":
                return Arg1().ToLowerInvariant() is "on" or "off" ? new CliCommand(CliKind.Overlay, Arg1().ToLowerInvariant()) : null;
            case "--curve":
                return Arg1().Length > 0 ? new CliCommand(CliKind.Curve, Arg1()) : null;
            case "--kbd":
                return ParseKbdLevel(Arg1()) >= 0 ? new CliCommand(CliKind.Kbd, Arg1().ToLowerInvariant()) : null;
            case "--webcam":
                return Arg1().ToLowerInvariant() is "on" or "off" ? new CliCommand(CliKind.Webcam, Arg1().ToLowerInvariant()) : null;
            case "--fnswap":
                return Arg1().ToLowerInvariant() is "left" or "right" ? new CliCommand(CliKind.FnSwap, Arg1().ToLowerInvariant()) : null;
            case "--brightness":
                return int.TryParse(Arg1(), out int bri) && bri is >= 0 and <= 100 ? new CliCommand(CliKind.Brightness, bri.ToString()) : null;
            case "--winlock":
                return Arg1().ToLowerInvariant() is "on" or "off" ? new CliCommand(CliKind.WinLock, Arg1().ToLowerInvariant()) : null;
            case "--hdr":
                return Arg1().ToLowerInvariant() is "on" or "off" ? new CliCommand(CliKind.HdrSwitch, Arg1().ToLowerInvariant()) : null;
            case "--touchpad":
                return Arg1().ToLowerInvariant() is "on" or "off" ? new CliCommand(CliKind.Touchpad, Arg1().ToLowerInvariant()) : null;
            case "--scene":
                return Arg1().Length > 0 ? new CliCommand(CliKind.Scene, Arg1()) : null;
            case "--panic": return new CliCommand(CliKind.Panic);
            case "--status": return new CliCommand(CliKind.Status);
            case "--help" or "-h" or "/?": return new CliCommand(CliKind.Help);
            default: return null;
        }
    }

    /// <summary>(#26) "off"/"low"/"mid"/"high" or "0".."3" -> level, -1 = invalid.</summary>
    public static int ParseKbdLevel(string arg) => arg.ToLowerInvariant() switch
    {
        "off" or "0" => 0, "low" or "1" => 1, "mid" or "2" => 2, "high" or "3" => 3, _ => -1,
    };

    /// <summary>Entry point for any launch with arguments. Returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        AttachConsole(-1);   // write into the parent console when started from a terminal
        var cmd = Parse(args);
        if (cmd == null) { Console.WriteLine(Usage); return 2; }
        if (cmd.Kind == CliKind.Help) { Console.WriteLine(Usage); return 0; }

        // The diagnostic zip always runs locally: it writes a file in the CALLER's directory
        // and only does read-only collection, so there is nothing the live instance adds.
        if (cmd.Kind == CliKind.Diag) return RunOneShot(cmd);

        // The model-table dump must come from THIS exe's compiled tables, never the pipe.
        if (cmd.Kind == CliKind.DumpModels)
        {
            string path = cmd.Arg.Length > 0 ? cmd.Arg : "models.json";
            File.WriteAllBytes(path, ModelDb.Dump());
            Console.WriteLine("model tables dumped: " + Path.GetFullPath(path));
            return 0;
        }
        if (cmd.Kind == CliKind.DumpSupportedMd)
        {
            string path = cmd.Arg.Length > 0 ? cmd.Arg : "SUPPORTED_MODELS.md";
            File.WriteAllBytes(path, SupportedModelsDoc.Generate());
            Console.WriteLine("supported-models doc dumped: " + Path.GetFullPath(path));
            return 0;
        }
        if (cmd.Kind == CliKind.VerifyModels)
        {
            byte[] file = File.ReadAllBytes(cmd.Arg);
            if (!ModelDb.TryParse(file, out var parsed, out string err) || parsed == null)
            {
                Console.WriteLine("PARSE FAILED: " + err);
                return 1;
            }
            bool roundtrip = ModelDb.DumpParsed(parsed).AsSpan().SequenceEqual(file);
            bool matchesCode = ModelDb.Dump().AsSpan().SequenceEqual(file);
            Console.WriteLine($"parse: OK ({parsed.Models.Length} models, dataVersion {parsed.DataVersion})");
            Console.WriteLine("round-trip byte-identical: " + (roundtrip ? "OK" : "FAILED"));
            Console.WriteLine("matches the compiled tables: " + (matchesCode ? "OK" : "FAILED"));
            return roundtrip && matchesCode ? 0 : 1;
        }

        // A running instance owns the tray/overlay state - forward to it over the pipe.
        if (TrySendToRunning(args, out string resp, out int code))
        {
            if (resp.Length > 0) Console.WriteLine(resp);
            return code;
        }
        return RunOneShot(cmd);
    }

    private static bool TrySendToRunning(string[] args, out string resp, out int code)
    {
        resp = ""; code = 1;
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(700);
            using var w = new StreamWriter(pipe) { AutoFlush = true };
            using var r = new StreamReader(pipe);
            w.WriteLine(string.Join('\t', args));
            string? line = r.ReadLine();
            if (line == null) return false;
            int bar = line.IndexOf('|');
            code = bar > 0 && int.TryParse(line[..bar], out int c) ? c : 1;
            resp = bar >= 0 ? line[(bar + 1)..] : line;
            return true;
        }
        catch { return false; }   // nobody listening -> one-shot mode
    }

    // ---------------- one-shot (app not running): talk to the EC directly ----------------
    private static int RunOneShot(CliCommand cmd)
    {
        var settings = AppSettings.Load();
        string fw = Ec.ReadFirmware();
        var dev = Devices.Detect(fw);
        // Legacy ExperimentalEnabled is honoured read-only here: a one-shot CLI call must not
        // lose to a settings file the tray app has not migrated yet.
        bool writable = dev != null && (dev.Tier == Tier.Tested
            || settings.ExperimentalWriteAllowedFor(dev.MatchedPrefix(fw))
            || settings.ExperimentalEnabled);

        // A travel mode whose date passed is caught up on any one-shot invocation - without the
        // tray app running there is no poll to do it. --diag keeps its read-only promise.
        if (cmd.Kind != CliKind.Diag && settings.TravelUntil != DateTime.MinValue && DateTime.Now >= settings.TravelUntil)
        {
            int back = settings.TravelPrevLimit;
            settings.TravelUntil = DateTime.MinValue;
            settings.ChargeLimit = back;
            settings.Save();
            if (back > 0 && writable && dev != null)
            {
                try { Ec.SetChargeLimit(dev, back); } catch { }
            }
            ChangeLog.Add(ChangeSource.Cli, $"Travel mode: ended (limit {(back > 0 ? back + " %" : "off")})");
        }

        try
        {
            switch (cmd.Kind)
            {
                case CliKind.Status:
                {
                    HwSnapshot hw = default;
                    ProfileId? cur = null;
                    bool telemetry = false;
                    if (dev != null) { Ec.TryReadHw(dev, out hw); cur = Ec.GetCurrent(dev); }
                    else if (MsiTelemetry.Available())
                    {
                        // (#48) monitoring-only boards: temperatures come from the vendor WMI blocks
                        telemetry = true;
                        var t = MsiTelemetry.Read();
                        hw = new HwSnapshot(t.CpuTemp, t.GpuTemp, 0, 0, 0, fw);
                    }
                    int? kbd = null; bool? webcam = null; bool? fnLeft = null;
                    if (dev != null)
                    {
                        try { byte ka = Devices.KbdBacklightFor(fw); if (ka != 0) kbd = Ec.GetKbdBacklight(ka); } catch { }
                        try { if (Devices.WebcamSupported(fw)) webcam = Ec.GetWebcam(); } catch { }
                        try { if (Devices.FnWinSwapFor(fw) is { } fsw) fnLeft = Ec.GetFnLeft(fsw); } catch { }
                    }
                    var ps = SystemInformation.PowerStatus;
                    int batt = ps.BatteryLifePercent is >= 0f and <= 1f ? (int)Math.Round(ps.BatteryLifePercent * 100) : -1;
                    bool noBatt = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
                    int battMin = Perf.BatteryMinutesLeft();
                    int wear = BatteryHealth.Read().WearPct;
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        running = false,
                        model = dev?.Name ?? "unsupported",
                        firmware = fw,
                        tier = dev?.Tier.ToString() ?? "None",
                        writable,
                        telemetry,
                        profile = cur?.ToString(),
                        cpuTemp = hw.CpuTemp, gpuTemp = hw.GpuTemp,
                        cpuFan = hw.CpuFan, gpuFan = hw.GpuFan,
                        cpuRpm = hw.CpuRpm, gpuRpm = hw.GpuRpm,
                        refreshHz = Display.Current(),
                        chargeLimit = settings.ChargeLimit,
                        kbdLight = kbd,
                        webcam,
                        fnLeft,
                        hdr = Hdr.Supported() ? Hdr.Enabled() : (bool?)null,
                        touchpad = Touchpad.State() is >= 0 and var tps ? tps == 1 : (bool?)null,
                        batteryPercent = noBatt || batt < 0 ? (int?)null : batt,
                        batteryCharging = noBatt ? (bool?)null : ps.PowerLineStatus == PowerLineStatus.Online,
                        batteryMinutesLeft = battMin > 0 ? battMin : (int?)null,
                        batteryWearPct = wear >= 0 ? wear : (int?)null,
                        disks = Perf.Disks().Select(dk => new { name = dk.Name, tempC = dk.TempC > 0 ? dk.TempC : (int?)null }).ToArray(),
                    }));
                    return 0;
                }
                case CliKind.Overlay:
                    Console.WriteLine("overlay control needs the GhostDeck app running");
                    return 1;
                case CliKind.Scene:
                    Console.WriteLine("scene control needs the GhostDeck app running");
                    return 1;
                case CliKind.WinLock:
                    // the hook lives inside the running process - a one-shot would exit and unhook
                    Console.WriteLine("the Windows-key lock needs the GhostDeck app running");
                    return 1;
                case CliKind.Refresh:
                {
                    // Windows display API, no EC needed - works on unsupported hardware too.
                    var rates = Display.SupportedRates();
                    if (rates.Count == 0) { Console.WriteLine("the display reports no switchable rates"); return 1; }
                    int hz = cmd.Arg == "max" ? rates.Max() : int.Parse(cmd.Arg);
                    if (!rates.Contains(hz)) { Console.WriteLine($"unsupported rate: {hz} (supported: {string.Join(", ", rates)})"); return 1; }
                    int before = Display.Current();
                    if (before != hz)
                    {
                        if (!Display.SetRefresh(hz)) { Console.WriteLine("the display refused the mode change"); return 1; }
                        ChangeLog.Load();
                        ChangeLog.Add(ChangeSource.Display, $"{before} Hz → {hz} Hz");
                    }
                    Console.WriteLine($"refresh rate: {hz} Hz");
                    return 0;
                }
                case CliKind.Diag:
                {
                    // Read-only collection; works on any machine (an EC failure is itself recorded).
                    string path = cmd.Arg.Length > 0 ? cmd.Arg : $"ghostdeck-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip";
                    string ver = typeof(Cli).Assembly.GetName().Version?.ToString(3) ?? "?";
                    Diagnostics.Save(path, ver, fw, dev?.Name ?? "unsupported", dev?.Tier.ToString() ?? "None");
                    Console.WriteLine("diagnostics saved: " + Path.GetFullPath(path));
                    return 0;
                }
                case CliKind.Brightness:
                {
                    // Windows-level (WMI), no EC needed - works on unsupported hardware too.
                    int pct = int.Parse(cmd.Arg);
                    try { Brightness.Set(pct); }
                    catch { Console.WriteLine("no brightness control (WMI) on this machine"); return 1; }
                    ChangeLog.Load();
                    ChangeLog.Add(ChangeSource.Cli, $"Brightness: {pct} %");
                    Console.WriteLine($"brightness: {pct}");
                    return 0;
                }
                case CliKind.HdrSwitch:
                {
                    // DisplayConfig API, no EC needed - works on unsupported hardware too.
                    if (!Hdr.Supported()) { Console.WriteLine("no HDR-capable display"); return 1; }
                    bool on = cmd.Arg == "on";
                    if (!Hdr.Set(on)) { Console.WriteLine("the display refused the HDR change"); return 1; }
                    ChangeLog.Load();
                    ChangeLog.Add(ChangeSource.Cli, $"HDR: {(on ? "on" : "off")}");
                    Console.WriteLine($"hdr: {cmd.Arg}");
                    return 0;
                }
                case CliKind.Touchpad:
                {
                    // Devnode switch (admin, which the manifest guarantees) - no EC needed.
                    if (Touchpad.State() < 0) { Console.WriteLine("no precision touchpad found"); return 1; }
                    bool on = cmd.Arg == "on";
                    try { Touchpad.Set(on); }
                    catch (Exception ex) { Console.WriteLine($"touchpad change failed ({ex.Message})"); return 1; }
                    ChangeLog.Load();
                    ChangeLog.Add(ChangeSource.Cli, $"Touchpad: {(on ? "on" : "off")}");
                    Console.WriteLine($"touchpad: {cmd.Arg}");
                    return 0;
                }
            }

            if (dev == null) { Console.WriteLine($"unsupported hardware (firmware: {(fw.Length > 0 ? fw : "unknown")})"); return 1; }
            if (!writable) { Console.WriteLine("model is experimental - enable Experimental writes in the app settings first"); return 1; }

            ChangeLog.Load();   // CLI actions land in the same change history the app shows
            switch (cmd.Kind)
            {
                case CliKind.Profile:
                case CliKind.Cycle:
                {
                    ProfileId id;
                    if (cmd.Kind == CliKind.Profile) id = Enum.Parse<ProfileId>(cmd.Arg);
                    else
                    {
                        int i = Array.IndexOf(Profiles.Order, Ec.GetCurrent(dev));
                        id = Profiles.Order[(i + 1) % Profiles.Order.Length];
                    }
                    Ec.Apply(dev.Recipes[id]);
                    ApplyAssignedCurveOneShot(settings, dev, id);
                    ChangeLog.Add(ChangeSource.Cli, $"{Profiles.Get(id).Label}  ·  CLI");
                    Console.WriteLine($"profile set: {id}");
                    return 0;
                }
                case CliKind.FanBoost:
                {
                    if (cmd.Arg2.Length > 0)
                    {
                        // one-shot exits immediately, so nothing would fire the auto-off
                        Console.WriteLine("the fan-boost timer needs the GhostDeck app running");
                        return 1;
                    }
                    bool on = cmd.Arg == "on";
                    Ec.SetCoolerBoost(dev, on);
                    if (!on)
                    {
                        // firmware keeps fans at max until the fan mode is re-asserted (same as the app)
                        byte b = Ec.GetCurrent(dev) == ProfileId.Silent ? dev.FanSilentValue : (byte)0x0D;
                        try { Ec.SetFanMode(dev, b); } catch { }
                    }
                    ChangeLog.Add(ChangeSource.Cli, $"Fan Boost: {(on ? "on" : "off")}");
                    Console.WriteLine($"fan boost: {cmd.Arg}");
                    return 0;
                }
                case CliKind.Curve:
                {
                    if (dev.FanCurve is not { } fc) { Console.WriteLine("no fan-curve support on this model"); return 1; }
                    if (cmd.Arg.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        byte b = Ec.GetCurrent(dev) == ProfileId.Silent ? dev.FanSilentValue : (byte)0x0D;
                        Ec.SetFanMode(dev, b);
                        ChangeLog.Add(ChangeSource.Cli, "Fan curve: stock");
                        Console.WriteLine("fan curve: stock");
                        return 0;
                    }
                    var p = settings.FindPreset(cmd.Arg);
                    if (p == null || !p.IsValid(fc)) { Console.WriteLine($"preset not found: {cmd.Arg}"); return 1; }
                    if (Ec.GetCurrent(dev) == ProfileId.Silent)
                        Ec.Apply(dev.Recipes[ProfileId.Balanced]);   // a curve drops the Silent cap (same EC byte)
                    Ec.WriteFanCurve(dev, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);
                    Ec.SetFanMode(dev, fc.AdvancedModeValue);
                    ChangeLog.Add(ChangeSource.Cli, $"Fan curve preset: {p.Name}");
                    Console.WriteLine($"fan curve applied: {p.Name}");
                    return 0;
                }
                case CliKind.Kbd:
                {
                    byte addr = Devices.KbdBacklightFor(fw);
                    if (addr == 0) { Console.WriteLine("no keyboard-backlight support on this model"); return 1; }
                    int level = ParseKbdLevel(cmd.Arg);
                    Ec.SetKbdBacklight(addr, level);
                    ChangeLog.Add(ChangeSource.Cli, $"Keyboard backlight: {cmd.Arg}");
                    Console.WriteLine($"keyboard backlight: {cmd.Arg}");
                    return 0;
                }
                case CliKind.Webcam:
                {
                    if (!Devices.WebcamSupported(fw)) { Console.WriteLine("no webcam control on this model"); return 1; }
                    bool on = cmd.Arg == "on";
                    if (on && Ec.GetWebcamBlock()) { Console.WriteLine("webcam is hard-blocked - lift the block in the app settings first"); return 1; }
                    Ec.SetWebcam(on);
                    ChangeLog.Add(ChangeSource.Cli, $"Webcam: {(on ? "on" : "off")}");
                    Console.WriteLine($"webcam: {cmd.Arg}");
                    return 0;
                }
                case CliKind.Charge:
                {
                    int limit = int.Parse(cmd.Arg);
                    // an explicit limit dissolves a pending travel-mode revert (the user took over)
                    if (limit != settings.ChargeLimit && settings.TravelUntil != DateTime.MinValue)
                    {
                        settings.TravelUntil = DateTime.MinValue;
                        ChangeLog.Add(ChangeSource.Cli, "Travel mode cancelled (limit changed manually)");
                    }
                    settings.ChargeLimit = limit;
                    settings.Save();
                    if (limit > 0)
                    {
                        Ec.SetChargeLimit(dev, limit);
                        ChangeLog.Add(ChangeSource.Cli, $"Charge limit: {limit} %");
                        Console.WriteLine($"charge limit: {limit} %");
                    }
                    else
                    {
                        // 0 = stop managing: the EC keeps its current threshold, we just stop re-asserting it
                        ChangeLog.Add(ChangeSource.Cli, "Charge limit: off");
                        Console.WriteLine("charge limit: off (no longer managed)");
                    }
                    return 0;
                }
                case CliKind.Travel:
                {
                    int days = int.Parse(cmd.Arg);
                    if (days > 0)
                    {
                        if (settings.TravelUntil == DateTime.MinValue)
                            settings.TravelPrevLimit = settings.ChargeLimit;   // re-stamping while active keeps the original
                        settings.TravelUntil = DateTime.Now.AddDays(days);   // full days from now, not calendar midnights
                        settings.ChargeLimit = 100;
                        settings.Save();
                        Ec.SetChargeLimit(dev, 100);
                        ChangeLog.Add(ChangeSource.Cli, $"Travel mode: 100 % until {settings.TravelUntil:yyyy-MM-dd}");
                        Console.WriteLine($"travel mode: 100 % until {settings.TravelUntil:yyyy-MM-dd}");
                    }
                    else
                    {
                        if (settings.TravelUntil == DateTime.MinValue) { Console.WriteLine("travel mode is not active"); return 0; }
                        int back = settings.TravelPrevLimit;
                        settings.TravelUntil = DateTime.MinValue;
                        settings.ChargeLimit = back;
                        settings.Save();
                        if (back > 0) Ec.SetChargeLimit(dev, back);   // 0 = stop managing, the EC keeps its threshold
                        ChangeLog.Add(ChangeSource.Cli, $"Travel mode: off (limit {(back > 0 ? back + " %" : "off")})");
                        Console.WriteLine($"travel mode: off ({(back > 0 ? "limit " + back + " %" : "no longer managed")})");
                    }
                    return 0;
                }
                case CliKind.FnSwap:
                {
                    if (Devices.FnWinSwapFor(fw) is not { } fs) { Console.WriteLine("no Fn/Win swap register on this model"); return 1; }
                    Ec.SetFnLeft(fs, cmd.Arg == "left");
                    ChangeLog.Add(ChangeSource.Cli, $"Fn key: {cmd.Arg}");
                    Console.WriteLine($"fn key: {cmd.Arg}");
                    return 0;
                }
                case CliKind.Panic:
                {
                    try { Ec.SetCoolerBoost(dev, false); } catch { }
                    // (#27) stock state includes a working camera (same as the in-app panic reset)
                    if (Devices.WebcamSupported(fw)) { try { Ec.SetWebcamBlock(false); Ec.SetWebcam(true); } catch { } }
                    Ec.Apply(dev.Recipes[ProfileId.Balanced]);
                    ChangeLog.Add(ChangeSource.Cli, "Panic reset  ·  CLI");
                    Console.WriteLine("panic reset done: Balanced, Fan Boost off, fans auto");
                    return 0;
                }
            }
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EC access failed ({ex.Message}) - run elevated (administrator) on supported hardware");
            return 1;
        }
    }

    // Same per-profile preset rule as the app: never Silent, only a valid assigned preset.
    private static void ApplyAssignedCurveOneShot(AppSettings s, DeviceProfile dev, ProfileId id)
    {
        if (id == ProfileId.Silent || dev.FanCurve is not { } fc) return;
        if (!s.ProfileCurves.TryGetValue(Profiles.Get(id).Key, out var name) || string.IsNullOrEmpty(name)) return;
        var p = s.FindPreset(name);
        if (p == null || !p.IsValid(fc)) return;
        try
        {
            Ec.WriteFanCurve(dev, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);
            Ec.SetFanMode(dev, fc.AdvancedModeValue);
        }
        catch { }
    }
}
