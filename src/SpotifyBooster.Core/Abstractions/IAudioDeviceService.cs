using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Abstractions;

public interface IAudioDeviceService
{
    string? LastEnumerationError { get; }

    void ClearEnumerationError();

    IReadOnlyList<AudioDeviceInfo> GetCaptureDevices();

    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();

    IReadOnlyList<AudioDeviceInfo> GetCaptureDeviceInventory();

    IReadOnlyList<AudioDeviceInfo> GetRenderDeviceInventory();
}
