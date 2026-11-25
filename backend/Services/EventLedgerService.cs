using System.Globalization;
using System.Text;
using System.Text.Json;
using ClimaPulse.Contracts;
using ClimaPulse.Enums;
using ClimaPulse.Models;
using ClimaPulse.Repositories;

namespace ClimaPulse.Services;

/// <summary>
/// The Historical Anomaly Ledger: browse, filter, page and export events (SRD FR-26, FR-27).
/// Events keep their own baseline snapshots, so nothing here joins back to Baseline.
/// </summary>
public class EventLedgerService : IEventLedgerService
{
    private readonly AnomalyEventRepository _events;
    private readonly CityRepository _cities;

    public EventLedgerService(AnomalyEventRepository events, CityRepository cities)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(cities);
        _events = events;
        _cities = cities;
    }

    /// <summary>Newest StartedAt first. Page starts at 1, page size 1 to 100.</summary>
    public Task<PagedResult<AnomalyEventDto>> ListAsync(EventQuery query, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query, page, pageSize);

        return Task.Run(() =>
        {
            var q = query;
            var rows = _events.List(q.CityId, q.Metric, q.Tier, q.Status, q.From, q.To, page, pageSize);
            var total = _events.Count(q.CityId, q.Metric, q.Tier, q.Status, q.From, q.To);
            return new PagedResult<AnomalyEventDto>(ToDtos(rows), page, pageSize, total);
        }, ct);
    }

    public Task<AnomalyEventDto> GetAsync(int eventId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var e = _events.GetById(eventId) ?? throw new NotFoundException("Event", eventId);
            return ToDtos([e])[0];
        }, ct);

    /// <summary>
    /// Every matching event (no paging) in the contract 9.7 format: camelCase names as in
    /// AnomalyEventDto, database-text enums, UTC ISO 8601 times, dot decimals, UTF-8.
    /// The stream is left open; the UI owns it.
    /// </summary>
    public async Task ExportAsync(EventQuery query, ExportFormat format, Stream destination, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(destination);
        Validate(query, page: 1, pageSize: 1);
        if (!Enum.IsDefined(format))
            throw Invalid("Format", "Export format must be CSV or JSON.");

        var q = query;
        var events = await Task.Run(
            () => ToDtos(_events.List(q.CityId, q.Metric, q.Tier, q.Status, q.From, q.To, 1, int.MaxValue)), ct);

        var bytes = format == ExportFormat.Csv ? ToCsv(events) : ToJson(events);
        await destination.WriteAsync(bytes, ct);
        await destination.FlushAsync(ct);
    }

    private List<AnomalyEventDto> ToDtos(List<AnomalyEvent> rows)
    {
        // Few cities, so one read of the table beats a lookup per event.
        var names = rows.Count == 0 ? [] : _cities.GetAll().ToDictionary(c => c.Id, c => c.Name);
        return rows.Select(e => e.ToDto(names.GetValueOrDefault(e.CityId, string.Empty))).ToList();
    }

    /// <summary>One export row: the AnomalyEventDto fields in order, already in their file form.</summary>
    private static (string Name, object? Value)[] Fields(AnomalyEventDto e) =>
    [
        ("id", e.Id),
        ("cityId", e.CityId),
        ("cityName", e.CityName),
        ("baselineId", e.BaselineId),
        ("advisoryId", e.AdvisoryId),
        ("metric", e.Metric.ToDb()),
        ("tier", e.Tier.ToDb()),
        ("startedAt", e.StartedAt.ToDb()),
        ("endedAt", e.EndedAt?.ToDb()),
        ("peakAt", e.PeakAt.ToDb()),
        ("peakObservedValue", e.PeakObservedValue),
        ("peakZScore", e.PeakZScore),
        ("baselineMeanSnapshot", e.BaselineMeanSnapshot),
        ("baselineStdDevSnapshot", e.BaselineStdDevSnapshot),
        ("status", e.Status.ToDb()),
        ("createdAt", e.CreatedAt.ToDb()),
    ];

    private static readonly string[] Header = Fields(
        new AnomalyEventDto(0, 0, "", 0, null, MetricType.Temperature, SeverityTier.Watch,
            default, null, default, 0, 0, 0, 0, EventStatus.Active, default)).Select(f => f.Name).ToArray();

    private static byte[] ToCsv(List<AnomalyEventDto> events)
    {
        var csv = new StringBuilder().AppendJoin(',', Header).Append("\r\n");
        foreach (var e in events)
            csv.AppendJoin(',', Fields(e).Select(f => CsvCell(f.Value))).Append("\r\n");
        return new UTF8Encoding(false).GetBytes(csv.ToString());
    }

    // Invariant culture keeps the decimal a dot on Indonesian Windows. Text is quoted when it holds , " or a line break.
    private static string CsvCell(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        string s when s.IndexOfAny([',', '"', '\r', '\n']) >= 0 => "\"" + s.Replace("\"", "\"\"") + "\"",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static byte[] ToJson(List<AnomalyEventDto> events)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartArray();
            foreach (var e in events)
            {
                json.WriteStartObject();
                foreach (var (name, value) in Fields(e))
                {
                    switch (value)
                    {
                        case null: json.WriteNull(name); break;
                        case double d: json.WriteNumber(name, d); break;
                        case int i: json.WriteNumber(name, i); break;
                        default: json.WriteString(name, (string)value); break;
                    }
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        return buffer.ToArray();
    }

    /// <summary>Contract 6.3: page from 1, page size 1 to 100, From not after To. Normal is never stored.</summary>
    private static void Validate(EventQuery q, int page, int pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
            errors["page"] = ["Page starts at 1."];
        if (pageSize is < 1 or > 100)
            errors["pageSize"] = ["Page size must be 1 to 100."];
        if (q.From > q.To)
            errors["From"] = ["From must not be after To."];
        if (q.Tier is { } t && (t == SeverityTier.Normal || !Enum.IsDefined(t)))
            errors["Tier"] = ["Filter by Watch, Warning or Extreme. Normal is never stored."];
        if (q.Metric is { } m && !Enum.IsDefined(m))
            errors["Metric"] = ["Unknown metric."];
        if (q.Status is { } s && !Enum.IsDefined(s))
            errors["Status"] = ["Unknown status."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
