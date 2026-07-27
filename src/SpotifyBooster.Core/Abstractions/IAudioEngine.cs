using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Abstractions;

public interface IAudioEngine : IAsyncDisposable
{
    event EventHandler<AudioEngineStatus>? StatusChanged;

    event EventHandler<AudioEngineMetrics>? MetricsUpdated;

    AudioEngineStatus Status { get; }

    Task StartAsync(
        AudioRoute route,
        AudioProcessingSettings settings,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    void UpdateProcessingSettings(AudioProcessingSettings settings);

    void PanicReset();
}
