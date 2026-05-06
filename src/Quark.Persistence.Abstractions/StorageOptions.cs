namespace Quark.Persistence.Abstractions;

/// <summary>
/// Options controlling grain state storage behavior.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>The default logical state name used by persistent grains.</summary>
    public const string DefaultStateName = "Default";

    /// <summary>
    /// Provider name reserved for the default storage provider.
    /// This mirrors Orleans' default grain storage concept.
    /// </summary>
    public string DefaultProviderName { get; set; } = "Default";
}