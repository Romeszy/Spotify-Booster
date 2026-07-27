using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SpotifyBooster.Core.Abstractions;

namespace SpotifyBooster.Infrastructure.Windows;

public sealed class WindowsSettingsLauncher : IWindowsSettingsLauncher
{
    private const string VolumeMixerUri = "ms-settings:apps-volume";
    private readonly ILogger<WindowsSettingsLauncher> logger;
    private readonly IErrorSink errorSink;

    public WindowsSettingsLauncher(
        ILogger<WindowsSettingsLauncher> logger,
        IErrorSink errorSink)
    {
        this.logger = logger;
        this.errorSink = errorSink;
    }

    public bool OpenVolumeMixer()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = VolumeMixerUri,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            const string message = "Impossible d’ouvrir le mélangeur de volume Windows.";
            logger.LogWarning(exception, "{Message}", message);
            errorSink.Report("Windows", message);
            return false;
        }
    }
}
