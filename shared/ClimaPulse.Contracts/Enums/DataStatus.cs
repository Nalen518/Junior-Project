namespace ClimaPulse.Enums;

/// <summary>
/// Whether a metric reading could be scored (contract 6.4). Anything but Ok means
/// no z-score and no tier, so the UI never shows a fake Normal.
/// </summary>
public enum DataStatus
{
    Ok,
    NoBaseline,
    ZeroVariance,
    NoData
}
