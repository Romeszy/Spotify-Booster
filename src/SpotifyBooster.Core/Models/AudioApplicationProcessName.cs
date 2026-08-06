namespace SpotifyBooster.Core.Models;

public static class AudioApplicationProcessName
{
    private static readonly Dictionary<string, AudioApplicationKind> BuiltInProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Spotify"] = AudioApplicationKind.Spotify,
            ["Discord"] = AudioApplicationKind.Discord,
            ["DiscordPTB"] = AudioApplicationKind.Discord,
            ["DiscordCanary"] = AudioApplicationKind.Discord,
            ["chrome"] = AudioApplicationKind.Chrome,
            ["msedge"] = AudioApplicationKind.Edge,
            ["opera"] = AudioApplicationKind.OperaGx,
            ["firefox"] = AudioApplicationKind.Firefox,
            ["brave"] = AudioApplicationKind.Brave,
        };

    public const int MaximumLength = 64;

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();
        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4].TrimEnd();
        }

        if (candidate.Length is 0 or > MaximumLength
            || candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || candidate.Contains(Path.DirectorySeparatorChar)
            || candidate.Contains(Path.AltDirectorySeparatorChar)
            || candidate.Any(char.IsControl))
        {
            return false;
        }

        normalized = candidate;
        return !IsBoosterProcessName(normalized);
    }

    public static bool TryGetBuiltInApplicationKind(
        string processName,
        out AudioApplicationKind applicationKind)
    {
        ArgumentNullException.ThrowIfNull(processName);

        string candidate = processName.Trim();
        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4].TrimEnd();
        }

        if (BuiltInProcessNames.TryGetValue(candidate, out applicationKind))
        {
            return true;
        }

        if (candidate.StartsWith(
            "SpotifyAB.SpotifyMusic",
            StringComparison.OrdinalIgnoreCase))
        {
            applicationKind = AudioApplicationKind.Spotify;
            return true;
        }

        applicationKind = default;
        return false;
    }

    public static bool IsBoosterProcessName(string processName)
    {
        ArgumentNullException.ThrowIfNull(processName);

        string candidate = processName.Trim();
        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[..^4].TrimEnd();
        }

        return candidate.Equals("SpotifyBooster", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("SpotifyBooster.", StringComparison.OrdinalIgnoreCase);
    }
}
