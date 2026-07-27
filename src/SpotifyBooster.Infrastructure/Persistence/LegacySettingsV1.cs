using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.Infrastructure.Persistence;

internal sealed record LegacyAudioProfileSettingsV1
{
    public required string Name { get; init; }

    public LegacyAudioProcessingSettingsV1 Processing { get; init; } = new();
}

internal sealed record LegacyAudioProcessingSettingsV1
{
    public int GainPercent { get; init; } = 100;

    public bool DcBlockEnabled { get; init; } = true;

    public bool BassBoostEnabled { get; init; } = true;

    public double BassFrequencyHz { get; init; } = 90;

    public double BassGainDb { get; init; } = 3;

    public BassProfile BassProfile { get; init; } = BassProfile.Balanced;

    public bool EqualizerEnabled { get; init; }

    public double EqualizerLowDb { get; init; }

    public double EqualizerMidDb { get; init; }

    public double EqualizerHighDb { get; init; }

    public bool CompressorEnabled { get; init; } = true;

    public double CompressorThresholdDb { get; init; } = -12;

    public double CompressorRatio { get; init; } = 3;

    public double CompressorAttackMs { get; init; } = 10;

    public double CompressorReleaseMs { get; init; } = 150;

    public double CompressorMakeupGainDb { get; init; }

    public bool LimiterEnabled { get; init; } = true;

    public double LimiterThresholdDb { get; init; } = -1;

    public double LimiterReleaseMs { get; init; } = 80;

    public double ParameterSmoothingMs { get; init; } = 20;
}

internal sealed record LegacyAppSettingsV1
{
    public int SchemaVersion { get; init; } = 1;

    public bool FirstRunCompleted { get; init; }

    public string? SourceDeviceId { get; init; }

    public string? OutputDeviceId { get; init; }

    public bool StartWithWindows { get; init; }

    public bool MinimizeToTray { get; init; } = true;

    public bool HeadphonesSafetyMode { get; init; } = true;

    public LegacyAudioProcessingSettingsV1 Processing { get; init; } = new();

    public IReadOnlyList<LegacyAudioProfileSettingsV1> Profiles { get; init; } = [];
}

internal static class SettingsV1Migration
{
    public static AppSettings Migrate(LegacyAppSettingsV1 legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        return new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            FirstRunCompleted = legacy.FirstRunCompleted,
            SourceDeviceId = legacy.SourceDeviceId,
            OutputDeviceId = legacy.OutputDeviceId,
            StartWithWindows = legacy.StartWithWindows,
            CloseBehavior = legacy.MinimizeToTray
                ? WindowCloseBehavior.MinimizeToTray
                : WindowCloseBehavior.Exit,
            HeadphonesSafetyMode = legacy.HeadphonesSafetyMode,
            Processing = Migrate(legacy.Processing),
            Profiles = legacy.Profiles
                .Where(profile => profile is not null)
                .Select(profile => new AudioProfileSettings
                {
                    Name = profile.Name,
                    Processing = Migrate(profile.Processing),
                })
                .ToArray(),
        };
    }

    public static AudioProcessingSettings Migrate(
        LegacyAudioProcessingSettingsV1 legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        float percent = Math.Clamp(legacy.GainPercent, 100, 500);
        return new AudioProcessingSettings
        {
            Mode = legacy.LimiterEnabled
                ? AudioProtectionMode.Protected
                : AudioProtectionMode.Direct,
            MainGainDb = 20d * Math.Log10(percent / 100d),
            DcBlockEnabled = legacy.DcBlockEnabled,
            BassBoostEnabled = legacy.BassBoostEnabled,
            BassFrequencyHz = legacy.BassFrequencyHz,
            BassGainDb = legacy.BassGainDb,
            BassProfile = legacy.BassProfile,
            EqualizerEnabled = legacy.EqualizerEnabled,
            EqualizerLowDb = legacy.EqualizerLowDb,
            EqualizerMidDb = legacy.EqualizerMidDb,
            EqualizerHighDb = legacy.EqualizerHighDb,
            CompressorEnabled = legacy.CompressorEnabled,
            CompressorThresholdDb = legacy.CompressorThresholdDb,
            CompressorRatio = legacy.CompressorRatio,
            CompressorAttackMs = legacy.CompressorAttackMs,
            CompressorReleaseMs = legacy.CompressorReleaseMs,
            CompressorMakeupGainDb = legacy.CompressorMakeupGainDb,
            SoftClipperEnabled = legacy.LimiterEnabled,
            LimiterEnabled = legacy.LimiterEnabled,
            LimiterThresholdDb = legacy.LimiterThresholdDb,
            LimiterReleaseMs = legacy.LimiterReleaseMs,
            ParameterSmoothingMs = legacy.ParameterSmoothingMs,
        };
    }
}
