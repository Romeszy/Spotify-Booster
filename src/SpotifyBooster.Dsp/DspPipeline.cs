namespace SpotifyBooster.Dsp;

public readonly record struct DspMetrics(
    float InputPeak,
    float AfterDcBlockPeak,
    float AfterMainGainPeak,
    float AfterBassBoostPeak,
    float AfterEqualizerPeak,
    float AfterCompressorPeak,
    float AfterSoftClipperPeak,
    float AfterLimiterPeak,
    float OutputPeak,
    float AppliedMainGainDecibels,
    float CompressorGainReductionDecibels,
    float LimiterGainReductionDecibels,
    int SoftClippedSamples,
    int LimitedFrames,
    int ClippedSamples,
    int SanitizedSamples);

public sealed class DspPipeline
{
    private const float EqualizerLowFrequencyHz = 120f;
    private const float EqualizerMidFrequencyHz = 1_000f;
    private const float EqualizerHighFrequencyHz = 8_000f;

    private readonly DecibelSmoother mainGain;
    private readonly GainSmoother outputGain;
    private readonly DcBlocker dcBlocker;
    private readonly LowShelfFilter lowShelf;
    private readonly PeakingEqualizer equalizerLow;
    private readonly PeakingEqualizer equalizerMid;
    private readonly PeakingEqualizer equalizerHigh;
    private readonly StereoCompressor compressor;
    private readonly StereoSoftClipper softClipper;
    private readonly StereoLimiter limiter;

    public DspPipeline(int sampleRate, DspSettings? settings = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        SampleRate = sampleRate;
        Settings = settings ?? DspSettings.Balanced;
        mainGain = new DecibelSmoother(
            sampleRate,
            Settings.GainSmoothingMilliseconds);
        mainGain.Target = SanitizeGainDecibels(Settings.MainGainDecibels);
        outputGain = new GainSmoother(
            sampleRate,
            Settings.GainSmoothingMilliseconds,
            1f)
        {
            Target = GainConversion.DecibelsToFactor(Settings.OutputGainDecibels),
        };
        dcBlocker = new DcBlocker(sampleRate);
        lowShelf = new LowShelfFilter(
            sampleRate,
            Settings.LowShelfFrequencyHz,
            Settings.LowShelfGainDecibels,
            Settings.LowShelfSlope);
        equalizerLow = CreateEqualizer(EqualizerLowFrequencyHz, Settings.EqualizerLowGainDecibels);
        equalizerMid = CreateEqualizer(EqualizerMidFrequencyHz, Settings.EqualizerMidGainDecibels);
        equalizerHigh = CreateEqualizer(EqualizerHighFrequencyHz, Settings.EqualizerHighGainDecibels);
        compressor = new StereoCompressor(
            sampleRate,
            Settings.CompressorEnabled,
            Settings.CompressorThresholdDecibels,
            Settings.CompressorRatio,
            Settings.CompressorAttackMilliseconds,
            Settings.CompressorReleaseMilliseconds,
            Settings.CompressorMakeupGainDecibels);
        softClipper = new StereoSoftClipper(
            Settings.SoftClipperEnabled,
            Settings.SoftClipperThresholdDecibels,
            Settings.SoftClipperDrive);
        limiter = new StereoLimiter(
            sampleRate,
            Settings.LimiterEnabled,
            Settings.LimiterCeilingDecibels,
            Settings.LimiterReleaseMilliseconds);
    }

    public int SampleRate { get; }

    public DspSettings Settings { get; private set; }

    public float TargetMainGainDecibels => (float)mainGain.Target;

    public float AppliedMainGainDecibels => (float)mainGain.Current;

    public void SetMainGainDecibels(float decibels)
    {
        mainGain.SetTarget(
            SanitizeGainDecibels(decibels),
            Settings.GainSmoothingMilliseconds);
    }

    public void ApplySettings(DspSettings settings, bool panic = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        mainGain.SetTarget(
            SanitizeGainDecibels(settings.MainGainDecibels),
            panic ? settings.PanicSmoothingMilliseconds : settings.GainSmoothingMilliseconds);
        outputGain.SetTarget(
            GainConversion.DecibelsToFactor(Math.Clamp(settings.OutputGainDecibels, -12f, 12f)),
            panic ? settings.PanicSmoothingMilliseconds : settings.GainSmoothingMilliseconds);
        lowShelf.Configure(
            SampleRate,
            settings.LowShelfFrequencyHz,
            settings.LowShelfGainDecibels,
            settings.LowShelfSlope);
        ConfigureEqualizer(equalizerLow, EqualizerLowFrequencyHz, settings.EqualizerLowGainDecibels);
        ConfigureEqualizer(equalizerMid, EqualizerMidFrequencyHz, settings.EqualizerMidGainDecibels);
        ConfigureEqualizer(equalizerHigh, EqualizerHighFrequencyHz, settings.EqualizerHighGainDecibels);
        compressor.Configure(
            SampleRate,
            settings.CompressorThresholdDecibels,
            settings.CompressorRatio,
            settings.CompressorAttackMilliseconds,
            settings.CompressorReleaseMilliseconds,
            settings.CompressorMakeupGainDecibels);
        compressor.Enabled = settings.CompressorEnabled;
        softClipper.Configure(settings.SoftClipperThresholdDecibels, settings.SoftClipperDrive);
        softClipper.Enabled = settings.SoftClipperEnabled;
        limiter.Configure(
            SampleRate,
            settings.LimiterCeilingDecibels,
            settings.LimiterReleaseMilliseconds);
        limiter.Enabled = settings.LimiterEnabled;
    }

    public void PanicReset()
    {
        ApplySettings(
            Settings with
            {
                MainGainDecibels = 0f,
                OutputGainDecibels = 0f,
            },
            panic: true);
    }

    public DspMetrics Process(Span<float> interleavedStereo)
    {
        if ((interleavedStereo.Length & 1) != 0)
        {
            throw new ArgumentException(
                "The interleaved stereo buffer must contain an even number of samples.",
                nameof(interleavedStereo));
        }

        float inputPeak = 0f;
        float afterDcPeak = 0f;
        float afterMainGainPeak = 0f;
        float afterBassPeak = 0f;
        float afterEqualizerPeak = 0f;
        float afterCompressorPeak = 0f;
        float afterSoftClipperPeak = 0f;
        float afterLimiterPeak = 0f;
        float outputPeak = 0f;
        float maximumCompressorReduction = 0f;
        float maximumLimiterReduction = 0f;
        int softClippedSamples = 0;
        int limitedFrames = 0;
        int clippedSamples = 0;
        int sanitizedSamples = 0;

        for (int index = 0; index < interleavedStereo.Length; index += 2)
        {
            float left = Sanitize(interleavedStereo[index], ref sanitizedSamples);
            float right = Sanitize(interleavedStereo[index + 1], ref sanitizedSamples);
            inputPeak = UpdatePeak(inputPeak, left, right);

            if (Settings.DcBlockEnabled)
            {
                dcBlocker.Process(ref left, ref right);
                int contaminatedChannels = SanitizeChannels(
                    ref left,
                    ref right,
                    ref sanitizedSamples);
                if ((contaminatedChannels & 1) != 0)
                {
                    dcBlocker.ResetLeft();
                }

                if ((contaminatedChannels & 2) != 0)
                {
                    dcBlocker.ResetRight();
                }
            }

            afterDcPeak = UpdatePeak(afterDcPeak, left, right);
            double appliedGain = mainGain.NextFactor();
            left = ApplyGainSaturated(left, appliedGain, ref sanitizedSamples);
            right = ApplyGainSaturated(right, appliedGain, ref sanitizedSamples);
            afterMainGainPeak = UpdatePeak(afterMainGainPeak, left, right);

            if (Settings.BassBoostEnabled)
            {
                lowShelf.Process(ref left, ref right);
                int contaminatedChannels = SanitizeChannels(
                    ref left,
                    ref right,
                    ref sanitizedSamples);
                if ((contaminatedChannels & 1) != 0)
                {
                    lowShelf.ResetLeft();
                }

                if ((contaminatedChannels & 2) != 0)
                {
                    lowShelf.ResetRight();
                }
            }

            afterBassPeak = UpdatePeak(afterBassPeak, left, right);

            if (Settings.EqualizerEnabled)
            {
                equalizerLow.Process(ref left, ref right);
                ResetEqualizerChannels(
                    equalizerLow,
                    SanitizeChannels(
                    ref left,
                    ref right,
                    ref sanitizedSamples));
                equalizerMid.Process(ref left, ref right);
                ResetEqualizerChannels(
                    equalizerMid,
                    SanitizeChannels(
                    ref left,
                    ref right,
                    ref sanitizedSamples));
                equalizerHigh.Process(ref left, ref right);
                ResetEqualizerChannels(
                    equalizerHigh,
                    SanitizeChannels(
                    ref left,
                    ref right,
                    ref sanitizedSamples));
            }

            afterEqualizerPeak = UpdatePeak(afterEqualizerPeak, left, right);
            compressor.Process(ref left, ref right);
            SanitizePair(ref left, ref right, ref sanitizedSamples);
            maximumCompressorReduction = Math.Max(
                maximumCompressorReduction,
                compressor.GainReductionDecibels);
            afterCompressorPeak = UpdatePeak(afterCompressorPeak, left, right);

            softClippedSamples += softClipper.Process(ref left, ref right);
            SanitizePair(ref left, ref right, ref sanitizedSamples);
            afterSoftClipperPeak = UpdatePeak(afterSoftClipperPeak, left, right);
            if (limiter.Process(ref left, ref right))
            {
                limitedFrames++;
            }

            maximumLimiterReduction = Math.Max(
                maximumLimiterReduction,
                limiter.GainReductionDecibels);
            SanitizePair(ref left, ref right, ref sanitizedSamples);
            afterLimiterPeak = UpdatePeak(afterLimiterPeak, left, right);

            float appliedOutputGain = outputGain.Next();
            left = ApplyGainSaturated(left, appliedOutputGain, ref sanitizedSamples);
            right = ApplyGainSaturated(right, appliedOutputGain, ref sanitizedSamples);
            if (Math.Abs(left) > 1f)
            {
                clippedSamples++;
            }

            if (Math.Abs(right) > 1f)
            {
                clippedSamples++;
            }

            outputPeak = UpdatePeak(outputPeak, left, right);
            interleavedStereo[index] = left;
            interleavedStereo[index + 1] = right;
        }

        return new DspMetrics(
            inputPeak,
            afterDcPeak,
            afterMainGainPeak,
            afterBassPeak,
            afterEqualizerPeak,
            afterCompressorPeak,
            afterSoftClipperPeak,
            afterLimiterPeak,
            outputPeak,
            AppliedMainGainDecibels,
            maximumCompressorReduction,
            maximumLimiterReduction,
            softClippedSamples,
            limitedFrames,
            clippedSamples,
            sanitizedSamples);
    }

    public void Reset()
    {
        mainGain.Reset();
        mainGain.SetTarget(
            SanitizeGainDecibels(Settings.MainGainDecibels),
            Settings.GainSmoothingMilliseconds);
        outputGain.Reset(1f);
        outputGain.SetTarget(
            GainConversion.DecibelsToFactor(Settings.OutputGainDecibels),
            Settings.GainSmoothingMilliseconds);
        dcBlocker.Reset();
        lowShelf.Reset();
        equalizerLow.Reset();
        equalizerMid.Reset();
        equalizerHigh.Reset();
        compressor.Reset();
        limiter.Reset();
    }

    private PeakingEqualizer CreateEqualizer(float frequency, float gain)
    {
        return new PeakingEqualizer(SampleRate, frequency, gain, Settings.EqualizerQ)
        {
            Enabled = true,
        };
    }

    private void ConfigureEqualizer(
        PeakingEqualizer equalizer,
        float frequency,
        float gain)
    {
        equalizer.Configure(SampleRate, frequency, gain, Settings.EqualizerQ);
        equalizer.Enabled = true;
    }

    private static float SanitizeGainDecibels(float decibels) =>
        float.IsFinite(decibels) ? Math.Clamp(decibels, 0f, 75f) : 0f;

    private static float ApplyGainSaturated(
        float sample,
        double gain,
        ref int count)
    {
        double amplified = sample * gain;
        if (double.IsNaN(amplified))
        {
            count++;
            return 0f;
        }

        if (amplified > float.MaxValue)
        {
            count++;
            return float.MaxValue;
        }

        if (amplified < -float.MaxValue)
        {
            count++;
            return -float.MaxValue;
        }

        return (float)amplified;
    }

    private static float UpdatePeak(float peak, float left, float right) =>
        Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));

    private static bool SanitizePair(
        ref float left,
        ref float right,
        ref int count)
    {
        bool contaminated = !float.IsFinite(left) || !float.IsFinite(right);
        left = Sanitize(left, ref count);
        right = Sanitize(right, ref count);
        return contaminated;
    }

    private static int SanitizeChannels(
        ref float left,
        ref float right,
        ref int count)
    {
        int contaminatedChannels = 0;
        if (!float.IsFinite(left))
        {
            left = Sanitize(left, ref count);
            contaminatedChannels |= 1;
        }

        if (!float.IsFinite(right))
        {
            right = Sanitize(right, ref count);
            contaminatedChannels |= 2;
        }

        return contaminatedChannels;
    }

    private static void ResetEqualizerChannels(
        StereoBiquad equalizer,
        int contaminatedChannels)
    {
        if ((contaminatedChannels & 1) != 0)
        {
            equalizer.ResetLeft();
        }

        if ((contaminatedChannels & 2) != 0)
        {
            equalizer.ResetRight();
        }
    }

    private static float Sanitize(float sample, ref int count)
    {
        if (!float.IsFinite(sample))
        {
            count++;
            return float.IsNaN(sample)
                ? 0f
                : MathF.CopySign(float.MaxValue, sample);
        }

        return sample;
    }
}
