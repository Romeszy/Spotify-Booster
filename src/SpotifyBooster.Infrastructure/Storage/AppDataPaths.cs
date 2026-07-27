namespace SpotifyBooster.Infrastructure.Storage;

public sealed class AppDataPaths
{
    private const string ApplicationDirectoryName = "SpotifyBooster";
    private const string PortableDataDirectoryName = "Data";
    private const string PortableMarkerFileName = "portable.mode";

    private AppDataPaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        SettingsFilePath = Path.Combine(RootDirectory, "settings.json");
        LogDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string SettingsFilePath { get; }

    public string LogDirectory { get; }

    public static AppDataPaths Resolve()
    {
        return Resolve(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData));
    }

    public static AppDataPaths Resolve(
        string applicationBaseDirectory,
        string localApplicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);

        string baseDirectory = Path.GetFullPath(applicationBaseDirectory);
        string rootDirectory = File.Exists(Path.Combine(
            baseDirectory,
            PortableMarkerFileName))
            ? Path.Combine(baseDirectory, PortableDataDirectoryName)
            : Path.Combine(
                Path.GetFullPath(localApplicationDataDirectory),
                ApplicationDirectoryName);

        return new AppDataPaths(rootDirectory);
    }
}
