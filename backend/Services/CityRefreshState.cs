using System.Collections.Concurrent;
using ClimaPulse.Contracts;

namespace ClimaPulse.Services;

/// <summary>
/// State shared by the baseline, monitoring and dashboard services, created once in Program.cs.
/// It holds which cities are refreshing right now, so a baseline refresh and a live refresh
/// never overlap (contract 9.2), and the last refresh result per city, kept in memory
/// instead of a table (SRD OPEN-10). Thread-safe.
/// </summary>
public sealed class CityRefreshState
{
    private readonly ConcurrentDictionary<int, byte> _running = new();
    private readonly ConcurrentDictionary<int, RefreshResultDto> _latest = new();

    /// <summary>
    /// Marks the city as refreshing until the returned lease is disposed.
    /// Throws ConflictException when a refresh for the same city is already running.
    /// </summary>
    public IDisposable Begin(int cityId, string cityName)
    {
        if (!_running.TryAdd(cityId, 0))
            throw new ConflictException($"{cityName} is already being refreshed. Try again when it finishes.");
        return new Lease(() => _running.TryRemove(cityId, out _));
    }

    public void SaveLatest(RefreshResultDto result) => _latest[result.CityId] = result;

    /// <summary>The last refresh of this city since the app started, or null.</summary>
    public RefreshResultDto? GetLatest(int cityId) => _latest.GetValueOrDefault(cityId);

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
