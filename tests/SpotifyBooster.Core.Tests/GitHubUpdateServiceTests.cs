using System.Net;
using System.Security.Cryptography;
using System.Text;
using SpotifyBooster.Infrastructure.Updates;

namespace SpotifyBooster.Core.Tests;

public sealed class GitHubUpdateServiceTests
{
    private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task CheckAsync_ReturnsNewStableInstallerMetadata()
    {
        using var service = CreateService(_ => JsonResponse(CreateReleaseJson(
            version: "v2.1.0",
            size: 42,
            digest: $"sha256:{ValidHash}",
            downloadUrl: "https://github.com/Romeszy/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe")));

        var release = await service.CheckAsync(new Version(2, 0, 0));

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 1, 0), release.Version);
        Assert.Equal(42, release.Size);
        Assert.Equal(ValidHash, release.Sha256);
        Assert.Equal(
            "https://github.com/Romeszy/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe",
            release.InstallerUrl.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAsync_WhenReleaseIsNotNewer_ReturnsNull()
    {
        using var service = CreateService(_ => JsonResponse(CreateReleaseJson(
            version: "v2.1.0",
            size: 42,
            digest: $"sha256:{ValidHash}",
            downloadUrl: "https://github.com/Romeszy/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe")));

        var release = await service.CheckAsync(new Version(2, 1, 0));

        Assert.Null(release);
    }

    [Fact]
    public async Task CheckAsync_WhenMetadataIsNull_RefusesResponse()
    {
        using var service = CreateService(_ => JsonResponse("null"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CheckAsync(new Version(2, 0, 0)));
    }

    [Fact]
    public async Task CheckAsync_WhenNoPublicStableReleaseExists_ReportsUnavailableRelease()
    {
        using var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAsync(new Version(2, 0, 0)));

        Assert.Contains("Aucune release stable publique", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_WhenDigestIsMissing_RefusesRelease()
    {
        using var service = CreateService(_ => JsonResponse(CreateReleaseJson(
            version: "v2.1.0",
            size: 42,
            digest: null,
            downloadUrl: "https://github.com/Romeszy/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe")));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CheckAsync(new Version(2, 0, 0)));

        Assert.Contains("SHA-256 obligatoire", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_WhenInstallerUrlTargetsAnotherRepository_RefusesRelease()
    {
        using var service = CreateService(_ => JsonResponse(CreateReleaseJson(
            version: "v2.1.0",
            size: 42,
            digest: $"sha256:{ValidHash}",
            downloadUrl: "https://github.com/attacker/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe")));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CheckAsync(new Version(2, 0, 0)));
    }

    [Fact]
    public async Task CheckAsync_WhenInstallerUrlUsesNonDefaultHttpsPort_RefusesRelease()
    {
        using var service = CreateService(_ => JsonResponse(CreateReleaseJson(
            version: "v2.1.0",
            size: 42,
            digest: $"sha256:{ValidHash}",
            downloadUrl: "https://github.com:444/Romeszy/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe")));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CheckAsync(new Version(2, 0, 0)));
    }

    [Fact]
    public async Task CheckAsync_WhenInstallerExceedsMaximumSize_RefusesRelease()
    {
        using var service = CreateService(_ => JsonResponse(CreateReleaseJson(
            version: "v2.1.0",
            size: 300L * 1024 * 1024 + 1,
            digest: $"sha256:{ValidHash}",
            downloadUrl: "https://github.com/Romeszy/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe")));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CheckAsync(new Version(2, 0, 0)));
    }

    [Fact]
    public async Task DownloadAsync_WhenSizeAndHashMatch_ReturnsStagedInstaller()
    {
        var payload = Encoding.UTF8.GetBytes("verified installer payload");
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        using var temporaryDirectory = new TemporaryDirectory();
        using var service = CreateService(
            _ => BinaryResponse(payload),
            temporaryDirectory.Path);
        var release = CreateRelease(payload.LongLength, hash);

        var installerPath = await service.DownloadAsync(release);

        Assert.Equal("SpotifyBooster-Setup-x64.exe", Path.GetFileName(installerPath));
        Assert.Equal(
            payload,
            await File.ReadAllBytesAsync(installerPath, TestContext.Current.CancellationToken));
        Assert.StartsWith(temporaryDirectory.Path, installerPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsync_WhenHashDoesNotMatch_RefusesAndRemovesStaging()
    {
        var payload = Encoding.UTF8.GetBytes("tampered installer payload");
        using var temporaryDirectory = new TemporaryDirectory();
        using var service = CreateService(
            _ => BinaryResponse(payload),
            temporaryDirectory.Path);
        var release = CreateRelease(payload.LongLength, ValidHash);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release));

        Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryDirectory.Path));
    }

    [Fact]
    public async Task DownloadAsync_WhenDownloadedSizeDiffers_RefusesAndRemovesStaging()
    {
        var payload = Encoding.UTF8.GetBytes("short payload");
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        using var temporaryDirectory = new TemporaryDirectory();
        using var service = CreateService(
            _ => BinaryResponse(payload),
            temporaryDirectory.Path);
        var release = CreateRelease(payload.LongLength + 1, hash);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release));

        Assert.Empty(Directory.EnumerateFileSystemEntries(temporaryDirectory.Path));
    }

    [Fact]
    public async Task DownloadAsync_WhenRedirectTargetsUntrustedHost_RefusesDownload()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var service = CreateService(
            _ => new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/installer.exe") },
            },
            temporaryDirectory.Path);
        var release = CreateRelease(42, ValidHash);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release));
    }

    private static GitHubRelease CreateRelease(long size, string hash) => new(
        new Version(2, 1, 0),
        new Uri("https://github.com/Romeszy/Spotify-Booster/releases/download/v2.1.0/SpotifyBooster-Setup-x64.exe"),
        size,
        hash);

    private static GitHubUpdateService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        string? updatesDirectory = null)
    {
        var client = new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new GitHubUpdateService(
            client,
            updatesDirectory ?? Path.Combine(Path.GetTempPath(), "SpotifyBoosterTests", Guid.NewGuid().ToString("N")));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage BinaryResponse(byte[] payload) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(payload),
    };

    private static string CreateReleaseJson(string version, long size, string? digest, string downloadUrl)
    {
        var digestJson = digest is null ? "null" : $"\"{digest}\"";
        return $$"""
            {
              "tag_name": "{{version}}",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "SpotifyBooster-Setup-x64.exe",
                  "size": {{size}},
                  "digest": {{digestJson}},
                  "browser_download_url": "{{downloadUrl}}"
                }
              ]
            }
            """;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = responseFactory(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SpotifyBoosterTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
