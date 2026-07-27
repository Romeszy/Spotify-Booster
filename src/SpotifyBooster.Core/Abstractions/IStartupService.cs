namespace SpotifyBooster.Core.Abstractions;

public interface IStartupService
{
    bool IsEnabled();

    bool SetEnabled(bool enabled);
}
