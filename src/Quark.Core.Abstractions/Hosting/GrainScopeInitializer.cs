namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Configures a grain's per-call dependency injection scope after Quark binds the grain identity
///     and before the behavior instance is resolved from that scope.
/// </summary>
public delegate ValueTask GrainScopeInitializer(
    ICallContext ctx,
    IServiceProvider scopedProvider,
    CancellationToken cancellationToken);
