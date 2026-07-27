using System.IO;
using System.Text;
using Microsoft.Win32;

namespace SpotifyBooster.App.Services;

public interface IDiagnosticFileExporter
{
    Task<string?> ExportAsync(string report, CancellationToken cancellationToken = default);
}

public sealed class DiagnosticFileExporter : IDiagnosticFileExporter
{
    public async Task<string?> ExportAsync(
        string report,
        CancellationToken cancellationToken = default)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = ".txt",
            FileName = $"spotify-booster-diagnostic-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = "Rapport texte (*.txt)|*.txt",
            OverwritePrompt = true,
            Title = "Exporter le diagnostic Spotify Booster",
            ValidateNames = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        await File.WriteAllTextAsync(
            dialog.FileName,
            report,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        return dialog.FileName;
    }
}
