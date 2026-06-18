using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Skat.KawkaProject.UI.ViewModels;

public class ConnectionStatusConverter : IValueConverter
{
    public static readonly ConnectionStatusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ConnectionStatus status) return Brushes.Transparent;
        bool isBadge = parameter?.ToString() == "badge";

        return status switch
        {
            ConnectionStatus.Connected  => new SolidColorBrush(Color.Parse(isBadge ? "#1Fa6e3a1" : "#a6e3a1")),
            ConnectionStatus.Connecting => new SolidColorBrush(Color.Parse(isBadge ? "#1Ff9e2af" : "#f9e2af")),
            ConnectionStatus.Error      => new SolidColorBrush(Color.Parse(isBadge ? "#1Ff38ba8" : "#f38ba8")),
            _                           => new SolidColorBrush(Color.Parse(isBadge ? "#1F45475a" : "#45475a"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
