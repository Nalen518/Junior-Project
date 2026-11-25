using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Repositories;

namespace ClimaPulse.Services;

/// <summary>
/// The dashboard: a profile's watched cities with their last refresh, worst tier first (SRD FR-17).
/// Reads stored data and the in-memory refresh results only; never calls Open-Meteo (contract 9.1).
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly UserProfileRepository _profiles;
    private readonly WatchlistRepository _watchlist;
    private readonly CityRepository _cities;
    private readonly CityRefreshState _state;

    public DashboardService(UserProfileRepository profiles, WatchlistRepository watchlist, CityRepository cities, CityRefreshState state)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(cities);
        ArgumentNullException.ThrowIfNull(state);
        _profiles = profiles;
        _watchlist = watchlist;
        _cities = cities;
        _state = state;
    }

    public Task<DashboardDto> GetAsync(int profileId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (_profiles.GetById(profileId) is null)
                throw new NotFoundException("Profile", profileId);

            var cities = _cities.GetWatchedBy(profileId).ToDictionary(c => c.Id);
            var cards = _watchlist.GetByProfile(profileId)
                .Select(w =>
                {
                    var latest = _state.GetLatest(w.CityId);
                    // Not refreshed since the app started: no time and no metrics (SRD OPEN-10).
                    return new DashboardCityDto(cities[w.CityId].ToDto(), w.AlertThresholdZScore,
                        latest?.RefreshedAt, latest?.Metrics ?? []);
                })
                .OrderByDescending(Severity)
                .ThenBy(c => c.City.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new DashboardDto(profileId, DateTime.UtcNow, cards);
        }, ct);

    /// <summary>Worst tier of the card for sorting: Extreme 3 down to Normal 0, not refreshed -1.</summary>
    public static int Severity(DashboardCityDto card) =>
        card.LastRefreshedAt is null ? -1 : card.Metrics.Max(m => (int?)(m.Tier ?? SeverityTier.Normal)) ?? 0;
}
