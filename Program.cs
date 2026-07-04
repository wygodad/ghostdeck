using System.Threading;

namespace MSIProfileSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mtx = new Mutex(true, "GhostDeck_SingleInstance", out bool createdNew);
        if (!createdNew) return;   // juz uruchomione

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
    }
}
