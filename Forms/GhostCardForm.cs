using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GhostDeck;

/// <summary>
/// The GhostDeck decision card: a user-facing message with two text buttons, in the same
/// visual language as the game-session card (SessionReportForm) - dark card with the
/// cyan→violet rail, GhostDeck wordmark and a scan tag, rendered per-pixel with
/// UpdateLayeredWindow. It is a decision, so it takes focus, sits in the centre of the
/// screen, Enter = accent action, Esc / ✕ = later. Draggable by the body.
/// Carriers: the firmware guard (#212) and the Apex first-enable explainer; any future
/// user-facing question uses this rather than MessageBox/TaskDialog (RENDERING.md 11).
///
/// A card that has to EDIT something (the profile mapping of the Windows power card) derives
/// from this class: a layered window cannot host child controls, so the subclass paints its
/// own content block between the body text and the buttons and hit-tests it
/// (<see cref="MeasureContent"/>, <see cref="PaintContent"/>, <see cref="ContentHit"/>,
/// <see cref="ContentClick"/>). What cannot be painted - a text field, a long list - rides
/// in small owned windows placed over the card (<see cref="CardTextHost"/>,
/// <see cref="CardPopupList"/>). The base class owns the text hosts (<see cref="AddTextField"/>:
/// placement, focus, Enter / Esc / Tab) and the shared painters of an editor card (switch,
/// segments, select field, text-field frame), so every editor looks the same.
/// A plain message card overrides nothing.
/// </summary>
public class GhostCardForm : Form
{
    private readonly string _scanTag;
    private readonly string _heading;
    private readonly string _body;
    private readonly string _ackLabel;
    private readonly string _laterLabel;
    private readonly Action _onAck;

    /// <summary>
    /// Shown without taking the focus: for cards that arrive on their own (a temperature alert,
    /// an update, a notice), which may land in the middle of a game. The card still sits on
    /// top; a click on it focuses it as usual.
    /// </summary>
    public bool Quiet { get; init; }
    protected override bool ShowWithoutActivation => Quiet;

    private int _hotBtn = -1;                       // 0 = accent action, 1 = later, 2 = ✕, 3 = aux (left)
    private int _hotZone = -1;                      // content zone under the cursor (subclass-defined), -1 = none
    private readonly Rectangle[] _btn = new Rectangle[4];
    private Rectangle _cardRect;
    private bool _drag;
    private bool _dragMoved;
    private Point _dragOff;

    // The card palette. Cards are dark in both app themes, so these are fixed colours, not Theme.*
    private static readonly Color Bg = Color.FromArgb(247, 0x10, 0x15, 0x1F);
    private protected static readonly Color White = Color.FromArgb(0xF3, 0xF7, 0xFF);
    private protected static readonly Color Muted = Color.FromArgb(0x98, 0xA0, 0xAE);
    private protected static readonly Color Ink = Color.FromArgb(0xC9, 0xD4, 0xE8);
    private protected static readonly Color Cyan = Color.FromArgb(0x3D, 0xE3, 0xFF);
    private static readonly Color Violet = Color.FromArgb(0x8D, 0x63, 0xFF);
    /// <summary>= Theme.AccentFill: the fill of a selected / switched-on control, with white text.</summary>
    private protected static readonly Color Fill = Color.FromArgb(0x3C, 0x7D, 0xFF);
    /// <summary>Opaque field background, so a hosted text box can match it exactly.</summary>
    private protected static readonly Color FieldBg = Color.FromArgb(0x1A, 0x20, 0x2C);
    private protected static readonly Color Amber = Color.FromArgb(0xFF, 0xC1, 0x5D);
    private protected static readonly Color SoftRed = Color.FromArgb(0xF0, 0x6A, 0x7A);

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            return cp;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);

    public GhostCardForm(string scanTag, string heading, string body,
                         string ackLabel, string laterLabel, Action onAck)
    {
        _scanTag = scanTag;
        _heading = heading;
        _body = body;
        _ackLabel = ackLabel;
        _laterLabel = laterLabel;
        _onAck = onAck;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Icon = TrayIconFactory.AppIcon();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
        // a card opened for a window (a modal editor) sits over that window; a plain message
        // sits in the middle of the primary screen
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var host = wa;
        if (HostWindow is { IsDisposed: false, Visible: true } o && o.WindowState != FormWindowState.Minimized)
        {
            host = o.Bounds;
            wa = Screen.FromControl(o).WorkingArea;
        }
        int x = host.Left + (host.Width - Width) / 2, y = host.Top + (host.Height - Height) / 2;
        Location = new Point(Math.Max(wa.Left, Math.Min(x, wa.Right - Width)), Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height)));
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    // ---------------- hosted text fields ----------------
    private readonly List<(CardTextHost Host, Func<Rectangle> Area)> _fields = new();
    private CardTextHost? _focused;
    private Font? _fieldFont;
    private bool _fieldsUp;
    private float _k = 1f;

    /// <summary>The scale (dpi / 96) of the last repaint.</summary>
    protected float UiScale => _k;

    /// <summary>Override to add the text fields of the card (<see cref="AddTextField"/>); runs once, when the card shows.</summary>
    protected virtual void CreateTextFields() { }
    /// <summary>Select the whole text of the first field when the card opens (a one-field prompt).</summary>
    protected virtual bool SelectAllOnOpen => false;

    /// <summary>
    /// Adds a real text box riding over the card. <paramref name="area"/> returns the text area
    /// of the field the card paints, in window coordinates (the rectangle
    /// <see cref="PaintField"/> hands back, moved by the content origin). Enter runs the accent
    /// action, Esc closes, Tab moves to the next field.
    /// </summary>
    private protected CardTextHost AddTextField(string text, HorizontalAlignment align, Func<Rectangle> area)
    {
        var h = new CardTextHost(FieldBg, White, align);
        h.Box.Text = text;
        h.Activated += (_, _) => SetFocused(h);
        h.Deactivate += (_, _) => { if (ReferenceEquals(_focused, h)) SetFocused(null); };
        h.CommandKey += key =>
        {
            if (key == Keys.Enter) Ack();
            else if (key == Keys.Escape) Close();
            else if (_fields.Count > 1) FocusField(_fields[(_fields.FindIndex(f => ReferenceEquals(f.Host, h)) + 1) % _fields.Count].Host);
        };
        _fields.Add((h, area));
        return h;
    }

    private protected bool IsFocused(CardTextHost? h) => h != null && ReferenceEquals(_focused, h);

    private void SetFocused(CardTextHost? h)
    {
        if (ReferenceEquals(_focused, h) || IsDisposed) return;
        _focused = h;
        Render();
    }

    private protected void FocusField(CardTextHost? h, bool selectAll = false)
    {
        if (h == null || h.IsDisposed) return;
        h.Activate();
        h.Box.Focus();
        if (selectAll) h.Box.SelectAll(); else h.Box.SelectionStart = h.Box.TextLength;
    }

    private void PlaceFields()
    {
        if (_fields.Count == 0) return;
        float px = 14f * _k;
        if (_fieldFont == null || Math.Abs(_fieldFont.Size - px) > 0.01f)
        {
            _fieldFont?.Dispose();
            _fieldFont = new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        foreach (var (host, area) in _fields)
        {
            var r = area();
            if (r.IsEmpty || host.IsDisposed) continue;
            host.Place(new Rectangle(Left + r.X, Top + r.Y, r.Width, r.Height), _fieldFont);
        }
    }

    // The boxes come up in the same call that shows the card (not a message-loop turn later),
    // so a field is never seen empty.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible || _fieldsUp) return;
        _fieldsUp = true;
        CreateTextFields();
        if (_fields.Count == 0) return;
        PlaceFields();
        foreach (var f in _fields) f.Host.Show(this);
        FocusField(_fields[0].Host, SelectAllOnOpen);
    }

    // A modal loop activates the card itself after it becomes visible, so the caret is put
    // into the first field once more when the card is fully up - typing works at once.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_fields.Count > 0) FocusField(_fields[0].Host, SelectAllOnOpen);
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        PlaceFields();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        foreach (var f in _fields) f.Host.Dispose();
        _fieldFont?.Dispose();
        _labelF?.Dispose(); _cellF?.Dispose(); _capF?.Dispose();
        _labelF = _cellF = _capF = null;
    }

    // ---------------- shared painters of an editor card ----------------
    private float _fontK;
    private Font? _labelF, _cellF, _capF;
    private static readonly StringFormat CenterFmt = new(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
    private static readonly StringFormat LeftFmt = new(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

    private void EnsureFonts()
    {
        if (_labelF != null && Math.Abs(_fontK - _k) < 0.001f) return;
        _labelF?.Dispose(); _cellF?.Dispose(); _capF?.Dispose();
        _labelF = new Font("Segoe UI Semibold", 13.5f * _k, FontStyle.Regular, GraphicsUnit.Pixel);
        _cellF = new Font("Segoe UI", 12.5f * _k, FontStyle.Bold, GraphicsUnit.Pixel);
        _capF = new Font("Segoe UI", 10.5f * _k, FontStyle.Bold, GraphicsUnit.Pixel);
        _fontK = _k;
    }

    /// <summary>Row label and select-field value.</summary>
    private protected Font LabelFont { get { EnsureFonts(); return _labelF!; } }
    /// <summary>Caption of a picker cell.</summary>
    private protected Font CellFont { get { EnsureFonts(); return _cellF!; } }
    /// <summary>Small upper-case caption above a field.</summary>
    private protected Font CaptionFont { get { EnsureFonts(); return _capF!; } }
    private protected static StringFormat CenterText => CenterFmt;
    private protected static StringFormat LeftText => LeftFmt;

    private int Px(float v) => (int)Math.Ceiling(v * _k);

    /// <summary>The rounded frame of a picker. <paramref name="on"/> = its row is switched on.</summary>
    private protected void PaintTrack(Graphics g, Rectangle track, bool on, bool hot = false, bool kbFocus = false)
    {
        using var tp = RoundPath(track, Px(9));
        using var tb = new SolidBrush(Color.FromArgb(on ? 12 : 7, 255, 255, 255));
        g.FillPath(tb, tp);
        using var te = new Pen(kbFocus ? Color.FromArgb(170, Cyan) : Color.FromArgb(hot ? 90 : on ? 34 : 22, 243, 247, 255), 1f);
        g.DrawPath(te, tp);
    }

    /// <summary>One cell of a segmented picker (also a stand-alone chip).</summary>
    private protected void PaintCell(Graphics g, Rectangle rc, string text, bool selected, bool on, bool hot)
    {
        if (selected || hot)
        {
            using var cp = RoundPath(rc, Px(7));
            using var cb = new SolidBrush(selected && on ? Fill : Color.FromArgb(selected ? 30 : 22, 255, 255, 255));
            g.FillPath(cb, cp);
        }
        using var tb = new SolidBrush(on ? (selected || hot ? White : Muted) : (selected ? Muted : Color.FromArgb(150, Muted)));
        g.DrawString(text, CellFont, tb, rc, CenterFmt);
    }

    /// <summary>A select field: the current value, an optional icon and a chevron. A click opens a <see cref="CardPopupList"/>.</summary>
    private protected void PaintSelect(Graphics g, Rectangle track, string text, bool on, bool hot = false, bool kbFocus = false,
                                       Action<Graphics, RectangleF, bool>? icon = null)
    {
        PaintTrack(g, track, on, hot, kbFocus);
        int x = track.X + Px(12), chev = Px(8), right = track.Right - Px(12) - chev;
        if (icon != null)
        {
            int ic = Px(18);
            icon(g, new RectangleF(x, track.Y + (track.Height - ic) / 2f, ic, ic), false);
            x += ic + Px(9);
        }
        using (var tb = new SolidBrush(on ? White : Muted))
            g.DrawString(text, LabelFont, tb, new RectangleF(x, track.Y, right - Px(6) - x, track.Height), LeftFmt);
        using var cp = new Pen(on ? Ink : Color.FromArgb(150, Muted), 1.4f * _k) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float cy = track.Y + track.Height / 2f;
        g.DrawLines(cp, new[] { new PointF(right, cy - chev * 0.25f), new PointF(right + chev / 2f, cy + chev * 0.25f), new PointF(right + chev, cy - chev * 0.25f) });
    }

    /// <summary>The on/off switch of a row.</summary>
    private protected void PaintSwitch(Graphics g, Rectangle r, bool on)
    {
        using (var tp = RoundPath(r, r.Height / 2))
        using (var tb = new SolidBrush(on ? Fill : Color.FromArgb(0x2E, 0x36, 0x46)))
            g.FillPath(tb, tp);
        float d = r.Height - 6 * _k, x = on ? r.Right - d - 3 * _k : r.X + 3 * _k;
        using var kb = new SolidBrush(on ? Color.White : Color.FromArgb(0xAE, 0xB8, 0xC9));
        g.FillEllipse(kb, x, r.Y + (r.Height - d) / 2f, d, d);
    }

    /// <summary>
    /// The frame of a text field. Returns the text area inside it (move it by the content origin
    /// to get the window rectangle <see cref="AddTextField"/> asks for).
    /// </summary>
    private protected Rectangle PaintField(Graphics g, Rectangle r, bool focused, bool invalid = false, int padX = 12)
    {
        using var fp = RoundPath(r, Px(8));
        using (var fb = new SolidBrush(FieldBg)) g.FillPath(fb, fp);
        using var ep = new Pen(invalid ? SoftRed : focused ? Color.FromArgb(190, Cyan) : Color.FromArgb(40, 243, 247, 255), 1f);
        g.DrawPath(ep, fp);
        return Rectangle.Inflate(r, -Px(padX), -Px(4));
    }

    /// <summary>Small upper-case caption above a field.</summary>
    private protected void PaintCaption(Graphics g, string text, int x, int y)
    {
        using var b = new SolidBrush(Muted);
        g.DrawString(text.ToUpperInvariant(), CaptionFont, b, x, y);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        else if (e.KeyCode == Keys.Enter) { Ack(); e.Handled = true; }
    }

    private void Ack()
    {
        bool close;
        try { close = Acknowledge(); } catch { close = true; }
        if (close) Close();
    }

    /// <summary>
    /// The window this card belongs to; the card centres over it. Set it before the card is
    /// shown - ShowDialog(owner) does not make the owner known by the time the handle exists.
    /// </summary>
    protected Form? HostWindow { get; set; }

    /// <summary>Runs the card as a modal editor centred over <paramref name="owner"/>.</summary>
    public DialogResult ShowOver(Form? owner)
    {
        HostWindow = owner;
        return ShowDialog(owner);
    }

    // ---------------- content hooks (editor cards) ----------------
    /// <summary>Card width in logical pixels.</summary>
    protected virtual int CardWidth => 500;
    /// <summary>Height in device pixels of the content block under the body text; 0 = none.</summary>
    protected virtual int MeasureContent(Graphics g, float k, int width) => 0;
    /// <summary>
    /// Paints the content block into <paramref name="area"/>. Add <paramref name="origin"/> to a
    /// painted rectangle to get the window coordinates <see cref="ContentHit"/> is asked about.
    /// </summary>
    protected virtual void PaintContent(Graphics g, float k, Rectangle area, Point origin) { }
    /// <summary>Clickable content zone at a window point, -1 = none.</summary>
    protected virtual int ContentHit(Point p) => -1;
    protected virtual void ContentClick(int zone) { }
    /// <summary>The accent action. Return false to keep the card open (e.g. a required field is empty).</summary>
    protected virtual bool Acknowledge() { _onAck(); return true; }
    /// <summary>Runs the accent action as if its button was pressed (for hosted text fields: Enter).</summary>
    protected void TriggerAck() => Ack();
    /// <summary>An optional third button on the LEFT of the button row (e.g. Delete); empty = none.</summary>
    protected virtual string AuxLabel => "";
    protected virtual Color AuxColor => SoftRed;
    protected virtual void AuxClick() { }
    /// <summary>Called after every repaint - the place to re-align owned overlay windows.</summary>
    protected virtual void OnRendered() { }
    /// <summary>The content zone under the cursor, for hover painting.</summary>
    protected int HotZone => _hotZone;
    protected void Rerender() => Render();

    // ---------------- mouse ----------------
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hotBtn == -1 && _hotZone == -1) return;
        _hotBtn = _hotZone = -1;
        Render();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _hotBtn == -1 && _hotZone == -1 && _cardRect.Contains(e.Location))
        {
            _drag = true;
            _dragMoved = false;
            _dragOff = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag)
        {
            int dx = e.X - _dragOff.X, dy = e.Y - _dragOff.Y;
            if (_dragMoved || Math.Abs(dx) > 4 || Math.Abs(dy) > 4)
            {
                _dragMoved = true;
                Location = new Point(Location.X + dx, Location.Y + dy);
            }
            return;
        }
        int hot = -1;
        for (int i = 0; i < _btn.Length; i++) if (_btn[i].Contains(e.Location)) hot = i;
        int zone = hot >= 0 ? -1 : ContentHit(e.Location);
        Cursor = hot >= 0 || zone >= 0 ? Cursors.Hand : _cardRect.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
        if (hot != _hotBtn || zone != _hotZone) { _hotBtn = hot; _hotZone = zone; Render(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        bool wasDrag = _drag && _dragMoved;
        _drag = false;
        if (wasDrag) return;
        if (_hotZone >= 0) { ContentClick(_hotZone); return; }
        switch (_hotBtn)
        {
            case 0: Ack(); return;
            case 1: case 2: Close(); return;
            case 3: AuxClick(); return;
        }
    }

    // ---------------- render ----------------
    private void Render()
    {
        if (!IsHandleCreated) return;
        using var bmp = Compose();
        if (Width != bmp.Width || Height != bmp.Height) Size = new Size(bmp.Width, bmp.Height);
        Push(bmp);
        PlaceFields();
        OnRendered();
    }

    private Bitmap Compose()
    {
        float dpi; using (var mg = CreateGraphics()) dpi = mg.DpiY;
        float k = dpi / 96f;
        _k = k;
        int Ce(float v) => (int)Math.Ceiling(v);

        int pad = Ce(14 * k);                        // transparent margin (shadow lives here)
        int W = Ce(CardWidth * k);
        int railW = Ce(5 * k);
        int r = Ce(12 * k);
        int cx = railW + Ce(15 * k);
        int cw = W - cx - Ce(18 * k);

        using var wordF = new Font("Segoe UI", 11.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var scanF = new Font("Consolas", 10.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleF = new Font("Segoe UI", 17f * k, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyF = new Font("Segoe UI", 13.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var btnF = new Font("Segoe UI", 13f * k, FontStyle.Bold, GraphicsUnit.Pixel);

        // measure the body first - the card grows with the text (and with the language)
        int yHdr = Ce(14 * k), hHdr = Ce(24 * k);
        int yTitle = yHdr + hHdr + Ce(8 * k);
        int hTitle, hBody, hContent, hBtn = Ce(34 * k);
        using (var probe = Graphics.FromImage(new Bitmap(1, 1)))
        {
            probe.TextRenderingHint = TextRenderingHint.AntiAlias;
            hTitle = Ce(titleF.GetHeight(probe));
            hBody = Ce(probe.MeasureString(_body, bodyF, cw).Height);
            hContent = MeasureContent(probe, k, cw);
        }
        int yBody = yTitle + hTitle + Ce(8 * k);
        int yContent = yBody + hBody + (hContent > 0 ? Ce(14 * k) : 0);
        int yBtns = yContent + hContent + Ce(14 * k);
        int H = yBtns + hBtn + Ce(14 * k);

        var bmp = new Bitmap(W + pad * 2, H + pad * 2, PixelFormat.Format32bppArgb);
        bmp.SetResolution(dpi, dpi);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);
        g.TranslateTransform(pad, pad);

        // card outline: square left corners, rounded right ones (the session-card shape, no tail)
        using var path = new GraphicsPath();
        path.StartFigure();
        path.AddLine(0, 0, W - r, 0);
        path.AddArc(W - 2 * r, 0, 2 * r, 2 * r, 270, 90);
        path.AddLine(W, r, W, H - r);
        path.AddArc(W - 2 * r, H - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddLine(W - r, H, 0, H);
        path.CloseFigure();

        // soft shadow: a few expanded low-alpha strokes (GDI+ has no blur)
        for (int i = 4; i >= 1; i--)
        {
            using var sp = new Pen(Color.FromArgb(7 * i, 0, 0, 0), i * 2.6f * k) { LineJoin = LineJoin.Round };
            var m = new Matrix(); m.Translate(0, 1.5f * k);
            using var sh = (GraphicsPath)path.Clone();
            sh.Transform(m);
            g.DrawPath(sp, sh);
        }

        using (var bb = new SolidBrush(Bg)) g.FillPath(bb, path);
        using (var bp = new Pen(Color.FromArgb(26, 243, 247, 255), Math.Max(1f, 1f * k)))
            g.DrawPath(bp, path);

        // rail: cyan→violet, flush with the square left edge
        using (var lg = new LinearGradientBrush(new RectangleF(0, 0, railW, H), Cyan, Violet, LinearGradientMode.Vertical))
            g.FillRectangle(lg, new RectangleF(0, 0, railW, H));

        // ---- header: ghost + wordmark, amber scan tag (a warning, not a session) ----
        int gs = Ce(21 * k);
        TrayIconFactory.DrawGhost(g, cx, yHdr + (hHdr - gs) / 2f, gs, Cyan, Color.FromArgb(0x0A, 0x0D, 0x14));
        using var whiteB = new SolidBrush(White);
        using var cyanB = new SolidBrush(Cyan);
        float wx = cx + gs + 6 * k, wy = yHdr + (hHdr - wordF.GetHeight(g)) / 2f;
        g.DrawString("Ghost", wordF, whiteB, wx, wy);
        float ghostW = g.MeasureString("Ghost", wordF, PointF.Empty, StringFormat.GenericTypographic).Width;
        g.DrawString("Deck", wordF, cyanB, wx + ghostW + 1 * k, wy);
        int xs = Ce(20 * k);                         // ✕ size, reserved right of the scan tag
        using (var scanB = new SolidBrush(Color.FromArgb(200, Amber)))
        {
            var sz = g.MeasureString(_scanTag, scanF);
            g.DrawString(_scanTag, scanF, scanB, cx + cw - sz.Width - xs - 8 * k, yHdr + (hHdr - sz.Height) / 2f);
        }

        // ---- heading + body ----
        g.DrawString(_heading, titleF, whiteB, cx - 2 * k, yTitle);
        using (var ib = new SolidBrush(Ink))
            g.DrawString(_body, bodyF, ib, new RectangleF(cx, yBody, cw, hBody + 4 * k));

        if (hContent > 0) PaintContent(g, k, new Rectangle(cx, yContent, cw, hContent), new Point(pad, pad));

        // ---- buttons: [accent action]  [later], right-aligned ----
        // An empty later label means an acknowledge-only card (results, errors): the second
        // button is skipped entirely rather than drawn as an empty pill.
        string ackTxt = _ackLabel, laterTxt = _laterLabel;
        int padX = Ce(14 * k), bgap = Ce(8 * k);
        int wAck = Ce(g.MeasureString(ackTxt, btnF).Width) + padX * 2;
        int wLater = laterTxt.Length == 0 ? 0 : Ce(g.MeasureString(laterTxt, btnF).Width) + padX * 2;
        int xLater = cx + cw - wLater, xAck = (wLater == 0 ? cx + cw : xLater - bgap) - wAck;
        void Button(int i, int bx, int bw, string text, Color? tint)
        {
            var rc = new Rectangle(bx, yBtns, bw, hBtn);
            _btn[i] = new Rectangle(rc.X + pad, rc.Y + pad, rc.Width, rc.Height);   // window coords
            bool hot = _hotBtn == i;
            using var fb = new SolidBrush(Color.FromArgb(hot ? 34 : 13, 255, 255, 255));
            using var rp = RoundPath(rc, Ce(8 * k));
            g.FillPath(fb, rp);
            Color edge = tint is { } t ? Color.FromArgb(hot ? 255 : 130, t) : Color.FromArgb(hot ? 90 : 36, 243, 247, 255);
            using var op = new Pen(edge, 1f);
            g.DrawPath(op, rp);
            using var tb = new SolidBrush(tint ?? (hot ? White : Ink));
            var sz = g.MeasureString(text, btnF);
            g.DrawString(text, btnF, tb, rc.X + (rc.Width - sz.Width) / 2f, rc.Y + (rc.Height - sz.Height) / 2f);
        }
        Button(0, xAck, wAck, ackTxt, Cyan);
        if (wLater > 0) Button(1, xLater, wLater, laterTxt, null);
        else _btn[1] = Rectangle.Empty;
        string auxTxt = AuxLabel;
        if (auxTxt.Length > 0) Button(3, cx, Ce(g.MeasureString(auxTxt, btnF).Width) + padX * 2, auxTxt, AuxColor);
        else _btn[3] = Rectangle.Empty;

        // small ✕ top-right (= Later)
        var xr = new Rectangle(W - xs - Ce(10 * k), yHdr + (hHdr - xs) / 2, xs, xs);
        _btn[2] = new Rectangle(xr.X + pad, xr.Y + pad, xr.Width, xr.Height);
        using (var xp = new Pen(_hotBtn == 2 ? White : Muted, Math.Max(1.2f, 1.5f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(xp, xr.X + xr.Width * .28f, xr.Y + xr.Height * .28f, xr.X + xr.Width * .72f, xr.Y + xr.Height * .72f);
            g.DrawLine(xp, xr.X + xr.Width * .72f, xr.Y + xr.Height * .28f, xr.X + xr.Width * .28f, xr.Y + xr.Height * .72f);
        }

        _cardRect = new Rectangle(pad, pad, W, H);
        return bmp;
    }

    private protected static GraphicsPath RoundPath(RectangleF r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void Push(Bitmap bmp)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(Left, Top);
            var bf = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref bf, 2);
        }
        finally { SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }
}
