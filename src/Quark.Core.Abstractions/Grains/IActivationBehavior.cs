namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Opts a grain behavior into <b>per-activation</b> lifetime instead of the default per-call model.
///     One instance is constructed when the activation is created, reused for every call on that
///     activation, and disposed on deactivation — versus <see cref="IGrainBehavior" />, which constructs
///     (and discards) a fresh instance for every grain method call.
/// </summary>
/// <remarks>
///     <para>
///         Because the instance lives for the whole activation, it may hold mutable per-activation state
///         directly in instance fields — they persist across calls, the way an Orleans grain's fields do.
///         The <c>QRK0020</c>/<c>QRK0021</c> analyzer rules (which forbid mutable state on the per-call
///         model) therefore do not apply here. Mutable <i>static</i> state (<c>QRK0022</c>) is still
///         shared across every activation on the silo and remains disallowed.
///     </para>
///     <para>
///         Constructor dependencies are resolved once from a single activation-lifetime service scope,
///         eliminating the per-call scope creation and DI resolution the default model pays on every
///         call. Per-call context (for example the idempotency key exposed through
///         <see cref="Quark.Core.Abstractions.Hosting.ICallContext" />) is still refreshed per call.
///     </para>
///     <para>
///         <b>Not yet supported on <c>[Reentrant]</c> grains.</b> A shared instance under concurrent
///         (interleaved) turns needs a per-turn call-context strategy this opt-in does not yet provide;
///         activating a reentrant behavior that also implements this interface throws.
///     </para>
/// </remarks>
public interface IActivationBehavior : IGrainBehavior;
