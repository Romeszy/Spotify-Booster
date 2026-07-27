namespace SpotifyBooster.Dsp;

public enum DspProfile
{
    Balanced,
    Punch,
    DeepBass,
    Custom,
}

public sealed record DspSettings
{
    public float MainGainDecibels { get; init; }

    public float GainSmoothingMilliseconds { get; init; } = 20f;

    public float PanicSmoothingMilliseconds { get; init; } = 1.5f;

    public bool DcBlockEnabled { get; init; } = true;

    public bool BassBoostEnabled { get; init; } = true;

    public float LowShelfFrequencyHz { get; init; } = 90f;

    public float LowShelfGainDecibels { get; init; } = 3f;

    public float LowShelfSlope { get; init; } = 1f;

    public bool EqualizerEnabled { get; init; }

    public float EqualizerLowGainDecibels { get; init; }

    public float EqualizerMidGainDecibels { get; init; }

    public float EqualizerHighGainDecibels { get; init; }

    public float EqualizerQ { get; init; } = 0.707f;

    public bool CompressorEnabled { get; init; }

    public float CompressorThresholdDecibels { get; init; } = -12f;

    public float CompressorRatio { get; init; } = 3f;

    public float CompressorAttackMilliseconds { get; init; } = 10f;

    public float CompressorReleaseMilliseconds { get; init; } = 150f;

    public float CompressorMakeupGainDecibels { get; init; }

    public bool SoftClipperEnabled { get; init; } = true;

    public float SoftClipperThresholdDecibels { get; init; } = -1f;

    public float SoftClipperDrive { get; init; } = 2f;

    public bool LimiterEnabled { get; init; } = true;

    public float LimiterCeilingDecibels { get; init; } = -1f;

    public float LimiterReleaseMilliseconds { get; init; } = 80f;

    public float OutputGainDecibels { get; init; }

    public static DspSettings Balanced => new()
    {
        LowShelfFrequencyHz = 90f,
        LowShelfGainDecibels = 3f,
        LowShelfSlope = 0.9f,
    };

    public static DspSettings Punch => new()
    {
        LowShelfFrequencyHz = 110f,
        LowShelfGainDecibels = 6f,
        LowShelfSlope = 1.2f,
    };

    public static DspSettings DeepBass => new()
    {
        LowShelfFrequencyHz = 55f,
        LowShelfGainDecibels = 9f,
        LowShelfSlope = 0.75f,
    };

    public static DspSettings ForProfile(DspProfile profile) => profile switch
    {
        DspProfile.Punch => Punch,
        DspProfile.DeepBass => DeepBass,
        _ => Balanced
    };
}
