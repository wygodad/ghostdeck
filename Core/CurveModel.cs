namespace GhostDeck;

/// <summary>
/// Pure arithmetic over a fan curve (temperature nodes + speed nodes, 0-100 %). Shared by
/// every Fan-curve view: the chart maps a live temperature onto its ordinal x axis, the
/// playground asks "what would the fans do at 78 °C", the intent tiles derive shapes from
/// the factory default, the crossfader blends two shapes. No EC, no UI, no state.
///
/// Interpolation between nodes is LINEAR. That is a model of the firmware, not a measurement:
/// nothing in the register maps documents how the EC interpolates or what hysteresis it
/// applies, so every consumer labels these results as expected/model values, never as
/// readings (TECHNICAL 17.3).
/// </summary>
public static class CurveModel
{
    /// <summary>
    /// Ordinal x position (0 .. n-1, fractional) of a temperature on a chart whose nodes are
    /// spaced evenly by index. Clamped to the ends. Nodes with equal temperatures (the 0 °C
    /// anchor followed by e.g. 50 °C) are handled by taking the first segment that brackets t.
    /// </summary>
    public static float OrdinalX(int[] temps, float t)
    {
        int n = temps.Length;
        if (n == 0) return 0f;
        if (n == 1 || t <= temps[0]) return 0f;
        if (t >= temps[n - 1]) return n - 1;
        for (int i = 0; i < n - 1; i++)
        {
            int a = temps[i], b = temps[i + 1];
            if (t >= a && t <= b)
                return b == a ? i : i + (t - a) / (float)(b - a);
        }
        return n - 1;
    }

    /// <summary>Expected fan speed (%) at temperature t under linear interpolation.</summary>
    public static float SpeedAt(int[] temps, int[] speeds, float t)
    {
        int n = Math.Min(temps.Length, speeds.Length);
        if (n == 0) return 0f;
        if (n == 1 || t <= temps[0]) return speeds[0];
        if (t >= temps[n - 1]) return speeds[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            int a = temps[i], b = temps[i + 1];
            if (t >= a && t <= b)
            {
                if (b == a) return speeds[i + 1];
                float f = (t - a) / (float)(b - a);
                return speeds[i] + (speeds[i + 1] - speeds[i]) * f;
            }
        }
        return speeds[n - 1];
    }

    /// <summary>
    /// Enforce the editor's invariant on a whole array: 0-100 and non-decreasing left to
    /// right. Later nodes are raised to the running maximum (never lowered), so a shape
    /// produced by a shift or a blend cannot dip. Returns a NEW array.
    /// </summary>
    public static int[] Monotone(int[] speeds)
    {
        var r = new int[speeds.Length];
        int run = 0;
        for (int i = 0; i < speeds.Length; i++)
        {
            int v = Math.Clamp(speeds[i], 0, 100);
            if (v < run) v = run;
            run = v;
            r[i] = v;
        }
        return r;
    }

    /// <summary>Every node shifted by <paramref name="delta"/> percentage points, then clamped and made monotone.</summary>
    public static int[] Shift(int[] speeds, int delta)
    {
        var r = new int[speeds.Length];
        for (int i = 0; i < speeds.Length; i++) r[i] = speeds[i] + delta;
        return Monotone(r);
    }

    /// <summary>
    /// Blend two shapes node by node: 0 = all <paramref name="a"/>, 100 = all <paramref name="b"/>.
    /// The crossfader on the Deck view. Arrays of unequal length blend over the shorter one.
    /// </summary>
    public static int[] Blend(int[] a, int[] b, int mix)
    {
        int n = Math.Min(a.Length, b.Length);
        float f = Math.Clamp(mix, 0, 100) / 100f;
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = (int)Math.Round(a[i] + (b[i] - a[i]) * f);
        return Monotone(r);
    }

    /// <summary>True when both arrays hold the same values (same length required).</summary>
    public static bool SameShape(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---- audibility bands (view 07): fixed thresholds, described in the UI as indicative ----
    // Below Quiet = barely audible, Quiet..Loud = audible, above Loud = loud. Per-model
    // calibration (rpm-based) is a later step that needs the tachometer sweep first.
    public const int QuietMax = 30;
    public const int LoudMin = 60;

    /// <summary>0 = quiet, 1 = audible, 2 = loud.</summary>
    public static int Band(int speedPct) => speedPct < QuietMax ? 0 : speedPct < LoudMin ? 1 : 2;

    // ---- intent shapes (view 08): derived from the factory default so they stay in the
    // family's own vocabulary; nothing is invented per model. ----
    public enum Intent { Quiet, Balanced, Cool, Max }

    public static int[] IntentShape(Intent intent, int[] factoryDefault, int[] temps)
    {
        switch (intent)
        {
            case Intent.Quiet:
                return Shift(factoryDefault, -12);
            case Intent.Cool:
                return Shift(factoryDefault, +10);
            case Intent.Max:
            {
                // fast ramp: default up to 55 °C, then straight to 100 by ~70 °C
                var r = new int[factoryDefault.Length];
                for (int i = 0; i < r.Length; i++)
                {
                    int t = i < temps.Length ? temps[i] : 100;
                    r[i] = t <= 55 ? factoryDefault[i]
                         : t >= 70 ? 100
                         : (int)Math.Round(factoryDefault[i] + (100 - factoryDefault[i]) * (t - 55) / 15f);
                }
                return Monotone(r);
            }
            default:
                return (int[])factoryDefault.Clone();
        }
    }
}
