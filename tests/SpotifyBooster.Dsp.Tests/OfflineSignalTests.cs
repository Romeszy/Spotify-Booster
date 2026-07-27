namespace SpotifyBooster.Dsp.Tests;

public sealed class OfflineSignalTests
{
    [Theory]
    [InlineData(OfflineSignal.Sine)]
    [InlineData(OfflineSignal.PinkNoise)]
    [InlineData(OfflineSignal.Impulse)]
    [InlineData(OfflineSignal.Peaks)]
    [InlineData(OfflineSignal.Silence)]
    [InlineData(OfflineSignal.AbruptGainChange)]
    public void GeneratorProducesFiniteStereoSignals(OfflineSignal signal)
    {
        var generator = new OfflineSignalGenerator(123u);
        float[] buffer = new float[4_096];

        generator.Fill(buffer, signal, 48_000);

        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
        for (int index = 0; index < buffer.Length; index += 2)
        {
            Assert.Equal(buffer[index], buffer[index + 1]);
        }
    }

    [Fact]
    public void PinkNoiseIsDeterministicForSeed()
    {
        var first = new OfflineSignalGenerator(42u);
        var second = new OfflineSignalGenerator(42u);
        float[] firstBuffer = new float[2_048];
        float[] secondBuffer = new float[2_048];

        first.Fill(firstBuffer, OfflineSignal.PinkNoise, 44_100);
        second.Fill(secondBuffer, OfflineSignal.PinkNoise, 44_100);

        Assert.Equal(firstBuffer, secondBuffer);
        Assert.Contains(firstBuffer, value => value != 0f);
    }

    [Fact]
    public void AbruptGainChangeHasHigherSecondHalfEnergy()
    {
        var generator = new OfflineSignalGenerator();
        float[] buffer = new float[48_000 * 2];
        generator.Fill(buffer, OfflineSignal.AbruptGainChange, 48_000, 1_000f, 0.8f);

        double firstEnergy = 0;
        double secondEnergy = 0;
        for (int frame = 0; frame < 48_000; frame++)
        {
            double energy = buffer[frame * 2] * buffer[frame * 2];
            if (frame < 24_000)
            {
                firstEnergy += energy;
            }
            else
            {
                secondEnergy += energy;
            }
        }

        Assert.True(secondEnergy > firstEnergy * 90d);
    }

    [Fact]
    public void ProfilesExposeDistinctSafeSettings()
    {
        Assert.True(DspSettings.DeepBass.LowShelfGainDecibels > DspSettings.Balanced.LowShelfGainDecibels);
        Assert.True(DspSettings.Punch.LowShelfGainDecibels > DspSettings.Balanced.LowShelfGainDecibels);
        Assert.NotEqual(DspSettings.Punch.LowShelfSlope, DspSettings.DeepBass.LowShelfSlope);
        Assert.All(Enum.GetValues<DspProfile>(), profile =>
        {
            DspSettings settings = DspSettings.ForProfile(profile);
            Assert.InRange(settings.MainGainDecibels, 0f, 30f);
            Assert.InRange(settings.LowShelfFrequencyHz, 35f, 200f);
            Assert.InRange(settings.LowShelfGainDecibels, 0f, 18f);
            Assert.True(settings.LimiterEnabled);
            Assert.Equal(-1f, settings.LimiterCeilingDecibels);
        });
    }
}
