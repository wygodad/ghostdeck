using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>Themed horizontal slider: drag to set any value in [min,max]; shows the value + suffix.</summary>
public sealed class Slider : Control
{
    private readonly int _min, _max, _step;
    private readonly string _suffix;
    private int _val;
    private bool _drag;
    public event Action<int>? ValueChanged;
    public bool ShowValue { get; set; } = true;   // false = full-width track, caption drawn elsewhere

    public Slider(int min, int max, int val, int step = 1, string suffix = "")
    {
        _min = min; _max = max; _step = Math.Max(1, step); _suffix = suffix;
        _val = Clamp(val);
        DoubleBuffered = true; ResizeRedraw = true; Height = 26; Width = 240; Cursor = Cursors.Hand;
    }

    public int Value { get => _val; set { _val = Clamp(value); Invalidate(); } }
    private int Clamp(int v) => Math.Clamp(v, _min, _max);
    private int ValueW => ShowValue ? 46 : 0;
    private int X0 => 8;
    private int X1 => Width - (ShowValue ? ValueW + 10 : 8);

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _drag = true; SetFromX(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) => _drag = false;

    private void SetFromX(int x)
    {
        float t = Math.Clamp((x - X0) / (float)Math.Max(1, X1 - X0), 0f, 1f);
        int raw = _min + (int)Math.Round(t * (_max - _min) / _step) * _step;
        int nv = Clamp(raw);
        if (nv != _val) { _val = nv; Invalidate(); ValueChanged?.Invoke(_val); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        int cy = Height / 2;
        float t = (_val - _min) / (float)Math.Max(1, _max - _min);
        int tx = X0 + (int)(t * (X1 - X0));
        using (var b = new SolidBrush(Theme.Border)) g.FillRectangle(b, X0, cy - 2, X1 - X0, 4);
        using (var b = new SolidBrush(Theme.AccentFill)) g.FillRectangle(b, X0, cy - 2, Math.Max(0, tx - X0), 4);
        using (var b = new SolidBrush(Theme.Surface)) g.FillEllipse(b, tx - 8, cy - 8, 16, 16);
        using (var p = new Pen(Theme.BorderStrong)) g.DrawEllipse(p, tx - 8, cy - 8, 16, 16);
        if (ShowValue)
            TextRenderer.DrawText(g, _val + _suffix, new Font("Segoe UI", 10f, FontStyle.Bold),
                new Rectangle(X1 + 6, 0, ValueW, Height), Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}
