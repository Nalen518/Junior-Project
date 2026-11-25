using ClimaPulse.Models;
using Npgsql;

namespace ClimaPulse.Repositories;

/// <summary>
/// Reads and writes the City table. Remove throws PostgresException while the city
/// still has anomaly events, because the ledger must keep its history.
/// </summary>
public class CityRepository : PostgresRepositoryBase, IRepository<City>
{
    private const string Columns =
        "city_id, name, country_code, admin_region, latitude, longitude, timezone, elevation_m, open_meteo_ref";

    public CityRepository(string connectionString) : base(connectionString)
    {
    }

    public void Add(City item)
    {
        item.Id = InsertAndGetId(
            @"INSERT INTO City (name, country_code, admin_region, latitude, longitude, timezone, elevation_m, open_meteo_ref)
              VALUES (@name, @country, @region, @lat, @lon, @tz, @elev, @ref)",
            cmd =>
            {
                AddParam(cmd, "@name", item.Name);
                AddParam(cmd, "@country", item.CountryCode);
                AddParam(cmd, "@region", item.AdminRegion);
                AddParam(cmd, "@lat", item.Latitude);
                AddParam(cmd, "@lon", item.Longitude);
                AddParam(cmd, "@tz", item.Timezone);
                AddParam(cmd, "@elev", item.ElevationM);
                AddParam(cmd, "@ref", item.OpenMeteoRef);
            });
    }

    public void Remove(City item) =>
        ExecuteNonQuery("DELETE FROM City WHERE city_id = @id", cmd => AddParam(cmd, "@id", item.Id));

    public List<City> GetAll() =>
        ReadList($"SELECT {Columns} FROM City ORDER BY name", Map);

    public List<City> Query(Func<City, bool> filter) =>
        GetAll().Where(filter).ToList();

    public City? GetById(int id) =>
        ReadList($"SELECT {Columns} FROM City WHERE city_id = @id", Map,
            cmd => AddParam(cmd, "@id", id)).FirstOrDefault();

    /// <summary>
    /// The stored city with this Open-Meteo geocoding id, or null. First duplicate check
    /// for D7, because City has no unique key.
    /// </summary>
    public City? FindByRef(string openMeteoRef) =>
        ReadList($"SELECT {Columns} FROM City WHERE open_meteo_ref = @ref LIMIT 1", Map,
            cmd => AddParam(cmd, "@ref", openMeteoRef)).FirstOrDefault();

    /// <summary>
    /// The stored city with the same name and coordinates rounded to 2 decimals (about 1 km),
    /// or null. Fallback duplicate check for D7 when a city has no Open-Meteo id.
    /// Rounding happens in SQL on both sides so the two values round the same way.
    /// </summary>
    public City? FindByPlace(string name, double latitude, double longitude) =>
        ReadList(
            $@"SELECT {Columns} FROM City
               WHERE name = @name
                 AND round(latitude::numeric, 2) = round(@lat::numeric, 2)
                 AND round(longitude::numeric, 2) = round(@lon::numeric, 2)
               ORDER BY city_id
               LIMIT 1",
            Map,
            cmd =>
            {
                AddParam(cmd, "@name", name);
                AddParam(cmd, "@lat", latitude);
                AddParam(cmd, "@lon", longitude);
            }).FirstOrDefault();

    /// <summary>Cities on one profile's watchlist. Replaces the old IsPinned flag.</summary>
    public List<City> GetWatchedBy(int profileId) =>
        ReadList(
            @"SELECT c.city_id, c.name, c.country_code, c.admin_region, c.latitude, c.longitude,
                     c.timezone, c.elevation_m, c.open_meteo_ref
              FROM City c
              JOIN Watchlist w ON w.city_id = c.city_id
              WHERE w.profile_id = @profile
              ORDER BY c.name",
            Map,
            cmd => AddParam(cmd, "@profile", profileId));

    private static City Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Name = r.GetString(1),
        CountryCode = r.GetString(2),
        AdminRegion = NullableString(r, 3),
        Latitude = r.GetDouble(4),
        Longitude = r.GetDouble(5),
        Timezone = r.GetString(6),
        ElevationM = NullableDouble(r, 7),
        OpenMeteoRef = NullableString(r, 8)
    };
}
