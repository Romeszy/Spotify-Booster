namespace SpotifyBooster.Core.Configuration;

public sealed record SettingsValidationResult(AppSettings Settings, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class SettingsValidator
{
    private const int MaximumDeviceIdLength = 1_024;
    private const int MaximumHotkeyLength = 64;

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

        ValidateEnum(settings.CloseBehavior, nameof(settings.CloseBehavior), errors);
        ValidateEnum(settings.Theme, nameof(settings.Theme), errors);
        ValidateEnum(settings.Density, nameof(settings.Density), errors);
        ValidateDeviceId(settings.SourceDeviceId, nameof(settings.SourceDeviceId), errors);
        ValidateDeviceId(settings.OutputDeviceId, nameof(settings.OutputDeviceId), errors);
        ValidateProcessing(settings.Processing, "Processing", errors);
        ValidateHotkeys(settings.Hotkeys, errors);
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
            || settings.Hotkeys is null
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
                    Mode = AudioProtectionMode.Protected,
                    MainGainDb = AudioProcessingSettings.MinimumGainDb,
                    OutputGainDb = 0,
                    CompressorEnabled = false,
                    SoftClipperEnabled = true,
                    LimiterEnabled = true,
                },
            };

        return new(safeSettings, errors);
    }

    private static void ValidateEnum<T>(T value, string field, List<string> errors)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add($"{field} contient une valeur inconnue.");
        }
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

    private static void ValidateHotkeys(HotkeySettings? hotkeys, List<string> errors)
    {
        if (hotkeys is null)
        {
            errors.Add("Hotkeys ne peut pas être nul.");
            return;
        }

        string?[] values =
        [
            hotkeys.IncreaseGain,
            hotkeys.DecreaseGain,
            hotkeys.ToggleProcessing,
            hotkeys.PanicReset,
            hotkeys.ToggleBassBoost,
            hotkeys.ToggleWindow,
        ];
        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > MaximumHotkeyLength
                || value.Any(char.IsControl)
                || !IsValidHotkeyGesture(value))
            {
                errors.Add(
                    "Chaque raccourci doit contenir un modificateur reconnu et une seule touche valide.");
            }
        }

        string[] normalizedGestures = values
            .Where(value => value is not null)
            .Select(value => TryNormalizeHotkeyGesture(value!, out string normalized)
                ? normalized
                : string.Empty)
            .Where(value => value.Length > 0)
            .ToArray();
        if (normalizedGestures
            .Distinct(StringComparer.Ordinal)
            .Count() != normalizedGestures.Length)
        {
            errors.Add("Deux actions ne peuvent pas utiliser le même raccourci.");
        }
    }

    private static bool IsValidHotkeyGesture(string value)
    {
        return TryNormalizeHotkeyGesture(value, out _);
    }

    private static bool TryNormalizeHotkeyGesture(
        string value,
        out string normalized)
    {
        normalized = string.Empty;
        string[] parts = value.Split(
            '+',
            StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var modifiers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (string part in parts)
        {
            string? normalizedModifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => "CTRL",
                "ALT" => "ALT",
                "SHIFT" => "SHIFT",
                "WIN" or "WINDOWS" => "WIN",
                _ => null,
            };
            if (normalizedModifier is not null)
            {
                if (!modifiers.Add(normalizedModifier))
                {
                    return false;
                }

                continue;
            }

            if (key is not null)
            {
                return false;
            }

            key = part;
        }

        if (modifiers.Count == 0
            || key is null
            || !IsValidHotkeyKey(key))
        {
            return false;
        }

        string[] orderedModifiers = ["CTRL", "ALT", "SHIFT", "WIN"];
        normalized = string.Join(
            '+',
            orderedModifiers
                .Where(modifiers.Contains)
                .Append(key.ToUpperInvariant()));
        return true;
    }

    private static bool IsValidHotkeyKey(string value)
    {
        if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]))
        {
            return true;
        }

        if (value.Length is 2 or 3
            && value[0] is 'F' or 'f'
            && int.TryParse(value.AsSpan(1), out int functionKey)
            && functionKey is >= 1 and <= 24)
        {
            return true;
        }

        return value.Equals("Up", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Down", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Left", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Right", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Home", StringComparison.OrdinalIgnoreCase)
            || value.Equals("End", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PageUp", StringComparison.OrdinalIgnoreCase)
            || value.Equals("PageDown", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Insert", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Delete", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Space", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Tab", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Enter", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Escape", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Back", StringComparison.OrdinalIgnoreCase)
            || value.Equals("OemPlus", StringComparison.OrdinalIgnoreCase)
            || value.Equals("OemMinus", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Add", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Subtract", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Multiply", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Divide", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Decimal", StringComparison.OrdinalIgnoreCase);
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

        ValidateEnum(processing.Mode, $"{prefix}.Mode", errors);
        ValidateEnum(processing.BassProfile, $"{prefix}.BassProfile", errors);
        double allowedGain = processing.Mode == AudioProtectionMode.RawExpert
            ? AudioProcessingSettings.RawExpertMaximumGainDb
            : AudioProcessingSettings.StandardMaximumGainDb;
        ValidateFiniteRange(
            processing.MainGainDb,
            AudioProcessingSettings.MinimumGainDb,
            allowedGain,
            $"{prefix}.MainGainDb",
            errors);
        if (processing.Mode == AudioProtectionMode.RawExpert
            && double.IsFinite(processing.MainGainDb)
            && Math.Abs(
                (processing.MainGainDb / AudioProcessingSettings.RawExpertGainStepDb)
                - Math.Round(
                    processing.MainGainDb / AudioProcessingSettings.RawExpertGainStepDb))
                > 1e-9)
        {
            errors.Add($"{prefix}.MainGainDb doit être aligné sur la grille de 0,5 dB.");
        }
        ValidateFiniteRange(processing.BassFrequencyHz, 35, 200, $"{prefix}.BassFrequencyHz", errors);
        ValidateFiniteRange(processing.BassGainDb, 0, 18, $"{prefix}.BassGainDb", errors);
        ValidateFiniteRange(processing.BassSlope, 0.25, 2, $"{prefix}.BassSlope", errors);
        ValidateFiniteRange(processing.EqualizerLowDb, -12, 12, $"{prefix}.EqualizerLowDb", errors);
        ValidateFiniteRange(processing.EqualizerMidDb, -12, 12, $"{prefix}.EqualizerMidDb", errors);
        ValidateFiniteRange(processing.EqualizerHighDb, -12, 12, $"{prefix}.EqualizerHighDb", errors);
        ValidateFiniteRange(processing.EqualizerQ, 0.1, 10, $"{prefix}.EqualizerQ", errors);
        ValidateFiniteRange(processing.CompressorThresholdDb, -60, 0, $"{prefix}.CompressorThresholdDb", errors);
        ValidateFiniteRange(processing.CompressorRatio, 1, 20, $"{prefix}.CompressorRatio", errors);
        ValidateFiniteRange(processing.CompressorAttackMs, 0.1, 200, $"{prefix}.CompressorAttackMs", errors);
        ValidateFiniteRange(processing.CompressorReleaseMs, 10, 2_000, $"{prefix}.CompressorReleaseMs", errors);
        ValidateFiniteRange(processing.CompressorMakeupGainDb, -12, 24, $"{prefix}.CompressorMakeupGainDb", errors);
        ValidateFiniteRange(processing.SoftClipperThresholdDb, -18, -0.01, $"{prefix}.SoftClipperThresholdDb", errors);
        ValidateFiniteRange(processing.SoftClipperDrive, 0.5, 8, $"{prefix}.SoftClipperDrive", errors);
        ValidateFiniteRange(processing.LimiterThresholdDb, -12, -0.01, $"{prefix}.LimiterThresholdDb", errors);
        ValidateFiniteRange(processing.LimiterReleaseMs, 10, 1_000, $"{prefix}.LimiterReleaseMs", errors);
        ValidateFiniteRange(processing.OutputGainDb, -12, 12, $"{prefix}.OutputGainDb", errors);
        ValidateFiniteRange(processing.ParameterSmoothingMs, 0.5, 250, $"{prefix}.ParameterSmoothingMs", errors);
        ValidateFiniteRange(processing.PanicSmoothingMs, 0.1, 10, $"{prefix}.PanicSmoothingMs", errors);

        if (processing.Mode == AudioProtectionMode.Protected
            && (!processing.LimiterEnabled || !processing.SoftClipperEnabled))
        {
            errors.Add(
                $"{prefix} est en mode Protégé mais le soft clipper ou le limiteur est désactivé.");
        }

        if (processing.Mode == AudioProtectionMode.Protected
            && processing.OutputGainDb > 0)
        {
            errors.Add(
                $"{prefix}.OutputGainDb ne peut pas être positif en mode Protégé car il intervient après le limiteur.");
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
