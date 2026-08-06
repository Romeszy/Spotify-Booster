using System.Diagnostics;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Infrastructure.Windows;

public sealed class AudioApplicationProcessDetector : IAudioApplicationProcessDetector
{
    public async Task<IReadOnlyList<DetectedAudioApplicationProcess>> DetectAsync(
        IReadOnlyCollection<AudioApplicationKind> applicationKinds,
        IReadOnlyCollection<string> customProcessNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationKinds);
        ArgumentNullException.ThrowIfNull(customProcessNames);
        cancellationToken.ThrowIfCancellationRequested();

        var selectedKinds = applicationKinds.ToHashSet();
        var normalizedCustomNames = customProcessNames
            .Select(name => AudioApplicationProcessName.TryNormalize(name, out string normalized)
                ? normalized
                : null)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return await Task.Run(
            () => Detect(selectedKinds, normalizedCustomNames, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public static bool TryClassifyProcessName(
        string processName,
        IReadOnlySet<AudioApplicationKind> selectedKinds,
        IReadOnlySet<string> customProcessNames,
        out AudioApplicationKind applicationKind)
    {
        ArgumentNullException.ThrowIfNull(processName);
        ArgumentNullException.ThrowIfNull(selectedKinds);
        ArgumentNullException.ThrowIfNull(customProcessNames);

        applicationKind = default;
        if (AudioApplicationProcessName.IsBoosterProcessName(processName))
        {
            return false;
        }

        if (AudioApplicationProcessName.TryGetBuiltInApplicationKind(
                processName,
                out AudioApplicationKind knownKind)
            && selectedKinds.Contains(knownKind))
        {
            applicationKind = knownKind;
            return true;
        }

        if (AudioApplicationProcessName.TryNormalize(processName, out string normalized)
            && customProcessNames.Contains(normalized))
        {
            applicationKind = AudioApplicationKind.Custom;
            return true;
        }

        return false;
    }

    private static DetectedAudioApplicationProcess[] Detect(
        IReadOnlySet<AudioApplicationKind> selectedKinds,
        IReadOnlySet<string> customProcessNames,
        CancellationToken cancellationToken)
    {
        var detected = new List<DetectedAudioApplicationProcess>();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (TryClassifyProcessName(
                        process.ProcessName,
                        selectedKinds,
                        customProcessNames,
                        out AudioApplicationKind applicationKind))
                    {
                        detected.Add(new(
                            process.Id,
                            process.ProcessName,
                            applicationKind,
                            TryGetStartTime(process),
                            TryGetDistribution(process)));
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or System.ComponentModel.Win32Exception
                    or NotSupportedException)
                {
                    // The process exited or denied access while it was inspected.
                }
            }
        }

        return detected
            .OrderBy(process => process.ProcessId)
            .ToArray();
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
