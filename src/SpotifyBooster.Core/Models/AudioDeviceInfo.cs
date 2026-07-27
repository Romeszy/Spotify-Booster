namespace SpotifyBooster.Core.Models;

public enum AudioDeviceDirection
{
    Capture,
    Render,
}

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    AudioDeviceDirection Direction,
    bool IsDefault,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    bool IsLikelyVirtual)
{
    public string FormatDescription => $"{SampleRate:N0} Hz · {Channels} ch · {BitsPerSample} bit";
}
