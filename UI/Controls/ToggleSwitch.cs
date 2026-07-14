using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>iOS-style themed on/off switch.</summary>
public sealed class ToggleSwitch : Control
{
    private bool _checked;
    public event Action<bool>? Toggled;
    public bool Checked { get => _checked; set { _checked = value; Invalidate(); } }

    public ToggleSwitch() { DoubleBuffered = true; ResizeRedraw = true; Cursor = Cursors.Hand; Size = new Size(52, 28); }

    protected override void OnClick(EventArgs e) { if (!Enabled) return; _checked = !_checked; Invalidate(); Toggled?.Invoke(_checked); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        // Dimmed when disabled (master shortcut switch off): muted track + knob so it reads as inactive.
        var track = !Enabled ? Theme.Border : _checked ? Theme.AccentFill : Theme.BorderStrong;
        var knob = Enabled ? Color.White : Theme.Muted;
        using (var path = Theme.RoundRect(r, Height / 2f))
        using (var b = new SolidBrush(track))
            g.FillPath(b, path);
        float d = Height - 8;
        float kx = _checked ? Width - d - 4 : 4;
        using var kb = new SolidBrush(knob);
        g.FillEllipse(kb, kx, 4, d, d);
    }
}
