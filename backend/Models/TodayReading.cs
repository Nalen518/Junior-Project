namespace ClimaPulse.Models;

/// <summary>
/// Today's daily values for one city from the Open-Meteo forecast API (SRD FR-06).
/// These are the values that get scored, not the current instant (contract 14.2).
/// A null value means Open-Meteo sent no data for that metric (DataStatus.NoData).
/// </summary>
public sealed record TodayReading(
    DateOnly LocalDate,
    double? Temperature,
    double? Precipitation,
    double? WindSpeed);
