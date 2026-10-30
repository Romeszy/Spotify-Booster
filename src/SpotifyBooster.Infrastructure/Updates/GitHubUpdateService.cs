using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpotifyBooster.Infrastructure.Updates;

public sealed class GitHubUpdateService : IDisposable
{
    private const string AssetName = "SpotifyBooster-Setup-x64.exe";
    private const long MaximumInstallerSize = 300L * 1024 * 1024;
    private const int MaximumMetadataSize = 1024 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/Romeszy/Spotify-Booster/releases/latest");
    private static readonly TimeSpan MetadataRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadRequestTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly string _updatesDirectory;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public GitHubUpdateService()
        : this(CreateHttpClient(), GetDefaultUpdatesDirectory(), ownsHttpClient: true)
    {
    }

    public GitHubUpdateService(HttpClient httpClient, string updatesDirectory)
        : this(httpClient, updatesDirectory, ownsHttpClient: false)
    {
    }

    private GitHubUpdateService(HttpClient httpClient, string updatesDirectory, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatesDirectory);

        _httpClient = httpClient;
        _updatesDirectory = Path.GetFullPath(updatesDirectory);
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<GitHubRelease?> CheckAsync(Version current)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(current);

        using var timeout = new CancellationTokenSource(MetadataRequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("SpotifyBooster-Updater/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);

        ValidateMetadataResponseUri(response.RequestMessage?.RequestUri);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "Aucune release stable publique n'est disponible, ou le dépôt GitHub n'est pas accessible.");
        }

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumMetadataSize)
        {
            throw new InvalidDataException("Les métadonnées de mise à jour dépassent la taille autorisée.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var metadataStream = await ReadBoundedAsync(
            responseStream,
            MaximumMetadataSize,
            timeout.Token).ConfigureAwait(false);

        ReleaseMetadata? metadata;
        try
        {
            metadata = await JsonSerializer.DeserializeAsync<ReleaseMetadata>(
                metadataStream,
                cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Les métadonnées de mise à jour GitHub sont invalides.", exception);
        }

        if (metadata is null)
        {
            throw new InvalidDataException("Les métadonnées de mise à jour GitHub sont vides.");
        }

        if (metadata.Draft || metadata.Prerelease)
        {
            return null;
        }

        var versionText = metadata.TagName?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var releaseVersion))
        {
            throw new InvalidDataException("La version de la mise à jour GitHub est invalide.");
        }

        if (releaseVersion <= current)
        {
            return null;
        }

        var matchingAssets = metadata.Assets?
            .Where(asset => string.Equals(asset.Name, AssetName, StringComparison.Ordinal))
            .ToArray() ?? [];
        if (matchingAssets.Length != 1)
        {
            throw new InvalidDataException($"La release doit contenir exactement un asset nommé {AssetName}.");
        }

        var asset = matchingAssets[0];
        if (asset.Size is <= 0 or > MaximumInstallerSize)
        {
            throw new InvalidDataException("La taille déclarée de l'installeur est invalide.");
        }

        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var installerUri) ||
            !IsRepositoryInstallerUri(installerUri))
        {
            throw new InvalidDataException("L'URL de l'installeur ne correspond pas au dépôt GitHub attendu.");
        }

        var sha256 = ParseRequiredSha256(asset.Digest);
        return new GitHubRelease(releaseVersion, installerUri, asset.Size, sha256);
    }

    public async Task<string> DownloadAsync(GitHubRelease release)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(release);

        if (!IsRepositoryInstallerUri(release.InstallerUrl))
        {
            throw new InvalidDataException("L'URL de téléchargement ne correspond pas au dépôt GitHub attendu.");
        }

        if (release.Size is <= 0 or > MaximumInstallerSize)
        {
            throw new InvalidDataException("La taille attendue de l'installeur est invalide.");
        }

        var expectedHash = ParseRequiredSha256($"sha256:{release.Sha256}");
        Directory.CreateDirectory(_updatesDirectory);
        var stagingDirectory = Path.Combine(_updatesDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var partialPath = Path.Combine(stagingDirectory, $"{AssetName}.download");
        var installerPath = Path.Combine(stagingDirectory, AssetName);

        try
        {
            using var timeout = new CancellationTokenSource(DownloadRequestTimeout);
            using var response = await SendDownloadRequestAsync(release.InstallerUrl, timeout.Token)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength &&
                (contentLength != release.Size || contentLength > MaximumInstallerSize))
            {
                throw new InvalidDataException("La taille HTTP de l'installeur ne correspond pas aux métadonnées GitHub.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long totalBytes = 0;

            try
            {
                await using (var output = new FileStream(
                                 partialPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 useAsync: true))
                {
                    int bytesRead;
                    while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token)
                               .ConfigureAwait(false)) != 0)
                    {
                        totalBytes += bytesRead;
                        if (totalBytes > release.Size || totalBytes > MaximumInstallerSize)
                        {
                            throw new InvalidDataException("L'installeur téléchargé dépasse la taille déclarée.");
                        }

                        hash.AppendData(buffer, 0, bytesRead);
                        await output.WriteAsync(buffer.AsMemory(0, bytesRead), timeout.Token).ConfigureAwait(false);
                    }

                    await output.FlushAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (totalBytes != release.Size)
            {
                throw new InvalidDataException("La taille de l'installeur téléchargé ne correspond pas aux métadonnées GitHub.");
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(expectedHash)))
            {
                throw new InvalidDataException("L'empreinte SHA-256 de l'installeur téléchargé est invalide.");
            }

            File.Move(partialPath, installerPath);
            return installerPath;
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(Uri installerUri, CancellationToken cancellationToken)
    {
        var currentUri = installerUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.UserAgent.ParseAdd("SpotifyBooster-Updater/1.0");
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            var effectiveUri = response.RequestMessage?.RequestUri ?? currentUri;
            if (!IsAllowedDownloadUri(effectiveUri, allowRepositoryUrl: redirect == 0))
            {
                response.Dispose();
                throw new InvalidDataException("GitHub a redirigé le téléchargement vers un domaine non autorisé.");
            }

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new InvalidDataException("La redirection GitHub ne contient aucune destination.");
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(effectiveUri, location);
            if (!IsAllowedDownloadUri(currentUri, allowRepositoryUrl: false))
            {
                throw new InvalidDataException("GitHub a redirigé le téléchargement vers un domaine non autorisé.");
            }
        }

        throw new InvalidDataException("Le téléchargement GitHub contient trop de redirections.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static bool IsRepositoryInstallerUri(Uri uri)
    {
        const string expectedPrefix = "/Romeszy/Spotify-Booster/releases/download/";
        return uri.Scheme == Uri.UriSchemeHttps &&
               uri.IsDefaultPort &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal) &&
               uri.AbsolutePath.EndsWith($"/{AssetName}", StringComparison.Ordinal) &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsAllowedDownloadUri(Uri uri, bool allowRepositoryUrl)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (allowRepositoryUrl && IsRepositoryInstallerUri(uri))
        {
            return true;
        }

        return string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateMetadataResponseUri(Uri? uri)
    {
        if (uri is null)
        {
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, LatestReleaseUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("La réponse de mise à jour ne provient pas de l'API GitHub attendue.");
        }
    }

    private static string ParseRequiredSha256(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub ne fournit pas l'empreinte SHA-256 obligatoire de l'installeur.");
        }

        var hash = digest[prefix.Length..];
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("L'empreinte SHA-256 fournie par GitHub est invalide.");
        }

        return hash.ToLowerInvariant();
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        Stream input,
        int maximumSize,
        CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) != 0)
            {
                if (output.Length + bytesRead > maximumSize)
                {
                    throw new InvalidDataException("Les métadonnées de mise à jour dépassent la taille autorisée.");
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static string GetDefaultUpdatesDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpotifyBooster",
        "Updates");

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class ReleaseMetadata
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("assets")]
        public AssetMetadata[]? Assets { get; init; }
    }

    private sealed class AssetMetadata
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}
