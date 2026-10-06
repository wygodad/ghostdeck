using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace GhostDeck;

/// <summary>
/// Administrator rights asked for by the app, not by the file (v1.38). Up to 1.37 the manifest
/// said requireAdministrator, so EVERY launch of GhostDeck.exe went through a UAC prompt - also
/// the short-lived one that only hands "--profile Silent" to the running instance. A taskbar
/// jump list or a ghostdeck:// link would have meant one prompt per click. The manifest now says
/// asInvoker and the rules are:
/// - the tray app relaunches itself elevated once (the same single prompt as before; none from
///   the autostart task, which runs with the highest privileges, and none after an update, whose
///   restart inherits the elevated token);
/// - a launch with arguments stays unelevated while a running instance can take the command
///   over the pipe; only a one-shot against the EC (no instance running) goes through the prompt.
/// The running app itself has exactly the rights it had before - nothing about the EC changed.
/// The pipe and the "show your window" event get an explicit permission for the logged-in user,
/// because the default permissions of an object created by an elevated process let the same
/// user's unelevated processes read it, not write to it.
/// </summary>
public static class Elevation
{
    public const string ShowEventName = "GhostDeck_ShowMainWindow";

    public static bool IsElevated { get; } = Check();

    private static bool Check()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>
    /// Starts this exe again through the UAC prompt with the same arguments. With
    /// <paramref name="wait"/> the child's exit code comes back (the CLI one-shot, whose console
    /// output the child writes into this process's console); otherwise 0 once the child started.
    /// A declined prompt is 1, never an exception.
    /// </summary>
    public static int Relaunch(string[] args, bool wait)
    {
        try
        {
            var psi = new ProcessStartInfo(ExePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory,   // a one-shot --diag writes its zip where the caller stands
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return 1;
            if (!wait) return 0;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception) { return 1; }   // ERROR_CANCELLED: the user said no
        catch { return 1; }
    }

    /// <summary>Starts the tray app (no arguments) with this process's own rights.</summary>
    public static bool StartTray()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,   // a one-shot --diag writes its zip where the caller stands
            });
            return true;
        }
        catch { return false; }
    }

    /// <summary>The logged-in user's own SID - the filtered (unelevated) token carries the same one.</summary>
    private static SecurityIdentifier UserSid()
    {
        using var id = WindowsIdentity.GetCurrent();
        return id.User ?? new SecurityIdentifier(WellKnownSidType.WorldSid, null);
    }

    /// <summary>The "show your window" event, writable by the user's unelevated processes.</summary>
    public static EventWaitHandle CreateShowEvent()
    {
        var sec = new EventWaitHandleSecurity();
        sec.AddAccessRule(new EventWaitHandleAccessRule(UserSid(), EventWaitHandleRights.FullControl, AccessControlType.Allow));
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, ShowEventName, out _, sec);
    }

    /// <summary>
    /// Asks a running instance to show its window. True when there is one (also when it is an
    /// older build whose event grants nothing - then there is nothing more this process can do).
    /// </summary>
    public static bool SignalRunningInstance()
    {
        try
        {
            if (!EventWaitHandleAcl.TryOpenExisting(ShowEventName, EventWaitHandleRights.Modify, out var ev) || ev == null) return false;
            using (ev) ev.Set();
            return true;
        }
        catch (UnauthorizedAccessException) { return true; }
        catch { return false; }
    }

    /// <summary>The CLI pipe, reachable by the user's unelevated processes (jump list, links, scripts).</summary>
    public static NamedPipeServerStream CreatePipeServer()
    {
        var sec = new PipeSecurity();
        sec.AddAccessRule(new PipeAccessRule(UserSid(), PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(Cli.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                               PipeOptions.None, 0, 0, sec);
    }
}
