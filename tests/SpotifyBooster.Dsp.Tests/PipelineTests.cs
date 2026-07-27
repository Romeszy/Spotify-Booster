namespace SpotifyBooster.Dsp.Tests;

public sealed class PipelineTests
{
    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void PipelineHandlesSilenceAndMaximumGainAtCommonRates(int sampleRate)
    {
        DspSettings settings = NeutralSettings() with { GainPercent = 500f, LimiterEnabled = true };
        var pipeline = new DspPipeline(sampleRate, settings);
        float[] silence = new float[2_048];

        DspMetrics silenceMetrics = pipeline.Process(silence);
        Assert.All(silence, sample => Assert.Equal(0f, sample));
        Assert.Equal(0f, silenceMetrics.OutputPeak);

        float[] maximum = new float[4_096];
        Array.Fill(maximum, 1f);
        DspMetrics metrics = pipeline.Process(maximum);
        float ceiling = GainConversion.DecibelsToFactor(-1f);
        Assert.All(maximum, sample => Assert.InRange(Math.Abs(sample), 0f, ceiling + 0.000001f));
        Assert.True(metrics.LimitedFrames > 0);
        Assert.True(metrics.LimiterGainReductionDecibels > 0f);
        Assert.Equal(0, metrics.ClippedSamples);
    }

    [Fact]
    public void PipelineSanitizesNonFiniteAndHugeSamples()
    {
        var pipeline = new DspPipeline(48_000, NeutralSettings());
        float[] buffer =
        [
            float.NaN, float.PositiveInfinity,
            float.NegativeInfinity, 1e30f,
            -1e30f, 0.25f
        ];

        DspMetrics metrics = pipeline.Process(buffer);

        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
        Assert.Equal(5, metrics.SanitizedSamples);
        Assert.True(metrics.InputPeak <= DspPipeline.MaximumSafeMagnitude);
    }

    [Fact]
    public void PipelineFinalOutputNeverExceedsLimiterCeiling()
    {
        var pipeline = new DspPipeline(48_000, DspSettings.DeepBass with { GainPercent = 500f });
        var generator = new OfflineSignalGenerator();
        float[] buffer = new float[48_000 * 2];
        generator.Fill(buffer, OfflineSignal.Peaks, 48_000, amplitude: 4f);

        DspMetrics metrics = pipeline.Process(buffer);
        float ceiling = GainConversion.DecibelsToFactor(-1f);

        Assert.True(buffer.Max(Math.Abs) <= ceiling + 0.000001f);
        Assert.True(metrics.LimitedFrames > 0);
        Assert.Equal(0, metrics.ClippedSamples);
    }

    [Fact]
    public void PipelineReportsInputAndOutputPeaks()
    {
        var pipeline = new DspPipeline(48_000, NeutralSettings());
        float[] buffer = [0.25f, -0.5f, 0.75f, -0.1f];

        DspMetrics metrics = pipeline.Process(buffer);

        Assert.Equal(0.75f, metrics.InputPeak);
        Assert.InRange(metrics.OutputPeak, 0.7f, 0.8f);
    }

    [Fact]
    public void ProcessingAReusedBufferDoesNotAllocate()
    {
        var pipeline = new DspPipeline(48_000, DspSettings.Punch);
        float[] buffer = new float[1_024];
        pipeline.Process(buffer);
        pipeline.Process(buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            pipeline.Process(buffer);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PipelineRejectsNonStereoBuffer()
    {
        var pipeline = new DspPipeline(48_000);
        Assert.Throws<ArgumentException>(() => pipeline.Process(new float[3]));
    }

    private static DspSettings NeutralSettings() => DspSettings.Balanced with
    {
        GainPercent = 100f,
        GainSmoothingMilliseconds = 0f,
        LowShelfGainDecibels = 0f,
        EqualizerEnabled = false,
        CompressorEnabled = false,
        LimiterEnabled = true
    };
}
