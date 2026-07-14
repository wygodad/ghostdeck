namespace GhostDeck;

/// <summary>Minimal themed one-line text prompt (used for fan-curve preset names).</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _box = new();

    private InputDialog(string title, string label, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        ClientSize = new Size(380, 128);
        BackColor = Theme.Surface;
        Icon = TrayIconFactory.AppIcon();

        var lbl = new Label
        {
            Text = label, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Surface,
            Font = new Font("Segoe UI", 10.5f), Location = new Point(16, 14),
        };
        _box.SetBounds(16, 42, ClientSize.Width - 32, 28);
        _box.Text = initial;
        _box.BackColor = Theme.Card; _box.ForeColor = Theme.Text;
        _box.BorderStyle = BorderStyle.FixedSingle;
        _box.Font = new Font("Segoe UI", 10.5f);

        var ok = new Button { Text = Lang.T("gen_ok"), DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
        var cancel = new Button { Text = Lang.T("gen_cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
        Ui.StylePrimary(ok); ok.Height = 32;
        Ui.StyleGhost(cancel); cancel.Height = 32;
        ok.Location = new Point(ClientSize.Width - 16 - 90 - 8 - ok.PreferredSize.Width, 84);
        cancel.Location = new Point(ClientSize.Width - 16 - cancel.PreferredSize.Width, 84);

        Controls.AddRange(new Control[] { lbl, _box, ok, cancel });
        AcceptButton = ok; CancelButton = cancel;
        Shown += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>Returns the trimmed text, or null when cancelled/empty.</summary>
    public static string? Ask(IWin32Window? owner, string title, string label, string initial = "")
    {
        using var dlg = new InputDialog(title, label, initial);
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        string t = dlg._box.Text.Trim();
        return t.Length == 0 ? null : t;
    }
}
