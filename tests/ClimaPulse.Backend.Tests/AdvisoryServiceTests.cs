using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

public class AdvisoryServiceValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetLatestAsync_rejects_a_blank_locale(string locale)
    {
        var service = new AdvisoryService(new AdvisoryRepository("Host=localhost;Database=unused"));

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.GetLatestAsync(MetricType.Temperature, SeverityTier.Watch, locale));
        Assert.Equal(["Locale"], ex.Errors.Keys);
    }
}

/// <summary>IAdvisoryService against climapulse_test.</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class AdvisoryServiceDatabaseTests
{
    private readonly AdvisoryService _service;

    public AdvisoryServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        var repo = new AdvisoryRepository(db.ConnectionString);
        _service = new AdvisoryService(repo);

        void Add(AdvisoryCategory category, int version, string title) => repo.Add(new Advisory
        {
            MetricType = MetricType.Temperature, SeverityTier = SeverityTier.Warning, Locale = "en-US",
            Category = category, Version = version, Title = title, Body = "Body"
        });

        Add(AdvisoryCategory.Health, 1, "Health v1");
        Add(AdvisoryCategory.Health, 2, "Health v2");
        Add(AdvisoryCategory.Climate, 1, "Climate v1");
    }

    [Fact]
    public async Task Returns_the_latest_version_per_category()
    {
        var result = await _service.GetLatestAsync(MetricType.Temperature, SeverityTier.Warning, "en-US");

        Assert.Equal(["Climate v1", "Health v2"], result.Select(a => a.Title).Order());
        Assert.All(result, a => Assert.Equal(SeverityTier.Warning, a.Tier));
    }

    [Fact]
    public async Task Falls_back_to_en_US_when_the_locale_has_none()
    {
        var result = await _service.GetLatestAsync(MetricType.Temperature, SeverityTier.Warning, "id-ID");

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal("en-US", a.Locale));
    }

    [Fact]
    public async Task Normal_and_tiers_without_advisories_return_an_empty_list()
    {
        Assert.Empty(await _service.GetLatestAsync(MetricType.Temperature, SeverityTier.Normal, "en-US"));
        Assert.Empty(await _service.GetLatestAsync(MetricType.Precipitation, SeverityTier.Extreme, "en-US"));
    }
}
