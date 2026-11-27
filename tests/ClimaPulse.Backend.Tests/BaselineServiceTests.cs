using System.Net;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>Baseline maths and the running-city guard, with no database (NFR-13).</summary>
public class BaselineMathTests
{
    private static HistoricalObservation Row(int month, double value, int year = 2000) =>
        new() { ObservedAt = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc), Value = value };

    [Fact]
    public void ComputeMonthly_gives_mean_sample_std_dev_and_count_per_month()
    {
        var rows = new[] { Row(1, 1), Row(1, 2), Row(1, 3), Row(1, 4), Row(2, 10), Row(2, 10) };

        var result = BaselineService.ComputeMonthly(7, MetricType.Temperature, rows);

        Assert.Equal([1, 2], result.Select(b => b.Month));
        var jan = result[0];
        Assert.Equal(2.5, jan.MeanValue);
        Assert.Equal(Math.Sqrt(5.0 / 3.0), jan.StdDevValue, 10);   // n - 1: sum of squares 5 over 3
        Assert.Equal(4, jan.SampleSize);
        Assert.Equal((7, MetricType.Temperature, 1995, 2024), (jan.CityId, jan.MetricType, jan.BaselineStartYear, jan.BaselineEndYear));
        Assert.Equal(0, result[1].StdDevValue);                    // zero variance is kept; monitoring reports it
    }

    [Fact]
    public void ComputeMonthly_skips_a_month_with_fewer_than_two_values()
    {
        var result = BaselineService.ComputeMonthly(7, MetricType.Precipitation, [Row(3, 5.0), Row(4, 1), Row(4, 2, 2001)]);

        Assert.Equal([4], result.Select(b => b.Month));
    }

    [Fact]
    public void Begin_blocks_a_second_refresh_of_the_same_city_until_the_first_ends()
    {
        var state = new CityRefreshState();

        using (state.Begin(1, "Yogyakarta"))
        {
            Assert.Throws<ConflictException>(() => state.Begin(1, "Yogyakarta"));
            using (state.Begin(2, "Jakarta")) { }   // other cities are not blocked
        }

        using (state.Begin(1, "Yogyakarta")) { }    // free again after dispose
    }
}

/// <summary>IBaselineService against climapulse_test. Open-Meteo is a stub, so no network.</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class BaselineServiceDatabaseTests
{
    private readonly string _connection;
    private readonly City _city;

    public BaselineServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        _connection = db.ConnectionString;
        _city = new City { Name = "Yogyakarta", CountryCode = "ID", Latitude = -7.7956, Longitude = 110.3695, Timezone = "Asia/Jakarta" };
        new CityRepository(_connection).Add(_city);
    }

    private BaselineService Service(StubHandler handler, CityRefreshState? state = null) => new(
        new CityRepository(_connection),
        new HistoricalObservationRepository(_connection),
        new BaselineRepository(_connection),
        new WeatherService(new HttpClient(handler), new OpenMeteoOptions()),
        state ?? new CityRefreshState());

    private static readonly StubHandler NoNetwork = new(new HttpRequestException("must not call Open-Meteo"));

    [Fact]
    public async Task Stored_history_is_used_with_no_Open_Meteo_call()
    {
        var rows = new List<HistoricalObservation>();
        foreach (var metric in BaselineService.ScoredMetrics)
            for (var year = 1995; year <= 1997; year++)
                rows.Add(new HistoricalObservation
                {
                    CityId = _city.Id, MetricType = metric,
                    ObservedAt = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc), Value = year - 1990
                });
        new HistoricalObservationRepository(_connection).AddRange(rows);
        var handler = new StubHandler(new HttpRequestException("must not call Open-Meteo"));
        var reported = new List<int>();

        var result = await Service(handler).RefreshAsync(_city.Id, new SyncProgress(reported));

        Assert.Equal(0, handler.Calls);
        Assert.Equal(3, result.Count);   // October, for temperature, precipitation and wind
        Assert.All(result, b => Assert.Equal((10, 6.0, 1.0, 3), (b.Month, b.Mean, b.StdDev, b.SampleSize)));
        Assert.Equal([10, 60, 100], reported);
        Assert.Equal(result.OrderBy(b => b.Metric), await Service(NoNetwork).ListAsync(_city.Id));
    }

    [Fact]
    public async Task No_history_downloads_the_archive_once_and_stores_it()
    {
        const string archive = """
            {"daily":{"time":["1995-01-01","1996-01-01"],
                      "temperature_2m_mean":[26.0,28.0],"precipitation_sum":[10.0,20.0],"wind_speed_10m_max":[5.0,6.0]}}
            """;
        var handler = new StubHandler(HttpStatusCode.OK, archive);

        var result = await Service(handler).RefreshAsync(_city.Id);

        Assert.Equal(1, handler.Calls);
        Assert.Equal([27.0, 15.0, 5.5], result.OrderBy(b => b.Metric).Select(b => b.Mean));
        Assert.Single(await Service(NoNetwork).ListAsync(_city.Id, MetricType.Precipitation));

        await Service(NoNetwork).RefreshAsync(_city.Id);   // second refresh reads the database only
    }

    [Fact]
    public async Task Recomputing_a_baseline_never_changes_a_past_event_FR28()
    {
        var history = new HistoricalObservationRepository(_connection);
        void AddOctober(int year, double temperature) => history.AddRange(BaselineService.ScoredMetrics.Select(m =>
            new HistoricalObservation
            {
                CityId = _city.Id, MetricType = m,
                ObservedAt = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc), Value = temperature
            }));

        AddOctober(1995, 27.0);
        AddOctober(1996, 29.0);   // October temperature: mean 28, std dev 1.41
        var before = (await Service(NoNetwork).RefreshAsync(_city.Id)).Single(b => b.Metric == MetricType.Temperature);

        // An event recorded against that baseline keeps a copy of its mean and standard deviation.
        var events = new AnomalyEventRepository(_connection);
        var e = new AnomalyEvent
        {
            CityId = _city.Id, BaselineId = before.Id, MetricType = MetricType.Temperature, SeverityTier = SeverityTier.Warning,
            StartedAt = new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc), PeakAt = new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc),
            PeakObservedValue = 33.0, PeakZScore = 3.5, BaselineMeanSnapshot = before.Mean, BaselineStdDevSnapshot = before.StdDev
        };
        events.Add(e);

        AddOctober(1997, 35.0);   // new history moves the baseline
        var after = (await Service(NoNetwork).RefreshAsync(_city.Id)).Single(b => b.Metric == MetricType.Temperature);

        Assert.Equal(before.Id, after.Id);          // same row, updated in place (FR-08)
        Assert.NotEqual(before.Mean, after.Mean);   // 28 -> 30.33
        Assert.NotEqual(before.StdDev, after.StdDev);
        var stored = events.GetById(e.Id)!;
        Assert.Equal((before.Mean, before.StdDev), (stored.BaselineMeanSnapshot, stored.BaselineStdDevSnapshot));
        Assert.Equal((33.0, 3.5, SeverityTier.Warning), (stored.PeakObservedValue, stored.PeakZScore, stored.SeverityTier));
    }

    [Fact]
    public async Task Unknown_city_and_a_running_refresh_throw()
    {
        var state = new CityRefreshState();

        await Assert.ThrowsAsync<NotFoundException>(() => Service(NoNetwork).RefreshAsync(999));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(NoNetwork).ListAsync(999));
        using (state.Begin(_city.Id, _city.Name))
            await Assert.ThrowsAsync<ConflictException>(() => Service(NoNetwork, state).RefreshAsync(_city.Id));
    }

    /// <summary>Progress&lt;T&gt; posts later; this records each report as it happens.</summary>
    private sealed class SyncProgress(List<int> values) : IProgress<int>
    {
        public void Report(int value) => values.Add(value);
    }
}
