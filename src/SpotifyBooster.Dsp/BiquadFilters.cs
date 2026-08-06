namespace SpotifyBooster.Dsp;

public abstract class StereoBiquad
{
    private float _b0 = 1f;
    private float _b1;
    private float _b2;
    private float _a1;
    private float _a2;
    private float _x1Left;
    private float _x2Left;
    private float _y1Left;
    private float _y2Left;
    private float _x1Right;
    private float _x2Right;
    private float _y1Right;
    private float _y2Right;

    protected void SetCoefficients(float b0, float b1, float b2, float a0, float a1, float a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public void Process(ref float left, ref float right)
    {
        float outputLeft = (_b0 * left) + (_b1 * _x1Left) + (_b2 * _x2Left)
            - (_a1 * _y1Left) - (_a2 * _y2Left);
        _x2Left = _x1Left;
        _x1Left = left;
        _y2Left = _y1Left;
        _y1Left = outputLeft;

        float outputRight = (_b0 * right) + (_b1 * _x1Right) + (_b2 * _x2Right)
            - (_a1 * _y1Right) - (_a2 * _y2Right);
        _x2Right = _x1Right;
        _x1Right = right;
        _y2Right = _y1Right;
        _y1Right = outputRight;

        left = outputLeft;
        right = outputRight;
    }

    public void Reset()
    {
        ResetLeft();
        ResetRight();
    }

    public void ResetLeft()
    {
        _x1Left = _x2Left = _y1Left = _y2Left = 0f;
    }

    public void ResetRight()
    {
        _x1Right = _x2Right = _y1Right = _y2Right = 0f;
    }
}

public sealed class LowShelfFilter : StereoBiquad
{
    public LowShelfFilter(
        int sampleRate,
        float frequencyHz = 100f,
        float gainDecibels = 0f,
        float slope = 1f)
    {
        Configure(sampleRate, frequencyHz, gainDecibels, slope);
    }

    public void Configure(
        int sampleRate,
        float frequencyHz,
        float gainDecibels,
        float slope = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        frequencyHz = Math.Clamp(frequencyHz, 35f, Math.Min(200f, sampleRate * 0.45f));
        gainDecibels = Math.Clamp(gainDecibels, 0f, 18f);
        slope = Math.Clamp(slope, 0.25f, 2f);

        float a = MathF.Pow(10f, gainDecibels / 40f);
        float omega = 2f * MathF.PI * frequencyHz / sampleRate;
        float cosine = MathF.Cos(omega);
        float sine = MathF.Sin(omega);
        float alpha = (sine / 2f) * MathF.Sqrt(
            ((a + (1f / a)) * ((1f / slope) - 1f)) + 2f);
        float twoSqrtAAlpha = 2f * MathF.Sqrt(a) * alpha;

        SetCoefficients(
            a * ((a + 1f) - ((a - 1f) * cosine) + twoSqrtAAlpha),
            2f * a * ((a - 1f) - ((a + 1f) * cosine)),
            a * ((a + 1f) - ((a - 1f) * cosine) - twoSqrtAAlpha),
            (a + 1f) + ((a - 1f) * cosine) + twoSqrtAAlpha,
            -2f * ((a - 1f) + ((a + 1f) * cosine)),
            (a + 1f) + ((a - 1f) * cosine) - twoSqrtAAlpha);
    }
}

public sealed class PeakingEqualizer : StereoBiquad
{
    public PeakingEqualizer(
        int sampleRate,
        float frequencyHz = 1_000f,
        float gainDecibels = 0f,
        float q = 0.707f)
    {
        Configure(sampleRate, frequencyHz, gainDecibels, q);
    }

    public bool Enabled { get; set; }

    public void Configure(int sampleRate, float frequencyHz, float gainDecibels, float q = 0.707f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        frequencyHz = Math.Clamp(frequencyHz, 20f, sampleRate * 0.45f);
        gainDecibels = Math.Clamp(gainDecibels, -12f, 12f);
        q = Math.Clamp(q, 0.1f, 10f);

        float a = MathF.Pow(10f, gainDecibels / 40f);
        float omega = 2f * MathF.PI * frequencyHz / sampleRate;
        float alpha = MathF.Sin(omega) / (2f * q);
        float cosine = MathF.Cos(omega);
        SetCoefficients(
            1f + (alpha * a),
            -2f * cosine,
            1f - (alpha * a),
            1f + (alpha / a),
            -2f * cosine,
            1f - (alpha / a));
    }

    public new void Process(ref float left, ref float right)
    {
        if (Enabled)
        {
            base.Process(ref left, ref right);
        }
    }
}
