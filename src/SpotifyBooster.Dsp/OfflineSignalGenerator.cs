namespace SpotifyBooster.Dsp;

public enum OfflineSignal
{
    Sine,
    PinkNoise,
    Impulse,
    Peaks,
    Silence,
    AbruptGainChange
}

public sealed class OfflineSignalGenerator
{
    private uint _randomState;
    private readonly float[] _pinkRows = new float[16];
    private uint _pinkCounter;
    private double _phase;

    public OfflineSignalGenerator(uint seed = 0xC0FFEEu)
    {
        _randomState = seed == 0 ? 1u : seed;
    }

    public void Fill(
        Span<float> interleavedStereo,
        OfflineSignal signal,
        int sampleRate,
        float frequencyHz = 440f,
        float amplitude = 0.5f)
    {
        if ((interleavedStereo.Length & 1) != 0)
        {
            throw new ArgumentException("The buffer must be interleaved stereo.", nameof(interleavedStereo));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        amplitude = Math.Clamp(amplitude, 0f, 4f);
        int frameCount = interleavedStereo.Length / 2;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float sample = signal switch
            {
                OfflineSignal.Sine => NextSine(sampleRate, frequencyHz) * amplitude,
                OfflineSignal.PinkNoise => NextPink() * amplitude,
                OfflineSignal.Impulse => frame == 0 ? amplitude : 0f,
                OfflineSignal.Peaks => frame % Math.Max(1, sampleRate / 20) == 0
                    ? ((frame / Math.Max(1, sampleRate / 20)) & 1) == 0 ? amplitude : -amplitude
                    : 0f,
                OfflineSignal.AbruptGainChange => NextSine(sampleRate, frequencyHz)
                    * (frame < frameCount / 2 ? amplitude * 0.1f : amplitude),
                _ => 0f
            };
            interleavedStereo[frame * 2] = sample;
            interleavedStereo[(frame * 2) + 1] = sample;
        }
    }

    public void Reset(uint seed = 0xC0FFEEu)
    {
        _randomState = seed == 0 ? 1u : seed;
        Array.Clear(_pinkRows);
        _pinkCounter = 0;
        _phase = 0;
    }

    private float NextSine(int sampleRate, float frequencyHz)
    {
        float value = MathF.Sin((float)(2d * Math.PI * _phase));
        _phase += Math.Clamp(frequencyHz, 1f, sampleRate * 0.45f) / sampleRate;
        _phase -= Math.Floor(_phase);
        return value;
    }

    private float NextPink()
    {
        _pinkCounter++;
        uint changed = _pinkCounter ^ (_pinkCounter - 1);
        for (int row = 0; row < _pinkRows.Length && (changed & (1u << row)) != 0; row++)
        {
            _pinkRows[row] = NextWhite();
        }

        float sum = NextWhite();
        for (int row = 0; row < _pinkRows.Length; row++)
        {
            sum += _pinkRows[row];
        }

        return Math.Clamp(sum / 5f, -1f, 1f);
    }

    private float NextWhite()
    {
        uint value = _randomState;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        _randomState = value;
        return ((value & 0x00FF_FFFFu) / 8_388_607.5f) - 1f;
    }
}
