using Quark.Core.Abstractions.Grains;

namespace Bank.GrainInterfaces;

/// <summary>
///     The account holder's profile, stored with an Orleans-compatible named state slot.
///     The behavior injects <c>[PersistentState("profile")] IPersistentState&lt;ProfileState&gt;</c>
///     and calls <c>ReadStateAsync</c>/<c>WriteStateAsync</c> explicitly.
/// </summary>
public interface IProfileGrain : IGrainWithStringKey
{
    /// <summary>Sets the display name and email, then writes the slot to storage.</summary>
    Task UpdateAsync(string displayName, string email);

    /// <summary>Returns a human-readable description, or a hint if no profile has been saved yet.</summary>
    Task<string> DescribeAsync();
}
