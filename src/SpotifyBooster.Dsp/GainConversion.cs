namespace SpotifyBooster.Dsp;

public static class GainConversion
{
    public const float MinimumPercent = 100f;
    public const float MaximumPercent = 500f;

    public static float PercentToFactor(float percent)
    {
        if (!float.IsFinite(percent))
        {
            return 1f;
        }

        return Math.Clamp(percent, MinimumPercent, MaximumPercent) / 100f;
    }

    public static float FactorToDecibels(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0f)
        {
            return float.NegativeInfinity;
        }

        return 20f * MathF.Log10(factor);
    }

    public static float PercentToDecibels(float percent) =>
        FactorToDecibels(PercentToFactor(percent));

    public static float DecibelsToFactor(float decibels)
    {
        if (float.IsNegativeInfinity(decibels))
        {
            return 0f;
        }

        return float.IsFinite(decibels) ? MathF.Pow(10f, decibels / 20f) : 1f;
    }
}
