namespace MSIProfileSwitcher;

/// <summary>
/// Per-model EC definition: which firmware it matches, the EC addresses, and the
/// per-profile write recipes. Adding a new MSI model = adding one entry here.
/// Addresses come from the msi-ec project; recipes are derived from a per-scenario
/// EC diff on the actual machine (see scripts/diagnostics + docs/TECHNICAL).
/// </summary>
public sealed class DeviceProfile
{
    public required string Name { get; init; }
    public required string[] FirmwarePrefixes { get; init; }   // matched against the EC firmware string

    // EC register addresses (defaults = CONF_G2_10 / 17S1IMS1 family)
    public byte ShiftMode { get; init; } = 0xD2;
    public byte FanMode { get; init; } = 0xD4;
    public byte CpuTemp { get; init; } = 0x68;
    public byte GpuTemp { get; init; } = 0x80;
    public byte CpuFan { get; init; } = 0x71;
    public byte GpuFan { get; init; } = 0x89;
    public byte ChargeCtrl { get; init; } = 0xD7;

    // values used to detect the current profile
    public byte FanSilentValue { get; init; } = 0x1D;
    public byte ShiftTurboValue { get; init; } = 0xC4;
    public byte ShiftEcoValue { get; init; } = 0xC2;

    public required Dictionary<ProfileId, (byte addr, byte val)[]> Recipes { get; init; }

    public bool Matches(string firmware) =>
        !string.IsNullOrEmpty(firmware) &&
        FirmwarePrefixes.Any(p => firmware.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}

public static class Devices
{
    public static readonly DeviceProfile[] All =
    {
        new()
        {
            Name = "MSI Raider GE78HX 13V",
            FirmwarePrefixes = new[] { "17S1IMS1" },   // 17S1IMS1.105 / .113 / .114 (CONF_G2_10)
            Recipes = new()
            {
                [ProfileId.Silent]       = new (byte, byte)[] { (0xD2, 0xC1), (0x34, 0x00), (0xEB, 0x00), (0xD4, 0x1D) },
                [ProfileId.Balanced]     = new (byte, byte)[] { (0xD2, 0xC1), (0x34, 0x01), (0xEB, 0x00), (0xD4, 0x0D) },
                [ProfileId.Extreme]      = new (byte, byte)[] { (0xD2, 0xC4), (0x34, 0x01), (0xEB, 0x00), (0xD4, 0x0D) },
                [ProfileId.SuperBattery] = new (byte, byte)[] { (0xD2, 0xC2), (0x34, 0x01), (0xEB, 0x0F), (0xD4, 0x0D) },
            },
        },
        // Add more MSI models here. See CONTRIBUTING / the model-support issue template.
    };

    /// <summary>Returns the device matching the EC firmware, or null if unsupported.</summary>
    public static DeviceProfile? Detect(string firmware) =>
        All.FirstOrDefault(d => d.Matches(firmware));
}
