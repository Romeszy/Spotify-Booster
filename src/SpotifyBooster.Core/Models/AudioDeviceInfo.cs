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
    bool IsLikelyVirtual,
    bool IsActive = true,
    string StateDescription = "Actif")
{
    public string FormatDescription => SampleRate > 0 && Channels > 0
        ? $"{SampleRate:N0} Hz · {Channels} ch · {BitsPerSample} bit"
        : "Format indisponible";
}
