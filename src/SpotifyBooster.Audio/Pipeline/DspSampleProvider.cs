using System.Diagnostics;
using NAudio.Wave;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Dsp;

namespace SpotifyBooster.Audio.Pipeline;

internal readonly record struct DspReadSnapshot(
    DspMetrics Metrics,
    long BufferCount,
    long ClipCount,
    long SoftClipCount,
    long LimitedFrameCount,
    long SanitizedSampleCount,
    double ProcessingLoadPercent,
    float RequestedMainGainDb);

internal sealed class DspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly DspPipeline pipeline;
    private PreparedSettings latestSettings;
    private PreparedSettings? pendingSettings;
    private PreparedSettings? pendingPanicSettings;
    private DspReadSnapshot publishedSnapshot;
    private int publishedSnapshotVersion;
    private int consumedSnapshotVersion;
    private long bufferCount;
    private long clipCount;
    private long softClipCount;
    private long limitedFrameCount;
    private long sanitizedSampleCount;
    private int reportFrames;
    private float reportInputPeak;
    private float reportAfterDcPeak;
    private float reportAfterMainGainPeak;
    private float reportAfterBassPeak;
    private float reportAfterEqualizerPeak;
    private float reportAfterCompressorPeak;
    private float reportAfterSoftClipperPeak;
    private float reportAfterLimiterPeak;
    private float reportOutputPeak;
    private float reportAppliedMainGainDb;
    private float reportCompressorReduction;
    private float reportLimiterReduction;
    private int reportSoftClippedSamples;
    private int reportLimitedFrames;
    private int reportClippedSamples;
    private int reportSanitizedSamples;

    public DspSampleProvider(
        ISampleProvider source,
        AudioProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException(
                "Le pipeline DSP attend une source stéréo.",
                nameof(source));
        }

        this.source = source;
        latestSettings = PrepareSettings(settings);
        WaveFormat = source.WaveFormat;
        pipeline = new DspPipeline(
            WaveFormat.SampleRate,
            latestSettings.DspSettings);
    }

    public WaveFormat WaveFormat { get; }

    public void UpdateSettings(AudioProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        PreparedSettings prepared = PrepareSettings(settings);
        Volatile.Write(ref latestSettings, prepared);
        Volatile.Write(ref pendingSettings, prepared);
    }

    public void RequestPanicReset()
    {
        AudioProcessingSettings safeSettings =
            Volatile.Read(ref latestSettings).CoreSettings with
            {
                MainGainDb = AudioProcessingSettings.MinimumGainDb,
                OutputGainDb = 0,
            };
        PreparedSettings prepared = PrepareSettings(safeSettings);
        Volatile.Write(ref latestSettings, prepared);
        Volatile.Write(ref pendingPanicSettings, prepared);
    }

    public bool TryConsumeMetrics(out DspReadSnapshot snapshot)
    {
        snapshot = default;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int versionBefore = Volatile.Read(ref publishedSnapshotVersion);
            if ((versionBefore & 1) != 0
                || versionBefore == consumedSnapshotVersion)
            {
                continue;
            }

            DspReadSnapshot candidate = publishedSnapshot;
            int versionAfter = Volatile.Read(ref publishedSnapshotVersion);
            if (versionBefore != versionAfter)
            {
                continue;
            }

            consumedSnapshotVersion = versionAfter;
            snapshot = candidate;
            return true;
        }

        return false;
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
        double audioMilliseconds = frames * 1_000d / WaveFormat.SampleRate;
        double processingLoadPercent = audioMilliseconds <= 0
            ? 0
            : elapsed.TotalMilliseconds / audioMilliseconds * 100;

        bufferCount++;
        clipCount += metrics.ClippedSamples;
        softClipCount += metrics.SoftClippedSamples;
        limitedFrameCount += metrics.LimitedFrames;
        sanitizedSampleCount += metrics.SanitizedSamples;
        AccumulateAndReport(metrics, frames, processingLoadPercent);
        return samplesRead;
    }

    private void ApplyPendingSettings()
    {
        PreparedSettings? panic = Interlocked.Exchange(
            ref pendingPanicSettings,
            null);
        if (panic is not null)
        {
            Interlocked.Exchange(ref pendingSettings, null);
            pipeline.ApplySettings(panic.DspSettings, panic: true);
            return;
        }

        PreparedSettings? update = Interlocked.Exchange(ref pendingSettings, null);
        if (update is null)
        {
            return;
        }

        pipeline.ApplySettings(update.DspSettings);
    }

    private void AccumulateAndReport(
        DspMetrics metrics,
        int frames,
        double processingLoadPercent)
    {
        reportFrames += frames;
        reportInputPeak = Math.Max(reportInputPeak, metrics.InputPeak);
        reportAfterDcPeak = Math.Max(reportAfterDcPeak, metrics.AfterDcBlockPeak);
        reportAfterMainGainPeak = Math.Max(reportAfterMainGainPeak, metrics.AfterMainGainPeak);
        reportAfterBassPeak = Math.Max(reportAfterBassPeak, metrics.AfterBassBoostPeak);
        reportAfterEqualizerPeak = Math.Max(reportAfterEqualizerPeak, metrics.AfterEqualizerPeak);
        reportAfterCompressorPeak = Math.Max(reportAfterCompressorPeak, metrics.AfterCompressorPeak);
        reportAfterSoftClipperPeak = Math.Max(reportAfterSoftClipperPeak, metrics.AfterSoftClipperPeak);
        reportAfterLimiterPeak = Math.Max(reportAfterLimiterPeak, metrics.AfterLimiterPeak);
        reportOutputPeak = Math.Max(reportOutputPeak, metrics.OutputPeak);
        reportAppliedMainGainDb = metrics.AppliedMainGainDecibels;
        reportCompressorReduction = Math.Max(
            reportCompressorReduction,
            metrics.CompressorGainReductionDecibels);
        reportLimiterReduction = Math.Max(
            reportLimiterReduction,
            metrics.LimiterGainReductionDecibels);
        reportSoftClippedSamples += metrics.SoftClippedSamples;
        reportLimitedFrames += metrics.LimitedFrames;
        reportClippedSamples += metrics.ClippedSamples;
        reportSanitizedSamples += metrics.SanitizedSamples;

        if (reportFrames < WaveFormat.SampleRate / 10)
        {
            return;
        }

        PublishSnapshot(new DspReadSnapshot(
            new DspMetrics(
                reportInputPeak,
                reportAfterDcPeak,
                reportAfterMainGainPeak,
                reportAfterBassPeak,
                reportAfterEqualizerPeak,
                reportAfterCompressorPeak,
                reportAfterSoftClipperPeak,
                reportAfterLimiterPeak,
                reportOutputPeak,
                reportAppliedMainGainDb,
                reportCompressorReduction,
                reportLimiterReduction,
                reportSoftClippedSamples,
                reportLimitedFrames,
                reportClippedSamples,
                reportSanitizedSamples),
            bufferCount,
            clipCount,
            softClipCount,
            limitedFrameCount,
            sanitizedSampleCount,
            processingLoadPercent,
            pipeline.Settings.MainGainDecibels));
        ResetReport();
    }

    private void PublishSnapshot(DspReadSnapshot snapshot)
    {
        int currentVersion = Volatile.Read(ref publishedSnapshotVersion);
        Volatile.Write(ref publishedSnapshotVersion, currentVersion + 1);
        publishedSnapshot = snapshot;
        Volatile.Write(ref publishedSnapshotVersion, currentVersion + 2);
    }

    private static PreparedSettings PrepareSettings(
        AudioProcessingSettings settings) =>
        new(settings, CoreToDspSettingsMapper.Map(settings));

    private void ResetReport()
    {
        reportFrames = 0;
        reportInputPeak = 0;
        reportAfterDcPeak = 0;
        reportAfterMainGainPeak = 0;
        reportAfterBassPeak = 0;
        reportAfterEqualizerPeak = 0;
        reportAfterCompressorPeak = 0;
        reportAfterSoftClipperPeak = 0;
        reportAfterLimiterPeak = 0;
        reportOutputPeak = 0;
        reportAppliedMainGainDb = 0;
        reportCompressorReduction = 0;
        reportLimiterReduction = 0;
        reportSoftClippedSamples = 0;
        reportLimitedFrames = 0;
        reportClippedSamples = 0;
        reportSanitizedSamples = 0;
    }

    private sealed record PreparedSettings(
        AudioProcessingSettings CoreSettings,
        DspSettings DspSettings);
}
