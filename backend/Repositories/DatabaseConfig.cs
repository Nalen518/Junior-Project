namespace ClimaPulse.Repositories;

/// <summary>
/// Where the PostgreSQL database lives. Reads CLIMAPULSE_DB_CONNECTION from the
/// environment and falls back to a local server. Note that .NET does not load .env
/// files by itself, so set the variable in the launch profile or load .env with a library.
/// </summary>
public static class DatabaseConfig
{
    /// <summary>Local default without a password; put the real password in your own .env.</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=climapulse;Username=postgres";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("CLIMAPULSE_DB_CONNECTION") ?? DefaultConnectionString;
}
