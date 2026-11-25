using ClimaPulse.Repositories;
using Npgsql;

namespace ClimaPulse.Backend.Tests;

/// <summary>
/// The climapulse_test database for tests marked [Trait("Category", "Database")].
/// Skipped in the normal run: dotnet test --filter Category!=Database.
/// Connection string from CLIMAPULSE_TEST_DB_CONNECTION.
/// </summary>
public sealed class TestDatabase
{
    public string ConnectionString { get; }

    public TestDatabase()
    {
        ConnectionString = Environment.GetEnvironmentVariable("CLIMAPULSE_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set CLIMAPULSE_TEST_DB_CONNECTION to run database tests, or run with --filter Category!=Database.");

        // Reset wipes every table, so never let it touch the real database.
        var database = new NpgsqlConnectionStringBuilder(ConnectionString).Database;
        if (database is null || !database.EndsWith("_test", StringComparison.Ordinal))
            throw new InvalidOperationException("CLIMAPULSE_TEST_DB_CONNECTION must point to a database whose name ends in _test.");

        DatabaseInitializer.EnsureCreated(ConnectionString, FindSchemaSql());
    }

    /// <summary>Empties every table and restarts the ids, so each test starts clean.</summary>
    public void Reset()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "TRUNCATE AnomalyEvent, Advisory, Baseline, HistoricalObservation, Watchlist, City, UserProfile RESTART IDENTITY CASCADE";
        command.ExecuteNonQuery();
    }

    // Walks up from the test output folder to the repository root.
    private static string FindSchemaSql()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "backend", "Database", "schema.sql");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("backend/Database/schema.sql not found above the test folder.");
    }
}

/// <summary>Database test classes share one TestDatabase and run one after another.</summary>
[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<TestDatabase>
{
}
