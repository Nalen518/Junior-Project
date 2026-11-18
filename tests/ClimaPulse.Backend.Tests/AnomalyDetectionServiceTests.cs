using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;
using ClimaPulse.Strategies;

namespace ClimaPulse.Backend.Tests;

/// <summary>Modul 9: one Evaluate call, a different strategy per metric (polymorphism).</summary>
public class AnomalyDetectionServiceTests
{
    // Repositories open a connection only when a query runs, so these tests need no database.
    private const string UnusedConnection = "Host=localhost;Database=unused";

    private static AnomalyDetectionService CreateService() => new(
        new Dictionary<MetricType, IAnomalyStrategy>
        {
            [MetricType.Temperature] = new TemperatureStrategy(),
            [MetricType.Precipitation] = new PrecipitationStrategy(),
        },
        new WeatherService(new HttpClient(), new OpenMeteoOptions()),
        new BaselineRepository(UnusedConnection),
        new AnomalyEventRepository(UnusedConnection));

    private static Baseline MakeBaseline(MetricType metric, double mean, double stdDev) =>
        new() { MetricType = metric, Month = 10, MeanValue = mean, StdDevValue = stdDev };

    [Fact]
    public void Temperature_reading_uses_TemperatureStrategy_and_gets_a_tier()
    {
        var result = CreateService().Evaluate(33.0, MakeBaseline(MetricType.Temperature, 28.0, 1.5));

        Assert.Equal("Temperature", result.AnomalyType);
        Assert.Equal(3.33, result.DeviationSigma, 2);   // SRD FR-10 example
        Assert.Equal(SeverityTier.Warning, result.SeverityTier);
    }

    [Fact]
    public void Precipitation_reading_uses_PrecipitationStrategy_which_ignores_dry_days()
    {
        var service = CreateService();

        var wet = service.Evaluate(50.0, MakeBaseline(MetricType.Precipitation, 10.0, 8.0));
        var dry = service.Evaluate(0.0, MakeBaseline(MetricType.Precipitation, 10.0, 8.0));

        Assert.Equal("Precipitation", wet.AnomalyType);
        Assert.Equal(SeverityTier.Extreme, wet.SeverityTier);   // z = 5.0
        Assert.Equal(0, dry.DeviationSigma);                     // below normal is not a rain anomaly
        Assert.Equal(SeverityTier.Normal, dry.SeverityTier);
    }

    [Fact]
    public void Zero_standard_deviation_gives_Normal_instead_of_dividing_by_zero()
    {
        var result = CreateService().Evaluate(40.0, MakeBaseline(MetricType.Temperature, 28.0, 0));

        Assert.Equal(0, result.DeviationSigma);
        Assert.Equal(SeverityTier.Normal, result.SeverityTier);
    }

    [Fact]
    public void Metric_without_a_strategy_is_rejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            CreateService().Evaluate(20.0, MakeBaseline(MetricType.WindSpeed, 10.0, 2.0)));
    }
}
