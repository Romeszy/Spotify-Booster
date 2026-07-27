using System.Windows;

namespace SpotifyBooster.App;

public enum CloseConfirmationChoice
{
    Cancel,
    MinimizeToTray,
    Exit,
}

public partial class CloseConfirmationWindow : Window
{
    public CloseConfirmationWindow()
    {
        InitializeComponent();
    }

    public CloseConfirmationChoice Choice { get; private set; }

    private void CancelButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        Choice = CloseConfirmationChoice.Cancel;
        Close();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        Choice = CloseConfirmationChoice.MinimizeToTray;
        Close();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        Choice = CloseConfirmationChoice.Exit;
        Close();
    }
}
