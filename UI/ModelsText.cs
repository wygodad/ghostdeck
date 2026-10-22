namespace GhostDeck;

/// <summary>
/// Text measuring shared by the Models tab views. Everything here is cached per text and font
/// (font by REFERENCE: Font.GetHashCode/Equals read the family name through GDI+ on every call,
/// which cost more than the measure being cached; the tab's fonts are static), so a window resize
/// re-wraps ~160 rows without a GDI measure per cell.
/// </summary>
internal static class ModelsText
{
    // Wrapping cells: word break, "&" printed as-is (a credit reads "A & B"), and an ellipsis only
    // on the last allowed line when even MaxLines are not enough.
    public const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;   // dpi-ok: model data wraps up to MaxLines first; the ellipsis only ends a cell longer than that
    public const TextFormatFlags Measure = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
        | TextFormatFlags.NoPrefix;
    public const TextFormatFlags Line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;   // dpi-ok: model data wraps up to MaxLines first; the ellipsis only ends a cell longer than that
    private const TextFormatFlags Tight = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
    public const int MaxLines = 6;   // safety cap only: the longest name and the six-person credit fit in fewer

    private static readonly Dictionary<Font, Dictionary<string, int>> NaturalCache = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Font, Dictionary<string, int>> WordCache = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Font, int> LineCache = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<(string, int), string> BrokenCache = new();

    private static int CachedW(Dictionary<Font, Dictionary<string, int>> cache, string text, Font font, TextFormatFlags flags)
    {
        if (!cache.TryGetValue(font, out var d)) cache[font] = d = new();
        if (!d.TryGetValue(text, out int w)) d[text] = w = TextRenderer.MeasureText(text, font, Size.Empty, flags).Width;
        return w;
    }

    /// <summary>One-line width. SingleLine, not Measure: with WordBreak and no width, MeasureText
    /// wraps at every space and returns the widest WORD instead of the whole line.</summary>
    public static int NaturalW(string text, Font font) =>
        CachedW(NaturalCache, text, font, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    private static int WordW(string word, Font font) => CachedW(WordCache, word, font, Tight);
    private static int SpaceW(Font font) => WordW("a a", font) - WordW("aa", font);
    // the left+right padding DrawText keeps inside the cell
    private static int PadW(Font font) => NaturalW("a", font) - WordW("a", font);

    /// <summary>One line as TextRenderer lays it out (taller than Font.Height).</summary>
    public static int LineH(Font font)
    {
        if (!LineCache.TryGetValue(font, out int h))
            LineCache[font] = h = TextRenderer.MeasureText("Ag", font, Size.Empty, Measure).Height;
        return h;
    }

    /// <summary>Height of an already broken text wrapped into a column, capped at MaxLines.</summary>
    public static int TextH(string text, Font font, int w)
    {
        if (text.IndexOf('\n') < 0 && NaturalW(text, font) <= w) return LineH(font);
        return Math.Min(Lines(text, font, w), MaxLines) * LineH(font);
    }

    /// <summary>Wrap a text (long words broken first) and return it with its height.</summary>
    public static (string Text, int H) Fit(string text, Font font, int w)
    {
        string t = BreakLongWords(text, font, w);
        return (t, TextH(t, font, w));
    }

    // Greedy like DrawText's word break, against a width 3 px narrower than the cell so the count
    // never comes out below what is drawn.
    private static int Lines(string text, Font font, int w)
    {
        int space = SpaceW(font), avail = w - PadW(font) - 3, lines = 0;
        foreach (var seg in text.Split('\n'))
        {
            int cur = -1;
            foreach (var word in seg.Split(' '))
            {
                if (word.Length == 0) continue;
                int ww = WordW(word, font);
                if (cur < 0) { cur = ww; lines++; }
                else if (cur + space + ww <= avail) cur += space + ww;
                else { cur = ww; lines++; }
            }
            if (cur < 0) lines++;   // empty segment still takes a line
        }
        return Math.Max(1, lines);
    }

    /// <summary>A single word wider than its column (a long GitHub login) is split onto the next
    /// line instead of ending in "...": word wrapping alone never breaks inside a word.</summary>
    public static string BreakLongWords(string text, Font font, int w)
    {
        if (w <= 0 || NaturalW(text, font) <= w) return text;
        // FCell and FMono never carry the same text, so text + width is a sufficient key
        if (BrokenCache.TryGetValue((text, w), out var done)) return done;
        return BrokenCache[(text, w)] = BreakCore(text, font, w);
    }

    private static string BreakCore(string text, Font font, int w)
    {
        var words = text.Split(' ');
        for (int k = 0; k < words.Length; k++)
        {
            string word = words[k];
            if (NaturalW(word, font) <= w) continue;
            var sb = new System.Text.StringBuilder();
            int start = 0;
            while (start < word.Length)
            {
                int len = 1;
                while (start + len < word.Length
                       && TextRenderer.MeasureText(word.Substring(start, len + 1), font, Size.Empty, Measure).Width <= w) len++;
                // prefer to break right after a hyphen ("world-" / "spec"), never leave a lone
                // trailing comma for the next line
                if (start + len < word.Length)
                {
                    int hy = word.LastIndexOf('-', start + len - 1, len - 1);
                    if (hy > start) len = hy - start + 1;
                    else if (start + len == word.Length - 1 && word[^1] == ',' && len > 1) len--;
                }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(word, start, len);
                start += len;
            }
            words[k] = sb.ToString();
        }
        return string.Join(" ", words);
    }
}
