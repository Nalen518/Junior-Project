using ClimaPulse.Enums;
using ClimaPulse.Models;
using Npgsql;

namespace ClimaPulse.Repositories;

/// <summary>
/// Reads and writes the HistoricalObservation table. Add throws PostgresException for a
/// reading that already exists (UNIQUE on city_id, metric_type, observed_at). Use
/// AddRange for bulk loads from Open-Meteo, which skips readings already stored.
/// </summary>
public class HistoricalObservationRepository : PostgresRepositoryBase, IRepository<HistoricalObservation>
{
    private const string Columns =
        "observation_id, city_id, metric_type, observed_at, value, source, fetched_at";

    public HistoricalObservationRepository(string connectionString) : base(connectionString)
    {
    }

    public void Add(HistoricalObservation item)
    {
        item.Id = InsertAndGetId(
            @"INSERT INTO HistoricalObservation (city_id, metric_type, observed_at, value, source, fetched_at)
              VALUES (@city, @metric, @observed, @value, @source, @fetched)",
            cmd =>
            {
                AddParam(cmd, "@city", item.CityId);
                AddParam(cmd, "@metric", item.MetricType.ToDb());
                AddParam(cmd, "@observed", item.ObservedAt.ToDb());
                AddParam(cmd, "@value", item.Value);
                AddParam(cmd, "@source", item.Source);
                AddParam(cmd, "@fetched", item.FetchedAt.ToDb());
            });
    }

    /// <summary>
    /// Inserts many readings in one transaction, ignoring any that already exist, so a
    /// re-fetch is safe. Returns how many new rows were stored. Ids are not set on items.
    /// The rows go in with one binary COPY into a temporary table and one INSERT from it,
    /// so 30 years of history costs a few round trips instead of one per row: on a hosted
    /// database about 90 ms per round trip made the old row-by-row insert take ~50 minutes.
    /// </summary>
    public int AddRange(IEnumerable<HistoricalObservation> items)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                @"CREATE TEMP TABLE observation_import (
                      city_id INTEGER, metric_type TEXT, observed_at TEXT,
                      value DOUBLE PRECISION, source TEXT, fetched_at TEXT
                  ) ON COMMIT DROP";
            create.ExecuteNonQuery();
        }

        using (var import = connection.BeginBinaryImport(
                   "COPY observation_import (city_id, metric_type, observed_at, value, source, fetched_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var item in items)
            {
                import.StartRow();
                import.Write(item.CityId);
                import.Write(item.MetricType.ToDb());
                import.Write(item.ObservedAt.ToDb());
                import.Write(item.Value);
                import.Write(item.Source);
                import.Write(item.FetchedAt.ToDb());
            }
            import.Complete();
        }

        // ON CONFLICT also skips duplicates inside the batch itself, as the old loop did.
        using var insert = connection.CreateCommand();
        insert.CommandText =
            @"INSERT INTO HistoricalObservation (city_id, metric_type, observed_at, value, source, fetched_at)
              SELECT city_id, metric_type, observed_at, value, source, fetched_at FROM observation_import
              ON CONFLICT (city_id, metric_type, observed_at) DO NOTHING";
        var inserted = insert.ExecuteNonQuery();

        transaction.Commit();
        return inserted;
    }

    public void Remove(HistoricalObservation item) =>
        ExecuteNonQuery("DELETE FROM HistoricalObservation WHERE observation_id = @id",
            cmd => AddParam(cmd, "@id", item.Id));

    public List<HistoricalObservation> GetAll() =>
        ReadList($"SELECT {Columns} FROM HistoricalObservation ORDER BY city_id, metric_type, observed_at", Map);

    public List<HistoricalObservation> Query(Func<HistoricalObservation, bool> filter) =>
        GetAll().Where(filter).ToList();

    /// <summary>Readings for one city and metric between two UTC instants, inclusive, oldest first.</summary>
    public List<HistoricalObservation> GetRange(int cityId, MetricType metric, DateTime fromUtc, DateTime toUtc) =>
        ReadList(
            $@"SELECT {Columns} FROM HistoricalObservation
               WHERE city_id = @city AND metric_type = @metric
                 AND observed_at >= @from AND observed_at <= @to
               ORDER BY observed_at",
            Map,
            cmd =>
            {
                AddParam(cmd, "@city", cityId);
                AddParam(cmd, "@metric", metric.ToDb());
                AddParam(cmd, "@from", fromUtc.ToDb());
                AddParam(cmd, "@to", toUtc.ToDb());
            });

    private static HistoricalObservation Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CityId = r.GetInt32(1),
        MetricType = r.GetString(2).ToMetricType(),
        ObservedAt = Timestamp(r, 3),
        Value = r.GetDouble(4),
        Source = r.GetString(5),
        FetchedAt = Timestamp(r, 6)
    };
}
