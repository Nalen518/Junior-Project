using ClimaPulse.Enums;

namespace ClimaPulse.Contracts;

// Validation ranges come from the schema. The UI checks them for fast feedback,
// and the backend checks them again and throws ValidationException.

public sealed record CreateProfileRequest(
    string DisplayName,                                         // 1 to 50 chars
    TemperatureUnit PreferredTemperatureUnit = TemperatureUnit.Celsius,
    string PreferredLocale = "en-US");                          // up to 10 chars

/// <summary>Full replacement: send every field.</summary>
public sealed record UpdateProfileRequest(
    string DisplayName,
    TemperatureUnit PreferredTemperatureUnit,
    string PreferredLocale);

public sealed record AddWatchlistRequest(
    int CityId,
    double AlertThresholdZScore = 2.0,                          // greater than 0
    string? Notes = null);                                      // up to 500 chars

/// <summary>Full replacement: Notes = null clears the note.</summary>
public sealed record UpdateWatchlistRequest(
    double AlertThresholdZScore,
    string? Notes);

/// <summary>Filters for the ledger. Null means "no filter". From and To bound StartedAt, inclusive, UTC.</summary>
public sealed record EventQuery(
    int? CityId = null,
    MetricType? Metric = null,
    SeverityTier? Tier = null,
    EventStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null);
