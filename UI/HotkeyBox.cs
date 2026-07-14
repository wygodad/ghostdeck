using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>Pole przechwytujace kombinacje klawiszy (do rebindu skrotow).</summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyDef Value { get; private set; } = new();

    public HotkeyBox()
    {
        ReadOnly = true;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        ShortcutsEnabled = false;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Theme.Surface;
    }

    // The stock FixedSingle border is drawn by Windows in a light system colour, which glows
    // white in dark mode (discussion #9). Overpaint the 1px non-client frame with the theme
    // border after every paint; colours are read live so a theme switch just needs Invalidate.
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    private const int WM_PAINT = 0x000F, WM_NCPAINT = 0x0085;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is WM_PAINT or WM_NCPAINT) PaintBorder();
    }

    private void PaintBorder()
    {
        IntPtr dc = GetWindowDC(Handle);
        if (dc == IntPtr.Zero) return;
        try
        {
            using var g = Graphics.FromHdc(dc);
            using var p = new Pen(Focused ? Theme.Accent : Theme.BorderStrong);
            g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
        finally { ReleaseDC(Handle, dc); }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    public void SetValue(HotkeyDef def)
    {
        Value = def.Clone();
        Text = string.IsNullOrEmpty(Value.Display) ? "(brak)" : Value.Display;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Focused)
        {
            HandleKey(keyData);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void HandleKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;

        if (key is Keys.Escape or Keys.Back or Keys.Delete)
        {
            Value = new HotkeyDef();
            Text = "(brak)";
            return;
        }

        if (key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin or Keys.None)
            return; // sam modyfikator

        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Hk.MOD_CONTROL;
        if ((keyData & Keys.Alt) != 0)     mods |= Hk.MOD_ALT;
        if ((keyData & Keys.Shift) != 0)   mods |= Hk.MOD_SHIFT;

        var disp = HotkeyText.Format(mods, key);
        Value = new HotkeyDef { Mods = mods, Vk = (uint)key, Display = disp };
        Text = disp;
    }
}

public static class HotkeyText
{
    public static string Format(uint mods, Keys key)
    {
        var parts = new List<string>();
        if ((mods & Hk.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & Hk.MOD_ALT) != 0)     parts.Add("Alt");
        if ((mods & Hk.MOD_SHIFT) != 0)   parts.Add("Shift");
        if ((mods & Hk.MOD_WIN) != 0)     parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        _ => key.ToString(),
    };
}
