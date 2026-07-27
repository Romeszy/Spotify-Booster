namespace SpotifyBooster.Core.Models;

public enum AudioEngineState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    DeviceLost,
    Faulted,
}

public sealed record AudioRoute(string SourceDeviceId, string OutputDeviceId);

public sealed record AudioEngineMetrics(
    DateTimeOffset Timestamp,
    float InputPeak,
    float AfterMainGainPeak,
    float AfterBassBoostPeak,
    float AfterCompressorPeak,
    float AfterLimiterPeak,
    float OutputPeak,
    float RequestedMainGainDb,
    float AppliedMainGainDb,
    double LinearGainFactor,
    float CompressorReductionDb,
    float LimiterReductionDb,
    bool InputSignalPresent,
    bool ClippingDetected,
    long ClipCount,
    long SoftClipCount,
    long LimitedFrameCount,
    long SanitizedSampleCount,
    long BufferCount,
    long BufferUnderrunCount,
    long BufferOverrunCount,
    int SampleRate,
    int Channels,
    int BufferMilliseconds,
    double EstimatedLatencyMilliseconds,
    double ProcessingLoadPercent,
    TimeSpan Uptime)
{
    public static AudioEngineMetrics Empty { get; } = new(
        Timestamp: DateTimeOffset.MinValue,
        InputPeak: 0,
        AfterMainGainPeak: 0,
        AfterBassBoostPeak: 0,
        AfterCompressorPeak: 0,
        AfterLimiterPeak: 0,
        OutputPeak: 0,
        RequestedMainGainDb: 0,
        AppliedMainGainDb: 0,
        LinearGainFactor: 1,
        CompressorReductionDb: 0,
        LimiterReductionDb: 0,
        InputSignalPresent: false,
        ClippingDetected: false,
        ClipCount: 0,
        SoftClipCount: 0,
        LimitedFrameCount: 0,
        SanitizedSampleCount: 0,
        BufferCount: 0,
        BufferUnderrunCount: 0,
        BufferOverrunCount: 0,
        SampleRate: 0,
        Channels: 0,
        BufferMilliseconds: 0,
        EstimatedLatencyMilliseconds: 0,
        ProcessingLoadPercent: 0,
        Uptime: TimeSpan.Zero);
}

public sealed record AudioEngineStatus(
    AudioEngineState State,
    string Message,
    string? SourceFormat,
    string? OutputFormat,
    AudioEngineMetrics Metrics,
    DateTimeOffset ChangedAt)
{
    public static AudioEngineStatus Stopped { get; } = new(
        AudioEngineState.Stopped,
        "Moteur arrêté",
        null,
        null,
        AudioEngineMetrics.Empty,
        DateTimeOffset.UtcNow);
}
