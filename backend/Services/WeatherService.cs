using System.Globalization;
using System.Net;
using System.Text.Json;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;

namespace ClimaPulse.Services;

/// <summary>
/// The only class that talks to Open-Meteo. It uses two endpoints: the forecast API
/// for today's reading and the archive API for historical data.
/// </summary>
public class WeatherService
{
    /// <summary>Baseline period (D5), matching baseline_start_year and baseline_end_year in seed.sql.</summary>
    public const string ArchiveStartDate = "1995-01-01";
    public const string ArchiveEndDate = "2024-12-31";

    // The same three daily variables on both endpoints, so today and the baseline compare like for like.
    private const string DailyVariables = "temperature_2m_mean,precipitation_sum,wind_speed_10m_max";

    // Units are Open-Meteo's defaults, sent anyway so a default change cannot shift the numbers.
    private const string Units = "temperature_unit=celsius&wind_speed_unit=kmh&precipitation_unit=mm";

    private static readonly (string Variable, MetricType Metric)[] Metrics =
    {
        ("temperature_2m_mean", MetricType.Temperature),
        ("precipitation_sum", MetricType.Precipitation),
        ("wind_speed_10m_max", MetricType.WindSpeed),
    };

    private readonly HttpClient _httpClient;

    /// <summary>Forecast API address, used by <see cref="FetchTodayAsync"/>.</summary>
    public Uri ForecastUri { get; }

    /// <summary>Archive API address, used by <see cref="FetchArchiveAsync"/>.</summary>
    public Uri ArchiveUri { get; }

    /// <summary>Geocoding API address, used by <see cref="SearchAsync"/>.</summary>
    public Uri GeocodingUri { get; }

    public WeatherService(HttpClient httpClient, OpenMeteoOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        ForecastUri = ParseHttpUri(options.ForecastBaseUrl, nameof(options.ForecastBaseUrl));
        ArchiveUri = ParseHttpUri(options.ArchiveBaseUrl, nameof(options.ArchiveBaseUrl));
        GeocodingUri = ParseHttpUri(options.GeocodingBaseUrl, nameof(options.GeocodingBaseUrl));
    }

    /// <summary>Places matching a name through Open-Meteo geocoding (SRD FR-01). Nothing is stored.</summary>
    public async Task<List<CitySearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var json = await GetAsync(BuildSearchUrl(GeocodingUri, query), null, ct);
        return Parse(() => ParseSearch(json), null);
    }

    /// <summary>Today's daily values in the city's own time zone (SRD FR-06).</summary>
    public async Task<TodayReading> FetchTodayAsync(City city, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(city);
        var json = await GetAsync(BuildTodayUrl(ForecastUri, city), city.Id, ct);
        return Parse(() => ParseToday(json), city.Id);
    }

    /// <summary>
    /// The last <paramref name="days"/> days up to and including today, oldest first, in the
    /// city's time zone, from one forecast call with past_days (FR-20, S4 "Last 14 days").
    /// The archive is not used because it lags about 5 days behind.
    /// </summary>
    public async Task<List<TodayReading>> FetchRecentAsync(City city, int days, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        var json = await GetAsync(BuildTodayUrl(ForecastUri, city, days - 1), city.Id, ct);
        return Parse(() => ParseDays(json), city.Id);
    }

    /// <summary>
    /// The full baseline period as one row per day and metric (SRD FR-07), about 32,900 rows.
    /// Nothing is saved here: IBaselineService stores the rows and reports progress.
    /// </summary>
    public async Task<List<HistoricalObservation>> FetchArchiveAsync(City city, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(city);
        var json = await GetAsync(BuildArchiveUrl(ArchiveUri, city), city.Id, ct);
        return Parse(() => ParseArchive(json, city.Id, DateTime.UtcNow), city.Id);
    }

    /// <summary>Geocoding URL: up to 10 English results for a place name.</summary>
    public static string BuildSearchUrl(Uri geocodingUri, string query) =>
        $"{geocodingUri}?name={Uri.EscapeDataString(query)}&count=10&language=en&format=json";

    /// <summary>
    /// Reads geocoding results[]. No "results" key means no match, an empty list.
    /// Hits without a country code or time zone are skipped: a city cannot be stored without them.
    /// </summary>
    public static List<CitySearchResult> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results))
            return [];

        string? Text(JsonElement hit, string name) =>
            hit.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var list = new List<CitySearchResult>();
        foreach (var hit in results.EnumerateArray())
        {
            var country = Text(hit, "country_code");
            var timezone = Text(hit, "timezone");
            if (string.IsNullOrEmpty(country) || string.IsNullOrEmpty(timezone))
                continue;

            list.Add(new CitySearchResult(
                Text(hit, "name") ?? string.Empty,
                country,
                Text(hit, "admin1"),
                hit.GetProperty("latitude").GetDouble(),
                hit.GetProperty("longitude").GetDouble(),
                timezone,
                hit.TryGetProperty("elevation", out var elev) ? NumberOrNull(elev) : null,
                hit.TryGetProperty("id", out var id) ? id.GetRawText() : null));
        }

        return list;
    }

    // Every Open-Meteo failure becomes UpstreamException (contract section 7). The user cancelling stays a cancel.
    private async Task<string> GetAsync(string url, int? cityId, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new UpstreamException("Open-Meteo limit reached. Try again in a minute.", cityId);
            if (!response.IsSuccessStatusCode)
                // shortcut: the 400 "reason" is not logged yet (no logger in the backend), add it with the log file.
                throw new UpstreamException($"Open-Meteo returned an error ({(int)response.StatusCode}).", cityId);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException e)
        {
            throw new UpstreamException("Open-Meteo could not be reached. Check the internet connection.", cityId, e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new UpstreamException("Open-Meteo did not answer in time.", cityId, e);
        }
    }

    private static T Parse<T>(Func<T> parse, int? cityId)
    {
        try
        {
            return parse();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException
                                       or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new UpstreamException("Open-Meteo sent an answer that could not be read.", cityId, e);
        }
    }

    /// <summary>
    /// Forecast URL for one day. Always sends the city's time zone: without it Open-Meteo
    /// silently uses GMT and "today" is the wrong day.
    /// </summary>
    public static string BuildTodayUrl(Uri forecastUri, City city, int pastDays = 0) =>
        $"{forecastUri}?{Coordinates(city)}&daily={DailyVariables}" +
        $"&timezone={Uri.EscapeDataString(city.Timezone)}&forecast_days=1" +
        (pastDays > 0 ? $"&past_days={pastDays.ToString(CultureInfo.InvariantCulture)}" : "") + $"&{Units}";

    /// <summary>Archive URL for the whole baseline period, in the city's time zone.</summary>
    public static string BuildArchiveUrl(Uri archiveUri, City city) =>
        $"{archiveUri}?{Coordinates(city)}&start_date={ArchiveStartDate}&end_date={ArchiveEndDate}" +
        $"&daily={DailyVariables}&timezone={Uri.EscapeDataString(city.Timezone)}&{Units}";

    /// <summary>Reads daily[0] of a forecast answer. A null value stays null (NoData for that metric).</summary>
    public static TodayReading ParseToday(string json) => ParseDays(json)[0];

    /// <summary>Reads every day of a forecast answer, oldest first. A null value stays null.</summary>
    public static List<TodayReading> ParseDays(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var daily = doc.RootElement.GetProperty("daily");

        double? At(string variable, int i) =>
            daily.TryGetProperty(variable, out var values) && values.GetArrayLength() > i
                ? NumberOrNull(values[i])
                : null;

        return daily.GetProperty("time").EnumerateArray()
            .Select((t, i) => new TodayReading(
                DateOnly.ParseExact(t.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                At("temperature_2m_mean", i),
                At("precipitation_sum", i),
                At("wind_speed_10m_max", i)))
            .ToList();
    }

    /// <summary>
    /// Turns an archive answer into rows. A null day is skipped for that metric only.
    /// Each local date becomes a date label at 00:00Z (see DbMapping).
    /// </summary>
    public static List<HistoricalObservation> ParseArchive(string json, int cityId, DateTime fetchedAtUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var daily = doc.RootElement.GetProperty("daily");
        var dates = daily.GetProperty("time").EnumerateArray()
            .Select(t => DateTime.SpecifyKind(
                DateTime.ParseExact(t.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeKind.Utc))
            .ToList();

        var rows = new List<HistoricalObservation>(dates.Count * Metrics.Length);
        foreach (var (variable, metric) in Metrics)
        {
            if (!daily.TryGetProperty(variable, out var values))
                continue;

            var i = 0;
            foreach (var value in values.EnumerateArray())
            {
                if (i < dates.Count && NumberOrNull(value) is { } v)
                {
                    rows.Add(new HistoricalObservation
                    {
                        CityId = cityId,
                        MetricType = metric,
                        ObservedAt = dates[i],
                        Value = v,
                        FetchedAt = fetchedAtUtc,
                    });
                }
                i++;
            }
        }

        return rows;
    }

    // Invariant culture with 4 decimals: a comma decimal makes Open-Meteo answer HTTP 400.
    private static string Coordinates(City city) =>
        string.Create(CultureInfo.InvariantCulture, $"latitude={city.Latitude:F4}&longitude={city.Longitude:F4}");

    private static double? NumberOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static Uri ParseHttpUri(string? value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"{name} must be an absolute http or https URL.", name);
        }

        return uri;
    }
}
