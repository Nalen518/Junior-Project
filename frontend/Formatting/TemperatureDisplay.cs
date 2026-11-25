using System.Globalization;
using ClimaPulse.Enums;

namespace ClimaPulse.Desktop.Formatting;

public static class TemperatureDisplay
{
    public static string Format(
        double celsius,
        TemperatureUnit unit)
    {
        var value = unit == TemperatureUnit.Fahrenheit
            ? (celsius * 9d / 5d) + 32d
            : celsius;

        var suffix = unit == TemperatureUnit.Fahrenheit
            ? "°F"
            : "°C";

        return $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {suffix}";
    }

    public static string FormatDifference(
        double celsiusDifference,
        TemperatureUnit unit)
    {
        var value = unit == TemperatureUnit.Fahrenheit
            ? celsiusDifference * 9d / 5d
            : celsiusDifference;

        var suffix = unit == TemperatureUnit.Fahrenheit
            ? "°F"
            : "°C";

        return $"{value.ToString("0.0", CultureInfo.InvariantCulture)} {suffix}";
    }
}