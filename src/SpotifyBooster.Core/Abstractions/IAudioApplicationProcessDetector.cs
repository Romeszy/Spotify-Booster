using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Abstractions;

public interface IAudioApplicationProcessDetector
{
    Task<IReadOnlyList<DetectedAudioApplicationProcess>> DetectAsync(
        IReadOnlyCollection<AudioApplicationKind> applicationKinds,
        IReadOnlyCollection<string> customProcessNames,
        CancellationToken cancellationToken = default);
}
