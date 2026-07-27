using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Abstractions;

public interface IErrorSink
{
    void Report(string component, string message);

    IReadOnlyList<DiagnosticError> GetRecent(int maximumCount = 20);
}

public interface IDiagnosticReportService
{
    string CreateTextReport(DiagnosticSnapshot snapshot);
}

public interface IWindowsSettingsLauncher
{
    bool OpenVolumeMixer();
}
