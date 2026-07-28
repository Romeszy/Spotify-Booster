using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Infrastructure.Diagnostics;
using SpotifyBooster.Infrastructure.Logging;
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
    public async Task SaveAndLoad_RoundTripsV2ModesProfilesAndHotkeys()
    {
        var service = CreateService();
        var settings = new AppSettings
        {
            FirstRunCompleted = true,
            Expert75DbAcknowledged = true,
            SourceDeviceId = "capture-id",
            OutputDeviceId = "render-id",
            CloseBehavior = WindowCloseBehavior.Ask,
            Theme = InterfaceTheme.System,
            Density = InterfaceDensity.Comfortable,
            Hotkeys = new HotkeySettings
            {
                GlobalHotkeysEnabled = true,
                IncreaseGain = "Ctrl+Shift+1",
                DecreaseGain = "Ctrl+Shift+2",
                ToggleProcessing = "Ctrl+Shift+3",
                PanicReset = "Ctrl+Shift+4",
                ToggleBassBoost = "Ctrl+Shift+5",
                ToggleWindow = "Ctrl+Shift+6",
            },
            Processing = RawProcessing(25) with
            {
                BassFrequencyHz = 35,
                BassGainDb = 18,
            },
            Profiles =
            [
                new AudioProfileSettings
                {
                    Name = "Direct",
                    Processing = DirectProcessing(13.9794),
                },
                new AudioProfileSettings
                {
                    Name = "Raw 25",
                    Processing = RawProcessing(25),
                },
            ],
        };

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(settings, cancellationToken);
        AppSettings loaded = await service.LoadAsync(cancellationToken);

        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(settings.FirstRunCompleted, loaded.FirstRunCompleted);
        Assert.Equal(settings.Expert75DbAcknowledged, loaded.Expert75DbAcknowledged);
        Assert.Equal(settings.SourceDeviceId, loaded.SourceDeviceId);
        Assert.Equal(settings.OutputDeviceId, loaded.OutputDeviceId);
        Assert.Equal(settings.CloseBehavior, loaded.CloseBehavior);
        Assert.Equal(settings.Theme, loaded.Theme);
        Assert.Equal(settings.Density, loaded.Density);
        Assert.Equal(settings.Hotkeys, loaded.Hotkeys);
        Assert.Equal(settings.Processing, loaded.Processing);
        Assert.Equal(settings.Profiles.ToArray(), loaded.Profiles.ToArray());
        Assert.Equal(AudioProtectionMode.Direct, loaded.Profiles[0].Processing.Mode);
        Assert.Equal(13.9794, loaded.Profiles[0].Processing.MainGainDb);
        Assert.Equal(AudioProtectionMode.RawExpert, loaded.Profiles[1].Processing.Mode);
        Assert.Equal(25, loaded.Profiles[1].Processing.MainGainDb);
    }

    [Theory]
    [InlineData(100, 0d)]
    [InlineData(200, 6.020599913d)]
    [InlineData(500, 13.979400087d)]
    public async Task Schema1MigrationCreatesBackupAndPreservesGainExactly(
        int gainPercent,
        double expectedGainDecibels)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        string legacyJson =
            $$"""
            {
              "schemaVersion": 1,
              "firstRunCompleted": true,
              "sourceDeviceId": "legacy-capture",
              "outputDeviceId": "legacy-render",
              "minimizeToTray": false,
              "processing": {
                "gainPercent": {{gainPercent}},
                "dcBlockEnabled": true,
                "bassBoostEnabled": false,
                "bassFrequencyHz": 90,
                "bassGainDb": 3,
                "compressorEnabled": false,
                "limiterEnabled": true,
                "limiterThresholdDb": -1,
                "limiterReleaseMs": 80,
                "parameterSmoothingMs": 20
              },
              "profiles": [
                {
                  "name": "Legacy {{gainPercent}}",
                  "processing": {
                    "gainPercent": {{gainPercent}},
                    "compressorEnabled": false,
                    "limiterEnabled": true
                  }
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(
            settingsPath,
            legacyJson,
            TestContext.Current.CancellationToken);
        var service = CreateService();

        AppSettings migrated = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(expectedGainDecibels, migrated.Processing.MainGainDb, 8);
        Assert.Equal(expectedGainDecibels, migrated.Profiles[0].Processing.MainGainDb, 8);
        Assert.Equal(AudioProtectionMode.Protected, migrated.Processing.Mode);
        Assert.Equal(WindowCloseBehavior.Exit, migrated.CloseBehavior);

        string backupPath = Path.Combine(
            temporaryDirectory,
            "settings.schema1.backup.json");
        Assert.True(File.Exists(backupPath));
        Assert.Equal(
            legacyJson,
            await File.ReadAllTextAsync(
                backupPath,
                TestContext.Current.CancellationToken));

        using JsonDocument rewritten = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                settingsPath,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            AppSettings.CurrentSchemaVersion,
            rewritten.RootElement.GetProperty("SchemaVersion").GetInt32());

        AppSettings roundTripped = await service.LoadAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            migrated with { Profiles = roundTripped.Profiles },
            roundTripped);
        Assert.Equal(
            migrated.Profiles.ToArray(),
            roundTripped.Profiles.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Schema2MigrationQuantizesOnlyRawExpertAndCreatesExactBackup(
        bool oldAcknowledgement)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        string schema2 =
            $$"""
            {
              "SchemaVersion": 2,
              "FirstRunCompleted": true,
              "ExpertModeAcknowledged": {{oldAcknowledgement.ToString().ToLowerInvariant()}},
              "Processing": {
                "Mode": 2,
                "MainGainDb": 74.6,
                "ExpertMaximumGainDb": 1000,
                "CompressorEnabled": false,
                "SoftClipperEnabled": false,
                "LimiterEnabled": false
              },
              "Profiles": [
                {
                  "Name": "Direct precise",
                  "Processing": {
                    "Mode": 1,
                    "MainGainDb": 10.1,
                    "ExpertMaximumGainDb": 1000,
                    "CompressorEnabled": false,
                    "SoftClipperEnabled": false,
                    "LimiterEnabled": false
                  }
                },
                {
                  "Name": "Raw clamped",
                  "Processing": {
                    "Mode": 2,
                    "MainGainDb": 1000,
                    "ExpertMaximumGainDb": 1000,
                    "CompressorEnabled": false,
                    "SoftClipperEnabled": false,
                    "LimiterEnabled": false
                  }
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(
            settingsPath,
            schema2,
            TestContext.Current.CancellationToken);

        AppSettings migrated = await CreateService().LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(3, migrated.SchemaVersion);
        Assert.False(migrated.Expert75DbAcknowledged);
        Assert.Equal(74.5, migrated.Processing.MainGainDb);
        Assert.Equal(10.1, migrated.Profiles[0].Processing.MainGainDb);
        Assert.Equal(75, migrated.Profiles[1].Processing.MainGainDb);
        Assert.Equal(
            schema2,
            await File.ReadAllTextAsync(
                Path.Combine(temporaryDirectory, "settings.schema2.backup.json"),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("{")]
    public async Task Schema2MigrationRefusesMismatchedExistingBackup(
        string existingBackup)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        const string schema2 =
            """{"SchemaVersion":2,"Processing":{"Mode":2,"MainGainDb":25,"ExpertMaximumGainDb":1000},"Profiles":[]}""";
        await File.WriteAllTextAsync(
            settingsPath,
            schema2,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.schema2.backup.json"),
            existingBackup,
            TestContext.Current.CancellationToken);

        AppSettings loaded = await CreateService().LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(loaded.FirstRunCompleted);
        Assert.Equal(
            schema2,
            await File.ReadAllTextAsync(
                settingsPath,
                TestContext.Current.CancellationToken));
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Fact]
    public async Task Schema2MigrationAcceptsExactExistingBackupForRecovery()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        const string schema2 =
            """{"SchemaVersion":2,"FirstRunCompleted":true,"ExpertModeAcknowledged":false,"Processing":{"Mode":2,"MainGainDb":25,"ExpertMaximumGainDb":1000},"Profiles":[]}""";
        string backupPath = Path.Combine(
            temporaryDirectory,
            "settings.schema2.backup.json");
        await File.WriteAllTextAsync(
            settingsPath,
            schema2,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            backupPath,
            schema2,
            TestContext.Current.CancellationToken);

        AppSettings loaded = await CreateService().LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.True(loaded.FirstRunCompleted);
        Assert.Equal(25, loaded.Processing.MainGainDb);
        Assert.False(loaded.Expert75DbAcknowledged);
        Assert.Equal(
            schema2,
            await File.ReadAllTextAsync(
                backupPath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Schema2MigrationIsIdempotentAfterV3Rewrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        const string schema2 =
            """{"SchemaVersion":2,"FirstRunCompleted":true,"ExpertModeAcknowledged":true,"Processing":{"Mode":2,"MainGainDb":74.6,"ExpertMaximumGainDb":1000,"CompressorEnabled":false,"SoftClipperEnabled":false,"LimiterEnabled":false},"Profiles":[]}""";
        await File.WriteAllTextAsync(
            settingsPath,
            schema2,
            TestContext.Current.CancellationToken);
        var service = CreateService();

        AppSettings first = await service.LoadAsync(TestContext.Current.CancellationToken);
        byte[] firstV3 = await File.ReadAllBytesAsync(
            settingsPath,
            TestContext.Current.CancellationToken);
        byte[] firstBackup = await File.ReadAllBytesAsync(
            Path.Combine(temporaryDirectory, "settings.schema2.backup.json"),
            TestContext.Current.CancellationToken);

        AppSettings second = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(first with { Profiles = second.Profiles }, second);
        Assert.Equal(first.Profiles.ToArray(), second.Profiles.ToArray());
        Assert.Equal(74.5, second.Processing.MainGainDb);
        Assert.False(second.Expert75DbAcknowledged);
        Assert.Equal(
            firstV3,
            await File.ReadAllBytesAsync(
                settingsPath,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            firstBackup,
            await File.ReadAllBytesAsync(
                Path.Combine(temporaryDirectory, "settings.schema2.backup.json"),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Schema3RejectsObsoleteExpertModeAcknowledgedWithoutRewritingSource()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        const string invalidV3 =
            """{"SchemaVersion":3,"FirstRunCompleted":true,"Expert75DbAcknowledged":true,"ExpertModeAcknowledged":true,"Processing":{"Mode":1,"MainGainDb":10.1,"CompressorEnabled":false,"SoftClipperEnabled":false,"LimiterEnabled":false},"Profiles":[]}""";
        await File.WriteAllTextAsync(
            settingsPath,
            invalidV3,
            TestContext.Current.CancellationToken);

        AppSettings loaded = await CreateService().LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(loaded.FirstRunCompleted);
        Assert.False(loaded.Expert75DbAcknowledged);
        Assert.Equal(
            invalidV3,
            await File.ReadAllTextAsync(
                settingsPath,
                TestContext.Current.CancellationToken));
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Fact]
    public async Task BackupWriteFailureLeavesSchema2SourceUntouched()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        const string schema2 =
            """{"SchemaVersion":2,"Processing":{"Mode":2,"MainGainDb":25,"ExpertMaximumGainDb":1000},"Profiles":[]}""";
        await File.WriteAllTextAsync(
            settingsPath,
            schema2,
            TestContext.Current.CancellationToken);
        string backupPath = Path.Combine(
            temporaryDirectory,
            "settings.schema2.backup.json");
        Directory.CreateDirectory(backupPath + ".tmp");

        AppSettings loaded = await CreateService().LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(loaded.FirstRunCompleted);
        Assert.Equal(
            schema2,
            await File.ReadAllTextAsync(
                settingsPath,
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(backupPath));
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Fact]
    public async Task SaveWriteFailureDoesNotReplaceExistingSettings()
    {
        var service = CreateService();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AppSettings originalSettings = new()
        {
            FirstRunCompleted = true,
            Processing = DirectProcessing(0),
            Profiles = [],
        };
        await service.SaveAsync(originalSettings, cancellationToken);
        string original = await File.ReadAllTextAsync(
            service.SettingsFilePath,
            cancellationToken);
        Directory.CreateDirectory(service.SettingsFilePath + ".tmp");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.SaveAsync(
                originalSettings with
                {
                    SourceDeviceId = "must-not-be-written",
                },
                cancellationToken));

        Assert.Equal(
            original,
            await File.ReadAllTextAsync(
                service.SettingsFilePath,
                cancellationToken));
    }

    [Fact]
    public async Task StrictTemporaryReloadFailurePreservesExistingSettingsByteForByte()
    {
        var service = CreateService();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AppSettings originalSettings = new()
        {
            FirstRunCompleted = true,
            SourceDeviceId = "original-device",
            Processing = DirectProcessing(0),
            Profiles = [],
        };
        await service.SaveAsync(originalSettings, cancellationToken);
        byte[] originalBytes = await File.ReadAllBytesAsync(
            service.SettingsFilePath,
            cancellationToken);
        var faultedService = new JsonSettingsService(
            NullLogger<JsonSettingsService>.Instance,
            errorSink,
            temporaryDirectory,
            static (temporaryPath, token) =>
                File.WriteAllTextAsync(temporaryPath, "{", token));

        await Assert.ThrowsAnyAsync<JsonException>(
            () => faultedService.SaveAsync(
                originalSettings with
                {
                    SourceDeviceId = "must-not-replace-original",
                },
                cancellationToken));

        Assert.Equal(
            originalBytes,
            await File.ReadAllBytesAsync(
                service.SettingsFilePath,
                cancellationToken));
        Assert.False(File.Exists(service.SettingsFilePath + ".tmp"));
    }

    [Fact]
    public async Task InvalidJson_ReturnsSafeV2DefaultsAndReportsError()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            "{ not-valid-json }",
            TestContext.Current.CancellationToken);
        var service = CreateService();

        AppSettings loaded = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(0, loaded.Processing.MainGainDb);
        Assert.Equal(AudioProtectionMode.Protected, loaded.Processing.Mode);
        Assert.True(loaded.Processing.SoftClipperEnabled);
        Assert.True(loaded.Processing.LimiterEnabled);
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Fact]
    public async Task InvalidProfileNameIsAbsentFromFileLogAndErrorSink()
    {
        const string sensitiveProfileName = "PERSONAL-NAME-DO-NOT-LOG";
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "settings.json");
        AppSettings invalidSettings = new()
        {
            FirstRunCompleted = true,
            Processing = DirectProcessing(0),
            Profiles =
            [
                new AudioProfileSettings
                {
                    Name = sensitiveProfileName,
                    Processing = DirectProcessing(0),
                },
                new AudioProfileSettings
                {
                    Name = sensitiveProfileName,
                    Processing = RawProcessing(75.5),
                },
            ],
        };
        await File.WriteAllTextAsync(
            settingsPath,
            JsonSerializer.Serialize(invalidSettings),
            TestContext.Current.CancellationToken);

        string logDirectory = Path.Combine(temporaryDirectory, "logs");
        using var provider = new AsyncFileLoggerProvider(
            logDirectory,
            maximumLogFileBytes: 1_048_576);
        using (ILoggerFactory loggerFactory = LoggerFactory.Create(
            builder => builder.AddProvider(provider)))
        {
            var service = new JsonSettingsService(
                loggerFactory.CreateLogger<JsonSettingsService>(),
                errorSink,
                temporaryDirectory);

            AppSettings loaded = await service.LoadAsync(
                TestContext.Current.CancellationToken);

            Assert.False(loaded.FirstRunCompleted);
        }
        provider.Dispose();

        string logText = await File.ReadAllTextAsync(
            Path.Combine(logDirectory, "spotify-booster.log"),
            TestContext.Current.CancellationToken);
        string errorText = string.Join(
            " ",
            errorSink.GetRecent().Select(
                error => $"{error.Component} {error.Message}"));

        Assert.Contains("Catégorie : validation.", logText, StringComparison.Ordinal);
        Assert.Contains("paramètres sûrs par défaut", errorText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveProfileName, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveProfileName, errorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownJsonPropertyIsRejected()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            """
            {
              "SchemaVersion": 2,
              "Unexpected": "value"
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
    [InlineData("""{"SchemaVersion":2,"Processing":null}""")]
    [InlineData("""{"SchemaVersion":2,"Profiles":null}""")]
    [InlineData("""{"SchemaVersion":2,"Hotkeys":null}""")]
    [InlineData("""{"SchemaVersion":2,"Profiles":[null]}""")]
    [InlineData("""{"SchemaVersion":2,"Profiles":[{"Name":"Broken","Processing":null}]}""")]
    public async Task NullConfigurationNodesReturnSafeDefaultsAndReportError(
        string json)
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
        Assert.Equal(0, loaded.Processing.MainGainDb);
        Assert.True(loaded.Processing.LimiterEnabled);
        Assert.NotEmpty(errorSink.GetRecent());
    }

    [Fact]
    public async Task Schema1NullProcessingReturnsSafeDefaultsWithoutBackup()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "settings.json"),
            """{"schemaVersion":1,"firstRunCompleted":true,"processing":null}""",
            TestContext.Current.CancellationToken);
        var service = CreateService();

        AppSettings loaded = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(loaded.FirstRunCompleted);
        Assert.Equal(0, loaded.Processing.MainGainDb);
        Assert.NotEmpty(errorSink.GetRecent());
        Assert.False(File.Exists(Path.Combine(
            temporaryDirectory,
            "settings.schema1.backup.json")));
    }

    [Fact]
    public async Task SaveInvalidConfigurationThrowsWithoutReplacingExistingFile()
    {
        var service = CreateService();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(
            new AppSettings
            {
                FirstRunCompleted = true,
                Processing = DirectProcessing(0),
                Profiles = [],
            },
            cancellationToken);
        string original = await File.ReadAllTextAsync(
            service.SettingsFilePath,
            cancellationToken);

        var invalid = new AppSettings
        {
            FirstRunCompleted = true,
            Processing = RawProcessing(25) with
            {
                MainGainDb = 75.5,
            },
            Profiles = [],
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

    private static AudioProcessingSettings DirectProcessing(double gainDecibels) =>
        new()
        {
            Mode = AudioProtectionMode.Direct,
            MainGainDb = gainDecibels,
            CompressorEnabled = false,
            SoftClipperEnabled = false,
            LimiterEnabled = false,
        };

    private static AudioProcessingSettings RawProcessing(double gainDecibels) =>
        DirectProcessing(gainDecibels) with
        {
            Mode = AudioProtectionMode.RawExpert,
        };
}
