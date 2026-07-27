namespace SpotifyBooster.Core.Configuration;

public sealed record AudioProfileSettings
{
    public required string Name { get; init; }

    public AudioProcessingSettings Processing { get; init; } = new();
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool FirstRunCompleted { get; init; }

    public string? SourceDeviceId { get; init; }

    public string? OutputDeviceId { get; init; }

    public bool StartWithWindows { get; init; }

    public bool MinimizeToTray { get; init; } = true;

    public bool HeadphonesSafetyMode { get; init; } = true;

    public AudioProcessingSettings Processing { get; init; } = new();

    public IReadOnlyList<AudioProfileSettings> Profiles { get; init; } =
    [
        new()
        {
            Name = "Balanced",
            Processing = new()
            {
                BassProfile = BassProfile.Balanced,
                BassFrequencyHz = 90,
                BassGainDb = 3,
            },
        },
        new()
        {
            Name = "Punch",
            Processing = new()
            {
                BassProfile = BassProfile.Punch,
                BassFrequencyHz = 105,
                BassGainDb = 5,
            },
        },
        new()
        {
            Name = "Deep Bass",
            Processing = new()
            {
                BassProfile = BassProfile.DeepBass,
                BassFrequencyHz = 60,
                BassGainDb = 6,
            },
        },
    ];
}
