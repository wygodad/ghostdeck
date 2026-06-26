namespace MSIProfileSwitcher;

public enum Tier { Tested, Experimental }

/// <summary>
/// Per-model EC definition: firmware match, EC addresses, per-profile recipes, and a tier.
/// Tested = verified on real hardware. Experimental = built from msi-ec's documented
/// shift/fan registers but NOT verified (the "Silent" power-cap behaviour is unconfirmed).
/// Adding a model = one entry below.
/// </summary>
public sealed class DeviceProfile
{
    public required string Name { get; init; }
    public required string[] FirmwarePrefixes { get; init; }
    public Tier Tier { get; init; } = Tier.Tested;

    // EC register addresses (defaults = G2 family / 17S1IMS1)
    public byte ShiftMode { get; init; } = 0xD2;
    public byte FanMode { get; init; } = 0xD4;
    public byte CpuTemp { get; init; } = 0x68;
    public byte GpuTemp { get; init; } = 0x80;
    public byte CpuFan { get; init; } = 0x71;
    public byte GpuFan { get; init; } = 0x89;
    public byte ChargeCtrl { get; init; } = 0xD7;

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
    // Generic recipe set from documented msi-ec shift_mode + fan_mode (+ optional super_battery).
    // Used for EXPERIMENTAL models. Note: does NOT include our tested model's undocumented
    // 0x34 power-cap co-flag — so on these models "Silent" may not cap power the same way.
    private static Dictionary<ProfileId, (byte, byte)[]> StdRecipes(byte shift, byte fan, byte? superBatt)
    {
        (byte, byte)[] R(byte shiftVal, byte fanVal, bool sbOn)
        {
            var l = new List<(byte, byte)> { (shift, shiftVal), (fan, fanVal) };
            if (superBatt is byte sb) l.Add((sb, (byte)(sbOn ? 0x0F : 0x00)));
            return l.ToArray();
        }
        return new()
        {
            [ProfileId.Silent]       = R(0xC1, 0x1D, false),   // comfort + fan silent
            [ProfileId.Balanced]     = R(0xC1, 0x0D, false),   // comfort + fan auto
            [ProfileId.Extreme]      = R(0xC4, 0x0D, false),   // turbo   + fan auto
            [ProfileId.SuperBattery] = R(0xC2, 0x0D, true),    // eco     + fan auto + super-batt
        };
    }

    public static readonly DeviceProfile[] All =
    {
        // ---------- TESTED ----------
        new()
        {
            Name = "MSI Raider GE78HX / Vector GP78HX 13V",
            FirmwarePrefixes = new[] { "17S1IMS1" },
            Tier = Tier.Tested,
            Recipes = new()
            {
                [ProfileId.Silent]       = new (byte, byte)[] { (0xD2, 0xC1), (0x34, 0x00), (0xEB, 0x00), (0xD4, 0x1D) },
                [ProfileId.Balanced]     = new (byte, byte)[] { (0xD2, 0xC1), (0x34, 0x01), (0xEB, 0x00), (0xD4, 0x0D) },
                [ProfileId.Extreme]      = new (byte, byte)[] { (0xD2, 0xC4), (0x34, 0x01), (0xEB, 0x00), (0xD4, 0x0D) },
                [ProfileId.SuperBattery] = new (byte, byte)[] { (0xD2, 0xC2), (0x34, 0x01), (0xEB, 0x0F), (0xD4, 0x0D) },
            },
        },

        // ---------- EXPERIMENTAL (from msi-ec, unverified, opt-in) ----------
        // G2 family — same EC layout as the tested model (shift 0xD2 / fan 0xD4 / super-batt 0xEB)
        new() { Name = "MSI Raider GE68HX 13V",          FirmwarePrefixes = new[] { "15M2IMS1" }, Tier = Tier.Experimental, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI GS66 Stealth",               FirmwarePrefixes = new[] { "16V1EMS1" }, Tier = Tier.Experimental, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Katana GF66",                FirmwarePrefixes = new[] { "1582EMS1" }, Tier = Tier.Experimental, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Katana GF76",                FirmwarePrefixes = new[] { "17L1EMS1" }, Tier = Tier.Experimental, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI GE66 Raider / GP66 Leopard", FirmwarePrefixes = new[] { "1543EMS1" }, Tier = Tier.Experimental, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },

        // G1 family — shift 0xF2 / fan 0xF4 / charge 0xEF, no super-battery register
        new() { Name = "MSI GS65 Stealth", FirmwarePrefixes = new[] { "16Q4EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF65 Thin",    FirmwarePrefixes = new[] { "16W2EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
    };

    public static DeviceProfile? Detect(string firmware) =>
        All.FirstOrDefault(d => d.Matches(firmware));
}
