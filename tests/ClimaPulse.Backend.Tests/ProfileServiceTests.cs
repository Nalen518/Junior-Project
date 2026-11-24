using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Repositories;
using ClimaPulse.Services;

namespace ClimaPulse.Backend.Tests;

/// <summary>Validation runs before any database call, so these need no database.</summary>
public class ProfileServiceValidationTests
{
    private static readonly ProfileService Service = new(new UserProfileRepository("Host=localhost;Database=unused"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890123456789012345678901")]   // 51 characters
    public async Task Create_rejects_a_bad_display_name(string name)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service.CreateAsync(new CreateProfileRequest(name)));
        Assert.True(ex.Errors.ContainsKey("DisplayName"));
    }

    [Fact]
    public async Task Create_rejects_a_locale_other_than_en_US()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service.CreateAsync(new CreateProfileRequest("Syauqi", PreferredLocale: "id-ID")));
        Assert.Equal(["PreferredLocale"], ex.Errors.Keys);
    }

    [Fact]
    public async Task Update_rejects_an_unknown_temperature_unit()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Service.UpdateAsync(1, new UpdateProfileRequest("Syauqi", (TemperatureUnit)9, "en-US")));
        Assert.True(ex.Errors.ContainsKey("PreferredTemperatureUnit"));
    }
}

/// <summary>IProfileService against climapulse_test.</summary>
[Collection("Database")]
[Trait("Category", "Database")]
public class ProfileServiceDatabaseTests
{
    private readonly ProfileService _service;

    public ProfileServiceDatabaseTests(TestDatabase db)
    {
        db.Reset();
        _service = new ProfileService(new UserProfileRepository(db.ConnectionString));
    }

    [Fact]
    public async Task Create_trims_the_name_and_returns_the_stored_profile()
    {
        var created = await _service.CreateAsync(new CreateProfileRequest("  Syauqi  ", TemperatureUnit.Fahrenheit));

        Assert.True(created.Id > 0);
        Assert.Equal("Syauqi", created.DisplayName);
        Assert.Equal(TemperatureUnit.Fahrenheit, created.PreferredTemperatureUnit);
        Assert.Equal("en-US", created.PreferredLocale);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
        Assert.Equal(created, await _service.GetAsync(created.Id));
    }

    [Fact]
    public async Task List_returns_every_profile_and_is_empty_not_null_when_there_are_none()
    {
        Assert.Empty(await _service.ListAsync());

        await _service.CreateAsync(new CreateProfileRequest("Nalen"));
        await _service.CreateAsync(new CreateProfileRequest("Christa"));

        Assert.Equal(["Christa", "Nalen"], (await _service.ListAsync()).Select(p => p.DisplayName));
    }

    [Fact]
    public async Task Update_replaces_the_fields()
    {
        var created = await _service.CreateAsync(new CreateProfileRequest("Radit"));

        var updated = await _service.UpdateAsync(created.Id,
            new UpdateProfileRequest("Radit P", TemperatureUnit.Fahrenheit, "en-US"));

        Assert.Equal("Radit P", updated.DisplayName);
        Assert.Equal(TemperatureUnit.Fahrenheit, updated.PreferredTemperatureUnit);
        Assert.Equal(updated, await _service.GetAsync(created.Id));
    }

    [Fact]
    public async Task Get_and_Update_throw_NotFound_for_an_unknown_id()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(999));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UpdateAsync(999, new UpdateProfileRequest("X", TemperatureUnit.Celsius, "en-US")));
    }
}
