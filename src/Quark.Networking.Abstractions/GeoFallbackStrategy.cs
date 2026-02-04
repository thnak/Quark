namespace Quark.Networking.Abstractions;

/// <summary>
///     Phase 8.3: Fallback strategy for geo-aware routing when preferred location is unavailable.
/// </summary>
public enum GeoFallbackStrategy
{
    /// <summary>
    ///     Fall back to any available silo (no geographic preference).
    /// </summary>
    Any,

    /// <summary>
    ///     Fall back to the nearest region based on configured latency matrix.
    /// </summary>
    NearestRegion,

    /// <summary>
    ///     Fail the request if no silos available in preferred region.
    /// </summary>
    Fail
}