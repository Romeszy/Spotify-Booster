namespace SpotifyBooster.Dsp;

public sealed class GainSmoother
{
    private readonly int sampleRate;
    private float smoothingMilliseconds;
    private float _current;
    private float _target;
    private float step;
    private int remainingSamples;

    public GainSmoother(int sampleRate, float smoothingMilliseconds = 5f, float initialGain = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        this.sampleRate = sampleRate;
        _current = SanitizeGain(initialGain);
        _target = _current;
        Configure(smoothingMilliseconds);
    }

    public float Current => _current;

    public float Target
    {
        get => _target;
        set => BeginRamp(SanitizeGain(value));
    }

    public void Configure(float smoothingMilliseconds)
    {
        this.smoothingMilliseconds =
            float.IsFinite(smoothingMilliseconds) && smoothingMilliseconds > 0f
                ? smoothingMilliseconds
                : 0f;
    }

    public void SetTarget(float gain, float smoothingMilliseconds)
    {
        Configure(smoothingMilliseconds);
        Target = gain;
    }

    public float Next()
    {
        if (remainingSamples <= 0)
        {
            return _current;
        }

        _current += step;
        remainingSamples--;
        if (remainingSamples == 0)
        {
            _current = _target;
            step = 0f;
        }

        return _current;
    }

    public void Reset(float gain = 1f)
    {
        _current = SanitizeGain(gain);
        _target = _current;
        step = 0f;
        remainingSamples = 0;
    }

    private void BeginRamp(float gain)
    {
        _target = gain;
        double exactSampleCount = sampleRate * (double)smoothingMilliseconds * 0.001d;
        if (exactSampleCount < 1d)
        {
            _current = _target;
            step = 0f;
            remainingSamples = 0;
            return;
        }

        remainingSamples = (int)Math.Min(int.MaxValue, Math.Ceiling(exactSampleCount));
        step = (_target - _current) / remainingSamples;
    }

    private static float SanitizeGain(float gain) =>
        float.IsFinite(gain) ? Math.Max(0f, gain) : 1f;
}
