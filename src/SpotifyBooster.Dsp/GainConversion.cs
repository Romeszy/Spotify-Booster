namespace SpotifyBooster.Dsp;

public static class GainConversion
{
    public static double FactorToDecibelsDouble(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0d)
        {
            return double.NegativeInfinity;
        }

        return 20d * Math.Log10(factor);
    }

    public static float FactorToDecibels(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0f)
        {
            return float.NegativeInfinity;
        }

        return 20f * MathF.Log10(factor);
    }

    public static double DecibelsToFactorDouble(double decibels)
    {
        if (double.IsNegativeInfinity(decibels))
        {
            return 0d;
        }

        if (!double.IsFinite(decibels))
        {
            return 1d;
        }

        double factor = Math.Pow(10d, decibels / 20d);
        return double.IsFinite(factor) ? factor : double.MaxValue;
    }

    public static float DecibelsToFactor(float decibels)
    {
        if (float.IsNegativeInfinity(decibels))
        {
            return 0f;
        }

        if (!float.IsFinite(decibels))
        {
            return 1f;
        }

        float factor = MathF.Pow(10f, decibels / 20f);
        return float.IsFinite(factor) ? factor : float.MaxValue;
    }

    public static float DecibelsToPercent(float decibels)
    {
        float factor = DecibelsToFactor(decibels);
        return factor <= float.MaxValue / 100f
            ? factor * 100f
            : float.MaxValue;
    }

    public static double DecibelsToPercentDouble(double decibels)
    {
        double factor = DecibelsToFactorDouble(decibels);
        return factor <= double.MaxValue / 100d
            ? factor * 100d
            : double.MaxValue;
    }

    public static float PercentToDecibels(float percent)
    {
        if (!float.IsFinite(percent) || percent <= 0f)
        {
            return float.NegativeInfinity;
        }

        return FactorToDecibels(percent / 100f);
    }
}
