namespace SpotifyBooster.Infrastructure.Updates;

public sealed record GitHubRelease(
    Version Version,
    Uri InstallerUrl,
    long Size,
    string Sha256);
