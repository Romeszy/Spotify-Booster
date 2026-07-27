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
        var source = new SineSampleProvider(44_100);
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
        Assert.All(buffer.AsSpan(0, read).ToArray(), sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void SettingsMapper_ForcesLimiterWhenGainIsAboveNeutral()
    {
        var settings = new AudioProcessingSettings
        {
            GainPercent = 200,
            LimiterEnabled = false,
        };

        SpotifyBooster.Dsp.DspSettings mapped = CoreToDspSettingsMapper.Map(settings);

        Assert.True(mapped.LimiterEnabled);
        Assert.Equal(200, mapped.GainPercent);
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
        DspReadSnapshot? snapshot = null;
        var provider = new DspSampleProvider(
            source,
            new AudioProcessingSettings
            {
                BassBoostEnabled = false,
                CompressorEnabled = false,
                LimiterEnabled = false,
            },
            value => snapshot = value,
            _ => { });
        var buffer = new float[100];

        provider.Read(buffer, 0, buffer.Length);
        provider.Read(buffer, 0, buffer.Length);

        DspReadSnapshot reported = Assert.IsType<DspReadSnapshot>(snapshot);
        Assert.True(reported.Metrics.ClippedSamples > 0);
        Assert.Equal(0, reported.Metrics.LimitedFrames);
        Assert.Equal(reported.Metrics.ClippedSamples, reported.ClipCount);
    }

    [Fact]
    public void EngineClippingFlag_UsesClippedSamplesNotLimiterActivity()
    {
        var limiterOnly = new DspMetrics(1f, 1f, 1f, 4, 0, 0);
        var clipped = new DspMetrics(2f, 2f, 0f, 0, 2, 0);

        Assert.False(NAudioEngine.IsClippingDetected(limiterOnly));
        Assert.True(NAudioEngine.IsClippingDetected(clipped));
    }

    private sealed class SineSampleProvider : ISampleProvider
    {
        private double phase;

        public SineSampleProvider(int sampleRate)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            for (int index = 0; index < count; index += 2)
            {
                float sample = (float)(Math.Sin(phase) * 0.25);
                phase += 2 * Math.PI * 440 / WaveFormat.SampleRate;
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
