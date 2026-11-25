using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>IDashboardService against climapulse_test; refresh results are put in the in-memory state directly.</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class DashboardServiceDatabaseTests
{
    private readonly string _connection;
    private readonly CityRefreshState _state = new();
    private readonly int _profileId;
    private readonly Dictionary<string, int> _cityIds = new();

    public DashboardServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        _connection = db.ConnectionString;

        var profile = new UserProfile { DisplayName = "Tester" };
        new UserProfileRepository(_connection).Add(profile);
        _profileId = profile.Id;

        var cities = new CityRepository(_connection);
        var watchlist = new WatchlistRepository(_connection);
        foreach (var name in new[] { "Bandung", "Jakarta", "Surabaya", "Yogyakarta" })
        {
            var city = new City { Name = name, CountryCode = "ID", Latitude = -7, Longitude = 110, Timezone = "Asia/Jakarta" };
            cities.Add(city);
            watchlist.Add(new WatchlistEntry { ProfileId = _profileId, CityId = city.Id, AlertThresholdZScore = 2.5 });
            _cityIds[name] = city.Id;
        }
    }

    private DashboardService Service() => new(
        new UserProfileRepository(_connection), new WatchlistRepository(_connection), new CityRepository(_connection), _state);

    private void Refreshed(string city, params SeverityTier?[] tiers) => _state.SaveLatest(new RefreshResultDto(
        _cityIds[city], DateTime.UtcNow,
        tiers.Select(t => new MetricReadingDto(MetricType.Temperature, t is null ? DataStatus.NoBaseline : DataStatus.Ok,
            DateTime.UtcNow, 30, null, null, null, t, null, [])).ToList()));

    [Fact]
    public async Task Cities_are_sorted_worst_tier_first_then_by_name_and_not_refreshed_last()
    {
        Refreshed("Yogyakarta", SeverityTier.Watch, SeverityTier.Extreme);
        Refreshed("Surabaya", SeverityTier.Normal, null);
        Refreshed("Bandung", SeverityTier.Extreme);
        // Jakarta is watched but not refreshed since start-up.

        var dashboard = await Service().GetAsync(_profileId);

        Assert.Equal(["Bandung", "Yogyakarta", "Surabaya", "Jakarta"], dashboard.Cities.Select(c => c.City.Name));
        var jakarta = dashboard.Cities[^1];
        Assert.Null(jakarta.LastRefreshedAt);
        Assert.Empty(jakarta.Metrics);
        Assert.All(dashboard.Cities, c => Assert.Equal(2.5, c.AlertThresholdZScore));
        Assert.Equal(_profileId, dashboard.ProfileId);
    }

    [Fact]
    public async Task A_profile_without_cities_gets_an_empty_list_and_an_unknown_profile_throws()
    {
        var other = new UserProfile { DisplayName = "Empty" };
        new UserProfileRepository(_connection).Add(other);

        Assert.Empty((await Service().GetAsync(other.Id)).Cities);
        await Assert.ThrowsAsync<NotFoundException>(() => Service().GetAsync(999));
    }
}
