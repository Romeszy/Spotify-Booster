using System.Diagnostics;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Infrastructure.Spotify;

public sealed class SpotifyProcessDetector : ISpotifyProcessDetector
{
    public async Task<IReadOnlyList<SpotifyProcessInfo>> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Process enumeration and MainModule access can block. Keep the entire scan
        // off the caller's context; concurrent calls intentionally run independent scans.
        return await Task.Run(
            () => Detect(cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<SpotifyProcessInfo> Detect(
        CancellationToken cancellationToken)
    {
        var detected = new List<SpotifyProcessInfo>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (!processName.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTimeOffset? startedAt = TryGetStartTime(process);
                string distribution = TryGetDistribution(process);
                detected.Add(new SpotifyProcessInfo(
                    process.Id,
                    processName,
                    startedAt,
                    distribution));
            }
        }

        IReadOnlyList<SpotifyProcessInfo> result = detected
            .OrderBy(process => process.ProcessId)
            .ToArray();
        return result;
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return null;
        }
    }

    private static string TryGetDistribution(Process process)
    {
        try
        {
            string? fileName = process.MainModule?.FileName;
            if (fileName?.Contains(
                "\\WindowsApps\\",
                StringComparison.OrdinalIgnoreCase) == true)
            {
                return "Microsoft Store";
            }

            return string.IsNullOrWhiteSpace(fileName) ? "Indéterminée" : "Desktop";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return "Indéterminée";
        }
    }
}
