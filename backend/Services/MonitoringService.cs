using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;

namespace ClimaPulse.Services;

/// <summary>
/// Refreshes one city: today's reading, z-score and tier per metric, then opens, updates or
/// closes ledger events with hysteresis (SRD FR-06, FR-10 to FR-12, FR-22 to FR-25).
/// The result is kept in memory for the dashboard (SRD OPEN-10).
/// </summary>
public class MonitoringService : IMonitoringService
{
    private const string AdvisoryLocale = "en-US";   // a refresh has no profile, and v1 is English only

    private readonly CityRepository _cities;
    private readonly BaselineRepository _baselines;
    private readonly AnomalyEventRepository _events;
    private readonly WeatherService _weather;
    private readonly AnomalyDetectionService _detection;
    private readonly AdvisoryService _advisories;
    private readonly CityRefreshState _state;

    public MonitoringService(
        CityRepository cities,
        BaselineRepository baselines,
        AnomalyEventRepository events,
        WeatherService weather,
        AnomalyDetectionService detection,
        AdvisoryService advisories,
        CityRefreshState state)
    {
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(weather);
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(advisories);
        ArgumentNullException.ThrowIfNull(state);
        _cities = cities;
        _baselines = baselines;
        _events = events;
        _weather = weather;
        _detection = detection;
        _advisories = advisories;
        _state = state;
    }

    /// <summary>A database error fails the whole refresh, so a fault is never shown as "No data".</summary>
    public async Task<RefreshResultDto> RefreshCityAsync(int cityId, CancellationToken ct = default)
    {
        var city = await Task.Run(() => _cities.GetById(cityId), ct) ?? throw new NotFoundException("City", cityId);
        using var _ = _state.Begin(city.Id, city.Name);

        var today = await _weather.FetchTodayAsync(city, ct);
        // Events and readings use the refresh time, not the daily date label (SRD OPEN-10).
        var now = DtoMapping.UtcNowForDb();

        var metrics = await Task.Run(
            () => Metrics.Select(m => Read(city.Id, m, ValueOf(today, m), today.LocalDate.Month, now)).ToList(), ct);

        var result = new RefreshResultDto(city.Id, now, metrics);
        _state.SaveLatest(result);
        return result;
    }

    /// <summary>
    /// The last days up to today for the S4 chart, scored like a refresh but read-only: no events,
    /// no advisories, nothing kept in memory (FR-20). ObservedAt is each day's local date at 00:00Z.
    /// </summary>
    public async Task<IReadOnlyList<MetricReadingDto>> GetRecentAsync(int cityId, int days = 14, CancellationToken ct = default)
    {
        if (days is < 1 or > MaxRecentDays)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["days"] = [$"Days must be 1 to {MaxRecentDays}."]
            });

        var city = await Task.Run(() => _cities.GetById(cityId), ct) ?? throw new NotFoundException("City", cityId);
        var recent = await _weather.FetchRecentAsync(city, days, ct);

        return await Task.Run<IReadOnlyList<MetricReadingDto>>(() =>
        {
            // One baseline lookup per metric and month, not one per day.
            var baselines = new Dictionary<(MetricType, int), Baseline?>();
            Baseline? BaselineFor(MetricType metric, int month) =>
                baselines.TryGetValue((metric, month), out var b)
                    ? b
                    : baselines[(metric, month)] = _baselines.GetByCityMetricMonth(city.Id, metric, month);

            return recent.SelectMany(day =>
                {
                    var at = new DateTime(day.LocalDate, TimeOnly.MinValue, DateTimeKind.Utc);
                    return Metrics.Select(m =>
                    {
                        var value = ValueOf(day, m);
                        return Score(m, value, value is null ? null : BaselineFor(m, day.LocalDate.Month), at);
                    });
                })
                .ToList();
        }, ct);
    }

    private const int MaxRecentDays = 31;

    /// <summary>The scored metrics, in the order every reading list uses.</summary>
    private static readonly MetricType[] Metrics = [MetricType.Temperature, MetricType.Precipitation, MetricType.WindSpeed];

    private static double? ValueOf(TodayReading day, MetricType metric) => metric switch
    {
        MetricType.Temperature => day.Temperature,
        MetricType.Precipitation => day.Precipitation,
        _ => day.WindSpeed,
    };

    private MetricReadingDto Read(int cityId, MetricType metric, double? value, int month, DateTime now)
    {
        var baseline = value is null ? null : _baselines.GetByCityMetricMonth(cityId, metric, month);
        var reading = Score(metric, value, baseline, now);
        if (reading.DataStatus != DataStatus.Ok)
            return reading;

        var tier = reading.Tier!.Value;
        List<Advisory> advisories = tier == SeverityTier.Normal ? [] : _advisories.GetLatest(metric, tier, AdvisoryLocale);
        var activeEventId = UpdateLedger(cityId, metric, reading.ObservedValue!.Value, reading.ZScore!.Value, tier,
            baseline!, advisories, now);

        return reading with { ActiveEventId = activeEventId, Advisories = advisories.Select(a => a.ToDto()).ToList() };
    }

    /// <summary>Status, z-score and tier of one value against its baseline. No ledger and no advisories.</summary>
    private MetricReadingDto Score(MetricType metric, double? value, Baseline? baseline, DateTime at)
    {
        if (value is not { } observed)
            return NotScored(metric, DataStatus.NoData, at, null, null);
        if (baseline is null)
            return NotScored(metric, DataStatus.NoBaseline, at, observed, null);
        // Checked here, before the strategy, so the Modul 9 strategies stay unchanged (D6).
        if (baseline.StdDevValue <= 0)
            return NotScored(metric, DataStatus.ZeroVariance, at, observed, baseline);

        var result = _detection.Evaluate(observed, baseline);
        return new MetricReadingDto(metric, DataStatus.Ok, at, observed, baseline.MeanValue, baseline.StdDevValue,
            result.DeviationSigma, result.SeverityTier, null, []);
    }

    /// <summary>Applies the hysteresis decision and returns the id of the event still open afterwards.</summary>
    private int? UpdateLedger(int cityId, MetricType metric, double observed, double z, SeverityTier tier,
        Baseline baseline, List<Advisory> advisories, DateTime now)
    {
        var open = _events.GetOpenEvent(cityId, metric);

        switch (AnomalyDetectionService.DecideEvent(open, z))
        {
            case EventAction.UpdatePeak:
                _events.UpdatePeak(open!.Id, tier, now, observed, z);
                return open.Id;
            case EventAction.Nothing:
                return open?.Id;
            case EventAction.Close:
                _events.Close(open!.Id, now);
                // A sign flip can close and reopen in the same refresh.
                if (AnomalyDetectionService.DecideEvent(null, z) != EventAction.Open)
                    return null;
                break;
        }

        var created = new AnomalyEvent
        {
            CityId = cityId,
            BaselineId = baseline.Id,
            AdvisoryId = advisories.FirstOrDefault(a => a.Category == AdvisoryCategory.Health)?.Id,   // D4
            MetricType = metric,
            SeverityTier = tier,
            StartedAt = now,
            PeakAt = now,
            PeakObservedValue = observed,
            PeakZScore = z,
            BaselineMeanSnapshot = baseline.MeanValue,
            BaselineStdDevSnapshot = baseline.StdDevValue,
            Status = EventStatus.Active,
            CreatedAt = now,
        };
        _events.Add(created);
        return created.Id;
    }

    private static MetricReadingDto NotScored(MetricType metric, DataStatus status, DateTime now, double? observed, Baseline? baseline) =>
        new(metric, status, now, observed, baseline?.MeanValue, baseline?.StdDevValue, null, null, null, []);
}
