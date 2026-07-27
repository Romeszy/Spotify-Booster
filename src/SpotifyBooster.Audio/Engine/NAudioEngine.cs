using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SpotifyBooster.Audio.Devices;
using SpotifyBooster.Audio.Pipeline;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Audio.Engine;

public sealed class NAudioEngine : IAudioEngine
{
    private const int CaptureLatencyMilliseconds = 50;
    private const int RenderLatencyMilliseconds = 50;
    private static readonly TimeSpan BufferCapacity = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TargetBuffer = TimeSpan.FromMilliseconds(100);
    private readonly ILogger<NAudioEngine> logger;
    private readonly IErrorSink errorSink;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly RecoveryTaskGate recovery = new();
    private readonly Timer metricsTimer;
    private AudioEngineStatus status = AudioEngineStatus.Stopped;
    private AudioProcessingSettings currentSettings = new();
    private AudioRoute? currentRoute;
    private AudioGraph? graph;
    private long operationGeneration;
    private int consumingMetrics;
    private int stopping;
    private int disposed;

    public NAudioEngine(
        ILogger<NAudioEngine> logger,
        IErrorSink errorSink)
    {
        this.logger = logger;
        this.errorSink = errorSink;
        metricsTimer = new Timer(
            static state => ((NAudioEngine)state!).ConsumeDspMetrics(),
            this,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100));
    }

    public event EventHandler<AudioEngineStatus>? StatusChanged;

    public event EventHandler<AudioEngineMetrics>? MetricsUpdated;

    public AudioEngineStatus Status => Volatile.Read(ref status);

    public async Task StartAsync(
        AudioRoute route,
        AudioProcessingSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(settings);
        long generation = Interlocked.Increment(ref operationGeneration);

        await lifecycle.WaitAsync(cancellationToken);
        try
        {
            await StopGraphCoreAsync(publishStopped: false);
            await StartGraphCoreAsync(route, settings, generation, cancellationToken);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        Interlocked.Increment(ref operationGeneration);
        recovery.SetStopping(true);
        try
        {
            await lifecycle.WaitAsync(cancellationToken);
            try
            {
                await StopGraphCoreAsync(publishStopped: true);
            }
            finally
            {
                lifecycle.Release();
            }
        }
        finally
        {
            recovery.SetStopping(false);
        }
    }

    public void UpdateProcessingSettings(AudioProcessingSettings settings)
    {
        ThrowIfDisposed();
        AudioProcessingSettings validated = ValidateProcessingSettings(settings);
        Volatile.Write(ref currentSettings, validated);
        Volatile.Read(ref graph)?.DspProvider.UpdateSettings(validated);
    }

    public void PanicReset()
    {
        ThrowIfDisposed();
        AudioProcessingSettings safe = Volatile.Read(ref currentSettings) with
        {
            MainGainDb = AudioProcessingSettings.MinimumGainDb,
            OutputGainDb = 0,
        };
        Volatile.Write(ref currentSettings, safe);
        Volatile.Read(ref graph)?.DspProvider.RequestPanicReset();
        logger.LogWarning("Retour d’urgence à 0 dB demandé.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Increment(ref operationGeneration);
        recovery.SetStopping(true);
        await metricsTimer.DisposeAsync();
        await recovery.DisposeAsync();
        await lifecycle.WaitAsync();
        try
        {
            try
            {
                await StopGraphCoreAsync(publishStopped: true);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Le moteur a rencontré une erreur pendant sa destruction, après avoir tenté de libérer toutes les ressources.");
            }
        }
        finally
        {
            lifecycle.Release();
            lifecycle.Dispose();
        }
    }

    private async Task StartGraphCoreAsync(
        AudioRoute route,
        AudioProcessingSettings settings,
        long generation,
        CancellationToken cancellationToken)
    {
        if (generation != Volatile.Read(ref operationGeneration))
        {
            return;
        }

        AudioProcessingSettings validatedSettings = ValidateProcessingSettings(settings);
        PublishStatus(
            AudioEngineState.Starting,
            "Initialisation du chemin audio…",
            null,
            null,
            AudioEngineMetrics.Empty);

        AudioGraph? createdGraph = null;
        try
        {
            createdGraph = await Task.Run(
                () => CreateGraph(route, validatedSettings),
                cancellationToken);
            if (generation != Volatile.Read(ref operationGeneration))
            {
                await DisposeGraphAsync(createdGraph);
                createdGraph = null;
                return;
            }

            createdGraph.Capture.DataAvailable += OnCaptureDataAvailable;
            createdGraph.Capture.RecordingStopped += OnRecordingStopped;
            createdGraph.Output.PlaybackStopped += OnPlaybackStopped;
            graph = createdGraph;
            Volatile.Write(ref currentSettings, validatedSettings);
            currentRoute = route;
            Volatile.Write(ref stopping, 0);

            await Task.Run(createdGraph.Capture.StartRecording, cancellationToken);
            await WaitForPrebufferAsync(createdGraph, cancellationToken);
            await Task.Run(createdGraph.Output.Play, cancellationToken);

            logger.LogInformation(
                "Moteur audio démarré : {SourceFormat} vers {OutputFormat}.",
                createdGraph.SourceFormat,
                createdGraph.OutputFormat);
            PublishStatus(
                AudioEngineState.Running,
                "Traitement actif",
                createdGraph.SourceFormat,
                createdGraph.OutputFormat,
                AudioEngineMetrics.Empty);
        }
        catch (OperationCanceledException cancellationException)
        {
            graph = null;
            currentRoute = null;
            Exception? cleanupFailure = createdGraph is null
                ? null
                : await CaptureCleanupFailureAsync(createdGraph);
            if (cleanupFailure is not null)
            {
                const string cleanupMessage =
                    "Le démarrage a été annulé, mais le nettoyage audio a rencontré une erreur.";
                AggregateException combinedFailure = CombineFailures(
                    cancellationException,
                    cleanupFailure);
                logger.LogError(combinedFailure, "{Message}", cleanupMessage);
                errorSink.Report("Audio", cleanupMessage);
                PublishStatus(
                    AudioEngineState.Faulted,
                    cleanupMessage,
                    null,
                    null,
                    AudioEngineMetrics.Empty);
                throw new InvalidOperationException(
                    cleanupMessage,
                    combinedFailure);
            }

            PublishStatus(
                AudioEngineState.Stopped,
                "Démarrage annulé",
                null,
                null,
            AudioEngineMetrics.Empty);
            throw;
        }
        catch (Exception exception)
        {
            graph = null;
            currentRoute = null;
            Exception? cleanupFailure = createdGraph is null
                ? null
                : await CaptureCleanupFailureAsync(createdGraph);
            const string message =
                "Le moteur audio n’a pas pu démarrer. Vérifiez la source et la sortie.";
            Exception reportedFailure = cleanupFailure is null
                ? exception
                : CombineFailures(exception, cleanupFailure);
            logger.LogError(reportedFailure, "{Message}", message);
            errorSink.Report("Audio", message);
            PublishStatus(
                AudioEngineState.Faulted,
                message,
                null,
                null,
                AudioEngineMetrics.Empty);
            throw new InvalidOperationException(message, reportedFailure);
        }
    }

    private async Task StopGraphCoreAsync(bool publishStopped)
    {
        AudioGraph? currentGraph = graph;
        if (currentGraph is null)
        {
            currentRoute = null;
            if (publishStopped)
            {
                PublishStatus(
                    AudioEngineState.Stopped,
                    "Moteur arrêté",
                    null,
                    null,
                    Status.Metrics);
            }

            return;
        }

        Volatile.Write(ref stopping, 1);
        recovery.SetStopping(true);
        Exception? cleanupFailure = null;
        try
        {
            PublishStatus(
                AudioEngineState.Stopping,
                "Arrêt sécurisé…",
                currentGraph.SourceFormat,
                currentGraph.OutputFormat,
                Status.Metrics);
            graph = null;
            currentRoute = null;
            cleanupFailure = await CaptureCleanupFailureAsync(currentGraph);
        }
        finally
        {
            Volatile.Write(ref stopping, 0);
            recovery.SetStopping(false);
        }

        if (cleanupFailure is not null)
        {
            const string message =
                "L’arrêt audio est incomplet. Toutes les ressources ont été traitées, mais au moins une libération native a échoué.";
            logger.LogError(cleanupFailure, "{Message}", message);
            errorSink.Report("Audio", message);
            PublishStatus(
                AudioEngineState.Faulted,
                message,
                null,
                null,
                Status.Metrics);
            throw new InvalidOperationException(message, cleanupFailure);
        }

        if (publishStopped)
        {
            PublishStatus(
                AudioEngineState.Stopped,
                "Moteur arrêté",
                null,
                null,
                Status.Metrics);
        }
    }

    private static AudioGraph CreateGraph(
        AudioRoute route,
        AudioProcessingSettings settings)
    {
        if (string.IsNullOrWhiteSpace(route.SourceDeviceId)
            || string.IsNullOrWhiteSpace(route.OutputDeviceId))
        {
            throw new ArgumentException("La source et la sortie doivent être sélectionnées.");
        }

        using var enumerator = new MMDeviceEnumerator();
        MMDevice? sourceDevice = null;
        MMDevice? outputDevice = null;
        WasapiCapture? capture = null;
        WasapiOut? output = null;

        try
        {
            sourceDevice = enumerator.GetDevice(route.SourceDeviceId);
            outputDevice = enumerator.GetDevice(route.OutputDeviceId);
            if (sourceDevice.DataFlow != DataFlow.Capture)
            {
                throw new InvalidOperationException(
                    "L’endpoint source sélectionné n’est pas un périphérique de capture.");
            }

            if (outputDevice.DataFlow != DataFlow.Render)
            {
                throw new InvalidOperationException(
                    "L’endpoint de sortie sélectionné n’est pas un périphérique de rendu.");
            }

            AudioDeviceInfo sourceDescription = NAudioDeviceService.Describe(
                sourceDevice,
                AudioDeviceDirection.Capture);
            AudioDeviceInfo outputDescription = NAudioDeviceService.Describe(
                outputDevice,
                AudioDeviceDirection.Render);
            AudioRouteValidationResult routeValidation = AudioRouteSafety.Validate(
                sourceDescription,
                outputDescription);
            if (!routeValidation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", routeValidation.Errors));
            }

            capture = new WasapiCapture(
                sourceDevice,
                useEventSync: true,
                audioBufferMillisecondsLength: CaptureLatencyMilliseconds);
            if (capture.WaveFormat.Channels is < 1 or > 2)
            {
                throw new NotSupportedException(
                    "La source doit être mono ou stéréo pour ce MVP.");
            }

            var buffered = new MonitoredBufferedWaveProvider(
                capture.WaveFormat,
                BufferCapacity);
            var standardFormat = new StandardWaveFormatProvider(buffered);
            ISampleProvider sampleProvider = standardFormat.ToSampleProvider();
            if (sampleProvider.WaveFormat.Channels == 1)
            {
                sampleProvider = new MonoToStereoSampleProvider(sampleProvider);
            }

            int outputSampleRate = outputDevice.AudioClient.MixFormat.SampleRate;
            var resampler = new AdaptiveResamplingSampleProvider(
                sampleProvider,
                outputSampleRate,
                () => buffered.BufferedDuration,
                TargetBuffer);
            var dspProvider = new DspSampleProvider(
                resampler,
                settings);
            output = new WasapiOut(
                outputDevice,
                AudioClientShareMode.Shared,
                useEventSync: true,
                latency: RenderLatencyMilliseconds);
            output.Init(dspProvider.ToWaveProvider());

            return new AudioGraph(
                sourceDevice,
                outputDevice,
                capture,
                output,
                buffered,
                resampler,
                dspProvider,
                capture.WaveFormat.ToString(),
                output.OutputWaveFormat.ToString());
        }
        catch (Exception creationFailure)
        {
            try
            {
                ExceptionResilientCleanup.Run(
                    () => output?.Dispose(),
                    () => capture?.Dispose(),
                    () => outputDevice?.Dispose(),
                    () => sourceDevice?.Dispose());
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "La création du graphe et la libération de ses ressources partielles ont échoué.",
                    creationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    private static async Task WaitForPrebufferAsync(
        AudioGraph audioGraph,
        CancellationToken cancellationToken)
    {
        DateTimeOffset timeoutAt = DateTimeOffset.UtcNow.AddSeconds(1);
        while (audioGraph.Buffer.BufferedDuration < TimeSpan.FromMilliseconds(80)
            && DateTimeOffset.UtcNow < timeoutAt)
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    private async Task DisposeGraphAsync(AudioGraph audioGraph)
    {
        await Task.Run(() =>
            ExceptionResilientCleanup.Run(
                () => audioGraph.Capture.DataAvailable -= OnCaptureDataAvailable,
                () => audioGraph.Capture.RecordingStopped -= OnRecordingStopped,
                () => audioGraph.Output.PlaybackStopped -= OnPlaybackStopped,
                audioGraph.Dispose));
    }

    private async Task<Exception?> CaptureCleanupFailureAsync(AudioGraph audioGraph)
    {
        try
        {
            await DisposeGraphAsync(audioGraph);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        AudioGraph? currentGraph = Volatile.Read(ref graph);
        if (currentGraph is null || !ReferenceEquals(sender, currentGraph.Capture))
        {
            return;
        }

        currentGraph.Buffer.AddSamples(
            eventArgs.Buffer,
            0,
            eventArgs.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        AudioGraph? currentGraph = Volatile.Read(ref graph);
        if (Volatile.Read(ref stopping) != 0
            || currentGraph is null
            || !ReferenceEquals(sender, currentGraph.Capture))
        {
            return;
        }

        BeginRecovery(eventArgs.Exception);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        AudioGraph? currentGraph = Volatile.Read(ref graph);
        if (Volatile.Read(ref stopping) != 0
            || currentGraph is null
            || !ReferenceEquals(sender, currentGraph.Output))
        {
            return;
        }

        BeginRecovery(eventArgs.Exception);
    }

    private void BeginRecovery(Exception? exception)
    {
        long generation = Volatile.Read(ref operationGeneration);
        recovery.TryStart(
            cancellationToken => RecoverAfterDeviceLossAsync(
                generation,
                exception,
                cancellationToken));
    }

    private async Task RecoverAfterDeviceLossAsync(
        long generation,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        try
        {
            string detail = exception is null
                ? "Le flux audio s’est arrêté de façon inattendue."
                : $"Le périphérique audio a été interrompu ({exception.GetType().Name}).";
            errorSink.Report("Audio", detail);
            logger.LogWarning(exception, "Interruption audio ; récupération en cours.");
            PublishStatus(
                AudioEngineState.DeviceLost,
                "Périphérique déconnecté — tentative de récupération…",
                Status.SourceFormat,
                Status.OutputFormat,
                Status.Metrics);

            AudioRoute? route = currentRoute;
            AudioProcessingSettings settings = Volatile.Read(ref currentSettings);
            await lifecycle.WaitAsync(cancellationToken);
            try
            {
                if (generation != Volatile.Read(ref operationGeneration))
                {
                    return;
                }

                try
                {
                    await StopGraphCoreAsync(publishStopped: false);
                }
                catch (InvalidOperationException cleanupException)
                {
                    logger.LogWarning(
                        cleanupException,
                        "Le nettoyage après perte du périphérique est incomplet ; la reconstruction va néanmoins être tentée.");
                    PublishStatus(
                        AudioEngineState.DeviceLost,
                        "Nettoyage incomplet — tentative de reconstruction avec un nouveau graphe…",
                        null,
                        null,
                        Status.Metrics);
                }
            }
            finally
            {
                lifecycle.Release();
            }

            if (route is null)
            {
                PublishStatus(
                    AudioEngineState.Faulted,
                    "La route audio active n’est plus disponible. Sélectionnez les périphériques puis réessayez.",
                    null,
                    null,
                    Status.Metrics);
                return;
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(attempt * 2),
                    cancellationToken);
                if (generation != Volatile.Read(ref operationGeneration))
                {
                    return;
                }

                await lifecycle.WaitAsync(cancellationToken);
                try
                {
                    if (generation != Volatile.Read(ref operationGeneration))
                    {
                        return;
                    }

                    try
                    {
                        await StartGraphCoreAsync(
                            route,
                            settings,
                            generation,
                            cancellationToken);
                        logger.LogInformation(
                            "Chemin audio récupéré après {Attempt} tentative(s).",
                            attempt);
                        return;
                    }
                    catch (InvalidOperationException) when (attempt < 3)
                    {
                    }
                }
                finally
                {
                    lifecycle.Release();
                }
            }

            PublishStatus(
                AudioEngineState.Faulted,
                "Récupération impossible. Rebranchez le périphérique puis réactivez.",
                null,
                null,
                Status.Metrics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Récupération audio annulée pendant l’arrêt du moteur.");
        }
        catch (Exception recoveryException)
        {
            logger.LogError(
                recoveryException,
                "La récupération automatique du chemin audio a échoué.");
            errorSink.Report(
                "Audio",
                "La récupération automatique du chemin audio a échoué.");
            if (generation == Volatile.Read(ref operationGeneration)
                && Volatile.Read(ref disposed) == 0)
            {
                PublishStatus(
                    AudioEngineState.Faulted,
                    "La récupération audio a échoué. Vérifiez les périphériques puis réessayez.",
                    null,
                    null,
                    Status.Metrics);
            }
        }
    }

    private void ConsumeDspMetrics()
    {
        if (Interlocked.Exchange(ref consumingMetrics, 1) != 0)
        {
            return;
        }

        try
        {
            AudioGraph? currentGraph = Volatile.Read(ref graph);
            if (currentGraph is null
                || !currentGraph.DspProvider.TryConsumeMetrics(
                    out DspReadSnapshot snapshot))
            {
                return;
            }

            var metrics = new AudioEngineMetrics(
                DateTimeOffset.UtcNow,
                snapshot.Metrics.InputPeak,
                snapshot.Metrics.AfterMainGainPeak,
                snapshot.Metrics.AfterBassBoostPeak,
                snapshot.Metrics.AfterCompressorPeak,
                snapshot.Metrics.AfterLimiterPeak,
                snapshot.Metrics.OutputPeak,
                snapshot.RequestedMainGainDb,
                snapshot.Metrics.AppliedMainGainDecibels,
                SpotifyBooster.Dsp.GainConversion.DecibelsToFactorDouble(
                    snapshot.Metrics.AppliedMainGainDecibels),
                snapshot.Metrics.CompressorGainReductionDecibels,
                snapshot.Metrics.LimiterGainReductionDecibels,
                snapshot.Metrics.InputPeak >= 0.0001f,
                IsClippingDetected(snapshot.Metrics),
                snapshot.ClipCount,
                snapshot.SoftClipCount,
                snapshot.LimitedFrameCount,
                snapshot.SanitizedSampleCount,
                snapshot.BufferCount,
                currentGraph.Buffer.UnderrunCount,
                currentGraph.Buffer.OverrunCount,
                currentGraph.DspProvider.WaveFormat.SampleRate,
                currentGraph.DspProvider.WaveFormat.Channels,
                CaptureLatencyMilliseconds,
                CaptureLatencyMilliseconds
                    + RenderLatencyMilliseconds
                    + currentGraph.Buffer.BufferedDuration.TotalMilliseconds,
                snapshot.ProcessingLoadPercent,
                DateTimeOffset.UtcNow - currentGraph.StartedAt);
            AudioEngineStatus currentStatus = Status;
            Volatile.Write(
                ref status,
                currentStatus with
                {
                    Metrics = metrics,
                });
            try
            {
                MetricsUpdated?.Invoke(this, metrics);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Un abonné aux métriques audio a échoué ; le moteur continue.");
                errorSink.Report("Audio", "Un abonné aux métriques audio a échoué.");
            }
        }
        finally
        {
            Volatile.Write(ref consumingMetrics, 0);
        }
    }

    internal static bool IsClippingDetected(SpotifyBooster.Dsp.DspMetrics metrics)
    {
        return metrics.ClippedSamples > 0;
    }

    private void PublishStatus(
        AudioEngineState state,
        string message,
        string? sourceFormat,
        string? outputFormat,
        AudioEngineMetrics metrics)
    {
        var updated = new AudioEngineStatus(
            state,
            message,
            sourceFormat,
            outputFormat,
            metrics,
            DateTimeOffset.UtcNow);
        Volatile.Write(ref status, updated);
        try
        {
            StatusChanged?.Invoke(this, updated);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Un abonné à l’état audio a échoué ; l’état interne reste valide.");
            errorSink.Report("Audio", "Une notification d’état audio a échoué.");
        }
    }

    private static AudioProcessingSettings ValidateProcessingSettings(
        AudioProcessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var wrapper = new AppSettings
        {
            FirstRunCompleted = true,
            ExpertModeAcknowledged =
                settings.Mode == AudioProtectionMode.RawExpert,
            Processing = settings,
        };
        SettingsValidationResult validation = SettingsValidator.Validate(wrapper);
        if (!validation.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                string.Join(" ", validation.Errors));
        }

        return validation.Settings.Processing;
    }

    private static AggregateException CombineFailures(
        Exception primaryFailure,
        Exception cleanupFailure)
    {
        return new AggregateException(
            "L’opération audio et son nettoyage ont toutes deux échoué.",
            primaryFailure,
            cleanupFailure).Flatten();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
    }

    private sealed class AudioGraph : IDisposable
    {
        public AudioGraph(
            MMDevice sourceDevice,
            MMDevice outputDevice,
            WasapiCapture capture,
            WasapiOut output,
            MonitoredBufferedWaveProvider buffer,
            AdaptiveResamplingSampleProvider resampler,
            DspSampleProvider dspProvider,
            string sourceFormat,
            string outputFormat)
        {
            SourceDevice = sourceDevice;
            OutputDevice = outputDevice;
            Capture = capture;
            Output = output;
            Buffer = buffer;
            Resampler = resampler;
            DspProvider = dspProvider;
            SourceFormat = sourceFormat;
            OutputFormat = outputFormat;
            StartedAt = DateTimeOffset.UtcNow;
        }

        public MMDevice SourceDevice { get; }

        public MMDevice OutputDevice { get; }

        public WasapiCapture Capture { get; }

        public WasapiOut Output { get; }

        public MonitoredBufferedWaveProvider Buffer { get; }

        public AdaptiveResamplingSampleProvider Resampler { get; }

        public DspSampleProvider DspProvider { get; }

        public string SourceFormat { get; }

        public string OutputFormat { get; }

        public DateTimeOffset StartedAt { get; }

        public void Dispose()
        {
            ExceptionResilientCleanup.Run(
                Capture.StopRecording,
                Output.Stop,
                DspProvider.RequestPanicReset,
                Buffer.Clear,
                Output.Dispose,
                Capture.Dispose,
                OutputDevice.Dispose,
                SourceDevice.Dispose);
        }
    }
}
