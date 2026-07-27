using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Dsp;

namespace SpotifyBooster.Audio.Pipeline;

internal static class CoreToDspSettingsMapper
{
    public static DspSettings Map(AudioProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new()
        {
            MainGainDecibels = (float)Math.Clamp(
                settings.MainGainDb,
                AudioProcessingSettings.MinimumGainDb,
                AudioProcessingSettings.ArchitecturalMaximumGainDb),
            GainSmoothingMilliseconds = (float)Math.Clamp(settings.ParameterSmoothingMs, 0.5, 250),
            PanicSmoothingMilliseconds = (float)Math.Clamp(settings.PanicSmoothingMs, 0.1, 10),
            DcBlockEnabled = settings.DcBlockEnabled,
            BassBoostEnabled = settings.BassBoostEnabled,
            LowShelfFrequencyHz = (float)Math.Clamp(settings.BassFrequencyHz, 35, 200),
            LowShelfGainDecibels = (float)Math.Clamp(settings.BassGainDb, 0, 18),
            LowShelfSlope = (float)Math.Clamp(settings.BassSlope, 0.25, 2),
            EqualizerEnabled = settings.EqualizerEnabled,
            EqualizerLowGainDecibels = (float)Math.Clamp(settings.EqualizerLowDb, -12, 12),
            EqualizerMidGainDecibels = (float)Math.Clamp(settings.EqualizerMidDb, -12, 12),
            EqualizerHighGainDecibels = (float)Math.Clamp(settings.EqualizerHighDb, -12, 12),
            EqualizerQ = (float)Math.Clamp(settings.EqualizerQ, 0.1, 10),
            CompressorEnabled = settings.CompressorEnabled,
            CompressorThresholdDecibels = (float)Math.Clamp(settings.CompressorThresholdDb, -60, 0),
            CompressorRatio = (float)Math.Clamp(settings.CompressorRatio, 1, 20),
            CompressorAttackMilliseconds = (float)Math.Clamp(settings.CompressorAttackMs, 0.1, 200),
            CompressorReleaseMilliseconds = (float)Math.Clamp(settings.CompressorReleaseMs, 10, 2_000),
            CompressorMakeupGainDecibels = (float)Math.Clamp(settings.CompressorMakeupGainDb, -12, 24),
            SoftClipperEnabled = settings.SoftClipperEnabled,
            SoftClipperThresholdDecibels = (float)Math.Clamp(settings.SoftClipperThresholdDb, -18, -0.01),
            SoftClipperDrive = (float)Math.Clamp(settings.SoftClipperDrive, 0.5, 8),
            LimiterEnabled = settings.LimiterEnabled,
            LimiterCeilingDecibels = (float)Math.Clamp(settings.LimiterThresholdDb, -12, -0.01),
            LimiterReleaseMilliseconds = (float)Math.Clamp(settings.LimiterReleaseMs, 10, 1_000),
            OutputGainDecibels = (float)Math.Clamp(settings.OutputGainDb, -12, 12),
        };
    }
}
