namespace SpotifyBooster.Dsp.Tests;

public sealed class DynamicsTests
{
    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void LimiterGuaranteesMinusOneDbfsCeiling(int sampleRate)
    {
        var limiter = new StereoLimiter(sampleRate);
        float ceiling = GainConversion.DecibelsToFactor(-1f);

        for (int index = 0; index < sampleRate; index++)
        {
            float left = index % 2 == 0 ? 8f : -8f;
            float right = -left * 0.5f;
            limiter.Process(ref left, ref right);

            Assert.InRange(Math.Abs(left), 0f, ceiling + 0.000001f);
            Assert.InRange(Math.Abs(right), 0f, ceiling + 0.000001f);
        }

        Assert.True(limiter.GainReductionDecibels > 0f);
    }

    [Fact]
    public void CompressorReducesSustainedSignalAboveThreshold()
    {
        var compressor = new StereoCompressor(
            48_000,
            enabled: true,
            thresholdDecibels: -18f,
            ratio: 4f,
            attackMilliseconds: 1f,
            releaseMilliseconds: 100f);
        float left = 1f;
        float right = -1f;

        for (int index = 0; index < 4_800; index++)
        {
            left = 1f;
            right = -1f;
            compressor.Process(ref left, ref right);
        }

        Assert.True(Math.Abs(left) < 0.3f, $"Compressed level was {left}.");
        Assert.True(compressor.GainReductionDecibels > 10f);
    }

    [Fact]
    public void DisabledCompressorIsNeutral()
    {
        var compressor = new StereoCompressor(48_000, enabled: false);
        float left = 1.5f;
        float right = -0.7f;

        compressor.Process(ref left, ref right);

        Assert.Equal(1.5f, left);
        Assert.Equal(-0.7f, right);
    }
}
