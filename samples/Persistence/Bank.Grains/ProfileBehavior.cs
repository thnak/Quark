using Bank.GrainInterfaces;
using Quark.Core.Abstractions.Grains;
using Quark.Persistence.Abstractions;

namespace Bank.Grains;

/// <summary>
///     Pattern 2 — <b>Named persistent state (Orleans-compatible)</b>.
///     <para>
///         The constructor injects a named state slot with <c>[PersistentState("profile")]</c>.
///         Quark resolves an <see cref="IPersistentState{TState}" /> backed by the <c>"Default"</c>
///         storage provider. Unlike persistent activation memory — which is owned by the long-lived
///         shell and cached across calls — a named state slot is a thin, per-call handle over a
///         storage record: you <c>ReadStateAsync</c> when you need the latest value and
///         <c>WriteStateAsync</c> after mutating it. <c>RecordExists</c> tells you whether anything
///         has been written yet.
///     </para>
/// </summary>
public sealed class ProfileBehavior : IGrainBehavior, IProfileGrain
{
    private readonly IPersistentState<ProfileState> _profile;

    public ProfileBehavior([PersistentState("profile")] IPersistentState<ProfileState> profile)
        => _profile = profile;

    public async Task UpdateAsync(string displayName, string email)
    {
        _profile.State.DisplayName = displayName;
        _profile.State.Email = email;
        _profile.State.UpdatedAt = DateTimeOffset.UtcNow;
        await _profile.WriteStateAsync();
    }

    public async Task<string> DescribeAsync()
    {
        await _profile.ReadStateAsync();
        if (!_profile.RecordExists)
            return "(no profile saved yet — use 'profile <name> <email>')";

        ProfileState p = _profile.State;
        return $"{p.DisplayName} <{p.Email}> (updated {p.UpdatedAt:u})";
    }
}
