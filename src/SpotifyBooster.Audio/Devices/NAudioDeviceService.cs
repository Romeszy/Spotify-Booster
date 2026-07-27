using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Audio.Devices;

public sealed class NAudioDeviceService : IAudioDeviceService
{
    private static readonly string[] VirtualDeviceMarkers =
    [
        "virtual",
        "cable",
        "vb-audio",
        "voicemeeter",
        "loopback",
        "streaming",
    ];

    private readonly ILogger<NAudioDeviceService> logger;
    private readonly IErrorSink? errorSink;
    private string? lastEnumerationError;

    public NAudioDeviceService(
        ILogger<NAudioDeviceService> logger,
        IErrorSink? errorSink = null)
    {
        this.logger = logger;
        this.errorSink = errorSink;
    }

    public string? LastEnumerationError =>
        Volatile.Read(ref lastEnumerationError);

    public void ClearEnumerationError()
    {
        Volatile.Write(ref lastEnumerationError, null);
    }

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        return Enumerate(
            DataFlow.Capture,
            AudioDeviceDirection.Capture,
            DeviceState.Active);
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        return Enumerate(
            DataFlow.Render,
            AudioDeviceDirection.Render,
            DeviceState.Active);
    }

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDeviceInventory()
    {
        return Enumerate(
            DataFlow.Capture,
            AudioDeviceDirection.Capture,
            DeviceState.All);
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDeviceInventory()
    {
        return Enumerate(
            DataFlow.Render,
            AudioDeviceDirection.Render,
            DeviceState.All);
    }

    internal static AudioDeviceInfo Describe(
        MMDevice device,
        AudioDeviceDirection direction,
        string? defaultDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        string name = device.FriendlyName;
        bool isLikelyVirtual = VirtualDeviceMarkers.Any(
            marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
        (int sampleRate, int channels, int bitsPerSample) = TryGetFormat(device);
        return new(
            device.ID,
            name,
            direction,
            string.Equals(device.ID, defaultDeviceId, StringComparison.Ordinal),
            sampleRate,
            channels,
            bitsPerSample,
            isLikelyVirtual,
            device.State == DeviceState.Active,
            DescribeState(device.State));
    }

    private AudioDeviceInfo[] Enumerate(
        DataFlow flow,
        AudioDeviceDirection direction,
        DeviceState state)
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = TryGetDefaultId(enumerator, flow);
            MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(
                flow,
                state);
            foreach (MMDevice endpoint in endpoints)
            {
                using (endpoint)
                {
                    try
                    {
                        devices.Add(Describe(endpoint, direction, defaultId));
                    }
                    catch (Exception exception) when (
                        exception is InvalidOperationException
                        or System.Runtime.InteropServices.COMException)
                    {
                        logger.LogWarning(
                            exception,
                            "Un périphérique audio actif n’a pas pu être interrogé.");
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            const string message =
                "L’API audio Windows n’a pas pu énumérer les périphériques.";
            Volatile.Write(ref lastEnumerationError, message);
            logger.LogWarning(
                exception,
                "L’énumération des périphériques audio Windows a échoué.");
            errorSink?.Report("Périphériques audio", message);
        }

        return devices
            .OrderByDescending(device => device.IsActive)
            .ThenByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string? TryGetDefaultId(
        MMDeviceEnumerator enumerator,
        DataFlow flow)
    {
        try
        {
            using MMDevice defaultDevice = enumerator.GetDefaultAudioEndpoint(
                flow,
                Role.Multimedia);
            return defaultDevice.ID;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static (int SampleRate, int Channels, int BitsPerSample) TryGetFormat(
        MMDevice device)
    {
        try
        {
            NAudio.Wave.WaveFormat format = device.AudioClient.MixFormat;
            return (format.SampleRate, format.Channels, format.BitsPerSample);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return (0, 0, 0);
        }
    }

    private static string DescribeState(DeviceState state)
    {
        if ((state & DeviceState.Active) != 0)
        {
            return "Actif";
        }

        if ((state & DeviceState.Disabled) != 0)
        {
            return "Désactivé";
        }

        if ((state & DeviceState.Unplugged) != 0)
        {
            return "Débranché";
        }

        if ((state & DeviceState.NotPresent) != 0)
        {
            return "Absent";
        }

        return "Indéterminé";
    }
}
