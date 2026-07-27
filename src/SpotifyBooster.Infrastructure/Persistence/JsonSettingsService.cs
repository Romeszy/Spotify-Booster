using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Configuration;

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

    private readonly ILogger<JsonSettingsService> logger;
    private readonly IErrorSink errorSink;
    private readonly string storageDirectory;

    public JsonSettingsService(
        ILogger<JsonSettingsService> logger,
        IErrorSink errorSink)
        : this(logger, errorSink, null)
    {
    }

    public JsonSettingsService(
        ILogger<JsonSettingsService> logger,
        IErrorSink errorSink,
        string? testStorageDirectory)
    {
        this.logger = logger;
        this.errorSink = errorSink;

        string baseDirectory = testStorageDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        storageDirectory = Path.GetFullPath(
            testStorageDirectory is null
                ? Path.Combine(baseDirectory, "SpotifyBooster")
                : baseDirectory);
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

            await using FileStream stream = new(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            AppSettings? loaded = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                SerializerOptions,
                cancellationToken);
            SettingsValidationResult validation = SettingsValidator.Validate(loaded);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(string.Join(" ", validation.Errors));
            }

            logger.LogInformation("Configuration locale chargée et validée.");
            return validation.Settings;
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
            }

            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
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
}
