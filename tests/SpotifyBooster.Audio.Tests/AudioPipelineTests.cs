using NAudio.Wave;
using SpotifyBooster.Audio.Engine;
using SpotifyBooster.Audio.Pipeline;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Dsp;

namespace SpotifyBooster.Audio.Tests;

public sealed class AudioPipelineTests
{
    [Fact]
    public void MonitoredBuffer_CountsUnderrunAndOverrun()
    {
        var format = new WaveFormat(48_000, 16, 2);
        var provider = new MonitoredBufferedWaveProvider(
            format,
            TimeSpan.FromMilliseconds(20));
        var output = new byte[128];

        int read = provider.Read(output, 0, output.Length);
        var fill = new byte[format.AverageBytesPerSecond / 50];
        provider.AddSamples(fill, 0, fill.Length);
        provider.AddSamples(fill, 0, fill.Length);

        Assert.Equal(output.Length, read);
        Assert.True(provider.UnderrunCount >= 1);
        Assert.True(provider.OverrunCount >= 1);
    }

    [Fact]
    public void AdaptiveResampler_Converts44100To48000AndAdjustsDrift()
    {
        var source = new SineSampleProvider(44_100, amplitude: 0.25f);
        TimeSpan buffered = TimeSpan.FromMilliseconds(200);
        var resampler = new AdaptiveResamplingSampleProvider(
            source,
            48_000,
            () => buffered,
            TimeSpan.FromMilliseconds(100));
        var buffer = new float[9_600];

        int read = resampler.Read(buffer, 0, buffer.Length);

        Assert.True(read > 0);
        Assert.Equal(48_000, resampler.WaveFormat.SampleRate);
        Assert.True(resampler.CurrentCorrectionPpm > 0);
        Assert.All(
            buffer.AsSpan(0, read).ToArray(),
            sample => Assert.True(float.IsFinite(sample)));
    }

    [Theory]
    [InlineData(AudioProtectionMode.Direct, 14d)]
    [InlineData(AudioProtectionMode.RawExpert, 25d)]
    public void SettingsMapper_DoesNotForceLimiterOrCompressor(
        AudioProtectionMode mode,
        double gainDecibels)
    {
        var settings = new AudioProcessingSettings
        {
            Mode = mode,
            MainGainDb = gainDecibels,
            CompressorEnabled = false,
            SoftClipperEnabled = false,
            LimiterEnabled = false,
        };

        DspSettings mapped = CoreToDspSettingsMapper.Map(settings);

        Assert.Equal((float)gainDecibels, mapped.MainGainDecibels);
        Assert.False(mapped.CompressorEnabled);
        Assert.False(mapped.SoftClipperEnabled);
        Assert.False(mapped.LimiterEnabled);
    }

    [Theory]
    [InlineData(35d, 18d)]
    [InlineData(200d, 18d)]
    public void SettingsMapper_PreservesFullV2BassRange(
        double frequencyHz,
        double gainDecibels)
    {
        var settings = new AudioProcessingSettings
        {
            BassFrequencyHz = frequencyHz,
            BassGainDb = gainDecibels,
            BassSlope = 2,
        };

        DspSettings mapped = CoreToDspSettingsMapper.Map(settings);

        Assert.Equal((float)frequencyHz, mapped.LowShelfFrequencyHz);
        Assert.Equal((float)gainDecibels, mapped.LowShelfGainDecibels);
        Assert.Equal(2f, mapped.LowShelfSlope);
    }

    [Theory]
    [InlineData(20d)]
    [InlineData(25d)]
    public void DspProvider_MeasuresRealRawGainBeforeProtections(
        double gainDecibels)
    {
        const int sampleRate = 48_000;
        float inputAmplitude = GainConversion.DecibelsToFactor(-40f);
        var source = new SineSampleProvider(sampleRate, inputAmplitude, 997f);
        var provider = new DspSampleProvider(
            source,
            RawSettings(gainDecibels));
        var buffer = new float[sampleRate / 5 * 2];
        double inputRms = inputAmplitude / Math.Sqrt(2);

        provider.Read(buffer, 0, buffer.Length);
        int read = provider.Read(buffer, 0, buffer.Length);

        Assert.True(provider.TryConsumeMetrics(out DspReadSnapshot reported));
        double outputRms = MeasureRms(buffer.AsSpan(0, read));
        double measuredGain = 20d * Math.Log10(outputRms / inputRms);
        float expectedPeak = GainConversion.DecibelsToFactor(
            -40f + (float)gainDecibels);
        Assert.InRange(measuredGain, gainDecibels - 0.02, gainDecibels + 0.02);
        Assert.InRange(
            reported.Metrics.AfterMainGainPeak,
            expectedPeak - 0.0001f,
            expectedPeak + 0.0001f);
        Assert.Equal((float)gainDecibels, reported.RequestedMainGainDb);
        Assert.InRange(
            reported.Metrics.AppliedMainGainDecibels,
            (float)gainDecibels - 0.01f,
            (float)gainDecibels + 0.01f);
        Assert.Equal(0, reported.Metrics.SoftClippedSamples);
        Assert.Equal(0, reported.Metrics.LimitedFrames);
    }

    [Fact]
    public void DspProvider_ReportsFinalCeilingWhenLimiterIsActive()
    {
        const int sampleRate = 48_000;
        var source = new SineSampleProvider(
            sampleRate,
            GainConversion.DecibelsToFactor(-10f),
            997f);
        AudioProcessingSettings settings = RawSettings(25d) with
        {
            LimiterEnabled = true,
            LimiterThresholdDb = -1,
        };
        var provider = new DspSampleProvider(
            source,
            settings);
        var buffer = new float[sampleRate / 5 * 2];

        int read = provider.Read(buffer, 0, buffer.Length);

        Assert.True(provider.TryConsumeMetrics(out DspReadSnapshot reported));
        float ceiling = GainConversion.DecibelsToFactor(-1f);
        Assert.True(buffer.AsSpan(0, read).ToArray().Max(Math.Abs) <= ceiling + 0.000001f);
        Assert.True(reported.Metrics.AfterMainGainPeak > 5f);
        Assert.True(reported.Metrics.AfterLimiterPeak <= ceiling + 0.000001f);
        Assert.True(reported.Metrics.OutputPeak <= ceiling + 0.000001f);
        Assert.True(reported.Metrics.LimitedFrames > 0);
        Assert.True(reported.Metrics.LimiterGainReductionDecibels > 0f);
    }

    [Fact]
    public void DspProvider_PanicResetReturnsRequestedAndAppliedGainToZeroDb()
    {
        const int sampleRate = 48_000;
        var source = new SineSampleProvider(
            sampleRate,
            GainConversion.DecibelsToFactor(-40f),
            997f);
        AudioProcessingSettings settings = RawSettings(25d) with
        {
            ParameterSmoothingMs = 0.5,
            PanicSmoothingMs = 1.5,
            OutputGainDb = 12,
        };
        var provider = new DspSampleProvider(
            source,
            settings);
        var buffer = new float[sampleRate / 5 * 2];

        provider.Read(buffer, 0, buffer.Length);
        Assert.True(provider.TryConsumeMetrics(out DspReadSnapshot boosted));
        Assert.InRange(boosted.Metrics.AppliedMainGainDecibels, 24.99f, 25.01f);

        provider.RequestPanicReset();
        provider.Read(buffer, 0, buffer.Length);

        Assert.True(provider.TryConsumeMetrics(out DspReadSnapshot reset));
        Assert.Equal(0f, reset.RequestedMainGainDb);
        Assert.InRange(reset.Metrics.AppliedMainGainDecibels, -0.001f, 0.02f);
        Assert.True(reset.Metrics.OutputPeak < boosted.Metrics.OutputPeak);
        double expectedRms =
            GainConversion.DecibelsToFactor(-40f) / Math.Sqrt(2);
        double tailRms = MeasureRms(buffer.AsSpan(buffer.Length - 960));
        Assert.InRange(tailRms, expectedRms * 0.99, expectedRms * 1.01);
    }

    [Fact]
    public void StandardFormatProvider_ConvertsWaveFormatExtensibleMetadata()
    {
        var extensible = new WaveFormatExtensible(48_000, 32, 2);
        var source = new SilentWaveProvider(extensible);

        var provider = new StandardWaveFormatProvider(source);

        Assert.Equal(WaveFormatEncoding.IeeeFloat, provider.WaveFormat.Encoding);
        Assert.Equal(2, provider.WaveFormat.Channels);
    }

    [Fact]
    public void DspProvider_AggregatesOverRangeSamplesAsClipsNotLimitedFrames()
    {
        var source = new TwoStageSampleProvider(1_000);
        var provider = new DspSampleProvider(
            source,
            RawSettings(0));
        var buffer = new float[100];

        provider.Read(buffer, 0, buffer.Length);
        provider.Read(buffer, 0, buffer.Length);

        Assert.True(provider.TryConsumeMetrics(out DspReadSnapshot reported));
        Assert.True(reported.Metrics.ClippedSamples > 0);
        Assert.Equal(0, reported.Metrics.LimitedFrames);
        Assert.Equal(reported.Metrics.ClippedSamples, reported.ClipCount);
    }

    [Fact]
    public void DspProvider_ReadDoesNotAllocateAfterSettingsUpdateOrPanic()
    {
        const int sampleRate = 48_000;
        var source = new SineSampleProvider(sampleRate, amplitude: 0.01f);
        var provider = new DspSampleProvider(source, RawSettings(0));
        var buffer = new float[960];

        provider.Read(buffer, 0, buffer.Length);
        provider.UpdateSettings(RawSettings(10));
        provider.Read(buffer, 0, buffer.Length);
        provider.RequestPanicReset();
        provider.Read(buffer, 0, buffer.Length);

        provider.UpdateSettings(RawSettings(12));
        long beforeUpdatedRead = GC.GetAllocatedBytesForCurrentThread();
        provider.Read(buffer, 0, buffer.Length);
        long updatedReadAllocations =
            GC.GetAllocatedBytesForCurrentThread() - beforeUpdatedRead;

        provider.RequestPanicReset();
        long beforePanicRead = GC.GetAllocatedBytesForCurrentThread();
        provider.Read(buffer, 0, buffer.Length);
        long panicReadAllocations =
            GC.GetAllocatedBytesForCurrentThread() - beforePanicRead;

        Assert.Equal(0, updatedReadAllocations);
        Assert.Equal(0, panicReadAllocations);
    }

    [Fact]
    public void EngineClippingFlag_UsesClippedSamplesNotLimiterActivity()
    {
        DspMetrics limiterOnly = CreateMetrics(limitedFrames: 4);
        DspMetrics clipped = CreateMetrics(clippedSamples: 2);

        Assert.False(NAudioEngine.IsClippingDetected(limiterOnly));
        Assert.True(NAudioEngine.IsClippingDetected(clipped));
    }

    private static AudioProcessingSettings RawSettings(double gainDecibels) =>
        new()
        {
            Mode = AudioProtectionMode.RawExpert,
            MainGainDb = gainDecibels,
            ExpertMaximumGainDb = 25,
            ParameterSmoothingMs = 0.5,
            DcBlockEnabled = false,
            BassBoostEnabled = false,
            EqualizerEnabled = false,
            CompressorEnabled = false,
            SoftClipperEnabled = false,
            LimiterEnabled = false,
            OutputGainDb = 0,
        };

    private static DspMetrics CreateMetrics(
        int limitedFrames = 0,
        int clippedSamples = 0) =>
        new(
            InputPeak: 1f,
            AfterDcBlockPeak: 1f,
            AfterMainGainPeak: 1f,
            AfterBassBoostPeak: 1f,
            AfterEqualizerPeak: 1f,
            AfterCompressorPeak: 1f,
            AfterSoftClipperPeak: 1f,
            AfterLimiterPeak: 1f,
            OutputPeak: 1f,
            AppliedMainGainDecibels: 0f,
            CompressorGainReductionDecibels: 0f,
            LimiterGainReductionDecibels: limitedFrames > 0 ? 1f : 0f,
            SoftClippedSamples: 0,
            LimitedFrames: limitedFrames,
            ClippedSamples: clippedSamples,
            SanitizedSamples: 0);

    private static double MeasureRms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (float sample in samples)
        {
            sum += sample * sample;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    private sealed class SineSampleProvider : ISampleProvider
    {
        private readonly float amplitude;
        private readonly float frequencyHz;
        private double phase;

        public SineSampleProvider(
            int sampleRate,
            float amplitude,
            float frequencyHz = 440f)
        {
            this.amplitude = amplitude;
            this.frequencyHz = frequencyHz;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int index = 0; index < count; index += 2)
            {
                float sample = (float)(Math.Sin(phase) * amplitude);
                phase += 2 * Math.PI * frequencyHz / WaveFormat.SampleRate;
                buffer[offset + index] = sample;
                buffer[offset + index + 1] = sample;
            }

            return count;
        }
    }

    private sealed class SilentWaveProvider : IWaveProvider
    {
        public SilentWaveProvider(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

    private sealed class TwoStageSampleProvider : ISampleProvider
    {
        private int readCount;

        public TwoStageSampleProvider(int sampleRate)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            float value = readCount++ == 0 ? 2f : 0f;
            Array.Fill(buffer, value, offset, count);
            return count;
        }
    }
}
