using SpotifyBooster.Core.Models;
using SpotifyBooster.Infrastructure.Windows;

namespace SpotifyBooster.Core.Tests;

public sealed class AudioApplicationProcessDetectorTests
{
    public static TheoryData<string, AudioApplicationKind> KnownApplications => new()
    {
        { "Spotify", AudioApplicationKind.Spotify },
        { "SpotifyAB.SpotifyMusic", AudioApplicationKind.Spotify },
        { "SpotifyAB.SpotifyMusic_1.250.0", AudioApplicationKind.Spotify },
        { "Discord", AudioApplicationKind.Discord },
        { "DiscordPTB", AudioApplicationKind.Discord },
        { "DiscordCanary", AudioApplicationKind.Discord },
        { "chrome", AudioApplicationKind.Chrome },
        { "msedge", AudioApplicationKind.Edge },
        { "opera", AudioApplicationKind.OperaGx },
        { "firefox", AudioApplicationKind.Firefox },
        { "brave", AudioApplicationKind.Brave },
    };

    [Theory]
    [MemberData(nameof(KnownApplications))]
    public void ClassifierMatchesKnownApplicationAllowlist(
        string processName,
        AudioApplicationKind expectedKind)
    {
        HashSet<AudioApplicationKind> selectedKinds = Enum
            .GetValues<AudioApplicationKind>()
            .ToHashSet();

        bool matched = AudioApplicationProcessDetector.TryClassifyProcessName(
            processName,
            selectedKinds,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            out AudioApplicationKind actualKind);

        Assert.True(matched);
        Assert.Equal(expectedKind, actualKind);
    }

    [Theory]
    [InlineData("DiscordHelper")]
    [InlineData("chrome_proxy")]
    [InlineData("msedgewebview2")]
    [InlineData("FakeSpotify")]
    [InlineData("SpotifyHelper")]
    public void ClassifierRejectsNearMatches(string processName)
    {
        HashSet<AudioApplicationKind> selectedKinds = Enum
            .GetValues<AudioApplicationKind>()
            .ToHashSet();

        Assert.False(AudioApplicationProcessDetector.TryClassifyProcessName(
            processName,
            selectedKinds,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            out _));
    }

    [Fact]
    public void ClassifierOnlyMatchesSelectedKnownApplications()
    {
        Assert.False(AudioApplicationProcessDetector.TryClassifyProcessName(
            "chrome",
            new HashSet<AudioApplicationKind> { AudioApplicationKind.Spotify },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            out _));
    }

    [Theory]
    [InlineData(" obs64 ", "obs64")]
    [InlineData("OBS64.EXE", "OBS64")]
    [InlineData("my-player.exe ", "my-player")]
    public void CustomProcessNamesAreNormalizedAndMatchedExactly(
        string configuredName,
        string expectedNormalized)
    {
        Assert.True(AudioApplicationProcessName.TryNormalize(
            configuredName,
            out string normalized));
        Assert.Equal(expectedNormalized, normalized);

        Assert.True(AudioApplicationProcessDetector.TryClassifyProcessName(
            expectedNormalized.ToUpperInvariant(),
            new HashSet<AudioApplicationKind>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized },
            out AudioApplicationKind kind));
        Assert.Equal(AudioApplicationKind.Custom, kind);
    }

    [Theory]
    [InlineData("SpotifyBooster")]
    [InlineData("SpotifyBooster.exe")]
    [InlineData("SpotifyBooster.App")]
    [InlineData("spotifybooster.helper")]
    public void BoosterProcessIsRejectedFromCustomRouting(string processName)
    {
        Assert.True(AudioApplicationProcessName.IsBoosterProcessName(processName));
        Assert.False(AudioApplicationProcessName.TryNormalize(processName, out _));
        Assert.False(AudioApplicationProcessDetector.TryClassifyProcessName(
            processName,
            new HashSet<AudioApplicationKind>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { processName },
            out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Apps\\player.exe")]
    [InlineData("player/name")]
    [InlineData("player\0name")]
    public void InvalidCustomProcessNamesAreRejected(string processName)
    {
        Assert.False(AudioApplicationProcessName.TryNormalize(processName, out _));
    }

    [Fact]
    public void OverlongCustomProcessNameIsRejected()
    {
        Assert.False(AudioApplicationProcessName.TryNormalize(
            new string('a', AudioApplicationProcessName.MaximumLength + 1),
            out _));
    }
}
