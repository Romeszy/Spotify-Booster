namespace SpotifyBooster.Core.Configuration;

public enum BassProfile
{
    Balanced,
    Punch,
    DeepBass,
    Custom,
}

public sealed record AudioProcessingSettings
{
    public const int MinimumGainPercent = 100;
    public const int MaximumGainPercent = 500;

    public int GainPercent { get; init; } = MinimumGainPercent;

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
