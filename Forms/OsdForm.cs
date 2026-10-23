using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GhostDeck;

/// <summary>Nakladka OSD: pasek "MSI · PROFIL" z kolorem, bez kradziezy fokusa, z zanikaniem.</summary>
public sealed class OsdForm : Form
{
    private string _title = "";
    private string _sub = "";
    private Color _accent = Color.Gray;
    private int _subY = 58;
    private List<string> _titleLines = new();
    private List<string> _subLines = new();
    private float _titleLineH, _subLineH;

    // Geometry at 100 % scaling, multiplied by the monitor's scale in S(): title and subtitle
    // start in the same column, right margin 34. Fonts are in points and grow with the scale on
    // their own, so every pixel value next to them has to grow too - a fixed 720 px cap with
    // a font at 125-150 % broke even a short title onto two lines.
    private const int TextX = 64, RightMargin = 34, MinWidth = 440, MinHeight = 104, MaxWidth = 960;
    // Long messages wrap instead of stretching the toast across the screen: at most MaxWidth
    // (scaled) and never more than this share of the screen's width.
    private const float MaxScreenShare = 0.6f;

    private int S(int v) => (int)Math.Round(v * Dpi.Of(this) / 96f);

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
        Size = new Size(440, 104);   // dpi-ok: placeholder; ShowProfile sizes the toast from its text before it is shown
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

        var wa = Screen.PrimaryScreen!.WorkingArea;

        // The width follows the longer of the two texts, up to MaxScreenShare of the screen;
        // past that the text wraps and the toast grows downwards instead of sideways. The
        // lines are broken here, once, at spaces only (WrapAtSpaces), and OnPaint draws exactly
        // these lines - so "75°C)" never splits after the degree sign the way GDI+ wrapping does,
        // and the measured height is the drawn height.
        using (var g = CreateGraphics())
        using (var tF = Dpi.Font("Segoe UI", 19f, FontStyle.Bold))
        using (var sF = Dpi.Font("Segoe UI", 12.5f, FontStyle.Bold))
        {
            int maxW = Math.Max(S(MinWidth), Math.Min(S(MaxWidth), Math.Min((int)(wa.Width * MaxScreenShare), wa.Width - S(120))));
            float maxTextW = maxW - S(TextX) - S(RightMargin);
            _titleLines = WrapAtSpaces(g, title, tF, maxTextW);
            _subLines = WrapAtSpaces(g, sub, sF, maxTextW);
            _titleLineH = tF.GetHeight(g);
            _subLineH = sF.GetHeight(g);

            float textW = 0;
            foreach (var l in _titleLines) textW = Math.Max(textW, g.MeasureString(l, tF).Width);
            foreach (var l in _subLines) textW = Math.Max(textW, g.MeasureString(l, sF).Width);
            int w = Math.Max(S(MinWidth), (int)Math.Ceiling(textW) + S(TextX) + S(RightMargin));

            int titleH = (int)Math.Ceiling(_titleLines.Count * _titleLineH);
            int subH = (int)Math.Ceiling(_subLines.Count * _subLineH);
            _subY = S(14) + Math.Max(S(44), titleH + S(2));     // one title line: y = 58 at 100 %
            int h = Math.Max(S(MinHeight), _subY + subH + S(21)); // one subtitle line: 104 at 100 %

            if (w != Width || h != Height)
            {
                Size = new Size(w, h);
                Region = RoundedRegion(Width, Height, S(18));
            }
        }

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
            var ar = new Rectangle(S(16), S(22), S(6), Height - S(44));
            int d = S(6);
            ap.AddArc(ar.X, ar.Y, d, d, 180, 90);
            ap.AddArc(ar.Right - d, ar.Y, d, d, 270, 90);
            ap.AddArc(ar.Right - d, ar.Bottom - d, d, d, 0, 90);
            ap.AddArc(ar.X, ar.Bottom - d, d, d, 90, 90);
            ap.CloseFigure();
            g.FillPath(ab, ap);
        }

        // kropka profilu: na srodku pierwszej linii tytulu
        int dotD = S(12);
        float dotY = S(14) + _titleLineH / 2f - dotD / 2f;
        using (var dot = new SolidBrush(_accent))
            g.FillEllipse(dot, S(40), dotY, dotD, dotD);

        using var tF = Dpi.Font("Segoe UI", 19f, FontStyle.Bold);
        using var sF = Dpi.Font("Segoe UI", 12.5f, FontStyle.Bold);
        using var tB = new SolidBrush(Color.White);
        using var sB = new SolidBrush(ColorTranslator.FromHtml("#C2C7D0"));
        // Exactly the lines ShowProfile broke and measured, one by one, from the same column.
        float y = S(14);
        foreach (var l in _titleLines) { g.DrawString(l, tF, tB, S(TextX), y); y += _titleLineH; }
        y = _subY;
        foreach (var l in _subLines) { g.DrawString(l, sF, sB, S(TextX), y); y += _subLineH; }
    }

    /// <summary>
    /// Greedy word wrap that breaks only at spaces, so a value with its unit ("75°C)", "95 %")
    /// stays on one line; GDI+'s own wrapping breaks after the degree sign. A single word wider
    /// than the line (a long drive name) is the one case that is split, by characters.
    /// </summary>
    private static List<string> WrapAtSpaces(Graphics g, string text, Font f, float maxW)
    {
        var lines = new List<string>();
        foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
        {
            string line = "";
            // Empty entries are kept, so the double spaces around the "·" of the prefix survive.
            foreach (var word in para.Split(' '))
            {
                if (line.Length == 0 && word.Length == 0) continue;
                string cand = line.Length == 0 ? word : line + " " + word;
                if (g.MeasureString(cand, f).Width <= maxW) { line = cand; continue; }
                if (line.Length > 0) lines.Add(line);
                line = word;
                while (line.Length > 1 && g.MeasureString(line, f).Width > maxW)
                {
                    int n = line.Length - 1;
                    while (n > 1 && g.MeasureString(line[..n], f).Width > maxW) n--;
                    lines.Add(line[..n]);
                    line = line[n..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _anim.Dispose();
        base.Dispose(disposing);
    }
}
