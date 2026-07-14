using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>Small segmented control (themed).</summary>
/// <summary>Small checkbox + label (blue box, white tick), matching the overlay-settings mockup.</summary>
public sealed class CheckItem : Control
{
    private bool _on;
    public event Action<bool>? Toggled;
    public CheckItem(string text, bool on)
    {
        Text = text; _on = on; DoubleBuffered = true; Cursor = Cursors.Hand; Height = 26;
        SetStyle(ControlStyles.Selectable, false);
    }
    public bool Checked { get => _on; set { _on = value; Invalidate(); } }
    private static readonly Font F = new("Segoe UI", 10.5f);
    private int Box => (int)Math.Ceiling(18 * DeviceDpi / 96f);   // DPI-aware box size
    public int PreferredWidth => Box + 10 + TextRenderer.MeasureText(Text, F).Width + 6;
    protected override void OnClick(EventArgs e) { base.OnClick(e); _on = !_on; Invalidate(); Toggled?.Invoke(_on); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        int b = Box, y = (Height - b) / 2;
        using var path = Theme.RoundRect(new RectangleF(0.5f, y + 0.5f, b - 1, b - 1), 4);
        if (_on)
        {
            using (var br = new SolidBrush(Theme.AccentFill)) g.FillPath(br, path);
            using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[] { new PointF(b * 0.26f, y + b * 0.52f), new PointF(b * 0.44f, y + b * 0.70f), new PointF(b * 0.74f, y + b * 0.30f) });
        }
        else using (var pen = new Pen(Theme.BorderStrong, 1.4f)) g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, F, new Rectangle(b + 10, 0, Width - b - 10, Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
