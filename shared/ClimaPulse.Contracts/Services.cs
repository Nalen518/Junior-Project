using ClimaPulse.Enums;

namespace ClimaPulse.Contracts;

// The UI (frontend) depends only on these interfaces. The backend implements them.
// Every method may throw the exceptions in Errors.cs and nothing else on purpose.

public interface IProfileService
{
    Task<IReadOnlyList<ProfileDto>> ListAsync(CancellationToken ct = default);               // FR-29 picker
    Task<ProfileDto> GetAsync(int profileId, CancellationToken ct = default);                // NotFound
    Task<ProfileDto> CreateAsync(CreateProfileRequest request, CancellationToken ct = default);  // Validation
    Task<ProfileDto> UpdateAsync(int profileId, UpdateProfileRequest request, CancellationToken ct = default); // FR-05
}

public interface ICityService
{
    /// <summary>Find places by name through Open-Meteo geocoding. Nothing is stored. FR-01.</summary>
    Task<IReadOnlyList<CitySearchResult>> SearchAsync(string query, CancellationToken ct = default); // Validation, Upstream
    Task<IReadOnlyList<CityDto>> ListStoredAsync(CancellationToken ct = default);
    Task<CityDto> GetAsync(int cityId, CancellationToken ct = default);                      // NotFound
    /// <summary>Store a search hit. Returns the existing city if the same place is already stored.</summary>
    Task<CityDto> AddAsync(CitySearchResult result, CancellationToken ct = default);         // Validation
}

public interface IWatchlistService
{
    Task<IReadOnlyList<WatchlistEntryDto>> ListAsync(int profileId, CancellationToken ct = default);  // FR-02
    Task<WatchlistEntryDto> AddAsync(int profileId, AddWatchlistRequest request, CancellationToken ct = default); // Conflict if already watched
    Task<WatchlistEntryDto> UpdateAsync(int profileId, int cityId, UpdateWatchlistRequest request, CancellationToken ct = default); // FR-04
    Task RemoveAsync(int profileId, int cityId, CancellationToken ct = default);             // FR-03, events are kept
}

public interface IDashboardService
{
    /// <summary>Reads stored data only. Never calls Open-Meteo. FR-17.</summary>
    Task<DashboardDto> GetAsync(int profileId, CancellationToken ct = default);
}

public interface IMonitoringService
{
    /// <summary>Fetch live data, compute z-scores, open, update or close events. FR-06, FR-10 to FR-12, FR-22 to FR-25.</summary>
    Task<RefreshResultDto> RefreshCityAsync(int cityId, CancellationToken ct = default);     // NotFound, Upstream
}

public interface IBaselineService
{
    Task<IReadOnlyList<BaselineDto>> ListAsync(int cityId, MetricType? metric = null, CancellationToken ct = default); // FR-20
    /// <summary>Slow (archive download). Progress reports 0 to 100. FR-07 to FR-09.</summary>
    Task<IReadOnlyList<BaselineDto>> RefreshAsync(int cityId, IProgress<int>? progress = null, CancellationToken ct = default); // Conflict if running, Upstream
}

public interface IAdvisoryService
{
    /// <summary>Latest version per category, falling back to en-US when the locale has none. FR-19.</summary>
    Task<IReadOnlyList<AdvisoryDto>> GetLatestAsync(MetricType metric, SeverityTier tier, string locale, CancellationToken ct = default);
}

public interface IEventLedgerService
{
    /// <summary>Newest StartedAt first. Page starts at 1, PageSize 1 to 100. FR-26.</summary>
    Task<PagedResult<AnomalyEventDto>> ListAsync(EventQuery query, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<AnomalyEventDto> GetAsync(int eventId, CancellationToken ct = default);              // NotFound
    /// <summary>Writes every matching event (no paging) to the stream. The UI owns the Save dialog. FR-27.</summary>
    Task ExportAsync(EventQuery query, ExportFormat format, Stream destination, CancellationToken ct = default);
}
