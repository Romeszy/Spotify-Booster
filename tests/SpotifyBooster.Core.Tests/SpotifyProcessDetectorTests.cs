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
            process => Assert.True(
                SpotifyProcessDetector.IsSpotifyProcessName(
                    process.ProcessName)));
    }

    [Theory]
    [InlineData("Spotify", true)]
    [InlineData("SPOTIFY", true)]
    [InlineData("SpotifyAB.SpotifyMusic", true)]
    [InlineData("SpotifyAB.SpotifyMusic_1.250.0", true)]
    [InlineData("SpotifyBooster.App", false)]
    [InlineData("FakeSpotify", false)]
    [InlineData("SpotifyHelper", false)]
    public void ProcessNameFilter_DoesNotConfuseBoosterWithSpotify(
        string processName,
        bool expected)
    {
        Assert.Equal(
            expected,
            SpotifyProcessDetector.IsSpotifyProcessName(processName));
    }
}
