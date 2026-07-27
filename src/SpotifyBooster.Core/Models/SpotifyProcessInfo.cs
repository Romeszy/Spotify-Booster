namespace SpotifyBooster.Core.Models;

public sealed record SpotifyProcessInfo(
    int ProcessId,
    string ProcessName,
    DateTimeOffset? StartedAt,
    string Distribution);
