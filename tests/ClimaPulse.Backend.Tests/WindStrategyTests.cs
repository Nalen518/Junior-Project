using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;
using ClimaPulse.Strategies;

namespace ClimaPulse.Backend.Tests;

/// <summary>WindStrategy through the same Evaluate call as the other metrics (FR-14). No database.</summary>
public class WindStrategyTests
{
    private const string UnusedConnection = "Host=localhost;Database=unused";

    private static AnomalyDetectionService CreateService() => new(
        new Dictionary<MetricType, IAnomalyStrategy>
        {
            [MetricType.Temperature] = new TemperatureStrategy(),
            [MetricType.Precipitation] = new PrecipitationStrategy(),
            [MetricType.WindSpeed] = new WindStrategy(),
        },
        new WeatherService(new HttpClient(), new OpenMeteoOptions()),
        new BaselineRepository(UnusedConnection),
        new AnomalyEventRepository(UnusedConnection));

    private static Baseline WindBaseline() =>
        new() { MetricType = MetricType.WindSpeed, Month = 1, MeanValue = 12.0, StdDevValue = 4.0 };

    [Theory]
    [InlineData(30.0, 4.5, SeverityTier.Extreme)]   // stormy day
    [InlineData(22.0, 2.5, SeverityTier.Watch)]
    [InlineData(2.0, -2.5, SeverityTier.Watch)]      // unusually calm counts too
    [InlineData(14.0, 0.5, SeverityTier.Normal)]
    public void Wind_reading_uses_WindStrategy_and_gets_z_and_tier(double observed, double z, SeverityTier tier)
    {
        var result = CreateService().Evaluate(observed, WindBaseline());

        Assert.Equal("WindSpeed", result.AnomalyType);
        Assert.Equal(z, result.DeviationSigma, 10);
        Assert.Equal(tier, result.SeverityTier);
        Assert.Equal(12.0, result.BaselineValue);
    }

    [Fact]
    public void WindStrategy_rejects_a_temperature_baseline()
    {
        var temperature = new Baseline { MetricType = MetricType.Temperature, MeanValue = 28.0, StdDevValue = 1.5 };

        Assert.Throws<ArgumentException>(() => new WindStrategy().Evaluate(30.0, temperature));
    }
}
