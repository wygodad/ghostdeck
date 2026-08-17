using System.Text;

namespace GhostDeck;

/// <summary>
/// Generates docs/SUPPORTED_MODELS.md from the compiled model tables (hidden CLI
/// --dump-supported-md, CI regenerates and compares - see ci.yml). The page uses the
/// Models tab's ordering (tested first, then experimental G2, then G1, alphabetical
/// within each group) and derives every cell from <see cref="DeviceProfile"/>, so the
/// human-readable table cannot drift from the code. Output is byte-exact: UTF-8 without
/// BOM, LF line endings, one trailing blank line.
/// </summary>
internal static class SupportedModelsDoc
{
    internal static byte[] Generate()
    {
        // Devices.BuiltIn, not Devices.All: the doc must describe the checked-out code,
        // never a signed database override downloaded on the machine that runs the dump.
        var models = Devices.BuiltIn
            .OrderBy(m => m.Tier == Tier.Tested ? 0 : (m.ShiftMode == 0xF2 ? 2 : 1))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tested = models.Where(m => m.Tier == Tier.Tested).ToArray();

        var sb = new StringBuilder(24 * 1024);
        void L(string line = "") { sb.Append(line); sb.Append('\n'); }

        L("# Supported models");
        L();
        L("> Auto-generated from [`Devices.cs`](../Core/Devices.cs) - the single source of truth. Do not edit by hand: regenerate with `GhostDeck.exe --dump-supported-md docs/SUPPORTED_MODELS.md` (CI fails when this file drifts from the code).");
        L();
        L($"**{models.Length} laptop models** are recognised: **{tested.Length} tested** on real hardware ({string.Join("; ", tested.Select(m => m.Name))}) and **{models.Length - tested.Length} experimental** (opt-in), built from the [msi-ec](https://github.com/BeardOverflow/msi-ec) register maps, with fan and temperature registers cross-checked against [MControlCenter](https://github.com/dmitry-s93/MControlCenter). Keyboard-backlight control covers models where msi-ec documents the EC brightness register; laptops with **per-key RGB keyboards** (SteelSeries) do not expose it and keep using their own Fn key (see [LIGHTING.md](LIGHTING.md) for the hardware research behind that). On an **unrecognised firmware the app stays read-only** (Status works, no writes), so it never touches wrong registers.");
        L();
        L("Column meaning:");
        L();
        L("- **Family** - EC register layout. **G2** = shift `0xD2` / fan `0xD4` / super-batt `0xEB` / charge `0xD7` (same as the tested board). **G1** = shift `0xF2` / fan `0xF4` / charge `0xEF`, older boards.");
        L("- **Status** - &#9989; tested = verified on hardware; &#9887;&#65039; experimental = documented registers, not yet confirmed by an owner (the low-power \"Silent\" behaviour in particular).");
        L("- **Fan curve** - &#9989; editable = the curve tab writes the curve; &#9989; verified (opt-in) = the curve itself is owner-verified while the model still awaits its profile checks, so editing needs the Experimental opt-in; \"(single fan)\" = iGPU model, only the CPU table applies; &#9673; unverified = editable once Experimental is enabled, but the table addresses (CPU `0x6A`/`0x72`, GPU `0x82`/`0x8A`, shared across the G2 family by MControlCenter) are not yet confirmed on that exact model - compare with MSI Center first; &mdash; = no curve support (profiles only).");
        L("- **Super Battery** - whether the model exposes a super-battery throttle register.");
        L("- **RPM** - whether the fan-tachometer registers are known (so real fan RPM is shown), with their addresses. Verified only where hardware/dumps confirmed them.");
        L();
        L("Own an experimental model and can confirm it works (or doesn't)? Use the in-app **Report my model...** wizard (tray menu / Status window) or open a [Model support request](../../../issues/new?template=model-support.yml). The **Power test** sub-tab beside it measures the profiles instead of asking you to judge them by ear, and needs no MSI Center; see [TECHNICAL.md](TECHNICAL.md) §60.");
        L();
        L("| Model | EC firmware | Family | Status | Fan curve | Super Battery | RPM |");
        L("|---|---|---|---|---|---|---|");
        foreach (var m in models)
        {
            bool isTested = m.Tier == Tier.Tested;
            string fw = string.Join(", ", m.FirmwarePrefixes.Select(p => "`" + p + "`"));
            string family = m.ShiftMode == 0xF2 ? "G1" : "G2";
            string status = isTested ? "&#9989; tested" : "&#9887;&#65039; experimental";

            string curve;
            if (m.FanCurve is { } fc)
            {
                curve = !fc.Verified ? "&#9673; unverified"
                    : isTested ? "&#9989; editable" : "&#9989; verified (opt-in)";
                if (fc.SingleFan) curve += " (single fan)";
            }
            else curve = "&mdash;";

            bool sbat = m.Recipes.TryGetValue(ProfileId.SuperBattery, out var sr) && sr.Any(x => x.val == 0x0F);
            string sbStr = sbat ? "&#10003;" : "&mdash;";
            string rpm = m.CpuRpmAddr == 0 ? "&mdash;"
                : m.GpuRpmAddr == 0 ? $"&#10003; 0x{m.CpuRpmAddr:X2}"
                : $"&#10003; 0x{m.CpuRpmAddr:X2}/0x{m.GpuRpmAddr:X2}";

            L($"| {m.Name} | {fw} | {family} | {status} | {curve} | {sbStr} | {rpm} |");
        }
        L();
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
