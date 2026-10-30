using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Net.Http;
using System.Windows;
using SpotifyBooster.App.Mvvm;
using SpotifyBooster.Infrastructure.Updates;

namespace SpotifyBooster.App.ViewModels;

public sealed class UpdatesViewModel : ObservableObject
{
    private readonly GitHubUpdateService service;
    private GitHubRelease? release;
    private bool busy;
    private string statusText = "Canal stable · Vérification manuelle sur GitHub.";

    public UpdatesViewModel(GitHubUpdateService service)
    {
        this.service = service;
        CheckCommand = new AsyncRelayCommand(CheckAsync, OnError, () => !busy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, OnError,
            () => !busy && release is not null && !IsPortable);
    }

    public string CurrentVersion { get; } = (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);
    public bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.mode"));
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }

    private async Task CheckAsync()
    {
        SetBusy(true);
        release = null;
        StatusText = "Recherche d’une version stable…";
        try
        {
            release = await service.CheckAsync(new Version(CurrentVersion));
            StatusText = release is null ? "Vous utilisez la dernière version stable disponible."
                : $"Version {release.Version} disponible." + (IsPortable
                    ? " Copie portable : utilisez l’archive de la release GitHub et conservez le dossier Data."
                    : " Cliquez sur Installer. Booster se fermera pour la mise à jour.");
        }
        finally { SetBusy(false); }
    }

    private async Task InstallAsync()
    {
        if (release is null || IsPortable) return;
        SetBusy(true);
        StatusText = "Téléchargement et vérification de l’installateur…";
        try
        {
            string path = await service.DownloadAsync(release);
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            start.ArgumentList.Add($"/UPDATEPID={Environment.ProcessId}");
            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("Impossible de lancer l’installateur.");
            StatusText = "Installateur ouvert. Fermeture de Spotify Booster…";
            ((App)Application.Current).RequestExit();
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        CheckCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
    }

    private void OnError(Exception exception)
    {
        StatusText = exception is HttpRequestException or TaskCanceledException
            ? "GitHub est indisponible ou la connexion a expiré. Réessayez plus tard."
            : "Mise à jour interrompue : " + exception.Message;
    }
}
