using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GhostDeck;

public enum CliKind { Profile, Cycle, FanBoost, Overlay, Curve, Panic, Status, Help }

public sealed record CliCommand(CliKind Kind, string Arg = "");

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
          GhostDeck.exe --fanboost <on|off>     full fan speed on/off
          GhostDeck.exe --overlay <on|off>      gaming overlay (needs the app running)
          GhostDeck.exe --curve <preset|auto>   apply a saved fan-curve preset (auto = stock fans)
          GhostDeck.exe --panic                 safe state: Fan Boost off, Balanced, fans auto
          GhostDeck.exe --status                print the current state as JSON
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
                return Arg1().ToLowerInvariant() is "on" or "off" ? new CliCommand(CliKind.FanBoost, Arg1().ToLowerInvariant()) : null;
            case "--overlay":
                return Arg1().ToLowerInvariant() is "on" or "off" ? new CliCommand(CliKind.Overlay, Arg1().ToLowerInvariant()) : null;
            case "--curve":
                return Arg1().Length > 0 ? new CliCommand(CliKind.Curve, Arg1()) : null;
            case "--panic": return new CliCommand(CliKind.Panic);
            case "--status": return new CliCommand(CliKind.Status);
            case "--help" or "-h" or "/?": return new CliCommand(CliKind.Help);
            default: return null;
        }
    }

    /// <summary>Entry point for any launch with arguments. Returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        AttachConsole(-1);   // write into the parent console when started from a terminal
        var cmd = Parse(args);
        if (cmd == null) { Console.WriteLine(Usage); return 2; }
        if (cmd.Kind == CliKind.Help) { Console.WriteLine(Usage); return 0; }

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
        bool writable = dev != null && (dev.Tier == Tier.Tested || settings.ExperimentalEnabled);

        try
        {
            switch (cmd.Kind)
            {
                case CliKind.Status:
                {
                    HwSnapshot hw = default;
                    ProfileId? cur = null;
                    if (dev != null) { try { hw = Ec.ReadHw(dev); cur = Ec.GetCurrent(dev); } catch { } }
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        running = false,
                        model = dev?.Name ?? "unsupported",
                        firmware = fw,
                        tier = dev?.Tier.ToString() ?? "None",
                        writable,
                        profile = cur?.ToString(),
                        cpuTemp = hw.CpuTemp, gpuTemp = hw.GpuTemp,
                        cpuFan = hw.CpuFan, gpuFan = hw.GpuFan,
                        cpuRpm = hw.CpuRpm, gpuRpm = hw.GpuRpm,
                        refreshHz = Display.Current(),
                    }));
                    return 0;
                }
                case CliKind.Overlay:
                    Console.WriteLine("overlay control needs the GhostDeck app running");
                    return 1;
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
                    if (p == null || !p.IsValid(fc.Points)) { Console.WriteLine($"preset not found: {cmd.Arg}"); return 1; }
                    if (Ec.GetCurrent(dev) == ProfileId.Silent)
                        Ec.Apply(dev.Recipes[ProfileId.Balanced]);   // a curve drops the Silent cap (same EC byte)
                    Ec.WriteFanCurve(dev, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);
                    Ec.SetFanMode(dev, fc.AdvancedModeValue);
                    ChangeLog.Add(ChangeSource.Cli, $"Fan curve preset: {p.Name}");
                    Console.WriteLine($"fan curve applied: {p.Name}");
                    return 0;
                }
                case CliKind.Panic:
                {
                    try { Ec.SetCoolerBoost(dev, false); } catch { }
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
        if (p == null || !p.IsValid(fc.Points)) return;
        try
        {
            Ec.WriteFanCurve(dev, p.CpuTemp, p.CpuSpeed, p.GpuTemp, p.GpuSpeed);
            Ec.SetFanMode(dev, fc.AdvancedModeValue);
        }
        catch { }
    }
}
