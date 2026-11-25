using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Strategies;

namespace ClimaPulse.Services;

/// <summary>
/// Orchestrates weather data fetching, strategy-based evaluation,
/// severity tier assignment, and anomaly log persistence.
/// </summary>
public class AnomalyDetectionService
{
    // Tier thresholds on the absolute z-score. Kept in one place (SRD FR-11, OPEN-1).
    public const double WatchThreshold = 2.0;
    public const double WarningThreshold = 3.0;
    public const double ExtremeThreshold = 4.0;

    /// <summary>An open event closes below this |z| (2.0 - 0.25 hysteresis margin).</summary>
    public const double CloseThreshold = 1.75;

    // One strategy per metric. Callers only see IAnomalyStrategy (polymorphism).
    private readonly IReadOnlyDictionary<MetricType, IAnomalyStrategy> _strategies;
    private readonly WeatherService _weatherService;
    private readonly BaselineRepository _baselineRepo;
    private readonly AnomalyEventRepository _eventRepo;

    public AnomalyDetectionService(
        IReadOnlyDictionary<MetricType, IAnomalyStrategy> strategies,
        WeatherService weatherService,
        BaselineRepository baselineRepo,
        AnomalyEventRepository eventRepo)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies;
        _weatherService = weatherService;
        _baselineRepo = baselineRepo;
        _eventRepo = eventRepo;
    }

    /// <summary>
    /// Judges one reading against its monthly baseline (SRD FR-10, FR-11): picks the strategy
    /// for the baseline's metric, lets that strategy compute the z-score, then sets the tier.
    /// The same call works for temperature and rain because each strategy overrides Evaluate.
    /// </summary>
    public AnomalyResult Evaluate(double observed, Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (!_strategies.TryGetValue(baseline.MetricType, out var strategy))
            throw new NotSupportedException($"No anomaly strategy for {baseline.MetricType} yet.");

        var result = strategy.Evaluate(observed, baseline);
        result.SeverityTier = AssignTier(result.DeviationSigma);
        return result;
    }

    /// <summary>
    /// Hysteresis for the event ledger (SRD FR-22 to FR-25, OPEN-1): an event opens at |z| >= 2.0
    /// and stays open until |z| drops below 1.75, so a reading hovering near 2.0 makes one event,
    /// not many. A sign flip (hot to cold) closes the event; the caller then decides again with
    /// no open event, which opens a new one if the reading is still anomalous.
    /// </summary>
    public static EventAction DecideEvent(AnomalyEvent? openEvent, double zScore)
    {
        var magnitude = Math.Abs(zScore);
        if (openEvent is null)
            return magnitude >= WatchThreshold ? EventAction.Open : EventAction.Nothing;

        if (magnitude < CloseThreshold || Math.Sign(zScore) != Math.Sign(openEvent.PeakZScore))
            return EventAction.Close;

        return magnitude > Math.Abs(openEvent.PeakZScore) ? EventAction.UpdatePeak : EventAction.Nothing;
    }

    /// <summary>
    /// Maps a z-score to a severity tier using its absolute value, so heat and cold
    /// are judged by the same thresholds. Below the Watch threshold the tier is Normal.
    /// </summary>
    public SeverityTier AssignTier(double zScore)
    {
        var magnitude = Math.Abs(zScore);

        if (magnitude >= ExtremeThreshold) return SeverityTier.Extreme;
        if (magnitude >= WarningThreshold) return SeverityTier.Warning;
        if (magnitude >= WatchThreshold) return SeverityTier.Watch;
        return SeverityTier.Normal;
    }
}

/// <summary>What a new reading does to the open event of its city and metric.</summary>
public enum EventAction
{
    Nothing,
    Open,
    UpdatePeak,
    Close
}
