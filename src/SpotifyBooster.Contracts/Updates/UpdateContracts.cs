namespace SpotifyBooster.Contracts.Updates;

public sealed record UpdateContract(Version Version, Uri DownloadPage, bool Mandatory);
