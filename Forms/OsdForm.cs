using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GhostDeck;

/// <summary>Nakladka OSD: pasek "MSI · PROFIL" z kolorem, bez kradziezy fokusa, z zanikaniem.</summary>
public sealed class OsdForm : Form
{
    private string _title = "";
    private string _sub = "";
    private Color _accent = Color.Gray;

    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    private enum Phase { Idle, In, Hold, Out }
    private Phase _phase = Phase.Idle;
    private DateTime _holdUntil;
    private int _holdMs = 3000;

    /// <summary>How long a toast stays fully visible (seconds); follows the OSD setting.</summary>
    public int HoldSeconds { get; set; } = 3;

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public OsdForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = ColorTranslator.FromHtml("#16181D");
        Size = new Size(440, 104);
        Opacity = 0;
        _anim.Tick += Anim_Tick;
        Region = RoundedRegion(Width, Height, 18);
    }

    private static Region RoundedRegion(int w, int h, int r)
    {
        using var p = new GraphicsPath();
        int d = r * 2;
        p.AddArc(0, 0, d, d, 180, 90);
        p.AddArc(w - d, 0, d, d, 270, 90);
        p.AddArc(w - d, h - d, d, d, 0, 90);
        p.AddArc(0, h - d, d, d, 90, 90);
        p.CloseFigure();
        return new Region(p);
    }

    // minSeconds lets important toasts (e.g. the temperature alert) stay up at least that long
    // even when the user prefers short OSDs for profile switches.
    public void ShowProfile(string title, string sub, Color accent, int minSeconds = 0)
    {
        _title = title; _sub = sub; _accent = accent;
        _holdMs = Math.Max(HoldSeconds, minSeconds) * 1000;

        // dopasuj szerokosc do dluzszej z linii (tytul np. "SUPER BATTERY" albo dluzszy podtytul)
        using (var g = CreateGraphics())
        using (var tF = new Font("Segoe UI", 19f, FontStyle.Bold))
        using (var sF = new Font("Segoe UI", 12.5f, FontStyle.Bold))
        {
            int titleW = (int)Math.Ceiling(g.MeasureString(title, tF).Width);
            int subW = (int)Math.Ceiling(g.MeasureString(sub, sF).Width);
            int textW = Math.Max(titleW, subW);
            int w = Math.Max(440, textW + 64 + 34);
            if (w != Width)
            {
                Size = new Size(w, Height);
                Region = RoundedRegion(Width, Height, 18);
            }
        }

        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + 90);
        Invalidate();
        if (!Visible) Show();
        BringToFront();
        _phase = Phase.In;
        _anim.Start();
    }

    private void Anim_Tick(object? sender, EventArgs e)
    {
        switch (_phase)
        {
            case Phase.In:
                Opacity = Math.Min(0.97, Opacity + 0.14);
                if (Opacity >= 0.97) { _phase = Phase.Hold; _holdUntil = DateTime.Now.AddMilliseconds(_holdMs); }
                break;
            case Phase.Hold:
                if (DateTime.Now >= _holdUntil) _phase = Phase.Out;
                break;
            case Phase.Out:
                Opacity = Math.Max(0, Opacity - 0.09);
                if (Opacity <= 0) { _anim.Stop(); Hide(); _phase = Phase.Idle; }
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        // pasek akcentu z lewej (zaokraglony)
        using (var ab = new SolidBrush(_accent))
        using (var ap = new GraphicsPath())
        {
            var ar = new Rectangle(16, 22, 6, Height - 44);
            const int d = 6;
            ap.AddArc(ar.X, ar.Y, d, d, 180, 90);
            ap.AddArc(ar.Right - d, ar.Y, d, d, 270, 90);
            ap.AddArc(ar.Right - d, ar.Bottom - d, d, d, 0, 90);
            ap.AddArc(ar.X, ar.Bottom - d, d, d, 90, 90);
            ap.CloseFigure();
            g.FillPath(ab, ap);
        }

        // kropka profilu
        using (var dot = new SolidBrush(_accent))
            g.FillEllipse(dot, 40, 39, 12, 12);

        using var tF = new Font("Segoe UI", 19f, FontStyle.Bold);
        using var sF = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        using var tB = new SolidBrush(Color.White);
        using var sB = new SolidBrush(ColorTranslator.FromHtml("#C2C7D0"));
        g.DrawString(_title, tF, tB, 64, 14);
        g.DrawString(_sub, sF, sB, 66, 58);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }
}
