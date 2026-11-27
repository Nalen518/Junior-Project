using System.Globalization;
using System.Net;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;
using ClimaPulse.Strategies;

namespace ClimaPulse.Backend.Tests;

/// <summary>IMonitoringService against climapulse_test. Open-Meteo is a stub answering one fixed day.</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class MonitoringServiceDatabaseTests
{
    private readonly string _connection;
    private readonly CityRefreshState _state = new();
    private readonly City _city;
    private readonly int _healthAdvisoryId;

    public MonitoringServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        _connection = db.ConnectionString;
        _city = new City { Name = "Yogyakarta", CountryCode = "ID", Latitude = -7.7956, Longitude = 110.3695, Timezone = "Asia/Jakarta" };
        new CityRepository(_connection).Add(_city);

        // October temperature baseline: mean 28, std dev 1.5. No precipitation baseline.
        new BaselineRepository(_connection).Add(new Baseline
        {
            CityId = _city.Id, MetricType = MetricType.Temperature, Month = 10, MeanValue = 28.0, StdDevValue = 1.5,
            SampleSize = 930, BaselineStartYear = 1995, BaselineEndYear = 2024
        });

        var advisories = new AdvisoryRepository(_connection);
        foreach (var tier in new[] { SeverityTier.Watch, SeverityTier.Warning, SeverityTier.Extreme })
        foreach (var category in new[] { AdvisoryCategory.Health, AdvisoryCategory.Climate })
        {
            var advisory = new Advisory
            {
                MetricType = MetricType.Temperature, SeverityTier = tier, Category = category, Title = $"{tier} {category}", Body = "Body"
            };
            advisories.Add(advisory);
            if (tier == SeverityTier.Warning && category == AdvisoryCategory.Health)
                _healthAdvisoryId = advisory.Id;
        }
    }

    /// <summary>Refreshes with a stub forecast for 2026-10-05 (October) giving this temperature, rain and wind.</summary>
    private Task<RefreshResultDto> Refresh(double? temperature, double? rain = 1.0, int? cityId = null, double? wind = 5.0)
    {
        static string Json(double? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "null";
        var body = """{"daily":{"time":["2026-10-05"],"temperature_2m_mean":[""" + Json(temperature) +
                   """],"precipitation_sum":[""" + Json(rain) + """],"wind_speed_10m_max":[""" + Json(wind) + "]}}";
        return Service(body).RefreshCityAsync(cityId ?? _city.Id);
    }

    /// <summary>The service with Open-Meteo stubbed to answer every call with this body.</summary>
    private MonitoringService Service(string body)
    {
        var baselines = new BaselineRepository(_connection);
        var service = new MonitoringService(
            new CityRepository(_connection),
            baselines,
            new AnomalyEventRepository(_connection),
            new WeatherService(new HttpClient(new StubHandler(HttpStatusCode.OK, body)), new OpenMeteoOptions()),
            new AnomalyDetectionService(
                new Dictionary<MetricType, IAnomalyStrategy>
                {
                    [MetricType.Temperature] = new TemperatureStrategy(),
                    [MetricType.Precipitation] = new PrecipitationStrategy(),
                    [MetricType.WindSpeed] = new WindStrategy(),
                },
                new WeatherService(new HttpClient(), new OpenMeteoOptions()), baselines, new AnomalyEventRepository(_connection)),
            new AdvisoryService(new AdvisoryRepository(_connection)),
            _state);
        return service;
    }

    private List<AnomalyEvent> Events() => new AnomalyEventRepository(_connection).GetAll();

    [Fact]
    public async Task An_anomalous_reading_opens_an_event_with_snapshots_and_the_health_advisory()
    {
        var result = await Refresh(33.0);   // z = 3.33, Warning

        var temp = result.Metrics.Single(m => m.Metric == MetricType.Temperature);
        Assert.Equal(DataStatus.Ok, temp.DataStatus);
        Assert.Equal(3.33, temp.ZScore!.Value, 2);
        Assert.Equal(SeverityTier.Warning, temp.Tier);
        Assert.Equal(["Warning Climate", "Warning Health"], temp.Advisories.Select(a => a.Title).Order());

        var e = Assert.Single(Events());
        Assert.Equal(temp.ActiveEventId, e.Id);
        Assert.Equal((EventStatus.Active, SeverityTier.Warning, 28.0, 1.5), (e.Status, e.SeverityTier, e.BaselineMeanSnapshot, e.BaselineStdDevSnapshot));
        Assert.Equal(_healthAdvisoryId, e.AdvisoryId);
        Assert.Equal(result.RefreshedAt, e.StartedAt);
        Assert.Same(result, _state.GetLatest(_city.Id));
    }

    [Fact]
    public async Task The_event_stays_open_near_the_threshold_updates_its_peak_and_closes_below_1_75()
    {
        await Refresh(31.0);                        // z = 2.0, opens Watch
        var hovering = await Refresh(30.85);        // z = 1.9, stays open
        await Refresh(34.5);                        // z = 4.33, worse: peak and tier go up
        await Refresh(31.0);                        // z = 2.0, not worse: nothing changes
        var open = Assert.Single(Events());
        Assert.Equal((EventStatus.Active, SeverityTier.Extreme, 34.5), (open.Status, open.SeverityTier, open.PeakObservedValue));
        Assert.Equal(open.Id, hovering.Metrics[0].ActiveEventId);

        var calm = await Refresh(29.0);             // z = 0.67, closes

        var closed = Assert.Single(Events());
        Assert.Equal(EventStatus.Closed, closed.Status);
        Assert.Equal(calm.RefreshedAt, closed.EndedAt);
        Assert.Null(calm.Metrics[0].ActiveEventId);
        Assert.Empty(calm.Metrics[0].Advisories);
    }

    [Fact]
    public async Task A_sign_flip_closes_the_hot_event_and_opens_a_cold_one()
    {
        await Refresh(32.0);   // z = +2.67
        await Refresh(24.0);   // z = -2.67

        var events = Events().OrderBy(e => e.Id).ToList();
        Assert.Equal([EventStatus.Closed, EventStatus.Active], events.Select(e => e.Status));
        Assert.True(events[1].PeakZScore < 0);
    }

    [Fact]
    public async Task Readings_that_cannot_be_scored_get_a_status_and_no_tier()
    {
        var noData = await Refresh(null, rain: null, wind: null);
        Assert.Equal([DataStatus.NoData, DataStatus.NoData, DataStatus.NoData], noData.Metrics.Select(m => m.DataStatus));

        var noBaseline = await Refresh(28.0, rain: 5.0);
        var rain = noBaseline.Metrics.Single(m => m.Metric == MetricType.Precipitation);
        Assert.Equal((DataStatus.NoBaseline, (double?)null, (SeverityTier?)null), (rain.DataStatus, rain.ZScore, rain.Tier));

        var baselines = new BaselineRepository(_connection);
        var temp = baselines.GetByCityMetricMonth(_city.Id, MetricType.Temperature, 10)!;
        temp.StdDevValue = 0;
        baselines.Upsert(temp);
        var flat = (await Refresh(35.0)).Metrics.Single(m => m.Metric == MetricType.Temperature);
        Assert.Equal((DataStatus.ZeroVariance, (double?)null, (SeverityTier?)null), (flat.DataStatus, flat.ZScore, flat.Tier));
        Assert.Empty(Events());
    }

    [Fact]
    public async Task Wind_is_the_third_metric_and_opens_an_event_without_seeded_advisories()
    {
        new BaselineRepository(_connection).Add(new Baseline
        {
            CityId = _city.Id, MetricType = MetricType.WindSpeed, Month = 10, MeanValue = 10.0, StdDevValue = 2.0,
            SampleSize = 930, BaselineStartYear = 1995, BaselineEndYear = 2024
        });

        var result = await Refresh(28.0, wind: 16.0);   // wind z = 3.0, Warning

        Assert.Equal([MetricType.Temperature, MetricType.Precipitation, MetricType.WindSpeed], result.Metrics.Select(m => m.Metric));
        var wind = result.Metrics[2];
        Assert.Equal((DataStatus.Ok, 3.0, SeverityTier.Warning), (wind.DataStatus, wind.ZScore!.Value, wind.Tier!.Value));
        Assert.Empty(wind.Advisories);   // seed.sql has no wind advisories; that must not fail the refresh

        var e = Assert.Single(Events());
        Assert.Equal((MetricType.WindSpeed, (int?)null, wind.ActiveEventId), (e.MetricType, e.AdvisoryId, (int?)e.Id));
    }

    [Fact]
    public async Task GetRecentAsync_scores_each_day_against_its_month_and_changes_nothing()
    {
        // 30 September has no baseline; 1 and 2 October use the October temperature baseline (28, 1.5).
        const string body = """
            {"daily":{"time":["2026-09-30","2026-10-01","2026-10-02"],
                      "temperature_2m_mean":[30.0,33.0,null],"precipitation_sum":[1.0,2.0,3.0],"wind_speed_10m_max":[5.0,6.0,7.0]}}
            """;

        var recent = await Service(body).GetRecentAsync(_city.Id, days: 3);

        Assert.Equal(9, recent.Count);   // 3 days x 3 metrics, oldest first
        var temps = recent.Where(r => r.Metric == MetricType.Temperature).ToList();
        Assert.Equal(
            [new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)],
            temps.Select(t => t.ObservedAt!.Value));
        Assert.Equal([DataStatus.NoBaseline, DataStatus.Ok, DataStatus.NoData], temps.Select(t => t.DataStatus));
        Assert.Equal((3.33, SeverityTier.Warning), (Math.Round(temps[1].ZScore!.Value, 2), temps[1].Tier!.Value));
        Assert.All(recent, r => Assert.Null(r.ActiveEventId));
        Assert.All(recent, r => Assert.Empty(r.Advisories));

        Assert.Empty(Events());                     // read-only: a Warning day in the past opens no event
        Assert.Null(_state.GetLatest(_city.Id));    // and does not replace the dashboard reading
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public async Task GetRecentAsync_rejects_days_outside_1_to_31(int days)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service("{}").GetRecentAsync(_city.Id, days));
        Assert.Equal(["days"], ex.Errors.Keys);
    }

    [Fact]
    public async Task GetRecentAsync_throws_NotFound_for_an_unknown_city()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service("{}").GetRecentAsync(999));
    }

    [Fact]
    public async Task Unknown_city_and_a_running_refresh_throw()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Refresh(30.0, cityId: 999));

        using (_state.Begin(_city.Id, _city.Name))
            await Assert.ThrowsAsync<ConflictException>(() => Refresh(30.0));
    }
}
