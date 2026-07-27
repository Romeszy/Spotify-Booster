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

    public NAudioDeviceService(ILogger<NAudioDeviceService> logger)
    {
        this.logger = logger;
    }

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        return Enumerate(DataFlow.Capture, AudioDeviceDirection.Capture);
    }

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        return Enumerate(DataFlow.Render, AudioDeviceDirection.Render);
    }

    internal static AudioDeviceInfo Describe(
        MMDevice device,
        AudioDeviceDirection direction,
        string? defaultDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        NAudio.Wave.WaveFormat format = device.AudioClient.MixFormat;
        string name = device.FriendlyName;
        bool isLikelyVirtual = VirtualDeviceMarkers.Any(
            marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
        return new(
            device.ID,
            name,
            direction,
            string.Equals(device.ID, defaultDeviceId, StringComparison.Ordinal),
            format.SampleRate,
            format.Channels,
            format.BitsPerSample,
            isLikelyVirtual);
    }

    private AudioDeviceInfo[] Enumerate(
        DataFlow flow,
        AudioDeviceDirection direction)
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = TryGetDefaultId(enumerator, flow);
            MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(
                flow,
                DeviceState.Active);
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
            logger.LogWarning(
                exception,
                "L’énumération des périphériques audio Windows a échoué.");
        }

        return devices
            .OrderByDescending(device => device.IsDefault)
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
}
