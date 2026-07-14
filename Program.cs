using System.Threading;

namespace GhostDeck;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Any argument = CLI mode: forwarded to the running instance over the pipe, or executed
        // one-shot against the EC. The tray app itself never starts with arguments.
        if (args.Length > 0) return Cli.Run(args);

        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "GhostDeck_ShowMainWindow");
        using var mtx = new Mutex(true, "GhostDeck_SingleInstance", out bool createdNew);
        if (!createdNew) { showSignal.Set(); return 0; }   // already running - ask it to show its window

        Updater.CleanupAfterUpdate();   // drop leftover GhostDeck.update.exe / .bak from a previous update
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
        return 0;
    }
}
