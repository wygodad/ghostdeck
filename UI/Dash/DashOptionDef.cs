namespace GhostDeck;

/// <summary>The kinds of field the widget settings card can draw.</summary>
internal enum DashOptKind
{
    /// <summary>"auto" or one of the palette (<see cref="DashPaint.PaletteChoices"/>): a "Default" pill and a row of discs.</summary>
    Color,
    /// <summary>One of a few: segments, or a select field when the captions do not fit side by side.</summary>
    Choice,
    /// <summary>A short text.</summary>
    Text,
    /// <summary>A whole number within a range, with a unit.</summary>
    Number,
    /// <summary>Yes or no.</summary>
    Toggle,
    /// <summary>Which of a few to show: a box to tick for each, always at least one ticked. Stored as the list of ticked ids; nothing stored = all.</summary>
    Checks,
}

/// <summary>
/// One setting of a widget, as the widget declares it and the settings card draws it (1.45).
/// The card needs nothing else from the widget, so a new widget type gets its card from this
/// list alone. Labels and captions are already in the app's language. The value lives in the
/// layout item under <see cref="Key"/> (<see cref="DashOptions"/>); a value equal to the default
/// is not stored at all, and an empty text or number field means the default, which the field
/// shows in grey.
/// </summary>
internal sealed record DashOptionDef(string Key, DashOptKind Kind, string Label)
{
    /// <summary>Choice and Checks: the stored ids with their captions.</summary>
    public IReadOnlyList<(string Id, string Caption)> Items { get; init; } = Array.Empty<(string, string)>();

    /// <summary>Choice and Color: the id in use when nothing is stored.</summary>
    public string Default { get; init; } = "";

    /// <summary>
    /// What "default" stands for, with the settings being edited: for a Color, and for the colour
    /// that goes with a field (<see cref="ColorKey"/>). It may depend on another setting (the
    /// speed line takes the dial's colour).
    /// </summary>
    public Func<DashOptions, Color>? Auto { get; init; }

    /// <summary>
    /// Color: the colours the "Default" pill shows when default is not one colour (a temperature
    /// dial is green, amber and red by itself). Empty = the one from <see cref="Auto"/>.
    /// </summary>
    public IReadOnlyList<Color> AutoPie { get; init; } = Array.Empty<Color>();

    /// <summary>
    /// Text, Number and Toggle: fields naming the same row stand on one line of the card (two
    /// thresholds and the switch that turns them off). "" = the field pairs up with its neighbour.
    /// </summary>
    public string Row { get; init; } = "";

    /// <summary>
    /// Text, Number and Toggle: the key of a colour that belongs to this field (the label's
    /// colour, the colour a threshold turns the dial to). The card shows it as a small colour
    /// button beside the field; "auto" or one of the palette, like a Color. "" = none.
    /// </summary>
    public string ColorKey { get; init; } = "";

    /// <summary>Text: grey text in an empty field (what the widget shows then).</summary>
    public string Placeholder { get; init; } = "";

    /// <summary>Number: the allowed range and the value in use when the field is empty.</summary>
    public int Min { get; init; }
    public int Max { get; init; } = 100;
    public int DefaultInt { get; init; }

    public string Unit { get; init; } = "";

    /// <summary>Toggle: the state when nothing is stored.</summary>
    public bool DefaultOn { get; init; }

    /// <summary>
    /// Toggle: switches naming the same group cannot all be off (the two lines of the fan curve);
    /// the last one on does not switch off. "" = no such rule.
    /// </summary>
    public string KeepOne { get; init; } = "";

    /// <summary>A small grey line under the field.</summary>
    public string Hint { get; init; } = "";

    /// <summary>A check across fields (the top of a scale above its bottom); false marks this field and keeps the card open.</summary>
    public Func<DashOptions, bool>? Rule { get; init; }

    /// <summary>False while another setting switches this one off (the thresholds under "No warnings"): the field is dimmed and keeps its value.</summary>
    public Func<DashOptions, bool>? EnabledWhen { get; init; }

    /// <summary>The number this setting holds in <paramref name="o"/>: the default, or a value within the range.</summary>
    public int NumberIn(DashOptions o) => o.Int(Key, DefaultInt, Min, Max);
}
