namespace SpotifyBooster.Dsp;

internal sealed class DecibelSmoother
{
    private readonly int sampleRate;
    private float smoothingMilliseconds;
    private double current;
    private double currentFactor;
    private double factorMultiplier = 1d;
    private double stepDecibels;
    private double target;
    private int remainingSamples;

    public DecibelSmoother(
        int sampleRate,
        float smoothingMilliseconds = 5f,
        double initialDecibels = 0d)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        this.sampleRate = sampleRate;
        current = Sanitize(initialDecibels);
        currentFactor = GainConversion.DecibelsToFactorDouble(current);
        target = current;
        Configure(smoothingMilliseconds);
    }

    public double Current => current;

    public double Target
    {
        get => target;
        set => BeginRamp(Sanitize(value));
    }

    public void SetTarget(double decibels, float smoothingMilliseconds)
    {
        Configure(smoothingMilliseconds);
        Target = decibels;
    }

    public double Next()
    {
        _ = NextFactor();
        return current;
    }

    public double NextFactor()
    {
        if (remainingSamples <= 0)
        {
            return currentFactor;
        }

        current += stepDecibels;
        currentFactor *= factorMultiplier;
        remainingSamples--;
        if (remainingSamples == 0)
        {
            current = target;
            currentFactor = GainConversion.DecibelsToFactorDouble(target);
            factorMultiplier = 1d;
            stepDecibels = 0d;
        }

        return currentFactor;
    }

    public void Reset(double decibels = 0d)
    {
        current = Sanitize(decibels);
        target = current;
        currentFactor = GainConversion.DecibelsToFactorDouble(current);
        factorMultiplier = 1d;
        stepDecibels = 0d;
        remainingSamples = 0;
    }

    private void Configure(float smoothingMilliseconds)
    {
        this.smoothingMilliseconds =
            float.IsFinite(smoothingMilliseconds) && smoothingMilliseconds > 0f
                ? smoothingMilliseconds
                : 0f;
    }

    private void BeginRamp(double decibels)
    {
        target = decibels;
        double exactSampleCount =
            sampleRate * (double)smoothingMilliseconds * 0.001d;
        if (exactSampleCount < 1d)
        {
            Reset(target);
            return;
        }

        remainingSamples = (int)Math.Min(
            int.MaxValue,
            Math.Ceiling(exactSampleCount));
        stepDecibels = (target - current) / remainingSamples;
        double targetFactor = GainConversion.DecibelsToFactorDouble(target);
        factorMultiplier = Math.Pow(
            targetFactor / currentFactor,
            1d / remainingSamples);
    }

    private static double Sanitize(double decibels) =>
        double.IsFinite(decibels) ? decibels : 0d;
}
