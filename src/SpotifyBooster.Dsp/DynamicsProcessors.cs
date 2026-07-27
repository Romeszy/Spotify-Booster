namespace SpotifyBooster.Dsp;

public sealed class StereoCompressor
{
    private float _envelope;
    private float _attackCoefficient;
    private float _releaseCoefficient;
    private float _thresholdDecibels;
    private float _ratio;
    private float _makeupGain;

    public StereoCompressor(
        int sampleRate,
        bool enabled = false,
        float thresholdDecibels = -12f,
        float ratio = 3f,
        float attackMilliseconds = 10f,
        float releaseMilliseconds = 100f,
        float makeupGainDecibels = 0f)
    {
        Enabled = enabled;
        Configure(sampleRate, thresholdDecibels, ratio, attackMilliseconds, releaseMilliseconds, makeupGainDecibels);
    }

    public bool Enabled { get; set; }
    public float GainReductionDecibels { get; private set; }

    public void Configure(
        int sampleRate,
        float thresholdDecibels,
        float ratio,
        float attackMilliseconds,
        float releaseMilliseconds,
        float makeupGainDecibels = 0f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _thresholdDecibels = Math.Clamp(thresholdDecibels, -60f, 0f);
        _ratio = Math.Clamp(ratio, 1f, 20f);
        _attackCoefficient = TimeCoefficient(sampleRate, Math.Max(0.01f, attackMilliseconds));
        _releaseCoefficient = TimeCoefficient(sampleRate, Math.Max(0.01f, releaseMilliseconds));
        _makeupGain = GainConversion.DecibelsToFactor(Math.Clamp(makeupGainDecibels, -12f, 24f));
    }

    public void Process(ref float left, ref float right)
    {
        if (!Enabled)
        {
            GainReductionDecibels = 0f;
            return;
        }

        float detector = Math.Max(Math.Abs(left), Math.Abs(right));
        float coefficient = detector > _envelope ? _attackCoefficient : _releaseCoefficient;
        _envelope = detector + (coefficient * (_envelope - detector));

        float inputDecibels = 20f * MathF.Log10(Math.Max(_envelope, 1e-9f));
        float reduction = inputDecibels > _thresholdDecibels
            ? (inputDecibels - _thresholdDecibels) * (1f - (1f / _ratio))
            : 0f;
        GainReductionDecibels = reduction;
        float gain = GainConversion.DecibelsToFactor(-reduction) * _makeupGain;
        left *= gain;
        right *= gain;
    }

    public void Reset()
    {
        _envelope = 0f;
        GainReductionDecibels = 0f;
    }

    private static float TimeCoefficient(int sampleRate, float milliseconds) =>
        MathF.Exp(-1f / (sampleRate * milliseconds * 0.001f));
}

public sealed class StereoLimiter
{
    public const float DefaultCeilingDecibels = -1f;

    private float _gain = 1f;
    private float _releaseCoefficient;
    private float _ceiling;

    public StereoLimiter(
        int sampleRate,
        bool enabled = true,
        float ceilingDecibels = DefaultCeilingDecibels,
        float releaseMilliseconds = 50f)
    {
        Enabled = enabled;
        Configure(sampleRate, ceilingDecibels, releaseMilliseconds);
    }

    public bool Enabled { get; set; }
    public float Ceiling => _ceiling;
    public float GainReductionDecibels { get; private set; }

    public void Configure(int sampleRate, float ceilingDecibels = DefaultCeilingDecibels, float releaseMilliseconds = 50f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ceilingDecibels = Math.Clamp(ceilingDecibels, -12f, -0.01f);
        _ceiling = GainConversion.DecibelsToFactor(ceilingDecibels);
        _releaseCoefficient = MathF.Exp(-1f / (sampleRate * Math.Max(0.01f, releaseMilliseconds) * 0.001f));
    }

    public bool Process(ref float left, ref float right)
    {
        if (!Enabled)
        {
            GainReductionDecibels = 0f;
            return false;
        }

        float peak = Math.Max(Math.Abs(left), Math.Abs(right));
        float requiredGain = peak > _ceiling ? _ceiling / peak : 1f;
        _gain = requiredGain < _gain
            ? requiredGain
            : 1f + (_releaseCoefficient * (_gain - 1f));
        GainReductionDecibels = -GainConversion.FactorToDecibels(Math.Max(_gain, 1e-9f));

        left = Math.Clamp(left * _gain, -_ceiling, _ceiling);
        right = Math.Clamp(right * _gain, -_ceiling, _ceiling);
        return requiredGain < 1f;
    }

    public void Reset()
    {
        _gain = 1f;
        GainReductionDecibels = 0f;
    }
}

public sealed class StereoSoftClipper
{
    private float threshold;
    private float drive;
    private float driveNormalization;

    public StereoSoftClipper(
        bool enabled = true,
        float thresholdDecibels = -1f,
        float drive = 2f)
    {
        Enabled = enabled;
        Configure(thresholdDecibels, drive);
    }

    public bool Enabled { get; set; }

    public void Configure(float thresholdDecibels, float drive)
    {
        this.threshold = GainConversion.DecibelsToFactor(
            Math.Clamp(thresholdDecibels, -18f, -0.01f));
        this.drive = Math.Clamp(drive, 0.5f, 8f);
        driveNormalization = MathF.Tanh(this.drive);
    }

    public int Process(ref float left, ref float right)
    {
        if (!Enabled)
        {
            return 0;
        }

        int affected = 0;
        left = ProcessSample(left, ref affected);
        right = ProcessSample(right, ref affected);
        return affected;
    }

    private float ProcessSample(float sample, ref int affected)
    {
        float magnitude = Math.Abs(sample);
        if (magnitude <= threshold)
        {
            return sample;
        }

        affected++;
        float kneePosition = Math.Clamp(
            (magnitude - threshold) / (1f - threshold),
            0f,
            1f);
        float shaped = MathF.Tanh(kneePosition * drive) / driveNormalization;
        float result = threshold + ((1f - threshold) * shaped);
        return MathF.CopySign(Math.Min(1f, result), sample);
    }
}
