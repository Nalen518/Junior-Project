using System.Globalization;
using System.Windows.Data;

namespace ClimaPulse.Desktop.Page.Auth;

public sealed class InitialConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var name = value as string;

        return string.IsNullOrWhiteSpace(name)
            ? "?"
            : name.Trim()[0].ToString().ToUpperInvariant();
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