namespace SpotifyBooster.Dsp.Tests;

public sealed class GainTests
{
    [Theory]
    [InlineData(100f, 1f)]
    [InlineData(200f, 2f)]
    [InlineData(500f, 5f)]
    [InlineData(0f, 1f)]
    [InlineData(900f, 5f)]
    public void PercentToFactorClampsToSupportedRange(float percent, float expected)
    {
        Assert.Equal(expected, GainConversion.PercentToFactor(percent), 5);
    }

    [Fact]
    public void GainConversionsRoundTripKnownValues()
    {
        Assert.Equal(0f, GainConversion.PercentToDecibels(100f), 5);
        Assert.Equal(6.0206f, GainConversion.PercentToDecibels(200f), 3);
        Assert.Equal(5f, GainConversion.DecibelsToFactor(GainConversion.PercentToDecibels(500f)), 4);
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
}
