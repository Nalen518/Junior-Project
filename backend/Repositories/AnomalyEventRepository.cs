using ClimaPulse.Enums;
using ClimaPulse.Models;
using Npgsql;

namespace ClimaPulse.Repositories;

/// <summary>
/// Reads and writes the AnomalyEvent table, the Historical Anomaly Ledger. Replaces
/// AnomalyLogRepository. An active event must have no ended_at, and a closed event
/// must end at or after it started; the schema rejects anything else.
/// </summary>
public class AnomalyEventRepository : PostgresRepositoryBase, IRepository<AnomalyEvent>
{
    private const string Columns =
        "event_id, city_id, baseline_id, advisory_id, metric_type, severity_tier, started_at, ended_at, " +
        "peak_at, peak_observed_value, peak_zscore, baseline_mean_snapshot, baseline_stddev_snapshot, status, created_at";

    public AnomalyEventRepository(string connectionString) : base(connectionString)
    {
    }

    public void Add(AnomalyEvent item)
    {
        item.Id = InsertAndGetId(
            @"INSERT INTO AnomalyEvent (city_id, baseline_id, advisory_id, metric_type, severity_tier, started_at,
                                        ended_at, peak_at, peak_observed_value, peak_zscore,
                                        baseline_mean_snapshot, baseline_stddev_snapshot, status, created_at)
              VALUES (@city, @baseline, @advisory, @metric, @tier, @started,
                      @ended, @peakAt, @peakValue, @peakZ,
                      @mean, @std, @status, @created)",
            cmd =>
            {
                AddParam(cmd, "@city", item.CityId);
                AddParam(cmd, "@baseline", item.BaselineId);
                AddParam(cmd, "@advisory", item.AdvisoryId);
                AddParam(cmd, "@metric", item.MetricType.ToDb());
                AddParam(cmd, "@tier", item.SeverityTier.ToDb());
                AddParam(cmd, "@started", item.StartedAt.ToDb());
                AddParam(cmd, "@ended", item.EndedAt?.ToDb());
                AddParam(cmd, "@peakAt", item.PeakAt.ToDb());
                AddParam(cmd, "@peakValue", item.PeakObservedValue);
                AddParam(cmd, "@peakZ", item.PeakZScore);
                AddParam(cmd, "@mean", item.BaselineMeanSnapshot);
                AddParam(cmd, "@std", item.BaselineStdDevSnapshot);
                AddParam(cmd, "@status", item.Status.ToDb());
                AddParam(cmd, "@created", item.CreatedAt.ToDb());
            });
    }

    public void Remove(AnomalyEvent item) =>
        ExecuteNonQuery("DELETE FROM AnomalyEvent WHERE event_id = @id", cmd => AddParam(cmd, "@id", item.Id));

    public List<AnomalyEvent> GetAll() =>
        ReadList($"SELECT {Columns} FROM AnomalyEvent ORDER BY started_at DESC", Map);

    public List<AnomalyEvent> Query(Func<AnomalyEvent, bool> filter) =>
        GetAll().Where(filter).ToList();

    public AnomalyEvent? GetById(int eventId) =>
        ReadList($"SELECT {Columns} FROM AnomalyEvent WHERE event_id = @id", Map,
            cmd => AddParam(cmd, "@id", eventId)).FirstOrDefault();

    /// <summary>
    /// The open episode for a city and metric, or null. Used to decide whether a new
    /// reading continues an existing event or starts a new one.
    /// </summary>
    public AnomalyEvent? GetOpenEvent(int cityId, MetricType metric) =>
        ReadList(
            $@"SELECT {Columns} FROM AnomalyEvent
               WHERE city_id = @city AND metric_type = @metric AND status = 'active'
               ORDER BY started_at DESC
               LIMIT 1",
            Map,
            cmd =>
            {
                AddParam(cmd, "@city", cityId);
                AddParam(cmd, "@metric", metric.ToDb());
            }).FirstOrDefault();

    /// <summary>
    /// Closes an active event. Returns false if the event does not exist or is already
    /// closed. Replaces the old Acknowledge method.
    /// </summary>
    public bool Close(int eventId, DateTime endedAtUtc) =>
        ExecuteNonQuery(
            "UPDATE AnomalyEvent SET status = 'closed', ended_at = @ended WHERE event_id = @id AND status = 'active'",
            cmd =>
            {
                AddParam(cmd, "@id", eventId);
                AddParam(cmd, "@ended", endedAtUtc.ToDb());
            }) > 0;

    /// <summary>
    /// One page of the ledger, newest StartedAt first (SRD FR-26). A null filter means
    /// "no filter"; from and to bound StartedAt, inclusive. Page starts at 1; the service
    /// checks page and pageSize.
    /// </summary>
    public List<AnomalyEvent> List(
        int? cityId, MetricType? metric, SeverityTier? tier, EventStatus? status,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        var (where, bind) = BuildFilter(cityId, metric, tier, status, fromUtc, toUtc);
        return ReadList(
            $"SELECT {Columns} FROM AnomalyEvent{where} ORDER BY started_at DESC, event_id DESC LIMIT @limit OFFSET @offset",
            Map,
            cmd =>
            {
                bind(cmd);
                AddParam(cmd, "@limit", pageSize);
                AddParam(cmd, "@offset", (page - 1) * pageSize);
            });
    }

    /// <summary>Number of events matching the same filter as <see cref="List"/>, for paging.</summary>
    public int Count(
        int? cityId, MetricType? metric, SeverityTier? tier, EventStatus? status,
        DateTime? fromUtc, DateTime? toUtc)
    {
        var (where, bind) = BuildFilter(cityId, metric, tier, status, fromUtc, toUtc);
        return (int)ReadList($"SELECT COUNT(*) FROM AnomalyEvent{where}", r => r.GetInt64(0), bind)[0];
    }

    // Only non-null filters become SQL, so Npgsql never sees an untyped null parameter.
    // started_at is ISO 8601 UTC text, so text comparison orders it correctly.
    private static (string Where, Action<NpgsqlCommand> Bind) BuildFilter(
        int? cityId, MetricType? metric, SeverityTier? tier, EventStatus? status,
        DateTime? fromUtc, DateTime? toUtc)
    {
        var clauses = new List<string>();
        var binds = new List<Action<NpgsqlCommand>>();

        void Add(string clause, string name, object value)
        {
            clauses.Add(clause);
            binds.Add(cmd => AddParam(cmd, name, value));
        }

        if (cityId is { } c) Add("city_id = @city", "@city", c);
        if (metric is { } m) Add("metric_type = @metric", "@metric", m.ToDb());
        if (tier is { } t) Add("severity_tier = @tier", "@tier", t.ToDb());
        if (status is { } s) Add("status = @status", "@status", s.ToDb());
        if (fromUtc is { } f) Add("started_at >= @from", "@from", f.ToDb());
        if (toUtc is { } to) Add("started_at <= @to", "@to", to.ToDb());

        var where = clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
        return (where, cmd => binds.ForEach(b => b(cmd)));
    }

    /// <summary>
    /// Records a worse reading on an active event (SRD FR-23). The caller decides the reading
    /// is worse; the SQL keeps the higher tier, so an event's tier never goes down.
    /// Returns false if the event does not exist or is already closed.
    /// </summary>
    public bool UpdatePeak(int eventId, SeverityTier tier, DateTime peakAtUtc, double peakValue, double peakZScore) =>
        ExecuteNonQuery(
            @"UPDATE AnomalyEvent
              SET peak_at = @peakAt, peak_observed_value = @peakValue, peak_zscore = @peakZ,
                  severity_tier = CASE
                      WHEN array_position(ARRAY['watch', 'warning', 'extreme'], @tier)
                         > array_position(ARRAY['watch', 'warning', 'extreme'], severity_tier)
                      THEN @tier ELSE severity_tier END
              WHERE event_id = @id AND status = 'active'",
            cmd =>
            {
                AddParam(cmd, "@id", eventId);
                AddParam(cmd, "@tier", tier.ToDb());
                AddParam(cmd, "@peakAt", peakAtUtc.ToDb());
                AddParam(cmd, "@peakValue", peakValue);
                AddParam(cmd, "@peakZ", peakZScore);
            }) > 0;

    private static AnomalyEvent Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CityId = r.GetInt32(1),
        BaselineId = r.GetInt32(2),
        AdvisoryId = NullableInt(r, 3),
        MetricType = r.GetString(4).ToMetricType(),
        SeverityTier = r.GetString(5).ToSeverityTier(),
        StartedAt = Timestamp(r, 6),
        EndedAt = NullableTimestamp(r, 7),
        PeakAt = Timestamp(r, 8),
        PeakObservedValue = r.GetDouble(9),
        PeakZScore = r.GetDouble(10),
        BaselineMeanSnapshot = r.GetDouble(11),
        BaselineStdDevSnapshot = r.GetDouble(12),
        Status = r.GetString(13).ToEventStatus(),
        CreatedAt = Timestamp(r, 14)
    };
}
