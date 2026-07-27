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

public sealed class AutomaticGainReductionEventArgs : EventArgs
{
    public AutomaticGainReductionEventArgs(int gainPercent)
    {
        GainPercent = gainPercent;
    }

    public int GainPercent { get; }
}

public sealed record AudioEngineMetrics(
    DateTimeOffset Timestamp,
    float InputPeak,
    float OutputPeak,
    float LimiterReductionDb,
    bool InputSignalPresent,
    bool ClippingDetected,
    long ClipCount,
    long BufferCount,
    long BufferUnderrunCount,
    long BufferOverrunCount,
    int SampleRate,
    int Channels,
    int BufferMilliseconds,
    double EstimatedLatencyMilliseconds,
    double ProcessingLoadPercent)
{
    public static AudioEngineMetrics Empty { get; } = new(
        DateTimeOffset.MinValue,
        0,
        0,
        0,
        false,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
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
