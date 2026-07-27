using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SpotifyBooster.Core.Abstractions;

namespace SpotifyBooster.Infrastructure.Windows;

public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Spotify Booster";
    private const string MinimizedArgument = "--minimized";

    private readonly ILogger<StartupService> logger;
    private readonly IErrorSink errorSink;
    private readonly string? executablePath;

    public StartupService(
        ILogger<StartupService> logger,
        IErrorSink errorSink)
    {
        this.logger = logger;
        this.errorSink = errorSink;
        executablePath = Environment.ProcessPath;
    }

    public bool IsEnabled()
    {
        if (!TryGetExpectedCommand(out string expectedCommand))
        {
            return false;
        }

        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: false);
            string? registeredCommand = runKey?.GetValue(
                ValueName,
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

            return string.Equals(
                registeredCommand,
                expectedCommand,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            ReportFailure(
                exception,
                "Impossible de vérifier le démarrage automatique.");
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        string expectedCommand = string.Empty;
        if (enabled && !TryGetExpectedCommand(out expectedCommand))
        {
            return false;
        }

        try
        {
            if (enabled)
            {
                using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(
                    RunKeyPath,
                    writable: true);
                runKey.SetValue(ValueName, expectedCommand, RegistryValueKind.String);
                bool enabledAfterWrite = IsEnabled();
                if (enabledAfterWrite)
                {
                    logger.LogInformation("Démarrage automatique Windows activé.");
                }

                return enabledAfterWrite;
            }

            using (RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(
                       RunKeyPath,
                       writable: true))
            {
                runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            bool disabled = !HasRegisteredValue();
            if (disabled)
            {
                logger.LogInformation("Démarrage automatique Windows désactivé.");
            }

            return disabled;
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            ReportFailure(
                exception,
                enabled
                    ? "Impossible d’activer le démarrage automatique."
                    : "Impossible de désactiver le démarrage automatique.");
            return false;
        }
    }

    public static string BuildStartupCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath)
            || executablePath.IndexOfAny(['"', '\r', '\n']) >= 0)
        {
            throw new ArgumentException(
                "Le chemin de l’exécutable doit être un chemin absolu valide.",
                nameof(executablePath));
        }

        string normalizedPath = Path.GetFullPath(executablePath);
        if (!string.Equals(
                Path.GetExtension(normalizedPath),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Le chemin de démarrage doit désigner un exécutable Windows.",
                nameof(executablePath));
        }

        return $"\"{normalizedPath}\" {MinimizedArgument}";
    }

    private static bool IsRegistryException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or ObjectDisposedException
            or PlatformNotSupportedException;
    }

    private static bool HasRegisteredValue()
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(
            RunKeyPath,
            writable: false);
        return runKey?.GetValue(
            ValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) is not null;
    }

    private bool TryGetExpectedCommand(out string command)
    {
        try
        {
            command = BuildStartupCommand(executablePath ?? string.Empty);
            return true;
        }
        catch (ArgumentException exception)
        {
            ReportFailure(
                exception,
                "Le chemin de l’application est indisponible pour le démarrage automatique.");
            command = string.Empty;
            return false;
        }
    }

    private void ReportFailure(Exception exception, string message)
    {
        logger.LogWarning(exception, "{Message}", message);
        errorSink.Report("Démarrage Windows", message);
    }
}
