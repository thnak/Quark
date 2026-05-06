using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// In-process <see cref="IGrainCallInvoker"/> that activates grains locally and routes
/// method calls through each grain's sequential scheduler.
/// </summary>
public sealed class LocalGrainCallInvoker : IGrainCallInvoker
{
    private readonly GrainActivationTable _activationTable;
    private readonly IGrainActivator _activator;
    private readonly IGrainTypeRegistry _typeRegistry;
    private readonly IGrainDirectory _directory;
    private readonly IGrainMethodInvokerRegistry _methodInvokerRegistry;
    private readonly IGrainFactory _grainFactory;
    private readonly IServiceProvider _services;
    private readonly SiloAddress _siloAddress;
    private readonly ILogger<LocalGrainCallInvoker> _logger;
    private readonly ILogger<GrainActivation> _activationLogger;

    /// <summary>Initialises the local grain invoker.</summary>
    public LocalGrainCallInvoker(
        GrainActivationTable activationTable,
        IGrainActivator activator,
        IGrainTypeRegistry typeRegistry,
        IGrainDirectory directory,
        IGrainMethodInvokerRegistry methodInvokerRegistry,
        IGrainFactory grainFactory,
        IServiceProvider services,
        IOptions<SiloRuntimeOptions> options,
        ILogger<LocalGrainCallInvoker> logger,
        ILogger<GrainActivation> activationLogger)
    {
        _activationTable = activationTable;
        _activator = activator;
        _typeRegistry = typeRegistry;
        _directory = directory;
        _methodInvokerRegistry = methodInvokerRegistry;
        _grainFactory = grainFactory;
        _services = services;
        _siloAddress = options.Value.SiloAddress;
        _logger = logger;
        _activationLogger = activationLogger;
    }

    /// <inheritdoc/>
    public async Task<object?> InvokeAsync(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default)
    {
        GrainActivation activation = await GetOrActivateAsync(grainId, cancellationToken).ConfigureAwait(false);

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await activation.PostAsync(async () =>
        {
            try
            {
                IGrainMethodInvoker invoker = _methodInvokerRegistry.GetInvoker(activation.Grain.GetType());
                object? result = await invoker.Invoke(activation.Grain, methodId, arguments).ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }).ConfigureAwait(false);

        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TResult> InvokeAsync<TResult>(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default)
    {
        object? result = await InvokeAsync(grainId, methodId, arguments, cancellationToken).ConfigureAwait(false);
        return result is TResult typed ? typed : (TResult)result!;
    }

    /// <inheritdoc/>
    public async Task InvokeVoidAsync(
        GrainId grainId,
        uint methodId,
        object?[]? arguments = null,
        CancellationToken cancellationToken = default)
    {
        _ = await InvokeAsync(grainId, methodId, arguments, cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------

    private Task<GrainActivation> GetOrActivateAsync(GrainId grainId, CancellationToken ct)
    {
        return _activationTable.GetOrCreateAsync(grainId, () => CreateActivationAsync(grainId, ct));
    }

    private async Task<GrainActivation> CreateActivationAsync(GrainId grainId, CancellationToken ct)
    {
        if (!_typeRegistry.TryGetGrainClass(grainId.Type, out Type? grainClass) || grainClass is null)
        {
            throw new InvalidOperationException(
                $"No grain class registered for grain type '{grainId.Type.Value}'. " +
                "Ensure the grain is registered with AddGrain<TGrain>().");
        }

        Grain grain = _activator.CreateInstance(grainId.Type);
        var context = new GrainContext(grainId, _grainFactory, _services);
        var activation = new GrainActivation(grain, context, _activationLogger);

        await context.ActivateAsync(grain, ct).ConfigureAwait(false);

        _directory.TryRegister(grainId, _siloAddress, out _);

        _logger.LogDebug("Activated grain {GrainId} as {GrainClass}", grainId, grainClass.Name);

        return activation;
    }
}
