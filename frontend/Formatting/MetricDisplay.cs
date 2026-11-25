using System.Globalization;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;

namespace ClimaPulse.Desktop.Formatting;

/// <summary>Display text for metric readings. Shows what the services return; never scores.</summary>
public static class MetricDisplay
{
    public static readonly MetricType[] All =
    [
        MetricType.Temperature,
        MetricType.Precipitation,
        MetricType.WindSpeed,
    ];

    public static string Name(MetricType metric) =>
        metric switch
        {
            MetricType.Precipitation => "Rain",
            MetricType.WindSpeed => "Wind",
            _ => "Temperature",
        };

    public static string Value(MetricType metric, double value, TemperatureUnit unit) =>
        metric switch
        {
            MetricType.Temperature => TemperatureDisplay.Format(value, unit),
            MetricType.Precipitation => $"{value.ToString("0.0", CultureInfo.InvariantCulture)} mm",
            _ => $"{value.ToString("0.0", CultureInfo.InvariantCulture)} km/h",
        };

    /// <summary>A spread (standard deviation): no +32 offset for Fahrenheit.</summary>
    public static string Spread(MetricType metric, double value, TemperatureUnit unit) =>
        metric == MetricType.Temperature
            ? TemperatureDisplay.FormatDifference(value, unit)
            : Value(metric, value, unit);

    public static string ZScore(double z) =>
        z.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    /// <summary>How the baseline summarises a day: daily mean, total or maximum (contract 14.3).</summary>
    public static string DailyKind(MetricType metric) =>
        metric switch
        {
            MetricType.Precipitation => "total",
            MetricType.WindSpeed => "max",
            _ => "mean",
        };

    /// <summary>Short status for a card footer: the tier, or why there is none.</summary>
    public static string ShortStatus(MetricReadingDto? reading) =>
        reading?.DataStatus switch
        {
            DataStatus.Ok => reading.Tier?.ToString() ?? "No data",
            DataStatus.NoBaseline => "No baseline",
            DataStatus.ZeroVariance => "No variation",
            _ => "No data",
        };

    /// <summary>"Far hotter than usual for November." from the backend's tier and the sign of z.</summary>
    public static string Explanation(MetricType metric, SeverityTier tier, double z, string month)
    {
        if (tier == SeverityTier.Normal)
        {
            return $"Within the normal range for {month}.";
        }

        var strength = tier switch
        {
            SeverityTier.Watch => "Somewhat",
            SeverityTier.Warning => "Much",
            _ => "Far",
        };

        var direction = (metric, z >= 0) switch
        {
            (MetricType.Temperature, true) => "hotter",
            (MetricType.Temperature, false) => "colder",
            (MetricType.Precipitation, true) => "wetter",
            (MetricType.Precipitation, false) => "drier",
            (_, true) => "windier",
            (_, false) => "calmer",
        };

        return $"{strength} {direction} than usual for {month}.";
    }

    /// <summary>The month at the city (the backend scores against the city's local month).</summary>
    public static int CityMonth(DateTime utc, CityDto city) => CityTime(utc, city).Month;

    public static string MonthName(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    private static DateTime CityTime(DateTime utc, CityDto city) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(city.Timezone, out var zone)
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
            : utc.ToLocalTime();
}
