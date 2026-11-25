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

        var metrics = await Task.Run(() => new List<MetricReadingDto>
        {
            Read(city.Id, MetricType.Temperature, today.Temperature, today.LocalDate.Month, now),
            Read(city.Id, MetricType.Precipitation, today.Precipitation, today.LocalDate.Month, now),
        }, ct);

        var result = new RefreshResultDto(city.Id, now, metrics);
        _state.SaveLatest(result);
        return result;
    }

    private MetricReadingDto Read(int cityId, MetricType metric, double? value, int month, DateTime now)
    {
        if (value is not { } observed)
            return NotScored(metric, DataStatus.NoData, now, null, null);

        var baseline = _baselines.GetByCityMetricMonth(cityId, metric, month);
        if (baseline is null)
            return NotScored(metric, DataStatus.NoBaseline, now, observed, null);
        // Checked here, before the strategy, so the Modul 9 strategies stay unchanged (D6).
        if (baseline.StdDevValue <= 0)
            return NotScored(metric, DataStatus.ZeroVariance, now, observed, baseline);

        var result = _detection.Evaluate(observed, baseline);
        var z = result.DeviationSigma;
        var tier = result.SeverityTier;
        List<Advisory> advisories = tier == SeverityTier.Normal ? [] : _advisories.GetLatest(metric, tier, AdvisoryLocale);

        var activeEventId = UpdateLedger(cityId, metric, observed, z, tier, baseline, advisories, now);

        return new MetricReadingDto(metric, DataStatus.Ok, now, observed, baseline.MeanValue, baseline.StdDevValue,
            z, tier, activeEventId, advisories.Select(a => a.ToDto()).ToList());
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
