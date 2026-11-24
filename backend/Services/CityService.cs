using ClimaPulse.Contracts;
using ClimaPulse.Models;
using ClimaPulse.Repositories;

namespace ClimaPulse.Services;

/// <summary>
/// City search through Open-Meteo and the stored City table (SRD FR-01, FR-20).
/// Adding the same place twice returns the stored city (D7), because City has no unique key.
/// </summary>
public class CityService : ICityService
{
    private readonly CityRepository _cities;
    private readonly WeatherService _weather;

    public CityService(CityRepository cities, WeatherService weather)
    {
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(weather);
        _cities = cities;
        _weather = weather;
    }

    /// <summary>Open-Meteo down or offline throws UpstreamException; there is no fallback to stored cities.</summary>
    public async Task<IReadOnlyList<CitySearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length < 2)
            throw Invalid("Query", "Type at least 2 characters to search.");

        return await _weather.SearchAsync(trimmed, ct);
    }

    public Task<IReadOnlyList<CityDto>> ListStoredAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<CityDto>>(() => _cities.GetAll().Select(c => c.ToDto()).ToList(), ct);

    public Task<CityDto> GetAsync(int cityId, CancellationToken ct = default) =>
        Task.Run(() => (_cities.GetById(cityId) ?? throw new NotFoundException("City", cityId)).ToDto(), ct);

    /// <summary>D7: match on the Open-Meteo id first, otherwise on name plus coordinates rounded to 2 decimals.</summary>
    public Task<CityDto> AddAsync(CitySearchResult result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        Validate(result);

        return Task.Run(() =>
        {
            var existing = (result.OpenMeteoRef is { } omRef ? _cities.FindByRef(omRef) : null)
                ?? _cities.FindByPlace(result.Name.Trim(), result.Latitude, result.Longitude);
            if (existing is not null)
                return existing.ToDto();

            var city = new City
            {
                Name = result.Name.Trim(),
                CountryCode = result.CountryCode.ToUpperInvariant(),
                AdminRegion = result.AdminRegion,
                Latitude = result.Latitude,
                Longitude = result.Longitude,
                Timezone = result.Timezone,
                ElevationM = result.ElevationM,
                OpenMeteoRef = result.OpenMeteoRef,
            };
            _cities.Add(city);
            return city.ToDto();
        }, ct);
    }

    /// <summary>Contract 6.3 and the City table CHECKs.</summary>
    private static void Validate(CitySearchResult r)
    {
        var errors = new Dictionary<string, string[]>();
        var name = r.Name?.Trim() ?? string.Empty;

        if (name.Length is < 1 or > 100)
            errors["Name"] = ["Name must be 1 to 100 characters."];
        if (r.CountryCode is not { Length: 2 } || !r.CountryCode.All(char.IsAsciiLetter))
            errors["CountryCode"] = ["Country code must be exactly 2 letters."];
        if (r.AdminRegion is { Length: > 100 })
            errors["AdminRegion"] = ["Region can be at most 100 characters."];
        if (!(r.Latitude is >= -90 and <= 90))
            errors["Latitude"] = ["Latitude must be between -90 and 90."];
        if (!(r.Longitude is >= -180 and <= 180))
            errors["Longitude"] = ["Longitude must be between -180 and 180."];
        if (string.IsNullOrWhiteSpace(r.Timezone) || r.Timezone.Length > 40)
            errors["Timezone"] = ["Time zone is required, at most 40 characters."];
        if (r.OpenMeteoRef is { Length: > 40 })
            errors["OpenMeteoRef"] = ["Open-Meteo id can be at most 40 characters."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
