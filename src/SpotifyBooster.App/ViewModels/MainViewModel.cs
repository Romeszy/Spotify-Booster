using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using SpotifyBooster.App.Mvvm;
using SpotifyBooster.App.Navigation;
using SpotifyBooster.App.Services;
using SpotifyBooster.Audio.Devices;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Core.Models;
using SpotifyBooster.Core.Services;
using SpotifyBooster.Dsp;

namespace SpotifyBooster.App.ViewModels;

public enum UiNotificationSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int SetupStepCount = 7;
    private static readonly TimeSpan StartupWaitTimeout =
        TimeSpan.FromSeconds(30);
    private readonly IAudioDeviceService deviceService;
    private readonly IAudioEngine audioEngine;
    private readonly ISettingsService settingsService;
    private readonly ISpotifyProcessDetector spotifyDetector;
    private readonly IDiagnosticReportService diagnosticReportService;
    private readonly IDiagnosticFileExporter diagnosticFileExporter;
    private readonly IWindowsSettingsLauncher windowsSettingsLauncher;
    private readonly IStartupService startupService;
    private readonly IErrorSink errorSink;
    private readonly ApplicationState applicationState;
    private readonly ILogger<MainViewModel> logger;
    private readonly DispatcherTimer spotifyTimer;
    private readonly DispatcherTimer metricsTimer;
    private readonly SemaphoreSlim saveSemaphore = new(1, 1);
    private CancellationTokenSource? saveDebounce;
    private AppSettings settings = new();
    private IReadOnlyList<AudioDeviceInfo> captureInventory = [];
    private IReadOnlyList<AudioDeviceInfo> renderInventory = [];
    private AudioDeviceInfo? selectedSource;
    private AudioDeviceInfo? selectedOutput;
    private NavigationPage currentPage = NavigationPage.Dashboard;
    private AudioProtectionMode protectionMode = AudioProtectionMode.Protected;
    private AudioProtectionMode? pendingProtectionMode;
    private double mainGainDb;
    private double bassGainDb = 3;
    private double bassFrequencyHz = 90;
    private double bassSlope = 1;
    private string selectedProfile = "Équilibré";
    private bool dcBlockEnabled = true;
    private bool bassBoostEnabled = true;
    private bool equalizerEnabled;
    private double equalizerLowDb;
    private double equalizerMidDb;
    private double equalizerHighDb;
    private bool compressorEnabled;
    private bool softClipperEnabled = true;
    private bool limiterEnabled = true;
    private bool headphonesSafetyMode = true;
    private bool startWithWindows;
    private bool startMinimized;
    private bool startProcessingAutomatically;
    private bool notificationsEnabled = true;
    private bool globalHotkeysEnabled;
    private bool detailedDiagnostics;
    private bool spotifyDetected;
    private AudioEngineState engineState = AudioEngineState.Stopped;
    private bool busy;
    private bool clippingDetected;
    private bool limiterWorking;
    private bool inputSignalPresent;
    private double inputLevel;
    private double outputLevel;
    private string inputPeakText = "−∞ dBFS";
    private string outputPeakText = "−∞ dBFS";
    private string limiterReductionText = "0,00 dB";
    private string engineStatusText = "Moteur arrêté";
    private string lastExportMessage = string.Empty;
    private string notificationTitle = string.Empty;
    private string notificationMessage = string.Empty;
    private UiNotificationSeverity notificationSeverity = UiNotificationSeverity.Info;
    private bool expertConfirmationVisible;
    private int setupStep = 1;
    private bool setupPanicTested;
    private AudioEngineMetrics lastMetrics = AudioEngineMetrics.Empty;
    private bool suppressSettingsChanges;
    private int spotifyRefreshInProgress;
    private int initialized;
    private int disposed;

    public MainViewModel(
        IAudioDeviceService deviceService,
        IAudioEngine audioEngine,
        ISettingsService settingsService,
        ISpotifyProcessDetector spotifyDetector,
        IDiagnosticReportService diagnosticReportService,
        IDiagnosticFileExporter diagnosticFileExporter,
        IWindowsSettingsLauncher windowsSettingsLauncher,
        IStartupService startupService,
        IErrorSink errorSink,
        ApplicationState applicationState,
        ILogger<MainViewModel> logger)
    {
        this.deviceService = deviceService;
        this.audioEngine = audioEngine;
        this.settingsService = settingsService;
        this.spotifyDetector = spotifyDetector;
        this.diagnosticReportService = diagnosticReportService;
        this.diagnosticFileExporter = diagnosticFileExporter;
        this.windowsSettingsLauncher = windowsSettingsLauncher;
        this.startupService = startupService;
        this.errorSink = errorSink;
        this.applicationState = applicationState;
        this.logger = logger;
        RefreshProfiles();

        NavigateCommand = new RelayCommand(Navigate);
        RefreshDevicesCommand = new AsyncRelayCommand(
            RefreshDevicesAsync,
            HandleCommandException,
            () => CanChangeDevices);
        ToggleEngineCommand = new AsyncRelayCommand(
            ToggleEngineAsync,
            HandleCommandException,
            () => CanActivate);
        PanicCommand = new RelayCommand(_ => PanicReset());
        OpenVolumeMixerCommand = new RelayCommand(_ => OpenVolumeMixer());
        OpenSoundSettingsCommand = new RelayCommand(_ => OpenSoundSettings());
        OpenVbCableWebsiteCommand = new RelayCommand(_ => OpenVbCableWebsite());
        PreviousSetupStepCommand = new RelayCommand(
            _ => SetupStep--,
            _ => SetupStep > 1);
        NextSetupStepCommand = new RelayCommand(
            _ => AdvanceSetup(),
            _ => SetupStep < SetupStepCount);
        CompleteSetupCommand = new AsyncRelayCommand(
            CompleteSetupAsync,
            HandleCommandException);
        RelaunchSetupCommand = new RelayCommand(_ => RelaunchSetup());
        ConfirmExpertModeCommand = new RelayCommand(_ => ConfirmExpertMode());
        CancelExpertModeCommand = new RelayCommand(_ => CancelExpertMode());
        ExportDiagnosticCommand = new AsyncRelayCommand(
            ExportDiagnosticAsync,
            HandleCommandException);
        DismissNotificationCommand = new RelayCommand(_ => ClearNotification());

        spotifyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        spotifyTimer.Tick += OnSpotifyTimerTick;

        metricsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        metricsTimer.Tick += OnMetricsTimerTick;

        audioEngine.StatusChanged += OnEngineStatusChanged;
    }

    public ObservableCollection<AudioDeviceInfo> CaptureDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> RenderDevices { get; } = [];

    public ObservableCollection<SpotifyProcessInfo> SpotifyProcesses { get; } = [];

    public ObservableCollection<string> Profiles { get; } = [];

    public IReadOnlyList<AudioProtectionMode> ProtectionModes { get; } =
        Enum.GetValues<AudioProtectionMode>();

    public IReadOnlyList<WindowCloseBehavior> CloseBehaviors { get; } =
        Enum.GetValues<WindowCloseBehavior>();

    public RelayCommand NavigateCommand { get; }

    public AsyncRelayCommand RefreshDevicesCommand { get; }

    public AsyncRelayCommand ToggleEngineCommand { get; }

    public RelayCommand PanicCommand { get; }

    public RelayCommand OpenVolumeMixerCommand { get; }

    public RelayCommand OpenSoundSettingsCommand { get; }

    public RelayCommand OpenVbCableWebsiteCommand { get; }

    public RelayCommand PreviousSetupStepCommand { get; }

    public RelayCommand NextSetupStepCommand { get; }

    public AsyncRelayCommand CompleteSetupCommand { get; }

    public RelayCommand RelaunchSetupCommand { get; }

    public RelayCommand ConfirmExpertModeCommand { get; }

    public RelayCommand CancelExpertModeCommand { get; }

    public AsyncRelayCommand ExportDiagnosticCommand { get; }

    public RelayCommand DismissNotificationCommand { get; }

    public AppSettings CurrentSettings => settings;

    public NavigationPage CurrentPage
    {
        get => currentPage;
        set
        {
            if (SetProperty(ref currentPage, value))
            {
                OnPropertyChanged(nameof(PageTitle));
            }
        }
    }

    public string PageTitle => CurrentPage switch
    {
        NavigationPage.Setup => "Assistant VB-CABLE",
        NavigationPage.Settings => "Réglages",
        NavigationPage.Diagnostics => "Diagnostic audio",
        _ => "Contrôle",
    };

    public AudioDeviceInfo? SelectedSource
    {
        get => selectedSource;
        set
        {
            if (!SetProperty(ref selectedSource, value))
            {
                return;
            }

            RaiseRouteProperties();
            if (!suppressSettingsChanges)
            {
                UpdateSettings(settings with
                {
                    SourceDeviceId = value?.Id,
                });
            }
        }
    }

    public AudioDeviceInfo? SelectedOutput
    {
        get => selectedOutput;
        set
        {
            if (!SetProperty(ref selectedOutput, value))
            {
                return;
            }

            RaiseRouteProperties();
            if (!suppressSettingsChanges)
            {
                UpdateSettings(settings with
                {
                    OutputDeviceId = value?.Id,
                });
            }
        }
    }

    public AudioProtectionMode ProtectionMode
    {
        get => protectionMode;
        set
        {
            if (value == protectionMode)
            {
                return;
            }

            if (value == AudioProtectionMode.RawExpert
                && !settings.Expert75DbAcknowledged)
            {
                pendingProtectionMode = value;
                ExpertConfirmationVisible = true;
                OnPropertyChanged();
                return;
            }

            ApplyProtectionMode(value);
        }
    }

    public string ProtectionModeText => ProtectionMode switch
    {
        AudioProtectionMode.Direct => "Direct",
        AudioProtectionMode.RawExpert => "Expert brut",
        _ => "Protégé",
    };

    public string ProtectionModeDescription => ProtectionMode switch
    {
        AudioProtectionMode.Direct =>
            "Gain, Bass Boost et égaliseur. Aucune dynamique imposée.",
        AudioProtectionMode.RawExpert =>
            "Jusqu’à +75 dB, sans réduction automatique ni protection cachée.",
        _ =>
            "Soft clipper et limiteur actifs. Le gain demandé ne change jamais tout seul.",
    };

    public double MainGainDb
    {
        get => mainGainDb;
        set
        {
            double safe = ProtectionMode == AudioProtectionMode.RawExpert
                ? AudioProcessingSettings.QuantizeRawExpertGain(value)
                : Math.Round(
                    Math.Clamp(
                        value,
                        AudioProcessingSettings.MinimumGainDb,
                        GainMaximumDb),
                    1,
                    MidpointRounding.AwayFromZero);
            if (!SetProperty(ref mainGainDb, safe))
            {
                return;
            }

            RaiseGainProperties();
            ApplyProcessingChange(processing => processing with
            {
                MainGainDb = safe,
            });
        }
    }

    public double GainMaximumDb =>
        ProtectionMode == AudioProtectionMode.RawExpert
            ? AudioProcessingSettings.RawExpertMaximumGainDb
            : AudioProcessingSettings.StandardMaximumGainDb;

    public double GainStepDb =>
        ProtectionMode == AudioProtectionMode.RawExpert
            ? AudioProcessingSettings.RawExpertGainStepDb
            : 0.1;

    public double LinearGainFactor =>
        GainConversion.DecibelsToFactorDouble(MainGainDb);

    public string GainDbText => string.Create(
        CultureInfo.CurrentCulture,
        $"{MainGainDb:+0.0;-0.0;0.0} dB");

    public string GainPercentText
    {
        get
        {
            double percent = GainConversion.DecibelsToPercentDouble(MainGainDb);
            return percent >= 1_000_000_000
                ? string.Create(
                    CultureInfo.CurrentCulture,
                    $"{percent:0.###E+0} %")
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"{percent:N0} %");
        }
    }

    public bool DcBlockEnabled
    {
        get => dcBlockEnabled;
        set
        {
            if (SetProperty(ref dcBlockEnabled, value))
            {
                ApplyProcessingChange(processing => processing with
                {
                    DcBlockEnabled = value,
                });
            }
        }
    }

    public bool BassBoostEnabled
    {
        get => bassBoostEnabled;
        set
        {
            if (SetProperty(ref bassBoostEnabled, value))
            {
                ApplyProcessingChange(processing => processing with
                {
                    BassBoostEnabled = value,
                });
            }
        }
    }

    public double BassGainDb
    {
        get => bassGainDb;
        set
        {
            double safe = Math.Round(Math.Clamp(value, 0, 18), 1);
            if (!SetProperty(ref bassGainDb, safe))
            {
                return;
            }

            OnPropertyChanged(nameof(BassGainText));
            SetCustomProfile();
            ApplyProcessingChange(processing => processing with
            {
                BassGainDb = safe,
                BassBoostEnabled = bassBoostEnabled,
                BassProfile = BassProfile.Custom,
            });
        }
    }

    public string BassGainText => string.Create(
        CultureInfo.CurrentCulture,
        $"{BassGainDb:+0.0;-0.0;0.0} dB");

    public double BassFrequencyHz
    {
        get => bassFrequencyHz;
        set
        {
            double safe = Math.Round(Math.Clamp(value, 35, 200));
            if (!SetProperty(ref bassFrequencyHz, safe))
            {
                return;
            }

            OnPropertyChanged(nameof(BassFrequencyText));
            SetCustomProfile();
            ApplyProcessingChange(processing => processing with
            {
                BassFrequencyHz = safe,
                BassProfile = BassProfile.Custom,
            });
        }
    }

    public string BassFrequencyText => $"{BassFrequencyHz:0} Hz";

    public double BassSlope
    {
        get => bassSlope;
        set
        {
            double safe = Math.Round(Math.Clamp(value, 0.25, 2), 2);
            if (!SetProperty(ref bassSlope, safe))
            {
                return;
            }

            OnPropertyChanged(nameof(BassSlopeText));
            SetCustomProfile();
            ApplyProcessingChange(processing => processing with
            {
                BassSlope = safe,
                BassProfile = BassProfile.Custom,
            });
        }
    }

    public string BassSlopeText => $"Pente {BassSlope:0.00}";

    public string SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value)
                || suppressSettingsChanges
                || string.Equals(value, "Personnalisé", StringComparison.Ordinal))
            {
                return;
            }

            ApplyProfile(value);
        }
    }

    public bool EqualizerEnabled
    {
        get => equalizerEnabled;
        set
        {
            if (SetProperty(ref equalizerEnabled, value))
            {
                ApplyProcessingChange(processing => processing with
                {
                    EqualizerEnabled = value,
                });
            }
        }
    }

    public double EqualizerLowDb
    {
        get => equalizerLowDb;
        set => SetEqualizerBand(ref equalizerLowDb, value, nameof(EqualizerLowDb));
    }

    public double EqualizerMidDb
    {
        get => equalizerMidDb;
        set => SetEqualizerBand(ref equalizerMidDb, value, nameof(EqualizerMidDb));
    }

    public double EqualizerHighDb
    {
        get => equalizerHighDb;
        set => SetEqualizerBand(ref equalizerHighDb, value, nameof(EqualizerHighDb));
    }

    public bool CompressorEnabled
    {
        get => compressorEnabled;
        set
        {
            if (SetProperty(ref compressorEnabled, value))
            {
                ApplyProcessingChange(processing => processing with
                {
                    CompressorEnabled = value,
                });
            }
        }
    }

    public double CompressorThresholdDb
    {
        get => settings.Processing.CompressorThresholdDb;
        set => SetProcessingParameter(
            value,
            -60,
            0,
            nameof(CompressorThresholdDb),
            (processing, safe) => processing with
            {
                CompressorThresholdDb = safe,
            });
    }

    public double CompressorRatio
    {
        get => settings.Processing.CompressorRatio;
        set => SetProcessingParameter(
            value,
            1,
            20,
            nameof(CompressorRatio),
            (processing, safe) => processing with
            {
                CompressorRatio = safe,
            });
    }

    public double CompressorAttackMs
    {
        get => settings.Processing.CompressorAttackMs;
        set => SetProcessingParameter(
            value,
            0.1,
            200,
            nameof(CompressorAttackMs),
            (processing, safe) => processing with
            {
                CompressorAttackMs = safe,
            });
    }

    public double CompressorReleaseMs
    {
        get => settings.Processing.CompressorReleaseMs;
        set => SetProcessingParameter(
            value,
            10,
            2_000,
            nameof(CompressorReleaseMs),
            (processing, safe) => processing with
            {
                CompressorReleaseMs = safe,
            });
    }

    public double CompressorMakeupGainDb
    {
        get => settings.Processing.CompressorMakeupGainDb;
        set => SetProcessingParameter(
            value,
            -12,
            24,
            nameof(CompressorMakeupGainDb),
            (processing, safe) => processing with
            {
                CompressorMakeupGainDb = safe,
            });
    }

    public bool SoftClipperEnabled
    {
        get => softClipperEnabled;
        set
        {
            if (!value && ProtectionMode == AudioProtectionMode.Protected)
            {
                ShowNotification(
                    UiNotificationSeverity.Warning,
                    "Mode Protégé",
                    "Le soft clipper reste actif dans ce mode. Passez en Direct ou Expert brut pour le désactiver.");
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref softClipperEnabled, value))
            {
                ApplyProcessingChange(processing => processing with
                {
                    SoftClipperEnabled = value,
                });
            }
        }
    }

    public double SoftClipperThresholdDb
    {
        get => settings.Processing.SoftClipperThresholdDb;
        set => SetProcessingParameter(
            value,
            -18,
            -0.01,
            nameof(SoftClipperThresholdDb),
            (processing, safe) => processing with
            {
                SoftClipperThresholdDb = safe,
            });
    }

    public double SoftClipperDrive
    {
        get => settings.Processing.SoftClipperDrive;
        set => SetProcessingParameter(
            value,
            0.5,
            8,
            nameof(SoftClipperDrive),
            (processing, safe) => processing with
            {
                SoftClipperDrive = safe,
            });
    }

    public bool LimiterEnabled
    {
        get => limiterEnabled;
        set
        {
            if (!value && ProtectionMode == AudioProtectionMode.Protected)
            {
                ShowNotification(
                    UiNotificationSeverity.Warning,
                    "Mode Protégé",
                    "Le limiteur reste actif dans ce mode. Passez en Direct ou Expert brut pour le désactiver.");
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref limiterEnabled, value))
            {
                return;
            }

            OnPropertyChanged(nameof(LimiterStatusText));
            ApplyProcessingChange(processing => processing with
            {
                LimiterEnabled = value,
            });
        }
    }

    public double LimiterThresholdDb
    {
        get => settings.Processing.LimiterThresholdDb;
        set => SetProcessingParameter(
            value,
            -12,
            -0.01,
            nameof(LimiterThresholdDb),
            (processing, safe) => processing with
            {
                LimiterThresholdDb = safe,
            });
    }

    public double LimiterReleaseMs
    {
        get => settings.Processing.LimiterReleaseMs;
        set => SetProcessingParameter(
            value,
            10,
            1_000,
            nameof(LimiterReleaseMs),
            (processing, safe) => processing with
            {
                LimiterReleaseMs = safe,
            });
    }

    public double OutputGainDb
    {
        get => settings.Processing.OutputGainDb;
        set
        {
            SetProcessingParameter(
                value,
                -12,
                ProtectionMode == AudioProtectionMode.Protected ? 0 : 12,
                nameof(OutputGainDb),
                (processing, safe) => processing with
                {
                    OutputGainDb = safe,
                });
            OnPropertyChanged(nameof(SafetyWarningText));
            OnPropertyChanged(nameof(HasSafetyWarning));
        }
    }

    public double ParameterSmoothingMs
    {
        get => settings.Processing.ParameterSmoothingMs;
        set => SetProcessingParameter(
            value,
            0.5,
            250,
            nameof(ParameterSmoothingMs),
            (processing, safe) => processing with
            {
                ParameterSmoothingMs = safe,
            });
    }

    public bool HeadphonesSafetyMode
    {
        get => headphonesSafetyMode;
        set
        {
            if (!SetProperty(ref headphonesSafetyMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SafetyWarningText));
            UpdateSettings(settings with
            {
                HeadphonesSafetyMode = value,
            });
        }
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (value == startWithWindows || suppressSettingsChanges)
            {
                SetProperty(ref startWithWindows, value);
                return;
            }

            if (!startupService.SetEnabled(value))
            {
                ShowNotification(
                    UiNotificationSeverity.Error,
                    "Démarrage Windows",
                    value
                        ? "L’activation a échoué. Aucune entrée de démarrage fiable n’a été conservée."
                        : "La désactivation n’a pas pu être confirmée.");
                OnPropertyChanged();
                return;
            }

            SetProperty(ref startWithWindows, value);
            UpdateSettings(settings with
            {
                StartWithWindows = value,
            });
        }
    }

    public bool StartMinimized
    {
        get => startMinimized;
        set
        {
            if (SetProperty(ref startMinimized, value))
            {
                UpdateSettings(settings with
                {
                    StartMinimized = value,
                });
            }
        }
    }

    public bool StartProcessingAutomatically
    {
        get => startProcessingAutomatically;
        set
        {
            if (SetProperty(ref startProcessingAutomatically, value))
            {
                UpdateSettings(settings with
                {
                    StartProcessingAutomatically = value,
                });
            }
        }
    }

    public bool WaitForDevicesOnStartup
    {
        get => settings.WaitForDevicesOnStartup;
        set
        {
            if (value == settings.WaitForDevicesOnStartup)
            {
                return;
            }

            UpdateSettings(settings with
            {
                WaitForDevicesOnStartup = value,
            });
            OnPropertyChanged();
        }
    }

    public bool WaitForSpotifyOnStartup
    {
        get => settings.WaitForSpotifyOnStartup;
        set
        {
            if (value == settings.WaitForSpotifyOnStartup)
            {
                return;
            }

            UpdateSettings(settings with
            {
                WaitForSpotifyOnStartup = value,
            });
            OnPropertyChanged();
        }
    }

    public WindowCloseBehavior CloseBehavior
    {
        get => settings.CloseBehavior;
        set
        {
            if (value == settings.CloseBehavior)
            {
                return;
            }

            UpdateSettings(settings with
            {
                CloseBehavior = value,
            });
            OnPropertyChanged();
        }
    }

    public bool NotificationsEnabled
    {
        get => notificationsEnabled;
        set
        {
            if (SetProperty(ref notificationsEnabled, value))
            {
                UpdateSettings(settings with
                {
                    NotificationsEnabled = value,
                });
            }
        }
    }

    public bool GlobalHotkeysEnabled
    {
        get => globalHotkeysEnabled;
        set
        {
            if (SetProperty(ref globalHotkeysEnabled, value))
            {
                UpdateSettings(settings with
                {
                    Hotkeys = settings.Hotkeys with
                    {
                        GlobalHotkeysEnabled = value,
                    },
                });
            }
        }
    }

    public string IncreaseGainHotkey
    {
        get => settings.Hotkeys.IncreaseGain;
        set => SetHotkey(
            value,
            nameof(IncreaseGainHotkey),
            (hotkeys, gesture) => hotkeys with
            {
                IncreaseGain = gesture,
            });
    }

    public string DecreaseGainHotkey
    {
        get => settings.Hotkeys.DecreaseGain;
        set => SetHotkey(
            value,
            nameof(DecreaseGainHotkey),
            (hotkeys, gesture) => hotkeys with
            {
                DecreaseGain = gesture,
            });
    }

    public string ToggleProcessingHotkey
    {
        get => settings.Hotkeys.ToggleProcessing;
        set => SetHotkey(
            value,
            nameof(ToggleProcessingHotkey),
            (hotkeys, gesture) => hotkeys with
            {
                ToggleProcessing = gesture,
            });
    }

    public string PanicResetHotkey
    {
        get => settings.Hotkeys.PanicReset;
        set => SetHotkey(
            value,
            nameof(PanicResetHotkey),
            (hotkeys, gesture) => hotkeys with
            {
                PanicReset = gesture,
            });
    }

    public string ToggleBassBoostHotkey
    {
        get => settings.Hotkeys.ToggleBassBoost;
        set => SetHotkey(
            value,
            nameof(ToggleBassBoostHotkey),
            (hotkeys, gesture) => hotkeys with
            {
                ToggleBassBoost = gesture,
            });
    }

    public string ToggleWindowHotkey
    {
        get => settings.Hotkeys.ToggleWindow;
        set => SetHotkey(
            value,
            nameof(ToggleWindowHotkey),
            (hotkeys, gesture) => hotkeys with
            {
                ToggleWindow = gesture,
            });
    }

    public bool ShowLinearPercent
    {
        get => settings.ShowLinearPercent;
        set
        {
            if (value == settings.ShowLinearPercent)
            {
                return;
            }

            UpdateSettings(settings with
            {
                ShowLinearPercent = value,
            });
            OnPropertyChanged();
        }
    }

    public bool DetailedDiagnostics
    {
        get => detailedDiagnostics;
        set
        {
            if (SetProperty(ref detailedDiagnostics, value))
            {
                UpdateSettings(settings with
                {
                    DetailedDiagnostics = value,
                });
            }
        }
    }

    public bool SpotifyDetected
    {
        get => spotifyDetected;
        private set
        {
            if (SetProperty(ref spotifyDetected, value))
            {
                OnPropertyChanged(nameof(SpotifyStatusText));
            }
        }
    }

    public string SpotifyStatusText => SpotifyDetected
        ? $"Spotify détecté · {SpotifyProcesses.Count} processus"
        : "Spotify non détecté";

    public AudioEngineState EngineState
    {
        get => engineState;
        private set
        {
            if (SetProperty(ref engineState, value))
            {
                OnPropertyChanged(nameof(IsEngineRunning));
                OnPropertyChanged(nameof(EngineButtonText));
                RaiseRouteProperties();
                OnPropertyChanged(nameof(InputSignalStatusText));
            }
        }
    }

    public bool IsEngineRunning => EngineState == AudioEngineState.Running;

    public string EngineButtonText => EngineState switch
    {
        AudioEngineState.Running => "Désactiver",
        AudioEngineState.Starting => "Activation…",
        AudioEngineState.Stopping => "Arrêt…",
        AudioEngineState.DeviceLost => "Récupération…",
        AudioEngineState.Faulted => "Réessayer",
        _ => "Activer le traitement",
    };

    public string EngineStatusText
    {
        get => engineStatusText;
        private set => SetProperty(ref engineStatusText, value);
    }

    public bool IsBusy
    {
        get => busy;
        private set
        {
            if (SetProperty(ref busy, value))
            {
                RaiseRouteProperties();
            }
        }
    }

    public bool CanActivate
    {
        get
        {
            if (IsBusy)
            {
                return false;
            }

            if (EngineState == AudioEngineState.Running)
            {
                return true;
            }

            if (EngineState is not (
                AudioEngineState.Stopped
                or AudioEngineState.Faulted))
            {
                return false;
            }

            return HasValidRoute;
        }
    }

    private bool HasValidRoute =>
        SelectedSource is not null
        && SelectedOutput is not null
        && AudioRouteSafety.Validate(
            SelectedSource,
            SelectedOutput).IsValid;

    public bool CanChangeDevices =>
        !IsBusy
        && EngineState is (
            AudioEngineState.Stopped
            or AudioEngineState.Faulted);

    public string RouteStatusText
    {
        get
        {
            if (IsEngineRunning)
            {
                return "Source virtuelle → traitement → sortie physique";
            }

            if (SelectedSource is null)
            {
                return HasVirtualSource
                    ? "Sélectionnez la source virtuelle"
                    : "Aucune source virtuelle active";
            }

            if (!SelectedSource.IsLikelyVirtual)
            {
                return "La source ne ressemble pas à un câble virtuel";
            }

            if (SelectedOutput is null)
            {
                return "Sélectionnez le casque ou les enceintes";
            }

            AudioRouteValidationResult validation = AudioRouteSafety.Validate(
                SelectedSource,
                SelectedOutput);
            return validation.IsValid
                ? "Chemin prêt"
                : validation.Errors[0];
        }
    }

    public bool HasVirtualSource =>
        CaptureDevices.Any(device => device.IsLikelyVirtual);

    public string CableInputStatusText => FormatCableStatus(
        renderInventory.FirstOrDefault(IsVbCableDevice),
        "CABLE Input");

    public string CableOutputStatusText => FormatCableStatus(
        captureInventory.FirstOrDefault(IsVbCableDevice),
        "CABLE Output");

    public string SelectedSourceStatusText => SelectedSource is null
        ? "Source non sélectionnée"
        : $"{SelectedSource.StateDescription} · {SelectedSource.FormatDescription}";

    public string SelectedOutputStatusText => SelectedOutput is null
        ? "Sortie non sélectionnée"
        : $"{SelectedOutput.StateDescription} · {SelectedOutput.FormatDescription}";

    public string SafetyWarningText
    {
        get
        {
            if (ProtectionMode == AudioProtectionMode.RawExpert)
            {
                if (MainGainDb >= 100)
                {
                    return "Gain extrême : le DSP borne les magnitudes internes pour rester fini. Le résultat sera essentiellement saturé ou limité, sans puissance physique supplémentaire.";
                }

                return "Expert brut : aucune protection cachée. Un signal au-dessus de 0 dBFS peut saturer immédiatement.";
            }

            if (OutputGainDb > 0)
            {
                return "Gain de sortie positif : ce réglage intervient après le limiteur et peut recréer un écrêtage.";
            }

            double linearPercent =
                GainConversion.DecibelsToPercentDouble(MainGainDb);
            if (linearPercent >= 300 && HeadphonesSafetyMode)
            {
                return "Casque : augmentez lentement. Le plafond numérique ne protège ni l’audition ni le matériel.";
            }

            if (linearPercent >= 300)
            {
                return "Gain élevé : distorsion et limitation peuvent devenir permanentes.";
            }

            if (MainGainDb > 0)
            {
                return "Le gain numérique augmente le signal, pas les capacités physiques de la sortie.";
            }

            return string.Empty;
        }
    }

    public bool HasSafetyWarning =>
        MainGainDb > 0
        || OutputGainDb > 0
        || ProtectionMode == AudioProtectionMode.RawExpert;

    public double InputLevel
    {
        get => inputLevel;
        private set => SetProperty(ref inputLevel, value);
    }

    public double OutputLevel
    {
        get => outputLevel;
        private set => SetProperty(ref outputLevel, value);
    }

    public string InputPeakText
    {
        get => inputPeakText;
        private set => SetProperty(ref inputPeakText, value);
    }

    public string OutputPeakText
    {
        get => outputPeakText;
        private set => SetProperty(ref outputPeakText, value);
    }

    public string LimiterReductionText
    {
        get => limiterReductionText;
        private set => SetProperty(ref limiterReductionText, value);
    }

    public bool ClippingDetected
    {
        get => clippingDetected;
        private set
        {
            if (SetProperty(ref clippingDetected, value))
            {
                OnPropertyChanged(nameof(ClippingStatusText));
            }
        }
    }

    public string ClippingStatusText => ClippingDetected
        ? "Écrêtage détecté"
        : "Aucun clip récent";

    public bool LimiterWorking
    {
        get => limiterWorking;
        private set
        {
            if (SetProperty(ref limiterWorking, value))
            {
                OnPropertyChanged(nameof(LimiterStatusText));
            }
        }
    }

    public string LimiterStatusText
    {
        get
        {
            if (!LimiterEnabled)
            {
                return "Limiteur désactivé";
            }

            return LimiterWorking
                ? $"Limiteur · −{LimiterReductionText}"
                : "Limiteur prêt · plafond −1 dBFS";
        }
    }

    public bool InputSignalPresent
    {
        get => inputSignalPresent;
        private set
        {
            if (SetProperty(ref inputSignalPresent, value))
            {
                OnPropertyChanged(nameof(InputSignalStatusText));
            }
        }
    }

    public string InputSignalStatusText
    {
        get
        {
            if (!IsEngineRunning)
            {
                return "Moteur inactif";
            }

            return InputSignalPresent
                ? "Signal reçu"
                : "Aucun signal — vérifiez le routage Spotify";
        }
    }

    public string NotificationTitle
    {
        get => notificationTitle;
        private set => SetProperty(ref notificationTitle, value);
    }

    public string NotificationMessage
    {
        get => notificationMessage;
        private set
        {
            if (SetProperty(ref notificationMessage, value))
            {
                OnPropertyChanged(nameof(HasNotification));
                OnPropertyChanged(nameof(LastError));
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public UiNotificationSeverity NotificationSeverity
    {
        get => notificationSeverity;
        private set => SetProperty(ref notificationSeverity, value);
    }

    public bool HasNotification => !string.IsNullOrWhiteSpace(NotificationMessage);

    public string LastError => NotificationMessage;

    public bool HasError => HasNotification;

    public bool ExpertConfirmationVisible
    {
        get => expertConfirmationVisible;
        private set => SetProperty(ref expertConfirmationVisible, value);
    }

    public int SetupStep
    {
        get => setupStep;
        private set
        {
            int safe = Math.Clamp(value, 1, SetupStepCount);
            if (SetProperty(ref setupStep, safe))
            {
                OnPropertyChanged(nameof(SetupProgressText));
                OnPropertyChanged(nameof(SetupStepTitle));
                PreviousSetupStepCommand.NotifyCanExecuteChanged();
                NextSetupStepCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SetupProgressText => $"Étape {SetupStep} sur {SetupStepCount}";

    public string SetupStepTitle => SetupStep switch
    {
        1 => "Comprendre le chemin audio",
        2 => "Installer VB-CABLE séparément",
        3 => "Vérifier les deux côtés du câble",
        4 => "Router uniquement Spotify",
        5 => "Choisir la source et la sortie",
        6 => "Tester le signal à 0 dB",
        _ => "Dépannage et fin de configuration",
    };

    public bool SetupPanicTested
    {
        get => setupPanicTested;
        private set => SetProperty(ref setupPanicTested, value);
    }

    public string LastExportMessage
    {
        get => lastExportMessage;
        private set => SetProperty(ref lastExportMessage, value);
    }

    public string DiagnosticEngineText =>
        $"{audioEngine.Status.State} · {audioEngine.Status.Message}";

    public string DiagnosticFormatsText =>
        $"Source : {SelectedSource?.Name ?? "non sélectionnée"}\n"
        + $"Format source : {audioEngine.Status.SourceFormat
            ?? SelectedSource?.FormatDescription
            ?? "indisponible"}\n"
        + $"Sortie : {SelectedOutput?.Name ?? "non sélectionnée"}\n"
        + $"Format sortie : {audioEngine.Status.OutputFormat
            ?? SelectedOutput?.FormatDescription
            ?? "indisponible"}";

    public string DiagnosticPipelineText
    {
        get
        {
            AudioProcessingSettings processing = settings.Processing;
            return string.Create(
                CultureInfo.CurrentCulture,
                $"Mode : {ProtectionModeText}\n"
                + $"DC blocker : {StateText(processing.DcBlockEnabled)}\n"
                + $"Gain demandé : {processing.MainGainDb:+0.0;-0.0;0.0} dB\n"
                + $"Gain appliqué : {lastMetrics.AppliedMainGainDb:+0.0;-0.0;0.0} dB · ×{FormatLinearFactor(lastMetrics.LinearGainFactor)}\n"
                + $"Bass Boost : {StateText(processing.BassBoostEnabled)} · {processing.BassFrequencyHz:0} Hz · {processing.BassGainDb:+0.0;-0.0;0.0} dB\n"
                + $"Égaliseur : {StateText(processing.EqualizerEnabled)}\n"
                + $"Compresseur : {StateText(processing.CompressorEnabled)} · réduction {lastMetrics.CompressorReductionDb:0.00} dB\n"
                + $"Soft clipper : {StateText(processing.SoftClipperEnabled)}\n"
                + $"Limiteur : {StateText(processing.LimiterEnabled)} · réduction {lastMetrics.LimiterReductionDb:0.00} dB\n"
                + $"Gain de sortie : {processing.OutputGainDb:+0.0;-0.0;0.0} dB");
        }
    }

    public string DiagnosticLevelsText => string.Create(
        CultureInfo.CurrentCulture,
        $"Entrée : {FormatDbfs(lastMetrics.InputPeak)}\n"
        + $"Après gain : {FormatDbfs(lastMetrics.AfterMainGainPeak)}\n"
        + $"Après Bass Boost : {FormatDbfs(lastMetrics.AfterBassBoostPeak)}\n"
        + $"Après compresseur : {FormatDbfs(lastMetrics.AfterCompressorPeak)}\n"
        + $"Après limiteur : {FormatDbfs(lastMetrics.AfterLimiterPeak)}\n"
        + $"Sortie : {FormatDbfs(lastMetrics.OutputPeak)}");

    public string DiagnosticMetricsText
    {
        get
        {
            AudioEngineMetrics metrics = lastMetrics;
            return string.Create(
                CultureInfo.CurrentCulture,
                $"Fréquence : {metrics.SampleRate:N0} Hz · {metrics.Channels} canaux\n"
                + $"Buffer : {metrics.BufferMilliseconds} ms · latence estimée : {metrics.EstimatedLatencyMilliseconds:F1} ms\n"
                + $"Charge DSP : {metrics.ProcessingLoadPercent:F2} % · buffers : {metrics.BufferCount:N0}\n"
                + $"Underruns : {metrics.BufferUnderrunCount:N0} · overruns : {metrics.BufferOverrunCount:N0}\n"
                + $"Clips : {metrics.ClipCount:N0} · soft clips : {metrics.SoftClipCount:N0} · frames limitées : {metrics.LimitedFrameCount:N0}\n"
                + $"Échantillons assainis : {metrics.SanitizedSampleCount:N0} · durée : {metrics.Uptime:hh\\:mm\\:ss}");
        }
    }

    public string DiagnosticSpotifyText =>
        SpotifyProcesses.Count == 0
            ? "Aucun processus Spotify détecté."
            : string.Join(
                Environment.NewLine,
                SpotifyProcesses.Select(process =>
                    $"{process.ProcessName} · PID {process.ProcessId} · {process.Distribution}"));

    public string DiagnosticErrorsText
    {
        get
        {
            IReadOnlyList<DiagnosticError> errors = errorSink.GetRecent();
            return errors.Count == 0
                ? "Aucune erreur récente."
                : string.Join(
                    Environment.NewLine,
                    errors.Select(error =>
                        $"{error.Timestamp:HH:mm:ss} · {error.Component} · {error.Message}"));
        }
    }

    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        IsBusy = true;
        suppressSettingsChanges = true;
        try
        {
            settings = await settingsService.LoadAsync();
            bool actualStartup = startupService.IsEnabled();
            if (settings.StartWithWindows != actualStartup)
            {
                settings = settings with
                {
                    StartWithWindows = actualStartup,
                };
            }

            LoadFieldsFromSettings();
            await RefreshDevicesCoreAsync();
            await RefreshSpotifyAsync();
            applicationState.SetSettings(settings);
            if (!settings.FirstRunCompleted)
            {
                CurrentPage = NavigationPage.Setup;
                SetupStep = 1;
            }

            spotifyTimer.Start();
            metricsTimer.Start();
        }
        finally
        {
            suppressSettingsChanges = false;
            IsBusy = false;
        }

        if (settings.StartProcessingAutomatically
            && settings.FirstRunCompleted)
        {
            await WaitForStartupPrerequisitesAsync();
            bool spotifyReady =
                !settings.WaitForSpotifyOnStartup || SpotifyDetected;
            if (HasValidRoute && spotifyReady)
            {
                await ToggleEngineAsync();
            }
            else
            {
                ShowNotification(
                    UiNotificationSeverity.Warning,
                    "Activation automatique en attente",
                    spotifyReady
                        ? "La route audio configurée n’est pas disponible."
                        : "Spotify n’a pas été détecté avant la fin du délai de démarrage.");
            }
        }

        ScheduleSave();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        spotifyTimer.Stop();
        spotifyTimer.Tick -= OnSpotifyTimerTick;
        metricsTimer.Stop();
        metricsTimer.Tick -= OnMetricsTimerTick;
        audioEngine.StatusChanged -= OnEngineStatusChanged;
        CancellationTokenSource? debounce = Interlocked.Exchange(ref saveDebounce, null);
        debounce?.Cancel();
        debounce?.Dispose();

        if (Volatile.Read(ref initialized) != 0)
        {
            await SaveNowAsync();
        }
    }

    public void AdjustGainDb(double delta)
    {
        MainGainDb += delta;
    }

    public void SetGainDb(double value)
    {
        MainGainDb = value;
    }

    public void ToggleBassBoost()
    {
        BassBoostEnabled = !BassBoostEnabled;
    }

    public void SelectProfile(string profile)
    {
        if (Profiles.Contains(profile, StringComparer.Ordinal))
        {
            SelectedProfile = profile;
        }
    }

    public void SelectProtectionMode(AudioProtectionMode mode)
    {
        ProtectionMode = mode;
    }

    public void PanicReset()
    {
        suppressSettingsChanges = true;
        try
        {
            mainGainDb = 0;
            OnPropertyChanged(nameof(MainGainDb));
            RaiseGainProperties();
            settings = settings with
            {
                Processing = settings.Processing with
                {
                    MainGainDb = 0,
                    OutputGainDb = 0,
                },
            };
            OnPropertyChanged(nameof(OutputGainDb));
        }
        finally
        {
            suppressSettingsChanges = false;
        }

        applicationState.SetSettings(settings);
        audioEngine.UpdateProcessingSettings(settings.Processing);
        audioEngine.PanicReset();
        if (SetupStep == 6)
        {
            SetupPanicTested = true;
        }

        ScheduleSave();
        ShowNotification(
            UiNotificationSeverity.Success,
            "Retour à 0 dB",
            "Le gain principal et le gain de sortie reviennent rapidement au niveau neutre.");
    }

    private void Navigate(object? parameter)
    {
        if (parameter is string value
            && Enum.TryParse(value, ignoreCase: true, out NavigationPage page))
        {
            CurrentPage = page;
        }
    }

    private async Task RefreshDevicesAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshDevicesCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshDevicesCoreAsync()
    {
        deviceService.ClearEnumerationError();
        (
            IReadOnlyList<AudioDeviceInfo> captures,
            IReadOnlyList<AudioDeviceInfo> renders,
            IReadOnlyList<AudioDeviceInfo> allCaptures,
            IReadOnlyList<AudioDeviceInfo> allRenders) = await Task.Run(() =>
            (
                deviceService.GetCaptureDevices(),
                deviceService.GetRenderDevices(),
                deviceService.GetCaptureDeviceInventory(),
                deviceService.GetRenderDeviceInventory()));
        string? enumerationError = deviceService.LastEnumerationError;
        captureInventory = allCaptures;
        renderInventory = allRenders;

        string? sourceId = SelectedSource?.Id ?? settings.SourceDeviceId;
        string? outputId = SelectedOutput?.Id ?? settings.OutputDeviceId;
        CaptureDevices.Clear();
        foreach (AudioDeviceInfo capture in captures)
        {
            CaptureDevices.Add(capture);
        }

        RenderDevices.Clear();
        foreach (AudioDeviceInfo render in renders)
        {
            RenderDevices.Add(render);
        }

        SelectedSource = captures.FirstOrDefault(device =>
                string.Equals(device.Id, sourceId, StringComparison.Ordinal))
            ?? captures.FirstOrDefault(device => device.IsLikelyVirtual);
        SelectedOutput = renders.FirstOrDefault(device =>
                string.Equals(device.Id, outputId, StringComparison.Ordinal))
            ?? renders.FirstOrDefault(device => device.IsDefault && !device.IsLikelyVirtual)
            ?? renders.FirstOrDefault(device => !device.IsLikelyVirtual);
        applicationState.SetDevices(captures, renders);
        RaiseRouteProperties();
        OnPropertyChanged(nameof(CableInputStatusText));
        OnPropertyChanged(nameof(CableOutputStatusText));
        if (!string.IsNullOrWhiteSpace(enumerationError))
        {
            ShowNotification(
                UiNotificationSeverity.Error,
                "API audio indisponible",
                enumerationError);
            OnPropertyChanged(nameof(DiagnosticErrorsText));
        }
    }

    private async Task ToggleEngineAsync()
    {
        IsBusy = true;
        try
        {
            if (IsEngineRunning)
            {
                await audioEngine.StopAsync();
                return;
            }

            if (SelectedSource is null || SelectedOutput is null)
            {
                ShowNotification(
                    UiNotificationSeverity.Warning,
                    "Chemin audio incomplet",
                    "Sélectionnez une source virtuelle et une sortie physique avant d’activer.");
                return;
            }

            AudioRouteValidationResult validation = AudioRouteSafety.Validate(
                SelectedSource,
                SelectedOutput);
            if (!validation.IsValid)
            {
                ShowNotification(
                    UiNotificationSeverity.Error,
                    "Route refusée",
                    string.Join(" ", validation.Errors));
                return;
            }

            await SaveNowAsync();
            await audioEngine.StartAsync(
                new AudioRoute(SelectedSource.Id, SelectedOutput.Id),
                settings.Processing);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenVolumeMixer()
    {
        if (!windowsSettingsLauncher.OpenVolumeMixer())
        {
            ShowNotification(
                UiNotificationSeverity.Error,
                "Mélangeur Windows",
                "Ouvrez Paramètres > Système > Son > Mélangeur de volume.");
        }
    }

    private void OpenSoundSettings()
    {
        if (!windowsSettingsLauncher.OpenSoundSettings())
        {
            ShowNotification(
                UiNotificationSeverity.Error,
                "Paramètres Son",
                "La page Son n’a pas pu être ouverte automatiquement.");
        }
    }

    private void OpenVbCableWebsite()
    {
        if (!windowsSettingsLauncher.OpenVbCableWebsite())
        {
            ShowNotification(
                UiNotificationSeverity.Error,
                "Site VB-Audio",
                "La page officielle n’a pas pu être ouverte.");
        }
    }

    private void AdvanceSetup()
    {
        if (SetupStep == 5
            && (SelectedSource is null || SelectedOutput is null))
        {
            ShowNotification(
                UiNotificationSeverity.Warning,
                "Sélection incomplète",
                "Vous pouvez continuer pour lire le dépannage, mais le test audio exige une source et une sortie.");
        }

        if (SetupStep == 5)
        {
            MainGainDb = 0;
        }

        SetupStep++;
    }

    private async Task CompleteSetupAsync()
    {
        settings = settings with
        {
            FirstRunCompleted = true,
            Processing = settings.Processing with
            {
                Mode = AudioProtectionMode.Protected,
                MainGainDb = 0,
                OutputGainDb = 0,
                CompressorEnabled = false,
                SoftClipperEnabled = true,
                LimiterEnabled = true,
            },
        };
        LoadFieldsFromSettings();
        audioEngine.UpdateProcessingSettings(settings.Processing);
        await SaveNowAsync();
        applicationState.SetSettings(settings);
        CurrentPage = NavigationPage.Dashboard;
        ShowNotification(
            UiNotificationSeverity.Success,
            "Assistant terminé",
            "Le réglage démarre à 0 dB en mode Protégé. Augmentez progressivement.");
    }

    private void RelaunchSetup()
    {
        SetupStep = 1;
        SetupPanicTested = false;
        CurrentPage = NavigationPage.Setup;
    }

    private async Task WaitForStartupPrerequisitesAsync()
    {
        bool waitForDevices =
            settings.WaitForDevicesOnStartup && !HasValidRoute;
        bool waitForSpotify =
            settings.WaitForSpotifyOnStartup && !SpotifyDetected;
        if (!waitForDevices && !waitForSpotify)
        {
            return;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + StartupWaitTimeout;
        IsBusy = true;
        EngineStatusText = "Attente des prérequis de démarrage…";
        try
        {
            while (DateTimeOffset.UtcNow < deadline
                   && Volatile.Read(ref disposed) == 0)
            {
                if (waitForDevices)
                {
                    await RefreshDevicesCoreAsync();
                    waitForDevices = !HasValidRoute;
                }

                if (waitForSpotify)
                {
                    await RefreshSpotifyAsync();
                    waitForSpotify = !SpotifyDetected;
                }

                if (!waitForDevices && !waitForSpotify)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
        finally
        {
            IsBusy = false;
            if (!IsEngineRunning)
            {
                EngineStatusText = "Moteur arrêté";
            }
        }
    }

    private void ConfirmExpertMode()
    {
        settings = settings with
        {
            Expert75DbAcknowledged = true,
        };
        ExpertConfirmationVisible = false;
        AudioProtectionMode target =
            pendingProtectionMode ?? AudioProtectionMode.RawExpert;
        pendingProtectionMode = null;
        ApplyProtectionMode(target);
    }

    private void CancelExpertMode()
    {
        pendingProtectionMode = null;
        ExpertConfirmationVisible = false;
        OnPropertyChanged(nameof(ProtectionMode));
    }

    private async Task ExportDiagnosticAsync()
    {
        var snapshot = new DiagnosticSnapshot(
            DateTimeOffset.UtcNow,
            SpotifyProcesses.ToArray(),
            SelectedSource,
            SelectedOutput,
            audioEngine.Status,
            errorSink.GetRecent(),
            settings.Processing);
        string report = diagnosticReportService.CreateTextReport(snapshot);
        string? path = await diagnosticFileExporter.ExportAsync(report);
        LastExportMessage = path is null
            ? "Export annulé."
            : $"Rapport exporté : {Path.GetFileName(path)}";
    }

    private async void OnSpotifyTimerTick(object? sender, EventArgs eventArgs)
    {
        await RefreshSpotifyAsync();
    }

    private async Task RefreshSpotifyAsync()
    {
        if (Interlocked.Exchange(ref spotifyRefreshInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<SpotifyProcessInfo> processes =
                await spotifyDetector.DetectAsync();
            SpotifyProcesses.Clear();
            foreach (SpotifyProcessInfo process in processes)
            {
                SpotifyProcesses.Add(process);
            }

            SpotifyDetected = processes.Count > 0;
            applicationState.SetSpotifyProcesses(processes);
            OnPropertyChanged(nameof(SpotifyStatusText));
            OnPropertyChanged(nameof(DiagnosticSpotifyText));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "La détection Spotify a échoué.");
            errorSink.Report("Spotify", "La détection des processus a échoué.");
            OnPropertyChanged(nameof(DiagnosticErrorsText));
        }
        finally
        {
            Volatile.Write(ref spotifyRefreshInProgress, 0);
        }
    }

    private void ApplyProtectionMode(AudioProtectionMode mode)
    {
        AudioProcessingSettings processing = settings.Processing with
        {
            Mode = mode,
        };
        processing = mode switch
        {
            AudioProtectionMode.Protected => processing with
            {
                MainGainDb = Math.Min(
                    processing.MainGainDb,
                    AudioProcessingSettings.StandardMaximumGainDb),
                OutputGainDb = Math.Min(processing.OutputGainDb, 0),
                SoftClipperEnabled = true,
                LimiterEnabled = true,
            },
            AudioProtectionMode.Direct => processing with
            {
                MainGainDb = Math.Min(
                    processing.MainGainDb,
                    AudioProcessingSettings.StandardMaximumGainDb),
                CompressorEnabled = false,
                SoftClipperEnabled = false,
                LimiterEnabled = false,
            },
            _ => processing with
            {
                MainGainDb = AudioProcessingSettings.QuantizeRawExpertGain(
                    processing.MainGainDb),
                CompressorEnabled = false,
                SoftClipperEnabled = false,
                LimiterEnabled = false,
            },
        };

        suppressSettingsChanges = true;
        try
        {
            protectionMode = mode;
            mainGainDb = processing.MainGainDb;
            compressorEnabled = processing.CompressorEnabled;
            softClipperEnabled = processing.SoftClipperEnabled;
            limiterEnabled = processing.LimiterEnabled;
            RaiseModeProperties();
            RaiseGainProperties();
        }
        finally
        {
            suppressSettingsChanges = false;
        }

        UpdateSettings(settings with
        {
            Processing = processing,
        });
        audioEngine.UpdateProcessingSettings(processing);
    }

    private void ApplyProfile(string profileName)
    {
        AudioProfileSettings? profile = settings.Profiles.FirstOrDefault(
            candidate => string.Equals(
                candidate.Name,
                profileName,
                StringComparison.Ordinal));
        if (profile is null)
        {
            return;
        }

        AudioProcessingSettings profileProcessing = profile.Processing;

        suppressSettingsChanges = true;
        try
        {
            bassFrequencyHz = profileProcessing.BassFrequencyHz;
            bassGainDb = profileProcessing.BassGainDb;
            bassSlope = profileProcessing.BassSlope;
            bassBoostEnabled = profileProcessing.BassBoostEnabled;
            RaiseBassProperties();
        }
        finally
        {
            suppressSettingsChanges = false;
        }

        AudioProcessingSettings updated = settings.Processing with
        {
            BassProfile = profileProcessing.BassProfile,
            BassFrequencyHz = profileProcessing.BassFrequencyHz,
            BassGainDb = profileProcessing.BassGainDb,
            BassSlope = profileProcessing.BassSlope,
            BassBoostEnabled = profileProcessing.BassBoostEnabled,
        };
        UpdateSettings(settings with
        {
            Processing = updated,
        });
        audioEngine.UpdateProcessingSettings(updated);
    }

    private void SetCustomProfile()
    {
        if (suppressSettingsChanges
            || string.Equals(
                selectedProfile,
                "Personnalisé",
                StringComparison.Ordinal))
        {
            return;
        }

        selectedProfile = "Personnalisé";
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void SetEqualizerBand(
        ref double field,
        double value,
        string propertyName)
    {
        double safe = Math.Round(Math.Clamp(value, -12, 12), 1);
        if (!SetProperty(ref field, safe, propertyName))
        {
            return;
        }

        ApplyProcessingChange(processing => processing with
        {
            EqualizerLowDb = equalizerLowDb,
            EqualizerMidDb = equalizerMidDb,
            EqualizerHighDb = equalizerHighDb,
        });
    }

    private void SetProcessingParameter(
        double value,
        double minimum,
        double maximum,
        string propertyName,
        Func<AudioProcessingSettings, double, AudioProcessingSettings> update)
    {
        double safe = Math.Round(
            Math.Clamp(value, minimum, maximum),
            2,
            MidpointRounding.AwayFromZero);
        double current = propertyName switch
        {
            nameof(CompressorThresholdDb) =>
                settings.Processing.CompressorThresholdDb,
            nameof(CompressorRatio) => settings.Processing.CompressorRatio,
            nameof(CompressorAttackMs) =>
                settings.Processing.CompressorAttackMs,
            nameof(CompressorReleaseMs) =>
                settings.Processing.CompressorReleaseMs,
            nameof(CompressorMakeupGainDb) =>
                settings.Processing.CompressorMakeupGainDb,
            nameof(SoftClipperThresholdDb) =>
                settings.Processing.SoftClipperThresholdDb,
            nameof(SoftClipperDrive) =>
                settings.Processing.SoftClipperDrive,
            nameof(LimiterThresholdDb) =>
                settings.Processing.LimiterThresholdDb,
            nameof(LimiterReleaseMs) =>
                settings.Processing.LimiterReleaseMs,
            nameof(OutputGainDb) => settings.Processing.OutputGainDb,
            nameof(ParameterSmoothingMs) =>
                settings.Processing.ParameterSmoothingMs,
            _ => double.NaN,
        };
        if (Math.Abs(current - safe) < 0.001)
        {
            return;
        }

        ApplyProcessingChange(processing => update(processing, safe));
        OnPropertyChanged(propertyName);
    }

    private void SetHotkey(
        string? value,
        string propertyName,
        Func<HotkeySettings, string, HotkeySettings> update)
    {
        string gesture = value?.Trim() ?? string.Empty;
        string current = propertyName switch
        {
            nameof(IncreaseGainHotkey) => settings.Hotkeys.IncreaseGain,
            nameof(DecreaseGainHotkey) => settings.Hotkeys.DecreaseGain,
            nameof(ToggleProcessingHotkey) =>
                settings.Hotkeys.ToggleProcessing,
            nameof(PanicResetHotkey) => settings.Hotkeys.PanicReset,
            nameof(ToggleBassBoostHotkey) =>
                settings.Hotkeys.ToggleBassBoost,
            nameof(ToggleWindowHotkey) => settings.Hotkeys.ToggleWindow,
            _ => string.Empty,
        };
        if (string.Equals(current, gesture, StringComparison.Ordinal))
        {
            return;
        }

        AppSettings candidate = settings with
        {
            Hotkeys = update(settings.Hotkeys, gesture),
        };
        SettingsValidationResult validation = SettingsValidator.Validate(candidate);
        if (!validation.IsValid)
        {
            ShowNotification(
                UiNotificationSeverity.Error,
                "Raccourci invalide",
                "Utilisez un modificateur et une touche uniques, par exemple Ctrl+Alt+P.");
            OnPropertyChanged(propertyName);
            return;
        }

        UpdateSettings(candidate);
        OnPropertyChanged(propertyName);
    }

    private void ApplyProcessingChange(
        Func<AudioProcessingSettings, AudioProcessingSettings> update)
    {
        if (suppressSettingsChanges)
        {
            return;
        }

        AudioProcessingSettings processing = update(settings.Processing);
        UpdateSettings(settings with
        {
            Processing = processing,
        });
        audioEngine.UpdateProcessingSettings(processing);
    }

    private void UpdateSettings(AppSettings updated)
    {
        settings = updated;
        applicationState.SetSettings(updated);
        OnPropertyChanged(nameof(CurrentSettings));
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (suppressSettingsChanges || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var replacement = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(
            ref saveDebounce,
            replacement);
        previous?.Cancel();
        previous?.Dispose();
        _ = SaveAfterDelayAsync(replacement.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(500, cancellationToken);
            await SaveNowAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            logger.LogError(exception, "La sauvegarde des réglages a échoué.");
            errorSink.Report("Configuration", "La sauvegarde des réglages a échoué.");
            RunOnUi(() =>
            {
                ShowNotification(
                    UiNotificationSeverity.Error,
                    "Configuration",
                    "Impossible de sauvegarder les réglages locaux.");
                OnPropertyChanged(nameof(DiagnosticErrorsText));
            });
        }
    }

    private async Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        await saveSemaphore.WaitAsync(cancellationToken);
        try
        {
            await settingsService.SaveAsync(settings, cancellationToken);
        }
        finally
        {
            saveSemaphore.Release();
        }
    }

    private void OnEngineStatusChanged(
        object? sender,
        AudioEngineStatus engineStatus)
    {
        RunOnUi(() =>
        {
            EngineStatusText = engineStatus.Message;
            EngineState = engineStatus.State;
            applicationState.SetEngineStatus(engineStatus);
            if (engineStatus.State != AudioEngineState.Running)
            {
                ResetLiveMetrics();
            }

            if (engineStatus.State is AudioEngineState.DeviceLost or AudioEngineState.Faulted)
            {
                ShowNotification(
                    engineStatus.State == AudioEngineState.Faulted
                        ? UiNotificationSeverity.Error
                        : UiNotificationSeverity.Warning,
                    "Moteur audio",
                    engineStatus.Message);
            }

            OnPropertyChanged(nameof(DiagnosticEngineText));
            OnPropertyChanged(nameof(DiagnosticFormatsText));
            OnPropertyChanged(nameof(InputSignalStatusText));
        });
    }

    private void OnMetricsTimerTick(object? sender, EventArgs eventArgs)
    {
        if (!IsEngineRunning)
        {
            return;
        }

        AudioEngineMetrics metrics = audioEngine.Status.Metrics;
        lastMetrics = metrics;
        InputLevel = ToMeterPercent(metrics.InputPeak);
        OutputLevel = ToMeterPercent(metrics.OutputPeak);
        InputPeakText = FormatDbfs(metrics.InputPeak);
        OutputPeakText = FormatDbfs(metrics.OutputPeak);
        LimiterReductionText = metrics.LimiterReductionDb.ToString(
            "0.00 dB",
            CultureInfo.CurrentCulture);
        ClippingDetected = metrics.ClippingDetected;
        LimiterWorking = metrics.LimiterReductionDb > 0.01f;
        InputSignalPresent = metrics.InputSignalPresent;
        OnPropertyChanged(nameof(DiagnosticPipelineText));
        OnPropertyChanged(nameof(DiagnosticLevelsText));
        OnPropertyChanged(nameof(DiagnosticMetricsText));
        OnPropertyChanged(nameof(DiagnosticErrorsText));
    }

    private void ResetLiveMetrics()
    {
        lastMetrics = AudioEngineMetrics.Empty;
        InputLevel = 0;
        OutputLevel = 0;
        InputPeakText = "−∞ dBFS";
        OutputPeakText = "−∞ dBFS";
        LimiterReductionText = "0,00 dB";
        ClippingDetected = false;
        LimiterWorking = false;
        InputSignalPresent = false;
        OnPropertyChanged(nameof(DiagnosticPipelineText));
        OnPropertyChanged(nameof(DiagnosticLevelsText));
        OnPropertyChanged(nameof(DiagnosticMetricsText));
    }

    private void HandleCommandException(Exception exception)
    {
        logger.LogError(exception, "Une commande de l’interface a échoué.");
        errorSink.Report(
            "Interface",
            $"Commande échouée : {exception.GetType().Name}.");
        ShowNotification(
            UiNotificationSeverity.Error,
            "Action impossible",
            exception.Message);
        OnPropertyChanged(nameof(DiagnosticErrorsText));
    }

    private void LoadFieldsFromSettings()
    {
        AudioProcessingSettings processing = settings.Processing;
        RefreshProfiles();
        protectionMode = processing.Mode;
        mainGainDb = processing.MainGainDb;
        bassGainDb = processing.BassGainDb;
        bassFrequencyHz = processing.BassFrequencyHz;
        bassSlope = processing.BassSlope;
        dcBlockEnabled = processing.DcBlockEnabled;
        bassBoostEnabled = processing.BassBoostEnabled;
        equalizerEnabled = processing.EqualizerEnabled;
        equalizerLowDb = processing.EqualizerLowDb;
        equalizerMidDb = processing.EqualizerMidDb;
        equalizerHighDb = processing.EqualizerHighDb;
        compressorEnabled = processing.CompressorEnabled;
        softClipperEnabled = processing.SoftClipperEnabled;
        limiterEnabled = processing.LimiterEnabled;
        headphonesSafetyMode = settings.HeadphonesSafetyMode;
        startWithWindows = settings.StartWithWindows;
        startMinimized = settings.StartMinimized;
        startProcessingAutomatically = settings.StartProcessingAutomatically;
        notificationsEnabled = settings.NotificationsEnabled;
        globalHotkeysEnabled = settings.Hotkeys.GlobalHotkeysEnabled;
        detailedDiagnostics = settings.DetailedDiagnostics;
        selectedProfile = settings.Profiles
            .FirstOrDefault(profile => HasSameBassSettings(
                profile.Processing,
                processing))
            ?.Name
            ?? "Personnalisé";
        RaiseAllSettingsProperties();
    }

    private void RaiseAllSettingsProperties()
    {
        RaiseModeProperties();
        RaiseGainProperties();
        RaiseBassProperties();
        OnPropertyChanged(nameof(DcBlockEnabled));
        OnPropertyChanged(nameof(BassBoostEnabled));
        OnPropertyChanged(nameof(EqualizerEnabled));
        OnPropertyChanged(nameof(EqualizerLowDb));
        OnPropertyChanged(nameof(EqualizerMidDb));
        OnPropertyChanged(nameof(EqualizerHighDb));
        OnPropertyChanged(nameof(CompressorEnabled));
        OnPropertyChanged(nameof(CompressorThresholdDb));
        OnPropertyChanged(nameof(CompressorRatio));
        OnPropertyChanged(nameof(CompressorAttackMs));
        OnPropertyChanged(nameof(CompressorReleaseMs));
        OnPropertyChanged(nameof(CompressorMakeupGainDb));
        OnPropertyChanged(nameof(SoftClipperEnabled));
        OnPropertyChanged(nameof(SoftClipperThresholdDb));
        OnPropertyChanged(nameof(SoftClipperDrive));
        OnPropertyChanged(nameof(LimiterEnabled));
        OnPropertyChanged(nameof(LimiterThresholdDb));
        OnPropertyChanged(nameof(LimiterReleaseMs));
        OnPropertyChanged(nameof(OutputGainDb));
        OnPropertyChanged(nameof(ParameterSmoothingMs));
        OnPropertyChanged(nameof(HeadphonesSafetyMode));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(StartMinimized));
        OnPropertyChanged(nameof(StartProcessingAutomatically));
        OnPropertyChanged(nameof(WaitForDevicesOnStartup));
        OnPropertyChanged(nameof(WaitForSpotifyOnStartup));
        OnPropertyChanged(nameof(CloseBehavior));
        OnPropertyChanged(nameof(NotificationsEnabled));
        OnPropertyChanged(nameof(GlobalHotkeysEnabled));
        OnPropertyChanged(nameof(IncreaseGainHotkey));
        OnPropertyChanged(nameof(DecreaseGainHotkey));
        OnPropertyChanged(nameof(ToggleProcessingHotkey));
        OnPropertyChanged(nameof(PanicResetHotkey));
        OnPropertyChanged(nameof(ToggleBassBoostHotkey));
        OnPropertyChanged(nameof(ToggleWindowHotkey));
        OnPropertyChanged(nameof(ShowLinearPercent));
        OnPropertyChanged(nameof(DetailedDiagnostics));
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(LimiterStatusText));
        OnPropertyChanged(nameof(DiagnosticPipelineText));
    }

    private void RaiseModeProperties()
    {
        OnPropertyChanged(nameof(ProtectionMode));
        OnPropertyChanged(nameof(ProtectionModeText));
        OnPropertyChanged(nameof(ProtectionModeDescription));
        OnPropertyChanged(nameof(GainMaximumDb));
        OnPropertyChanged(nameof(GainStepDb));
        OnPropertyChanged(nameof(OutputGainDb));
        OnPropertyChanged(nameof(SafetyWarningText));
        OnPropertyChanged(nameof(HasSafetyWarning));
        OnPropertyChanged(nameof(CompressorEnabled));
        OnPropertyChanged(nameof(SoftClipperEnabled));
        OnPropertyChanged(nameof(LimiterEnabled));
        OnPropertyChanged(nameof(LimiterStatusText));
    }

    private void RaiseGainProperties()
    {
        OnPropertyChanged(nameof(MainGainDb));
        OnPropertyChanged(nameof(GainDbText));
        OnPropertyChanged(nameof(GainPercentText));
        OnPropertyChanged(nameof(LinearGainFactor));
        OnPropertyChanged(nameof(SafetyWarningText));
        OnPropertyChanged(nameof(HasSafetyWarning));
    }

    private void RaiseBassProperties()
    {
        OnPropertyChanged(nameof(BassBoostEnabled));
        OnPropertyChanged(nameof(BassGainDb));
        OnPropertyChanged(nameof(BassGainText));
        OnPropertyChanged(nameof(BassFrequencyHz));
        OnPropertyChanged(nameof(BassFrequencyText));
        OnPropertyChanged(nameof(BassSlope));
        OnPropertyChanged(nameof(BassSlopeText));
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void RaiseRouteProperties()
    {
        OnPropertyChanged(nameof(RouteStatusText));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanChangeDevices));
        OnPropertyChanged(nameof(HasVirtualSource));
        OnPropertyChanged(nameof(SelectedSourceStatusText));
        OnPropertyChanged(nameof(SelectedOutputStatusText));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        RefreshDevicesCommand.NotifyCanExecuteChanged();
        ToggleEngineCommand.NotifyCanExecuteChanged();
    }

    private void ShowNotification(
        UiNotificationSeverity severity,
        string title,
        string message)
    {
        NotificationSeverity = severity;
        NotificationTitle = title;
        NotificationMessage = message;
    }

    private void ClearNotification()
    {
        NotificationTitle = string.Empty;
        NotificationMessage = string.Empty;
    }

    private static void RunOnUi(Action action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.BeginInvoke(action, DispatcherPriority.Background);
    }

    private static double ToMeterPercent(float peak)
    {
        if (!float.IsFinite(peak) || peak <= 0)
        {
            return 0;
        }

        double decibels = 20 * Math.Log10(peak);
        return Math.Clamp((decibels + 60) / 60 * 100, 0, 100);
    }

    private static string FormatDbfs(float peak)
    {
        if (peak <= 0 || !float.IsFinite(peak))
        {
            return "−∞ dBFS";
        }

        double db = 20 * Math.Log10(peak);
        return string.Create(CultureInfo.CurrentCulture, $"{db:0.0} dBFS");
    }

    private static string FormatLinearFactor(double factor)
    {
        return factor >= 1_000_000_000
            ? factor.ToString("0.###E+0", CultureInfo.CurrentCulture)
            : factor.ToString("0.000", CultureInfo.CurrentCulture);
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (string name in settings.Profiles
                     .Select(profile => profile.Name)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Profiles.Add(name);
        }

        if (!Profiles.Contains("Personnalisé", StringComparer.OrdinalIgnoreCase))
        {
            Profiles.Add("Personnalisé");
        }
    }

    private static bool HasSameBassSettings(
        AudioProcessingSettings left,
        AudioProcessingSettings right)
    {
        return left.BassBoostEnabled == right.BassBoostEnabled
            && left.BassProfile == right.BassProfile
            && Math.Abs(left.BassFrequencyHz - right.BassFrequencyHz) < 0.01
            && Math.Abs(left.BassGainDb - right.BassGainDb) < 0.01
            && Math.Abs(left.BassSlope - right.BassSlope) < 0.001;
    }

    private static bool IsVbCableDevice(AudioDeviceInfo device)
    {
        return device.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
            || device.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCableStatus(
        AudioDeviceInfo? device,
        string expectedName)
    {
        if (device is null)
        {
            return $"{expectedName} : absent";
        }

        return $"{expectedName} : {device.StateDescription} · {device.FormatDescription}";
    }

    private static string StateText(bool enabled) => enabled ? "actif" : "bypass";
}
