using ClimaPulse.Enums;
using ClimaPulse.Models;

namespace ClimaPulse.Strategies;

public class PrecipitationStrategy : IAnomalyStrategy
{
    public AnomalyResult Evaluate(double observed, Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.MetricType != MetricType.Precipitation)
            throw new ArgumentException(
                "PrecipitationStrategy needs a precipitation baseline.", nameof(baseline));

        var zScore = ZScoreCalculator.Compute(observed, baseline);

        return new AnomalyResult
        {
            ObservedValue = observed,
            BaselineValue = baseline.MeanValue,
            DeviationSigma = Math.Max(0, zScore),
            AnomalyType = nameof(MetricType.Precipitation)
        };
    }
}
