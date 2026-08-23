namespace GhostDeck;

/// <summary>
/// A named fan curve: the temperature and speed tables for both fans (same shape as the EC
/// tables in <see cref="FanCurveSpec"/>). Presets live in settings.json, so the settings
/// backup (export/import) carries them automatically; a single preset can also be exported
/// as a standalone JSON file or shared on GitHub Discussions.
/// </summary>
public sealed class FanCurvePreset
{
    public string Name { get; set; } = "";
    public int[] CpuTemp { get; set; } = Array.Empty<int>();
    public int[] CpuSpeed { get; set; } = Array.Empty<int>();
    public int[] GpuTemp { get; set; } = Array.Empty<int>();
    public int[] GpuSpeed { get; set; } = Array.Empty<int>();

    /// <summary>Shape/range check against the device's table size and speed scale before any EC write.</summary>
    public bool IsValid(int points, int maxPct = 150) =>
        !string.IsNullOrWhiteSpace(Name) &&
        CpuTemp.Length == points && CpuSpeed.Length == points &&
        GpuTemp.Length == points && GpuSpeed.Length == points &&
        CpuSpeed.All(v => v >= 0 && v <= maxPct) && GpuSpeed.All(v => v >= 0 && v <= maxPct) &&
        CpuTemp.All(v => v is >= 0 and <= 115) && GpuTemp.All(v => v is >= 0 and <= 115);

    /// <summary>Convenience overload: validate against a device's curve spec.</summary>
    public bool IsValid(FanCurveSpec fc) => IsValid(fc.Points, fc.MaxFanPct);

    public FanCurvePreset Clone() => new()
    {
        Name = Name,
        CpuTemp = (int[])CpuTemp.Clone(), CpuSpeed = (int[])CpuSpeed.Clone(),
        GpuTemp = (int[])GpuTemp.Clone(), GpuSpeed = (int[])GpuSpeed.Clone(),
    };
}
