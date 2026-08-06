using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Tests;

public sealed class AudioApplicationDetectionSnapshotTests
{
    [Fact]
    public void NonSelectedBuiltInApplicationRemainsVisibleToDetectionCards()
    {
        DetectedAudioApplicationProcess chrome = CreateProcess(
            42,
            "chrome",
            AudioApplicationKind.Chrome);

        var snapshot = new AudioApplicationDetectionSnapshot(
            [chrome],
            [AudioApplicationKind.Spotify]);

        Assert.Equal(1, snapshot.GetRunningInstanceCount(AudioApplicationKind.Chrome));
        Assert.Empty(snapshot.SelectedProcesses);
    }

    [Fact]
    public void SelectedProcessesIncludeChosenFamiliesAndConfiguredCustomMatches()
    {
        DetectedAudioApplicationProcess discord = CreateProcess(
            10,
            "Discord",
            AudioApplicationKind.Discord);
        DetectedAudioApplicationProcess chrome = CreateProcess(
            11,
            "chrome",
            AudioApplicationKind.Chrome);
        DetectedAudioApplicationProcess custom = CreateProcess(
            12,
            "tidal",
            AudioApplicationKind.Custom);

        var snapshot = new AudioApplicationDetectionSnapshot(
            [discord, chrome, custom],
            [AudioApplicationKind.Discord]);

        Assert.Equal([discord, custom], snapshot.SelectedProcesses);
        Assert.Equal(1, snapshot.GetRunningInstanceCount(AudioApplicationKind.Chrome));
    }

    private static DetectedAudioApplicationProcess CreateProcess(
        int processId,
        string processName,
        AudioApplicationKind kind)
    {
        return new(
            processId,
            processName,
            kind,
            StartedAt: null,
            Distribution: "Desktop");
    }
}
