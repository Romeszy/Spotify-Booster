using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpotifyBooster.App.Services;
using SpotifyBooster.App.ViewModels;
using SpotifyBooster.Audio.Devices;
using SpotifyBooster.Audio.Engine;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Services;
using SpotifyBooster.Infrastructure.Diagnostics;
using SpotifyBooster.Infrastructure.Logging;
using SpotifyBooster.Infrastructure.Persistence;
using SpotifyBooster.Infrastructure.Storage;
using SpotifyBooster.Infrastructure.Windows;

namespace SpotifyBooster.App;

public partial class App : Application
{
    private ServiceProvider? services;
    private TrayIconService? trayIconService;
    private GlobalHotkeyService? globalHotkeyService;
    private bool startMinimizedRequested;
    private int exitRequested;
    private int minimizedNotificationShown;
    private int handlingDispatcherException;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();
            LogLevel minimumLevel = Enum.TryParse(
                configuration["Logging:MinimumLevel"],
                ignoreCase: true,
                out LogLevel configuredLevel)
                ? configuredLevel
                : LogLevel.Information;

            var collection = new ServiceCollection();
            AppDataPaths dataPaths = AppDataPaths.Resolve();
            collection.AddSingleton(configuration);
            collection.AddSingleton(dataPaths);
            collection.AddLogging(builder =>
            {
                builder.SetMinimumLevel(minimumLevel);
                builder.AddDebug();
                builder.AddProvider(new AsyncFileLoggerProvider(dataPaths));
            });
            collection.AddSingleton<ApplicationState>();
            collection.AddSingleton<IErrorSink, InMemoryErrorSink>();
            collection.AddSingleton<ISettingsService, JsonSettingsService>();
            collection.AddSingleton<IAudioApplicationProcessDetector,
                AudioApplicationProcessDetector>();
            collection.AddSingleton<IDiagnosticReportService, DiagnosticReportService>();
            collection.AddSingleton<IWindowsSettingsLauncher, WindowsSettingsLauncher>();
            collection.AddSingleton<IStartupService, StartupService>();
            collection.AddSingleton<IAudioDeviceService, NAudioDeviceService>();
            collection.AddSingleton<IAudioEngine, NAudioEngine>();
            collection.AddSingleton<IDiagnosticFileExporter, DiagnosticFileExporter>();
            collection.AddSingleton<TrayIconService>();
            collection.AddSingleton<GlobalHotkeyService>();
            collection.AddSingleton<MainViewModel>();
            collection.AddSingleton<MainWindow>();

            services = collection.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });
            var window = services.GetRequiredService<MainWindow>();
            var viewModel = services.GetRequiredService<MainViewModel>();
            trayIconService = services.GetRequiredService<TrayIconService>();
            globalHotkeyService =
                services.GetRequiredService<GlobalHotkeyService>();
            startMinimizedRequested = e.Args.Contains(
                "--minimized",
                StringComparer.OrdinalIgnoreCase);
            MainWindow = window;
            window.InitializationCompleted +=
                OnMainWindowInitializationCompleted;
            if (startMinimizedRequested)
            {
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowState = WindowState.Minimized;
            }

            window.Show();
            if (startMinimizedRequested)
            {
                window.Hide();
            }

            trayIconService.Initialize(window, viewModel, RequestExit);
            globalHotkeyService.RegistrationFailed +=
                OnHotkeyRegistrationFailed;
            globalHotkeyService.Initialize(window, viewModel);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Spotify Booster n’a pas pu démarrer.\n\n{exception.Message}",
                "Erreur de démarrage",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.InitializationCompleted -=
                OnMainWindowInitializationCompleted;
        }

        if (globalHotkeyService is not null)
        {
            globalHotkeyService.RegistrationFailed -=
                OnHotkeyRegistrationFailed;
            globalHotkeyService.Dispose();
            globalHotkeyService = null;
        }

        trayIconService?.Dispose();
        trayIconService = null;

        ServiceProvider? provider = services;
        if (provider is not null)
        {
            Task.Run(() => DisposeServicesAsync(provider))
                .GetAwaiter()
                .GetResult();
        }

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        base.OnExit(e);
    }

    public void RequestExit()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(RequestExit);
            return;
        }

        if (Interlocked.Exchange(ref exitRequested, 1) != 0)
        {
            return;
        }

        globalHotkeyService?.Dispose();
        trayIconService?.Dispose();
        if (MainWindow is MainWindow window)
        {
            window.RequestApplicationExit();
        }

        Shutdown();
    }

    public void NotifyMinimizedToTray()
    {
        MainViewModel? viewModel = services?.GetService<MainViewModel>();
        if (viewModel?.NotificationsEnabled != true
            || Interlocked.Exchange(ref minimizedNotificationShown, 1) != 0)
        {
            return;
        }

        trayIconService?.ShowBalloon(
            "Spotify Booster reste actif",
            "Double-cliquez sur l’icône pour restaurer la fenêtre.");
    }

    private void OnMainWindowInitializationCompleted(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        MainViewModel? viewModel = services?.GetService<MainViewModel>();
        if (startMinimizedRequested || viewModel?.StartMinimized == true)
        {
            window.HideToTray();
        }
    }

    private void OnHotkeyRegistrationFailed(
        object? sender,
        HotkeyRegistrationFailedEventArgs eventArgs)
    {
        string message =
            $"Le raccourci {eventArgs.Gesture} n’a pas pu être activé : "
            + eventArgs.Message;
        services?.GetService<IErrorSink>()?.Report(
            "Raccourcis globaux",
            message);
        services?.GetService<ILogger<App>>()?.LogWarning(
            "Échec d’enregistrement du raccourci {SettingName} ({Gesture}), "
            + "erreur native {NativeErrorCode}.",
            eventArgs.SettingName,
            eventArgs.Gesture,
            eventArgs.NativeErrorCode);

        MainViewModel? viewModel = services?.GetService<MainViewModel>();
        if (viewModel?.NotificationsEnabled == true)
        {
            trayIconService?.ShowBalloon(
                "Raccourci non disponible",
                message,
                System.Windows.Forms.ToolTipIcon.Warning,
                5000);
        }
    }

    private async Task DisposeServicesAsync(ServiceProvider provider)
    {
        MainViewModel? viewModel = provider.GetService<MainViewModel>();
        if (viewModel is not null)
        {
            try
            {
                await viewModel.DisposeAsync();
            }
            catch (Exception exception)
            {
                ReportUnhandled("Fermeture interface", exception);
            }
        }

        IAudioEngine? engine = provider.GetService<IAudioEngine>();
        if (engine is not null)
        {
            try
            {
                await engine.DisposeAsync();
            }
            catch (Exception exception)
            {
                ReportUnhandled("Fermeture audio", exception);
            }
        }

        try
        {
            await provider.DisposeAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Spotify Booster service disposal failed: {exception.GetType().Name}");
        }
    }

    private async void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        ReportUnhandled("Interface", eventArgs.Exception);
        eventArgs.Handled = true;
        if (Interlocked.Exchange(ref handlingDispatcherException, 1) != 0)
        {
            Shutdown(-1);
            return;
        }

        bool audioStopped = false;
        try
        {
            IAudioEngine? engine = services?.GetService<IAudioEngine>();
            if (engine is not null)
            {
                engine.PanicReset();
                await engine.StopAsync();
            }

            audioStopped = true;
        }
        catch (Exception stopException)
        {
            ReportUnhandled("Arrêt de sécurité", stopException);
        }

        MessageBox.Show(
            audioStopped
                ? "Une erreur inattendue a été interceptée. Le gain a été remis à 100 %, le moteur audio a été arrêté et l’application va se fermer."
                : "Une erreur inattendue a été interceptée. L’arrêt audio n’a pas pu être confirmé ; l’application va se fermer immédiatement.",
            "Spotify Booster",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(-1);
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        ReportUnhandled("Tâche", eventArgs.Exception);
        eventArgs.SetObserved();
    }

    private void OnDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs eventArgs)
    {
        if (eventArgs.ExceptionObject is Exception exception)
        {
            ReportUnhandled("Processus", exception);
        }
    }

    private void ReportUnhandled(string component, Exception exception)
    {
        services?.GetService<IErrorSink>()?.Report(
            component,
            $"Erreur inattendue : {exception.GetType().Name}.");
        services?.GetService<ILogger<App>>()?.LogError(
            exception,
            "Erreur non gérée dans {Component}.",
            component);
    }
}
