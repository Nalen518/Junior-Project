using ClimaPulse.Enums;

namespace ClimaPulse.Contracts;

// Every DateTime in the contract is UTC (DateTimeKind.Utc). Temperatures are Celsius.

public sealed record CityDto(
    int Id,
    string Name,
    string CountryCode,
    string? AdminRegion,
    double Latitude,
    double Longitude,
    string Timezone,
    double? ElevationM);

/// <summary>A search hit that is not stored yet. Pass it to ICityService.AddAsync.</summary>
public sealed record CitySearchResult(
    string Name,
    string CountryCode,
    string? AdminRegion,
    double Latitude,
    double Longitude,
    string Timezone,
    double? ElevationM,
    string? OpenMeteoRef);

public sealed record ProfileDto(
    int Id,
    string DisplayName,
    TemperatureUnit PreferredTemperatureUnit,
    string PreferredLocale,
    DateTime CreatedAt);

public sealed record WatchlistEntryDto(
    int Id,
    int ProfileId,
    CityDto City,
    double AlertThresholdZScore,
    string? Notes,
    DateTime AddedAt);

public sealed record BaselineDto(
    int Id,
    int CityId,
    MetricType Metric,
    int Month,
    double Mean,
    double StdDev,
    int SampleSize,
    int BaselineStartYear,
    int BaselineEndYear,
    DateTime ComputedAt);

public sealed record AdvisoryDto(
    int Id,
    MetricType Metric,
    SeverityTier Tier,
    string Locale,
    AdvisoryCategory Category,
    string Title,
    string Body,
    int Version,
    DateTime EffectiveFrom);

/// <summary>One metric of one city at one moment. Used by refresh and dashboard.</summary>
public sealed record MetricReadingDto(
    MetricType Metric,
    DataStatus DataStatus,
    DateTime? ObservedAt,
    double? ObservedValue,
    double? BaselineMean,
    double? BaselineStdDev,
    double? ZScore,                        // null unless DataStatus is Ok
    SeverityTier? Tier,                    // null unless DataStatus is Ok, computed by the backend only
    int? ActiveEventId,
    IReadOnlyList<AdvisoryDto> Advisories); // empty when Tier is null or Normal

public sealed record RefreshResultDto(
    int CityId,
    DateTime RefreshedAt,
    IReadOnlyList<MetricReadingDto> Metrics);

public sealed record DashboardCityDto(
    CityDto City,
    double AlertThresholdZScore,
    DateTime? LastRefreshedAt,
    IReadOnlyList<MetricReadingDto> Metrics);

public sealed record DashboardDto(
    int ProfileId,
    DateTime GeneratedAt,
    IReadOnlyList<DashboardCityDto> Cities);   // worst tier first

public sealed record AnomalyEventDto(
    int Id,
    int CityId,
    string CityName,
    int BaselineId,
    int? AdvisoryId,
    MetricType Metric,
    SeverityTier Tier,                      // never Normal
    DateTime StartedAt,
    DateTime? EndedAt,                      // null exactly when Status is Active
    DateTime PeakAt,
    double PeakObservedValue,
    double PeakZScore,
    double BaselineMeanSnapshot,
    double BaselineStdDevSnapshot,
    EventStatus Status,
    DateTime CreatedAt);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems)
{
    public int TotalPages => PageSize == 0 ? 0 : (TotalItems + PageSize - 1) / PageSize;
}
