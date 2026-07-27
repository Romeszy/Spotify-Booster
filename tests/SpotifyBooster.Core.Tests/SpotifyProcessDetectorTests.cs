using SpotifyBooster.Infrastructure.Spotify;

namespace SpotifyBooster.Core.Tests;

public sealed class SpotifyProcessDetectorTests
{
    [Fact]
    public async Task DetectionReturnsOnlySpotifyNamedProcesses()
    {
        var detector = new SpotifyProcessDetector();

        var processes = await detector.DetectAsync(
            TestContext.Current.CancellationToken);

        Assert.All(
            processes,
            process => Assert.Contains(
                "Spotify",
                process.ProcessName,
                StringComparison.OrdinalIgnoreCase));
    }
}
