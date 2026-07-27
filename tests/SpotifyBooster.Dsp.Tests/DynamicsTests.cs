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

    [Fact]
    public void DisabledLimiterIsTransparentAboveDigitalFullScale()
    {
        var limiter = new StereoLimiter(48_000, enabled: false);
        float left = 2.5f;
        float right = -1.25f;

        bool limited = limiter.Process(ref left, ref right);

        Assert.False(limited);
        Assert.Equal(2.5f, left);
        Assert.Equal(-1.25f, right);
        Assert.Equal(0f, limiter.GainReductionDecibels);
    }

    [Fact]
    public void DisabledSoftClipperIsTransparentAboveDigitalFullScale()
    {
        var clipper = new StereoSoftClipper(enabled: false);
        float left = 2.5f;
        float right = -1.25f;

        int affected = clipper.Process(ref left, ref right);

        Assert.Equal(0, affected);
        Assert.Equal(2.5f, left);
        Assert.Equal(-1.25f, right);
    }

    [Theory]
    [InlineData(-1f, 2f)]
    [InlineData(-6f, 4f)]
    [InlineData(-12f, 8f)]
    public void SoftClipperIsContinuousAndBounded(
        float thresholdDecibels,
        float drive)
    {
        var clipper = new StereoSoftClipper(
            enabled: true,
            thresholdDecibels,
            drive);
        float threshold = GainConversion.DecibelsToFactor(thresholdDecibels);
        float belowLeft = threshold * 0.99f;
        float belowRight = -belowLeft;

        int belowAffected = clipper.Process(ref belowLeft, ref belowRight);

        Assert.Equal(0, belowAffected);
        Assert.Equal(threshold * 0.99f, belowLeft);
        Assert.Equal(-threshold * 0.99f, belowRight);

        float aboveLeft = 4f;
        float aboveRight = -2f;
        int aboveAffected = clipper.Process(ref aboveLeft, ref aboveRight);

        Assert.Equal(2, aboveAffected);
        Assert.InRange(aboveLeft, threshold, 1f);
        Assert.InRange(aboveRight, -1f, -threshold);
    }
}
