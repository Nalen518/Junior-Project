using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;

namespace ClimaPulse.Services;

/// <summary>
/// Turns ClimaPulse.Models rows into contract DTOs. Services return only these,
/// so the UI never sees a database entity (contract section 3).
/// </summary>
internal static class DtoMapping
{
    public static ProfileDto ToDto(this UserProfile p) => new(
        p.Id,
        p.DisplayName,
        p.PreferredTemperatureUnit == "F" ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius,
        p.PreferredLocale,
        p.CreatedAt);

    public static CityDto ToDto(this City c) => new(
        c.Id, c.Name, c.CountryCode, c.AdminRegion, c.Latitude, c.Longitude, c.Timezone, c.ElevationM);

    public static WatchlistEntryDto ToDto(this WatchlistEntry w, City city) => new(
        w.Id, w.ProfileId, city.ToDto(), w.AlertThresholdZScore, w.Notes, w.AddedAt);

    public static AdvisoryDto ToDto(this Advisory a) => new(
        a.Id, a.MetricType, a.SeverityTier, a.Locale, a.Category, a.Title, a.Body, a.Version, a.EffectiveFrom);

    public static AnomalyEventDto ToDto(this AnomalyEvent e, string cityName) => new(
        e.Id, e.CityId, cityName, e.BaselineId, e.AdvisoryId, e.MetricType, e.SeverityTier,
        e.StartedAt, e.EndedAt, e.PeakAt, e.PeakObservedValue, e.PeakZScore,
        e.BaselineMeanSnapshot, e.BaselineStdDevSnapshot, e.Status, e.CreatedAt);

    /// <summary>
    /// UTC now cut to whole seconds, the precision the database keeps, so a DTO
    /// returned on create equals the one read back later.
    /// </summary>
    public static DateTime UtcNowForDb() =>
        DateTime.UnixEpoch.AddSeconds((long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds);

    /// <summary>The schema stores the unit as "C" or "F".</summary>
    public static string ToDb(this TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? "F" : "C";
}
