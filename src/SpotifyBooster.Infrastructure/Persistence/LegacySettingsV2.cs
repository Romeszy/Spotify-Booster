using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.Infrastructure.Persistence;

internal sealed record LegacyAppSettingsV2
{
    public int SchemaVersion { get; init; } = 2;

    public bool FirstRunCompleted { get; init; }

    public bool ExpertModeAcknowledged { get; init; }

    public string? SourceDeviceId { get; init; }

    public string? OutputDeviceId { get; init; }

    public bool StartWithWindows { get; init; }

    public bool StartMinimized { get; init; }

    public bool StartProcessingAutomatically { get; init; }

    public bool WaitForDevicesOnStartup { get; init; } = true;

    public bool WaitForSpotifyOnStartup { get; init; }

    public WindowCloseBehavior CloseBehavior { get; init; } =
        WindowCloseBehavior.MinimizeToTray;

    public bool NotificationsEnabled { get; init; } = true;

    public bool HeadphonesSafetyMode { get; init; } = true;

    public InterfaceTheme Theme { get; init; } = InterfaceTheme.Dark;

    public InterfaceDensity Density { get; init; } = InterfaceDensity.Compact;

    public bool ReduceAnimations { get; init; }

    public bool ShowLinearPercent { get; init; } = true;

    public bool DetailedDiagnostics { get; init; }

    public HotkeySettings? Hotkeys { get; init; } = new();

    public LegacyAudioProcessingSettingsV2? Processing { get; init; } = new();

    public IReadOnlyList<LegacyAudioProfileSettingsV2?>? Profiles { get; init; } = [];

    public AppSettings Migrate()
    {
        if (Processing is null
            || Hotkeys is null
            || Profiles is null
            || Profiles.Any(profile => profile?.Processing is null))
        {
            throw new InvalidDataException(
                "La configuration V2 contient un nœud nul.");
        }

        return new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            FirstRunCompleted = FirstRunCompleted,
            Expert75DbAcknowledged = false,
            SourceDeviceId = SourceDeviceId,
            OutputDeviceId = OutputDeviceId,
            StartWithWindows = StartWithWindows,
            StartMinimized = StartMinimized,
            StartProcessingAutomatically = StartProcessingAutomatically,
            WaitForDevicesOnStartup = WaitForDevicesOnStartup,
            WaitForSpotifyOnStartup = WaitForSpotifyOnStartup,
            CloseBehavior = CloseBehavior,
            NotificationsEnabled = NotificationsEnabled,
            HeadphonesSafetyMode = HeadphonesSafetyMode,
            Theme = Theme,
            Density = Density,
            ReduceAnimations = ReduceAnimations,
            ShowLinearPercent = ShowLinearPercent,
            DetailedDiagnostics = DetailedDiagnostics,
            Hotkeys = Hotkeys,
            Processing = Processing.Migrate(),
            Profiles = Profiles.Select(profile => new AudioProfileSettings
            {
                Name = profile!.Name,
                Processing = profile.Processing!.Migrate(),
            }).ToArray(),
        };
    }
}

internal sealed record LegacyAudioProfileSettingsV2
{
    public required string Name { get; init; }

    public LegacyAudioProcessingSettingsV2? Processing { get; init; } = new();
}

internal sealed record LegacyAudioProcessingSettingsV2
{
    public AudioProtectionMode Mode { get; init; } = AudioProtectionMode.Protected;

    public double MainGainDb { get; init; }

    public double ExpertMaximumGainDb { get; init; } = 1000;

    public bool DcBlockEnabled { get; init; } = true;

    public bool BassBoostEnabled { get; init; } = true;

    public double BassFrequencyHz { get; init; } = 90;

    public double BassGainDb { get; init; } = 3;

    public double BassSlope { get; init; } = 1;

    public BassProfile BassProfile { get; init; } = BassProfile.Balanced;

    public bool EqualizerEnabled { get; init; }

    public double EqualizerLowDb { get; init; }

    public double EqualizerMidDb { get; init; }

    public double EqualizerHighDb { get; init; }

    public double EqualizerQ { get; init; } = 0.707;

    public bool CompressorEnabled { get; init; }

    public double CompressorThresholdDb { get; init; } = -12;

    public double CompressorRatio { get; init; } = 3;

    public double CompressorAttackMs { get; init; } = 10;

    public double CompressorReleaseMs { get; init; } = 150;

    public double CompressorMakeupGainDb { get; init; }

    public bool SoftClipperEnabled { get; init; } = true;

    public double SoftClipperThresholdDb { get; init; } = -1;

    public double SoftClipperDrive { get; init; } = 2;

    public bool LimiterEnabled { get; init; } = true;

    public double LimiterThresholdDb { get; init; } = -1;

    public double LimiterReleaseMs { get; init; } = 80;

    public double OutputGainDb { get; init; }

    public double ParameterSmoothingMs { get; init; } = 20;

    public double PanicSmoothingMs { get; init; } = 1.5;

    public AudioProcessingSettings Migrate() => new()
    {
        Mode = Mode,
        MainGainDb = Mode == AudioProtectionMode.RawExpert
            ? AudioProcessingSettings.QuantizeRawExpertGain(MainGainDb)
            : MainGainDb,
        DcBlockEnabled = DcBlockEnabled,
        BassBoostEnabled = BassBoostEnabled,
        BassFrequencyHz = BassFrequencyHz,
        BassGainDb = BassGainDb,
        BassSlope = BassSlope,
        BassProfile = BassProfile,
        EqualizerEnabled = EqualizerEnabled,
        EqualizerLowDb = EqualizerLowDb,
        EqualizerMidDb = EqualizerMidDb,
        EqualizerHighDb = EqualizerHighDb,
        EqualizerQ = EqualizerQ,
        CompressorEnabled = CompressorEnabled,
        CompressorThresholdDb = CompressorThresholdDb,
        CompressorRatio = CompressorRatio,
        CompressorAttackMs = CompressorAttackMs,
        CompressorReleaseMs = CompressorReleaseMs,
        CompressorMakeupGainDb = CompressorMakeupGainDb,
        SoftClipperEnabled = SoftClipperEnabled,
        SoftClipperThresholdDb = SoftClipperThresholdDb,
        SoftClipperDrive = SoftClipperDrive,
        LimiterEnabled = LimiterEnabled,
        LimiterThresholdDb = LimiterThresholdDb,
        LimiterReleaseMs = LimiterReleaseMs,
        OutputGainDb = OutputGainDb,
        ParameterSmoothingMs = ParameterSmoothingMs,
        PanicSmoothingMs = PanicSmoothingMs,
    };
}
