namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Opt-in, compile-time-discovered factory that supplies the IServiceProvider used to resolve a
///     behavior's own (non-Quark) constructor-injected services. Implemented directly on the behavior
///     class. Called once per grain type at silo startup; the returned provider is cached and shared by
///     every activation of that type for the process lifetime.
/// </summary>
public interface IGrainUserServiceProviderFactory
{
    /// <param name="rootServices">
    ///     The ordinary root IServiceProvider built from the silo's registered services (silo.Services).
    ///     Use this to pull already-registered user singletons, or return it unchanged if the developer's
    ///     services are already cheap/stateless to resolve from it directly.
    /// </param>
    static abstract IServiceProvider CreateUserServiceProvider(IServiceProvider rootServices);
}
