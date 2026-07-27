using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using SpotifyBooster.App.Mvvm;
using SpotifyBooster.App.Navigation;
using SpotifyBooster.App.ViewModels;
using SpotifyBooster.Core.Configuration;
using SpotifyBooster.Core.Models;
using Forms = System.Windows.Forms;

namespace SpotifyBooster.App.Services;

public sealed class TrayIconService : IDisposable
{
    private const int MaximumToolTipLength = 63;
    private static readonly double[] GainPresets =
        [0, 3, 6, 10, 14, 20, 25, 50, 100, 250, 500, 1_000];

    private readonly Forms.NotifyIcon notifyIcon = new();
    private Icon? activeIcon;
    private Icon? inactiveIcon;
    private MainWindow? window;
    private MainViewModel? viewModel;
    private Action? requestExit;
    private int refreshQueued;
    private int disposed;

    public void Initialize(
        MainWindow mainWindow,
        MainViewModel mainViewModel,
        Action exitCallback)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(mainViewModel);
        ArgumentNullException.ThrowIfNull(exitCallback);

        Detach();
        window = mainWindow;
        viewModel = mainViewModel;
        requestExit = exitCallback;

        activeIcon ??= LoadResourceIcon("Assets/SpotifyBooster.ico");
        inactiveIcon ??= LoadResourceIcon("Assets/SpotifyBooster-inactive.ico");
        notifyIcon.Icon = inactiveIcon ?? activeIcon ?? SystemIcons.Application;
        notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
        notifyIcon.Visible = true;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.RenderDevices.CollectionChanged += OnRenderDevicesChanged;
        Refresh();
    }

    public void ShowBalloon(
        string title,
        string message,
        Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info,
        int timeoutMilliseconds = 3000)
    {
        if (Volatile.Read(ref disposed) != 0 || !notifyIcon.Visible)
        {
            return;
        }

        notifyIcon.ShowBalloonTip(
            Math.Max(1000, timeoutMilliseconds),
            title ?? string.Empty,
            message ?? string.Empty,
            icon);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Detach();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        activeIcon?.Dispose();
        inactiveIcon?.Dispose();
        activeIcon = null;
        inactiveIcon = null;
    }

    private void Refresh()
    {
        MainViewModel? currentViewModel = viewModel;
        if (currentViewModel is null || Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        notifyIcon.Text = BoundToolTip(
            $"Spotify Booster · {currentViewModel.EngineStatusText} · {currentViewModel.GainDbText}");
        notifyIcon.Icon = currentViewModel.IsEngineRunning
            ? activeIcon ?? SystemIcons.Application
            : inactiveIcon ?? activeIcon ?? SystemIcons.Application;

        Forms.ContextMenuStrip menu = BuildMenu(currentViewModel);
        Forms.ContextMenuStrip? previousMenu = notifyIcon.ContextMenuStrip;
        notifyIcon.ContextMenuStrip = menu;
        previousMenu?.Dispose();
    }

    private Forms.ContextMenuStrip BuildMenu(MainViewModel currentViewModel)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(CreateItem("Ouvrir Spotify Booster", (_, _) => RestoreWindow()));
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(CreateCommandItem(
            currentViewModel.IsEngineRunning ? "Arrêter le moteur" : "Démarrer le moteur",
            currentViewModel.ToggleEngineCommand));

        var gainMenu = new Forms.ToolStripMenuItem(
            $"Gain actuel : {currentViewModel.GainDbText}")
        {
            Enabled = false,
        };
        menu.Items.Add(gainMenu);

        var presetsMenu = new Forms.ToolStripMenuItem("Préréglages de gain");
        foreach (double preset in GainPresets.Where(
                     preset => preset <= currentViewModel.GainMaximumDb))
        {
            presetsMenu.DropDownItems.Add(CreateItem(
                FormatGain(preset),
                (_, _) => currentViewModel.SetGainDb(preset),
                IsSameGain(currentViewModel.MainGainDb, preset)));
        }

        menu.Items.Add(presetsMenu);
        menu.Items.Add(CreateItem(
            "Augmenter de 1 dB",
            (_, _) => currentViewModel.AdjustGainDb(1)));
        menu.Items.Add(CreateItem(
            "Diminuer de 1 dB",
            (_, _) => currentViewModel.AdjustGainDb(-1)));
        menu.Items.Add(CreateItem(
            "Retour à 0 dB",
            (_, _) => currentViewModel.PanicReset()));

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem(
            currentViewModel.BassBoostEnabled
                ? "Désactiver Bass Boost"
                : "Activer Bass Boost",
            (_, _) => currentViewModel.ToggleBassBoost()));

        var profilesMenu = new Forms.ToolStripMenuItem("Profil de basses");
        foreach (string profile in currentViewModel.Profiles)
        {
            profilesMenu.DropDownItems.Add(CreateItem(
                profile,
                (_, _) => currentViewModel.SelectProfile(profile),
                string.Equals(
                    currentViewModel.SelectedProfile,
                    profile,
                    StringComparison.Ordinal)));
        }

        menu.Items.Add(profilesMenu);

        var modesMenu = new Forms.ToolStripMenuItem("Mode de protection");
        modesMenu.DropDownItems.Add(CreateModeItem(
            "Protégé",
            AudioProtectionMode.Protected,
            currentViewModel));
        modesMenu.DropDownItems.Add(CreateModeItem(
            "Direct",
            AudioProtectionMode.Direct,
            currentViewModel));
        modesMenu.DropDownItems.Add(CreateModeItem(
            "Expert",
            AudioProtectionMode.RawExpert,
            currentViewModel));
        menu.Items.Add(modesMenu);

        var outputsMenu = new Forms.ToolStripMenuItem("Sortie physique");
        AudioDeviceInfo[] outputs = currentViewModel.RenderDevices
            .Where(device => !device.IsLikelyVirtual)
            .ToArray();
        if (outputs.Length == 0)
        {
            outputsMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(
                "Aucune sortie disponible")
            {
                Enabled = false,
            });
        }
        else
        {
            foreach (AudioDeviceInfo output in outputs)
            {
                Forms.ToolStripMenuItem outputItem = CreateItem(
                    output.Name,
                    (_, _) => currentViewModel.SelectedOutput = output,
                    string.Equals(
                        currentViewModel.SelectedOutput?.Id,
                        output.Id,
                        StringComparison.Ordinal));
                outputItem.Enabled = currentViewModel.CanChangeDevices;
                outputsMenu.DropDownItems.Add(outputItem);
            }
        }

        menu.Items.Add(outputsMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(CreateItem(
            "Diagnostic",
            (_, _) =>
            {
                currentViewModel.NavigateCommand.Execute(
                    NavigationPage.Diagnostics);
                RestoreWindow();
            }));
        menu.Items.Add(CreateItem("Quitter", (_, _) => requestExit?.Invoke()));
        return menu;
    }

    private Forms.ToolStripMenuItem CreateModeItem(
        string text,
        AudioProtectionMode mode,
        MainViewModel currentViewModel)
    {
        return CreateItem(
            text,
            (_, _) =>
            {
                currentViewModel.SelectProtectionMode(mode);
                if (currentViewModel.ExpertConfirmationVisible)
                {
                    RestoreWindow();
                }
            },
            currentViewModel.ProtectionMode == mode);
    }

    private static Forms.ToolStripMenuItem CreateCommandItem(
        string text,
        AsyncRelayCommand command)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Enabled = command.CanExecute(null),
        };
        item.Click += (_, _) => command.Execute(null);
        return item;
    }

    private static Forms.ToolStripMenuItem CreateItem(
        string text,
        EventHandler onClick,
        bool isChecked = false)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            Checked = isChecked,
        };
        item.Click += onClick;
        return item;
    }

    private static bool IsSameGain(double left, double right)
    {
        return Math.Abs(left - right) < 0.05;
    }

    private static string FormatGain(double gain)
    {
        return gain == 0 ? "0 dB" : $"+{gain:0} dB";
    }

    private static string BoundToolTip(string value)
    {
        return value.Length <= MaximumToolTipLength
            ? value
            : value[..MaximumToolTipLength];
    }

    private static Icon? LoadResourceIcon(string relativePath)
    {
        var uri = new Uri(
            $"pack://application:,,,/{relativePath}",
            UriKind.Absolute);
        System.Windows.Resources.StreamResourceInfo? resource =
            System.Windows.Application.GetResourceStream(uri);
        if (resource is null)
        {
            return null;
        }

        using Stream stream = resource.Stream;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private void RestoreWindow()
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null)
        {
            return;
        }

        currentWindow.RestoreFromTray();
    }

    private void OnNotifyIconDoubleClick(object? sender, EventArgs eventArgs)
    {
        RestoreWindow();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainViewModel.EngineStatusText)
            or nameof(MainViewModel.IsEngineRunning)
            or nameof(MainViewModel.CanActivate)
            or nameof(MainViewModel.MainGainDb)
            or nameof(MainViewModel.GainDbText)
            or nameof(MainViewModel.GainMaximumDb)
            or nameof(MainViewModel.BassBoostEnabled)
            or nameof(MainViewModel.SelectedProfile)
            or nameof(MainViewModel.ProtectionMode)
            or nameof(MainViewModel.SelectedOutput)
            or nameof(MainViewModel.CanChangeDevices))
        {
            QueueRefresh();
        }
    }

    private void OnRenderDevicesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        MainWindow? currentWindow = window;
        if (currentWindow is null
            || Interlocked.Exchange(ref refreshQueued, 1) != 0)
        {
            return;
        }

        _ = currentWindow.Dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref refreshQueued, 0);
            Refresh();
        });
    }

    private void Detach()
    {
        notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.RenderDevices.CollectionChanged -= OnRenderDevicesChanged;
        }

        Forms.ContextMenuStrip? menu = notifyIcon.ContextMenuStrip;
        notifyIcon.ContextMenuStrip = null;
        menu?.Dispose();
        window = null;
        viewModel = null;
        requestExit = null;
        Volatile.Write(ref refreshQueued, 0);
    }
}
