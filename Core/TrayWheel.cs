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
/// Mouse-wheel support for the tray icons (#23). Windows never routes wheel messages to
/// notification icons, so a low-level mouse hook (WH_MOUSE_LL) watches WM_MOUSEWHEEL and
/// matches the cursor against the watched icons' screen rectangles (Shell_NotifyIconGetRect) -
/// the main icon plus, when shown, the temperature icons (SetIcons).
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

    // One watched notification icon. Identity (message window handle + id) is resolved once
    // via reflection; the icon's screen rect is cached briefly so a wheel spin does one shell
    // query per icon, not one per notch. Rect/identity fields are touched only on the hook
    // thread; the UI thread swaps whole arrays (volatile _tracked below), never entries.
    private sealed class Tracked
    {
        public readonly NotifyIcon Icon;
        public IntPtr Hwnd;
        public uint Id;
        public RECT Rect;
        public long FetchedTicks;
        public Tracked(NotifyIcon icon) => Icon = icon;
    }

    private readonly SynchronizationContext _ui;
    private readonly Action<int> _onWheel;      // raw wheel delta (multiples of ±120), on the UI thread
    private readonly HookProc _proc;            // field keeps the delegate alive for the native hook
    private IntPtr _hook;
    private uint _threadId;
    private volatile bool _stopped;
    private volatile Tracked[] _tracked;

    public TrayWheel(NotifyIcon icon, SynchronizationContext ui, Action<int> onWheel)
    {
        _tracked = new[] { new Tracked(icon) };
        _ui = ui;
        _onWheel = onWheel;
        _proc = Callback;
        new Thread(Loop) { IsBackground = true, Name = "GhostDeckTrayWheel", Priority = ThreadPriority.AboveNormal }.Start();
    }

    /// <summary>
    /// Replace the set of icons the wheel reacts over (the main icon plus any temperature
    /// icons - they mirror the main icon's mouse actions). Still ONE global hook; the callback
    /// just checks a few rectangles instead of one. No-op when the set is unchanged, so it is
    /// safe to call on every poll. Entries that stay keep their resolved identity and rect.
    /// </summary>
    public void SetIcons(NotifyIcon[] icons)
    {
        var cur = _tracked;
        if (cur.Length == icons.Length)
        {
            bool same = true;
            for (int i = 0; i < icons.Length; i++)
                if (!ReferenceEquals(cur[i].Icon, icons[i])) { same = false; break; }
            if (same) return;
        }
        var next = new Tracked[icons.Length];
        for (int i = 0; i < icons.Length; i++)
        {
            Tracked? kept = null;
            foreach (var t in cur) if (ReferenceEquals(t.Icon, icons[i])) { kept = t; break; }
            next[i] = kept ?? new Tracked(icons[i]);
        }
        _tracked = next;
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
                if (OverAnyIcon(info.Pt.X, info.Pt.Y))
                {
                    int delta = (short)(info.MouseData >> 16);
                    _ui.Post(_ => { if (!_stopped) _onWheel(delta); }, null);
                }
            }
            catch { }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool OverAnyIcon(int x, int y)
    {
        // Both the hook coordinates and Shell_NotifyIconGetRect are physical pixels for a
        // PerMonitorV2 process, so the comparison needs no DPI conversion.
        long now = Environment.TickCount64;
        foreach (var t in _tracked)
        {
            if (now - t.FetchedTicks > 1500)
            {
                if (!FetchRect(t)) continue;   // unresolved/disposed icon -> just skip it
                t.FetchedTicks = now;
            }
            if (x >= t.Rect.Left && x < t.Rect.Right && y >= t.Rect.Top && y < t.Rect.Bottom)
                return true;
        }
        return false;
    }

    private static bool FetchRect(Tracked t)
    {
        if (t.Hwnd == IntPtr.Zero && !ResolveIconIdentity(t)) return false;
        var ident = new NOTIFYICONIDENTIFIER { CbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), HWnd = t.Hwnd, UId = t.Id };
        return Shell_NotifyIconGetRect(ref ident, out t.Rect) == 0;
    }

    // WinForms offers no public access to the Shell_NotifyIcon identity, so read the private
    // id + message-window fields (named "_id"/"_window" on .NET Core, "id"/"window" on
    // Framework). The id is uint on .NET 8 and int on Framework - convert, don't pattern-match.
    private static bool ResolveIconIdentity(Tracked t)
    {
        try
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var ty = typeof(NotifyIcon);
            var idField = ty.GetField("_id", F) ?? ty.GetField("id", F);
            var winField = ty.GetField("_window", F) ?? ty.GetField("window", F);
            if (idField?.GetValue(t.Icon) is not { } idVal) return false;
            if (winField?.GetValue(t.Icon) is not NativeWindow win || win.Handle == IntPtr.Zero) return false;
            t.Hwnd = win.Handle;
            t.Id = Convert.ToUInt32(idVal);
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
