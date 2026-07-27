namespace SpotifyBooster.Core.Configuration;

public sealed record SettingsValidationResult(AppSettings Settings, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class SettingsValidator
{
    private const int MaximumDeviceIdLength = 1_024;

    public static SettingsValidationResult Validate(AppSettings? settings)
    {
        if (settings is null)
        {
            return new(new AppSettings(), ["La configuration est absente."]);
        }

        var errors = new List<string>();

        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            errors.Add($"Version de configuration non prise en charge : {settings.SchemaVersion}.");
        }

        ValidateDeviceId(settings.SourceDeviceId, nameof(settings.SourceDeviceId), errors);
        ValidateDeviceId(settings.OutputDeviceId, nameof(settings.OutputDeviceId), errors);
        ValidateProcessing(settings.Processing, "Processing", errors);

        if (settings.Profiles is null)
        {
            errors.Add("Profiles ne peut pas être nul.");
        }
        else if (settings.Profiles.Count > 32)
        {
            errors.Add("Le nombre de profils ne peut pas dépasser 32.");
        }

        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AudioProfileSettings? profile in settings.Profiles ?? [])
        {
            if (profile is null)
            {
                errors.Add("Un profil ne peut pas être nul.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 64)
            {
                errors.Add("Chaque profil doit avoir un nom de 1 à 64 caractères.");
            }
            else if (!profileNames.Add(profile.Name))
            {
                errors.Add($"Le profil « {profile.Name} » est dupliqué.");
            }

            ValidateProcessing(profile.Processing, $"Profiles[{profile.Name}]", errors);
        }

        if (settings.Processing is null
            || settings.Profiles is null
            || settings.Profiles.Any(profile => profile is null || profile.Processing is null))
        {
            return new(new AppSettings(), errors);
        }

        AudioProcessingSettings processing = settings.Processing;
        AppSettings safeSettings = settings.FirstRunCompleted
            ? settings
            : settings with
            {
                Processing = processing with
                {
                    GainPercent = AudioProcessingSettings.MinimumGainPercent,
                    LimiterEnabled = true,
                },
            };

        return new(safeSettings, errors);
    }

    private static void ValidateDeviceId(string? value, string field, List<string> errors)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length == 0 || value.Length > MaximumDeviceIdLength || value.Any(char.IsControl))
        {
            errors.Add($"{field} contient un identifiant de périphérique invalide.");
        }
    }

    private static void ValidateProcessing(
        AudioProcessingSettings? processing,
        string prefix,
        List<string> errors)
    {
        if (processing is null)
        {
            errors.Add($"{prefix} ne peut pas être nul.");
            return;
        }

        ValidateRange(processing.GainPercent, 100, 500, $"{prefix}.GainPercent", errors);
        ValidateFiniteRange(processing.BassFrequencyHz, 40, 200, $"{prefix}.BassFrequencyHz", errors);
        ValidateFiniteRange(processing.BassGainDb, 0, 12, $"{prefix}.BassGainDb", errors);
        ValidateFiniteRange(processing.EqualizerLowDb, -12, 12, $"{prefix}.EqualizerLowDb", errors);
        ValidateFiniteRange(processing.EqualizerMidDb, -12, 12, $"{prefix}.EqualizerMidDb", errors);
        ValidateFiniteRange(processing.EqualizerHighDb, -12, 12, $"{prefix}.EqualizerHighDb", errors);
        ValidateFiniteRange(processing.CompressorThresholdDb, -60, 0, $"{prefix}.CompressorThresholdDb", errors);
        ValidateFiniteRange(processing.CompressorRatio, 1, 20, $"{prefix}.CompressorRatio", errors);
        ValidateFiniteRange(processing.CompressorAttackMs, 0.1, 200, $"{prefix}.CompressorAttackMs", errors);
        ValidateFiniteRange(processing.CompressorReleaseMs, 10, 2_000, $"{prefix}.CompressorReleaseMs", errors);
        ValidateFiniteRange(processing.CompressorMakeupGainDb, 0, 12, $"{prefix}.CompressorMakeupGainDb", errors);
        ValidateFiniteRange(processing.LimiterThresholdDb, -12, -0.1, $"{prefix}.LimiterThresholdDb", errors);
        ValidateFiniteRange(processing.LimiterReleaseMs, 10, 1_000, $"{prefix}.LimiterReleaseMs", errors);
        ValidateFiniteRange(processing.ParameterSmoothingMs, 1, 200, $"{prefix}.ParameterSmoothingMs", errors);
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string field,
        List<string> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"{field} doit être compris entre {minimum} et {maximum}.");
        }
    }

    private static void ValidateFiniteRange(
        double value,
        double minimum,
        double maximum,
        string field,
        List<string> errors)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            errors.Add($"{field} doit être un nombre fini compris entre {minimum} et {maximum}.");
        }
    }
}
