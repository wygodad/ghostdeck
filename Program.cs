using System.Threading;

namespace GhostDeck;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Signed model-database override (ModelDb): a downloaded, verified, NEWER database
        // replaces the compiled tables for this run - for the tray app and the CLI alike.
        if (ModelDb.LoadOverride() is { } db) Devices.ApplyOverride(db);

        // Any argument = CLI mode: forwarded to the running instance over the pipe, or executed
        // one-shot against the EC. The tray app itself never starts with arguments.
        if (args.Length > 0) return Cli.Run(args);

        // Tray start. A running instance only has to show its window - any process may ask it,
        // elevated or not. Otherwise the tray needs the EC, so an unelevated launch (double-click,
        // a pinned icon) goes through the one UAC prompt and ends here; the autostart task starts
        // elevated already (RL HIGHEST) and never sees that prompt.
        if (Elevation.SignalRunningInstance()) return 0;
        if (!Elevation.IsElevated) return Elevation.Relaunch(args, wait: false);

        Taskbar.SetProcessId();   // the jump list and the pinned icon hang on this identity
        using var showSignal = Elevation.CreateShowEvent();
        using var mtx = new Mutex(true, "GhostDeck_SingleInstance", out bool createdNew);
        if (!createdNew) { showSignal.Set(); return 0; }   // already running - ask it to show its window

        Updater.CleanupAfterUpdate();   // drop leftover GhostDeck.update.exe / .bak from a previous update
        AppLifecycle.Install();         // shutdown flag + no WinForms crash dialog, ever
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
        return 0;
    }
}
