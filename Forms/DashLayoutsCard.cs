using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GhostDeck;

/// <summary>What to tell the user when a Dashboard file was refused, in the app's language.</summary>
internal static class DashFileText
{
    /// <param name="wantLayout">A layout was asked for (true) or a widget's style (false): decides the wording for a file of the other kind.</param>
    public static string Of(DashFile.Error e, bool wantLayout) => Lang.T(e switch
    {
        DashFile.Error.Io => "dash_file_err_io",
        DashFile.Error.TooLarge or DashFile.Error.TooMany => "dash_file_err_large",
        DashFile.Error.NotJson or DashFile.Error.NotOurs => "dash_file_err_notours",
        DashFile.Error.Newer => "dash_file_err_newer",
        DashFile.Error.WrongKind => wantLayout ? "dash_file_err_want_layout" : "dash_file_err_want_widget",
        _ => "dash_file_err_damaged",
    });

    /// <summary>What to tell the user when a file could not be written: nothing here speaks of loading.</summary>
    public static string OfExport(DashFile.Error e) => Lang.T(e switch
    {
        DashFile.Error.Empty => "dash_lay_empty",
        DashFile.Error.TooLarge or DashFile.Error.TooMany => "dash_file_err_export_large",
        _ => "dash_file_err_io",
    });

    public const string Filter = "GhostDeck Dashboard (*.json)|*.json";
}

/// <summary>
/// Whole Dashboard layouts (1.45), as a GhostDeck card opened from edit mode: keep the current
/// layout under a name, bring one back from the list, write the layout to a file, read one from
/// a file, and undo the last load. Loading replaces the layout on the Dashboard, so the one
/// before stays at hand for one undo. A file is checked whole before anything of it is used
/// (<see cref="DashFile"/>), and what went wrong is said here, in the card.
///
/// A card has no scroll bar. When the list of saved layouts would make the card taller than the
/// screen's working area, the list goes on pages, so the file buttons and the card's own
/// buttons are always on the screen.
/// </summary>
internal sealed class DashLayoutsCard : GhostCardForm
{
    private enum Act { Save, Load, Delete, Export, Import, Undo, Field, Page }
    private readonly record struct Zone(Rectangle R, Act Kind, int Idx);

    private readonly DashCanvas _canvas;
    private readonly List<Zone> _zones = new();
    private CardTextHost? _name;
    private Rectangle _nameRect;
    private string _nameText = "";
    private int _armed = -1;            // the saved layout whose Delete was clicked once and waits for the second click
    private string _status = "";
    private bool _statusBad;
    private int _perPage = int.MaxValue;   // saved layouts shown at once; all of them when the card fits the screen
    private int _page;
    private int _reveal = -1;              // the saved layout whose page to show at the next paint (the one just saved)

    public DashLayoutsCard(DashCanvas canvas)
        : base("//DASHBOARD", Lang.T("dash_layouts_title"), Lang.T("dash_lay_hint"), Lang.T("dash_done"), "", () => { })
    {
        _canvas = canvas;
    }

    protected override int CardWidth => 580;
    protected override int SideInset => 8;
    protected override bool FocusFirstFieldOnOpen => false;

    protected override void CreateTextFields()
    {
        _name = AddTextField("", HorizontalAlignment.Left, () => _nameRect);
        _name.Box.MaxLength = DashSavedLayout.MaxName;
        _name.SetCue(Lang.T("dash_lay_name"));
        _name.Box.TextChanged += (_, _) => { _nameText = _name.Box.Text; _armed = -1; Rerender(); };
    }

    /// <summary>Enter in the name field keeps the layout under that name; anywhere else the card closes.</summary>
    protected override bool Acknowledge()
    {
        if (IsFocused(_name) && DashOptions.Clean(_nameText, DashSavedLayout.MaxName).Length > 0) { Do(Act.Save, 0); return false; }
        return true;
    }

    private void Say(string text, bool bad = false) { _status = text; _statusBad = bad; }

    private void Do(Act act, int idx)
    {
        if (act != Act.Delete) _armed = -1;
        switch (act)
        {
            case Act.Field: FocusField(_name); return;
            case Act.Save:
            {
                string name = DashOptions.Clean(_nameText, DashSavedLayout.MaxName);
                switch (name.Length == 0 ? DashCanvas.SaveAs.NoName : _canvas.SaveLayoutAs(name))
                {
                    case DashCanvas.SaveAs.Done:
                        Say(Lang.T("dash_lay_ok_saved"));
                        for (int i = 0; i < _canvas.SavedLayouts.Count; i++)
                            if (string.Equals(_canvas.SavedLayouts[i].Name, name, StringComparison.OrdinalIgnoreCase)) _reveal = i;
                        if (_name != null) _name.Box.Text = "";
                        break;
                    case DashCanvas.SaveAs.Empty: Say(Lang.T("dash_lay_empty"), bad: true); break;
                    case DashCanvas.SaveAs.Full: Say(Lang.T("dash_lay_full"), bad: true); break;
                    default: FocusField(_name); return;
                }
                break;
            }
            case Act.Page:
                _page += idx;
                break;
            case Act.Load:
                _canvas.LoadSavedLayout(idx);
                Say(Lang.T("dash_lay_ok_loaded"));
                break;
            case Act.Delete:
                // the first click asks, the second one deletes
                if (_armed != idx) { _armed = idx; break; }
                _armed = -1;
                _canvas.DeleteSavedLayout(idx);
                Say("");
                break;
            case Act.Export:
            {
                // nothing to write: said before a file name is asked for
                if (!_canvas.HasWidgets) { Say(DashFileText.OfExport(DashFile.Error.Empty), bad: true); break; }
                using var dlg = new SaveFileDialog { Filter = DashFileText.Filter, FileName = DashFile.ExportName(null, DateTime.Now), OverwritePrompt = true };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var err = _canvas.ExportLayout(dlg.FileName);
                Say(err == DashFile.Error.None ? Lang.T("dash_lay_ok_exported") : DashFileText.OfExport(err), err != DashFile.Error.None);
                break;
            }
            case Act.Import:
            {
                using var dlg = new OpenFileDialog { Filter = DashFileText.Filter, CheckFileExists = true };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var err = _canvas.ImportLayout(dlg.FileName);
                Say(err == DashFile.Error.None ? Lang.T("dash_lay_ok_loaded") : DashFileText.Of(err, wantLayout: true), err != DashFile.Error.None);
                break;
            }
            case Act.Undo:
                _canvas.UndoLayout();
                Say(Lang.T("dash_lay_ok_undone"));
                break;
        }
        Rerender();
    }

    // ---------------- layout and painting ----------------

    protected override int MeasureContent(Graphics g, float k, int width)
    {
        _perPage = int.MaxValue;
        int h = Flow(g, k, width, null);
        // too tall for the screen: fewer saved layouts at once, the rest on further pages
        for (int per = _canvas.SavedLayouts.Count - 1; per >= 1 && h > ContentRoom; per--)
        {
            _perPage = per;
            h = Flow(g, k, width, null);
        }
        return h;
    }

    protected override void PaintContent(Graphics g, float k, Rectangle area, Point origin)
    {
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        Flow(g, k, area.Width, (area.Location, origin));
        g.TextRenderingHint = hint;
    }

    /// <summary>One pass for both measuring and painting. Returns the height used.</summary>
    private int Flow(Graphics g, float k, int width, (Point at, Point origin)? paintAt)
    {
        int Ce(float v) => (int)Math.Ceiling(v);
        bool paint = paintAt != null;
        int x = paintAt?.at.X ?? 0, y = paintAt?.at.Y ?? 0, y0 = y;
        var org = paintAt?.origin ?? Point.Empty;
        Rectangle Win(Rectangle r) => new(r.X + org.X, r.Y + org.Y, r.Width, r.Height);
        if (paint) _zones.Clear();

        using var fieldF = Dpi.Font("Segoe UI", 14f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var smallF = Dpi.Font("Segoe UI", 12f * k, FontStyle.Regular, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.Word };
        using var whiteB = new SolidBrush(White);
        using var mutedB = new SolidBrush(Muted);
        int fieldH = Ce(36 * k), btnH = Ce(32 * k), gap = Ce(8 * k), padX = Ce(14 * k);

        int BtnW(string text) => Ce(g.MeasureString(text, CellFont).Width) + padX * 2;
        // a button of the card: an outlined cell, tinted when it is the one to press again
        void Button(Rectangle r, string text, Act act, int idx, bool on = true, Color? tint = null)
        {
            if (!paint) return;
            bool hot = on && HotZone == _zones.Count;
            using (var path = RoundPath(r, Ce(8 * k)))
            {
                using var fill = new SolidBrush(Color.FromArgb(hot ? 34 : 13, 255, 255, 255));
                g.FillPath(fill, path);
                using var edge = new Pen(tint is { } t ? Color.FromArgb(hot ? 255 : 150, t) : Color.FromArgb(hot ? 90 : on ? 40 : 20, 243, 247, 255), 1f);
                g.DrawPath(edge, path);
            }
            using var tb = new SolidBrush(!on ? Color.FromArgb(110, Muted) : tint ?? (hot ? White : Ink));
            g.DrawString(text, CellFont, tb, r, CenterText);
            if (on) _zones.Add(new Zone(Win(r), act, idx));
        }

        // ---- keep the current layout under a name ----
        string saveText = Lang.T("dash_lay_save");
        int saveW = BtnW(saveText);
        var fr = new Rectangle(x, y, width - saveW - gap, fieldH);
        if (paint)
        {
            var inner = PaintField(g, fr, IsFocused(_name));
            _nameRect = Win(inner);
            // the text itself, for a picture of the card without its text box
            g.DrawString(_nameText.Length > 0 ? _nameText : Lang.T("dash_lay_name"), fieldF, _nameText.Length > 0 ? whiteB : mutedB, new RectangleF(inner.X, inner.Y, inner.Width, inner.Height), LeftText);
            _zones.Add(new Zone(Win(fr), Act.Field, 0));
        }
        Button(new Rectangle(fr.Right + gap, y + (fieldH - btnH) / 2, saveW, btnH), saveText, Act.Save, 0,
            on: DashOptions.Clean(_nameText, DashSavedLayout.MaxName).Length > 0, tint: Cyan);
        y += fieldH + Ce(18 * k);

        // ---- the saved ones ----
        if (paint) PaintCaption(g, Lang.T("dash_lay_list"), x, y);
        y += Ce(CaptionFont.GetHeight(g)) + gap;
        var saved = _canvas.SavedLayouts;
        if (saved.Count == 0)
        {
            int nh = Ce(g.MeasureString(Lang.T("dash_lay_none"), smallF, width, fmt).Height);
            if (paint) g.DrawString(Lang.T("dash_lay_none"), smallF, mutedB, new RectangleF(x, y, width, nh + 2 * k), fmt);
            y += nh;
        }
        else
        {
            string loadText = Lang.T("dash_lay_load"), delText = Lang.T("scene_delete"), sureText = Lang.T("dash_lay_sure");
            int loadW = BtnW(loadText), delW = Math.Max(BtnW(delText), BtnW(sureText)), rowH = Ce(40 * k);
            int per = Math.Clamp(_perPage, 1, saved.Count), pages = (saved.Count + per - 1) / per;
            // the page is settled when the card is painted: a measuring pass tries other page sizes,
            // and must not move the page the user is on
            int page = Math.Clamp(_reveal >= 0 ? _reveal / per : _page, 0, pages - 1);
            if (paint) _page = page;
            int first = page * per, last = Math.Min(saved.Count, first + per);
            for (int i = first; i < last; i++)
            {
                var row = new Rectangle(x, y, width, rowH);
                if (paint)
                {
                    using var line = new Pen(Color.FromArgb(22, 243, 247, 255), 1f);
                    if (i > first) g.DrawLine(line, row.Left, row.Top, row.Right, row.Top);
                }
                int right = row.Right;
                var del = new Rectangle(right - delW, y + (rowH - btnH) / 2, delW, btnH);
                Button(del, _armed == i ? sureText : delText, Act.Delete, i, tint: _armed == i ? SoftRed : null);
                var load = new Rectangle(del.Left - gap - loadW, del.Top, loadW, btnH);
                Button(load, loadText, Act.Load, i);
                if (paint)
                {
                    string count = string.Format(Lang.T("dash_lay_count_fmt"), saved[i].Items.Count);
                    int cw = Ce(g.MeasureString(count, smallF).Width);
                    int textRight = load.Left - gap * 2;
                    g.DrawString(count, smallF, mutedB, new RectangleF(textRight - cw, y, cw + 2, rowH), LeftText);
                    g.DrawString(saved[i].Name, LabelFont, whiteB, new RectangleF(x, y, Math.Max(1, textRight - cw - gap - x), rowH), LeftText);
                }
                y += rowH;
            }
            if (pages > 1)
            {
                // every page is as tall as a full one, so what is below the list stays in place;
                // then the page and the two arrows, on the right
                y += (per - (last - first)) * rowH + gap;
                int numW = Ce(g.MeasureString("00 / 00", smallF).Width) + gap;
                var next = new Rectangle(x + width - btnH, y, btnH, btnH);
                var num = new Rectangle(next.Left - numW, y, numW, btnH);
                var prev = new Rectangle(num.Left - btnH, y, btnH, btnH);
                Button(prev, "‹", Act.Page, -1, on: page > 0);
                if (paint) g.DrawString((page + 1) + " / " + pages, smallF, mutedB, num, CenterText);
                Button(next, "›", Act.Page, +1, on: page < pages - 1);
                y += btnH;
            }
        }
        if (paint) _reveal = -1;
        y += Ce(18 * k);

        // ---- files, and the way back ----
        if (paint)
        {
            using var line = new Pen(Color.FromArgb(30, 243, 247, 255), 1f);
            g.DrawLine(line, x, y, x + width, y);
        }
        y += Ce(14 * k);
        int bx = x, by = y;
        foreach (var (text, act, on) in new[]
                 {
                     (Lang.T("dash_lay_export"), Act.Export, true),
                     (Lang.T("dash_lay_import"), Act.Import, true),
                     (Lang.T("dash_lay_undo"), Act.Undo, _canvas.CanUndoLayout),
                 })
        {
            int w = BtnW(text);
            if (bx > x && bx + w > x + width) { bx = x; by += btnH + gap; }   // a long language: the buttons go to the next line, never over the edge
            Button(new Rectangle(bx, by, w, btnH), text, act, 0, on);
            bx += w + gap;
        }
        y = by + btnH;

        // ---- what the last action did ----
        if (_status.Length > 0)
        {
            y += Ce(12 * k);
            int sh = Ce(g.MeasureString(_status, smallF, width, fmt).Height);
            if (paint)
            {
                using var sb = new SolidBrush(_statusBad ? SoftRed : Ink);
                g.DrawString(_status, smallF, sb, new RectangleF(x, y, width, sh + 2 * k), fmt);
            }
            y += sh;
        }
        return y - y0;
    }

    // ---------------- mouse ----------------

    protected override int ContentHit(Point p)
    {
        for (int i = _zones.Count - 1; i >= 0; i--) if (_zones[i].R.Contains(p)) return i;
        return -1;
    }

    protected override void ContentClick(int zone)
    {
        if (zone < 0 || zone >= _zones.Count) return;
        Do(_zones[zone].Kind, _zones[zone].Idx);
    }

    /// <summary>Preview tool only: the card as it stands after an action, without its dialogs.</summary>
    internal void PreviewState(string name, int armed, string status, bool bad, int page = 0)
    {
        _nameText = name; _armed = armed; _status = status; _statusBad = bad; _page = page;
    }

    /// <summary>Preview tool only: the page shown and how many saved layouts one page holds (all of them when the card fits).</summary>
    internal (int page, int perPage) PreviewPaging => (_page, _perPage);
}
