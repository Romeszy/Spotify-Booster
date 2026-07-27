using NAudio.Dsp;
using NAudio.Wave;

namespace SpotifyBooster.Audio.Pipeline;

internal sealed class AdaptiveResamplingSampleProvider : ISampleProvider
{
    private const double MaximumCorrectionPpm = 1_500;
    private const double CorrectionPpmPerMillisecond = 12;
    private readonly ISampleProvider source;
    private readonly Func<TimeSpan> bufferedDuration;
    private readonly WdlResampler resampler = new();
    private readonly int channels;
    private readonly int nominalInputRate;
    private readonly int outputRate;
    private readonly double targetBufferMilliseconds;
    private int framesSinceAdjustment;

    public AdaptiveResamplingSampleProvider(
        ISampleProvider source,
        int outputSampleRate,
        Func<TimeSpan> bufferedDuration,
        TimeSpan targetBuffer)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bufferedDuration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputSampleRate);

        this.source = source;
        this.bufferedDuration = bufferedDuration;
        channels = source.WaveFormat.Channels;
        nominalInputRate = source.WaveFormat.SampleRate;
        outputRate = outputSampleRate;
        targetBufferMilliseconds = targetBuffer.TotalMilliseconds;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, channels);

        resampler.SetMode(interp: true, filtercnt: 2, sinc: false);
        resampler.SetFilterParms();
        resampler.SetFeedMode(wantInputDriven: false);
        resampler.SetRates(nominalInputRate, outputRate);
    }

    public WaveFormat WaveFormat { get; }

    public double CurrentCorrectionPpm { get; private set; }

    public int Read(float[] buffer, int offset, int count)
    {
        int requestedFrames = count / channels;
        AdjustRateIfRequired(requestedFrames);

        int inputFramesNeeded = resampler.ResamplePrepare(
            requestedFrames,
            channels,
            out float[] inputBuffer,
            out int inputOffset);
        int inputFramesAvailable = source.Read(
            inputBuffer,
            inputOffset,
            inputFramesNeeded * channels) / channels;
        int outputFrames = resampler.ResampleOut(
            buffer,
            offset,
            inputFramesAvailable,
            requestedFrames,
            channels);
        return outputFrames * channels;
    }

    private void AdjustRateIfRequired(int requestedFrames)
    {
        framesSinceAdjustment += requestedFrames;
        if (framesSinceAdjustment < outputRate / 10)
        {
            return;
        }

        framesSinceAdjustment = 0;
        double errorMilliseconds =
            bufferedDuration().TotalMilliseconds - targetBufferMilliseconds;
        CurrentCorrectionPpm = Math.Clamp(
            errorMilliseconds * CorrectionPpmPerMillisecond,
            -MaximumCorrectionPpm,
            MaximumCorrectionPpm);
        double correctedInputRate =
            nominalInputRate * (1 + (CurrentCorrectionPpm / 1_000_000));
        resampler.SetRates(correctedInputRate, outputRate);
    }
}
