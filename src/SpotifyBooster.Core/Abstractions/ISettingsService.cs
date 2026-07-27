using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.Core.Abstractions;

public interface ISettingsService
{
    string SettingsFilePath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
