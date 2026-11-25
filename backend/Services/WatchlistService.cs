using ClimaPulse.Contracts;
using ClimaPulse.Models;
using ClimaPulse.Repositories;
using Npgsql;

namespace ClimaPulse.Services;

/// <summary>
/// The cities one profile watches, with a per-city alert threshold and notes (SRD FR-02 to FR-04).
/// Removing a city keeps its anomaly events in the ledger.
/// </summary>
public class WatchlistService : IWatchlistService
{
    private readonly WatchlistRepository _watchlist;
    private readonly UserProfileRepository _profiles;
    private readonly CityRepository _cities;

    public WatchlistService(WatchlistRepository watchlist, UserProfileRepository profiles, CityRepository cities)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(cities);
        _watchlist = watchlist;
        _profiles = profiles;
        _cities = cities;
    }

    /// <summary>Oldest entry first. An unknown profile has an empty list.</summary>
    public Task<IReadOnlyList<WatchlistEntryDto>> ListAsync(int profileId, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<WatchlistEntryDto>>(() =>
        {
            var cities = _cities.GetWatchedBy(profileId).ToDictionary(c => c.Id);
            return _watchlist.GetByProfile(profileId).Select(w => w.ToDto(cities[w.CityId])).ToList();
        }, ct);

    public Task<WatchlistEntryDto> AddAsync(int profileId, AddWatchlistRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.AlertThresholdZScore, request.Notes);

        return Task.Run(() =>
        {
            if (_profiles.GetById(profileId) is null)
                throw new NotFoundException("Profile", profileId);
            var city = _cities.GetById(request.CityId) ?? throw new NotFoundException("City", request.CityId);

            var entry = new WatchlistEntry
            {
                ProfileId = profileId,
                CityId = city.Id,
                AlertThresholdZScore = request.AlertThresholdZScore,
                Notes = request.Notes,
                AddedAt = DtoMapping.UtcNowForDb(),
            };

            try
            {
                _watchlist.Add(entry);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new ConflictException($"{city.Name} is already on this watchlist.");
            }

            return entry.ToDto(city);
        }, ct);
    }

    /// <summary>Full replacement: Notes = null clears the note.</summary>
    public Task<WatchlistEntryDto> UpdateAsync(int profileId, int cityId, UpdateWatchlistRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.AlertThresholdZScore, request.Notes);

        return Task.Run(() =>
        {
            var entry = Find(profileId, cityId);
            entry.AlertThresholdZScore = request.AlertThresholdZScore;
            entry.Notes = request.Notes;
            _watchlist.Update(entry);
            return entry.ToDto(_cities.GetById(cityId)!);
        }, ct);
    }

    public Task RemoveAsync(int profileId, int cityId, CancellationToken ct = default) =>
        Task.Run(() => _watchlist.Remove(Find(profileId, cityId)), ct);

    private WatchlistEntry Find(int profileId, int cityId) =>
        _watchlist.GetByProfile(profileId).FirstOrDefault(w => w.CityId == cityId)
            ?? throw new NotFoundException("Watched city", cityId);

    /// <summary>Contract 6.3: threshold greater than 0, notes up to 500 characters.</summary>
    private static void Validate(double threshold, string? notes)
    {
        var errors = new Dictionary<string, string[]>();
        if (!double.IsFinite(threshold) || threshold <= 0)
            errors["AlertThresholdZScore"] = ["Alert threshold must be greater than 0."];
        if (notes is { Length: > 500 })
            errors["Notes"] = ["Notes can be at most 500 characters."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
