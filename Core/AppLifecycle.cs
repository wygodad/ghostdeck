using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GhostDeck;

/// <summary>
/// Shutdown awareness + last-chance exception handling.
///
/// Every EC read goes through WMI, and WMI stops serving BEFORE our process is killed on a
/// Windows shutdown / logoff: each Get_Data then throws ManagementException "Shutting down".
/// The overlay timer polled the EC once a second straight on the UI thread, so that exception
/// escaped into the message loop and WinForms put up its ThreadExceptionDialog - a crash box on
/// the way down (reported on v1.23.1).
///
/// Two layers: <see cref="ShuttingDown"/> stops the pollers as soon as Windows announces the
/// session is ending, and the installed handlers make sure NO stray exception can ever raise a
/// dialog again - transient WMI noise is dropped, anything else is appended to errors.log.
/// </summary>
public static class AppLifecycle
{
    /// <summary>Windows is logging off / shutting down: stop touching the EC.</summary>
    public static volatile bool ShuttingDown;

    private static string LogPath => Path.Combine(AppSettings.Dir, "errors.log");
    private const long MaxLogBytes = 128 * 1024;

    /// <summary>Wired once from Program.Main, before the message loop starts.</summary>
    public static void Install()
    {
        SystemEvents.SessionEnding += (_, _) => ShuttingDown = true;
        SystemEvents.SessionEnded += (_, _) => ShuttingDown = true;

        // CatchException routes UI-thread exceptions to our handler instead of the built-in
        // "unhandled exception" dialog with Continue/Quit.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception, "ui");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, "domain");
    }

    /// <summary>
    /// True for failures that only mean "WMI is momentarily out": nothing to fix, nothing worth
    /// telling the user about. The caller skips that one read and tries again next time - a failed
    /// EC call drops the shared WMI session, so the next one reconnects on its own.
    /// </summary>
    public static bool IsTransient(Exception? ex)
    {
        switch (ex)
        {
            // NOTE: ManagementStatus.ShuttingDown (WBEM_E_SHUTTING_DOWN) does NOT mean Windows is
            // shutting down. It is what WMI answers while its provider host (WmiPrvSE.exe) or the
            // Winmgmt service is being recycled, which happens during normal work. It must never
            // set the ShuttingDown flag - that would stop all EC polling for the rest of the session.
            case ManagementException me:
                return me.ErrorCode is ManagementStatus.ShuttingDown or ManagementStatus.CallCanceled
                    or ManagementStatus.ServerTooBusy or ManagementStatus.Timedout;
            // RPC server unavailable / call failed: the WMI host is restarting or already gone
            case COMException com:
                return (uint)com.HResult is 0x800706BA or 0x800706BE or 0x80010108;
            default:
                return ShuttingDown;
        }
    }

    /// <summary>
    /// Human-readable reason for an EC access failure, for the report wizard's error box.
    /// The interesting case is `ManagementStatus.NotSupported` ("Unsupported"): the firmware
    /// answers the WMI call but refuses that request. Seen on a Delta 15 A5EFK (issue #48)
    /// whose MSI_ACPI does declare Get_Data/Set_Data, so the message stays factual about what
    /// happened and asks for a report instead of blaming a missing interface.
    /// </summary>
    public static string DescribeEcFailure(Exception? ex) => ex switch
    {
        ManagementException { ErrorCode: ManagementStatus.NotSupported } => Lang.T("ec_err_unsupported"),
        ManagementException { ErrorCode: ManagementStatus.AccessDenied } => Lang.T("ec_err_denied"),
        InvalidOperationException => Lang.T("ec_err_missing"),
        _ => ex?.Message ?? "",
    };

    /// <summary>Swallow a transient failure, record anything else. Never throws.</summary>
    public static void Report(Exception? ex, string where)
    {
        if (ex == null || IsTransient(ex)) return;
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes) File.Delete(LogPath);
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
