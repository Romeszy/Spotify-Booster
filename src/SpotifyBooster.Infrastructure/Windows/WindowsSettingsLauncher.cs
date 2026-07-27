using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SpotifyBooster.Core.Abstractions;

namespace SpotifyBooster.Infrastructure.Windows;

public sealed class WindowsSettingsLauncher : IWindowsSettingsLauncher
{
    private const string VolumeMixerUri = "ms-settings:apps-volume";
    private const string SoundSettingsUri = "ms-settings:sound";
    private const string VbCableWebsiteUri = "https://vb-audio.com/Cable/";
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
        return Open(
            VolumeMixerUri,
            "Impossible d’ouvrir le mélangeur de volume Windows.");
    }

    public bool OpenSoundSettings()
    {
        return Open(
            SoundSettingsUri,
            "Impossible d’ouvrir les paramètres de son Windows.");
    }

    public bool OpenVbCableWebsite()
    {
        return Open(
            VbCableWebsiteUri,
            "Impossible d’ouvrir la page officielle de VB-CABLE.");
    }

    private bool Open(string destination, string failureMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = destination,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "{Message}", failureMessage);
            errorSink.Report("Windows", failureMessage);
            return false;
        }
    }
}
