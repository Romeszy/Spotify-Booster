using SpotifyBooster.Contracts.Updates;

namespace SpotifyBooster.Core.Abstractions;

public interface IUpdateService
{
    Task<UpdateContract?> CheckAsync(CancellationToken cancellationToken = default);
}
