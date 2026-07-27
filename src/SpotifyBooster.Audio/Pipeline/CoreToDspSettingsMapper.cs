using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Dsp;

namespace SpotifyBooster.Audio.Pipeline;

internal static class CoreToDspSettingsMapper
{
    public static DspSettings Map(AudioProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        float gainPercent = Math.Clamp(settings.GainPercent, 100, 500);
        bool limiterEnabled = settings.LimiterEnabled || gainPercent > 100;
        return new(
            gainPercent,
            (float)Math.Clamp(settings.ParameterSmoothingMs, 1, 200),
            (float)Math.Clamp(settings.BassFrequencyHz, 40, 200),
            settings.BassBoostEnabled
                ? (float)Math.Clamp(settings.BassGainDb, 0, 12)
                : 0,
            settings.EqualizerEnabled,
            1_000,
            (float)Math.Clamp(settings.EqualizerMidDb, -12, 12),
            0.707f,
            settings.CompressorEnabled,
            (float)Math.Clamp(settings.CompressorThresholdDb, -60, 0),
            (float)Math.Clamp(settings.CompressorRatio, 1, 20),
            (float)Math.Clamp(settings.CompressorAttackMs, 0.1, 200),
            (float)Math.Clamp(settings.CompressorReleaseMs, 10, 2_000),
            (float)Math.Clamp(settings.CompressorMakeupGainDb, 0, 12),
            limiterEnabled,
            (float)Math.Clamp(settings.LimiterThresholdDb, -12, -0.1),
            (float)Math.Clamp(settings.LimiterReleaseMs, 10, 1_000));
    }
}
