namespace SpotifyBooster.Dsp.Tests;

public sealed class GainTests
{
    [Theory]
    [InlineData(0f, 1f, 100f)]
    [InlineData(6.0206f, 2f, 200f)]
    [InlineData(13.9794f, 5f, 500f)]
    [InlineData(20f, 10f, 1_000f)]
    [InlineData(25f, 17.782794f, 1_778.2794f)]
    public void DecibelConversionsCoverV2GainLandmarks(
        float decibels,
        float expectedFactor,
        float expectedPercent)
    {
        Assert.InRange(
            GainConversion.DecibelsToFactor(decibels),
            expectedFactor - 0.00001f,
            expectedFactor + 0.00001f);
        Assert.InRange(
            GainConversion.DecibelsToPercent(decibels),
            expectedPercent - 0.001f,
            expectedPercent + 0.001f);
        Assert.Equal(decibels, GainConversion.FactorToDecibels(expectedFactor), 3);
    }

    [Theory]
    [InlineData(100f, 0f)]
    [InlineData(200f, 6.0206f)]
    [InlineData(500f, 13.9794f)]
    [InlineData(1_000f, 20f)]
    [InlineData(1_778.2794f, 25f)]
    public void PercentDisplayRoundTripsDecibelGain(float percent, float expectedDecibels)
    {
        float decibels = GainConversion.PercentToDecibels(percent);

        Assert.Equal(expectedDecibels, decibels, 3);
        Assert.Equal(percent, GainConversion.DecibelsToPercent(decibels), 2);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidPercentHasNoFiniteDecibelRepresentation(float percent)
    {
        Assert.Equal(float.NegativeInfinity, GainConversion.PercentToDecibels(percent));
    }

    [Fact]
    public void DoubleConversionSupportsOneThousandDecibels()
    {
        double factor = GainConversion.DecibelsToFactorDouble(1_000d);

        Assert.True(double.IsFinite(factor));
        Assert.InRange(factor, 9.9999999999999e49, 1.00000000000001e50);
        Assert.InRange(
            GainConversion.DecibelsToPercentDouble(1_000d),
            9.9999999999999e51,
            1.00000000000001e52);
        Assert.Equal(1_000d, GainConversion.FactorToDecibelsDouble(factor), 10);
    }

    [Fact]
    public void FloatConversionSaturatesInsteadOfReturningInfinityAtOneThousandDecibels()
    {
        float factor = GainConversion.DecibelsToFactor(1_000f);
        float percent = GainConversion.DecibelsToPercent(1_000f);

        Assert.Equal(float.MaxValue, factor);
        Assert.Equal(float.MaxValue, percent);
        Assert.True(float.IsFinite(factor));
        Assert.True(float.IsFinite(percent));
    }

    [Fact]
    public void SmootherConvergesWithoutImmediateStep()
    {
        var smoother = new GainSmoother(48_000, 5f, 1f) { Target = 5f };

        float first = smoother.Next();
        Assert.InRange(first, 1f, 1.1f);

        for (int index = 0; index < 2_400; index++)
        {
            smoother.Next();
        }

        Assert.InRange(smoother.Current, 4.99f, 5f);
    }

    [Fact]
    public void SmootherSupportsImmediateMode()
    {
        var smoother = new GainSmoother(44_100, 0f, 1f) { Target = 3f };
        Assert.Equal(3f, smoother.Next());
    }

    [Fact]
    public void SmootherCanSwitchFromNormalToPanicTimeConstant()
    {
        var smoother = new GainSmoother(48_000, 100f, 1f);
        smoother.SetTarget(GainConversion.DecibelsToFactor(25f), 100f);
        for (int index = 0; index < 4_800; index++)
        {
            smoother.Next();
        }

        float beforePanic = smoother.Current;
        smoother.SetTarget(1f, 1.5f);
        for (int index = 0; index < 480; index++)
        {
            smoother.Next();
        }

        Assert.True(beforePanic > 10f);
        Assert.InRange(smoother.Current, 0.9999f, 1.02f);
    }

    [Theory]
    [InlineData(44_100, 20f)]
    [InlineData(48_000, 20f)]
    [InlineData(96_000, 20f)]
    [InlineData(44_100, 1.5f)]
    [InlineData(48_000, 1.5f)]
    [InlineData(96_000, 1.5f)]
    public void SmootherReachesTargetExactlyAtCeilingSampleCount(
        int sampleRate,
        float smoothingMilliseconds)
    {
        var smoother = new GainSmoother(sampleRate, smoothingMilliseconds, 1f)
        {
            Target = 5f,
        };
        int sampleCount = (int)Math.Ceiling(
            sampleRate * (double)smoothingMilliseconds / 1000d);
        float previous = smoother.Current;
        for (int index = 1; index < sampleCount; index++)
        {
            float current = smoother.Next();
            Assert.InRange(current, previous, 5f);
            Assert.True(current < 5f);
            previous = current;
        }

        Assert.Equal(5f, smoother.Next());
        for (int index = 0; index < sampleCount * 2; index++)
        {
            Assert.Equal(5f, smoother.Next());
        }
    }
}
