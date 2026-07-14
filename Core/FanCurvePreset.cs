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

    /// <summary>Shape/range check against the device's table size before any EC write.</summary>
    public bool IsValid(int points) =>
        !string.IsNullOrWhiteSpace(Name) &&
        CpuTemp.Length == points && CpuSpeed.Length == points &&
        GpuTemp.Length == points && GpuSpeed.Length == points &&
        CpuSpeed.All(v => v is >= 0 and <= 100) && GpuSpeed.All(v => v is >= 0 and <= 100) &&
        CpuTemp.All(v => v is >= 0 and <= 115) && GpuTemp.All(v => v is >= 0 and <= 115);

    public FanCurvePreset Clone() => new()
    {
        Name = Name,
        CpuTemp = (int[])CpuTemp.Clone(), CpuSpeed = (int[])CpuSpeed.Clone(),
        GpuTemp = (int[])GpuTemp.Clone(), GpuSpeed = (int[])GpuSpeed.Clone(),
    };
}
