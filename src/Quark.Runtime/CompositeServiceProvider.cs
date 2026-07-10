namespace Quark.Runtime;

/// <summary>
///     IServiceProvider that resolves from a primary provider first, falling back to a secondary
///     provider when the primary has no registration for the requested type. Used to compose Quark's
///     own per-call scope with a developer-supplied, cached user-service provider
///     (see IGrainUserServiceProviderFactory) without letting the user provider ever satisfy a
///     Quark-owned service type.
/// </summary>
internal sealed class CompositeServiceProvider(IServiceProvider primary, IServiceProvider secondary) : IServiceProvider
{
    public object? GetService(Type serviceType) => primary.GetService(serviceType) ?? secondary.GetService(serviceType);
}
