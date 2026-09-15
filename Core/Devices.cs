namespace GhostDeck;

public enum Tier { Tested, Experimental }

/// <summary>
/// Per-model fan-curve layout (read-only for now). Curve = N points of
/// (temperature threshold, fan speed %) stored in consecutive EC bytes, separate
/// tables for CPU (Fan 1) and GPU (Fan 2). Advanced mode value goes in FanMode.
/// Addresses differ per model; null on a DeviceProfile = no curve support.
/// </summary>
public sealed record FanCurveSpec(
    byte AdvancedModeValue,
    byte CpuTempBase, byte CpuSpeedBase,
    byte GpuTempBase, byte GpuSpeedBase,
    int Points,
    bool Verified = false,    // false = read-only preview (addresses unconfirmed on real hardware)
    bool SingleFan = false,   // true = board exposes ONE controllable curve (MSI Center shows a
                              // single slider); the editor hides the GPU plot and the GPU table
                              // is never written (it is a dead field on such boards, see #22)
    int MaxFanPct = 150);     // top of the speed scale the editor allows. 150 matches the vendor
                              // tools: MSI Center's Advanced sliders go to 150 % (byte-proven on
                              // 17S1IMS1: Save 150 -> 0x77/0x8F = 0x96) and MControlCenter hard-
                              // codes maximum=150 for every model. Stock curves stay <= 100 %
                              // (some boards ship >100 in the hidden 7th byte we never touch);
                              // built-in presets never exceed 100 - only a deliberate manual drag
                              // reaches the range above. Lower per model here if a board ever
                              // proves to misbehave.

/// <summary>
/// An extra value the shift-mode register accepts on some boards, on top of the three the four
/// profiles use. MSI Center presents it as a switch inside its top performance scenario rather
/// than as a fifth scenario, and the value differs per board, which is why it lives here and not
/// in a constant. Only the value is recorded, because a per-scenario capture proves only that:
/// whether the register accepts it from outside MSI Center, and what else moves with it, is what
/// the Power test measures. <see cref="Name"/> is the label the vendor's own software shows, kept
/// so a report is recognisable to its owner. Null on a DeviceProfile = no such value known.
/// </summary>
public sealed record FourthModeSpec(string Name, byte ShiftValue);

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

    // Cooler Boost (max fans) toggle. msi-ec documents this as address 0x98, bit 7 (mask 0x80)
    // across the whole G1/G2 range (msi-ec.c cooler_boost), matching MSI Center's Cooler Boost
    // button. Read-modify-write bit 7 only; fully reversible. See TECHNICAL §17.7.
    public byte CoolerBoost { get; init; } = 0x98;
    public byte CoolerBoostMask { get; init; } = 0x80;

    // Fan tachometer registers (0 = unknown -> RPM not shown). RPM = RpmConst / raw.
    public byte CpuRpmAddr { get; init; }
    public byte GpuRpmAddr { get; init; }
    // Wide (16-bit) tachometers: some boards hold the divisor as a big-endian byte PAIR at
    // addr (high) : addr+1 (low) - carriers so far read 0xC8:0xC9 / 0xCA:0xCB. A single-byte
    // read of such a pair yields garbage (~10000 rpm), which is why these boards kept RPM off
    // before this format existed. A model sets either the single-byte fields above or these,
    // never both; these fields hold the HIGH-byte address.
    public byte CpuRpmAddr16 { get; init; }
    public byte GpuRpmAddr16 { get; init; }
    public FanCurveSpec? FanCurve { get; init; }
    public int RpmConst { get; init; } = 478000;

    public byte FanSilentValue { get; init; } = 0x1D;
    public byte ShiftTurboValue { get; init; } = 0xC4;
    public byte ShiftEcoValue { get; init; } = 0xC2;

    // A fourth shift-mode value this board is known to accept (captured from the vendor software),
    // or null. Nothing writes it yet - it is what the Power test probes. See FourthModeSpec.
    public FourthModeSpec? FourthMode { get; init; }

    // Community attribution (Models tab "Thanks" column): GitHub login of the person whose
    // report/verification backs this entry, and the issue to link as a thank-you.
    public string Credit { get; init; } = "";
    public string CreditUrl { get; init; } = "";

    public required Dictionary<ProfileId, (byte addr, byte val)[]> Recipes { get; init; }

    public bool Matches(string firmware) =>
        !string.IsNullOrEmpty(firmware) &&
        FirmwarePrefixes.Any(p => firmware.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>The firmware prefix this device matched on, or null. Per-model experimental
    /// write consent is keyed by this exact prefix, so a BIOS update that changes the prefix
    /// lands back on read-only until the owner consents again.</summary>
    public string? MatchedPrefix(string firmware) =>
        string.IsNullOrEmpty(firmware) ? null :
        FirmwarePrefixes.FirstOrDefault(p => firmware.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}

public static class Devices
{
    // Version of the COMPILED tables below. Bump together with any model-data change; the
    // generated data/models.json carries the same number (CI byte-compares a fresh dump
    // against the committed file, so the two cannot drift). A downloaded database is used
    // only when its dataVersion is strictly NEWER than this (anti-rollback, see ModelDb).
    public const int DataVersion = 20260918;

    // A signed, newer database downloaded from the repo (ModelDb.LoadOverride). Null = the
    // compiled tables below are in effect. Volatile because it is applied on the UI thread and
    // read from the sampling and page-refresh threads.
    private static volatile ModelDb.Parsed? _override;

    /// <summary>
    /// Put a downloaded database in effect. Returns false when it is not newer than what is
    /// already effective: the anti-rollback lives HERE because the database is applied from
    /// several places (startup, the periodic check, the Models tab, the Settings button) and two
    /// of them racing must never walk the tables backwards.
    /// </summary>
    public static bool ApplyOverride(ModelDb.Parsed parsed)
    {
        if (parsed.DataVersion <= EffectiveDataVersion) return false;
        _override = parsed;
        return true;
    }
    public static bool UsingOverride => _override != null;
    public static int EffectiveDataVersion => _override?.DataVersion ?? DataVersion;

    /// <summary>The database in effect: the downloaded override when active, else the compiled tables.</summary>
    public static IReadOnlyList<DeviceProfile> All => _override?.Models ?? BuiltIn;

    // Wire-format accessors for ModelDb.Dump (always the COMPILED tables, never the override).
    internal static IReadOnlyDictionary<string, byte> KbdBacklightRef => KbdBacklightMap;
    internal static IReadOnlyCollection<string> NoWebcamCtrlRef => NoWebcamCtrl;
    internal static IReadOnlyDictionary<string, (byte Addr, bool Invert)> FnWinSwapRef => FnWinSwapMap;

    // Generic recipe set from documented msi-ec shift_mode + fan_mode (+ optional super_battery).
    // Used for EXPERIMENTAL models. Note: does NOT include our tested model's undocumented
    // 0x34 power-cap co-flag — so on these models "Silent" may not cap power the same way.
    /// <summary>
    /// Standard recipe plus ONE extra register written per profile. Used where a board needs a
    /// vendor byte that the shared recipe does not carry - today only the power-management byte
    /// 0xD6 on the GE66/GP66 board, see the comment at that model.
    /// </summary>
    private static Dictionary<ProfileId, (byte, byte)[]> StdRecipesPlus(byte shift, byte fan, byte? superBatt,
                                                                        byte addr, byte silent, byte balanced, byte extreme, byte superB)
    {
        var b = StdRecipes(shift, fan, superBatt);
        var extra = new Dictionary<ProfileId, byte>
        {
            [ProfileId.Silent] = silent, [ProfileId.Balanced] = balanced,
            [ProfileId.Extreme] = extreme, [ProfileId.SuperBattery] = superB,
        };
        return b.ToDictionary(kv => kv.Key, kv => kv.Value.Append((addr, extra[kv.Key])).ToArray());
    }

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

    // Modern-family fan-curve layout (same as the tested 17S1IMS1). The same fixed table addresses
    // (CPU temp 0x6A/speed 0x72, GPU temp 0x82/speed 0x8A) are what MControlCenter reads/writes for the
    // whole G2 family (src/operate.cpp), so they are practice-confirmed, not guessed. Verified = false is
    // NOT a write block (see TECHNICAL §19.2): it is a UI confidence marker only. Editing is allowed once
    // Experimental is on (like profile switching); the live preview is the sanity check, and it's reversible.
    private static readonly FanCurveSpec ModernCurve =
        new(0x8D, CpuTempBase: 0x69, CpuSpeedBase: 0x72, GpuTempBase: 0x81, GpuSpeedBase: 0x8A, Points: 6, Verified: false);

    // Same table layout as ModernCurve, but Verified: an owner set a known test curve in MSI Center and
    // GhostDeck's fan-curve wizard found those exact values at 0x72 (CPU) / 0x8A (GPU) — so the addresses
    // are hardware-confirmed for that model (not just family-inferred).
    private static readonly FanCurveSpec ModernCurveVerified =
        new(0x8D, CpuTempBase: 0x69, CpuSpeedBase: 0x72, GpuTempBase: 0x81, GpuSpeedBase: 0x8A, Points: 6, Verified: true);

    // ---------------------------------------------------------------------
    // (#26) Keyboard-backlight level register, per firmware prefix. Generated from msi-ec's
    // per-conf kbd_bl blocks (bl_state_address): write 0x80 | level (0-3 = off/low/mid/high),
    // read the low 2 bits. Boards absent here have no EC-level brightness register in msi-ec -
    // that includes the per-key RGB models (SteelSeries-controlled) and 158NIMS1, which msi-ec
    // lists under two confs with contradicting kbd_bl data. Hardware-verified additions for
    // boards outside msi-ec go here too (none yet).
    private static readonly Dictionary<string, byte> KbdBacklightMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["13P3EMS1"] = 0xD3, ["13P5EMS1"] = 0xD3, ["13Q2EMS1"] = 0xD3, ["13Q3EMS1"] = 0xD3, ["14C4EMS1"] = 0xD3, ["14C6EMS1"] = 0xD3, ["14D2EMS1"] = 0xD3, ["14D3EMS1"] = 0xD3,
        ["14F1EMS1"] = 0xD3, ["14J1IMS1"] = 0xD3, ["14L1EMS1"] = 0xD3, ["14N1EMS1"] = 0xD3, ["14N2EMS1"] = 0xD3, ["14P1IMS1"] = 0xD3, ["14P1IWS1"] = 0xD3, ["14QKIMS1"] = 0xD3, ["1552EMS1"] = 0xD3,
        ["1581EMS1"] = 0xD3, ["1582EMS1"] = 0xD3, ["1583EMS1"] = 0xD3, ["1584EMS1"] = 0xD3, ["1584IMS1"] = 0xD3, ["1585EMS1"] = 0xD3, ["1585EMS2"] = 0xD3, ["158PIMS1"] = 0xD3,
        ["1591EMS1"] = 0xD3, ["1592EMS1"] = 0xD3, ["1594EMS1"] = 0xD3, ["1596EMS1"] = 0xD3, ["159KIMS1"] = 0xD3, ["15A1EMS1"] = 0xD3, ["15A3EMS1"] = 0xD3, ["15H1IMS1"] = 0xD3,
        ["15H2IMS1"] = 0xD3, ["15H5EMS1"] = 0xD3, ["15K1IMS1"] = 0xD3, ["16R6EMS1"] = 0xD3, ["16R7IMS1"] = 0xD3, ["16R8IMS1"] = 0xD3, ["16R8IMS2"] = 0xD3, ["16RKIMS1"] = 0xD3,
        ["16RKIMS2"] = 0xD3, ["16S6EMS1"] = 0xD3, ["16S8EMS1"] = 0xD3, ["16V6EMS1"] = 0xD3, ["17L1EMS1"] = 0xD3, ["17L2EMS1"] = 0xD3, ["17L3EMS1"] = 0xD3, ["17L4EMS1"] = 0xD3,
        ["17LNIMS1"] = 0xD3, ["17M1EMS2"] = 0xD3,
        ["14C1EMS1"] = 0xF3, ["14D1EMS1"] = 0xF3, ["14DKEMS1"] = 0xF3, ["14DLEMS1"] = 0xF3, ["14JKEMS1"] = 0xF3, ["1551EMS1"] = 0xF3, ["155LEMS1"] = 0xF3, ["158KEMS1"] = 0xF3,
        ["158MEMS1"] = 0xF3, ["15HKEMS1"] = 0xF3, ["16R1EMS1"] = 0xF3, ["16R3EMS1"] = 0xF3, ["16R4EMS1"] = 0xF3, ["16R4EMS2"] = 0xF3, ["16R5EMS1"] = 0xF3, ["16S1EMS1"] = 0xF3,
        ["16S3EMS1"] = 0xF3, ["16U7EMS1"] = 0xF3, ["16V2EMS1"] = 0xF3, ["16W1EMS1"] = 0xF3, ["16W1EMS2"] = 0xF3, ["16W2EMS1"] = 0xF3, ["16WKEMS1"] = 0xF3, ["17E7EMS1"] = 0xF3,
        ["17E8EMS1"] = 0xF3, ["17F2EMS1"] = 0xF3, ["17F3EMS1"] = 0xF3, ["17F3EMS2"] = 0xF3, ["17F4EMS2"] = 0xF3, ["17F5EMS1"] = 0xF3, ["17F6EMS1"] = 0xF3, ["17FKEMS1"] = 0xF3,
    };

    /// <summary>(#26) Keyboard-backlight register for this firmware, or 0 = not supported.</summary>
    public static byte KbdBacklightFor(string firmware)
    {
        if (string.IsNullOrEmpty(firmware)) return 0;
        var map = _override?.KbdBacklight ?? KbdBacklightMap;
        return map.TryGetValue(FwPrefix(firmware), out var a) ? a : (byte)0;
    }

    // (#27) msi-ec documents the webcam switch (0x2E) and webcam block (0x2F) identically on
    // every conf; only these boards are annotated as having no hardware webcam control at all.
    private static readonly HashSet<string> NoWebcamCtrl = new(StringComparer.OrdinalIgnoreCase)
        { "159KIMS1", "15H5EMS1", "13P5EMS1" };

    /// <summary>(#27) Whether the EC webcam switch is expected to exist on this firmware.</summary>
    public static bool WebcamSupported(string firmware) =>
        !string.IsNullOrEmpty(firmware) && !(_override?.NoWebcamCtrl ?? NoWebcamCtrl).Contains(FwPrefix(firmware));

    // ---------------------------------------------------------------------
    // Fn/Windows key swap register, per firmware prefix. Generated from msi-ec's per-conf
    // fn_win_swap blocks: the bit is 4 on every conf; the address is 0xBF or 0xE8 and some
    // families invert the direction. Normalized semantics follow msi-ec's fn_key attribute:
    // (raw bit ^ invert) = 1 means the WIN key sits on the left (so Fn is on the right).
    // Boards absent here have no fn_win_swap block in msi-ec (no cross-conf contradictions
    // existed at generation time). 17S2IMS2 (same board as 17S1IMS1) is not listed in msi-ec
    // and stays out until an owner verifies it.
    private static readonly Dictionary<string, (byte Addr, bool Invert)> FnWinSwapMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // address 0xBF, invert=false
        ["14C1EMS1"] = (0xBF, false), ["14JKEMS1"] = (0xBF, false), ["1551EMS1"] = (0xBF, false), ["16JFEMS1"] = (0xBF, false), ["16P5EMS1"] = (0xBF, false), ["16S3EMS1"] = (0xBF, false),
        ["16U7EMS1"] = (0xBF, false), ["1782EMS1"] = (0xBF, false), ["1799EMS1"] = (0xBF, false), ["17E7EMS1"] = (0xBF, false), ["17E8EMS1"] = (0xBF, false), ["17E9EMS1"] = (0xBF, false),
        ["17F2EMS1"] = (0xBF, false), ["17F3EMS1"] = (0xBF, false), ["17F3EMS2"] = (0xBF, false), ["17F4EMS1"] = (0xBF, false), ["17F4EMS2"] = (0xBF, false), ["17F5EMS1"] = (0xBF, false),
        ["17F6EMS1"] = (0xBF, false), ["17FKEMS1"] = (0xBF, false), ["17G1EMS1"] = (0xBF, false), ["17G1EMS2"] = (0xBF, false), ["17G3EMS1"] = (0xBF, false), ["17H1EMS1"] = (0xBF, false),
        // address 0xBF, invert=true
        ["14D1EMS1"] = (0xBF, true), ["14DKEMS1"] = (0xBF, true), ["14DLEMS1"] = (0xBF, true), ["1541EMS1"] = (0xBF, true), ["1542EMS1"] = (0xBF, true), ["155LEMS1"] = (0xBF, true),
        ["158KEMS1"] = (0xBF, true), ["158LEMS1"] = (0xBF, true), ["158MEMS1"] = (0xBF, true), ["15CKEMS1"] = (0xBF, true), ["15HKEMS1"] = (0xBF, true), ["16Q2EMS1"] = (0xBF, true),
        ["16Q3EMS1"] = (0xBF, true), ["16Q4EMS1"] = (0xBF, true), ["16R1EMS1"] = (0xBF, true), ["16R3EMS1"] = (0xBF, true), ["16R4EMS1"] = (0xBF, true), ["16R4EMS2"] = (0xBF, true),
        ["16R5EMS1"] = (0xBF, true), ["16S1EMS1"] = (0xBF, true), ["16V1EMS1"] = (0xBF, true), ["16V2EMS1"] = (0xBF, true), ["16V3EMS1"] = (0xBF, true), ["16W1EMS1"] = (0xBF, true),
        ["16W1EMS2"] = (0xBF, true), ["16W2EMS1"] = (0xBF, true), ["16WKEMS1"] = (0xBF, true), ["17K2EMS1"] = (0xBF, true), ["17LLEMS1"] = (0xBF, true),
        // address 0xE8, invert=false
        ["13P3EMS1"] = (0xE8, false), ["13P5EMS1"] = (0xE8, false), ["13Q2EMS1"] = (0xE8, false), ["13Q3EMS1"] = (0xE8, false), ["14F1EMS1"] = (0xE8, false), ["14J1IMS1"] = (0xE8, false),
        ["14K1EMS1"] = (0xE8, false), ["14K2EMS1"] = (0xE8, false), ["14L1EMS1"] = (0xE8, false), ["14N1EMS1"] = (0xE8, false), ["14N2EMS1"] = (0xE8, false), ["14P1IMS1"] = (0xE8, false), ["14P1IWS1"] = (0xE8, false),
        ["14Q2EMS1"] = (0xE8, false), ["14QKIMS1"] = (0xE8, false), ["14T2EMS1"] = (0xE8, false), ["15A1EMS1"] = (0xE8, false), ["15A3EMS1"] = (0xE8, false), ["15Q3EMS1"] = (0xE8, false),
        ["15QKIMS1"] = (0xE8, false),
        // address 0xE8, invert=true
        ["14C4EMS1"] = (0xE8, true), ["14C6EMS1"] = (0xE8, true), ["14D2EMS1"] = (0xE8, true), ["14D3EMS1"] = (0xE8, true), ["1543EMS1"] = (0xE8, true), ["1544EMS1"] = (0xE8, true),
        ["1545IMS1"] = (0xE8, true), ["1552EMS1"] = (0xE8, true), ["1562EMS1"] = (0xE8, true), ["1563EMS1"] = (0xE8, true), ["1571EMS1"] = (0xE8, true), ["1572EMS1"] = (0xE8, true),
        ["1581EMS1"] = (0xE8, true), ["1582EMS1"] = (0xE8, true), ["1583EMS1"] = (0xE8, true), ["1584EMS1"] = (0xE8, true), ["1584IMS1"] = (0xE8, true), ["1585EMS1"] = (0xE8, true),
        ["1585EMS2"] = (0xE8, true), ["1587EMS1"] = (0xE8, true), ["158NIMS1"] = (0xE8, true), ["158PIMS1"] = (0xE8, true), ["1591EMS1"] = (0xE8, true), ["1592EMS1"] = (0xE8, true),
        ["1594EMS1"] = (0xE8, true), ["1596EMS1"] = (0xE8, true), ["159KIMS1"] = (0xE8, true), ["15B1EMS1"] = (0xE8, true), ["15F2EMS1"] = (0xE8, true), ["15F3EMS1"] = (0xE8, true),
        ["15F4EMS1"] = (0xE8, true), ["15F5EMS1"] = (0xE8, true), ["15FKIMS1"] = (0xE8, true), ["15FLIMS1"] = (0xE8, true), ["15FMIBA1"] = (0xE8, true), ["15G2EWS1"] = (0xE8, true),
        ["15H1IMS1"] = (0xE8, true), ["15H2IMS1"] = (0xE8, true), ["15H4IMS1"] = (0xE8, true), ["15H5EMS1"] = (0xE8, true), ["15K1IMS1"] = (0xE8, true), ["15K2EMS1"] = (0xE8, true),
        ["15M1IMS1"] = (0xE8, true), ["15M1IMS2"] = (0xE8, true), ["15M2IMS1"] = (0xE8, true), ["15M2IMS2"] = (0xE8, true), ["15M3EMS1"] = (0xE8, true), ["15P2EMS1"] = (0xE8, true),
        ["15P3EMS1"] = (0xE8, true), ["15P4EMS1"] = (0xE8, true), ["16R6EMS1"] = (0xE8, true), ["16R7IMS1"] = (0xE8, true), ["16R8IMS1"] = (0xE8, true), ["16R8IMS2"] = (0xE8, true),
        ["16RKIMS1"] = (0xE8, true), ["16RKIMS2"] = (0xE8, true), ["16S6EMS1"] = (0xE8, true), ["16S8EMS1"] = (0xE8, true), ["16V4EMS1"] = (0xE8, true), ["16V4EMS2"] = (0xE8, true),
        ["16V5EMS1"] = (0xE8, true), ["16V6EMS1"] = (0xE8, true), ["17K3EMS1"] = (0xE8, true), ["17K4EMS1"] = (0xE8, true), ["17K5IMS1"] = (0xE8, true), ["17KKIMS1"] = (0xE8, true),
        ["17L1EMS1"] = (0xE8, true), ["17L2EMS1"] = (0xE8, true), ["17L3EMS1"] = (0xE8, true), ["17L4EMS1"] = (0xE8, true), ["17L5EMS1"] = (0xE8, true), ["17L5EMS2"] = (0xE8, true),
        ["17L7EMS1"] = (0xE8, true), ["17LNIMS1"] = (0xE8, true), ["17M1EMS1"] = (0xE8, true), ["17M1EMS2"] = (0xE8, true), ["17N1EMS1"] = (0xE8, true), ["17P1EMS1"] = (0xE8, true),
        ["17P2EMS1"] = (0xE8, true), ["17Q1IMS1"] = (0xE8, true), ["17Q2IMS1"] = (0xE8, true), ["17S1IMS1"] = (0xE8, true), ["17S1IMS2"] = (0xE8, true), ["17S2IMS1"] = (0xE8, true),
        ["17S3EMS1"] = (0xE8, true), ["17T2EMS1"] = (0xE8, true), ["1822EMS1"] = (0xE8, true), ["1824EMS1"] = (0xE8, true), ["182KIMS1"] = (0xE8, true), ["182LIMS1"] = (0xE8, true),
    };

    /// <summary>Fn/Win swap register (address + direction invert) for this firmware, or null.</summary>
    public static (byte Addr, bool Invert)? FnWinSwapFor(string firmware)
    {
        if (string.IsNullOrEmpty(firmware)) return null;
        var map = _override?.FnWinSwap ?? FnWinSwapMap;
        return map.TryGetValue(FwPrefix(firmware), out var v) ? v : null;
    }

    private static string FwPrefix(string firmware)
    {
        int dot = firmware.IndexOf('.');
        return dot > 0 ? firmware[..dot] : firmware;
    }

    internal static readonly DeviceProfile[] BuiltIn =
    {
        // ---------- TESTED ----------
        new()
        {
            Name = "MSI Raider GE78HX 13V / Vector 17 HX A14V",  // 17S1IMS1 (13V, also Vector GP78HX 13V) + 17S2IMS2
            // MS-17S1 and MS-17S2 are different boards with a dump-confirmed identical EC layout.
            // Every 17S2IMS2 report on record comes from Vector 17 HX owners (issue #32 here,
            // msi-ec #668 upstream: "Vector 17 HX A14VGG"); no GE78 HX 14V owner has ever
            // reported, so the entry is named after the machines that supplied the evidence.
            // 17S2IMS2 stays Tier.Tested on those confirmations. See TECHNICAL §19.5.
            // The Vector 17 HX A14V ships the same MS-17S2 board (17S2IMS2.112, issue #32): its owner's
            // fan-curve wizard found the test curve at the shipped 0x72/0x8A — independent 14V confirmation.
            FirmwarePrefixes = new[] { "17S1IMS1", "17S2IMS2" },
            Tier = Tier.Tested,
            CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB,    // verified vs MSI Center (RPM = 478000 / raw)
            // Re-confirmed on firmware 17S1IMS1.114 by a 13VH owner (issue #115, MSI Center
            // 2.0.48): recipe bytes match in all four scenarios and all three hardware checks
            // passed - credit shared as thanks. (His Extreme column showed 0xD4=8D: his own
            // MSI Center Advanced curve active at capture time, not a board quirk.)
            Credit = "wygodad, megadude9704", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/1",

            // Fan-curve tables located via the test tool; 6 points each (read-only preview for now).
            // First point is the 0°C→0% entry; tables verified 1:1 against MSI Center (6 points each).
            FanCurve = new FanCurveSpec(0x8D, CpuTempBase: 0x69, CpuSpeedBase: 0x72, GpuTempBase: 0x81, GpuSpeedBase: 0x8A, Points: 6, Verified: true),
            Recipes = new()
            {
                // 0x34 matches MSI Center 2.0.48 exactly: 0x00 ONLY in Extreme (unlocks turbo power), 0x01 elsewhere.
                [ProfileId.Silent]       = new (byte, byte)[] { (0xD2, 0xC1), (0x34, 0x01), (0xEB, 0x00), (0xD4, 0x1D) },
                [ProfileId.Balanced]     = new (byte, byte)[] { (0xD2, 0xC1), (0x34, 0x01), (0xEB, 0x00), (0xD4, 0x0D) },
                [ProfileId.Extreme]      = new (byte, byte)[] { (0xD2, 0xC4), (0x34, 0x00), (0xEB, 0x00), (0xD4, 0x0D) },
                [ProfileId.SuperBattery] = new (byte, byte)[] { (0xD2, 0xC2), (0x34, 0x01), (0xEB, 0x0F), (0xD4, 0x0D) },
            },
        },

        // Crosshair A16 HX (D7W/D8W) — full per-scenario EC dumps (issues #3/#4, fw 15PLIMS1.106) confirm:
        // shift (0xD2: C1/C1/C4/C2), fan (0xD4: 1D/0D/0D/0D), no super-batt register (0xEB stays 00, hence null),
        // 0x34 constant at 01, the ModernCurve tables (0x69/0x72/0x81/0x8A) hold a valid ascending curve, and
        // fan RPM lives at 0xC9/0xCB (varies per scenario). Real-hardware write-tested in issue #5: Silent
        // measurably lowers CPU package power/clocks vs Balanced (HWiNFO64), promoted to Tested. Note: Silent
        // and Super Battery read identically on this unit (both ~35.7-35.8 W in the owner's test) — unlike the
        // Intel reference board, ECO shift (C2) doesn't cap further than Comfort+silent-fan (C1/1D) here.
        // Fan curve VERIFIED (issue #11): the owner ran the wizard and it found the test curve at exactly
        // 0x72 (CPU) / 0x8A (GPU) — the shipped ModernCurve addresses — so ModernCurveVerified.
        new() { Name = "MSI Crosshair A16 HX (D7W/D8W)", FirmwarePrefixes = new[] { "15PLIMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "cesarcamps", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/5" },

        // Sword 16 HX B13V / B14V (15P2EMS1) — owner per-scenario dump (issue #6) matches StdRecipes 1:1:
        // shift 0xD2 C1/C1/C4/C2, fan 0xD4 1D/0D/0D/0D, super-batt 0xEB=0F only in Super Battery. Real-hardware
        // write-tested by the owner (issue #6): Cinebench 2026 + HWiNFO64 confirm each profile hits the intended
        // package-power limit, and the fan curve behaves on par with MSI Center. Promoted to Tested.
        // Fan RPM at 0xC9/0xCB (same as the tested G2 GE78HX): the #6 dump shows 0xC9 = C6/C0/C4/C0 across
        // scenarios → 478000/raw ≈ 2400-2490 RPM (plausible, load-varying); 0xCB = 00 (dGPU fan idle at capture).
        // Enabled so CPU fan RPM shows (issue #7); owner-confirm the value against HWiNFO.
        // Silent on THIS board measured as fan-only (owner power test, issue #93): over 60 s phases
        // Silent and Balanced completed the same work (within 0.04 %, against ~2 % second-to-second
        // noise) at the same clocks (4465 vs 4453 MHz), and only the fans differed (3053 vs 3665 RPM,
        // 3 °C cooler). The same run separated Extreme by 12 %, so the method had the sensitivity;
        // a cap that only tightens after minutes would still be invisible to it. Not a fault and nothing is written differently - the same
        // 0xD4 = 0x1D means "less noise" here and "less power" on a GE78HX. Recorded so nobody later
        // reads the equal work columns as a bug (FAQ: "Does Silent lower power on every laptop?").
        // Fan curve VERIFIED (issue #8): the owner set a known test curve in MSI Center and the wizard found
        // it at exactly 0x72 (CPU) / 0x8A (GPU) — the shipped ModernCurve addresses — so ModernCurveVerified.
        new() { Name = "MSI Sword 16 HX B13V / B14V", FirmwarePrefixes = new[] { "15P2EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "skandy121", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/6" },

        // Raider GE67 HX 12U (1545IMS1) — owner per-scenario snapshot (issue #14) matches StdRecipes 1:1:
        // shift 0xD2 C1/C1/C4/C2, fan 0xD4 1D/0D/0D/0D, super-batt 0xEB=0F only in Super Battery. Owner
        // hardware-confirmed all three checks (Silent lowers power/noise vs Balanced, Extreme unlocks,
        // switching stable), so promoted to Tested. Fan curve VERIFIED (issue #16): the owner ran the wizard
        // and it reported the test curve at 0x72 (CPU) / 0x8A (GPU) — the shipped ModernCurve addresses.
        //   RPM: 16-bit wide-tach pairs 0xC8:0xC9 / 0xCA:0xCB (the sixth wide-tach carrier).
        //   The two formats cannot be told apart above ~1870 rpm, because there the raw
        //   divisor fits in one byte and the high byte reads 00 - which is why a second
        //   owner's Fan Boost capture (issue #164, 62/61 = ~4880/4930 rpm) looked single-byte.
        //   His idle side-by-side settles it: a single-byte read of 0xC9=0x4D shows 6207 rpm
        //   where HWiNFO64 and MSI Center read ~1435-1500, and the pair 0x01:0x4D = 333 as
        //   one 16-bit divisor is exactly HWiNFO's recorded 1435 rpm minimum.
        new() { Name = "MSI Raider GE67 HX 12U", FirmwarePrefixes = new[] { "1545IMS1" }, Tier = Tier.Tested,
                CpuRpmAddr16 = 0xC8, GpuRpmAddr16 = 0xCA, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "alibi90, Hobby-Schrauber-88", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/14" },

        // Raider GE76 12UE (17K4EMS1) — owner per-scenario snapshot (issue #47) matches StdRecipes:
        // shift 0xD2 C1/C1/C4/C2, fan 0xD4 1D/…/0D/0D, super-batt 0xEB=0F only in Super Battery.
        // Balanced read 0x8D (advanced fan) rather than 0x0D because a custom curve from the curve
        // capture was still active — 0x8D is exactly the AdvancedModeValue we write for curves, so
        // the map holds. Fan curve VERIFIED (issue #45): the owner's test curve (CPU 25/35/45/55/65/75,
        // GPU 20/30/40/50/60/70) sits byte-for-byte at 0x72 / 0x8A — the shipped ModernCurve addresses.
        // RPM: 0xC9/0xCB carry plausible values in both dumps (e.g. 0x8E/0xD0 ≈ 3366/2298 RPM) — same
        // layout as the other tested G2 boards.
        // Sold as both 12UE and 12UGS (same MS-17K4 board; the #47 owner's unit is a 12UGS).
        new() { Name = "MSI Raider GE76 12UE / 12UGS", FirmwarePrefixes = new[] { "17K4EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "moragab1993", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/47" },

        // Cyborg 15 A12VF (15K1IMS1) — owner per-scenario dump (issue #19) matches StdRecipes 1:1:
        // shift 0xD2 C1/C1/C4/C2, fan 0xD4 1D/0D/0D/0D, super-batt 0xEB=0F only in Super Battery
        // (0x34 constant 00 across scenarios — ignored, StdRecipes doesn't touch it). ModernCurve
        // tables (0x69/0x72/0x81/0x8A) hold a valid ascending curve in the dump. Owner confirmed
        // all three hardware checks (Silent lowers power, Extreme unlocks, switching stable), so
        // Tier.Tested. Fan RPM: 0xC9 varies per scenario (9C/85/7D/7E ≈ 3000-3800 RPM), 0xCB = 00
        // (dGPU fan idle at capture) — same layout as the other tested G2 boards. Fan curve
        // VERIFIED (issue #29): the owner ran the wizard and it found the test curve at exactly
        // 0x72 (CPU) / 0x8A (GPU) — the shipped addresses. A second owner's capture (issue #31)
        // independently matched the recipe 1:1.
        // A13VF (15K1IMS1.111-.113) owner-verified too (issue #57): all hardware checks pass,
        // incl. the classic Silent cap, plus live RPM. Note for triage: MSI Center 2.0.72 on the
        // A13VF ships only 3 scenarios — its "Silent" writes the super-battery state (D2=C2 +
        // EB=0F), byte-identical to this recipe's Super Battery; Balanced/Extreme match 1:1.
        new() { Name = "MSI Cyborg 15 A12VF / A13VF", FirmwarePrefixes = new[] { "15K1IMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "hengeleng10-tech, M-Essa11", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/19" },

        // Thin GF63 12VE (16R8IMS1) — owner per-scenario dump (issue #21) matches StdRecipes 1:1:
        // shift 0xD2 C1/C1/C4/C2, fan 0xD4 1D/0D/0D/0D, super-batt 0xEB=0F only in Super Battery
        // (0x34 constant 00 — ignored). Fan RPM at 0xC9/0xCB (0xC9=A3 ≈ 2930 RPM in the capture,
        // 0xCB=00 = second fan idle). Fan curve VERIFIED for the CPU table (issue #22): the test
        // curve was found at exactly the shipped 0x72; this is a SINGLE-CURVE board — the owner
        // confirms MSI Center has always shown one fan slider (cross-checked with YAMDCC, it is
        // the CPU fan), so the wizard's "not located" was just the missing Fan 2. The GPU table
        // at 0x8A holds the family-standard layout; writing it appears to be a no-op here.
        new() { Name = "MSI Thin GF63 12VE", FirmwarePrefixes = new[] { "16R8IMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified with { SingleFan = true },
                Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "fwbvng", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/21" },

        // Titan 18 HX Dragon Edition (1824EMS1) — owner per-scenario dump (issue #23) matches
        // StdRecipes 1:1 (shift C1/C1/C4/C2, fan 1D/0D/0D/0D, 0xEB=0F only in Super Battery).
        // Fan curve VERIFIED (issue #24): the wizard found the test curve at exactly 0x72 / 0x8A.
        // Fan RPM at 0xC9/0xCB — both plausible in the capture (FB/F3 ≈ 1900-1970 RPM).
        new() { Name = "MSI Titan 18 HX Dragon Edition", FirmwarePrefixes = new[] { "1824EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Mung-Bean-Monkey", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/23" },

        // Bravo 15 B7ED (158PIMS1) — owner per-scenario dump (issue #25) confirms shift/fan 1:1
        // (0xD2 C1/C1/C4/C2, 0xD4 1D/0D/0D/0D). Like the Crosshair (the other tested AMD board),
        // 0xEB never leaves 00 → no super-battery register, hence null; the ECO shift (C2) still
        // applies in Super Battery (the owner's capture shows SB moving 0xF5/F7/F9 instead).
        new() { Name = "MSI Bravo 15 B7ED", FirmwarePrefixes = new[] { "158PIMS1" }, Tier = Tier.Tested,
                FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "AnyTw", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/25" },

        // GF63 Thin 11UC / 11SC (16R6EMS1) — owner per-scenario dump (issue #30) matches
        // StdRecipes 1:1 (shift C1/C1/C4/C2, fan 1D/0D/0D/0D, 0xEB=0F only in Super Battery).
        //   RPM enabled from a second owner's dumps (issue #118): the CPU tach reads as a live
        //   single-byte divisor at 0xC9 (A1/A0/9F = ~2400 rpm) while 0xC8/0xCA/0xCB stay 00 in
        //   every capture, so only the CPU address ships; reporter asked to cross-check HWiNFO64.
        //   Credit shared as thanks.
        new() { Name = "MSI GF63 Thin 11UC / 11SC", FirmwarePrefixes = new[] { "16R6EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Qaron-makaron, SrBeans", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/30" },

        // Katana GF66 11UE / 11UG (1581EMS1) — owner per-scenario dump (issue #34) matches
        // StdRecipes 1:1 (shift C1/C1/C4/C2, fan 1D/0D/0D/0D, 0xEB=0F only in Super Battery).
        //   Re-confirmed on firmware .107 by a second owner (issues #121/#122): recipes 1:1
        //   again, and his test curve sits byte-for-byte at the shipped 0x72/0x8A - curve
        //   VERIFIED. His dumps also show both tachs as live single-byte divisors at 0xC9/0xCB
        //   (D1-D3 idle, 96/CA under the test curve = ~2260-3190 rpm, high bytes always 00,
        //   unlike the 16-bit sibling 1585EMS1) - RPM enabled, reporter asked to cross-check
        //   against HWiNFO64. Credit shared as thanks.
        //   RPM CONFIRMED by that owner 2026-08-25 (#121 follow-up): GhostDeck's readout matches
        //   HWiNFO64 on his machine (screenshots), so the divisor scheme is hardware fact here too.
        new() { Name = "MSI Katana GF66 11UE / 11UG", FirmwarePrefixes = new[] { "1581EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "mewmrow, vlf1e", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/34" },

        // Bravo 17 C7VE / D7VFK (17LNIMS1) — owner per-scenario dump (issue #40, a D7VFK unit)
        // matches shift/fan 1:1 (0xD2 C1/C1/C4/C2, 0xD4 1D/0D/0D/0D) and the owner confirmed all
        // three hardware checks. Like the other AMD Bravos, 0xEB never leaves 00 → no
        // super-battery register (null). Fan curve VERIFIED (issue #41): wizard found the test
        // curve at exactly 0x72 / 0x8A.
        new() { Name = "MSI Bravo 17 C7VE / D7VFK", FirmwarePrefixes = new[] { "17LNIMS1" }, Tier = Tier.Tested,
                FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "inokra", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/40" },

        // Pulse/Katana 17 B13V/GK (17L5EMS1) — owner per-scenario dump (issue #38) matches
        // StdRecipes 1:1. Fan curve VERIFIED (issue #39): the wizard found the test curve at
        // exactly 0x72 / 0x8A. RPM: wide-tach 16-bit pairs 0xC8:0xC9 / 0xCA:0xCB (issue #76
        // dumps compute to ~1750/1530 RPM; a single-byte 0xC9 read was implausible, which is
        // how the format was found). First carrier of the wide-tach format; owner asked to
        // cross-check against HWiNFO64 once the readout ships.
        new() { Name = "MSI Pulse/Katana 17 B13V/GK", FirmwarePrefixes = new[] { "17L5EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr16 = 0xC8, GpuRpmAddr16 = 0xCA,
                FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "eaglent1", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/38" },

        // Crosshair 16 HX AI D2XW (15P4EMS1) — owner per-scenario dump (issue #44) shows shift
        // 0xD2 = C2/C1/C4 across scenarios and 0xEB 00↔0F, matching the shipped map (and upstream
        // msi-ec, which lists 15P4EMS1 with the standard G2 config incl. fan-silent 0x1D). In the
        // owner captures (#12, #44) the "Silent" step read eco C2 + fan auto 0x0D + 0xEB 0F —
        // identical to the Super Battery step; with this generation's renamed MSI Center
        // scenarios that may simply be a scenario-selection artifact, so it is NOT treated as a
        // board quirk. StdRecipes' Silent stays comfort C1 + fan-silent 0x1D (documented upstream,
        // owner-confirmed quieter and lower power on this unit). All three hardware checks
        // confirmed. Fan curve VERIFIED (issue #43): wizard found the test curve at exactly
        // 0x72 / 0x8A. Note: EC accepts curve speeds up to 150% here (see TODO MaxFanPct).
        new() { Name = "MSI Crosshair 16 HX AI D2XW", FirmwarePrefixes = new[] { "15P4EMS1" }, Tier = Tier.Tested,
                FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Harsh3456D", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/44" },

        // Modern 14 C12M (14J1IMS1) — owner per-scenario dump (issue #61, MSI Center 2.0.71)
        // matches StdRecipes: shift 0xD2 C2/C1/C4 with 0xEB=0F in the eco step (this MSI Center
        // generation labels that step "Silent" — the super-battery state, same as on the Cyborg
        // A13VF, #57); the "Super Battery" column read C4, a scenario-selection artifact like
        // issue #44, not a board quirk. All three hardware checks confirmed by the owner with
        // OUR recipes (classic Silent C1+1D audibly quieter and lower power), so Tier.Tested.
        // Fan curve VERIFIED (issue #60): the test curve sits exactly at the shipped 0x72;
        // the GPU table stayed at family defaults — SINGLE-FAN board (iGPU Modern), like the
        // Thin GF63 12VE. Fan RPM: 0xC9 reads 00 with the fan stopped and 0x51 (≈5900 RPM)
        // under load in the Extreme capture — a live tach; no second fan, so no 0xCB.
        new() { Name = "MSI Modern 14 C12M", FirmwarePrefixes = new[] { "14J1IMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, FanCurve = ModernCurveVerified with { SingleFan = true },
                Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "ping-myildirim", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/61" },

        // Pulse 16 AI C1VGKG/C1VFKG (15P3EMS1) - owner per-scenario dump (issue #68, MSI Center 2.0.48,
        // i.e. the last lineup with the classic Silent scenario) matches StdRecipes 1:1: shift 0xD2
        // C1/C1/C4/C2, fan 0xD4 1D/0D/0D/0D, super-batt 0xEB=0F only in Super Battery; charge limit
        // alive at 0xD7 (E4 = active at 100%). All three hardware checks confirmed by the owner, and
        // the power test (issue #85) proves the Silent cap outright: 59% of Balanced's work at 2954 vs
        // 5007 MHz, with the profile bytes read back intact after every phase. Fan curve VERIFIED on
        // both fans (issue #84): the test curve (CPU 25/35/45/55/65/75, GPU 20/30/40/50/60/70) sits
        // byte-for-byte at the shipped 0x72 / 0x8A. Fan RPM at 0xC9/0xCB, single-byte divisors
        // (9D/9C = ~3044/3064 RPM in Silent, both varying per scenario and alive under load).
        new() { Name = "MSI Pulse 16 AI C1VGKG/C1VFKG", FirmwarePrefixes = new[] { "15P3EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "migecko", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/68" },

        // Creator M16 B13VF / Pulse 15 B13VGK / Katana 15 B13UDXK (1585EMS1) - one board, three
        // market names, discovered via the "Actual model" report field. Promoted on paired reports:
        // #90 (Pulse 15 B13VGK, MSI Center 2.0.48 = the last lineup with the classic Silent
        // scenario) delivered full per-scenario dumps matching StdRecipes 1:1 - shift 0xD2
        // C1/C1/C4/C2, fan 0xD4 1D/0D/0D/0D with the real Silent value, 0xEB=0F only in Super
        // Battery; #89 (Katana 15 B13UDXK, MSI Center 2.0.72 - its "Silent" column shows the known
        // ECO-Silent/Super Battery artifact) confirmed all three hardware checks. Curve tables hold
        // the family-standard layout (structural only, no test curve captured - NOT curve-verified).
        // Fan tachometers are 16-BIT PAIRS 0xC8:0xC9 / 0xCA:0xCB (issue #90 dumps; a single-byte
        // read would show ~10000 rpm garbage) - wide-tach readout enabled, second carrier after
        // 17L5EMS1; owners asked to cross-check against HWiNFO64 once the readout ships.
        //   Fourth retail line on this board: Crosshair 16 A13V (issue #190, name confirmed
        //   on msi.com, spotted via the report form's "Actual model" field) - that owner's
        //   clean power test (1% drift) shows Silent at 92% of Balanced's work with fans at
        //   duty 35 vs 85 and CPU 79 vs 94 C, and Extreme at +3%.
        new() { Name = "MSI Creator M16 B13VF / Pulse 15 B13VGK / Katana 15 B13UDXK / Crosshair 16 A13V",
                FirmwarePrefixes = new[] { "1585EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr16 = 0xC8, GpuRpmAddr16 = 0xCA,
                FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Punssama & Gangan-Lin, GabrielGby", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/90" },

        // ---------- EXPERIMENTAL (from msi-ec, unverified, opt-in) ----------
        // G2 family — same EC layout as the tested model (shift 0xD2 / fan 0xD4 / super-batt 0xEB)
        new() { Name = "MSI Raider GE68HX 13V",          FirmwarePrefixes = new[] { "15M2IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // 16V1EMS1 is msi-ec CONF_G1_3 (G1 board), not G2 — corrected to 0xF2/0xF4 (was wrongly 0xD2/0xD4).
        new() { Name = "MSI GS66 Stealth", FirmwarePrefixes = new[] { "16V1EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Katana GF66",                FirmwarePrefixes = new[] { "1582EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Katana GF76",                FirmwarePrefixes = new[] { "17L1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // GE66 Raider / GP66 Leopard (1543EMS1) — owner dump from a GP66 Leopard 11UG (issue #52)
        // matches StdRecipes 1:1 including 0xEB=0F only in Super Battery. Fan curve VERIFIED
        // (issue #53): the test curve sits byte-for-byte at 0x72 / 0x8A. RPM 0xC9/0xCB vary per
        // scenario (A0/BC/B0/BC ≈ 2500-3000 RPM). Extreme confirmed by MEASUREMENT (issue #52):
        // HWiNFO package-power logs show our Extreme and MSI Center's Extreme reaching the same
        // power, which was the last open hardware check.
        //
        // 0xD6 — the extra byte this board needs for Balanced (issue #52, measured twice).
        // Symptom: with the shared recipe, our Balanced sat at a FIXED PL1 of 30 W - the same as
        // Silent - while MSI Center's Balanced held a moving 57-71 W, although both write the same
        // 0xD2/0xD4. The owner's per-scenario dump has exactly one configuration byte that differs
        // between the vendor's Silent and its Balanced: 0xD6 = 05 / 03 / 05 / 05. A one-off test
        // build writing those vendor values was run by the owner with Cinebench + HWiNFO logging:
        // PL1 became DYNAMIC (90 W at idle, settling at 55 W under load, 165 of 171 samples) and
        // package power averaged 58 W - i.e. the vendor's behaviour instead of the Silent-level
        // cap. The values written here are the vendor's own per-scenario values, Silent included,
        // so Silent keeps the cap MSI Center gives it.
        new() { Name = "MSI GE66 Raider / GP66 Leopard", FirmwarePrefixes = new[] { "1543EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified,
                Recipes = StdRecipesPlus(0xD2, 0xD4, 0xEB, 0xD6, silent: 0x05, balanced: 0x03, extreme: 0x05, superB: 0x05),
                Credit = "krystian-pytlik", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/52" },

        // G1 family — shift 0xF2 / fan 0xF4 / charge 0xEF, no super-battery register
        new() { Name = "MSI GS65 Stealth", FirmwarePrefixes = new[] { "16Q4EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF65 Thin",    FirmwarePrefixes = new[] { "16W2EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },

        // ===== BULK IMPORT (msi-ec / MControlCenter) — all EXPERIMENTAL, opt-in, unverified =====
        // G2 modern-HX siblings of the tested 17S1IMS1 board (same 0xD2/0xD4 layout).
        // Vector GP68 HX 13V (15M1IMS1) - fan curve VERIFIED (issue #131): the owner's test
        // curve sits byte-for-byte at the shipped 0x72/0x8A. RPM enabled at 0xC9/0xCB: live in
        // his dump (B1/A5) and the same divisor scheme the sister-board 15M1IMS2 owner confirmed
        // against HWiNFO64; his 0xCA read 01 once - the transient mid-update glitch known from
        // 15P3EMS1, which the plausibility gate absorbs. Tier stays Experimental (no hardware
        // checks yet).
        new() { Name = "MSI Vector GP68 HX 13V",            FirmwarePrefixes = new[] { "15M1IMS1" }, Tier = Tier.Experimental,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Fanilo-Nantenaina", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/131" },

        // Vector A16 HX A8WIG (15MMIMS1) - first owner report of the MS-15MM AMD board (issue
        // #130, MSI Center 2.0.48, wizard run on a machine the app did not recognise). Snapshot =
        // standard shift/fan recipes 1:1 (real Silent 0x1D); Super Battery writes no throttle
        // register (0xEB reads 00 in every scenario), so the eco recipe is the mode byte alone.
        // Curve VERIFIED on the spot: his test curve sits byte-for-byte at the shipped 0x72/0x8A.
        // RPM at 0xC9/0xCB, single-byte divisors alive (9F/CD = ~2350-3000 rpm).
        // TESTED (issue #135, the owner's clean third power-test run on 1.36.0): Silent does 69%
        // of Balanced's work at 58 vs 72 C and 3023 vs 3950 rpm - a real Silent cap; Extreme
        // adds 8% with the fans opened to ~6125 rpm; every phase read its bytes back intact and
        // the run drifted 4%. His first two runs (#133/#134) were disturbed mid-measurement
        // (repeat phase read back an eco shift) and were not scored. Board quirk to watch: the
        // fan DUTY bytes read implausible constants under load (103/112, once 150) while both
        // tachometers stay live and plausible - RPM is the readout to trust here.
        new() { Name = "MSI Vector A16 HX A8WIG",           FirmwarePrefixes = new[] { "15MMIMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "Matt99-sys", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/130" },
        // Raider GE68 HX 14VIG board (15M1IMS2), also sold as Vector 16 HX A13V - the owner's
        // report (issue #104, taken on MSI Center 2.0.48, the last lineup with the real Silent
        // scenario) matches StdRecipes on the three main profiles: shift 0xD2 C1/C1/C4, fan
        // 0xD4 1D/0D/0D with a true Silent 0x1D. Super Battery is NOT the family canon on this
        // board: the vendor writes shift 0xC6 (not 0xC2) and never touches 0xEB (00 in every
        // scenario; 0x50/0x51 rise to 4B/4B there as well), so the recipe mirrors the capture -
        // 0xC6 and no 0xEB write - and ShiftEcoValue makes detection report it. Eco itself is
        // unverified on hardware; the power test covers the three main profiles.
        // TESTED 2026-08-23 on the owner's power-test run (#105): Silent completes 73% of
        // Balanced's work at 2474 vs 3364 MHz, Extreme adds 10% on top (3649 MHz), recipe bytes
        // read back intact after every phase, 2% baseline drift. RPM: single-byte divisors at
        // 0xC9/0xCB as on the Pulse 16 AI (0xC9 = C8/C8/C8/CD = ~2390-2330 rpm, 0xCB = EB/A6 =
        // ~2030-2880 rpm where the GPU fan was awake; the wide-pair bytes 0xC8/0xCA sit at 00 in
        // all four columns) - enabled as the family scheme, owner asked to cross-check with HWiNFO.
        //   RPM CONFIRMED by the owner 2026-08-22 (#104 follow-up): GhostDeck's readout matches
        //   HWiNFO64 "100 percent" on his machine - the divisor hypothesis is hardware fact.
        //   Curve VERIFIED 2026-08-25 (issue #138): the owner set the test curve in MSI Center
        //   2.0.48 and the wizard found it byte-for-byte at the shipped 0x72 / 0x8A. His capture
        //   also shows MSI Center engaging the curve as 0xD4 = 0x9D (bit 7 on the value already
        //   there) where we write the constant 0x8D - both reach advanced mode.
        new() { Name = "MSI Raider GE68 HX 14VIG / Vector 16 HX A13V", FirmwarePrefixes = new[] { "15M1IMS2" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, ShiftEcoValue = 0xC6,
                Recipes = new()
                {
                    [ProfileId.Silent]       = new (byte, byte)[] { (0xD2, 0xC1), (0xD4, 0x1D) },
                    [ProfileId.Balanced]     = new (byte, byte)[] { (0xD2, 0xC1), (0xD4, 0x0D) },
                    [ProfileId.Extreme]      = new (byte, byte)[] { (0xD2, 0xC4), (0xD4, 0x0D) },
                    [ProfileId.SuperBattery] = new (byte, byte)[] { (0xD2, 0xC6), (0xD4, 0x0D) },
                },
                Credit = "dodi6161", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/104" },
        new() { Name = "MSI Raider GE68 HX 14VGG",          FirmwarePrefixes = new[] { "15M2IMS2" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Vector 16 HX AI (15M3EMS1) - the first model promoted on a MEASUREMENT rather than on its
        // owner's judgement. The Power test (issue #74) answers all three hardware checks with
        // numbers: Silent holds 84 % of Balanced's delivered work at 4728 MHz against 5432 and
        // 56 C against 69, so the Silent fan value caps power here as well as quietening the fans;
        // Extreme reaches 111 % at 6306 MHz, 93 C and 100 % fan; and every phase read its own
        // recipe back unchanged across a run that restored the starting profile.
        //   RPM: 0xC9/0xCB move monotonically with fan duty across four states in that run's dumps
        //   (idle B2/B2, Silent B0/B0, Balanced 88/87, Extreme 51/52 = 2685, 2715, 3514, 5901 RPM),
        //   which a constant cannot do.
        //   Curve VERIFIED (issue #70): the owner set the test curve in MSI Center, which was
        //   installed at the time, and the wizard found it at the shipped 0x72 / 0x8A.
        //   Fourth value C5 (issue #75): the owner's per-scenario capture on MSI Center 2.0.48
        //   shows the vendor's own Extreme Performance writing 0xD2 = C5, the value measured as
        //   C4's superior on the Stealth 16 AI+. Recorded so the app reads that state as Extreme
        //   instead of logging a change every poll, and so the Power test probes it. Named after
        //   what it is here - the value MSI Center itself writes - not after Stealth's "Apex".
        //   Re-confirmed on firmware .113 by a second owner (issues #113/#114, MSI Center 2.0.48,
        //   A2XWHG-275US): the snapshot matches every recipe byte, the test curve landed at the
        //   shipped 0x72/0x8A again, and both tachs are alive - credit shared with the original
        //   reporter as thanks.
        //   Re-confirmed on firmware .304 by a third owner (issues #136/#137, MSI Center 2.0.48):
        //   snapshot = recipes 1:1 with a real Silent, the test curve landed at the shipped
        //   0x72/0x8A again, and both tachs are alive. His vendor "Extreme Performance" wrote
        //   0xD2 = C4 (not C5), so the C5 fourth value looks build- or firmware-dependent - it
        //   stays recorded for detection, which accepts both. 0xD6 self-set 03 in Extreme =
        //   sixth board for the #52 observation.
        //   Re-confirmed a fourth time by the same third owner's power test on .304 (issue
        //   #144, zero drift, clean read-backs): Silent held 97 % of Balanced's work at 74 C
        //   and 3554/3549 rpm against 85 C and 4669/4900; Extreme added 3 %. First
        //   measurement of the C5 fourth mode on this board: the write was accepted and
        //   cleanly reverted, and it delivered 105 % against Extreme's 103 % while pushing
        //   the fans to 6331/6853 rpm and the CPU to 95 C - 2 % more work for a lot more
        //   noise and heat, so the app's Extreme recipe stays on C4.
        //   Dual retail name (issues #136/#144): the third owner's machine is a Raider 16 HX
        //   AI - the Raider 16 HX AI A2XW retail line matches his configuration exactly -
        //   on the same MS-15M3 board, hence both names in the entry.
        //   Stock fan tables in his dumps: CPU 0/40/48/60/75/89 (+ hidden 103 %), GPU
        //   0/48/60/70/82/93 (+ hidden 112 %) - exactly the values behind the app's
        //   "MSI default" button. The 38-86 set posted in #137 appears in no dump (it reads
        //   like the vendor editor's starting template, kept in that thread for reference).
        new() { Name = "MSI Vector 16 HX AI / Raider 16 HX AI A2XWHG / A2XWIG", FirmwarePrefixes = new[] { "15M3EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                FourthMode = new FourthModeSpec("MSI Center Extreme", 0xC5),
                Credit = "xulu19861102-hub, mithril01, H0tSTUff", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/74" },
        // Raider GE78 HX 14VHG (17S1IMS2) - owner-verified (issues #102/#103, MSI Center 2.0.48,
        // the last lineup with the real Silent scenario). The per-scenario capture matches
        // StdRecipes 1:1 in all four scenarios; 0x34 sat at 01 in every column of his capture,
        // so unlike the sibling 17S1IMS1 entry nothing writes it here - the recipes stay
        // standard. Power test (#103): 6% thermal drift, but Silent's effect dwarfs it - CPU
        // 80 C vs Balanced's 95 C, GPU 67 vs 90 C, fans at 48% duty vs 53-77%, at 92% of
        // Balanced's work (2393 vs 2601 MHz), recipes read back intact each phase; Extreme
        // measured level with Balanced (shared package budget, recorded). Curve tables hold the
        // family layout at the shipped addresses (stock values identical to the 17S1IMS1 board)
        // but no test curve was run, so the curve stays unverified. RPM at 0xC9/0xCB,
        // single-byte divisors, alive in every scenario (A6/A6/AA/B5 and A6/AA/A6/CD =
        // ~2300-2900 rpm).
        new() { Name = "MSI Raider GE78 HX 14VHG",          FirmwarePrefixes = new[] { "17S1IMS2" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "OrbNRG", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/102" },
        new() { Name = "MSI Raider GE78 HX Smart Touchpad 13V", FirmwarePrefixes = new[] { "17S2IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Vector 17 HX AI A2XWHG (17S3EMS1) - owner-verified (issue #171, firmware .107): the
        // per-scenario snapshot matches StdRecipes 1:1 with a REAL Silent column (0xD4=1D) on
        // MSI Center 2.0.73 - a 2.0.7x build showing a true Silent, recorded as-is - and all
        // three hardware checks confirmed, the Tested bar. 0x34 reads 01 in every scenario
        // (the GE78-family pattern), so the recipes leave it alone. No dumps in the report,
        // so RPM stays off and the curve keeps the family-standard unverified layout.
        new() { Name = "MSI Vector 17 HX AI A2XWHG",        FirmwarePrefixes = new[] { "17S3EMS1" }, Tier = Tier.Tested,
                FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "A7GoD", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/171" },

        // G2 family (shift 0xD2 / fan 0xD4 / super-batt 0xEB). Fan-curve tables use the shared modern layout
        // (CPU 0x6A/0x72, GPU 0x82/0x8A) that MControlCenter writes for this whole family (src/operate.cpp) —
        // so ModernCurve here is still a read-only preview (Verified=false) until eyeballed against MSI Center.
        new() { Name = "MSI Summit E13 Flip A12MT",         FirmwarePrefixes = new[] { "13P3EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Summit 13 AI+ Evo A2VM",        FirmwarePrefixes = new[] { "13P5EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 13 AI Evo A1MG",       FirmwarePrefixes = new[] { "13Q2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 13 AI+ Evo A2VMG",     FirmwarePrefixes = new[] { "13Q3EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 14 A11SCX",            FirmwarePrefixes = new[] { "14C4EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 14 Evo A12M",          FirmwarePrefixes = new[] { "14C6EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 14 B11M",                FirmwarePrefixes = new[] { "14D2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 14 B11MOU",              FirmwarePrefixes = new[] { "14D3EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Prestige 14 H B13U added on an owner's report (issue #160): his Prestige 14 H
        // B13UCX-601US (BIOS E14F1IMS.50F) runs this same 14F1EMS1 EC firmware - two retail
        // lines behind one EC identity, spotted by the reporter himself. His per-scenario
        // capture matches StdRecipes 1:1 with a real Silent column and four distinct columns;
        // 0xD6 read 03 only under the vendor's Extreme (the #52 observation).
        //   Tested via the same owner's clean power test (issue #160, drift 0%): Silent does
        //   the same work as Balanced on slower, cooler fans (duty 92 vs 110, 84 vs 88 C) -
        //   the fan-side Silent criterion; Extreme measured only +4% on this thin chassis
        //   (board trait, recorded, does not block promotion).
        //   Curve VERIFIED (CPU): his test curve sits byte-for-byte at the shipped 0x72.
        //   Single fan: the GPU table stayed factory with Fan 2 set in the wizard and the
        //   owner confirms the machine has no GPU fan (his Radeon is an external card) -
        //   the Thin 15 B12 signature. RPM: CPU tach live at 0xC9, single-byte divisor
        //   (0xCB stays 00).
        new() { Name = "MSI Summit E14 Flip Evo A12MT / Prestige 14 H B13U", FirmwarePrefixes = new[] { "14F1EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9,
                FanCurve = ModernCurveVerified with { SingleFan = true }, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Acoustichayes", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/160" },
        // moved to the Tested block above (issues #60 / #61) - Modern 14 C12M (14J1IMS1)
        // Stealth 14 Studio A13VF (14K1EMS1) - owner-verified end to end in one evening (issues
        // #107/#108/#109, MSI Center 2.0.48, the last lineup with the real Silent scenario). The
        // per-scenario dump matches StdRecipes 1:1 in all four scenarios (shift 0xD2 C1/C1/C4/C2,
        // fan 0xD4 1D/0D/0D/0D, super-batt 0xEB=0F only in Super Battery). Curve VERIFIED (#107):
        // the test curve (CPU 25/35/45/55/65/75, GPU 20/30/40/50/60/70) sits byte-for-byte at the
        // shipped 0x72 / 0x8A. Power test (#108, 3% drift, recipes read back intact each phase):
        // Silent completes 103% of Balanced's work at 3591 MHz while running 4 C cooler at lower
        // fan duty (81 vs 85%) - the combined CPU+GPU load is heat-limited on this thin 14"
        // chassis, so the profiles converge under full load (same pattern as the Stealth 17
        // Studio, #82); Silent still buys the firmware's quiet-fan preset. RPM at 0xC9/0xCB,
        // single-byte divisors, alive in every capture (B3/AE/B2/B5 and B2/B4/B2/B3, ~2600-2750
        // rpm). 0xD6 flips to 03 in Extreme on its own (EC-driven at 0xD2=C4, nothing writes it) -
        // fourth board showing the D6/power-level link tracked since #52.
        new() { Name = "MSI Stealth 14 Studio A13VF",       FirmwarePrefixes = new[] { "14K1EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "kltk", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/109" },
        new() { Name = "MSI Stealth 14 AI Studio A1VGG / A1VFG", FirmwarePrefixes = new[] { "14K2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 14 H D13M",              FirmwarePrefixes = new[] { "14L1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 14 AI Evo C1MG",       FirmwarePrefixes = new[] { "14N1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Prestige 14 AI Studio C1UDXG (14N2EMS1) - owner-verified end to end (issues #156/
        // #157/#158, MSI Center 2.0.48, app 1.36.0, firmware .103). Snapshot matches StdRecipes
        // 1:1 with a real Silent column (#156). Curve VERIFIED on both fans (#157): the GPU test
        // curve sits at the shipped 0x8A, and the CPU re-capture with all six sliders set to the
        // custom 26-76 values landed byte-for-byte at 0x72 from the first slot (the wizard's
        // "not found" only means it looked for the standard 25-75 values). Power test (#158,
        // read with its own caveats - 6% drift, a few percent of background load): Extreme
        // +23%, clear beyond the noise; Silent does Balanced's work (99 vs 100) at 46 vs 71%
        // fan duty and 3 C cooler - the "quieter, not slower" trait, recorded so nobody "fixes"
        // it. RPM: live single-byte divisors at 0xC9/0xCB (D8/CC = ~2210/2340 rpm in the curve
        // capture, high bytes 00) - the Katana-family scheme; owner asked to cross-check HWiNFO64.
        new() { Name = "MSI Prestige 14 AI Studio C1UDXG",  FirmwarePrefixes = new[] { "14N2EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "gkyrios", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/156" },
        new() { Name = "MSI Cyborg 14 A13VF",               FirmwarePrefixes = new[] { "14P1IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Creator M14 A13VE (14P1IWS1) - the Creator build of the SAME board as the Cyborg 14
        // above: its BIOS is E14P1IMS, and msi-ec carries the sibling prefix 14P1IMS1 in
        // CONF_G2_3, whose map is byte-identical to what StdRecipes writes here (shift 0xD2
        // C1/C2/C4, super-batt 0xEB mask 0x0F, fan 0xD4 with silent 0x1D, charge 0xD7).
        // The prefix itself is in NEITHER map: this owner's report (issue #91) and the still
        // unanswered msi-ec issue #692 are the only two captures of it. His per-scenario dump
        // matches StdRecipes 1:1 with a real Silent (0x1D), taken on MSI Center 2.0.48 - the
        // last lineup that still had the Silent scenario, so every column has a clean source.
        // Curve tables hold the family layout at the shipped ModernCurve addresses (CPU 0x69/
        // 0x72, GPU 0x81/0x8A) with ascending values. RPM: 0xC9 varies per scenario
        // (A3/A0/BA/BA = 2930/2990/2570 rpm); 0xCB stays 00, and the msi-ec reporter of the
        // same machine states one fan - so the second tachometer is left off.
        // Kbd backlight and Fn/Win swap CONFIRMED BY CHANGE by the owner (#91, 2026-08-15
        // comment): 0xD3 steps 80/81/82/83 with the backlight level (cycle Off->3->2->1) and
        // 0xE8 reads 01 unswapped / 11 swapped - the same layout as the sibling 14P1IMS1, so
        // both maps carry this prefix even though msi-ec still lists only the sibling.
        // TESTED 2026-08-23 on the owner's power-test run (#91, 2026-08-17): Silent drops the
        // fan from 3571 to 2964 rpm with work at 99 vs Balanced's 100 - it quiets the machine
        // without giving up performance - and every phase read its recipe back unchanged with
        // zero baseline drift. Extreme measured equal to Balanced under the all-core CPU+GPU
        // load (CPU pinned at ~2457 MHz throughout); likely a shared package budget on this
        // thin 14" chassis, so C4 is kept as the recipe and the equality is just recorded here.
        new() { Name = "MSI Creator M14 A13VE",             FirmwarePrefixes = new[] { "14P1IWS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "otherpartsoftheworld-spec", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/91" },
        new() { Name = "MSI Venture 14 AI A2HMG",           FirmwarePrefixes = new[] { "14Q2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Venture A14 AI+ A3HMG",         FirmwarePrefixes = new[] { "14QKIMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 14 Flip AI+ D3MTG",    FirmwarePrefixes = new[] { "14T2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Vector GP66 12UGS",             FirmwarePrefixes = new[] { "1544EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 15 A11M",                FirmwarePrefixes = new[] { "1552EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 15M A11SEK",            FirmwarePrefixes = new[] { "1562EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 15M A11UEK",            FirmwarePrefixes = new[] { "1563EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Creator Z16 A11UE",             FirmwarePrefixes = new[] { "1571EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Creator Z16 A12U",              FirmwarePrefixes = new[] { "1572EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Crosshair 15 B12UEZ / B12UGSZ (1583EMS1) - owner-verified (issue #142, MSI Center
        // 2.0.48, the last lineup with the real Silent scenario). The per-scenario capture
        // matches StdRecipes 1:1 on the three main profiles (shift 0xD2 C1/C1/C4, fan 0xD4
        // 1D/0D/0D) with four distinct columns, and the owner confirmed all three hardware
        // checks - the Tested bar. His power test (issue #143) could not be scored (uneven
        // CPU shares between phases, 16 % baseline drift, thermally saturated), but every
        // phase read its recipe bytes back intact under load.
        //   RPM: single-byte divisors at 0xC9/0xCB with the high bytes 0xC8/0xCA at 00 in
        //   every column, moving with load across his power-test dumps (B2 = ~2690, 84 =
        //   ~3620, 5E = ~5085 rpm) - the 1581/1584 sister scheme; owner asked to cross-check
        //   against HWiNFO.
        //   Super Battery: RESOLVED by a second owner (issue #154, firmware .111, four distinct
        //   columns matching the recipes 1:1) - his vendor capture shows Super Battery writing
        //   the standard 0xD2=C2, so the C4 left behind in the first owner's SB column (#142)
        //   was a stale leftover of that one capture, not a board trait. The C2 recipe stands,
        //   now vendor-confirmed on this board.
        //   0xD6 read 03 in the vendor's Extreme column only (05 elsewhere), yet stayed 05
        //   in the app's own Extreme phase of the power test - seventh board for the #52
        //   observation, and the first to show the value under the vendor's Extreme but not
        //   under ours.
        new() { Name = "MSI Crosshair 15 B12UEZ / B12UGSZ", FirmwarePrefixes = new[] { "1583EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "VamiCLAY, SvinoSuper", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/142" },
        // Katana GF66 12U / Sword 15 A12UC (1584EMS1) - one board behind two retail lines.
        // Tested via the Katana 12UD owner (issue #116: snapshot = StdRecipes 1:1 on MSI Center
        // 2.0.48, all three hardware checks confirmed, no dump). The Sword 15 A12UC owner
        // (issues #119/#120) supplied what that report lacked: full dumps (snapshot also 1:1)
        // and the curve proof - his test curve sits byte-for-byte at the shipped 0x72/0x8A, so
        // the curve is VERIFIED. His dumps read both tachs as live single-byte divisors at
        // 0xC9/0xCB (AD/C3/87 and BA/BB/98 = ~2450-3540 rpm, high bytes always 00, unlike the
        // 16-bit sibling 1585EMS1) - RPM enabled, both owners asked to cross-check HWiNFO64.
        new() { Name = "MSI Katana GF66 12U / Sword 15 A12UC", FirmwarePrefixes = new[] { "1584EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "messer2212, Error29112002", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/116" },
        new() { Name = "MSI Katana GF66 12UDO",             FirmwarePrefixes = new[] { "1584IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Katana 15 B12VEK / B12VFK / B12VGK (1585EMS2) - an owner's per-scenario dump set
        // (issue #184, MSI Center 2.0.48) matches StdRecipes 1:1 with a real Silent column;
        // his machine is a Katana 15 B12UDXK (name confirmed on msi.com, i5-12450H/i7-12650H
        // + RTX 3050 - the fourth retail line on this firmware, from the form's "Actual
        // model" field), hence the added name.
        // still Experimental until a power test or the three hardware checks. 0xD6 read 03
        // only under the vendor's Extreme (the #52 observation, twelfth board). RPM: 16-bit
        // pairs 0xC8:0xC9 / 0xCA:0xCB like the sibling 1585EMS1; in his captures the GPU pair
        // read 00:BA-00:C2 (~2500-2570 rpm, where the two formats coincide) and the CPU pair
        // 00:00 (fan parked) - enabled as pairs on the sibling's evidence, owner asked to
        // cross-check HWiNFO64 once the 16-bit readout ships.
        new() { Name = "MSI Katana 15 B12VEK / B12VFK / B12VGK / B12UDXK", FirmwarePrefixes = new[] { "1585EMS2" }, Tier = Tier.Experimental,
                CpuRpmAddr16 = 0xC8, GpuRpmAddr16 = 0xCA,
                FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Katana 15 HX B14WEK (1587EMS1) - owner per-scenario snapshot (issue #63) matches StdRecipes
        // 1:1: shift 0xD2 C1/C1/C4/C2, fan 0xD4 1D/0D/0D/0D, super-batt 0xEB=0F only in Super Battery.
        // All three hardware checks confirmed by the owner, so Tested. Fan curve VERIFIED (issue #64)
        // on BOTH fans: the test curve sits byte-for-byte at 0x72 and 0x8A, and the temperature tables
        // at 0x69 / 0x81 are ascending. RPM 0xC9/0xCB vary per scenario (A3/90/81/88 = 2930-3710 RPM).
        //   Re-confirmed by a second owner on .103 (issues #176/#177): a clean power test
        //   (drift 0%) with Silent at 98% of Balanced's work on slower fans (2906 vs 3552 rpm,
        //   67 vs 72 C) and Extreme at +63%, and his curve capture shows the test curve at the
        //   shipped addresses again (GPU byte-for-byte at 0x8A; CPU at 0x72 with the first
        //   slider stored as 26 for a requested 25 - a snap the wizard's exact-match search
        //   does not tolerate, the layout itself is not in doubt).
        //   Third owner (issue #191): a Katana 15 HX B14WFK - the RTX 50 refresh of the line,
        //   name confirmed on msi.com, same 1587EMS1 firmware, hence the second name. His
        //   test curve sits byte-for-byte at 0x72/0x8A on both fans - the second independent
        //   curve confirmation.
        //   Fourth report set (issues #192/#193, another B14WFK): snapshot 1:1 again, and a
        //   power test with Extreme at +45% (clear beyond that run's 12% drift) and Silent
        //   at 73% of Balanced's work on slower fans. Note the spread: the #177 machine's
        //   Silent cut almost nothing while this one cuts a quarter - both runs look valid,
        //   so it is recorded as unit-to-unit spread, not corrected either way.
        new() { Name = "MSI Katana 15 HX B14WEK / B14WFK", FirmwarePrefixes = new[] { "1587EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "zajebistylukasz-beep, DRLOGIC01, Osanosa, sensini82", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/63" },
        // Bravo 15 C7V (158NIMS1) — fan curve VERIFIED (issue #27): the wizard found the test
        // curve at exactly 0x72 / 0x8A. The owner's capture (issue #26) shows standard shift/fan
        // bytes, and like the other AMD Bravos 0xEB never leaves 00 → no super-battery register
        // (null). Stays Experimental until an owner confirms the hardware checks.
        // MSI ships the same 158N board in the Katana A15 AI B8VG (issue #80): that owner's
        // per-scenario capture matches this entry byte for byte (0xD2 C1/C1/C4/C2,
        // 0xD4 1D/0D/0D/0D, 0xEB pinned to 00), with live RPM at 0xC9/0xCB.
        new() { Name = "MSI Bravo 15 C7V / Katana A15 AI B8VG", FirmwarePrefixes = new[] { "158NIMS1" }, Tier = Tier.Experimental,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "dmas-dll, Lofre", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/27" },
        new() { Name = "MSI Summit E16 Flip A11UCT",        FirmwarePrefixes = new[] { "1591EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Summit E16 Flip A12UCT / A12MT", FirmwarePrefixes = new[] { "1592EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Prestige 16 Studio A13VE / Summit E16 Flip A13VFT (1594EMS1) - one board, two retail
        // lines; owner-verified (issues #127/#128, MSI Center 2.0.48). Snapshot = StdRecipes 1:1
        // in all four scenarios; power test clean (2% drift): Silent does 95% of Balanced's work
        // 7 C cooler on slower fans, Extreme unlocks +34% (4472 MHz), recipes read back intact
        // each phase. 0xD6 flips to 03 in Extreme by itself - FIFTH board for the #52
        // observation. RPM at 0xC9/0xCB, single-byte divisors alive (86/87/A0 = ~2700-2840 rpm).
        // Fan curve = family standard (issue #129): the owner's on-screen slider values map
        // 1:1 onto 0x72-0x77 / 0x8A-0x8F (proven by pairing a GE78 screen with its dump the
        // same way: 0/40/48/60/75/89 = 00 28 30 3C 4B 59 exactly), and the editor's speed
        // scale covers the board's stock 130 % top slider (the scale reaches 150, same as
        // MSI Center's own sliders). Verified stays false: the wizard saw only four of the
        // six tracer values in his run, so a full six-value tracer match is still owed.
        // Note for the whole family: one extra stock byte sits past the sliders at
        // 0x78/0x90 (103 % on GE78 boards, 130 % here) that MSI Center's UI never writes; we
        // do not touch it either.
        //   RPM CONFIRMED by the owner (#129 follow-up, 2026-08-23): both fans match HWiNFO64
        //   side by side (2987/2914). Board quirk from his captures: the GPU DUTY byte reads 0
        //   at idle while the GPU fan spins (tach alive) - most likely it only reports while
        //   the dGPU is awake (his power test showed a live value under GPU load); the same
        //   one-sided-duty family as 17P2EMS1's dead CPU duty. Status then shows "-" on the
        //   GPU fan dial while the RPM tile stays correct.
        new() { Name = "MSI Prestige 16 Studio A13VE / Summit E16 Flip A13VFT", FirmwarePrefixes = new[] { "1594EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Flo827", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/127" },
        new() { Name = "MSI Summit E16 AI Studio A1VETG",   FirmwarePrefixes = new[] { "1596EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige A16 AI+ A3HMG",        FirmwarePrefixes = new[] { "159KIMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 16 AI Evo B1MG",       FirmwarePrefixes = new[] { "15A1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 16 AI+ Evo B2VMG",     FirmwarePrefixes = new[] { "15A3EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 15M B12UE",             FirmwarePrefixes = new[] { "15B1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 16 Studio A13VG",       FirmwarePrefixes = new[] { "15F2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 16 AI Studio A1VHG",    FirmwarePrefixes = new[] { "15F3EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 16 AI Studio A1VFG",    FirmwarePrefixes = new[] { "15F4EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 16 AI A2HWFG",          FirmwarePrefixes = new[] { "15F5EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth A16 AI+ A3XVFG / A3XVGG", FirmwarePrefixes = new[] { "15FKIMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth A16 AI+ A3XWHG",        FirmwarePrefixes = new[] { "15FLIMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth A16 Mercedes AMG AI+ A3XWGG", FirmwarePrefixes = new[] { "15FMIBA1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI CreatorPro Z16HXStudio B13VJTO / B13VKTO", FirmwarePrefixes = new[] { "15G2EWS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 15 B13M",                FirmwarePrefixes = new[] { "15H1IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 15 B12HW",               FirmwarePrefixes = new[] { "15H2IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 15 H B13M",              FirmwarePrefixes = new[] { "15H4IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Modern 15 H AI C1MG",           FirmwarePrefixes = new[] { "15H5EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Cyborg 15 AI A1VFK",            FirmwarePrefixes = new[] { "15K2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Cyborg 15 B13WFKG / B2RWFKG / B2RWEKG (15Q3EMS1) - Tested on two owners' evidence.
        // Recipes: the first owner's vendor capture (issue #97) and the second owner's
        // snapshot (issue #140) both match StdRecipes 1:1. Measured (issue #146; the run was
        // clean - the report's not-idle warning was the fixed-bar false alarm on a 12-thread
        // CPU, shares were an even 83 % everywhere with 3 % drift): Extreme delivers +16 %
        // work; Silent does NOT cap CPU power on this board (104 % of Balanced's work at
        // near-identical temperatures) - it only keeps the fan ramp lower (GPU fan peaked
        // 57 % against Balanced's 70 %). The first owner's run (#97) showed the same no-cap
        // Silent, so this is a trait of the board, not a bad run - recorded here so nobody
        // "fixes" it later.
        //   Stock fan tables (consistent across the pre-experiment #145/#146 dumps):
        //   0/39/43/48/57/70 at 0x72-0x77 AND 0x8A-0x8F - both fans identical - with the
        //   hidden top byte 0x52 (82 %) at 0x78/0x90.
        //   RPM: wide-tach 16-bit pair 0xC8:0xC9, CPU only (single fan; 0xCA:0xCB read 00 in
        //   every dump). The #145 re-capture shows 0xC8:0xC9 = 01:19 = ~1700 rpm as a pair,
        //   while a single-byte read of 0xC9 would give 19120 rpm garbage - the same wide
        //   format as 17L5EMS1/1585EMS1 (third carrier); owner asked to cross-check against
        //   HWiNFO64 once the readout ships.
        //   Curve VERIFIED, single fan (#145 re-capture): the owner's second pass set all six
        //   sliders and the test curve sits byte-for-byte at the shipped 0x72 from the first
        //   slot. The GPU table stayed factory through both passes, the owner states his
        //   machine has one fan, and Cyborg 15 chassis teardowns (LaptopMedia A12V/A13V) show
        //   a single shared fan - the Thin GF63 12VE single-curve pattern.
        new() { Name = "MSI Cyborg 15 B13WFKG / B2RWFKG / B2RWEKG", FirmwarePrefixes = new[] { "15Q3EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr16 = 0xC8,
                FanCurve = ModernCurveVerified with { SingleFan = true }, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "parkisutama, tenduo", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/97" },
        new() { Name = "MSI Venture A15 AI A2HMG / A2HMTG", FirmwarePrefixes = new[] { "15QKIMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Cyborg 15 C13WEO (15T1EMS1, board MS-15T1) - NEW prefix, absent from msi-ec; added
        // from one owner's thorough report set (issues #173/#174/#175, i7-13620H + RTX 5050;
        // sold in some markets as "Cyborg 15 Max C13WEO"). His own read-only MSI_ACPI captures
        // (#174, scenario changes verified before each dump) show the standard recipes: the
        // 2.0.71 "Silent" tile writes the Super Battery set (C2 + 0xEB=0F - the known lineup
        // trap, which he identified himself), Balanced C1, Extreme C4; Apex adds 0xD2=C5 with
        // companion bytes moving (0x5A 00->01, 0xD9 05->15, 0xED C2->DA - observation only,
        // nothing written), so the C5 fourth mode ships for the power test to probe.
        //   Curve VERIFIED, single fan: the wizard's test curve sits byte-for-byte at the
        //   shipped 0x72 from the first slot (#175); MSI Center's hardware monitor lists
        //   Fan 1 only, 0xCA:0xCB read 00 everywhere and the GPU table never changes - the
        //   Cyborg 15Q3 pattern. Hidden trailing bytes 0x78/0x79 hold stock values above the
        //   six sliders (the GE78 pattern) - not written, like everywhere else.
        //   RPM: wide-tach 16-bit pair 0xC8:0xC9 (01:02 = ~1850 rpm idle), CPU only - the
        //   fifth wide-tach carrier; readout arrives with the release that ships the format.
        // Tier stays Experimental: the app could not write on the unrecognised machine, so
        // switching stability is unproven - his power test after the entry lands settles it.
        new() { Name = "MSI Cyborg 15 C13WEO",              FirmwarePrefixes = new[] { "15T1EMS1" }, Tier = Tier.Experimental,
                CpuRpmAddr16 = 0xC8,
                FourthMode = new FourthModeSpec("Apex", 0xC5),
                FanCurve = ModernCurveVerified with { SingleFan = true }, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI GV62 8RD",                      FirmwarePrefixes = new[] { "16JFEMS1" }, Tier = Tier.Experimental, ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Thin GF63 12HW",                FirmwarePrefixes = new[] { "16R7IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Thin 15 B12UCX / B12VE (16R8IMS2) - fan curve VERIFIED (issue #111): the test curve sits
        // byte-for-byte at the shipped 0x72; the GPU part was not written, the same signature as
        // the sibling 16R8IMS1 (Thin GF63 12VE) on the same MS-16R8 board, and teardown videos of
        // the Thin 15 B12 chassis show a SINGLE fan, so SingleFan like the sibling. Fan RPM: 0xC9
        // single-byte divisor, live through the whole power test (2791-5085 rpm).
        // TESTED on the owner's clean re-run (issue #159; the "not idle" banner there was the
        // fixed-bar false alarm on a 12-thread CPU - shares an even 83.2 everywhere, drift 3%):
        // Silent is a real cap, 87% of Balanced's work at 78 vs 95 C on slower fans. Board
        // trait recorded, not to be "fixed": EXTREME runs SLOWER than Balanced here (78% of its
        // work) with the CPU pinned in a flat 3086-3099 MHz band at only 79 C on fast fans -
        // a deliberate-looking clock cap under C4, not thermals (Balanced sat at 95 C and was
        // faster). The owner was asked for an optional HWiNFO power reading to chase it.
        // A second owner's per-scenario snapshot (issue #162) re-confirms every recipe byte
        // with four distinct columns - both owners share the credit.
        new() { Name = "MSI Thin 15 B12UCX / B12VE",        FirmwarePrefixes = new[] { "16R8IMS2" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, FanCurve = ModernCurveVerified with { SingleFan = true }, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "arcfybrr, pushtamper-nice", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/111" },
        new() { Name = "MSI Thin A15 B7VF",                 FirmwarePrefixes = new[] { "16RKIMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Thin A15 B7VF",                 FirmwarePrefixes = new[] { "16RKIMS2" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 15 A11SCX",            FirmwarePrefixes = new[] { "16S6EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Prestige 15 A12SC / A12UC",     FirmwarePrefixes = new[] { "16S8EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI GS66 Stealth 11UE / 11UG",      FirmwarePrefixes = new[] { "16V4EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Creator 15 A11UE",              FirmwarePrefixes = new[] { "16V4EMS2" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth GS66 12UE / 12UGS",     FirmwarePrefixes = new[] { "16V5EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 15 A13V",               FirmwarePrefixes = new[] { "16V6EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI GE76 Raider 10UG",              FirmwarePrefixes = new[] { "17K2EMS1" }, Tier = Tier.Experimental, ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GE76 Raider 11U / 11UH",        FirmwarePrefixes = new[] { "17K3EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // (Raider GE76 12UE moved to the Tested block above — issues #45 / #47.)
        new() { Name = "MSI Raider GE77 HX 12UGS",          FirmwarePrefixes = new[] { "17K5IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Alpha 17 C7VF / C7VG (17KKIMS1) - owner-verified (issues #152/#153, MSI Center 2.0.48,
        // app 1.36.0, firmware .115), the first Alpha-line board confirmed on real hardware.
        // Curve VERIFIED (#152): the test curve sits byte-for-byte at the shipped 0x72/0x8A on
        // BOTH fans. TESTED on his power test (#153): the run tripped the 99-C guard in Extreme
        // (thermally tight chassis - the safety cutoff working, noted, not a defect), but the
        // data is clean (own share an even 93 throughout): Silent delivers 89% of Balanced's
        // work at 76 vs 85 C on slower fans (57 vs 80% duty) - a real cap - and Extreme ran
        // +16% before the cutoff, with every phase reading its bytes back intact.
        //   RPM: live single-byte divisors at 0xC9/0xCB with high bytes 00 (Silent loaded
        //   A3 = ~2930 rpm, Extreme loaded 60 = ~4980) - the Katana-family scheme; owner
        //   asked to cross-check against HWiNFO64.
        //   Super Battery: no 0xEB write - the owner's per-scenario capture (issue #151, four
        //   distinct columns, recipes 1:1 otherwise) shows 0xEB at 00 in EVERY column including
        //   Super Battery, the same no-limiter pattern as the other AMD boards (Bravo 15/17,
        //   Raider A18). 0x34 reads 01 everywhere (left alone), and 0xD6 read 03 only under
        //   the vendor's Extreme (the #52 observation).
        new() { Name = "MSI Alpha 17 C7VF / C7VG",          FirmwarePrefixes = new[] { "17KKIMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "Liuwins", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/152" },
        // Katana GF76 11UC / 11UD (17L2EMS1) - owner per-scenario dump (issue #165, MSI Center
        // 2.0.61, app 1.36.0) matches StdRecipes 1:1 with a real Silent column and four distinct
        // columns; no hardware checks yet, so the tier stays Experimental until his power test.
        // RPM: CPU tach live as a single-byte divisor at 0xC9 (D0-D5 = ~2250 rpm) with 0xCB at
        // 00 in every column (GPU fan parked at idle, the GF63 pattern) - CPU address ships,
        // owner asked to cross-check against HWiNFO64.
        new() { Name = "MSI Katana GF76 11UC / 11UD",       FirmwarePrefixes = new[] { "17L2EMS1" }, Tier = Tier.Experimental,
                CpuRpmAddr = 0xC9, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Crosshair 17 B12UGZ",           FirmwarePrefixes = new[] { "17L3EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Katana GF76 12UC",              FirmwarePrefixes = new[] { "17L4EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Katana 17 B12UCXK / B12VGK (17L5EMS2) - fan curve VERIFIED (issue #125): the B12VGK
        // owner's test curve sits byte-for-byte at the shipped 0x72/0x8A. Tier stays
        // Experimental: his scenario capture (issue #124) holds identical bytes in all four
        // columns (the machine sat in one scenario throughout, the same procedure slip as
        // issue #58 on this prefix) and his power test (issue #126) ran with Fan Boost ON and
        // 8% drift, so both await clean re-runs. RPM stays off on purpose: the sibling
        // 17L5EMS1 reports fan speed as 16-bit wide-tach pairs, and one dump with
        // low-byte-only values cannot rule that out here - a dump with a live 0xC8 would.
        new() { Name = "MSI Katana 17 B12UCXK / B12VGK",    FirmwarePrefixes = new[] { "17L5EMS2" }, Tier = Tier.Experimental,
                FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "Dkrimz", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/125" },
        new() { Name = "MSI Katana 17 HX B14WGK",           FirmwarePrefixes = new[] { "17L7EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth GS76 11UG",             FirmwarePrefixes = new[] { "17M1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Creator 17 B11UE",              FirmwarePrefixes = new[] { "17M1EMS2" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Creator Z17 (17N1EMS1) — fan curve VERIFIED (issue #77): tracer speeds set in MSI Center
        // sit byte-for-byte at the shipped 0x72 / 0x8A. Tier stays Experimental: the same report's
        // power test ran thermally saturated (94-95 C in every phase), so Silent is unconfirmed.
        new() { Name = "MSI Creator Z17 A12UGST",           FirmwarePrefixes = new[] { "17N1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "oscarschulzbongert-oss", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/77" },
        new() { Name = "MSI Stealth GS77 12U",              FirmwarePrefixes = new[] { "17P1EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Stealth 17 Studio A13VI",       FirmwarePrefixes = new[] { "17P2EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Titan GT77 12UHS",              FirmwarePrefixes = new[] { "17Q1IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        new() { Name = "MSI Titan GT77HX 13VH",             FirmwarePrefixes = new[] { "17Q2IMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Sword 17 HX B14VGKG (17T2EMS1) - owner report and verification (issue #139, MSI Center
        // 2.0.72, app 1.36.0). Curve VERIFIED: his test curve (CPU 25/35/45/55/65/75, GPU
        // 20/30/40/50/60/70) sits byte-for-byte at the shipped 0x72/0x8A.
        // TESTED on his power test, read with the run's own caveat: it is heat-limited (86 C
        // ceiling, 17% end-to-end drift), so Extreme and Balanced converge and cannot be ranked
        // apart. Silent still shows a real cap - it ran first, when the machine was coolest and
        // therefore fastest, yet delivered 88% of Balanced's work at 5 C lower CPU (72% vs 80%
        // CPU load), and every phase read its bytes back intact. No fan RPM in the capture (both
        // tachometer columns blank), so RPM stays off until a dump shows it.
        new() { Name = "MSI Sword 17 HX B14VGKG",           FirmwarePrefixes = new[] { "17T2EMS1" }, Tier = Tier.Tested,
                FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "GalacticPasha", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/139" },
        // Pulse 17 AI C1VGKG / C1VFKG (17T3EMS1) - NEW prefix, absent from msi-ec; added from
        // an owner's per-scenario snapshot (issue #182; name confirmed on msi.com: Ultra 7
        // 155H + RTX 4070/4060, the Pulse 16 AI's 17-inch sibling). Balanced C1, Extreme C4,
        // Super Battery C2 + 0xEB=0F; the "Silent" column showed the Super Battery set (the
        // MSI Center 2.0.71 lineup trap).
        //   Tested via the same owner's power test (issue #195): the run tripped the 99 C
        //   safety fuse during Extreme (thermally tight chassis - the Alpha 17 pattern,
        //   recorded here, protection working as designed), but the data before the cutoff
        //   is decisive: Silent at 82% of Balanced's work on far quieter fans (duty 35 vs
        //   60, 78 vs 92 C) - and Silent ran FIRST, in the coolest phase, so the cut is
        //   drift-resistant even though the aborted run has no baseline repeat. Extreme
        //   measured +8% on the five samples before the cutoff. Switching stable with clean
        //   byte readbacks in all three phases, which also confirms the assumed Silent fan
        //   byte 0x1D on real hardware.
        //   RPM: the run's dumps show both tachs as live single-byte divisors at 0xC9/0xCB
        //   (84/83 then 55/57 = ~3600 rising to ~5600 rpm with load, high bytes always 00;
        //   00/00 with fans parked at idle) - the 17T4EMS1 sibling's scheme. Owner asked to
        //   cross-check against HWiNFO64. Curve stays unverified (no curve capture yet).
        new() { Name = "MSI Pulse 17 AI C1VGKG / C1VFKG",   FirmwarePrefixes = new[] { "17T3EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "SorgZZ", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/195" },
        // Crosshair 17 HX AI D2XW (17T4EMS1) - owner per-scenario dump (issue #148, MSI Center
        // 2.0.48, app 1.36.0, firmware .103) matches StdRecipes 1:1: shift 0xD2 C1/C1/C4/C2,
        // fan 0xD4 1D/0D/0D/0D with a real Silent column, 0xEB=0F only in Super Battery, four
        // distinct columns. All three hardware checks confirmed - the Tested bar. 0x34 reads
        // 01 in every scenario (the GE78 HX 14VHG pattern), so the recipes leave it alone.
        //   RPM: live single-byte divisors at 0xC9/0xCB with the high bytes 0xC8/0xCA at 00
        //   in every column (C2/C3 = ~2460 rpm idle in Silent) - the Katana-family scheme;
        //   owner asked to cross-check against HWiNFO64.
        //   Kbd backlight observation: 0xD3 read 83 in every column but 80 in the vendor's
        //   Super Battery (the 1583 pattern). Not wired up - 17T4EMS1 is absent from msi-ec,
        //   so the register stays observation-only until an owner write-check.
        new() { Name = "MSI Crosshair 17 HX AI D2XW",       FirmwarePrefixes = new[] { "17T4EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                Credit = "SpeedPlayzz", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/148" },
        new() { Name = "MSI Titan 18 HX A14V",              FirmwarePrefixes = new[] { "1822EMS1" }, Tier = Tier.Experimental, FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },
        // Raider A18 HX A7VIG (182KIMS1) — owner per-scenario dump (issue #50) matches StdRecipes on
        // shift 0xD2 C1/C1/C4/C2 and fan 0xD4 1D/0D/0D/0D, and all three hardware checks passed, so
        // Tested. Super battery: upstream msi-ec maps 0xEB for CONF_G2_10, but MSI Center on this AMD
        // board leaves 0xEB=00 even in Super Battery (same as the Crosshair / AMD Bravos), so we drop
        // the write and mirror what MSI Center actually does. RPM: 0xC9/0xCB vary per scenario
        // (C8/C8 → 96/7D ≈ 2400-3800 RPM), the usual G2 layout. Fan curve VERIFIED (issue #55): the
        // owner's wizard capture shows the MSI Center test curve exactly at 0x72 (CPU: 19 23 2D 37 41
        // 4B) and 0x8A (GPU: 14 1E 28 32 3C 46), with 0xD4=8D — the shipped ModernCurve addresses.
        new() { Name = "MSI Raider A18 HX A7VIG", FirmwarePrefixes = new[] { "182KIMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "afk789", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/50" },

        // Vector A18 HX A9WHG (182LIMS1) — owner dump (issue #54) shows the same picture as its Raider
        // sibling: recipe matches, 0xEB stays 00 in Super Battery → dropped. All three hardware
        // checks confirmed by the first owner → promoted to Tested.
        //   Second owner (issues #166/#167/#168, firmware .111, MSI Center 2.0.71): his machine
        //   is a RAIDER A18 HX A9WIG (msi.com line, RTX 5080 config 1:1) on the same MS-182L
        //   board - dual name added. Fan curve VERIFIED (#167): the test curve sits
        //   byte-for-byte at the shipped 0x72/0x8A on both fans. His power test (#168) is the
        //   cleanest run on record for this board: 1% drift, Silent a hard cap at 25% of
        //   Balanced's work (830 vs 3300 MHz), Extreme +30% with fan duty reading 150%, every
        //   phase's bytes read back intact. RPM enabled: both tachs live as single-byte
        //   divisors at 0xC9/0xCB, varying per scenario (9F-C8 = ~2390-3000 rpm) - unlike the
        //   first owner's constant reads; asked to cross-check HWiNFO64. His snapshot columns
        //   were shifted one tile (2.0.71 has no Silent), which the power test supersedes.
        //   0xD6 read 03 under the vendor's Extreme column - the #52 observation, first AMD
        //   board on that list.
        //   Third retail name (issue #172): a Raider A18 HX A9WJG (9955HX3D + RTX 5090, the
        //   line exists on msi.com) runs the same firmware. That owner's capture is also the
        //   first on this board with a REAL Silent column (MSI Center 2.0.48): recipes 1:1,
        //   0xEB=00 in every column re-confirmed, 0xD6=03 under the vendor's Extreme again.
        new() { Name = "MSI Vector A18 HX A9WHG / Raider A18 HX A9WIG / A9WJG", FirmwarePrefixes = new[] { "182LIMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, null),
                Credit = "Skullkidsrevenge, bnjhdaskghsnlh, UzaydaGezen", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/54" },

        // Stealth 18 HX AI A2XW (1833EMS1) - NEW prefix, absent from msi-ec; added from one
        // owner's paired reports (issues #179/#180, an A2XWJG = Ultra 9 275HX + RTX 5090;
        // name confirmed on msi.com, which also lists an A2XWHG line). Per-scenario snapshot:
        // Balanced C1, Extreme C4, Super Battery C2 + 0xEB=0F - and the "Silent" column ALSO
        // reads C2 + 0xEB=0F, i.e. his MSI Center 2.0.73 tile writes the Super Battery set
        // (the lineup trap; the same version showed a real Silent on a Vector 17 HX AI in
        // #171, so the lineup follows the machine, not the version). StdRecipes regardless.
        //   Curve VERIFIED: his test curve sits byte-for-byte at the shipped 0x72/0x8A.
        //   Board quirk from his per-scenario diff: the Advanced curve tables live in the EC
        //   only while the Extreme scenario is active - MSI Center restores the factory
        //   tables in the other scenarios (observation only; our curve writes are direct).
        //   RPM: joins the wide-tach carriers - 16-bit pairs 0xC8:0xC9 / 0xCA:0xCB, both
        //   proven at idle (01:12 = 274 = ~1745 rpm, high byte non-zero).
        // Tier stays Experimental until a power test or the three hardware checks.
        new() { Name = "MSI Stealth 18 HX AI A2XW",         FirmwarePrefixes = new[] { "1833EMS1" }, Tier = Tier.Experimental,
                CpuRpmAddr16 = 0xC8, GpuRpmAddr16 = 0xCA,
                FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB) },

        // Crosshair 18 HX AI A2XW (1841EMS1) - NEW prefix, absent from msi-ec; added from an
        // owner's per-scenario dump set (issue #183; name confirmed on msi.com, Ultra 9
        // 275HX). MSI Center 2.0.48 with a REAL Silent column: shift 0xD2 C1/C1/C4/C2, fan
        // 0xD4 1D/0D/0D/0D, four distinct columns - and 0xEB reads 00 in every column
        // including Super Battery, so the eco recipe is the mode byte alone (the Alpha 17 /
        // A18 pattern, here on an Intel board). 0x34 reads 01 everywhere - left alone.
        //   RPM: joins the wide-tach carriers - 16-bit pairs 0xC8:0xC9 / 0xCA:0xCB. The GPU
        //   pair is proven at idle (01:00 and 01:01 = raw 256-257 = ~1860 rpm, high byte
        //   non-zero); the CPU pair read 00:FB there (the formats coincide above ~1870 rpm)
        //   and follows the board's format.
        // Tier stays Experimental: the hardware-check boxes were not ticked - a power test
        // or the three checks settle promotion.
        new() { Name = "MSI Crosshair 18 HX AI A2XW",       FirmwarePrefixes = new[] { "1841EMS1" }, Tier = Tier.Experimental,
                CpuRpmAddr16 = 0xC8, GpuRpmAddr16 = 0xCA,
                FanCurve = ModernCurve, Recipes = StdRecipes(0xD2, 0xD4, null) },

        // Stealth 16 AI+ B3WI (2631EMS1) - the first board in this table with a documented FOURTH
        // shift-mode value. It is NOT in msi-ec's conf table, so every address below comes from the
        // owner's own captures (issues #66 / #67), not from the driver.
        //   0xD2 across the four captures: C1, C4, C2 (with 0xEB=0F beside it) and C5. The first three
        //   are the comfort / turbo / eco values StdRecipes already writes, so the standard G2 recipe
        //   applies unchanged. C5 is the extra value, recorded in FourthMode.
        //   Fan curve VERIFIED: the wizard found the MSI Center test curve byte-for-byte at 0x72 (CPU:
        //   19 23 2D 37 41 4B) and 0x8A (GPU: 14 1E 28 32 3C 46) - the shipped ModernCurve addresses.
        //   RPM: 0xC9/0xCB read C6 / E2 (~2400 / ~2100 RPM at 478000/raw) in the capture where the fans
        //   were spinning, and 00 / 00 in the idle captures - live tachs at the family addresses.
        //   Tested via a second owner (issues #185-#189, a Stealth 16 AI+ B3WH - name confirmed
        //   on msi.com, hence the second name): his snapshot shows the real Silent fan byte
        //   0x1D (MSI Center 2.0.48), and his clean power test (#189, 0% drift) measures
        //   Silent doing exactly Balanced's work on clearly slower fans (3376 vs 4401 rpm)
        //   with clean byte readbacks across all five phases. Extreme = Balanced's CPU work
        //   in both his runs - recorded, not held against promotion; both ran with the
        //   graphics load on, and a combined load appears to share one power budget
        //   (unverified). First Apex (C5) measurement on this board: accepted, correctly
        //   reverted, performs like Extreme - the Extreme recipe stays at the standard turbo
        //   value, the Vector 16 HX AI verdict. His curve capture re-confirms both tables
        //   (GPU slider 4 stored as 49 for a requested 50 - a snap, not a mismatch).
        new() { Name = "MSI Stealth 16 AI+ B3WI / B3WH", FirmwarePrefixes = new[] { "2631EMS1" }, Tier = Tier.Tested,
                CpuRpmAddr = 0xC9, GpuRpmAddr = 0xCB, FanCurve = ModernCurveVerified, Recipes = StdRecipes(0xD2, 0xD4, 0xEB),
                FourthMode = new FourthModeSpec("Apex", 0xC5),
                Credit = "SteppinStone, AiM-lab-owl", CreditUrl = "https://github.com/wygodad/ghostdeck/issues/66" },

        // G1 family (shift 0xF2 / fan 0xF4 / charge 0xEF) — older boards; super-batt addr unknown (null) unless noted.
        new() { Name = "MSI Prestige 14 A10SC", FirmwarePrefixes = new[] { "14C1EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Modern 14 B10MW", FirmwarePrefixes = new[] { "14D1EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Modern 14 B4MW", FirmwarePrefixes = new[] { "14DKEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Modern 14 B5M", FirmwarePrefixes = new[] { "14DLEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Modern 14 C5M", FirmwarePrefixes = new[] { "14JKEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GE66 Raider 10SF", FirmwarePrefixes = new[] { "1541EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GP66 Leopard 10UG / 10UE / 10UH", FirmwarePrefixes = new[] { "1542EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Modern 15 A10M", FirmwarePrefixes = new[] { "1551EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Modern 15 A5M", FirmwarePrefixes = new[] { "155LEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Bravo 15 B5DD", FirmwarePrefixes = new[] { "158KEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Alpha 15 B5EE / B5EEK", FirmwarePrefixes = new[] { "158LEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Bravo 15 B5ED", FirmwarePrefixes = new[] { "158MEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Delta 15 A5EFK", FirmwarePrefixes = new[] { "15CKEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Modern 15 B7M", FirmwarePrefixes = new[] { "15HKEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GS65 Stealth Thin 8RE / 8RF", FirmwarePrefixes = new[] { "16Q2EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI P65 Creator 8RE", FirmwarePrefixes = new[] { "16Q3EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF63 8RC-249", FirmwarePrefixes = new[] { "16R1EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF63 Thin 9SC", FirmwarePrefixes = new[] { "16R3EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF63 Thin 10SCX / 10SCS", FirmwarePrefixes = new[] { "16R4EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF63 Thin 9SCSR", FirmwarePrefixes = new[] { "16R4EMS2" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        // 10UC added on an owner's report (issue #86, GF63 Thin 10UC on 16R5EMS1): the machine
        // ships with Dragon Center (no MSI Center, so no per-scenario capture is possible), yet
        // two of the three hardware checks passed - Silent audibly quiets the machine and
        // profile switching is stable with the app state matching. That makes it the only
        // behavioural confirmation of the whole G1 register set so far. Tier stays Experimental
        // and no credit yet: promotion was publicly tied to a power-test run that has not
        // arrived, and we hold no dump from this generation at all.
        new() { Name = "MSI GF63 Thin 10U / 10SC / 10UC", FirmwarePrefixes = new[] { "16R5EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI PS63 MODERN 8RD", FirmwarePrefixes = new[] { "16S1EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Prestige 15 A10SC", FirmwarePrefixes = new[] { "16S3EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Creator 15 A10SD", FirmwarePrefixes = new[] { "16V2EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, 0xD5) },
        new() { Name = "MSI GS66 Stealth 10UE", FirmwarePrefixes = new[] { "16V3EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF65 Thin 9SE / 9SD", FirmwarePrefixes = new[] { "16W1EMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI GF65 Thin 10SCSXR / 10SD / 10SE", FirmwarePrefixes = new[] { "16W1EMS2" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Bravo 15 A4DDR", FirmwarePrefixes = new[] { "16WKEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Bravo 17 A4DDR / A4DDK", FirmwarePrefixes = new[] { "17FKEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
        new() { Name = "MSI Alpha 17 B5EEK", FirmwarePrefixes = new[] { "17LLEMS1" }, Tier = Tier.Experimental,
                ShiftMode = 0xF2, FanMode = 0xF4, ChargeCtrl = 0xEF, Recipes = StdRecipes(0xF2, 0xF4, null) },
    };

    public static DeviceProfile? Detect(string firmware) =>
        All.FirstOrDefault(d => d.Matches(firmware));
}
