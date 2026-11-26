using System.Globalization;
using System.Text;
using System.Text.Json;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>Validation runs before any database call, so these need no database.</summary>
public class EventLedgerServiceValidationTests
{
    private const string Unused = "Host=localhost;Database=unused";
    private static readonly EventLedgerService Service = new(new AnomalyEventRepository(Unused), new CityRepository(Unused));

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task List_rejects_bad_paging(int page, int pageSize, string field)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.ListAsync(new EventQuery(), page, pageSize));
        Assert.Equal([field], ex.Errors.Keys);
    }

    [Fact]
    public async Task List_rejects_From_after_To_and_a_Normal_tier()
    {
        var query = new EventQuery(Tier: SeverityTier.Normal, From: new DateTime(2026, 10, 2), To: new DateTime(2026, 10, 1));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.ListAsync(query));

        Assert.Equal(["From", "Tier"], ex.Errors.Keys.Order());
    }

    [Fact]
    public async Task Export_rejects_an_unknown_format()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service.ExportAsync(new EventQuery(), (ExportFormat)9, new MemoryStream()));
        Assert.Equal(["Format"], ex.Errors.Keys);
    }
}

/// <summary>IEventLedgerService against climapulse_test.</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class EventLedgerServiceDatabaseTests
{
    private readonly EventLedgerService _service;
    private readonly AnomalyEventRepository _events;
    private readonly int _cityId;
    private readonly int _baselineId;
    private readonly int _advisoryId;

    public EventLedgerServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        var cities = new CityRepository(db.ConnectionString);
        var baselines = new BaselineRepository(db.ConnectionString);
        var advisories = new AdvisoryRepository(db.ConnectionString);
        _events = new AnomalyEventRepository(db.ConnectionString);
        _service = new EventLedgerService(_events, cities, advisories);

        // A comma in the title checks CSV quoting of the new column.
        var advisory = new Advisory
        {
            MetricType = MetricType.Temperature, SeverityTier = SeverityTier.Warning, Category = AdvisoryCategory.Health,
            Title = "Heat, stay inside", Body = "Body"
        };
        advisories.Add(advisory);
        _advisoryId = advisory.Id;

        // A comma and a quote in the name check CSV quoting.
        var city = new City
        {
            Name = "Kota \"Gudeg\", Yogyakarta", CountryCode = "ID", Latitude = -7.7956, Longitude = 110.3695, Timezone = "Asia/Jakarta"
        };
        cities.Add(city);
        var baseline = new Baseline
        {
            CityId = city.Id, MetricType = MetricType.Temperature, Month = 10, MeanValue = 28.0, StdDevValue = 1.5,
            SampleSize = 930, BaselineStartYear = 1995, BaselineEndYear = 2024
        };
        baselines.Add(baseline);
        _cityId = city.Id;
        _baselineId = baseline.Id;
    }

    private static DateTime Utc(int day) => new(2026, 10, day, 5, 0, 0, DateTimeKind.Utc);

    private AnomalyEvent AddEvent(SeverityTier tier, int day, bool withAdvisory = false)
    {
        var e = new AnomalyEvent
        {
            CityId = _cityId, BaselineId = _baselineId, AdvisoryId = withAdvisory ? _advisoryId : null,
            MetricType = MetricType.Temperature, SeverityTier = tier,
            StartedAt = Utc(day), PeakAt = Utc(day), PeakObservedValue = 33.25, PeakZScore = 3.5,
            BaselineMeanSnapshot = 28.0, BaselineStdDevSnapshot = 1.5, CreatedAt = Utc(day)
        };
        _events.Add(e);
        return e;
    }

    [Fact]
    public async Task List_pages_newest_first_with_city_name_and_total()
    {
        for (var day = 1; day <= 3; day++)
            AddEvent(SeverityTier.Watch, day);

        var page = await _service.ListAsync(new EventQuery(), page: 1, pageSize: 2);

        Assert.Equal(3, page.TotalItems);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal([Utc(3), Utc(2)], page.Items.Select(e => e.StartedAt));
        Assert.All(page.Items, e => Assert.Equal("Kota \"Gudeg\", Yogyakarta", e.CityName));
    }

    [Fact]
    public async Task List_is_empty_not_null_when_nothing_matches()
    {
        var page = await _service.ListAsync(new EventQuery(Tier: SeverityTier.Extreme));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task Get_returns_the_event_and_throws_NotFound_for_an_unknown_id()
    {
        var e = AddEvent(SeverityTier.Warning, 1);

        Assert.Equal(SeverityTier.Warning, (await _service.GetAsync(e.Id)).Tier);
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(999));
    }

    [Fact]
    public async Task The_advisory_title_is_filled_and_null_when_the_event_has_no_advisory()
    {
        var withAdvisory = AddEvent(SeverityTier.Warning, 1, withAdvisory: true);
        var without = AddEvent(SeverityTier.Watch, 2);

        var listed = (await _service.ListAsync(new EventQuery())).Items.ToDictionary(e => e.Id);

        Assert.Equal((_advisoryId, "Heat, stay inside"), (listed[withAdvisory.Id].AdvisoryId, listed[withAdvisory.Id].AdvisoryTitle));
        Assert.Equal(((int?)null, (string?)null), (listed[without.Id].AdvisoryId, listed[without.Id].AdvisoryTitle));
        Assert.Equal("Heat, stay inside", (await _service.GetAsync(withAdvisory.Id)).AdvisoryTitle);
    }

    [Fact]
    public async Task Csv_export_uses_the_contract_format_even_on_Indonesian_Windows()
    {
        var e = AddEvent(SeverityTier.Warning, 1, withAdvisory: true);
        _events.Close(e.Id, Utc(2));
        var noAdvisory = AddEvent(SeverityTier.Watch, 3);
        _events.Close(noAdvisory.Id, Utc(4));

        var csv = await ExportOnIndonesianWindows(new EventQuery(Status: EventStatus.Closed), ExportFormat.Csv);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);   // header + the two closed events, newest first
        Assert.Equal("id,cityId,cityName,baselineId,advisoryId,metric,tier,startedAt,endedAt,peakAt,peakObservedValue,peakZScore,baselineMeanSnapshot,baselineStdDevSnapshot,status,createdAt,advisoryTitle", lines[0]);
        Assert.Equal(
            $"{e.Id},{_cityId},\"Kota \"\"Gudeg\"\", Yogyakarta\",{_baselineId},{_advisoryId},temperature_2m,warning," +
            "2026-10-01T05:00:00Z,2026-10-02T05:00:00Z,2026-10-01T05:00:00Z,33.25,3.5,28,1.5,closed,2026-10-01T05:00:00Z," +
            "\"Heat, stay inside\"",
            lines[2]);
        Assert.EndsWith(",closed,2026-10-03T05:00:00Z,", lines[1]);   // no advisory: empty id and title
    }

    [Fact]
    public async Task Json_export_has_camelCase_names_text_enums_and_dot_decimals()
    {
        AddEvent(SeverityTier.Extreme, 1, withAdvisory: true);

        var json = await ExportOnIndonesianWindows(new EventQuery(), ExportFormat.Json);

        var item = Assert.Single(JsonDocument.Parse(json).RootElement.EnumerateArray());
        Assert.Equal("Heat, stay inside", item.GetProperty("advisoryTitle").GetString());
        Assert.Equal("extreme", item.GetProperty("tier").GetString());
        Assert.Equal("temperature_2m", item.GetProperty("metric").GetString());
        Assert.Equal("active", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("endedAt").ValueKind);
        Assert.Equal(33.25, item.GetProperty("peakObservedValue").GetDouble());
        Assert.Contains("33.25", json);
    }

    private async Task<string> ExportOnIndonesianWindows(EventQuery query, ExportFormat format)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("id-ID");
        try
        {
            using var stream = new MemoryStream();
            await _service.ExportAsync(query, format, stream);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
