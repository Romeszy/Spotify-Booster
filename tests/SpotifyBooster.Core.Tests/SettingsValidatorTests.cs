using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.Core.Tests;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void FirstRun_ForcesNeutralGainAndLimiter()
    {
        var settings = new AppSettings
        {
            FirstRunCompleted = false,
            Processing = new AudioProcessingSettings
            {
                GainPercent = 500,
                LimiterEnabled = false,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Settings.Processing.GainPercent);
        Assert.True(result.Settings.Processing.LimiterEnabled);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(501)]
    public void InvalidGain_IsRejected(int gainPercent)
    {
        var settings = new AppSettings
        {
            FirstRunCompleted = true,
            Processing = new AudioProcessingSettings
            {
                GainPercent = gainPercent,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("GainPercent", StringComparison.Ordinal));
    }

    [Fact]
    public void NonFiniteValues_AreRejected()
    {
        var settings = new AppSettings
        {
            FirstRunCompleted = true,
            Processing = new AudioProcessingSettings
            {
                BassGainDb = double.NaN,
                BassFrequencyHz = double.PositiveInfinity,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
    }

    [Fact]
    public void DeviceIdWithControlCharacter_IsRejected()
    {
        var settings = new AppSettings
        {
            SourceDeviceId = "device\ninjected",
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DuplicateProfileNames_AreRejectedCaseInsensitively()
    {
        var settings = new AppSettings
        {
            Profiles =
            [
                new AudioProfileSettings { Name = "Studio" },
                new AudioProfileSettings { Name = "studio" },
            ],
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("dupliqué", StringComparison.Ordinal));
    }
}
