namespace SpotifyBooster.Dsp;

public readonly record struct DspMetrics(
    float InputPeak,
    float OutputPeak,
    float LimiterGainReductionDecibels,
    int LimitedFrames,
    int ClippedSamples,
    int SanitizedSamples);

public sealed class DspPipeline
{
    public const float MaximumSafeMagnitude = 32f;

    private readonly GainSmoother _gain;
    private readonly DcBlocker _dcBlocker;
    private readonly LowShelfFilter _lowShelf;
    private readonly PeakingEqualizer _equalizer;
    private readonly StereoCompressor _compressor;
    private readonly StereoLimiter _limiter;

    public DspPipeline(int sampleRate, DspSettings? settings = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        SampleRate = sampleRate;
        Settings = settings ?? DspSettings.Balanced;
        _gain = new GainSmoother(sampleRate, Settings.GainSmoothingMilliseconds, 1f)
        {
            Target = GainConversion.PercentToFactor(Settings.GainPercent)
        };
        _dcBlocker = new DcBlocker(sampleRate);
        _lowShelf = new LowShelfFilter(sampleRate, Settings.LowShelfFrequencyHz, Settings.LowShelfGainDecibels);
        _equalizer = new PeakingEqualizer(
            sampleRate,
            Settings.EqualizerFrequencyHz,
            Settings.EqualizerGainDecibels,
            Settings.EqualizerQ)
        {
            Enabled = Settings.EqualizerEnabled
        };
        _compressor = new StereoCompressor(
            sampleRate,
            Settings.CompressorEnabled,
            Settings.CompressorThresholdDecibels,
            Settings.CompressorRatio,
            Settings.CompressorAttackMilliseconds,
            Settings.CompressorReleaseMilliseconds,
            Settings.CompressorMakeupGainDecibels);
        _limiter = new StereoLimiter(
            sampleRate,
            Settings.LimiterEnabled,
            Settings.LimiterCeilingDecibels,
            Settings.LimiterReleaseMilliseconds);
    }

    public int SampleRate { get; }
    public DspSettings Settings { get; private set; }
    public float TargetGainPercent => _gain.Target * 100f;

    public void SetGainPercent(float percent) =>
        _gain.Target = GainConversion.PercentToFactor(percent);

    public void ApplySettings(DspSettings settings)
    {
        Settings = settings;
        SetGainPercent(settings.GainPercent);
        _lowShelf.Configure(SampleRate, settings.LowShelfFrequencyHz, settings.LowShelfGainDecibels);
        _equalizer.Configure(SampleRate, settings.EqualizerFrequencyHz, settings.EqualizerGainDecibels, settings.EqualizerQ);
        _equalizer.Enabled = settings.EqualizerEnabled;
        _compressor.Configure(
            SampleRate,
            settings.CompressorThresholdDecibels,
            settings.CompressorRatio,
            settings.CompressorAttackMilliseconds,
            settings.CompressorReleaseMilliseconds,
            settings.CompressorMakeupGainDecibels);
        _compressor.Enabled = settings.CompressorEnabled;
        _limiter.Configure(SampleRate, settings.LimiterCeilingDecibels, settings.LimiterReleaseMilliseconds);
        _limiter.Enabled = settings.LimiterEnabled;
    }

    public DspMetrics Process(Span<float> interleavedStereo)
    {
        if ((interleavedStereo.Length & 1) != 0)
        {
            throw new ArgumentException("The interleaved stereo buffer must contain an even number of samples.", nameof(interleavedStereo));
        }

        float inputPeak = 0f;
        float outputPeak = 0f;
        float maximumLimiterReduction = 0f;
        int limitedFrames = 0;
        int clippedSamples = 0;
        int sanitizedSamples = 0;

        for (int index = 0; index < interleavedStereo.Length; index += 2)
        {
            float left = Sanitize(interleavedStereo[index], ref sanitizedSamples);
            float right = Sanitize(interleavedStereo[index + 1], ref sanitizedSamples);
            inputPeak = Math.Max(inputPeak, Math.Max(Math.Abs(left), Math.Abs(right)));

            _dcBlocker.Process(ref left, ref right);
            float gain = _gain.Next();
            left *= gain;
            right *= gain;
            _lowShelf.Process(ref left, ref right);
            _equalizer.Process(ref left, ref right);
            _compressor.Process(ref left, ref right);

            left = Sanitize(left, ref sanitizedSamples);
            right = Sanitize(right, ref sanitizedSamples);
            if (_limiter.Process(ref left, ref right))
            {
                limitedFrames++;
            }

            maximumLimiterReduction = Math.Max(maximumLimiterReduction, _limiter.GainReductionDecibels);
            left = Sanitize(left, ref sanitizedSamples);
            right = Sanitize(right, ref sanitizedSamples);

            if (Math.Abs(left) > 1f)
            {
                clippedSamples++;
            }

            if (Math.Abs(right) > 1f)
            {
                clippedSamples++;
            }

            outputPeak = Math.Max(outputPeak, Math.Max(Math.Abs(left), Math.Abs(right)));
            interleavedStereo[index] = left;
            interleavedStereo[index + 1] = right;
        }

        return new DspMetrics(
            inputPeak,
            outputPeak,
            maximumLimiterReduction,
            limitedFrames,
            clippedSamples,
            sanitizedSamples);
    }

    public void Reset()
    {
        _gain.Reset(1f);
        _gain.Target = GainConversion.PercentToFactor(Settings.GainPercent);
        _dcBlocker.Reset();
        _lowShelf.Reset();
        _equalizer.Reset();
        _compressor.Reset();
        _limiter.Reset();
    }

    private static float Sanitize(float sample, ref int count)
    {
        if (!float.IsFinite(sample))
        {
            count++;
            return 0f;
        }

        if (sample > MaximumSafeMagnitude)
        {
            count++;
            return MaximumSafeMagnitude;
        }

        if (sample < -MaximumSafeMagnitude)
        {
            count++;
            return -MaximumSafeMagnitude;
        }

        return sample;
    }
}
