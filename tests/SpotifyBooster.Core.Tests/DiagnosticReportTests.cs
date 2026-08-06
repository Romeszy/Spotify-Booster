using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Core.Models;
using SpotifyBooster.Infrastructure.Diagnostics;

namespace SpotifyBooster.Core.Tests;

public sealed class DiagnosticReportTests
{
    [Fact]
    public void Report_ContainsOperationalDataButNotDeviceIds()
    {
        var source = new AudioDeviceInfo(
            "sensitive-capture-id",
            "Virtual Cable",
            AudioDeviceDirection.Capture,
            false,
            48_000,
            2,
            32,
            true);
        var output = new AudioDeviceInfo(
            "sensitive-output-id",
            "Headphones",
            AudioDeviceDirection.Render,
            true,
            48_000,
            2,
            32,
            false);
        var snapshot = new DiagnosticSnapshot(
            DateTimeOffset.UtcNow,
            [new SpotifyProcessInfo(42, "Spotify", null, "Desktop")],
            source,
            output,
            AudioEngineStatus.Stopped,
            [],
            ApplicationProcesses:
            [
                new DetectedAudioApplicationProcess(
                    84,
                    "Discord",
                    AudioApplicationKind.Discord,
                    null,
                    "Desktop"),
            ],
            ManualRouting: new ManualRoutingSettings
            {
                SelectedApplications =
                [
                    AudioApplicationKind.Spotify,
                    AudioApplicationKind.Discord,
                ],
                CustomProcessNames = ["tidal"],
            });

        string report = new DiagnosticReportService().CreateTextReport(snapshot);

        Assert.Contains("Virtual Cable", report, StringComparison.Ordinal);
        Assert.Contains("Spotify", report, StringComparison.Ordinal);
        Assert.Contains("Discord", report, StringComparison.Ordinal);
        Assert.Contains("tidal.exe", report, StringComparison.Ordinal);
        Assert.Contains("Isolation : informative", report, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-capture-id", report, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-output-id", report, StringComparison.Ordinal);
    }
}
