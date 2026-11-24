using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;

namespace ClimaPulse.Backend.Tests;

/// <summary>Runs the repository SQL against climapulse_test (UpdatePeak, List, Count, FindByRef, FindByPlace).</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class RepositoryDatabaseTests
{
    private readonly CityRepository _cities;
    private readonly BaselineRepository _baselines;
    private readonly AnomalyEventRepository _events;

    public RepositoryDatabaseTests(TestDatabase db)
    {
        db.Reset();
        _cities = new CityRepository(db.ConnectionString);
        _baselines = new BaselineRepository(db.ConnectionString);
        _events = new AnomalyEventRepository(db.ConnectionString);
    }

    private static DateTime Utc(int month, int day) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    private City AddCity(string name = "Yogyakarta", string? omRef = "1621177")
    {
        var city = new City
        {
            Name = name, CountryCode = "ID", Latitude = -7.7956, Longitude = 110.3695,
            Timezone = "Asia/Jakarta", OpenMeteoRef = omRef
        };
        _cities.Add(city);
        return city;
    }

    private AnomalyEvent AddEvent(int cityId, MetricType metric, SeverityTier tier, DateTime startedAt)
    {
        var baseline = _baselines.GetByCityMetricMonth(cityId, metric, 10);
        if (baseline is null)
        {
            baseline = new Baseline
            {
                CityId = cityId, MetricType = metric, Month = 10, MeanValue = 28.0, StdDevValue = 1.5,
                SampleSize = 930, BaselineStartYear = 1995, BaselineEndYear = 2024
            };
            _baselines.Add(baseline);
        }

        var e = new AnomalyEvent
        {
            CityId = cityId, BaselineId = baseline.Id, MetricType = metric, SeverityTier = tier,
            StartedAt = startedAt, PeakAt = startedAt, PeakObservedValue = 31.5, PeakZScore = 2.3,
            BaselineMeanSnapshot = 28.0, BaselineStdDevSnapshot = 1.5
        };
        _events.Add(e);
        return e;
    }

    // ---- UpdatePeak (FR-23) ----------------------------------------------

    [Fact]
    public void UpdatePeak_raises_the_tier_and_records_the_peak()
    {
        var city = AddCity();
        var e = AddEvent(city.Id, MetricType.Temperature, SeverityTier.Watch, Utc(10, 1));

        Assert.True(_events.UpdatePeak(e.Id, SeverityTier.Warning, Utc(10, 2), 33.0, 3.3));

        var stored = _events.GetOpenEvent(city.Id, MetricType.Temperature)!;
        Assert.Equal(SeverityTier.Warning, stored.SeverityTier);
        Assert.Equal(Utc(10, 2), stored.PeakAt);
        Assert.Equal(33.0, stored.PeakObservedValue);
        Assert.Equal(3.3, stored.PeakZScore);
    }

    [Fact]
    public void UpdatePeak_never_lowers_the_tier()
    {
        var city = AddCity();
        var e = AddEvent(city.Id, MetricType.Temperature, SeverityTier.Extreme, Utc(10, 1));

        Assert.True(_events.UpdatePeak(e.Id, SeverityTier.Watch, Utc(10, 2), 31.0, 2.1));

        Assert.Equal(SeverityTier.Extreme, _events.GetOpenEvent(city.Id, MetricType.Temperature)!.SeverityTier);
    }

    [Fact]
    public void UpdatePeak_returns_false_for_a_closed_or_unknown_event()
    {
        var city = AddCity();
        var e = AddEvent(city.Id, MetricType.Temperature, SeverityTier.Watch, Utc(10, 1));
        _events.Close(e.Id, Utc(10, 3));

        Assert.False(_events.UpdatePeak(e.Id, SeverityTier.Extreme, Utc(10, 4), 40.0, 8.0));
        Assert.False(_events.UpdatePeak(9999, SeverityTier.Extreme, Utc(10, 4), 40.0, 8.0));
    }

    // ---- List and Count (FR-26) --------------------------------------------

    [Fact]
    public void List_is_newest_first_and_paged_and_Count_ignores_paging()
    {
        var city = AddCity();
        for (var day = 1; day <= 5; day++)
            AddEvent(city.Id, MetricType.Temperature, SeverityTier.Watch, Utc(10, day));

        var page1 = _events.List(null, null, null, null, null, null, page: 1, pageSize: 2);
        var page3 = _events.List(null, null, null, null, null, null, page: 3, pageSize: 2);

        Assert.Equal(new[] { Utc(10, 5), Utc(10, 4) }, page1.Select(e => e.StartedAt));
        Assert.Equal(new[] { Utc(10, 1) }, page3.Select(e => e.StartedAt));
        Assert.Equal(5, _events.Count(null, null, null, null, null, null));
    }

    [Fact]
    public void List_filters_combine_and_tier_matches_exactly()
    {
        var yogya = AddCity();
        var jakarta = AddCity("Jakarta", "1642911");
        AddEvent(yogya.Id, MetricType.Temperature, SeverityTier.Warning, Utc(10, 1));
        AddEvent(yogya.Id, MetricType.Temperature, SeverityTier.Extreme, Utc(10, 2));
        AddEvent(yogya.Id, MetricType.Precipitation, SeverityTier.Warning, Utc(10, 3));
        AddEvent(jakarta.Id, MetricType.Temperature, SeverityTier.Warning, Utc(10, 4));

        var result = _events.List(yogya.Id, MetricType.Temperature, SeverityTier.Warning, null, null, null, 1, 20);

        var only = Assert.Single(result);
        Assert.Equal(Utc(10, 1), only.StartedAt);
        Assert.Equal(1, _events.Count(yogya.Id, MetricType.Temperature, SeverityTier.Warning, null, null, null));
    }

    [Fact]
    public void List_filters_by_status_and_inclusive_date_range()
    {
        var city = AddCity();
        var closed = AddEvent(city.Id, MetricType.Temperature, SeverityTier.Watch, Utc(10, 1));
        _events.Close(closed.Id, Utc(10, 2));
        AddEvent(city.Id, MetricType.Temperature, SeverityTier.Watch, Utc(10, 5));
        AddEvent(city.Id, MetricType.Precipitation, SeverityTier.Watch, Utc(10, 9));

        Assert.Equal(1, _events.Count(null, null, null, EventStatus.Closed, null, null));
        Assert.Equal(2, _events.Count(null, null, null, EventStatus.Active, null, null));

        var range = _events.List(null, null, null, null, Utc(10, 1), Utc(10, 5), 1, 20);
        Assert.Equal(new[] { Utc(10, 5), Utc(10, 1) }, range.Select(e => e.StartedAt));
    }

    // ---- FindByRef and FindByPlace (D7) ------------------------------------

    [Fact]
    public void FindByRef_finds_the_stored_city()
    {
        var city = AddCity();

        Assert.Equal(city.Id, _cities.FindByRef("1621177")?.Id);
        Assert.Null(_cities.FindByRef("0"));
    }

    [Fact]
    public void FindByPlace_matches_name_and_coordinates_rounded_to_two_decimals()
    {
        var city = AddCity(omRef: null);

        Assert.Equal(city.Id, _cities.FindByPlace("Yogyakarta", -7.7951, 110.3702)?.Id);   // both round to -7.80, 110.37
        Assert.Null(_cities.FindByPlace("Yogyakarta", -7.7800, 110.3695));                // -7.78 is another place
        Assert.Null(_cities.FindByPlace("Jakarta", -7.7956, 110.3695));
    }
}
