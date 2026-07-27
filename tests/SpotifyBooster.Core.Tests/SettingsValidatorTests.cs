using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.Core.Tests;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void FirstRun_ForcesProtectedZeroDbSafetyConfiguration()
    {
        var settings = new AppSettings
        {
            FirstRunCompleted = false,
            Processing = new AudioProcessingSettings
            {
                Mode = AudioProtectionMode.Direct,
                MainGainDb = 14,
                OutputGainDb = 12,
                CompressorEnabled = true,
                SoftClipperEnabled = false,
                LimiterEnabled = false,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.True(result.IsValid);
        Assert.Equal(AudioProtectionMode.Protected, result.Settings.Processing.Mode);
        Assert.Equal(0, result.Settings.Processing.MainGainDb);
        Assert.Equal(0, result.Settings.Processing.OutputGainDb);
        Assert.False(result.Settings.Processing.CompressorEnabled);
        Assert.True(result.Settings.Processing.SoftClipperEnabled);
        Assert.True(result.Settings.Processing.LimiterEnabled);
    }

    [Fact]
    public void RawExpertModeRequiresExplicitAcknowledgement()
    {
        var settings = new AppSettings
        {
            FirstRunCompleted = true,
            ExpertModeAcknowledged = false,
            Processing = new AudioProcessingSettings
            {
                Mode = AudioProtectionMode.RawExpert,
                MainGainDb = 1_000,
                ExpertMaximumGainDb = 1_000,
                CompressorEnabled = false,
                SoftClipperEnabled = false,
                LimiterEnabled = false,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains(
                "acquittement explicite",
                StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(AudioProtectionMode.Direct, -0.01, 25)]
    [InlineData(AudioProtectionMode.Direct, 14.01, 25)]
    [InlineData(AudioProtectionMode.Protected, 14.01, 25)]
    [InlineData(AudioProtectionMode.RawExpert, 25.01, 25)]
    [InlineData(AudioProtectionMode.RawExpert, 1000.01, 1000)]
    public void GainOutsideModeLimitIsRejected(
        AudioProtectionMode mode,
        double gainDecibels,
        double expertMaximum)
    {
        var settings = ValidCompletedSettings() with
        {
            Processing = DirectProcessing() with
            {
                Mode = mode,
                MainGainDb = gainDecibels,
                ExpertMaximumGainDb = expertMaximum,
                SoftClipperEnabled = mode == AudioProtectionMode.Protected,
                LimiterEnabled = mode == AudioProtectionMode.Protected,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("MainGainDb", StringComparison.Ordinal));
    }

    [Fact]
    public void ProtectedModeRejectsPositivePostLimiterOutputGain()
    {
        var settings = ValidCompletedSettings() with
        {
            Processing = new AudioProcessingSettings
            {
                Mode = AudioProtectionMode.Protected,
                SoftClipperEnabled = true,
                LimiterEnabled = true,
                OutputGainDb = 0.1,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains(
                "après le limiteur",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AudioProtectionMode.Protected, 14d)]
    [InlineData(AudioProtectionMode.Direct, 14d)]
    [InlineData(AudioProtectionMode.RawExpert, 25d)]
    [InlineData(AudioProtectionMode.RawExpert, 1000d)]
    public void GainAtEachModeBoundaryIsAccepted(
        AudioProtectionMode mode,
        double gainDecibels)
    {
        AudioProcessingSettings processing = DirectProcessing() with
        {
            Mode = mode,
            MainGainDb = gainDecibels,
            SoftClipperEnabled = mode == AudioProtectionMode.Protected,
            LimiterEnabled = mode == AudioProtectionMode.Protected,
        };

        SettingsValidationResult result = SettingsValidator.Validate(
            ValidCompletedSettings() with { Processing = processing });

        Assert.True(result.IsValid, string.Join(" ", result.Errors));
        Assert.Equal(gainDecibels, result.Settings.Processing.MainGainDb);
    }

    [Fact]
    public void ProtectedModeRequiresSoftClipperAndLimiter()
    {
        AudioProcessingSettings processing = DirectProcessing() with
        {
            Mode = AudioProtectionMode.Protected,
            SoftClipperEnabled = false,
            LimiterEnabled = true,
        };

        SettingsValidationResult result = SettingsValidator.Validate(
            ValidCompletedSettings() with { Processing = processing });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("mode Protégé", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AudioProtectionMode.Direct)]
    [InlineData(AudioProtectionMode.RawExpert)]
    public void DirectAndRawModesAllowCompressorAndProtectionsToBeDisabled(
        AudioProtectionMode mode)
    {
        AudioProcessingSettings processing = DirectProcessing() with
        {
            Mode = mode,
            MainGainDb = mode == AudioProtectionMode.RawExpert ? 25 : 14,
        };

        SettingsValidationResult result = SettingsValidator.Validate(
            ValidCompletedSettings() with { Processing = processing });

        Assert.True(result.IsValid, string.Join(" ", result.Errors));
        Assert.False(result.Settings.Processing.CompressorEnabled);
        Assert.False(result.Settings.Processing.SoftClipperEnabled);
        Assert.False(result.Settings.Processing.LimiterEnabled);
    }

    [Theory]
    [InlineData(34.99, 18)]
    [InlineData(200.01, 18)]
    [InlineData(35, 18.01)]
    public void BassOutsideV2RangeIsRejected(
        double frequencyHz,
        double gainDecibels)
    {
        var settings = ValidCompletedSettings() with
        {
            Processing = DirectProcessing() with
            {
                BassFrequencyHz = frequencyHz,
                BassGainDb = gainDecibels,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonFiniteValuesAreRejected()
    {
        var settings = ValidCompletedSettings() with
        {
            Processing = DirectProcessing() with
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
    public void InvalidConfigurationEnumsAreRejected()
    {
        var settings = ValidCompletedSettings() with
        {
            CloseBehavior = (WindowCloseBehavior)999,
            Theme = (InterfaceTheme)999,
            Density = (InterfaceDensity)999,
            Processing = DirectProcessing() with
            {
                Mode = (AudioProtectionMode)999,
                BassProfile = (BassProfile)999,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 5);
    }

    [Fact]
    public void DuplicateHotkeysAreRejectedCaseInsensitively()
    {
        var settings = ValidCompletedSettings() with
        {
            Hotkeys = new HotkeySettings
            {
                IncreaseGain = "Ctrl+Alt+Up",
                DecreaseGain = "ctrl+alt+up",
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("même raccourci", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateHotkeysAreRejectedWhenModifierOrderOrAliasesDiffer()
    {
        var settings = ValidCompletedSettings() with
        {
            Hotkeys = new HotkeySettings
            {
                IncreaseGain = "Control+Alt+Up",
                DecreaseGain = "alt+ctrl+UP",
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("même raccourci", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Ctrl+\nP")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+Up+Down")]
    [InlineData("Ctrl+Ctrl+P")]
    [InlineData("UnknownModifier+P")]
    public void InvalidHotkeyTextIsRejected(string hotkey)
    {
        var settings = ValidCompletedSettings() with
        {
            Hotkeys = new HotkeySettings
            {
                PanicReset = hotkey,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("raccourci", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidV2ConfigurationAndHotkeysAreAccepted()
    {
        AppSettings settings = ValidCompletedSettings() with
        {
            ExpertModeAcknowledged = true,
            CloseBehavior = WindowCloseBehavior.Ask,
            Theme = InterfaceTheme.System,
            Density = InterfaceDensity.Comfortable,
            Hotkeys = new HotkeySettings
            {
                GlobalHotkeysEnabled = true,
                IncreaseGain = "Ctrl+Shift+1",
                DecreaseGain = "Ctrl+Shift+2",
                ToggleProcessing = "Ctrl+Shift+3",
                PanicReset = "Ctrl+Shift+4",
                ToggleBassBoost = "Ctrl+Shift+5",
                ToggleWindow = "Ctrl+Shift+6",
            },
            Processing = DirectProcessing() with
            {
                Mode = AudioProtectionMode.RawExpert,
                MainGainDb = 25,
                BassFrequencyHz = 35,
                BassGainDb = 18,
            },
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.True(result.IsValid, string.Join(" ", result.Errors));
        Assert.Equal(settings, result.Settings);
    }

    [Fact]
    public void DeviceIdWithControlCharacterIsRejected()
    {
        var settings = ValidCompletedSettings() with
        {
            SourceDeviceId = "device\ninjected",
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DuplicateProfileNamesAreRejectedCaseInsensitively()
    {
        var settings = ValidCompletedSettings() with
        {
            Profiles =
            [
                new AudioProfileSettings
                {
                    Name = "Studio",
                    Processing = DirectProcessing(),
                },
                new AudioProfileSettings
                {
                    Name = "studio",
                    Processing = DirectProcessing(),
                },
            ],
        };

        SettingsValidationResult result = SettingsValidator.Validate(settings);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Contains("dupliqué", StringComparison.Ordinal));
    }

    private static AppSettings ValidCompletedSettings() =>
        new()
        {
            FirstRunCompleted = true,
            ExpertModeAcknowledged = true,
            Processing = DirectProcessing(),
            Profiles = [],
        };

    private static AudioProcessingSettings DirectProcessing() =>
        new()
        {
            Mode = AudioProtectionMode.Direct,
            MainGainDb = 0,
            CompressorEnabled = false,
            SoftClipperEnabled = false,
            LimiterEnabled = false,
        };
}
