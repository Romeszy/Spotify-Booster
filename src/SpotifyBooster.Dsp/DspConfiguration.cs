namespace SpotifyBooster.Dsp;

public enum DspProfile
{
    Balanced,
    Punch,
    DeepBass
}

public readonly record struct DspSettings(
    float GainPercent,
    float GainSmoothingMilliseconds,
    float LowShelfFrequencyHz,
    float LowShelfGainDecibels,
    bool EqualizerEnabled,
    float EqualizerFrequencyHz,
    float EqualizerGainDecibels,
    float EqualizerQ,
    bool CompressorEnabled,
    float CompressorThresholdDecibels,
    float CompressorRatio,
    float CompressorAttackMilliseconds,
    float CompressorReleaseMilliseconds,
    float CompressorMakeupGainDecibels,
    bool LimiterEnabled,
    float LimiterCeilingDecibels,
    float LimiterReleaseMilliseconds)
{
    public static DspSettings Balanced => new(
        150f, 5f, 90f, 2f,
        false, 1_000f, 0f, 0.707f,
        true, -10f, 2.5f, 10f, 100f, 0f,
        true, -1f, 50f);

    public static DspSettings Punch => new(
        200f, 4f, 110f, 4f,
        true, 2_500f, 1.5f, 0.8f,
        true, -14f, 4f, 5f, 80f, 1f,
        true, -1f, 60f);

    public static DspSettings DeepBass => new(
        180f, 8f, 70f, 9f,
        false, 1_000f, 0f, 0.707f,
        true, -16f, 3f, 15f, 140f, 0f,
        true, -1f, 80f);

    public static DspSettings ForProfile(DspProfile profile) => profile switch
    {
        DspProfile.Punch => Punch,
        DspProfile.DeepBass => DeepBass,
        _ => Balanced
    };
}
