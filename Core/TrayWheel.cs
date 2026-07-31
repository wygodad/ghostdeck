using System.Runtime.InteropServices;

namespace GhostDeck;

// (#23) Configurable tray-icon mouse actions. Stored in settings as ints; the order shown
// in the Settings combo lives in SettingsPage.TrayActionOrder.
public enum TrayAction
{
    None = 0,
    CycleProfile = 1,     // next profile (the historical left-click behaviour)
    FanBoost = 2,
    Overlay = 3,
    ShowState = 4,        // current state as an OSD toast
    PanicReset = 5,
    OpenScenarios = 6,
    OpenStatus = 7,
    OpenFanCurve = 8,
    OpenSettings = 9,
    OpenModels = 10,
    OpenChangeLog = 11,
}

// (#23) What the scroll wheel over the tray icon does.
public enum TrayWheelMode
{
    None = 0,
    Profiles = 1,         // wheel up = next profile, wheel down = previous
    Scenes = 2,           // (#21) wheel through scenes, applied when the wheel rests
    KbdLight = 3,         // (#26) backlight level up/down (supported models)
}

/// <summary>
/// Mouse-wheel support for the tray icon (#23). Windows never routes wheel messages to
/// notification icons, so a low-level mouse hook (WH_MOUSE_LL) watches WM_MOUSEWHEEL and
/// matches the cursor against the icon's screen rectangle (Shell_NotifyIconGetRect).
/// The hook lives on its own message-loop thread: its callback must stay fast and must
/// never wait on a busy UI thread, or every mouse event in the system would lag behind it
/// (Windows silently drops hooks that exceed its low-level-hook timeout).
/// Wheel steps are posted to the UI thread; everything else passes straight through.
/// </summary>
public sealed class TrayWheel : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT Pt; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER { public int CbSize; public IntPtr HWnd; public uint UId; public Guid GuidItem; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr HWnd; public uint Message; public IntPtr WParam; public IntPtr LParam; public uint Time; public POINT Pt; }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

    private readonly NotifyIcon _icon;
    private readonly SynchronizationContext _ui;
    private readonly Action<int> _onWheel;      // raw wheel delta (multiples of ±120), on the UI thread
    private readonly HookProc _proc;            // field keeps the delegate alive for the native hook
    private IntPtr _hook;
    private uint _threadId;
    private volatile bool _stopped;

    // Icon identity (message window handle + id) resolved once via reflection; the icon's
    // screen rect is cached briefly so a wheel spin does one shell query, not one per notch.
    private IntPtr _iconHwnd;
    private uint _iconId;
    private RECT _rect;
    private long _rectFetchedTicks;

    public TrayWheel(NotifyIcon icon, SynchronizationContext ui, Action<int> onWheel)
    {
        _icon = icon;
        _ui = ui;
        _onWheel = onWheel;
        _proc = Callback;
        new Thread(Loop) { IsBackground = true, Name = "GhostDeckTrayWheel", Priority = ThreadPriority.AboveNormal }.Start();
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
        if (_hook == IntPtr.Zero) return;   // hook refused -> wheel silently unavailable, clicks unaffected
        while (!_stopped && GetMessageW(out _, IntPtr.Zero, 0, 0) > 0) { }
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_MOUSEWHEEL)
        {
            try
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (OverIcon(info.Pt.X, info.Pt.Y))
                {
                    int delta = (short)(info.MouseData >> 16);
                    _ui.Post(_ => { if (!_stopped) _onWheel(delta); }, null);
                }
            }
            catch { }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool OverIcon(int x, int y)
    {
        // Both the hook coordinates and Shell_NotifyIconGetRect are physical pixels for a
        // PerMonitorV2 process, so the comparison needs no DPI conversion.
        long now = Environment.TickCount64;
        if (now - _rectFetchedTicks > 1500)
        {
            if (!FetchRect()) return false;
            _rectFetchedTicks = now;
        }
        return x >= _rect.Left && x < _rect.Right && y >= _rect.Top && y < _rect.Bottom;
    }

    private bool FetchRect()
    {
        if (_iconHwnd == IntPtr.Zero && !ResolveIconIdentity()) return false;
        var ident = new NOTIFYICONIDENTIFIER { CbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), HWnd = _iconHwnd, UId = _iconId };
        return Shell_NotifyIconGetRect(ref ident, out _rect) == 0;
    }

    // WinForms offers no public access to the Shell_NotifyIcon identity, so read the private
    // id + message-window fields (named "_id"/"_window" on .NET Core, "id"/"window" on
    // Framework). The id is uint on .NET 8 and int on Framework - convert, don't pattern-match.
    private bool ResolveIconIdentity()
    {
        try
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var t = typeof(NotifyIcon);
            var idField = t.GetField("_id", F) ?? t.GetField("id", F);
            var winField = t.GetField("_window", F) ?? t.GetField("window", F);
            if (idField?.GetValue(_icon) is not { } idVal) return false;
            if (winField?.GetValue(_icon) is not NativeWindow win || win.Handle == IntPtr.Zero) return false;
            _iconHwnd = win.Handle;
            _iconId = Convert.ToUInt32(idVal);
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _stopped = true;
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
