using System.Globalization;
using System.Windows.Data;
using ClimaPulse.Contracts;

namespace ClimaPulse.Desktop.Formatting;

/// <summary>One way to show a city's place and coordinates on every screen.</summary>
public static class CityFormat
{
    /// <summary>"West Java, ID", or just "ID" when there is no region.</summary>
    public static string Place(string? adminRegion, string countryCode) =>
        string.IsNullOrWhiteSpace(adminRegion) ? countryCode : $"{adminRegion}, {countryCode}";

    /// <summary>"-6.92, 107.61" with a dot, also on an Indonesian Windows (decimal comma).</summary>
    public static string Coordinates(double latitude, double longitude) =>
        $"{latitude.ToString("0.00", CultureInfo.InvariantCulture)}, {longitude.ToString("0.00", CultureInfo.InvariantCulture)}";
}

/// <summary>For search results in XAML. ConverterParameter "Coordinates" gives the coordinates, anything else the place and time zone.</summary>
public sealed class CitySearchResultConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not CitySearchResult city
            ? string.Empty
            : parameter as string == "Coordinates"
                ? CityFormat.Coordinates(city.Latitude, city.Longitude)
                : $"{CityFormat.Place(city.AdminRegion, city.CountryCode)} · {city.Timezone}";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
