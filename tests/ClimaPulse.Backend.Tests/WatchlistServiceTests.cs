using ClimaPulse.Contracts;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>Validation runs before any database call, so these need no database.</summary>
public class WatchlistServiceValidationTests
{
    private const string Unused = "Host=localhost;Database=unused";

    private static readonly WatchlistService Service = new(
        new WatchlistRepository(Unused), new UserProfileRepository(Unused), new CityRepository(Unused));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public async Task Add_rejects_a_threshold_that_is_not_above_zero(double threshold)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service.AddAsync(1, new AddWatchlistRequest(1, threshold)));
        Assert.True(ex.Errors.ContainsKey("AlertThresholdZScore"));
    }

    [Fact]
    public async Task Update_rejects_notes_over_500_characters()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service.UpdateAsync(1, 1, new UpdateWatchlistRequest(2.0, new string('x', 501))));
        Assert.Equal(["Notes"], ex.Errors.Keys);
    }
}

/// <summary>IWatchlistService against climapulse_test.</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class WatchlistServiceDatabaseTests
{
    private readonly WatchlistService _service;
    private readonly int _profileId;
    private readonly int _cityId;

    public WatchlistServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        var profiles = new UserProfileRepository(db.ConnectionString);
        var cities = new CityRepository(db.ConnectionString);
        _service = new WatchlistService(new WatchlistRepository(db.ConnectionString), profiles, cities);

        var profile = new UserProfile { DisplayName = "Syauqi" };
        profiles.Add(profile);
        var city = new City
        {
            Name = "Yogyakarta", CountryCode = "ID", Latitude = -7.7956, Longitude = 110.3695, Timezone = "Asia/Jakarta"
        };
        cities.Add(city);
        _profileId = profile.Id;
        _cityId = city.Id;
    }

    [Fact]
    public async Task Add_then_List_returns_the_entry_with_its_city()
    {
        Assert.Empty(await _service.ListAsync(_profileId));

        var added = await _service.AddAsync(_profileId, new AddWatchlistRequest(_cityId, 2.5, "Home town"));

        var entry = Assert.Single(await _service.ListAsync(_profileId));
        Assert.Equal(added, entry);
        Assert.Equal("Yogyakarta", entry.City.Name);
        Assert.Equal(2.5, entry.AlertThresholdZScore);
        Assert.Equal("Home town", entry.Notes);
    }

    [Fact]
    public async Task Add_twice_throws_Conflict()
    {
        await _service.AddAsync(_profileId, new AddWatchlistRequest(_cityId));

        await Assert.ThrowsAsync<ConflictException>(() => _service.AddAsync(_profileId, new AddWatchlistRequest(_cityId)));
    }

    [Fact]
    public async Task Add_with_an_unknown_profile_or_city_throws_NotFound()
    {
        var noProfile = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.AddAsync(999, new AddWatchlistRequest(_cityId)));
        var noCity = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.AddAsync(_profileId, new AddWatchlistRequest(999)));

        Assert.Equal("Profile", noProfile.Entity);
        Assert.Equal("City", noCity.Entity);
    }

    [Fact]
    public async Task Update_replaces_threshold_and_null_notes_clear_the_note()
    {
        await _service.AddAsync(_profileId, new AddWatchlistRequest(_cityId, 2.0, "old note"));

        var updated = await _service.UpdateAsync(_profileId, _cityId, new UpdateWatchlistRequest(3.0, null));

        Assert.Equal(3.0, updated.AlertThresholdZScore);
        Assert.Null(updated.Notes);
        Assert.Null(Assert.Single(await _service.ListAsync(_profileId)).Notes);
    }

    [Fact]
    public async Task Remove_deletes_the_entry_and_a_second_remove_throws_NotFound()
    {
        await _service.AddAsync(_profileId, new AddWatchlistRequest(_cityId));

        await _service.RemoveAsync(_profileId, _cityId);

        Assert.Empty(await _service.ListAsync(_profileId));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveAsync(_profileId, _cityId));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateAsync(_profileId, _cityId, new UpdateWatchlistRequest(2.0, null)));
    }
}
