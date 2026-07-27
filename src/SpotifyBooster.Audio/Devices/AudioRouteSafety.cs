using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Audio.Devices;

public sealed record AudioRouteValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public static class AudioRouteSafety
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

    public static AudioRouteValidationResult Validate(
        AudioDeviceInfo source,
        AudioDeviceInfo output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (source.Direction != AudioDeviceDirection.Capture)
        {
            errors.Add("La source doit être un périphérique de capture.");
        }

        if (output.Direction != AudioDeviceDirection.Render)
        {
            errors.Add("La sortie doit être un périphérique de rendu.");
        }

        if (string.Equals(source.Id, output.Id, StringComparison.Ordinal))
        {
            errors.Add("La source et la sortie ne peuvent pas être le même endpoint.");
        }

        if (source.Channels is < 1 or > 2)
        {
            errors.Add("Le MVP accepte uniquement une source mono ou stéréo.");
        }

        if (output.Channels != 2)
        {
            errors.Add("Le MVP accepte uniquement une sortie configurée en stéréo.");
        }

        if (source.SampleRate <= 0 || output.SampleRate <= 0)
        {
            errors.Add("Le format audio du périphérique est invalide.");
        }

        bool sourceIsVirtual = IsLikelyVirtual(source);
        bool outputIsVirtual = IsLikelyVirtual(output);

        if (!sourceIsVirtual)
        {
            errors.Add(
                "Pour ce MVP, la source doit être un périphérique de capture virtuel dédié à Spotify.");
        }

        bool sameLoopFamily = VirtualDeviceMarkers.Any(marker =>
            source.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)
            && output.Name.Contains(marker, StringComparison.OrdinalIgnoreCase));
        if (sameLoopFamily)
        {
            errors.Add(
                "Cette combinaison peut créer une boucle audio. Choisissez une sortie physique.");
        }
        else if (outputIsVirtual)
        {
            errors.Add(
                "Pour ce MVP, la sortie doit être un périphérique de rendu physique.");
        }

        return new(errors, warnings);
    }

    private static bool IsLikelyVirtual(AudioDeviceInfo device)
    {
        return device.IsLikelyVirtual
            || VirtualDeviceMarkers.Any(marker =>
                device.Name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
