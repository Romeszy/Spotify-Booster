using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Abstractions;

public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetCaptureDevices();

    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();
}
