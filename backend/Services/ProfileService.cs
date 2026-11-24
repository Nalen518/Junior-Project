using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;

namespace ClimaPulse.Services;

/// <summary>
/// Local profiles for the profile picker and preferences (SRD FR-29, FR-05).
/// No passwords. The repository is synchronous, so calls run inside Task.Run.
/// </summary>
public class ProfileService : IProfileService
{
    private const string SupportedLocale = "en-US";   // v1 is English only (SRD UI-5)

    private readonly UserProfileRepository _profiles;

    public ProfileService(UserProfileRepository profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles;
    }

    public Task<IReadOnlyList<ProfileDto>> ListAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ProfileDto>>(() => _profiles.GetAll().Select(p => p.ToDto()).ToList(), ct);

    public Task<ProfileDto> GetAsync(int profileId, CancellationToken ct = default) =>
        Task.Run(() => Find(profileId).ToDto(), ct);

    public Task<ProfileDto> CreateAsync(CreateProfileRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = Validate(request.DisplayName, request.PreferredTemperatureUnit, request.PreferredLocale);

        return Task.Run(() =>
        {
            var profile = new UserProfile
            {
                DisplayName = name,
                PreferredTemperatureUnit = request.PreferredTemperatureUnit.ToDb(),
                PreferredLocale = request.PreferredLocale,
                CreatedAt = DtoMapping.UtcNowForDb(),
            };
            _profiles.Add(profile);
            return profile.ToDto();
        }, ct);
    }

    public Task<ProfileDto> UpdateAsync(int profileId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = Validate(request.DisplayName, request.PreferredTemperatureUnit, request.PreferredLocale);

        return Task.Run(() =>
        {
            // Read first so fields outside the contract (is_active) keep their stored value.
            var profile = Find(profileId);
            profile.DisplayName = name;
            profile.PreferredTemperatureUnit = request.PreferredTemperatureUnit.ToDb();
            profile.PreferredLocale = request.PreferredLocale;
            _profiles.Update(profile);
            return profile.ToDto();
        }, ct);
    }

    private UserProfile Find(int profileId) =>
        _profiles.GetById(profileId) ?? throw new NotFoundException("Profile", profileId);

    /// <summary>Checks the contract 6.3 rules and returns the trimmed display name.</summary>
    private static string Validate(string? displayName, TemperatureUnit unit, string? locale)
    {
        var errors = new Dictionary<string, string[]>();
        var name = displayName?.Trim() ?? string.Empty;

        if (name.Length is < 1 or > 50)
            errors["DisplayName"] = ["Display name must be 1 to 50 characters, not only spaces."];
        if (!Enum.IsDefined(unit))
            errors["PreferredTemperatureUnit"] = ["Temperature unit must be Celsius or Fahrenheit."];
        if (locale != SupportedLocale)
            errors["PreferredLocale"] = ["Only en-US is supported in this version."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
        return name;
    }
}
