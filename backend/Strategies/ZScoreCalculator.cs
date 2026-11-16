using ClimaPulse.Models;

namespace ClimaPulse.Strategies;

/// <summary>The z-score formula shared by the strategies: (observed - mean) / stdDev.</summary>
internal static class ZScoreCalculator
{
    /// <summary>
    /// How many standard deviations a reading is from the baseline mean. When the baseline
    /// has no spread (stdDev of 0 or less) no judgement is possible, so this returns 0
    /// instead of dividing by zero, which means "no anomaly" (SRD FR-10).
    /// </summary>
    public static double Compute(double observed, Baseline baseline)
    {
        if (baseline.StdDevValue <= 0)
            return 0;

        return (observed - baseline.MeanValue) / baseline.StdDevValue;
    }
}
