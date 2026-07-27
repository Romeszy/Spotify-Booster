using System.Globalization;
using System.Text;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Infrastructure.Diagnostics;

public sealed class DiagnosticReportService : IDiagnosticReportService
{
    public string CreateTextReport(DiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder(2_048);
        builder.AppendLine("Spotify Booster — rapport de diagnostic");
        builder.AppendLine($"Généré (UTC) : {snapshot.GeneratedAt:O}");
        builder.AppendLine($"Système : Windows {Environment.OSVersion.Version}");
        builder.AppendLine($"Runtime : .NET {Environment.Version}");
        builder.AppendLine();
        builder.AppendLine($"Spotify détecté : {(snapshot.SpotifyProcesses.Count > 0 ? "oui" : "non")}");
        foreach (SpotifyProcessInfo process in snapshot.SpotifyProcesses)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {process.ProcessName} (PID {process.ProcessId}, {process.Distribution})");
        }

        builder.AppendLine();
        AppendDevice(builder, "Source", snapshot.Source);
        AppendDevice(builder, "Sortie", snapshot.Output);
        builder.AppendLine($"État moteur : {snapshot.Engine.State} — {snapshot.Engine.Message}");
        builder.AppendLine($"Format source : {snapshot.Engine.SourceFormat ?? "indisponible"}");
        builder.AppendLine($"Format sortie : {snapshot.Engine.OutputFormat ?? "indisponible"}");
        builder.AppendLine($"Fréquence : {snapshot.Engine.Metrics.SampleRate} Hz");
        builder.AppendLine($"Canaux : {snapshot.Engine.Metrics.Channels}");
        builder.AppendLine($"Buffer : {snapshot.Engine.Metrics.BufferMilliseconds} ms");
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Latence estimée : {snapshot.Engine.Metrics.EstimatedLatencyMilliseconds:F1} ms");
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Charge DSP estimée : {snapshot.Engine.Metrics.ProcessingLoadPercent:F1} %");
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Pic entrée/sortie : {snapshot.Engine.Metrics.InputPeak:F4} / {snapshot.Engine.Metrics.OutputPeak:F4}");
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Réduction limiteur : {snapshot.Engine.Metrics.LimiterReductionDb:F2} dB");
        builder.AppendLine($"Clips de sortie détectés : {snapshot.Engine.Metrics.ClipCount}");
        builder.AppendLine($"Underruns : {snapshot.Engine.Metrics.BufferUnderrunCount}");
        builder.AppendLine($"Overruns : {snapshot.Engine.Metrics.BufferOverrunCount}");
        builder.AppendLine();
        builder.AppendLine("Erreurs récentes :");

        if (snapshot.RecentErrors.Count == 0)
        {
            builder.AppendLine("- aucune");
        }
        else
        {
            foreach (DiagnosticError error in snapshot.RecentErrors)
            {
                builder.AppendLine($"- {error.Timestamp:O} [{error.Component}] {error.Message}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Ce rapport exclut les chemins de fichiers, identifiants de périphérique et arguments de processus.");
        return builder.ToString();
    }

    private static void AppendDevice(
        StringBuilder builder,
        string label,
        AudioDeviceInfo? device)
    {
        if (device is null)
        {
            builder.AppendLine($"{label} : non sélectionnée");
            return;
        }

        builder.AppendLine($"{label} : {device.Name}");
        builder.AppendLine($"{label} format : {device.FormatDescription}");
    }
}
