namespace GhostDeck;

/// <summary>
/// Editor for the profile → Windows power mode mapping used by "Auto: profile" (Settings →
/// Power → Windows power). One row per profile - the scenario-tile icon in the profile
/// colour, the name, and a ThemedComboBox with the three Windows modes. Same visual
/// conventions as ScheduleRuleForm; commits into <see cref="AppSettings.PowerModeMap"/> on OK,
/// storing only the rows that differ from the defaults (an empty map = the defaults).
/// </summary>
public sealed class PowerMapForm : Form
{
    private static readonly (ProfileId Id, string Name)[] Rows =
    {
        (ProfileId.SuperBattery, "Super Battery"), (ProfileId.Silent, "Silent"),
        (ProfileId.Balanced, "Balanced"), (ProfileId.Extreme, "Extreme"),
    };

    private readonly MainDeps _d;
    private readonly int _rowTop = 18, _rowH = 46;

    public PowerMapForm(MainDeps d)
    {
        _d = d;
        Text = Lang.T("pw_mapping");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        BackColor = Theme.Surface;
        Icon = TrayIconFactory.AppIcon();
        DoubleBuffered = true;

        var modes = new object[] { Lang.T("pwm_req_eff"), Lang.T("pwm_req_bal"), Lang.T("pwm_req_perf") };
        var combos = new ThemedComboBox[Rows.Length];
        int y = _rowTop;
        const int W = 520, comboW = 290;
        for (int i = 0; i < Rows.Length; i++)
        {
            Controls.Add(new Label
            {
                Text = Rows[i].Name, AutoSize = true, Location = new Point(52, y + 6),
                ForeColor = Theme.Text, BackColor = Theme.Surface, Font = new Font("Segoe UI Semibold", 10.5f),
            });
            var cb = new ThemedComboBox { Width = comboW, Location = new Point(W - 18 - comboW, y + 2) };
            cb.Items.AddRange(modes);
            cb.SelectedIndex = PowerPlan.ModeGroupFor(d.Settings, Rows[i].Id);
            combos[i] = cb;
            Controls.Add(cb);
            y += _rowH;
        }
        y += 8;

        var ok = new Button { Text = Lang.T("gen_ok"), AutoSize = true, Padding = new Padding(14, 2, 14, 2), Height = 32 };
        var cancel = new Button { Text = Lang.T("gen_cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(14, 2, 14, 2), Height = 32 };
        Ui.StylePrimary(ok);
        Ui.StyleGhost(cancel);
        ok.Click += (_, _) =>
        {
            var map = d.Settings.PowerModeMap;
            map.Clear();
            for (int i = 0; i < Rows.Length; i++)
            {
                int g = Math.Clamp(combos[i].SelectedIndex, 0, 2);
                if (g != PowerPlan.DefaultModeGroup(Rows[i].Id)) map[Rows[i].Id.ToString()] = g;
            }
            d.Settings.Save();
            DialogResult = DialogResult.OK;
        };
        ClientSize = new Size(W, y + 52);
        cancel.Location = new Point(ClientSize.Width - 18 - cancel.PreferredSize.Width, y);
        ok.Location = new Point(cancel.Left - 8 - ok.PreferredSize.Width, y);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int i = 0; i < Rows.Length; i++)
            IconPainter.Scenario(e.Graphics, Rows[i].Id, new RectangleF(18, _rowTop + i * _rowH + 5, 22, 22), _d.ColorOf(Rows[i].Id), 1.8f);
    }
}
