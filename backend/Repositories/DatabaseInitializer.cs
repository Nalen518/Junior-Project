using Npgsql;

namespace ClimaPulse.Repositories;

/// <summary>Creates the database tables from Database/schema.sql on first run.</summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Runs schema.sql when the database has no City table yet. Safe to call on every
    /// start: an existing database is left untouched. Unlike SQLite, PostgreSQL does not
    /// create the database itself, so run `createdb climapulse` once before the first start.
    /// </summary>
    public static void EnsureCreated(string connectionString, string schemaSqlPath)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using (var check = connection.CreateCommand())
        {
            // PostgreSQL folds unquoted identifiers to lower case, so the table is "city".
            check.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables " +
                "WHERE table_schema = current_schema() AND table_name = 'city';";
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                return;
        }

        using var create = connection.CreateCommand();
        create.CommandText = File.ReadAllText(schemaSqlPath);
        create.ExecuteNonQuery();
    }
}
