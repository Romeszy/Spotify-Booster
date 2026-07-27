using Microsoft.Extensions.Logging.Abstractions;
using SpotifyBooster.Audio.Devices;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Audio.Tests;

public sealed class DeviceEnumerationSmokeTests
{
    [Fact]
    public void ActiveWindowsDevices_CanBeEnumeratedWithoutFailure()
    {
        var service = new NAudioDeviceService(
            NullLogger<NAudioDeviceService>.Instance);

        IReadOnlyList<AudioDeviceInfo> captures = service.GetCaptureDevices();
        IReadOnlyList<AudioDeviceInfo> renders = service.GetRenderDevices();
        Console.WriteLine(
            $"Active endpoints: capture={captures.Count}, render={renders.Count}, virtualCapture={captures.Count(device => device.IsLikelyVirtual)}");

        Assert.NotNull(captures);
        Assert.NotNull(renders);
        Assert.All(
            captures,
            device => Assert.Equal(AudioDeviceDirection.Capture, device.Direction));
        Assert.All(
            renders,
            device => Assert.Equal(AudioDeviceDirection.Render, device.Direction));
    }
}
