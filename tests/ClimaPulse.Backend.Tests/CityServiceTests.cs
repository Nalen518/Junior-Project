using ClimaPulse.Contracts;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>Validation runs before any database or network call.</summary>
public class CityServiceValidationTests
{
    private static readonly CityService Service = new(
        new CityRepository("Host=localhost;Database=unused"),
        new WeatherService(new HttpClient(), new OpenMeteoOptions()));

    private static readonly CitySearchResult Yogyakarta =
        new("Yogyakarta", "ID", "Yogyakarta", -7.7956, 110.3695, "Asia/Jakarta", 113.0, "1621177");

    [Theory]
    [InlineData("")]
    [InlineData(" Y ")]
    public async Task Search_needs_at_least_two_characters(string query)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.SearchAsync(query));
        Assert.Equal(["Query"], ex.Errors.Keys);
    }

    [Fact]
    public async Task Add_rejects_values_the_City_table_would_refuse()
    {
        var bad = Yogyakarta with { CountryCode = "IDN", Latitude = 95, Timezone = "" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.AddAsync(bad));

        Assert.Equal(["CountryCode", "Latitude", "Timezone"], ex.Errors.Keys.Order());
    }
}

/// <summary>ICityService storage against climapulse_test (search needs the network, so it is not here).</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class CityServiceDatabaseTests
{
    private static readonly CitySearchResult Yogyakarta =
        new("Yogyakarta", "ID", "Yogyakarta", -7.7956, 110.3695, "Asia/Jakarta", 113.0, "1621177");

    private readonly CityService _service;

    public CityServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        _service = new CityService(
            new CityRepository(db.ConnectionString),
            new WeatherService(new HttpClient(), new OpenMeteoOptions()));
    }

    [Fact]
    public async Task Add_stores_the_city_and_Get_and_List_return_it()
    {
        var added = await _service.AddAsync(Yogyakarta);

        Assert.True(added.Id > 0);
        Assert.Equal(added, await _service.GetAsync(added.Id));
        Assert.Equal([added], await _service.ListStoredAsync());
    }

    [Fact]
    public async Task Adding_the_same_place_again_returns_the_stored_city()
    {
        var first = await _service.AddAsync(Yogyakarta);

        var sameRef = await _service.AddAsync(Yogyakarta with { Latitude = -7.80139 });
        var samePlaceNoRef = await _service.AddAsync(Yogyakarta with { OpenMeteoRef = null, Latitude = -7.7951 });

        Assert.Equal(first.Id, sameRef.Id);
        Assert.Equal(first.Id, samePlaceNoRef.Id);
        Assert.Single(await _service.ListStoredAsync());
    }

    [Fact]
    public async Task Get_throws_NotFound_for_an_unknown_id()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(999));
    }
}
