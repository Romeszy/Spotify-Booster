using System.Diagnostics;
using NAudio.Wave;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Dsp;

namespace SpotifyBooster.Audio.Pipeline;

internal readonly record struct DspReadSnapshot(
    DspMetrics Metrics,
    long BufferCount,
    long ClipCount,
    double ProcessingLoadPercent,
    int EffectiveGainPercent);

internal sealed class DspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly DspPipeline pipeline;
    private readonly Action<DspReadSnapshot> metricsCallback;
    private readonly Action<int> automaticGainReductionCallback;
    private SettingsEnvelope? pendingSettings;
    private AudioProcessingSettings activeSettings;
    private int panicRequested;
    private long bufferCount;
    private long clipCount;
    private int reportFrames;
    private int safetyWindowFrames;
    private int safetyLimitedFrames;
    private int safetyCooldownFrames;
    private float reportInputPeak;
    private float reportOutputPeak;
    private float reportLimiterReduction;
    private int reportLimitedFrames;
    private int reportClippedSamples;

    public DspSampleProvider(
        ISampleProvider source,
        AudioProcessingSettings settings,
        Action<DspReadSnapshot> metricsCallback,
        Action<int> automaticGainReductionCallback)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(metricsCallback);
        ArgumentNullException.ThrowIfNull(automaticGainReductionCallback);
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException(
                "Le pipeline DSP attend une source stéréo.",
                nameof(source));
        }

        this.source = source;
        activeSettings = settings;
        this.metricsCallback = metricsCallback;
        this.automaticGainReductionCallback = automaticGainReductionCallback;
        WaveFormat = source.WaveFormat;
        pipeline = new DspPipeline(
            WaveFormat.SampleRate,
            CoreToDspSettingsMapper.Map(settings));
    }

    public WaveFormat WaveFormat { get; }

    public void UpdateSettings(AudioProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref pendingSettings, new SettingsEnvelope(settings));
    }

    public void RequestPanicReset()
    {
        Interlocked.Exchange(ref panicRequested, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        ApplyPendingSettings();

        long startedAt = Stopwatch.GetTimestamp();
        int samplesRead = source.Read(buffer, offset, count);
        if (samplesRead < count)
        {
            Array.Clear(buffer, offset + samplesRead, count - samplesRead);
            samplesRead = count;
        }

        if ((samplesRead & 1) != 0)
        {
            samplesRead--;
        }

        DspMetrics metrics = pipeline.Process(buffer.AsSpan(offset, samplesRead));
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
        int frames = samplesRead / 2;
        double audioMilliseconds =
            frames * 1_000d / WaveFormat.SampleRate;
        double processingLoadPercent = audioMilliseconds <= 0
            ? 0
            : elapsed.TotalMilliseconds / audioMilliseconds * 100;

        bufferCount++;
        clipCount += metrics.ClippedSamples;
        UpdateSafetyWindow(metrics, frames);
        AccumulateAndReport(metrics, frames, processingLoadPercent);
        return samplesRead;
    }

    private void ApplyPendingSettings()
    {
        if (Interlocked.Exchange(ref panicRequested, 0) != 0)
        {
            activeSettings = activeSettings with
            {
                GainPercent = AudioProcessingSettings.MinimumGainPercent,
                LimiterEnabled = true,
            };
            Interlocked.Exchange(ref pendingSettings, null);
            pipeline.ApplySettings(CoreToDspSettingsMapper.Map(activeSettings));
            pipeline.Reset();
            ResetSafetyWindow();
            return;
        }

        SettingsEnvelope? update = Interlocked.Exchange(ref pendingSettings, null);
        if (update is null)
        {
            return;
        }

        activeSettings = update.Settings;
        pipeline.ApplySettings(CoreToDspSettingsMapper.Map(activeSettings));
    }

    private void UpdateSafetyWindow(DspMetrics metrics, int frames)
    {
        if (safetyCooldownFrames > 0)
        {
            safetyCooldownFrames -= frames;
        }

        safetyWindowFrames += frames;
        safetyLimitedFrames += metrics.LimitedFrames;
        if (safetyWindowFrames < WaveFormat.SampleRate / 2)
        {
            return;
        }

        double limitedRatio = safetyWindowFrames == 0
            ? 0
            : (double)safetyLimitedFrames / safetyWindowFrames;
        if (limitedRatio >= 0.35
            && safetyCooldownFrames <= 0
            && activeSettings.GainPercent > AudioProcessingSettings.MinimumGainPercent)
        {
            int reducedGain = Math.Max(
                AudioProcessingSettings.MinimumGainPercent,
                activeSettings.GainPercent - 25);
            activeSettings = activeSettings with
            {
                GainPercent = reducedGain,
                LimiterEnabled = true,
            };
            pipeline.ApplySettings(CoreToDspSettingsMapper.Map(activeSettings));
            safetyCooldownFrames = WaveFormat.SampleRate * 2;
            automaticGainReductionCallback(reducedGain);
        }

        safetyWindowFrames = 0;
        safetyLimitedFrames = 0;
    }

    private void AccumulateAndReport(
        DspMetrics metrics,
        int frames,
        double processingLoadPercent)
    {
        reportFrames += frames;
        reportInputPeak = Math.Max(reportInputPeak, metrics.InputPeak);
        reportOutputPeak = Math.Max(reportOutputPeak, metrics.OutputPeak);
        reportLimiterReduction = Math.Max(
            reportLimiterReduction,
            metrics.LimiterGainReductionDecibels);
        reportLimitedFrames += metrics.LimitedFrames;
        reportClippedSamples += metrics.ClippedSamples;

        if (reportFrames < WaveFormat.SampleRate / 10)
        {
            return;
        }

        metricsCallback(new DspReadSnapshot(
            new DspMetrics(
                reportInputPeak,
                reportOutputPeak,
                reportLimiterReduction,
                reportLimitedFrames,
                reportClippedSamples,
                metrics.SanitizedSamples),
            bufferCount,
            clipCount,
            processingLoadPercent,
            activeSettings.GainPercent));
        reportFrames = 0;
        reportInputPeak = 0;
        reportOutputPeak = 0;
        reportLimiterReduction = 0;
        reportLimitedFrames = 0;
        reportClippedSamples = 0;
    }

    private void ResetSafetyWindow()
    {
        safetyWindowFrames = 0;
        safetyLimitedFrames = 0;
        safetyCooldownFrames = 0;
    }

    private sealed record SettingsEnvelope(AudioProcessingSettings Settings);
}
