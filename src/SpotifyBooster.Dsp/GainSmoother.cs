namespace SpotifyBooster.Dsp;

public sealed class GainSmoother
{
    private readonly int sampleRate;
    private float coefficient;
    private float _current;
    private float _target;

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
        set => _target = SanitizeGain(value);
    }

    public void Configure(float smoothingMilliseconds)
    {
        coefficient = !float.IsFinite(smoothingMilliseconds) || smoothingMilliseconds <= 0f
            ? 0f
            : MathF.Exp(-1f / (sampleRate * smoothingMilliseconds * 0.001f));
    }

    public void SetTarget(float gain, float smoothingMilliseconds)
    {
        Configure(smoothingMilliseconds);
        Target = gain;
    }

    public float Next()
    {
        _current = _target + ((_current - _target) * coefficient);
        if (Math.Abs(_target - _current) < 1e-7f)
        {
            _current = _target;
        }

        return _current;
    }

    public void Reset(float gain = 1f)
    {
        _current = SanitizeGain(gain);
        _target = _current;
    }

    private static float SanitizeGain(float gain) =>
        float.IsFinite(gain) ? Math.Max(0f, gain) : 1f;
}
