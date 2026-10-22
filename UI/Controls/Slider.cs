using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostDeck;

/// <summary>Themed horizontal slider: drag to set any value in [min,max]; shows the value + suffix.</summary>
public sealed class Slider : Control
{
    private readonly int _min, _max, _step, _valueW;
    private readonly string _suffix;
    private int _val;
    private bool _drag;
    public event Action<int>? ValueChanged;
    public bool ShowValue { get; set; } = true;   // false = full-width track, caption drawn elsewhere
    /// <summary>A value the thumb snaps to when dragged close (0 for an offset); wheel and keys still reach every value.</summary>
    public int? SnapTo { get; set; }
    /// <summary>A double-click puts the slider back to this value.</summary>
    public int? DefaultValue { get; set; }

    public Slider(int min, int max, int val, int step = 1, string suffix = "")
    {
        _min = min; _max = max; _step = Math.Max(1, step); _suffix = suffix;
        _val = Clamp(val);
        DoubleBuffered = true; ResizeRedraw = true; Height = 26; Width = 240; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);   // arrow keys, after a click
        TabStop = true;
        // room for the longest value this slider can show ("100%", "-500"), never less than before
        using var f = Dpi.Font("Segoe UI", 10f, FontStyle.Bold);
        _valueW = Math.Max(46, Math.Max(TextRenderer.MeasureText(_max + _suffix, f).Width, TextRenderer.MeasureText(_min + _suffix, f).Width) + 6);
    }

    public int Value { get => _val; set { _val = Clamp(value); Invalidate(); } }
    private int Clamp(int v) => Math.Clamp(v, _min, _max);
    private int ValueW => ShowValue ? _valueW : 0;
    private int X0 => 8;
    private int X1 => Width - (ShowValue ? ValueW + 10 : 8);

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) { _drag = true; SetFromX(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) => _drag = false;
    protected override void OnMouseDoubleClick(MouseEventArgs e) { if (DefaultValue is { } d) Set(d); }

    // A wide range on a short track (the crosshair offsets: 1000 values on 190 px) makes an
    // exact value hard to drag to; the wheel and the keys move by one step, Ctrl by ten.
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs h) h.Handled = true;   // the page must not scroll under the slider
        int dir = e.Delta > 0 ? 1 : e.Delta < 0 ? -1 : 0;
        if (dir != 0) Set(_val + dir * _step * ((ModifierKeys & Keys.Control) != 0 ? 10 : 1));
    }

    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Set(_val - _step); e.Handled = true; break;
            case Keys.Right or Keys.Up: Set(_val + _step); e.Handled = true; break;
            case Keys.PageDown: Set(_val - _step * 10); e.Handled = true; break;
            case Keys.PageUp: Set(_val + _step * 10); e.Handled = true; break;
            case Keys.Home: Set(_min); e.Handled = true; break;
            case Keys.End: Set(_max); e.Handled = true; break;
        }
    }

    private void Set(int v)
    {
        int nv = Clamp(v);
        if (nv != _val) { _val = nv; Invalidate(); ValueChanged?.Invoke(_val); }
    }

    private void SetFromX(int x)
    {
        float t = Math.Clamp((x - X0) / (float)Math.Max(1, X1 - X0), 0f, 1f);
        int raw = _min + (int)Math.Round(t * (_max - _min) / _step) * _step;
        // dragging within about half a percent of the range of the snap value lands on it
        if (SnapTo is { } snap && Math.Abs(raw - snap) <= Math.Max(_step, (_max - _min) / 200)) raw = snap;
        Set(raw);
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
            TextRenderer.DrawText(g, _val + _suffix, Dpi.Font("Segoe UI", 10f, FontStyle.Bold),
                new Rectangle(X1 + 6, 0, ValueW, Height), Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}
