namespace SpotifyBooster.Dsp;

public sealed class GainSmoother
{
    private readonly float _coefficient;
    private float _current;
    private float _target;

    public GainSmoother(int sampleRate, float smoothingMilliseconds = 5f, float initialGain = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        _current = SanitizeGain(initialGain);
        _target = _current;
        _coefficient = smoothingMilliseconds <= 0f
            ? 0f
            : MathF.Exp(-1f / (sampleRate * smoothingMilliseconds * 0.001f));
    }

    public float Current => _current;

    public float Target
    {
        get => _target;
        set => _target = SanitizeGain(value);
    }

    public float Next()
    {
        _current = _target + ((_current - _target) * _coefficient);
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
