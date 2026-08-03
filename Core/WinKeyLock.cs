using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// Windows-key lock: a low-level keyboard hook (WH_KEYBOARD_LL) swallows both Windows keys
/// while enabled, so a game never loses focus to an accidental Start menu. Same rules as
/// TrayWheel: the hook lives on its own message-loop thread and its callback must stay fast
/// and independent of the UI thread (Windows silently drops slow low-level hooks).
/// The hook is installed only while the lock is on; install/uninstall happen ON the hook
/// thread (posted as thread messages), so no cross-thread races on the hook handle.
/// Blocking is total: every Win combo including Win+L is blocked; Ctrl+Alt+Del is out of
/// a hook's reach and keeps working. A panic reset always lifts the lock.
/// </summary>
public sealed class WinKeyLock : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_APP_LOCK = 0x8001;      // WPARAM: 1 = install hook, 0 = remove it
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint VkCode; public uint ScanCode; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr HWnd; public uint Message; public IntPtr WParam; public IntPtr LParam; public uint Time; public POINT Pt; }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);

    private readonly HookProc _proc;                       // field keeps the delegate alive for the native hook
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _hook;                                  // touched only on the hook thread
    private uint _threadId;
    private volatile bool _on;

    public WinKeyLock() { _proc = Callback; }

    public bool Enabled => _on;

    public void Set(bool on)
    {
        if (on == _on) return;
        _on = on;
        EnsureThread();
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_APP_LOCK, on ? 1 : IntPtr.Zero, IntPtr.Zero);
    }

    private void EnsureThread()
    {
        if (_threadId != 0) return;
        new Thread(Loop) { IsBackground = true, Name = "GhostDeckWinLock", Priority = ThreadPriority.AboveNormal }.Start();
        _ready.Wait(2000);   // wait for the message queue to exist so the first post isn't lost
    }

    private void Loop()
    {
        PeekMessageW(out _, IntPtr.Zero, 0, 0, 0);   // force-create this thread's message queue
        _threadId = GetCurrentThreadId();
        _ready.Set();
        while (GetMessageW(out var m, IntPtr.Zero, 0, 0) > 0)
        {
            if (m.Message != WM_APP_LOCK) continue;
            bool want = m.WParam != IntPtr.Zero;
            if (want && _hook == IntPtr.Zero)
                _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
            else if (!want && _hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _threadId = 0;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _on)
        {
            try
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (info.VkCode is VK_LWIN or VK_RWIN) return 1;   // swallow key-down AND key-up
            }
            catch { }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _on = false;
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
