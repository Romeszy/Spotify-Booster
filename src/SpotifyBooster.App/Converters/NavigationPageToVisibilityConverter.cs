using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SpotifyBooster.App.Navigation;

namespace SpotifyBooster.App.Converters;

public sealed class NavigationPageToVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is NavigationPage current
            && parameter is string expected
            && Enum.TryParse(expected, ignoreCase: true, out NavigationPage page)
            && current == page)
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
