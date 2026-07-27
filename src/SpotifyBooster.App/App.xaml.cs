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
using SpotifyBooster.Infrastructure.Spotify;
using SpotifyBooster.Infrastructure.Windows;

namespace SpotifyBooster.App;

public partial class App : Application
{
    private ServiceProvider? services;
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
            collection.AddSingleton(configuration);
            collection.AddLogging(builder =>
            {
                builder.SetMinimumLevel(minimumLevel);
                builder.AddDebug();
                builder.AddProvider(new AsyncFileLoggerProvider());
            });
            collection.AddSingleton<ApplicationState>();
            collection.AddSingleton<IErrorSink, InMemoryErrorSink>();
            collection.AddSingleton<ISettingsService, JsonSettingsService>();
            collection.AddSingleton<ISpotifyProcessDetector, SpotifyProcessDetector>();
            collection.AddSingleton<IDiagnosticReportService, DiagnosticReportService>();
            collection.AddSingleton<IWindowsSettingsLauncher, WindowsSettingsLauncher>();
            collection.AddSingleton<IAudioDeviceService, NAudioDeviceService>();
            collection.AddSingleton<IAudioEngine, NAudioEngine>();
            collection.AddSingleton<IDiagnosticFileExporter, DiagnosticFileExporter>();
            collection.AddSingleton<MainViewModel>();
            collection.AddSingleton<MainWindow>();

            services = collection.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });
            var window = services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
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
