using System.Globalization;
using System.Net;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>Open-Meteo URLs and parsing, with saved answers instead of the network.</summary>
public class WeatherServiceTests
{
    private static readonly City Yogyakarta = new()
    {
        Id = 7, Name = "Yogyakarta", CountryCode = "ID",
        Latitude = -7.7956, Longitude = 110.3695, Timezone = "Asia/Jakarta"
    };

    private static readonly OpenMeteoOptions Options = new();

    // Shape of a real forecast answer (2026-11-24), trimmed to the fields we read.
    private const string ForecastJson = """
        {"latitude":-7.768,"longitude":110.375,"utc_offset_seconds":25200,"timezone":"Asia/Jakarta",
         "daily":{"time":["2026-11-24"],"temperature_2m_mean":[26.4],"precipitation_sum":[null],"wind_speed_10m_max":[11.2]}}
        """;

    private const string ArchiveJson = """
        {"daily":{"time":["1995-01-01","1995-01-02"],
                  "temperature_2m_mean":[25.1,24.8],"precipitation_sum":[3.2,null],"wind_speed_10m_max":[9.0,8.5]}}
        """;

    [Fact]
    public void Urls_use_a_dot_decimal_even_on_Indonesian_Windows()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("id-ID");
        try
        {
            var url = WeatherService.BuildTodayUrl(new Uri(Options.ForecastBaseUrl), Yogyakarta);

            Assert.Contains("latitude=-7.7956&longitude=110.3695", url);
            Assert.DoesNotContain("7,7956", url);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Recent_days_add_past_days_to_the_forecast_url_and_today_does_not()
    {
        var recent = WeatherService.BuildTodayUrl(new Uri(Options.ForecastBaseUrl), Yogyakarta, pastDays: 13);
        var today = WeatherService.BuildTodayUrl(new Uri(Options.ForecastBaseUrl), Yogyakarta);

        Assert.Contains("&forecast_days=1&past_days=13&", recent);
        Assert.DoesNotContain("past_days", today);
    }

    [Fact]
    public void ParseDays_reads_every_day_oldest_first_and_keeps_nulls()
    {
        const string json = """
            {"daily":{"time":["2026-11-11","2026-11-12"],
                      "temperature_2m_mean":[27.5,null],"precipitation_sum":[0.0,12.4],"wind_speed_10m_max":[8.1,9.0]}}
            """;

        var days = WeatherService.ParseDays(json);

        Assert.Equal([new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12)], days.Select(d => d.LocalDate));
        Assert.Equal(new TodayReading(new DateOnly(2026, 11, 12), null, 12.4, 9.0), days[1]);
    }

    [Fact]
    public void Urls_always_send_the_city_time_zone()
    {
        var today = WeatherService.BuildTodayUrl(new Uri(Options.ForecastBaseUrl), Yogyakarta);
        var archive = WeatherService.BuildArchiveUrl(new Uri(Options.ArchiveBaseUrl), Yogyakarta);

        Assert.Contains("timezone=Asia%2FJakarta", today);
        Assert.Contains("forecast_days=1", today);
        Assert.Contains("timezone=Asia%2FJakarta", archive);
        Assert.Contains("start_date=1995-01-01&end_date=2024-12-31", archive);
    }

    [Fact]
    public void ParseToday_reads_daily_values_and_keeps_null_as_no_data()
    {
        var reading = WeatherService.ParseToday(ForecastJson);

        Assert.Equal(new DateOnly(2026, 11, 24), reading.LocalDate);
        Assert.Equal(26.4, reading.Temperature);
        Assert.Null(reading.Precipitation);
        Assert.Equal(11.2, reading.WindSpeed);
    }

    [Fact]
    public void ParseSearch_maps_results_and_skips_hits_without_country_or_time_zone()
    {
        const string json = """
            {"results":[
              {"id":1621177,"name":"Yogyakarta","latitude":-7.80139,"longitude":110.36472,"elevation":113.0,
               "country_code":"ID","admin1":"Yogyakarta","timezone":"Asia/Jakarta"},
              {"id":2,"name":"Nowhere","latitude":1.0,"longitude":2.0,"timezone":"UTC"},
              {"id":3,"name":"Smalltown","latitude":1.0,"longitude":2.0,"country_code":"ID","timezone":"Asia/Jakarta"}
            ]}
            """;

        var results = WeatherService.ParseSearch(json);

        Assert.Equal(2, results.Count);
        Assert.Equal(new CitySearchResult("Yogyakarta", "ID", "Yogyakarta", -7.80139, 110.36472, "Asia/Jakarta", 113.0, "1621177"), results[0]);
        Assert.Null(results[1].AdminRegion);
        Assert.Null(results[1].ElevationM);
    }

    [Fact]
    public void ParseSearch_with_no_results_key_is_an_empty_list()
    {
        Assert.Empty(WeatherService.ParseSearch("""{"generationtime_ms":0.5}"""));
    }

    [Fact]
    public void BuildSearchUrl_escapes_the_query()
    {
        var url = WeatherService.BuildSearchUrl(new Uri(Options.GeocodingBaseUrl), "São Paulo");

        Assert.Equal("https://geocoding-api.open-meteo.com/v1/search?name=S%C3%A3o%20Paulo&count=10&language=en&format=json", url);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":true,"reason":"bad"}""")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"daily":{}}""")]
    public async Task Open_Meteo_failures_become_UpstreamException_with_the_city_id(HttpStatusCode status, string body)
    {
        var service = new WeatherService(new HttpClient(new StubHandler(status, body)), Options);

        var ex = await Assert.ThrowsAsync<UpstreamException>(() => service.FetchTodayAsync(Yogyakarta));

        Assert.Equal(7, ex.CityId);
    }

    [Fact]
    public async Task An_HTTP_400_keeps_Open_Meteo_reason_for_the_log_but_not_in_the_user_message()
    {
        const string body = """{"error":true,"reason":"Cannot initialize WeatherVariable from invalid String value tempeture_2m."}""";
        var service = new WeatherService(new HttpClient(new StubHandler(HttpStatusCode.BadRequest, body)), Options);

        var ex = await Assert.ThrowsAsync<UpstreamException>(() => service.FetchTodayAsync(Yogyakarta));

        Assert.Equal("Open-Meteo returned an error (400).", ex.Message);
        Assert.Equal("Open-Meteo HTTP 400: Cannot initialize WeatherVariable from invalid String value tempeture_2m.", ex.InnerException?.Message);
        Assert.Contains("tempeture_2m", ex.ToString());   // what the error log writes
    }

    [Theory]
    [InlineData("""{"error":true,"reason":"bad latitude"}""", "bad latitude")]
    [InlineData("<html>502 Bad Gateway</html>", "<html>502 Bad Gateway</html>")]
    [InlineData("  ", "(no body)")]
    public void ErrorReason_reads_the_JSON_reason_or_falls_back_to_the_text(string body, string expected)
    {
        Assert.Equal(expected, WeatherService.ErrorReason(body));
    }

    [Fact]
    public async Task No_network_becomes_UpstreamException()
    {
        var service = new WeatherService(new HttpClient(new StubHandler(new HttpRequestException("offline"))), Options);

        await Assert.ThrowsAsync<UpstreamException>(() => service.SearchAsync("Yogyakarta"));
    }

    [Fact]
    public void ParseArchive_makes_one_row_per_day_and_metric_and_skips_nulls()
    {
        var fetchedAt = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
        var rows = WeatherService.ParseArchive(ArchiveJson, 7, fetchedAt);

        Assert.Equal(5, rows.Count);   // 2 days x 3 metrics, minus one null rain value
        Assert.All(rows, r => Assert.Equal(7, r.CityId));
        Assert.DoesNotContain(rows, r => r.MetricType == MetricType.Precipitation && r.ObservedAt.Day == 2);

        var first = rows.First(r => r.MetricType == MetricType.Temperature);
        Assert.Equal(new DateTime(1995, 1, 1, 0, 0, 0, DateTimeKind.Utc), first.ObservedAt);
        Assert.Equal(DateTimeKind.Utc, first.ObservedAt.Kind);
        Assert.Equal(25.1, first.Value);
    }
}
