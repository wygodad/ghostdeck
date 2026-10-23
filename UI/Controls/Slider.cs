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
    private int _valueW;
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
        DoubleBuffered = true; ResizeRedraw = true; Width = 240; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);   // arrow keys, after a click
        TabStop = true;
        // as tall as the value's line plus air (26 px up to about 150 %), so the digits are not
        // cut at the bottom; room for the longest value this slider can show ("100%", "-500")
        Height = Math.Max(26, ValueF.Height + 6);
        _valueW = NaturalValueWidth;
    }

    private static readonly Font ValueF = Dpi.Font("Segoe UI", 10f, FontStyle.Bold);

    /// <summary>The value column this slider needs for its own longest value.</summary>
    public int NaturalValueWidth =>
        Math.Max(46, Math.Max(TextRenderer.MeasureText(_max + _suffix, ValueF).Width, TextRenderer.MeasureText(_min + _suffix, ValueF).Width) + 6);

    /// <summary>
    /// Width of the value column. Sliders stacked in one card share the widest one, so their
    /// tracks start and end at the same place whatever each can show.
    /// </summary>
    public int ValueWidth { get => _valueW; set { _valueW = Math.Max(NaturalValueWidth, value); Invalidate(); } }

    public int Value { get => _val; set { _val = Clamp(value); Invalidate(); } }
    private int Clamp(int v) => Math.Clamp(v, _min, _max);
    private int ValueW => ShowValue ? _valueW : 0;
    // thumb, track and margins grow with the scale, so the thumb stays a target at 200 %
    private int S(int v) => (int)Math.Ceiling(v * Dpi.Of(this) / 96f);
    private int X0 => S(8);
    private int X1 => Width - (ShowValue ? ValueW + S(10) : S(8));

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
        int th = S(4), r = S(8);
        using (var b = new SolidBrush(Theme.Border)) g.FillRectangle(b, X0, cy - th / 2, X1 - X0, th);
        using (var b = new SolidBrush(Theme.AccentFill)) g.FillRectangle(b, X0, cy - th / 2, Math.Max(0, tx - X0), th);
        using (var b = new SolidBrush(Theme.Surface)) g.FillEllipse(b, tx - r, cy - r, r * 2, r * 2);
        using (var p = new Pen(Theme.BorderStrong)) g.DrawEllipse(p, tx - r, cy - r, r * 2, r * 2);
        if (ShowValue)
            TextRenderer.DrawText(g, _val + _suffix, ValueF,
                new Rectangle(X1 + S(6), 0, ValueW, Height), Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}
