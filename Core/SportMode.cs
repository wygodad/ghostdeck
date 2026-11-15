namespace GhostDeck;

/// <summary>
/// TEST BUILD for issue #48 (Delta 15 A5EFK, EC line 15CKEMS1, backup WMI path only).
///
/// The owner wants Balanced with faster fans. His path tests measured: in the comfort shift
/// mode (0xC1, which Balanced writes) the controller reports the fan duty the curve asks for
/// but holds the fan at about 4200 rpm; in 0xC0 ("sport" on other boards of this generation,
/// msi-ec) and in turbo (0xC4, Extreme) the same duty gives about 4750 rpm. What 0xC0 does to
/// the power limits over minutes is what the long comparison in this build's path test
/// measures. Until that is known the switch is an experiment on one machine: off by default,
/// behind a consent card, a panic reset switches it off and writes plain Balanced.
///
/// With the switch on, the Balanced recipe writes 0xC0 instead of 0xC1 to the shift-mode slot
/// (TrayContext.EffectiveRecipe, the Apex pattern), detection reports 0xC0 as Balanced
/// (Ec.GetCurrent), and the backup path's allow-list accepts the value (Ec.BuildBackup).
/// Kept out of the model database on purpose: nothing of it reaches other machines.
/// </summary>
public static class SportMode
{
    public const byte Shift = 0xC0;

    /// <summary>True on the one board this test build's switch is meant for.</summary>
    public static bool AvailableFor(DeviceProfile? dev) =>
        dev != null && dev.FirmwarePrefixes.Any(p => p.Equals("15CKEMS1", StringComparison.OrdinalIgnoreCase));
}
