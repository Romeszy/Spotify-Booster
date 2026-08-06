namespace SpotifyBooster.Core.Models;

public sealed class AudioApplicationDetectionSnapshot
{
    private readonly DetectedAudioApplicationProcess[] monitoredProcesses;

    public AudioApplicationDetectionSnapshot(
        IEnumerable<DetectedAudioApplicationProcess> processes,
        IReadOnlyCollection<AudioApplicationKind> selectedApplications)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(selectedApplications);

        monitoredProcesses = processes.ToArray();
        SelectedProcesses = monitoredProcesses
            .Where(process => process.ApplicationKind == AudioApplicationKind.Custom
                || selectedApplications.Contains(process.ApplicationKind))
            .ToArray();
    }

    public IReadOnlyList<DetectedAudioApplicationProcess> MonitoredProcesses =>
        monitoredProcesses;

    public IReadOnlyList<DetectedAudioApplicationProcess> SelectedProcesses { get; }

    public int GetRunningInstanceCount(AudioApplicationKind applicationKind)
    {
        return monitoredProcesses.Count(process =>
            process.ApplicationKind == applicationKind);
    }
}
