using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.Core.Models;

public sealed record DiagnosticError(
    DateTimeOffset Timestamp,
    string Component,
    string Message);

public sealed record DiagnosticSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SpotifyProcessInfo> SpotifyProcesses,
    AudioDeviceInfo? Source,
    AudioDeviceInfo? Output,
    AudioEngineStatus Engine,
    IReadOnlyList<DiagnosticError> RecentErrors,
    AudioProcessingSettings? Processing = null);
