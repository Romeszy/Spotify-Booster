namespace SpotifyBooster.Core.Models;

public sealed record DetectedAudioApplicationProcess(
    int ProcessId,
    string ProcessName,
    AudioApplicationKind ApplicationKind,
    DateTimeOffset? StartedAt,
    string Distribution);
