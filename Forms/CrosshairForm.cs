using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// The crosshair window (roadmap #118, discussion #247): a small per-pixel layered window
/// (WS_EX_LAYERED + UpdateLayeredWindow, premultiplied ARGB) that never takes the focus
/// (WS_EX_NOACTIVATE), stays off the taskbar, sits on top and lets every click through
/// (WS_EX_TRANSPARENT) - the same technique as the gaming overlay (RENDERING.md 3), as its own
/// window so it works with the overlay off. It sits at the centre of the screen the overlay
/// uses (or under the mouse pointer), shifted by the offset in the style. The picture comes from
/// <see cref="CrosshairPainter"/>, so the Settings preview and the screen agree pixel for pixel.
/// The click effect and the aim-hide option watch the mouse through <see cref="MouseWatch"/>,
/// installed only while one of them is on.
/// </summary>
public sealed class CrosshairForm : Form
{
    private readonly AppSettings _settings;
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _fxTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _followTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _topTimer = new() { Interval = 2000 };
    private MouseWatch? _watch;
    private Bitmap? _image;                 // the custom picture, reloaded when its file changes
    private DateTime _imageStamp;
    private long _fxStart = -1;             // Environment.TickCount64 at the click, -1 = at rest
    private bool _aimHidden;                // hidden by the right button (hold or toggle)
    private Point _lastCursor = new(int.MinValue, int.MinValue);
    private Size _frame;                    // size of the last pushed bitmap

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    private const int ULW_ALPHA = 2; private const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    public CrosshairForm(AppSettings settings, SynchronizationContext ui)
    {
        _settings = settings;
        _ui = ui;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        Size = new Size(8, 8);   // dpi-ok: placeholder; the window takes the crosshair's size before it is shown
        _fxTimer.Tick += (_, _) => FxFrame();
        _followTimer.Tick += (_, _) => Follow();
        // a game that comes up after the crosshair can land above it; re-assert quietly
        _topTimer.Tick += (_, _) => { if (IsHandleCreated && Visible) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); };
    }

    private CrosshairStyle Style => _settings.Crosshair;

    // Layered rendering owns the pixels; nothing else may paint.
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && !AppLifecycle.ShuttingDown) { ApplySettings(); _topTimer.Start(); }
        else { _fxTimer.Stop(); _followTimer.Stop(); _topTimer.Stop(); _watch?.Set(false); _fxStart = -1; }
    }

    /// <summary>The screen the crosshair belongs to: the one the overlay sits on, else the main one.</summary>
    public static Screen TargetScreen(AppSettings s)
    {
        if (s.OverlayX >= 0 && s.OverlayY >= 0) return Screen.FromPoint(new Point(s.OverlayX, s.OverlayY));
        return Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    /// <summary>Re-reads the style (after an edit in Settings, a preset, a code, a scene) and redraws.</summary>
    public void ApplySettings()
    {
        if (!IsHandleCreated || !Visible) return;
        var st = Style;
        ReloadImageIfChanged(st);
        _fxStart = -1;
        _fxTimer.Stop();
        _aimHidden = _aimHidden && st.AdsId != CrosshairAds.None;
        bool hook = st.NeedsMouseHook;
        if (hook && _watch == null) _watch = new MouseWatch(_ui, OnButton);
        _watch?.Set(hook);
        if (st.FollowCursor) { _lastCursor = new Point(int.MinValue, int.MinValue); _followTimer.Start(); }
        else _followTimer.Stop();
        Render(0f);
    }

    private void ReloadImageIfChanged(CrosshairStyle st)
    {
        if (st.KindId != CrosshairKind.Image) return;
        DateTime stamp = DateTime.MinValue;
        try { if (File.Exists(CrosshairPainter.ImagePath)) stamp = File.GetLastWriteTimeUtc(CrosshairPainter.ImagePath); } catch { }
        if (_image != null && stamp == _imageStamp) return;
        _image?.Dispose();
        _image = CrosshairPainter.LoadImage();
        _imageStamp = stamp;
    }

    private float Scale()
    {
        // the window carries the DPI of the monitor it is on (PerMonitorV2); place it there first
        var sc = TargetScreen(_settings);
        var c = Centre(sc);
        if (!sc.Bounds.Contains(Location)) Location = new Point(c.X - Width / 2, c.Y - Height / 2);
        return Math.Max(0.5f, Dpi.Of(this) / 96f);
    }

    private Point Centre(Screen sc)
    {
        if (Style.FollowCursor) return Cursor.Position;
        var b = sc.Bounds;   // the full screen, not the working area: a borderless game covers the taskbar
        return new Point(b.X + b.Width / 2, b.Y + b.Height / 2);
    }

    private void Render(float fx)
    {
        if (!IsHandleCreated) return;
        var st = Style;
        float k = Scale();
        using var bmp = CrosshairPainter.Render(st, st.KindId == CrosshairKind.Image ? _image : null, k, fx);
        _frame = bmp.Size;
        Push(bmp, k);
    }

    private void Push(Bitmap bmp, float k)
    {
        var st = Style;
        var c = Centre(TargetScreen(_settings));
        int x = c.X + (int)MathF.Round(st.OffsetX * k) - bmp.Width / 2;
        int y = c.Y + (int)MathF.Round(st.OffsetY * k) - bmp.Height / 2;
        // an aim-hidden crosshair keeps its window: a fully transparent frame is cheaper and
        // safer than Hide/Show churn while the right button is held
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        IntPtr hbmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            Bitmap? blank = null;
            var src = _aimHidden ? blank = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppPArgb) : bmp;
            hbmp = src.GetHbitmap(Color.FromArgb(0)); old = SelectObject(mem, hbmp);
            var size = new SIZE(bmp.Width, bmp.Height);
            var srcPt = new POINT(0, 0);
            var dst = new POINT(x, y);
            var bf = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref srcPt, 0, ref bf, ULW_ALPHA);
            blank?.Dispose();
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
        }
        // keep the managed view of the window in step (the layered call moved and sized it)
        if (Location.X != x || Location.Y != y) Location = new Point(x, y);
        if (Size != bmp.Size) Size = bmp.Size;
    }

    // ---------------- follow the pointer ----------------
    private void Follow()
    {
        if (!Visible || !IsHandleCreated) return;
        var p = Cursor.Position;
        if (p == _lastCursor) return;
        _lastCursor = p;
        if (_fxStart >= 0) return;   // the effect frames place the window themselves
        Render(0f);
    }

    // ---------------- click effect and aim-hide ----------------
    private void OnButton(MouseWatch.Button b)
    {
        if (!Visible || !IsHandleCreated) return;
        var st = Style;
        switch (b)
        {
            case MouseWatch.Button.LeftDown:
                if (st.ClickFxId == CrosshairClickFx.None || _aimHidden) return;
                _fxStart = Environment.TickCount64;
                _fxTimer.Start();
                FxFrame();
                break;
            case MouseWatch.Button.RightDown:
                if (st.AdsId == CrosshairAds.HideWhileHeld) SetAimHidden(true);
                else if (st.AdsId == CrosshairAds.ToggleOnClick) SetAimHidden(!_aimHidden);
                break;
            case MouseWatch.Button.RightUp:
                if (st.AdsId == CrosshairAds.HideWhileHeld) SetAimHidden(false);
                break;
        }
    }

    private void SetAimHidden(bool hidden)
    {
        if (_aimHidden == hidden) return;
        _aimHidden = hidden;
        _fxStart = -1; _fxTimer.Stop();
        Render(0f);
    }

    private void FxFrame()
    {
        if (_fxStart < 0) { _fxTimer.Stop(); return; }
        float t = (Environment.TickCount64 - _fxStart) / (float)CrosshairPainter.FxMs;
        if (t >= 1f) { _fxStart = -1; _fxTimer.Stop(); Render(0f); return; }
        Render(t);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _fxTimer.Stop(); _followTimer.Stop(); _topTimer.Stop();
        _watch?.Dispose(); _watch = null;
        _image?.Dispose(); _image = null;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _fxTimer.Dispose(); _followTimer.Dispose(); _topTimer.Dispose(); _watch?.Dispose(); _image?.Dispose(); }
        base.Dispose(disposing);
    }
}
