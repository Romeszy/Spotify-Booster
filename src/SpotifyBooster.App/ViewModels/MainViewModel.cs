using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
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

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAudioDeviceService deviceService;
    private readonly IAudioEngine audioEngine;
    private readonly ISettingsService settingsService;
    private readonly ISpotifyProcessDetector spotifyDetector;
    private readonly IDiagnosticReportService diagnosticReportService;
    private readonly IDiagnosticFileExporter diagnosticFileExporter;
    private readonly IWindowsSettingsLauncher windowsSettingsLauncher;
    private readonly IErrorSink errorSink;
    private readonly ApplicationState applicationState;
    private readonly ILogger<MainViewModel> logger;
    private readonly DispatcherTimer spotifyTimer;
    private readonly DispatcherTimer metricsTimer;
    private readonly SemaphoreSlim saveSemaphore = new(1, 1);
    private CancellationTokenSource? saveDebounce;
    private AppSettings settings = new();
    private AudioDeviceInfo? selectedSource;
    private AudioDeviceInfo? selectedOutput;
    private NavigationPage currentPage = NavigationPage.Dashboard;
    private double gainPercent = 100;
    private double bassGainDb = 3;
    private double bassFrequencyHz = 90;
    private string selectedProfile = "Balanced";
    private bool compressorEnabled = true;
    private bool limiterEnabled = true;
    private bool headphonesSafetyMode = true;
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
    private string limiterReductionText = "0.00 dB";
    private string engineStatusText = "Moteur arrêté";
    private string lastError = string.Empty;
    private string lastExportMessage = string.Empty;
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
        this.errorSink = errorSink;
        this.applicationState = applicationState;
        this.logger = logger;

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
        CompleteSetupCommand = new AsyncRelayCommand(
            CompleteSetupAsync,
            HandleCommandException);
        ExportDiagnosticCommand = new AsyncRelayCommand(
            ExportDiagnosticAsync,
            HandleCommandException);
        DismissErrorCommand = new RelayCommand(_ => LastError = string.Empty);

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
        audioEngine.GainAutomaticallyReduced += OnGainAutomaticallyReduced;
    }

    public ObservableCollection<AudioDeviceInfo> CaptureDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> RenderDevices { get; } = [];

    public ObservableCollection<SpotifyProcessInfo> SpotifyProcesses { get; } = [];

    public IReadOnlyList<string> Profiles { get; } =
    [
        "Balanced",
        "Punch",
        "Deep Bass",
        "Custom",
    ];

    public RelayCommand NavigateCommand { get; }

    public AsyncRelayCommand RefreshDevicesCommand { get; }

    public AsyncRelayCommand ToggleEngineCommand { get; }

    public RelayCommand PanicCommand { get; }

    public RelayCommand OpenVolumeMixerCommand { get; }

    public AsyncRelayCommand CompleteSetupCommand { get; }

    public AsyncRelayCommand ExportDiagnosticCommand { get; }

    public RelayCommand DismissErrorCommand { get; }

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
        NavigationPage.Setup => "Assistant de configuration",
        NavigationPage.Settings => "Réglages audio",
        NavigationPage.Diagnostics => "Diagnostic",
        _ => "Contrôle audio",
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

            OnPropertyChanged(nameof(RouteStatusText));
            OnPropertyChanged(nameof(CanActivate));
            OnPropertyChanged(nameof(CanChangeDevices));
            OnPropertyChanged(nameof(HasVirtualSource));
            NotifyCommandStates();
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

            OnPropertyChanged(nameof(RouteStatusText));
            OnPropertyChanged(nameof(CanActivate));
            OnPropertyChanged(nameof(CanChangeDevices));
            NotifyCommandStates();
            if (!suppressSettingsChanges)
            {
                UpdateSettings(settings with
                {
                    OutputDeviceId = value?.Id,
                });
            }
        }
    }

    public double GainPercent
    {
        get => gainPercent;
        set
        {
            int rounded = (int)Math.Round(
                Math.Clamp(
                    value,
                    AudioProcessingSettings.MinimumGainPercent,
                    AudioProcessingSettings.MaximumGainPercent),
                MidpointRounding.AwayFromZero);
            if (!SetProperty(ref gainPercent, rounded))
            {
                return;
            }

            if (rounded > 100 && !limiterEnabled)
            {
                limiterEnabled = true;
                OnPropertyChanged(nameof(LimiterEnabled));
            }

            OnPropertyChanged(nameof(GainPercentText));
            OnPropertyChanged(nameof(GainDbText));
            OnPropertyChanged(nameof(SafetyWarningText));
            OnPropertyChanged(nameof(HasSafetyWarning));
            OnPropertyChanged(nameof(LimiterStatusText));
            ApplyProcessingChange(processing => processing with
            {
                GainPercent = rounded,
                LimiterEnabled = rounded > 100 || limiterEnabled,
            });
        }
    }

    public string GainPercentText => $"{GainPercent:0} %";

    public string GainDbText
    {
        get
        {
            float decibels = GainConversion.PercentToDecibels((float)GainPercent);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{decibels:+0.00;-0.00;0.00} dB");
        }
    }

    public double BassGainDb
    {
        get => bassGainDb;
        set
        {
            double safe = Math.Round(Math.Clamp(value, 0, 12), 1);
            if (!SetProperty(ref bassGainDb, safe))
            {
                return;
            }

            OnPropertyChanged(nameof(BassGainText));
            SetCustomProfile();
            ApplyProcessingChange(processing => processing with
            {
                BassGainDb = safe,
                BassBoostEnabled = safe > 0,
                BassProfile = BassProfile.Custom,
            });
        }
    }

    public string BassGainText => $"+{BassGainDb:0.0} dB";

    public double BassFrequencyHz
    {
        get => bassFrequencyHz;
        set
        {
            double safe = Math.Round(Math.Clamp(value, 40, 200));
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

    public string SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!SetProperty(ref selectedProfile, value)
                || suppressSettingsChanges
                || string.Equals(value, "Custom", StringComparison.Ordinal))
            {
                return;
            }

            ApplyProfile(value);
        }
    }

    public bool CompressorEnabled
    {
        get => compressorEnabled;
        set
        {
            if (!SetProperty(ref compressorEnabled, value))
            {
                return;
            }

            ApplyProcessingChange(processing => processing with
            {
                CompressorEnabled = value,
            });
        }
    }

    public bool LimiterEnabled
    {
        get => limiterEnabled;
        set
        {
            if (!value && GainPercent > 100)
            {
                LastError =
                    "Le limiteur reste obligatoire au-dessus de 100 % pour éviter une sortie numérique incontrôlée.";
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
        : "Spotify fermé ou non détecté";

    public AudioEngineState EngineState
    {
        get => engineState;
        private set
        {
            if (SetProperty(ref engineState, value))
            {
                OnPropertyChanged(nameof(IsEngineRunning));
                OnPropertyChanged(nameof(EngineButtonText));
                OnPropertyChanged(nameof(RouteStatusText));
                OnPropertyChanged(nameof(CanActivate));
                OnPropertyChanged(nameof(CanChangeDevices));
                OnPropertyChanged(nameof(InputSignalStatusText));
                NotifyCommandStates();
            }
        }
    }

    public bool IsEngineRunning => EngineState == AudioEngineState.Running;

    public string EngineButtonText => EngineState switch
    {
        AudioEngineState.Running => "Désactiver le traitement",
        AudioEngineState.Starting => "Activation…",
        AudioEngineState.Stopping => "Arrêt…",
        AudioEngineState.DeviceLost => "Récupération du périphérique…",
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
                OnPropertyChanged(nameof(CanActivate));
                OnPropertyChanged(nameof(CanChangeDevices));
                NotifyCommandStates();
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

            return SelectedSource is not null
                && SelectedOutput is not null
                && AudioRouteSafety.Validate(
                    SelectedSource,
                    SelectedOutput).IsValid;
        }
    }

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
                return "Source sélectionnée → DSP → sortie sélectionnée";
            }

            if (SelectedSource is null)
            {
                return HasVirtualSource
                    ? "Sélectionnez la source virtuelle"
                    : "Aucune source virtuelle reconnue";
            }

            if (!SelectedSource.IsLikelyVirtual)
            {
                return "Source non reconnue comme virtuelle — vérifiez le routage";
            }

            if (SelectedOutput is null)
            {
                return "Sélectionnez le casque ou les enceintes";
            }

            if (SelectedOutput.IsLikelyVirtual)
            {
                return "Sortie virtuelle détectée — risque de boucle";
            }

            return "Chemin prêt à être activé";
        }
    }

    public bool HasVirtualSource =>
        CaptureDevices.Any(device => device.IsLikelyVirtual);

    public string SafetyWarningText
    {
        get
        {
            if (GainPercent >= 300 && HeadphonesSafetyMode)
            {
                return "DANGER CASQUE · Commencez bas. 300–500 % peut être agressif et n’est jamais garanti sans danger.";
            }

            if (GainPercent >= 300)
            {
                return "Gain très élevé · risque de fatigue auditive, distorsion et limitation continue.";
            }

            if (GainPercent > 100)
            {
                return "Augmentez progressivement. Le limiteur protège le signal numérique, pas votre audition.";
            }

            return string.Empty;
        }
    }

    public bool HasSafetyWarning => GainPercent > 100;

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
        ? "Saturation interceptée"
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
            if (!LimiterEnabled && GainPercent <= 100)
            {
                return "Limiteur désactivé — prudence";
            }

            return LimiterWorking
                ? $"Limiteur actif · −{LimiterReductionText}"
                : "Limiteur armé · plafond −1 dBFS";
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

    public string LastError
    {
        get => lastError;
        private set
        {
            if (SetProperty(ref lastError, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);

    public string LastExportMessage
    {
        get => lastExportMessage;
        private set => SetProperty(ref lastExportMessage, value);
    }

    public string DiagnosticEngineText =>
        $"{audioEngine.Status.State} · {audioEngine.Status.Message}";

    public string DiagnosticFormatsText =>
        $"Source : {audioEngine.Status.SourceFormat ?? "indisponible"}\n"
        + $"Sortie : {audioEngine.Status.OutputFormat ?? "indisponible"}";

    public string DiagnosticMetricsText
    {
        get
        {
            AudioEngineMetrics metrics = lastMetrics;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Fréquence : {metrics.SampleRate} Hz · {metrics.Channels} canaux\n"
                + $"Buffer capture : {metrics.BufferMilliseconds} ms · latence estimée : {metrics.EstimatedLatencyMilliseconds:F1} ms\n"
                + $"Charge DSP : {metrics.ProcessingLoadPercent:F2} % · buffers : {metrics.BufferCount}\n"
                + $"Underruns : {metrics.BufferUnderrunCount} · overruns : {metrics.BufferOverrunCount} · clips de sortie : {metrics.ClipCount}");
        }
    }

    public string DiagnosticSpotifyText
    {
        get
        {
            if (SpotifyProcesses.Count == 0)
            {
                return "Aucun processus Spotify détecté.";
            }

            return string.Join(
                Environment.NewLine,
                SpotifyProcesses.Select(process =>
                    $"{process.ProcessName} · PID {process.ProcessId} · {process.Distribution}"));
        }
    }

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
            AudioProcessingSettings processing = settings.Processing;
            gainPercent = processing.GainPercent;
            bassGainDb = processing.BassGainDb;
            bassFrequencyHz = processing.BassFrequencyHz;
            compressorEnabled = processing.CompressorEnabled;
            limiterEnabled = processing.LimiterEnabled;
            headphonesSafetyMode = settings.HeadphonesSafetyMode;
            selectedProfile = ProfileName(processing.BassProfile);
            RaiseProcessingProperties();

            await RefreshDevicesCoreAsync();
            await RefreshSpotifyAsync();
            applicationState.SetSettings(settings);
            if (!settings.FirstRunCompleted)
            {
                CurrentPage = NavigationPage.Setup;
            }

            spotifyTimer.Start();
            metricsTimer.Start();
        }
        finally
        {
            suppressSettingsChanges = false;
            IsBusy = false;
        }
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
        audioEngine.GainAutomaticallyReduced -= OnGainAutomaticallyReduced;
        CancellationTokenSource? debounce = Interlocked.Exchange(ref saveDebounce, null);
        debounce?.Cancel();
        debounce?.Dispose();

        if (Volatile.Read(ref initialized) != 0)
        {
            await SaveNowAsync();
        }

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
        (IReadOnlyList<AudioDeviceInfo> captures, IReadOnlyList<AudioDeviceInfo> renders) =
            await Task.Run(() =>
                (deviceService.GetCaptureDevices(), deviceService.GetRenderDevices()));

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
        OnPropertyChanged(nameof(HasVirtualSource));
        OnPropertyChanged(nameof(RouteStatusText));
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(CanChangeDevices));
    }

    private async Task ToggleEngineAsync()
    {
        IsBusy = true;
        LastError = string.Empty;
        try
        {
            if (IsEngineRunning)
            {
                await audioEngine.StopAsync();
                return;
            }

            if (SelectedSource is null || SelectedOutput is null)
            {
                LastError =
                    "Sélectionnez une source virtuelle et une sortie physique avant d’activer.";
                return;
            }

            AudioRouteValidationResult validation = AudioRouteSafety.Validate(
                SelectedSource,
                SelectedOutput);
            if (!validation.IsValid)
            {
                LastError = string.Join(" ", validation.Errors);
                return;
            }

            if (validation.Warnings.Count > 0)
            {
                LastError = string.Join(" ", validation.Warnings);
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

    private void PanicReset()
    {
        GainPercent = 100;
        LimiterEnabled = true;
        audioEngine.PanicReset();
        LastError = "Retour d’urgence appliqué : gain 100 %, limiteur actif.";
    }

    private void OpenVolumeMixer()
    {
        if (!windowsSettingsLauncher.OpenVolumeMixer())
        {
            LastError =
                "Le mélangeur n’a pas pu être ouvert. Ouvrez Paramètres > Système > Son > Mélangeur de volume.";
        }
    }

    private async Task CompleteSetupAsync()
    {
        settings = settings with
        {
            FirstRunCompleted = true,
            Processing = settings.Processing with
            {
                GainPercent = (int)GainPercent,
                LimiterEnabled = true,
            },
        };
        await SaveNowAsync();
        applicationState.SetSettings(settings);
        CurrentPage = NavigationPage.Dashboard;
    }

    private async Task ExportDiagnosticAsync()
    {
        var snapshot = new DiagnosticSnapshot(
            DateTimeOffset.UtcNow,
            SpotifyProcesses.ToArray(),
            SelectedSource,
            SelectedOutput,
            audioEngine.Status,
            errorSink.GetRecent());
        string report = diagnosticReportService.CreateTextReport(snapshot);
        string? path = await diagnosticFileExporter.ExportAsync(report);
        LastExportMessage = path is null
            ? "Export annulé."
            : "Rapport exporté avec succès.";
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

    private void ApplyProfile(string profileName)
    {
        AudioProcessingSettings updatedProcessing;
        suppressSettingsChanges = true;
        try
        {
            (double frequency, double gain, BassProfile profile) = profileName switch
            {
                "Punch" => (105, 5, BassProfile.Punch),
                "Deep Bass" => (60, 6, BassProfile.DeepBass),
                _ => (90, 3, BassProfile.Balanced),
            };
            bassFrequencyHz = frequency;
            bassGainDb = gain;
            OnPropertyChanged(nameof(BassFrequencyHz));
            OnPropertyChanged(nameof(BassFrequencyText));
            OnPropertyChanged(nameof(BassGainDb));
            OnPropertyChanged(nameof(BassGainText));
            updatedProcessing = settings.Processing with
            {
                BassProfile = profile,
                BassFrequencyHz = frequency,
                BassGainDb = gain,
                BassBoostEnabled = true,
            };
        }
        finally
        {
            suppressSettingsChanges = false;
        }

        UpdateSettings(settings with
        {
            Processing = updatedProcessing,
        });
        audioEngine.UpdateProcessingSettings(updatedProcessing);
    }

    private void SetCustomProfile()
    {
        if (suppressSettingsChanges
            || string.Equals(selectedProfile, "Custom", StringComparison.Ordinal))
        {
            return;
        }

        selectedProfile = "Custom";
        OnPropertyChanged(nameof(SelectedProfile));
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
                LastError = "Impossible de sauvegarder les réglages locaux.";
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
        InputLevel = Math.Clamp(metrics.InputPeak * 100, 0, 100);
        OutputLevel = Math.Clamp(metrics.OutputPeak * 100, 0, 100);
        InputPeakText = FormatDbfs(metrics.InputPeak);
        OutputPeakText = FormatDbfs(metrics.OutputPeak);
        LimiterReductionText = metrics.LimiterReductionDb.ToString(
            "0.00 dB",
            CultureInfo.InvariantCulture);
        ClippingDetected = metrics.ClippingDetected;
        LimiterWorking = metrics.LimiterReductionDb > 0.01f;
        InputSignalPresent = metrics.InputSignalPresent;
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
        LimiterReductionText = "0.00 dB";
        ClippingDetected = false;
        LimiterWorking = false;
        InputSignalPresent = false;
        OnPropertyChanged(nameof(DiagnosticMetricsText));
    }

    private void OnGainAutomaticallyReduced(
        object? sender,
        AutomaticGainReductionEventArgs eventArgs)
    {
        RunOnUi(() =>
        {
            GainPercent = eventArgs.GainPercent;
            LastError =
                $"Gain réduit automatiquement à {eventArgs.GainPercent} % après une limitation répétée.";
        });
    }

    private void HandleCommandException(Exception exception)
    {
        logger.LogError(exception, "Une commande de l’interface a échoué.");
        errorSink.Report(
            "Interface",
            $"Commande échouée : {exception.GetType().Name}.");
        LastError = exception.Message;
        OnPropertyChanged(nameof(DiagnosticErrorsText));
    }

    private void NotifyCommandStates()
    {
        RefreshDevicesCommand.NotifyCanExecuteChanged();
        ToggleEngineCommand.NotifyCanExecuteChanged();
    }

    private void RaiseProcessingProperties()
    {
        OnPropertyChanged(nameof(GainPercent));
        OnPropertyChanged(nameof(GainPercentText));
        OnPropertyChanged(nameof(GainDbText));
        OnPropertyChanged(nameof(BassGainDb));
        OnPropertyChanged(nameof(BassGainText));
        OnPropertyChanged(nameof(BassFrequencyHz));
        OnPropertyChanged(nameof(BassFrequencyText));
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(CompressorEnabled));
        OnPropertyChanged(nameof(LimiterEnabled));
        OnPropertyChanged(nameof(HeadphonesSafetyMode));
        OnPropertyChanged(nameof(SafetyWarningText));
        OnPropertyChanged(nameof(HasSafetyWarning));
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

    private static string FormatDbfs(float peak)
    {
        if (peak <= 0 || !float.IsFinite(peak))
        {
            return "−∞ dBFS";
        }

        double db = 20 * Math.Log10(peak);
        return string.Create(CultureInfo.InvariantCulture, $"{db:0.0} dBFS");
    }

    private static string ProfileName(BassProfile profile)
    {
        return profile switch
        {
            BassProfile.Punch => "Punch",
            BassProfile.DeepBass => "Deep Bass",
            BassProfile.Custom => "Custom",
            _ => "Balanced",
        };
    }
}
