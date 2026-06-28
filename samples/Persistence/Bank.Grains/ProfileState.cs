using Quark.Serialization.Abstractions.Attributes;

namespace Bank.Grains;

/// <summary>
///     Durable state for <see cref="ProfileBehavior" />, persisted through a named
///     <c>IPersistentState&lt;ProfileState&gt;</c> slot.
/// </summary>
[GenerateSerializer]
public sealed class ProfileState
{
    [Id(0)] public string DisplayName { get; set; } = "";
    [Id(1)] public string Email { get; set; } = "";
    [Id(2)] public DateTimeOffset UpdatedAt { get; set; }
}
