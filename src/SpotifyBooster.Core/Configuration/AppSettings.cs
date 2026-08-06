using System.Text.Json.Serialization;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Core.Configuration;

public enum WindowCloseBehavior
{
    MinimizeToTray,
    Ask,
    Exit,
}

public enum InterfaceTheme
{
    Dark,
    System,
}

public enum InterfaceDensity
{
    Compact,
    Comfortable,
}

public sealed record AudioProfileSettings
{
    public required string Name { get; init; }

    public AudioProcessingSettings Processing { get; init; } = new();
}

public sealed record HotkeySettings
{
    public bool GlobalHotkeysEnabled { get; init; }

    public string IncreaseGain { get; init; } = "Ctrl+Alt+Up";

    public string DecreaseGain { get; init; } = "Ctrl+Alt+Down";

    public string ToggleProcessing { get; init; } = "Ctrl+Alt+B";

    public string PanicReset { get; init; } = "Ctrl+Alt+0";

    public string ToggleBassBoost { get; init; } = "Ctrl+Alt+G";

    public string ToggleWindow { get; init; } = "Ctrl+Alt+S";
}

public sealed record ManualRoutingSettings
{
    public IReadOnlyList<AudioApplicationKind> SelectedApplications { get; init; } =
    [
        AudioApplicationKind.Spotify,
    ];

    public IReadOnlyList<string> CustomProcessNames { get; init; } = [];

    public bool Equals(ManualRoutingSettings? other)
    {
        return other is not null
            && (ReferenceEquals(this, other)
                || (SelectedApplications.SequenceEqual(other.SelectedApplications)
                    && CustomProcessNames.SequenceEqual(
                        other.CustomProcessNames,
                        StringComparer.Ordinal)));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (AudioApplicationKind application in SelectedApplications)
        {
            hash.Add(application);
        }

        foreach (string processName in CustomProcessNames)
        {
            hash.Add(processName, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool FirstRunCompleted { get; init; }

    public bool Expert75DbAcknowledged { get; init; }

    // Source-compatible alias for the audio host. It is never persisted in V3.
    [JsonIgnore]
    public bool ExpertModeAcknowledged { get; init; }

    public string? SourceDeviceId { get; init; }

    public string? OutputDeviceId { get; init; }

    public bool StartWithWindows { get; init; }

    public bool StartMinimized { get; init; }

    public bool StartProcessingAutomatically { get; init; }

    public bool WaitForDevicesOnStartup { get; init; } = true;

    public bool WaitForSpotifyOnStartup { get; init; }

    public WindowCloseBehavior CloseBehavior { get; init; } =
        WindowCloseBehavior.MinimizeToTray;

    public bool NotificationsEnabled { get; init; } = true;

    public bool HeadphonesSafetyMode { get; init; } = true;

    public InterfaceTheme Theme { get; init; } = InterfaceTheme.Dark;

    public InterfaceDensity Density { get; init; } = InterfaceDensity.Compact;

    public bool ReduceAnimations { get; init; }

    public bool ShowLinearPercent { get; init; } = true;

    public bool DetailedDiagnostics { get; init; }

    public ManualRoutingSettings ManualRouting { get; init; } = new();

    public HotkeySettings Hotkeys { get; init; } = new();

    public AudioProcessingSettings Processing { get; init; } = new();

    public IReadOnlyList<AudioProfileSettings> Profiles { get; init; } =
    [
        new()
        {
            Name = "Équilibré",
            Processing = new()
            {
                BassProfile = BassProfile.Balanced,
                BassFrequencyHz = 90,
                BassGainDb = 3,
                BassSlope = 0.9,
            },
        },
        new()
        {
            Name = "Punch",
            Processing = new()
            {
                BassProfile = BassProfile.Punch,
                BassFrequencyHz = 110,
                BassGainDb = 6,
                BassSlope = 1.2,
            },
        },
        new()
        {
            Name = "Deep Bass",
            Processing = new()
            {
                BassProfile = BassProfile.DeepBass,
                BassFrequencyHz = 55,
                BassGainDb = 9,
                BassSlope = 0.75,
            },
        },
    ];
}
