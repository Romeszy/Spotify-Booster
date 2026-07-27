using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SpotifyBooster.App.ViewModels;
using SpotifyBooster.Core.Configuration;

namespace SpotifyBooster.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool applicationExitRequested;

    public event EventHandler? InitializationCompleted;

    public MainWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;
        try
        {
            await viewModel.InitializeAsync();
            InitializationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"L’initialisation a échoué. L’application va se fermer afin de ne pas rester dans un état partiel.\n\n{exception.Message}",
                "Spotify Booster",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ((App)Application.Current).RequestExit();
        }
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        Close();
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    public void RestoreFromTray()
    {
        ShowInTaskbar = true;
        ShowActivated = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public void RequestApplicationExit()
    {
        applicationExitRequested = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (applicationExitRequested)
        {
            return;
        }

        switch (viewModel.CloseBehavior)
        {
            case WindowCloseBehavior.MinimizeToTray:
                eventArgs.Cancel = true;
                HideToTray();
                ((App)Application.Current).NotifyMinimizedToTray();
                break;

            case WindowCloseBehavior.Ask:
                HandleCloseConfirmation(eventArgs);
                break;

            case WindowCloseBehavior.Exit:
                eventArgs.Cancel = true;
                _ = Dispatcher.BeginInvoke(
                    ((App)Application.Current).RequestExit);
                break;
        }
    }

    private void HandleCloseConfirmation(CancelEventArgs eventArgs)
    {
        var dialog = new CloseConfirmationWindow
        {
            Owner = this,
        };
        dialog.ShowDialog();

        switch (dialog.Choice)
        {
            case CloseConfirmationChoice.MinimizeToTray:
                eventArgs.Cancel = true;
                HideToTray();
                ((App)Application.Current).NotifyMinimizedToTray();
                break;

            case CloseConfirmationChoice.Exit:
                eventArgs.Cancel = true;
                _ = Dispatcher.BeginInvoke(
                    ((App)Application.Current).RequestExit);
                break;

            default:
                eventArgs.Cancel = true;
                break;
        }
    }
}
