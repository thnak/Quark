namespace Quark.Runtime;

/// <summary>
///     IServiceProvider that resolves from a primary provider first, falling back to a secondary
///     provider when the primary has no registration for the requested type. Used to compose Quark's
///     own per-call scope with a developer-supplied, cached user-service provider
///     (see IGrainUserServiceProviderFactory) without letting the user provider ever satisfy a
///     Quark-owned service type.
///     Known v1 limitation: does not merge <see cref="IEnumerable{T}"/> registrations across
///     primary/secondary — MS.DI always returns a non-null collection for <c>IEnumerable&lt;T&gt;</c>,
///     so primary's (possibly empty) result always wins and secondary's registrations for that type are
///     never consulted. If a behavior needs multiple registered implementations of an interface,
///     aggregate them inside <c>CreateUserServiceProvider</c> itself rather than relying on
///     cross-boundary <c>IEnumerable&lt;T&gt;</c> resolution.
/// </summary>
internal sealed class CompositeServiceProvider(IServiceProvider primary, IServiceProvider secondary) : IServiceProvider
{
    public object? GetService(Type serviceType) => primary.GetService(serviceType) ?? secondary.GetService(serviceType);
}
