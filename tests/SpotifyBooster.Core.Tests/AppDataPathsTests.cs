using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyBooster.Infrastructure.Diagnostics;
using SpotifyBooster.Infrastructure.Logging;
using SpotifyBooster.Infrastructure.Persistence;
using SpotifyBooster.Infrastructure.Storage;

namespace SpotifyBooster.Core.Tests;

public sealed class AppDataPathsTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpotifyBooster.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_WithPortableMarker_UsesDataDirectoryForSettingsAndLogs()
    {
        string applicationDirectory = Path.Combine(
            temporaryDirectory,
            "portable-app");
        string fakeLocalAppData = Path.Combine(
            temporaryDirectory,
            "unused-local-app-data");
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllText(
            Path.Combine(applicationDirectory, "portable.mode"),
            string.Empty);

        AppDataPaths paths = AppDataPaths.Resolve(
            applicationDirectory,
            fakeLocalAppData);

        string expectedRoot = Path.Combine(applicationDirectory, "Data");
        Assert.Equal(Path.GetFullPath(expectedRoot), paths.RootDirectory);
        Assert.Equal(
            Path.Combine(expectedRoot, "settings.json"),
            paths.SettingsFilePath);
        Assert.Equal(Path.Combine(expectedRoot, "logs"), paths.LogDirectory);

        var settingsService = new JsonSettingsService(
            NullLogger<JsonSettingsService>.Instance,
            new InMemoryErrorSink(),
            paths);
        Assert.Equal(paths.SettingsFilePath, settingsService.SettingsFilePath);

        using (var loggerProvider = new AsyncFileLoggerProvider(paths))
        {
            loggerProvider.CreateLogger("PortableMode").LogInformation(
                "Portable logging test.");
        }

        Assert.True(File.Exists(Path.Combine(
            paths.LogDirectory,
            "spotify-booster.log")));
        Assert.False(Directory.Exists(fakeLocalAppData));
    }

    [Fact]
    public void Resolve_WithoutPortableMarker_UsesSuppliedLocalAppDataRoot()
    {
        string applicationDirectory = Path.Combine(
            temporaryDirectory,
            "installed-app");
        string fakeLocalAppData = Path.Combine(
            temporaryDirectory,
            "fake-local-app-data");
        Directory.CreateDirectory(applicationDirectory);

        AppDataPaths paths = AppDataPaths.Resolve(
            applicationDirectory,
            fakeLocalAppData);

        string expectedRoot = Path.Combine(
            fakeLocalAppData,
            "SpotifyBooster");
        Assert.Equal(Path.GetFullPath(expectedRoot), paths.RootDirectory);
        Assert.Equal(
            Path.Combine(expectedRoot, "settings.json"),
            paths.SettingsFilePath);
        Assert.Equal(Path.Combine(expectedRoot, "logs"), paths.LogDirectory);

        var settingsService = new JsonSettingsService(
            NullLogger<JsonSettingsService>.Instance,
            new InMemoryErrorSink(),
            paths);
        Assert.Equal(paths.SettingsFilePath, settingsService.SettingsFilePath);

        using (var loggerProvider = new AsyncFileLoggerProvider(paths))
        {
            loggerProvider.CreateLogger("InstalledMode").LogInformation(
                "Installed logging test.");
        }

        Assert.True(File.Exists(Path.Combine(
            paths.LogDirectory,
            "spotify-booster.log")));
        Assert.False(Directory.Exists(Path.Combine(
            applicationDirectory,
            "Data")));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
