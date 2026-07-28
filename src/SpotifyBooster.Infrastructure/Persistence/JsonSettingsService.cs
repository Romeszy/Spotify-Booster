using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Infrastructure.Storage;

namespace SpotifyBooster.Infrastructure.Persistence;

public sealed class JsonSettingsService : ISettingsService
{
    private const long MaximumSettingsFileBytes = 1_048_576;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = false,
        MaxDepth = 16,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions LegacySerializerOptions = new(SerializerOptions)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<JsonSettingsService> logger;
    private readonly IErrorSink errorSink;
    private readonly string storageDirectory;

    public JsonSettingsService(
        ILogger<JsonSettingsService> logger,
        IErrorSink errorSink)
        : this(logger, errorSink, AppDataPaths.Resolve())
    {
    }

    public JsonSettingsService(
        ILogger<JsonSettingsService> logger,
        IErrorSink errorSink,
        AppDataPaths paths)
        : this(
            logger,
            errorSink,
            paths?.RootDirectory
                ?? throw new ArgumentNullException(nameof(paths)))
    {
    }

    public JsonSettingsService(
        ILogger<JsonSettingsService> logger,
        IErrorSink errorSink,
        string? testStorageDirectory)
    {
        this.logger = logger;
        this.errorSink = errorSink;

        storageDirectory = Path.GetFullPath(
            testStorageDirectory
            ?? throw new ArgumentNullException(nameof(testStorageDirectory)));
        SettingsFilePath = Path.Combine(storageDirectory, "settings.json");
    }

    public string SettingsFilePath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var fileInfo = new FileInfo(SettingsFilePath);
            if (fileInfo.Length <= 0 || fileInfo.Length > MaximumSettingsFileBytes)
            {
                throw new InvalidDataException("La taille du fichier de configuration est invalide.");
            }

            JsonDocument document;
            await using (var stream = new FileStream(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 16,
                    },
                    cancellationToken);
            }

            using (document)
            {
                int schemaVersion = ReadSchemaVersion(document.RootElement);
                AppSettings? loaded;
                if (schemaVersion == 1)
                {
                    LegacyAppSettingsV1? legacy =
                        document.RootElement.Deserialize<LegacyAppSettingsV1>(
                            LegacySerializerOptions);
                    if (legacy?.Processing is null
                        || legacy.Profiles is null
                        || legacy.Profiles.Any(profile =>
                            profile is null || profile.Processing is null))
                    {
                        throw new InvalidDataException(
                            "La configuration héritée contient un nœud nul.");
                    }

                    loaded = SettingsV1Migration.Migrate(legacy);
                    if (loaded is not null)
                    {
                        await BackupBeforeMigrationAsync(1, cancellationToken);
                        await SaveAsync(loaded, cancellationToken);
                        logger.LogInformation(
                            "Configuration migrée du schéma 1 vers le schéma {SchemaVersion}.",
                            AppSettings.CurrentSchemaVersion);
                    }
                }
                else if (schemaVersion == 2)
                {
                    LegacyAppSettingsV2? legacy =
                        document.RootElement.Deserialize<LegacyAppSettingsV2>(
                            SerializerOptions);
                    loaded = legacy?.Migrate()
                        ?? throw new InvalidDataException(
                            "La configuration V2 doit être un objet.");
                    await BackupBeforeMigrationAsync(2, cancellationToken);
                    await SaveAsync(loaded, cancellationToken);
                    logger.LogInformation(
                        "Configuration migrée du schéma 2 vers le schéma {SchemaVersion}.",
                        AppSettings.CurrentSchemaVersion);
                }
                else
                {
                    if (ContainsObsoleteV3Property(document.RootElement))
                    {
                        throw new InvalidDataException(
                            "ExpertModeAcknowledged est obsolète dans le schéma V3.");
                    }

                    loaded = document.RootElement.Deserialize<AppSettings>(
                        SerializerOptions);
                }

                SettingsValidationResult validation = SettingsValidator.Validate(loaded);
                if (!validation.IsValid)
                {
                    throw new InvalidDataException(string.Join(" ", validation.Errors));
                }

                logger.LogInformation("Configuration locale chargée et validée.");
                return validation.Settings;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException)
        {
            const string message = "Configuration invalide ou illisible ; paramètres sûrs par défaut utilisés.";
            logger.LogWarning(exception, "{Message}", message);
            errorSink.Report("Configuration", message);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SettingsValidationResult validation = SettingsValidator.Validate(settings);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        }

        Directory.CreateDirectory(storageDirectory);
        string temporaryPath = SettingsFilePath + ".tmp";

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    validation.Settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
            await ReadStrictV3Async(cancellationToken);
            logger.LogDebug("Configuration locale sauvegardée.");
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("La racine de configuration doit être un objet JSON.");
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                nameof(AppSettings.SchemaVersion),
                StringComparison.OrdinalIgnoreCase)
                && property.Value.TryGetInt32(out int version))
            {
                return version;
            }
        }

        throw new InvalidDataException("La version de configuration est absente.");
    }

    private static bool ContainsObsoleteV3Property(JsonElement root)
    {
        if (root.TryGetProperty("ExpertModeAcknowledged", out _)
            || root.TryGetProperty("Processing", out JsonElement processing)
                && processing.ValueKind == JsonValueKind.Object
                && processing.TryGetProperty("ExpertMaximumGainDb", out _))
        {
            return true;
        }

        if (!root.TryGetProperty("Profiles", out JsonElement profiles)
            || profiles.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement profile in profiles.EnumerateArray())
        {
            if (profile.ValueKind == JsonValueKind.Object
                && profile.TryGetProperty("Processing", out JsonElement profileProcessing)
                && profileProcessing.ValueKind == JsonValueKind.Object
                && profileProcessing.TryGetProperty("ExpertMaximumGainDb", out _))
            {
                return true;
            }
        }

        return false;
    }

    private async Task BackupBeforeMigrationAsync(
        int schemaVersion,
        CancellationToken cancellationToken)
    {
        string backupPath = Path.Combine(
            storageDirectory,
            $"settings.schema{schemaVersion}.backup.json");
        if (File.Exists(backupPath))
        {
            await VerifyFilesEqualAsync(
                SettingsFilePath,
                backupPath,
                cancellationToken);
            return;
        }

        string temporaryBackupPath = backupPath + ".tmp";
        try
        {
            await using (FileStream source = OpenRead(SettingsFilePath))
            await using (FileStream destination = new(
                temporaryBackupPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            await VerifyFilesEqualAsync(
                SettingsFilePath,
                temporaryBackupPath,
                cancellationToken);
            File.Move(temporaryBackupPath, backupPath);
            await VerifyFilesEqualAsync(
                SettingsFilePath,
                backupPath,
                cancellationToken);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryBackupPath);
            throw;
        }
    }

    private async Task<AppSettings> ReadStrictV3Async(
        CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenRead(SettingsFilePath);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            },
            cancellationToken);
        if (ReadSchemaVersion(document.RootElement)
            != AppSettings.CurrentSchemaVersion
            || ContainsObsoleteV3Property(document.RootElement))
        {
            throw new InvalidDataException(
                "La configuration sauvegardée n'est pas un schéma V3 strict.");
        }

        AppSettings? reloaded = document.RootElement.Deserialize<AppSettings>(
            SerializerOptions);
        SettingsValidationResult validation = SettingsValidator.Validate(reloaded);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        }

        return validation.Settings;
    }

    private static async Task VerifyFilesEqualAsync(
        string expectedPath,
        string actualPath,
        CancellationToken cancellationToken)
    {
        await using FileStream expected = OpenRead(expectedPath);
        await using FileStream actual = OpenRead(actualPath);
        if (expected.Length != actual.Length)
        {
            throw new InvalidDataException(
                "La sauvegarde de migration existante ne correspond pas au fichier source.");
        }

        byte[] expectedBuffer = new byte[16_384];
        byte[] actualBuffer = new byte[16_384];
        while (true)
        {
            int expectedRead = await expected.ReadAsync(
                expectedBuffer,
                cancellationToken);
            int actualRead = await actual.ReadAsync(
                actualBuffer,
                cancellationToken);
            if (expectedRead != actualRead
                || !expectedBuffer.AsSpan(0, expectedRead).SequenceEqual(
                    actualBuffer.AsSpan(0, actualRead)))
            {
                throw new InvalidDataException(
                    "La sauvegarde de migration existante ne correspond pas au fichier source.");
            }

            if (expectedRead == 0)
            {
                return;
            }
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 16_384,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
}
