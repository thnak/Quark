namespace Quark.Persistence.Abstractions;

/// <summary>
///     Marks a grain constructor parameter as a named, provider-backed persistent state slot.
///     Orleans-compatible: <c>[PersistentState("stateName")]</c> or
///     <c>[PersistentState("stateName", "providerName")]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class PersistentStateAttribute : Attribute
{
    /// <summary>Initialises the attribute with a state name and optional storage provider name.</summary>
    public PersistentStateAttribute(string stateName, string storageName = "Default")
    {
        StateName = stateName;
        StorageName = storageName;
    }

    /// <summary>The logical name of this state slot (e.g. <c>"profile"</c>, <c>"balance"</c>).</summary>
    public string StateName { get; }

    /// <summary>
    ///     The name of the <see cref="IGrainStorage" /> provider to use.
    ///     Defaults to <c>"Default"</c> (the unnamed provider registered via
    ///     <c>AddInMemoryGrainStorage()</c> / <c>AddRedisGrainStorage()</c>).
    /// </summary>
    public string StorageName { get; }
}
