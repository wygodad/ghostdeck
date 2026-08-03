using System.Globalization;

namespace GhostDeck;

/// <summary>
/// Editor for one schedule rule: scene + weekday chips + a start/end time on a 30-minute
/// grid. Same visual conventions as SceneEditForm; commits into the passed rule on OK.
/// </summary>
public sealed class ScheduleRuleForm : Form
{
    private readonly ScheduleRule _rule;

    public ScheduleRuleForm(MainDeps d, ScheduleRule rule)
    {
        _rule = rule;
        Text = Lang.T("sch_rule_title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        ClientSize = new Size(500, 236);
        BackColor = Theme.Surface;
        Icon = TrayIconFactory.AppIcon();

        int y = 16;
        void L(string text, int lx, int ly) => Controls.Add(new Label
        {
            Text = text, AutoSize = true, Location = new Point(lx, ly),
            ForeColor = Theme.Text, BackColor = Theme.Surface, Font = new Font("Segoe UI", 10f),
        });

        // scene picker (the Settings card only opens this form when at least one scene exists)
        L(Lang.T("sch_scene"), 16, y + 4);
        var scenes = d.Settings.Scenes;
        var scene = new ThemedComboBox { Width = 250 };
        scene.Items.AddRange(scenes.Select(s => (object)s.Name).ToArray());
        scene.SelectedIndex = Math.Max(0, scenes.FindIndex(s => s.Id.Equals(rule.SceneId, StringComparison.OrdinalIgnoreCase)));
        scene.Location = new Point(ClientSize.Width - 16 - scene.Width, y);
        Controls.Add(scene);
        y += 46;

        // weekday chips, Monday first (bit 0 = Monday ... bit 6 = Sunday)
        L(Lang.T("sch_days"), 16, y + 6);
        string[] abbr;
        try { abbr = CultureInfo.GetCultureInfo(Lang.CurrentCode).DateTimeFormat.AbbreviatedDayNames; }
        catch { abbr = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames; }
        var chips = new Button[7];
        int chipW = 44, cx = ClientSize.Width - 16 - 7 * (chipW + 4) + 4;
        for (int i = 0; i < 7; i++)
        {
            int bit = i;
            var b = new Button
            {
                Text = abbr[(i + 1) % 7],   // AbbreviatedDayNames is Sunday-first
                Size = new Size(chipW, 30),
                Location = new Point(cx + i * (chipW + 4), y),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => { _rule.Days ^= 1 << bit; StyleChip(b, (_rule.Days >> bit & 1) != 0); };
            StyleChip(b, (rule.Days >> bit & 1) != 0);
            chips[i] = b;
            Controls.Add(b);
        }
        y += 48;

        // start / end on a 30-minute grid (overnight = end before start, the hint says so)
        var times = Enumerable.Range(0, 48).Select(i => $"{i / 2:D2}:{i % 2 * 30:D2}").ToArray();
        int Idx(string t) => Math.Clamp((ScheduleRule.MinutesOf(t) + 15) / 30 % 48, 0, 47);
        L(Lang.T("sch_from"), 16, y + 4);
        var from = new ThemedComboBox { Width = 100 };
        from.Items.AddRange(times);
        from.SelectedIndex = Idx(rule.Start);
        from.Location = new Point(150, y);
        Controls.Add(from);
        L(Lang.T("sch_to"), 270, y + 4);
        var to = new ThemedComboBox { Width = 100 };
        to.Items.AddRange(times);
        to.SelectedIndex = Idx(rule.End);
        to.Location = new Point(ClientSize.Width - 16 - to.Width, y);
        Controls.Add(to);
        y += 52;

        var ok = new Button { Text = Lang.T("gen_ok"), AutoSize = true, Padding = new Padding(14, 2, 14, 2), Height = 32 };
        var cancel = new Button { Text = Lang.T("gen_cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(14, 2, 14, 2), Height = 32 };
        Ui.StylePrimary(ok);
        Ui.StyleGhost(cancel);
        ok.Click += (_, _) =>
        {
            if (scene.SelectedIndex < 0 || _rule.Days == 0) return;   // needs a scene and a day
            _rule.SceneId = scenes[scene.SelectedIndex].Id;
            _rule.Start = times[Math.Max(0, from.SelectedIndex)];
            _rule.End = times[Math.Max(0, to.SelectedIndex)];
            DialogResult = DialogResult.OK;
        };
        ClientSize = new Size(500, y + 52);
        ok.Location = new Point(ClientSize.Width - 16 - 90 - 8 - ok.PreferredSize.Width, y);
        cancel.Location = new Point(ClientSize.Width - 16 - cancel.PreferredSize.Width, y);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }

    private static void StyleChip(Button b, bool on)
    {
        b.BackColor = on ? Theme.AccentFill : Theme.Card;
        b.ForeColor = on ? Color.White : Theme.Muted;
        b.FlatAppearance.MouseOverBackColor = on ? Theme.AccentFill : Theme.RowAlt;
    }
}
