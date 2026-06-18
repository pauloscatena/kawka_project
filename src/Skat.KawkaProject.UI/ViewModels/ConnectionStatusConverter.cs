using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Skat.KawkaProject.UI.ViewModels;

public class ConnectionStatusConverter : IValueConverter
{
    public static readonly ConnectionStatusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ConnectionStatus status
            ? status switch
            {
                ConnectionStatus.Connected => Brushes.LimeGreen,
                ConnectionStatus.Connecting => Brushes.Orange,
                ConnectionStatus.Error => Brushes.Red,
                _ => Brushes.Gray
            }
            : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
