namespace Quark.Runtime;

/// <summary>
///     Mutable holder for the lazily-built Quark-only satellite IServiceProvider (see
///     SiloHostedService.ApplyUserServiceProviderFactoryRegistrations). Registered as a singleton so
///     GrainActivation can read it without a constructor signature change; null when no behavior in the
///     process has opted into IGrainUserServiceProviderFactory.
/// </summary>
internal sealed class QuarkOnlyServiceProviderHolder
{
    public IServiceProvider? Provider { get; set; }
}
