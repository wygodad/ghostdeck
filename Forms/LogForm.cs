namespace GhostDeck;

/// <summary>
/// Full history-log window: every recorded profile / EC change (time, source, written bytes,
/// readback). Opened from the tray menu ("Change log") or the Status tab button. Read-only view
/// of <see cref="ChangeLog"/>; refreshes live as new entries arrive. Copy / Clear helpers assist
/// with model-support reports.
/// </summary>
public sealed class LogForm : Form
{
    private static LogForm? _open;

    private readonly ListView _list = new BufferedListView();
    private readonly Action _onChanged;

    // ListView repaints whole rows on hover/selection; without double buffering the
    // owner-drawn cells flicker visibly.
    private sealed class BufferedListView : ListView
    {
        public BufferedListView() { DoubleBuffered = true; }
    }

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
        Icon = TrayIconFactory.AppIcon();
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 640);
        MinimumSize = new Size(820, 460);
        Font = new Font("Segoe UI", 9.5f);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = false;
        _list.BorderStyle = BorderStyle.None;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.Columns.Add(Lang.T("log_col_time"), 180);
        _list.Columns.Add(Lang.T("log_col_source"), 160);
        _list.Columns.Add(Lang.T("log_col_detail"), 470);
        _list.Columns.Add(Lang.T("log_col_result"), 290);

        // Owner-drawn cells: the stock ListView paints a glaring white-on-black grid in dark
        // mode. Alternating themed row tints + accent-soft selection keep it readable.
        _list.OwnerDraw = true;
        _list.DrawItem += (_, e) => { };   // cells are painted per-subitem below
        _list.DrawColumnHeader += (_, e) =>
        {
            using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            using var hf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", hf,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        _list.DrawSubItem += (_, e) =>
        {
            bool sel = e.Item?.Selected == true;
            var bg = sel ? Theme.AccentSoft : e.ItemIndex % 2 == 0 ? Theme.Card : Theme.Surface;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            // column colours: muted time, accent source, plain detail, muted readback
            var fg = e.ColumnIndex switch { 0 => Theme.Muted, 1 => Theme.Accent, 3 => Theme.Muted, _ => Theme.Text };
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", _list.Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
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
        Ui.StyleGhost(close);
        close.Click += (_, _) => Close();
        var copy = new Button { Text = Lang.T("log_copy_all"), AutoSize = true, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(6, 0, 0, 0) };
        Ui.StyleGhost(copy);
        copy.Click += (_, _) => { try { Clipboard.SetText(ChangeLog.ToText()); } catch { } };
        var clear = new Button { Text = Lang.T("log_clear"), AutoSize = true, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(6, 0, 0, 0) };
        Ui.StyleGhost(clear);
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
