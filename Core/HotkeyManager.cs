using System.Runtime.InteropServices;

namespace GhostDeck;

public static class Hk
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
}

/// <summary>Globalne skroty przez RegisterHotKey na ukrytym oknie komunikatow.</summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager() => CreateHandle(new CreateParams());

    public bool Register(uint mods, uint vk, Action onPressed)
    {
        if (vk == 0) return false;
        int id = _nextId++;
        if (!RegisterHotKey(Handle, id, mods | Hk.MOD_NOREPEAT, vk))
            return false;
        _actions[id] = onPressed;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToList())
            UnregisterHotKey(Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var act))
            act();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
