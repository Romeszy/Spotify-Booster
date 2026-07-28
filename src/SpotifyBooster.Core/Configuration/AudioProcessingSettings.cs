using System.Text.Json.Serialization;

namespace SpotifyBooster.Core.Configuration;

public enum AudioProtectionMode
{
    Protected,
    Direct,
    RawExpert,
}

public enum BassProfile
{
    Balanced,
    Punch,
    DeepBass,
    Custom,
}

public sealed record AudioProcessingSettings
{
    public const double MinimumGainDb = 0;
    public const double StandardMaximumGainDb = 14;
    public const double RawExpertMaximumGainDb = 75;
    public const double RawExpertGainStepDb = 0.5;
    public const double ArchitecturalMaximumGainDb = RawExpertMaximumGainDb;

    public AudioProtectionMode Mode { get; init; } = AudioProtectionMode.Protected;

    public double MainGainDb { get; init; }

    // Source-compatible alias for V2 callers. The product ceiling is fixed in V3.
    [JsonIgnore]
    public double ExpertMaximumGainDb { get; init; } = RawExpertMaximumGainDb;

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

    public double MaximumAllowedGainDb =>
        Mode == AudioProtectionMode.RawExpert
            ? RawExpertMaximumGainDb
            : StandardMaximumGainDb;

    public static double QuantizeRawExpertGain(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Le gain doit être fini.");
        }

        double clamped = Math.Clamp(value, MinimumGainDb, RawExpertMaximumGainDb);
        double steps = Math.Round(
            clamped / RawExpertGainStepDb,
            MidpointRounding.AwayFromZero);
        return Math.Clamp(
            steps * RawExpertGainStepDb,
            MinimumGainDb,
            RawExpertMaximumGainDb);
    }
}
