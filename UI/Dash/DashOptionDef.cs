namespace GhostDeck;

/// <summary>The kinds of field the widget settings card can draw.</summary>
internal enum DashOptKind
{
    /// <summary>"auto" or one of the palette (<see cref="DashPaint.PaletteChoices"/>): a row of swatches.</summary>
    Color,
    /// <summary>One of a few: segments, or a select field when the captions do not fit side by side.</summary>
    Choice,
    /// <summary>A short text.</summary>
    Text,
    /// <summary>A whole number within a range, with a unit.</summary>
    Number,
    /// <summary>Yes or no.</summary>
    Toggle,
}

/// <summary>
/// One setting of a widget, as the widget declares it and the settings card draws it (1.45).
/// The card needs nothing else from the widget, so a new widget type gets its card from this
/// list alone. Labels and captions are already in the app's language. The value lives in the
/// layout item under <see cref="Key"/> (<see cref="DashOptions"/>); a value equal to the default
/// is not stored at all.
/// </summary>
internal sealed record DashOptionDef(string Key, DashOptKind Kind, string Label)
{
    /// <summary>Choice: the stored ids with their captions.</summary>
    public IReadOnlyList<(string Id, string Caption)> Items { get; init; } = Array.Empty<(string, string)>();

    /// <summary>Choice and Color: the id in use when nothing is stored.</summary>
    public string Default { get; init; } = "";

    /// <summary>Color: what the "auto" swatch shows, one colour or several (the temperature colours).</summary>
    public IReadOnlyList<Color> AutoColors { get; init; } = Array.Empty<Color>();

    /// <summary>Color: the line under the swatches while "auto" is picked.</summary>
    public string AutoHint { get; init; } = "";

    /// <summary>Text: grey text in an empty field (what the widget shows then).</summary>
    public string Placeholder { get; init; } = "";

    /// <summary>Number: the allowed range and the value in use when nothing is stored.</summary>
    public int Min { get; init; }
    public int Max { get; init; } = 100;
    public int DefaultInt { get; init; }

    /// <summary>Number: what an empty field means (0 = "off" for a threshold); null = the field may not be empty.</summary>
    public int? EmptyValue { get; init; }

    public string Unit { get; init; } = "";

    /// <summary>Toggle: the state when nothing is stored.</summary>
    public bool DefaultOn { get; init; }

    /// <summary>A small grey line under the field (what a threshold does).</summary>
    public string Hint { get; init; } = "";

    /// <summary>A check across fields (the top of a scale above its bottom); false marks this field and keeps the card open.</summary>
    public Func<DashOptions, bool>? Rule { get; init; }

    /// <summary>The number this setting holds in <paramref name="o"/>: the default, the "empty" value, or a value within the range.</summary>
    public int NumberIn(DashOptions o)
    {
        int lo = EmptyValue is { } e ? Math.Min(e, Min) : Min;
        int v = o.Int(Key, DefaultInt, lo, Max);
        if (EmptyValue is { } ev && v != ev && v < Min) v = Min;
        return v;
    }
}
