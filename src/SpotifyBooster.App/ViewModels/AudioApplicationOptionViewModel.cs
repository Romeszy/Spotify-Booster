using SpotifyBooster.App.Mvvm;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.App.ViewModels;

public sealed class AudioApplicationOptionViewModel : ObservableObject
{
    private readonly Action<AudioApplicationOptionViewModel> selectionChanged;
    private bool isSelected;
    private bool detectionAvailable = true;
    private int runningInstanceCount;

    public AudioApplicationOptionViewModel(
        AudioApplicationKind kind,
        Action<AudioApplicationOptionViewModel> selectionChanged)
    {
        if (kind == AudioApplicationKind.Custom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Les processus personnalisés sont gérés séparément.");
        }

        Kind = kind;
        this.selectionChanged = selectionChanged
            ?? throw new ArgumentNullException(nameof(selectionChanged));
    }

    public AudioApplicationKind Kind { get; }

    public string DisplayName => Kind switch
    {
        AudioApplicationKind.Spotify => "Spotify",
        AudioApplicationKind.Discord => "Discord",
        AudioApplicationKind.Chrome => "Google Chrome",
        AudioApplicationKind.Edge => "Microsoft Edge",
        AudioApplicationKind.OperaGx => "Opera GX",
        AudioApplicationKind.Firefox => "Mozilla Firefox",
        AudioApplicationKind.Brave => "Brave",
        _ => Kind.ToString(),
    };

    public string ProcessHint => Kind switch
    {
        AudioApplicationKind.Spotify => "Spotify.exe / version Microsoft Store",
        AudioApplicationKind.Discord => "Discord, PTB ou Canary",
        AudioApplicationKind.Chrome => "chrome.exe",
        AudioApplicationKind.Edge => "msedge.exe",
        AudioApplicationKind.OperaGx => "opera.exe",
        AudioApplicationKind.Firefox => "firefox.exe",
        AudioApplicationKind.Brave => "brave.exe",
        _ => string.Empty,
    };

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                selectionChanged(this);
            }
        }
    }

    public int RunningInstanceCount
    {
        get => runningInstanceCount;
        private set
        {
            if (SetProperty(ref runningInstanceCount, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsRunning => RunningInstanceCount > 0;

    public bool DetectionAvailable
    {
        get => detectionAvailable;
        private set
        {
            if (SetProperty(ref detectionAvailable, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => !DetectionAvailable
        ? "Détection indisponible"
        : IsRunning
            ? $"Détecté · {RunningInstanceCount} processus"
            : "Non détecté";

    internal void SetSelectedWithoutNotification(bool value)
    {
        _ = SetProperty(ref isSelected, value, nameof(IsSelected));
    }

    internal void SetRunningInstanceCount(int value)
    {
        DetectionAvailable = true;
        RunningInstanceCount = Math.Max(0, value);
    }

    internal void SetDetectionUnavailable()
    {
        RunningInstanceCount = 0;
        DetectionAvailable = false;
    }
}
