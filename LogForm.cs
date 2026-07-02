namespace MSIProfileSwitcher;

/// <summary>
/// Full history-log window: every recorded profile / EC change (time, source, written bytes,
/// readback). Opened from the tray menu ("Change log") or the Status tab button. Read-only view
/// of <see cref="ChangeLog"/>; refreshes live as new entries arrive. Copy / Clear helpers assist
/// with model-support reports.
/// </summary>
public sealed class LogForm : Form
{
    private static LogForm? _open;

    private readonly ListView _list = new();
    private readonly Action _onChanged;

    public static void ShowSingleton()
    {
        if (_open is { IsDisposed: false })
        {
            _open.WindowState = FormWindowState.Normal;
            _open.BringToFront();
            _open.Activate();
            return;
        }
        _open = new LogForm();
        _open.Show();
    }

    public LogForm()
    {
        Text = Lang.T("log_title");
        Icon = TrayIconFactory.Create(Theme.Accent);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 640);
        MinimumSize = new Size(820, 460);
        Font = new Font("Segoe UI", 9.5f);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.Columns.Add(Lang.T("log_col_time"), 180);
        _list.Columns.Add(Lang.T("log_col_source"), 160);
        _list.Columns.Add(Lang.T("log_col_detail"), 470);
        _list.Columns.Add(Lang.T("log_col_result"), 290);
        _list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C) { CopySelected(); e.Handled = e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in _list.Items) it.Selected = true; e.Handled = true; }
        };

        // AutoSize bottom bar so the buttons are never clipped, whatever the DPI scaling.
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(12, 10, 12, 12),
        };
        var close = new Button { Text = Lang.T("set_close"), AutoSize = true, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(6, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = Lang.T("log_copy_all"), AutoSize = true, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(6, 0, 0, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText(ChangeLog.ToText()); } catch { } };
        var clear = new Button { Text = Lang.T("log_clear"), AutoSize = true, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(6, 0, 0, 0) };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, Lang.T("log_clear_confirm"), Lang.T("log_title"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                ChangeLog.Clear();
        };
        bar.Controls.Add(close);
        bar.Controls.Add(copy);
        bar.Controls.Add(clear);

        // Add the docked bar first, then the Fill list, so the list fills the space above the bar.
        Controls.Add(bar);
        Controls.Add(_list);

        ApplyTheme();
        Reload();

        _onChanged = () => { if (!IsDisposed) BeginInvoke(Reload); };
        ChangeLog.Changed += _onChanged;
        FormClosed += (_, _) => { ChangeLog.Changed -= _onChanged; if (_open == this) _open = null; };
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Surface;
        _list.BackColor = Theme.Card;
        _list.ForeColor = Theme.Text;
    }

    private void Reload()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var e in ChangeLog.All())
        {
            var it = new ListViewItem(e.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            it.SubItems.Add(ChangeLog.SourceLabel(e.Source));
            it.SubItems.Add(e.Detail);
            it.SubItems.Add(e.Result);
            _list.Items.Add(it);
        }
        _list.EndUpdate();
    }

    private void CopySelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        foreach (ListViewItem it in _list.SelectedItems)
            sb.AppendLine(string.Join("\t", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text)));
        try { Clipboard.SetText(sb.ToString()); } catch { }
    }
}
