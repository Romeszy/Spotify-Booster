using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Abstractions;

public interface ISpotifyProcessDetector
{
    Task<IReadOnlyList<SpotifyProcessInfo>> DetectAsync(CancellationToken cancellationToken = default);
}
