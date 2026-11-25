using System.Globalization;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;

namespace ClimaPulse.Services;

/// <summary>
/// Monthly baselines per city and metric from 30 years of daily values (SRD FR-07 to FR-09).
/// The archive is downloaded only when the city has no stored history; later refreshes
/// recompute from the database with no Open-Meteo call (SRD OPEN-10).
/// </summary>
public class BaselineService : IBaselineService
{
    public const int StartYear = 1995;
    public const int EndYear = 2024;

    /// <summary>Every metric that is scored, in the order the UI shows them.</summary>
    public static readonly MetricType[] ScoredMetrics = [MetricType.Temperature, MetricType.Precipitation, MetricType.WindSpeed];

    private static readonly DateTime From = ParseDate(WeatherService.ArchiveStartDate);
    private static readonly DateTime To = ParseDate(WeatherService.ArchiveEndDate);

    private readonly CityRepository _cities;
    private readonly HistoricalObservationRepository _history;
    private readonly BaselineRepository _baselines;
    private readonly WeatherService _weather;
    private readonly CityRefreshState _state;

    public BaselineService(
        CityRepository cities,
        HistoricalObservationRepository history,
        BaselineRepository baselines,
        WeatherService weather,
        CityRefreshState state)
    {
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(weather);
        ArgumentNullException.ThrowIfNull(state);
        _cities = cities;
        _history = history;
        _baselines = baselines;
        _weather = weather;
        _state = state;
    }

    /// <summary>Stored baselines of a city, by metric then month.</summary>
    public Task<IReadOnlyList<BaselineDto>> ListAsync(int cityId, MetricType? metric = null, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<BaselineDto>>(() =>
        {
            if (_cities.GetById(cityId) is null)
                throw new NotFoundException("City", cityId);

            // The Baseline table is small (cities x 2 metrics x 12 months), so filtering in memory is fine.
            return _baselines.Query(b => b.CityId == cityId && (metric is null || b.MetricType == metric))
                .OrderBy(b => b.MetricType).ThenBy(b => b.Month)
                .Select(b => b.ToDto()).ToList();
        }, ct);

    /// <summary>Progress: 10 when the city is loaded, 60 after the history is read or downloaded, 100 when stored.</summary>
    public async Task<IReadOnlyList<BaselineDto>> RefreshAsync(int cityId, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var city = await Task.Run(() => _cities.GetById(cityId), ct) ?? throw new NotFoundException("City", cityId);
        using var _ = _state.Begin(city.Id, city.Name);
        progress?.Report(10);

        var history = await Task.Run(ReadHistory, ct);
        if (ScoredMetrics.Any(m => history[m].Count == 0))
        {
            var downloaded = await _weather.FetchArchiveAsync(city, ct);
            await Task.Run(() => _history.AddRange(downloaded), ct);
            history = await Task.Run(ReadHistory, ct);
        }
        progress?.Report(60);

        var result = await Task.Run(() =>
        {
            var computed = ScoredMetrics.SelectMany(m => ComputeMonthly(city.Id, m, history[m])).ToList();
            foreach (var baseline in computed)
                _baselines.Upsert(baseline);
            return computed.Select(b => b.ToDto()).ToList();
        }, ct);
        progress?.Report(100);
        return result;

        Dictionary<MetricType, List<HistoricalObservation>> ReadHistory() =>
            ScoredMetrics.ToDictionary(m => m, m => _history.GetRange(city.Id, m, From, To));
    }

    /// <summary>
    /// Mean, sample standard deviation (n - 1) and count per calendar month (D5).
    /// A month with fewer than 2 values is skipped: n - 1 would be 0.
    /// </summary>
    public static List<Baseline> ComputeMonthly(int cityId, MetricType metric, IEnumerable<HistoricalObservation> rows) =>
        rows.GroupBy(r => r.ObservedAt.Month)
            .Where(g => g.Count() >= 2)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var values = g.Select(r => r.Value).ToList();
                var mean = values.Average();
                var variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
                return new Baseline
                {
                    CityId = cityId,
                    MetricType = metric,
                    Month = g.Key,
                    MeanValue = mean,
                    StdDevValue = Math.Sqrt(variance),
                    SampleSize = values.Count,
                    BaselineStartYear = StartYear,
                    BaselineEndYear = EndYear,
                    ComputedAt = DtoMapping.UtcNowForDb(),
                };
            })
            .ToList();

    private static DateTime ParseDate(string text) =>
        DateTime.SpecifyKind(DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc);
}
