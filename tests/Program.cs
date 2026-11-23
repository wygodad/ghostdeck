using GhostDeck;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    private static int _passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < .001, $"{actual} != {expected}");
    private static void Test(string name, Action body) { body(); Console.WriteLine("PASS " + name); _passed++; }
    private static void Throws(Action body) { try { body(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
    private static readonly int[] Temps = { 0, 20, 40, 60, 80, 100 }, Speeds = { 0, 20, 40, 60, 80, 100 };
    private static readonly DateTimeOffset Epoch = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [STAThread]
    private static int Main()
    {
        try
        {
            AppSettings.DirOverride = Path.Combine(Path.GetTempPath(), "ghostdeck-cross-cooling-tests-" + Guid.NewGuid().ToString("N"));
            Test("all modes x algorithms preserve floors and direction", () =>
            {
                foreach (var mode in Enum.GetValues<CrossCoolingMode>())
                foreach (var algorithm in Enum.GetValues<CoolingMix>())
                foreach (var pair in new[] { (90.0, 30.0), (30.0, 90.0), (90.0, 90.0), (0.0, 150.0) })
                {
                    var r = CrossCooling.Calculate(pair.Item1, pair.Item2, 150, new() { Mode = mode, Algorithm = algorithm });
                    Check(r.CpuTarget >= pair.Item1 && r.GpuTarget >= pair.Item2, "floor");
                    if (mode is CrossCoolingMode.Off or CrossCoolingMode.CpuToGpu) Near(r.CpuTarget, pair.Item1);
                    if (mode is CrossCoolingMode.Off or CrossCoolingMode.GpuToCpu) Near(r.GpuTarget, pair.Item2);
                }
            });
            Test("maximum and average", () =>
            {
                var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                var r = CrossCooling.Calculate(90, 30, 150, s); Near(r.CpuTarget, 90); Near(r.GpuTarget, 90);
                s.Algorithm = CoolingMix.Average; r = CrossCooling.Calculate(90, 30, 150, s);
                Near(r.Mixed, 60); Near(r.CpuTarget, 90); Near(r.GpuTarget, 60);
            });
            Test("normalized weights and presets", () =>
            {
                var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both, Algorithm = CoolingMix.WeightedAverage, CpuWeight = 75, GpuWeight = 25 };
                Near(CrossCooling.Calculate(90, 30, 150, s).Mixed, 75);
                s.CpuWeight = 30; s.GpuWeight = 10; Near(CrossCooling.Calculate(90, 30, 150, s).Mixed, 75);
                s.CpuWeight = 25; s.GpuWeight = 75; Near(CrossCooling.Calculate(90, 30, 150, s).Mixed, 45);
            });
            Test("zero, negative and oversized weights rejected", () =>
            {
                foreach (var weights in new[] { (0, 0), (-1, 50), (101, 0) })
                    Throws(() => CrossCooling.Calculate(90, 30, 150, new() { CpuWeight = weights.Item1, GpuWeight = weights.Item2 }));
            });
            Test("priority boost both directions and endpoints", () =>
            {
                foreach (int boost in new[] { 0, 25, 100 })
                {
                    var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both, Algorithm = CoolingMix.PriorityBoost, BoostPercent = boost };
                    var r = CrossCooling.Calculate(90, 30, 150, s); Near(r.CpuTarget, 90); Near(r.GpuTarget, 30 + .6 * boost);
                    r = CrossCooling.Calculate(30, 90, 150, s); Near(r.GpuTarget, 90); Near(r.CpuTarget, 30 + .6 * boost);
                }
            });
            Test("device-specific ceiling including 150", () =>
            {
                Near(CrossCooling.Calculate(200, 30, 150, new() { Mode = CrossCoolingMode.Both }).GpuTarget, 150);
                Near(CrossCooling.Calculate(200, -1, 80, new() { Mode = CrossCoolingMode.Both }).CpuTarget, 80);
                Throws(() => CrossCooling.Calculate(double.NaN, 30, 150, new()));
                Throws(() => CrossCooling.Calculate(30, double.PositiveInfinity, 150, new()));
            });
            Test("six point floor retains nodes, monotonicity and original table", () =>
            {
                var original = new[] { 0, 40, 60, 80, 110, 150 };
                var table = CrossCooling.Floor(original, 75.1, 150);
                Check(table.SequenceEqual(new[] { 76, 76, 76, 80, 110, 150 }), "floor table");
                Check(original[0] == 0, "input mutated");
                Check(CrossCooling.ValidCurve(Temps, table, 150), "monotone");
                Throws(() => CrossCooling.Floor(new[] { 0, 50, 40, 60, 70, 80 }, 70, 150));
                Throws(() => CrossCooling.Floor(new[] { 10, 20 }, 70, 150));
            });
            Test("temperature boundaries, sleeping GPU, NaN and infinity", () =>
            {
                foreach (double? bad in new double?[] { null, 0, -1, 116, double.NaN, double.PositiveInfinity })
                    Check(!CrossCooling.ValidTemperature(bad), "invalid temperature");
                Check(CrossCooling.ValidTemperature(115), "boundary");
            });
            Test("sensor faults reset scheduler state", () =>
            {
                foreach (double? bad in new double?[] { null, 0, double.NaN })
                {
                    var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                    Check(Sample(p, s, 90, 30) != null, "baseline");
                    Check(Sample(p, s, 90, bad) == null && p.LastPlan == null, "fault reset");
                }
            });
            Test("stale/future sample and timeout reset", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                Check(Sample(p, s, 90, 30, 7, Epoch) == null, "stale");
                Check(Sample(p, s, 90, 30, 0, Epoch.AddSeconds(1)) == null, "future");
            });
            Test("bounded updates and no duplicate plans", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                Check(Sample(p, s, 80, 30)!.PlanChanged, "first");
                Check(!Sample(p, s, 100, 30, 2)!.PlanChanged, "minimum interval");
                Check(Sample(p, s, 100, 30, 5)!.PlanChanged, "interval elapsed");
                Check(!Sample(p, s, 100, 30, 10)!.PlanChanged, "duplicate");
            });
            Test("hysteresis and gradual decrease", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                Sample(p, s, 90, 30);
                Check(!Sample(p, s, 87, 30, 5)!.PlanChanged, "small fall");
                var r = Sample(p, s, 40, 30, 10)!; Check(r.CpuTable[0] == 85, "gradual decline");
            });
            Test("smoothing never delays heat spike or reduces raw-demand floor", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both, Smoothing = true };
                Sample(p, s, 30, 30);
                var r = Sample(p, s, 100, 30, 2)!; Near(r.Demand.CpuTarget, 100); Near(r.Demand.GpuTarget, 100);
                r = Sample(p, s, 40, 30, 5)!; Check(r.Demand.CpuTarget > 40, "smooth fall");
            });
            Test("off and cancellation discard plan without writes", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                Sample(p, s, 90, 30); s.Mode = CrossCoolingMode.Off;
                var r = Sample(p, s, 90, 30, 5)!;
                Check(!r.PlanChanged && p.LastPlan == null && r.CpuTable.SequenceEqual(Speeds), "off");
                Check(p.Sample(90, 30, Epoch, Epoch, Temps, Speeds, Temps, Speeds, 150, s, cancelled: true) == null, "cancel");
            });
            Test("conflicts, silent/native mode, boost, diagnostics and unsupported devices", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                foreach (int reason in new[] { 0, 1, 2 })
                    Check(p.Sample(90, 30, Epoch, Epoch, Temps, Speeds, Temps, Speeds, 150, s,
                        supported: reason != 0, singleFan: reason == 1, conflict: reason == 2) == null, "gate");
            });
            Test("profile/curve change can reset schedule", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                Sample(p, s, 90, 30); p.Reset();
                Check(p.LastPlan == null && Sample(p, s, 40, 30, 1)!.PlanChanged, "reset profile");
            });
            Test("scheduler uses CurveModel interpolation rather than raw-temperature average", () =>
            {
                var p = new CrossCoolingScheduler(); var s = new CrossCoolingSettings { Mode = CrossCoolingMode.Both };
                var r = p.Sample(90, 30, Epoch, Epoch, Temps, new[] { 0, 40, 80, 100, 140, 150 }, Temps, Speeds, 150, s)!;
                Near(r.Demand.CpuOriginal, 145); Near(r.Demand.GpuTarget, 145);
            });
            Test("settings legacy migration, normalization and JSON roundtrip", () =>
            {
                var app = JsonSerializer.Deserialize<AppSettings>("{}")!;
                Check(app.CrossCooling.Mode == CrossCoolingMode.Off, "legacy default");
                app = JsonSerializer.Deserialize<AppSettings>("{\"CrossCooling\":null}")!; app.EnsureDefaults();
                Check(app.CrossCooling.Mode == CrossCoolingMode.Off, "null default");
                app.CrossCooling = new() { Mode = CrossCoolingMode.Both, Algorithm = CoolingMix.PriorityBoost, BoostPercent = 35 };
                var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(app))!;
                Check(restored.CrossCooling.BoostPercent == 35 && restored.CrossCooling.Mode == CrossCoolingMode.Both, "roundtrip");
                restored.CrossCooling.UpdateSeconds = 1; restored.CrossCooling.CpuWeight = restored.CrossCooling.GpuWeight = 0; restored.EnsureDefaults();
                Check(restored.CrossCooling.UpdateSeconds == 5 && restored.CrossCooling.CpuWeight == 50, "normalization");
            });
            Test("application embedded resources survive restricted builds", () =>
            {
                var resources = typeof(AppSettings).Assembly.GetManifestResourceNames();
                foreach (string name in new[] { "GhostDeck.app.ico", "GhostDeck.model-history.json", "GhostDeck.worldmap.txt" })
                    Check(resources.Contains(name), "missing embedded resource " + name);
            });
            Test("settings clone/import are independent and preserve unknown options", () =>
            {
                var app = JsonSerializer.Deserialize<AppSettings>("{\"CrossCooling\":{\"Mode\":3,\"FutureOption\":42}}")!;
                app.EnsureDefaults(); var clone = app.Clone(); clone.CrossCooling.CpuWeight = 75;
                Check(app.CrossCooling.CpuWeight == 50, "clone alias");
                var target = new AppSettings(); target.ImportFrom(app); target.CrossCooling.Mode = CrossCoolingMode.Off;
                Check(app.CrossCooling.Mode == CrossCoolingMode.Both, "import alias");
                Check(JsonSerializer.Serialize(clone).Contains("FutureOption"), "unknown field lost");
            });
            Test("all UI localization keys resolve in every language", () =>
            {
                var map = (Dictionary<string, string[]>)typeof(Lang).GetField("Map", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                foreach (var code in Lang.Codes)
                {
                    Lang.Set(code);
                    foreach (string key in map.Keys.Where(k => k.StartsWith("cc_"))) Check(Lang.T(key) != key, key + "/" + code);
                    _ = string.Format(Lang.T("cc_values"), 90, 30, "90%", "30%", "90%", "90%", "90%");
                }
                Lang.Set("en");
            });
            LiveTests();
            Test("native switch binds to actual live activation and restores on disable", LiveUiTest);
            Test("native UI rebuild, themes, languages and disposal without EC writes", UiTest);
            Test("Fan Curve page navigation, unsupported-device scheduler and rendering", PageTest);
            Test("context menus render dark/light surfaces, states and nested menus", ContextMenuTest);
            Test("Cross-Cooling saves enable/options to disk and restores after restart", CoolingPersistenceTest);
            Console.WriteLine($"{_passed} tests passed. No physical hardware writes were performed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            if (AppSettings.DirOverride is { } dir && Directory.Exists(dir)) Directory.Delete(dir, true);
            AppSettings.DirOverride = null;
        }
    }

    private static CoolingPlan? Sample(CrossCoolingScheduler p, CrossCoolingSettings s, double? cpu, double? gpu,
        int seconds = 0, DateTimeOffset? sampled = null) => p.Sample(cpu, gpu, sampled ?? Epoch.AddSeconds(seconds), Epoch.AddSeconds(seconds), Temps, Speeds, Temps, Speeds, 150, s);

    private sealed class FakeHardware : ICrossCoolingHardware
    {
        public byte AdvancedMode => 0x8D;
        public FanCurvePreset Curve = new() { CpuTemp = (int[])Temps.Clone(), CpuSpeed = (int[])Speeds.Clone(), GpuTemp = (int[])Temps.Clone(), GpuSpeed = (int[])Speeds.Clone() };
        public byte Mode = 0x0D;
        public int Cpu = 90, Gpu = 30, Applies, Restores, Autos;
        public bool Boost, FailApply, FailRestore, FailRead;
        public DateTimeOffset Time = Epoch;
        public Action? OnApply;
        public CoolingBaseline Capture() => new(Curve.Clone(), Mode);
        public CoolingReading Read()
        {
            if (FailRead) throw new IOException("read failure");
            return new(Time, new(Cpu, Gpu, 90, 30, 100, "test", 4000, 2000), Mode, Boost);
        }
        public void Apply(FanCurvePreset original, int[] cpuSpeeds, int[] gpuSpeeds)
        {
            Applies++; Curve = original.Clone(); Curve.CpuSpeed = (int[])cpuSpeeds.Clone();
            OnApply?.Invoke();
            if (FailApply) throw new IOException("partial write");
            Curve.GpuSpeed = (int[])gpuSpeeds.Clone(); Mode = AdvancedMode;
        }
        public void Restore(CoolingBaseline b)
        {
            Restores++;
            if (FailRestore) throw new IOException("restore failure");
            Curve = b.Curve.Clone(); Mode = b.Mode;
        }
        public void Automatic() { Autos++; Mode = 0x0D; }
    }

    private static CrossCoolingSettings LiveSettings(CrossCoolingMode mode = CrossCoolingMode.Both) => new() { Mode = mode };
    private static void LiveTests()
    {
        Test("live Auto activation writes both tables and enters Advanced", () =>
        {
            var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150);
            c.Configure(LiveSettings(), true); c.Tick(Epoch);
            Check(h.Applies == 1 && h.Mode == h.AdvancedMode && c.Status.Key == "cc_running", "not active");
            Check(h.Curve.CpuSpeed[0] == 90 && h.Curve.GpuSpeed[0] == 90, "both fans demand");
            Check(c.Status.LastWrite == Epoch && c.Status.Reading!.Value.Hardware.CpuRpm == 4000, "actual status");
            Check(c.Baseline!.CpuSpeed.SequenceEqual(Speeds), "baseline overwritten");
            c.Configure(LiveSettings(), false);
            Check(h.Restores == 1 && h.Mode == 0x0D && h.Curve.GpuSpeed.SequenceEqual(Speeds), "disable restore");
        });
        Test("live directional control and exact mode restoration", () =>
        {
            foreach (var mode in new[] { CrossCoolingMode.CpuToGpu, CrossCoolingMode.GpuToCpu })
            {
                var h = new FakeHardware { Mode = 0xCD, Cpu = mode == CrossCoolingMode.GpuToCpu ? 30 : 90, Gpu = mode == CrossCoolingMode.GpuToCpu ? 90 : 30 };
                using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(mode), true); c.Tick(Epoch);
                Check((mode == CrossCoolingMode.CpuToGpu ? h.Curve.CpuSpeed : h.Curve.GpuSpeed).SequenceEqual(Speeds), "source table changed");
                c.Configure(LiveSettings(mode), false); Check(h.Mode == 0xCD, "previous mode lost");
            }
        });
        Test("live duplicate and interval writes are bounded", () =>
        {
            var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true);
            c.Tick(Epoch); h.Time = Epoch.AddSeconds(2); h.Cpu = 100; c.Tick(h.Time); Check(h.Applies == 1, "interval ignored");
            h.Time = Epoch.AddSeconds(5); c.Tick(h.Time); Check(h.Applies == 2, "update missing");
            h.Time = Epoch.AddSeconds(10); c.Tick(h.Time); Check(h.Applies == 2, "duplicate write");
        });
        Test("live missing and stale sensors restore and recover", () =>
        {
            foreach (bool stale in new[] { false, true })
            {
                var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true); c.Tick(Epoch);
                if (!stale) h.Gpu = 0;
                c.Tick(Epoch.AddSeconds(10)); Check(h.Restores == 1 && c.Status.Key == "cc_sensor_paused", "fault not restored");
                h.Gpu = 30; h.Time = Epoch.AddSeconds(12); c.Resume(); c.Tick(h.Time);
                Check(h.Applies == 2 && c.Status.Key == "cc_running", "sensor recovery");
            }
        });
        Test("live settings changes cannot bypass write interval or keep a deselected fan floor", () =>
        {
            var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150);
            c.Configure(LiveSettings(), true); c.Tick(Epoch);
            var settings = LiveSettings(CrossCoolingMode.CpuToGpu);
            for (int i = 1; i < 5; i++)
            {
                settings.CpuWeight = i; c.Configure(settings, true); h.Time = Epoch.AddSeconds(i); c.Tick(h.Time);
            }
            Check(h.Applies == 1 && h.Restores == 0, "settings slider spammed EC");
            h.Time = Epoch.AddSeconds(5); c.Tick(h.Time);
            Check(h.Applies == 2 && h.Curve.CpuSpeed.SequenceEqual(Speeds), "source floor retained");
        });
        Test("live Fan Boost suspends and resumes without losing baseline", () =>
        {
            var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true); c.Tick(Epoch);
            h.Boost = true; h.Time = Epoch.AddSeconds(5); c.Tick(h.Time);
            Check(h.Restores == 1 && c.Status.Key == "cc_boost_paused" && c.Status.Enabled, "boost pause");
            h.Boost = false; c.Resume(); c.Tick(h.Time); Check(h.Applies == 2, "boost recovery");
        });
        Test("live session suspension blocks queued writes and accepts new curve baseline", () =>
        {
            var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true); c.Tick(Epoch);
            c.Suspend(); c.Tick(Epoch.AddSeconds(5)); Check(h.Applies == 1 && h.Restores == 1, "queued write after suspension");
            h.Curve.CpuSpeed = new[] { 10, 30, 50, 70, 90, 110 }; h.Time = Epoch.AddSeconds(10);
            c.Resume(); c.Tick(h.Time); c.Configure(LiveSettings(), false);
            Check(h.Curve.CpuSpeed[0] == 10, "new baseline discarded");
        });
        Test("live suspension serializes with an in-flight write", () =>
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var h = new FakeHardware { OnApply = () => { entered.Set(); Check(release.Wait(5000), "write timeout"); } };
            using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true);
            var writer = Task.Run(() => c.Tick(Epoch)); Check(entered.Wait(5000), "write never started");
            var pause = Task.Run(() => c.Suspend());
            release.Set(); Check(Task.WaitAll(new[] { writer, pause }, 5000), "suspension deadlock");
            c.Tick(Epoch); Check(h.Applies == 1 && h.Restores == 1 && h.Mode == 0x0D, "suspension did not restore");
        });
        Test("live partial writes restore and disarm", () =>
        {
            var h = new FakeHardware { FailApply = true }; using var c = new CrossCoolingController(h, 150);
            c.Configure(LiveSettings(), true); c.Tick(Epoch);
            Check(!c.Status.Enabled && c.Status.Key == "cc_failed" && h.Restores == 1 && h.Curve.CpuSpeed.SequenceEqual(Speeds), "partial write recovery");
            h.FailApply = false; c.Tick(Epoch); Check(h.Applies == 1, "failure silently rearmed");
        });
        Test("live failed restoration retains baseline, requests Auto and retries", () =>
        {
            var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true); c.Tick(Epoch);
            h.FailRestore = true; c.Configure(LiveSettings(), false);
            Check(c.Status.Key == "cc_restore_failed" && h.Autos == 1 && c.Baseline != null, "restore failure lost");
            h.FailRestore = false; c.Tick(Epoch.AddSeconds(5));
            Check(c.Status.Key == "cc_disabled" && c.Baseline == null && h.Curve.CpuSpeed.SequenceEqual(Speeds), "retry missing");
        });
        Test("live native mode change disarms and preserves the selected mode", () =>
        {
            var h = new FakeHardware(); using var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true); c.Tick(Epoch);
            h.Mode = 0x1D; h.Time = Epoch.AddSeconds(5); c.Tick(h.Time);
            Check(!c.Status.Enabled && h.Mode == 0x1D && h.Curve.CpuSpeed.SequenceEqual(Speeds), "native mode overridden");
        });
        Test("live read failure and disposal stop further control", () =>
        {
            var h = new FakeHardware(); var c = new CrossCoolingController(h, 150); c.Configure(LiveSettings(), true); c.Tick(Epoch);
            h.FailRead = true; c.Tick(Epoch); Check(!c.Status.Enabled && h.Restores == 1, "read failure left control active");
            h.FailRead = false; c.Configure(LiveSettings(), true); c.Tick(Epoch); c.Dispose(); c.Resume(); c.Tick(Epoch);
            Check(h.Applies == 2 && h.Restores == 2, "write after disposal");
        });
        Test("live Off never acquires hardware", () =>
        {
            var h = new FakeHardware { FailRead = true }; using var c = new CrossCoolingController(h, 150);
            c.Configure(new(), true); c.Tick(Epoch); Check(!c.Status.Enabled && h.Applies == 0 && h.Restores == 0, "off writes");
        });
    }

    private static void CoolingPersistenceTest()
    {
        var app = new AppSettings
        {
            CrossCooling = new()
            {
                Mode = CrossCoolingMode.Both, Algorithm = CoolingMix.WeightedAverage,
                CpuWeight = 75, GpuWeight = 25, SamplingSeconds = 3, UpdateSeconds = 10,
                Threshold = 7, IncreaseHysteresis = 2, DecreaseHysteresis = 8, DecreaseStep = 4,
                BoostPercent = 60, Smoothing = true,
            },
        };
        app.EnsureDefaults();
        var h = new FakeHardware { Time = DateTimeOffset.UtcNow };
        using (var controller = new CrossCoolingController(h, 150))
        {
            var deps = Activator.CreateInstance<MainDeps>();
            void Set(string name, object value) => typeof(MainDeps).GetProperty(name)!.SetValue(deps, value);
            Set("Settings", app); Set("SaveSettings", (Action)app.Save);
            Set("Current", (Func<ProfileId>)(() => ProfileId.Balanced));
            Set("CoolingStatus", (Func<CoolingStatus>)(() => controller.Status));
            Set("ConfigureCooling", (Action<bool>)(on => { controller.Configure(app.CrossCooling, on); controller.Tick(h.Time); }));
            using var panel = new CrossCoolingPanel(deps, () => null, () => default);
            var toggle = (ToggleSwitch)typeof(CrossCoolingPanel).GetField("_enable", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
            typeof(ToggleSwitch).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(toggle, new object[] { EventArgs.Empty });
            Check(app.CrossCooling.Enabled && controller.Status.Enabled, "enable was not saved");
            Check(app.Clone().CrossCooling.Enabled, "clone lost enabled state");
            var imported = new AppSettings(); imported.ImportFrom(app);
            Check(imported.CrossCooling.Enabled, "import lost enabled state");
        }
        Check(app.CrossCooling.Enabled && h.Mode == 0x0D, "normal exit forgot preference or failed restore");
        var loaded = AppSettings.Load();
        Check(JsonSerializer.Serialize(loaded.CrossCooling) == JsonSerializer.Serialize(app.CrossCooling),
            "disk roundtrip lost an option: " + JsonSerializer.Serialize(loaded.CrossCooling) + " expected " + JsonSerializer.Serialize(app.CrossCooling));
        var nextHardware = new FakeHardware { Time = DateTimeOffset.UtcNow };
        using var nextController = new CrossCoolingController(nextHardware, 150);
        nextController.Configure(loaded.CrossCooling, loaded.CrossCooling.Enabled); nextController.Tick(nextHardware.Time);
        Check(nextController.Status.Enabled && nextHardware.Applies == 1, "restart did not restore enabled choice");
        loaded.CrossCooling.Enabled = false; loaded.Save(); nextController.Configure(loaded.CrossCooling, false);
        var disabled = AppSettings.Load();
        Check(!disabled.CrossCooling.Enabled && disabled.CrossCooling.Mode == CrossCoolingMode.Both, "disabled choice or selected mode lost");
        nextController.Configure(disabled.CrossCooling, disabled.CrossCooling.Enabled); nextController.Tick(nextHardware.Time);
        Check(!nextController.Status.Enabled && nextHardware.Applies == 1, "disabled restarted control");
        var off = new CrossCoolingSettings { Enabled = true, Mode = CrossCoolingMode.Off }; off.Normalize();
        Check(!off.Enabled, "Off mode retained enabled choice");
        Check(!JsonSerializer.Deserialize<AppSettings>("{}")!.CrossCooling.Enabled, "legacy settings activate unexpectedly");
    }

    private static void ContextMenuTest()
    {
        Lang.Set("en");
        Theme.Set(false);
        using var menu = new ThemedContextMenu();
        var normal = new ToolStripMenuItem("Open panel");
        var selected = new ToolStripMenuItem("Selected profile") { Checked = true };
        var disabled = new ToolStripMenuItem("Unavailable") { Enabled = false };
        var custom = new ToolStripMenuItem("Warning") { ForeColor = Color.OrangeRed };
        var parent = new ToolStripMenuItem("Profiles");
        var nested = new ToolStripMenuItem("Nested");
        nested.DropDownItems.Add("Balanced"); parent.DropDownItems.Add(nested);
        menu.Items.AddRange(new ToolStripItem[] { new ToolStripLabel("GhostDeck"), normal, selected,
            disabled, custom, new ToolStripSeparator(), parent });
        int clicks = 0; normal.Click += (_, _) => clicks++;
        var opening = typeof(ThemedContextMenu).GetMethod("OnOpening", BindingFlags.NonPublic | BindingFlags.Instance)!;
        void Open() => opening.Invoke(menu, new object[] { new System.ComponentModel.CancelEventArgs() });
        static bool HasColor(Bitmap bitmap, Color color)
        {
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()) return true;
            return false;
        }
        Bitmap Render(ToolStrip strip)
        {
            strip.Size = strip.GetPreferredSize(Size.Empty); strip.PerformLayout();
            var bitmap = new Bitmap(strip.Width, strip.Height);
            strip.DrawToBitmap(bitmap, strip.ClientRectangle);
            return bitmap;
        }
        foreach (bool dark in new[] { true, false, true })
        {
            Theme.Set(dark); Open();
            Check(menu.BackColor == Theme.Surface && normal.ForeColor == Theme.Text, "root theme");
            foreach (var drop in new[] { parent.DropDown, nested.DropDown })
            {
                Check(drop.BackColor == Theme.Surface && drop.ForeColor == Theme.Text, "nested theme");
                Check(ReferenceEquals(drop.Renderer, menu.Renderer), "nested renderer");
                using var subBitmap = Render(drop);
                Check(HasColor(subBitmap, Theme.Surface), "nested background pixels");
            }
            Check(custom.ForeColor == Color.OrangeRed, "custom text color lost");
            selected.Select();
            using var bitmap = Render(menu);
            Check(HasColor(bitmap, Theme.Surface), "background pixels");
            Check(HasColor(bitmap, Theme.Text), "text/checkmark pixels");
            Check(HasColor(bitmap, Theme.Muted), "disabled text pixels");
            Check(HasColor(bitmap, Theme.Border), "separator pixels");
            Check(HasColor(bitmap, Theme.AccentSoft), "hover/check background pixels");
            Check(bitmap.GetPixel(selected.Bounds.Right - 5, selected.Bounds.Top + 3).ToArgb() == Theme.AccentSoft.ToArgb(),
                "Windows highlight replaced app hover color");
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, dark ? "context-menu-dark.png" : "context-menu-light.png"));
            // Update already constructed menus immediately, without opening/rebuilding them.
            Theme.Toggle();
            Check(menu.BackColor == Theme.Surface && nested.DropDown.BackColor == Theme.Surface, "live theme change");
        }
        normal.PerformClick(); disabled.PerformClick(); Check(clicks == 1, "menu action changed");
        // A dynamically added submenu gets the palette on the next opening.
        var late = new ToolStripMenuItem("New scene"); late.DropDownItems.Add("Run"); menu.Items.Add(late); Open();
        Check(late.DropDown.BackColor == Theme.Surface, "dynamic submenu");
        menu.Dispose(); Theme.Toggle(); Theme.Toggle(); // no disposed handler retained
        Theme.Set(false);
    }

    private static void LiveUiTest()
    {
        var h = new FakeHardware { Time = DateTimeOffset.UtcNow };
        using var c = new CrossCoolingController(h, 150);
        var d = Activator.CreateInstance<MainDeps>();
        void Set(string name, object value) => typeof(MainDeps).GetProperty(name)!.SetValue(d, value);
        Set("Settings", new AppSettings { CrossCooling = LiveSettings() });
        Set("Current", (Func<ProfileId>)(() => ProfileId.Balanced)); Set("SaveSettings", (Action)(() => { }));
        Set("CoolingStatus", (Func<CoolingStatus>)(() => c.Status));
        Set("ConfigureCooling", (Action<bool>)(on => { c.Configure(d.Settings.CrossCooling, on); c.Tick(h.Time); }));
        using var panel = new CrossCoolingPanel(d, () => null, () => default);
        var toggle = (ToggleSwitch)typeof(CrossCoolingPanel).GetField("_enable", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        Check(!toggle.Checked, "saved mode falsely shows active control");
        var click = typeof(ToggleSwitch).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        click.Invoke(toggle, new object[] { EventArgs.Empty });
        Check(toggle.Checked && c.Status.Enabled && h.Applies == 1, "switch did not activate hardware adapter");
        var label = (Label)typeof(CrossCoolingPanel).GetField("_values", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        Check(label.Text.Contains("Calculated targets") && !label.Text.Contains("live control disabled"), "live display wrong");
        panel.Rebuild(); panel.Visible = false;
        Check(c.Status.Enabled && h.Restores == 0, "hiding page disabled tray controller");
        click.Invoke(toggle, new object[] { EventArgs.Empty });
        Check(!c.Status.Enabled && !toggle.Checked && h.Restores == 1, "switch did not restore");
        Check(d.Settings.CrossCooling.Mode == CrossCoolingMode.Both, "disable lost saved mode");
    }

    private static void UiTest()
    {
        var d = Activator.CreateInstance<MainDeps>();
        void Set(string name, object value) => typeof(MainDeps).GetProperty(name)!.SetValue(d, value);
        Set("Settings", new AppSettings()); Set("Current", (Func<ProfileId>)(() => ProfileId.Balanced));
        Set("SaveSettings", (Action)(() => { })); Set("CoolerBoost", (Func<bool>)(() => false));
        using var panel = new CrossCoolingPanel(d, () => null, () => default);
        panel.CreateControl();
        foreach (string lang in Lang.Codes)
        {
            Lang.Set(lang); Theme.Set(true); panel.Width = 400; panel.Height = 500; panel.Rebuild();
            Theme.Set(false); panel.Rebuild();
            Check(panel.Controls.Count == 1, "rebuild leaked root");
        }
        Lang.Set("en");
        var field = typeof(CrossCoolingPanel).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var combo = (ThemedComboBox)field.GetValue(panel)!;
        foreach (var mode in Enum.GetValues<CrossCoolingMode>()) { combo.SelectedIndex = (int)mode; Check(d.Settings.CrossCooling.Mode == mode, "mode UI"); }
        panel.UpdateStatus(); panel.Rebuild();
        Check(!combo.IsDisposed, "retained control disposed");
        Check(typeof(CrossCoolingPanel).GetField("_simulate", BindingFlags.NonPublic | BindingFlags.Instance) == null,
            "simulated temperature controls remain");
    }

    private static void PageTest()
    {
        var d = Activator.CreateInstance<MainDeps>();
        void Set(string name, object value) => typeof(MainDeps).GetProperty(name)!.SetValue(d, value);
        Set("Settings", new AppSettings()); Set("Current", (Func<ProfileId>)(() => ProfileId.Balanced));
        Set("SaveSettings", (Action)(() => { })); Set("SettingsChanged", (Action)(() => { }));
        Set("CoolerBoost", (Func<bool>)(() => false)); Set("Writable", (Func<bool>)(() => false));
        Set("Firmware", (Func<string>)(() => "")); Set("Simulating", (Func<bool>)(() => true));
        Set("ColorOf", (Func<ProfileId, Color>)(_ => Theme.Accent));
        Set("Status", (Func<StatusInfo>)(() => new(ProfileId.Balanced, false, false, "", "", Theme.Text, 0, TimeSpan.Zero, false, "test")));
        using var host = new Form { Size = new Size(1000, 900), ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        using var page = new FanCurvePage(d);
        host.Controls.Add(page); host.CreateControl(); page.CreateControl();
        host.Show();
        var select = typeof(FanCurvePage).GetMethod("SelectView", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var panel = (CrossCoolingPanel)typeof(FanCurvePage).GetField("_cross", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        select.Invoke(page, new object[] { 4, true });
        Check(panel.Visible, "Cross-Cooling panel is hidden");
        Check(d.Settings.FanCurveView == 4, "view persistence");
        panel.UpdateStatus();
        var values = (Label)typeof(CrossCoolingPanel).GetField("_values", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        Check(values.Text.Contains("Hardware readings unavailable"), "fabricated hardware readings");
        foreach (bool dark in new[] { true, false })
        {
            Theme.Set(dark); page.ApplyTheme(); page.OnLanguageChanged();
            page.PerformLayout(); panel.PerformLayout();
            using var bitmap = new Bitmap(page.Width, page.Height);
            page.DrawToBitmap(bitmap, page.ClientRectangle);
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, dark ? "cross-cooling-dark.png" : "cross-cooling-light.png"));
        }
        foreach (int view in new[] { 0, 1, 2, 3, 4 }) select.Invoke(page, new object[] { view, true });
        Set("Firmware", (Func<string>)(() => "17S1IMS1.114"));
        using var supportedPage = new FanCurvePage(d);
        host.Controls.Remove(page); host.Controls.Add(supportedPage);
        select.Invoke(supportedPage, new object[] { 4, false });
        select.Invoke(supportedPage, new object[] { 0, false });
        var curveSwitch = (ToggleSwitch)typeof(FanCurvePage).GetField("_enable", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(supportedPage)!;
        Check(curveSwitch.Visible, "editing controls were not restored after Cross-Cooling");
        Theme.Set(false);
    }
}
