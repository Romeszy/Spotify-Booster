namespace SpotifyBooster.Dsp.Tests;

public sealed class FilterTests
{
    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void LowShelfFlatSettingIsNeutral(int sampleRate)
    {
        var filter = new LowShelfFilter(sampleRate, 100f, 0f);
        float left = 0.25f;
        float right = -0.5f;

        filter.Process(ref left, ref right);

        Assert.Equal(0.25f, left, 5);
        Assert.Equal(-0.5f, right, 5);
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void LowShelfBoostsBassMoreThanTrebleAndRemainsStable(int sampleRate)
    {
        float bass = MeasureGain(sampleRate, 50f, 10f);
        float treble = MeasureGain(sampleRate, 5_000f, 10f);

        Assert.True(float.IsFinite(bass));
        Assert.True(float.IsFinite(treble));
        Assert.True(bass > 2.2f, $"Bass gain was {bass}.");
        Assert.InRange(treble, 0.95f, 1.05f);
    }

    [Theory]
    [InlineData(44_100, 35f, 18f, 10f)]
    [InlineData(44_100, 200f, 18f, 20f)]
    [InlineData(48_000, 35f, 18f, 10f)]
    [InlineData(48_000, 200f, 18f, 20f)]
    public void LowShelfSupportsFullV2BassRange(
        int sampleRate,
        float shelfFrequency,
        float boostDecibels,
        float measuredFrequency)
    {
        float bass = MeasureGain(
            sampleRate,
            measuredFrequency,
            boostDecibels,
            shelfFrequency,
            slope: 1f,
            durationSeconds: 4);
        float treble = MeasureGain(
            sampleRate,
            5_000f,
            boostDecibels,
            shelfFrequency,
            slope: 1f,
            durationSeconds: 2);

        Assert.True(float.IsFinite(bass));
        Assert.InRange(bass, 6f, 8.5f);
        Assert.InRange(treble, 0.95f, 1.05f);
    }

    [Fact]
    public void LowShelfImpulseResponseDecaysAndIsFinite()
    {
        var filter = new LowShelfFilter(48_000, 80f, 12f);
        float maximum = 0f;
        float tail = 0f;

        for (int index = 0; index < 48_000; index++)
        {
            float left = index == 0 ? 1f : 0f;
            float right = left;
            filter.Process(ref left, ref right);
            Assert.True(float.IsFinite(left));
            maximum = Math.Max(maximum, Math.Abs(left));
            if (index > 47_000)
            {
                tail = Math.Max(tail, Math.Abs(left));
            }
        }

        Assert.InRange(maximum, 0.9f, 2f);
        Assert.True(tail < 0.0001f, $"Tail remained at {tail}.");
    }

    [Fact]
    public void DcBlockerRemovesConstantOffset()
    {
        var blocker = new DcBlocker(48_000);
        float left = 0f;
        float right = 0f;

        for (int index = 0; index < 96_000; index++)
        {
            left = 0.4f;
            right = -0.4f;
            blocker.Process(ref left, ref right);
        }

        Assert.InRange(Math.Abs(left), 0f, 0.00001f);
        Assert.InRange(Math.Abs(right), 0f, 0.00001f);
    }

    [Fact]
    public void EqualizerIsFlatWhenDisabled()
    {
        var equalizer = new PeakingEqualizer(48_000, 1_000f, 12f) { Enabled = false };
        float left = 0.2f;
        float right = -0.3f;
        equalizer.Process(ref left, ref right);

        Assert.Equal(0.2f, left);
        Assert.Equal(-0.3f, right);
    }

    private static float MeasureGain(
        int sampleRate,
        float frequency,
        float boost,
        float shelfFrequency = 100f,
        float slope = 1f,
        int durationSeconds = 2)
    {
        var filter = new LowShelfFilter(
            sampleRate,
            shelfFrequency,
            boost,
            slope);
        double inputEnergy = 0;
        double outputEnergy = 0;
        int measured = 0;

        for (int index = 0; index < sampleRate * durationSeconds; index++)
        {
            float input = MathF.Sin(2f * MathF.PI * frequency * index / sampleRate) * 0.1f;
            float left = input;
            float right = input;
            filter.Process(ref left, ref right);
            if (index >= sampleRate * (durationSeconds - 1))
            {
                inputEnergy += input * input;
                outputEnergy += left * left;
                measured++;
            }
        }

        return (float)Math.Sqrt((outputEnergy / measured) / (inputEnergy / measured));
    }
}
