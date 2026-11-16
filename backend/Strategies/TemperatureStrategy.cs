using ClimaPulse.Enums;
using ClimaPulse.Models;

namespace ClimaPulse.Strategies;


public class TemperatureStrategy : IAnomalyStrategy
{
    public AnomalyResult Evaluate(double observed, Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.MetricType != MetricType.Temperature)
            throw new ArgumentException(
                "TemperatureStrategy needs a temperature baseline.", nameof(baseline));

        return new AnomalyResult
        {
            ObservedValue = observed,
            BaselineValue = baseline.MeanValue,
            DeviationSigma = ZScoreCalculator.Compute(observed, baseline),
            AnomalyType = nameof(MetricType.Temperature)
            // SeverityTier is left at Normal; AnomalyDetectionService.AssignTier sets it.
        };
    }
}
