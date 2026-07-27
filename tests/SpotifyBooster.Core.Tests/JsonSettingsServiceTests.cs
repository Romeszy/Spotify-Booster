using Microsoft.Extensions.Logging.Abstractions;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Infrastructure.Diagnostics;
using SpotifyBooster.Infrastructure.Persistence;

namespace SpotifyBooster.Core.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpotifyBooster.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly InMemoryErrorSink errorSink = new();

    [Fact]
    public async Task SaveAndLoad_RoundTripsProfiles()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            FirstRunCompleted = true,
            SourceDeviceId = "capture-id",
            OutputDeviceId = "render-id",
            Processing = new AudioProcessingSettings
            {
                GainPercent = 250,
                BassGainDb = 4.5,
            },
            Profiles =
            [
                new AudioProfileSettings
                {
                    Name = "Test",
                    Processing = new AudioProcessingSettings
                    {
                        GainPercent = 175,
                    },
                },
            ],
        };

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(settings, cancellationToken);
        AppSettings loaded = await service.LoadAsync(cancellationToken);

        Assert.Equal(settings.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(settings.FirstRunCompleted, loaded.FirstRunCompleted);
        Assert.Equal(settings.SourceDeviceId, loaded.SourceDeviceId);
        Assert.Equal(settings.OutputDeviceId, loaded.OutputDeviceId);
        Assert.Equal(settings.Processing, loaded.Processing);
        Assert.Single(loaded.Profiles);
        Assert.Equal("Test", loaded.Profiles[0].Name);
        Assert.Equal(175, loaded.Profiles[0].Processing.GainPercent);
    }

    [Fact]
    public async Task InvalidJson_ReturnsSafeDefaultsAndReportsError()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            "{ not-valid-json }",
            TestContext.Current.CancellationToken);
        var service = CreateService();

        AppSettings loaded = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(100, loaded.Processing.GainPercent);
        Assert.True(loaded.Processing.LimiterEnabled);
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Fact]
    public async Task UnknownJsonProperty_IsRejected()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            """
            {
              "schemaVersion": 1,
              "unexpected": "value"
            }
            """,
            TestContext.Current.CancellationToken);
        var service = CreateService();

        AppSettings loaded = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(loaded.FirstRunCompleted);
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"processing":null}""")]
    [InlineData("""{"schemaVersion":1,"profiles":null}""")]
    [InlineData("""{"schemaVersion":1,"profiles":[null]}""")]
    [InlineData("""{"schemaVersion":1,"profiles":[{"name":"Broken","processing":null}]}""")]
    public async Task NullConfigurationNodes_ReturnSafeDefaultsAndReportError(string json)
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            json,
            TestContext.Current.CancellationToken);
        var service = CreateService();

        AppSettings loaded = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(loaded.FirstRunCompleted);
        Assert.Equal(100, loaded.Processing.GainPercent);
        Assert.True(loaded.Processing.LimiterEnabled);
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Fact]
    public async Task Save_InvalidConfigurationThrowsWithoutReplacingExistingFile()
    {
        var service = CreateService();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(
            new AppSettings { FirstRunCompleted = true },
            cancellationToken);
        string original = await File.ReadAllTextAsync(
            service.SettingsFilePath,
            cancellationToken);

        var invalid = new AppSettings
        {
            FirstRunCompleted = true,
            Processing = new AudioProcessingSettings
            {
                GainPercent = 900,
            },
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.SaveAsync(invalid, cancellationToken));
        string afterFailure = await File.ReadAllTextAsync(
            service.SettingsFilePath,
            cancellationToken);
        Assert.Equal(original, afterFailure);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private JsonSettingsService CreateService()
    {
        return new(
            NullLogger<JsonSettingsService>.Instance,
            errorSink,
            temporaryDirectory);
    }
}
