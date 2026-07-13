using System.Threading;

namespace GhostDeck;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "GhostDeck_ShowMainWindow");
        using var mtx = new Mutex(true, "GhostDeck_SingleInstance", out bool createdNew);
        if (!createdNew) { showSignal.Set(); return; }   // already running - ask it to show its window

        Updater.CleanupAfterUpdate();   // drop leftover GhostDeck.update.exe / .bak from a previous update
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
    }
}
