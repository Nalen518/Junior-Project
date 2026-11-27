using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;

namespace ClimaPulse.Services;

/// <summary>
/// Picks the advisories to show for an active anomaly (SRD FR-19). The text lives in the
/// Advisory table (loaded from seed.sql), not in code. GetLatestAsync is the contract method
/// for the UI; GetLatest stays synchronous for backend services that already run in Task.Run.
/// </summary>
public class AdvisoryService : IAdvisoryService
{
    /// <summary>Used when the user's language has no advisory for this metric and tier.</summary>
    public const string FallbackLocale = "en-US";   // v1 is English only (SRD UI-5)

    private readonly AdvisoryRepository _advisoryRepo;

    public AdvisoryService(AdvisoryRepository advisoryRepo)
    {
        ArgumentNullException.ThrowIfNull(advisoryRepo);
        _advisoryRepo = advisoryRepo;
    }

    /// <summary>Contract version of <see cref="GetLatest"/>: DTOs, and ValidationException for a missing locale.</summary>
    public Task<IReadOnlyList<AdvisoryDto>> GetLatestAsync(
        MetricType metric, SeverityTier tier, string locale, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(locale))
            throw new ValidationException(new Dictionary<string, string[]> { ["Locale"] = ["Locale is required."] });

        return Task.Run<IReadOnlyList<AdvisoryDto>>(
            () => GetLatest(metric, tier, locale).Select(a => a.ToDto()).ToList(), ct);
    }

    /// <summary>
    /// Latest version per category (health, climate) for a metric, tier and locale, falling
    /// back to en-US when the locale has none. Normal means no anomaly, so it returns nothing.
    /// </summary>
    public List<Advisory> GetLatest(MetricType metric, SeverityTier tier, string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if (tier == SeverityTier.Normal)
            return [];

        var advisories = _advisoryRepo.GetLatest(metric, tier, locale);
        if (advisories.Count == 0 && locale != FallbackLocale)
            advisories = _advisoryRepo.GetLatest(metric, tier, FallbackLocale);

        return advisories;
    }
}
