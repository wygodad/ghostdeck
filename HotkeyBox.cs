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
        BackColor = Color.White;
    }

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
