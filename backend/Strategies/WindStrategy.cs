using ClimaPulse.Enums;
using ClimaPulse.Models;

namespace ClimaPulse.Strategies;

/// <summary>
/// Scores the daily maximum wind speed in km/h (Open-Meteo wind_speed_10m_max) against its
/// monthly baseline (SRD FR-14). Like temperature, both directions count: a very calm day
/// gives a negative z-score.
/// </summary>
public class WindStrategy : IAnomalyStrategy
{
    public AnomalyResult Evaluate(double observed, Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.MetricType != MetricType.WindSpeed)
            throw new ArgumentException(
                "WindStrategy needs a wind speed baseline.", nameof(baseline));

        return new AnomalyResult
        {
            ObservedValue = observed,
            BaselineValue = baseline.MeanValue,
            DeviationSigma = ZScoreCalculator.Compute(observed, baseline),
            AnomalyType = nameof(MetricType.WindSpeed)
            // SeverityTier is left at Normal; AnomalyDetectionService.AssignTier sets it.
        };
    }
}
