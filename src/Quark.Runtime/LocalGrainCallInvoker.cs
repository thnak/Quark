using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime.Clustering;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     In-process <see cref="IGrainCallInvoker" /> that activates grains locally and routes
///     method calls through each grain's sequential scheduler.
///     Creates a fresh <see cref="IServiceScope" /> per call; scoped DI services are disposed
///     when the call returns.
/// </summary>
public sealed class LocalGrainCallInvoker : IGrainCallInvoker
{
    private readonly GrainActivationTable _activationTable;
    private readonly ICopierProvider? _copierProvider;
    private readonly IQuarkDiagnosticListener _diagnostics;
    private readonly IGrainDirectory _directory;
    private readonly Lazy<IGrainFactory> _grainFactory;
    private readonly ILogger<LocalGrainCallInvoker> _logger;
    private readonly ILogger<GrainActivation> _activationLogger;
    private readonly ObserverRegistry? _observerRegistry;
    private readonly TcpClientObserverTable? _tcpObserverTable;
    private readonly IServiceProvider _services;
    private readonly SiloAddress _siloAddress;
    private readonly ISiloRouter? _siloRouter;
    private readonly IGrainTypeRegistry _typeRegistry;

    public LocalGrainCallInvoker(
        GrainActivationTable activationTable,
        IGrainTypeRegistry typeRegistry,
        IGrainDirectory directory,
        IServiceProvider services,
        IOptions<SiloRuntimeOptions> options,
        ILogger<LocalGrainCallInvoker> logger,
        ILogger<GrainActivation> activationLogger,
        ObserverRegistry? observerRegistry = null,
        IGrainFactory? grainFactory = null,
        ICopierProvider? copierProvider = null,
        ISiloRouter? siloRouter = null,
        TcpClientObserverTable? tcpObserverTable = null,
        IQuarkDiagnosticListener? diagnostics = null)
    {
        _activationTable = activationTable;
        _typeRegistry = typeRegistry;
        _directory = directory;
        _services = services;
        _grainFactory = grainFactory is not null
            ? new Lazy<IGrainFactory>(() => grainFactory)
            : new Lazy<IGrainFactory>(services.GetRequiredService<IGrainFactory>);
        _siloAddress = options.Value.SiloAddress;
        _logger = logger;
        _activationLogger = activationLogger;
        _observerRegistry = observerRegistry;
        _copierProvider = copierProvider;
        _siloRouter = siloRouter;
        _tcpObserverTable = tcpObserverTable;
        _diagnostics = diagnostics ?? NullDiagnosticListener.Instance;
    }

    /// <inheritdoc />
    public async Task<TResult> InvokeAsync<TInvokable, TResult>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainInvokable<TResult>
    {
        using Activity? activity = QuarkInstruments.ActivitySource.StartActivity("grain.invoke");
        activity?.SetTag("grain.type", grainId.Type.Value);
        activity?.SetTag("grain.key", grainId.Key);
        activity?.SetTag("grain.method_id", invokable.MethodId);

        IGrainCallInvoker? remote = TryRouteRemote(grainId);
        if (remote is not null)
            return await remote.InvokeAsync<TInvokable, TResult>(grainId, invokable, cancellationToken)
                .ConfigureAwait(false);

        _diagnostics.OnInvocationStart(new InvocationStartEvent(grainId, invokable.MethodId, isObserver: false));
        long startedAt = Stopwatch.GetTimestamp();

        GrainActivation activation = await GetOrActivateAsync(grainId, cancellationToken).ConfigureAwait(false);
        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        await activation.PostAsync(async () =>
        {
            using IServiceScope scope = _services.CreateScope();
            IServiceProvider sp = scope.ServiceProvider;
            try
            {
                BindScope(sp, activation);
                IGrainBehavior behavior = sp.GetRequiredService<IBehaviorResolver>().Resolve(activation.GrainType);
                TResult result = await invokable.Invoke(behavior).ConfigureAwait(false);
                if (_copierProvider?.TryGetCopier<TResult>() is { } copier)
                    result = copier.DeepCopy(result, new CopyContext());
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }).ConfigureAwait(false);

        try
        {
            TResult r = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: false, elapsed, null));
            QuarkInstruments.GrainInvocations.Add(1, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            QuarkInstruments.GrainInvocationDuration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            return r;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: false, elapsed, ex));
            QuarkInstruments.GrainInvocationErrors.Add(1, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InvokeVoidAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainVoidInvokable
    {
        using Activity? activity = QuarkInstruments.ActivitySource.StartActivity("grain.invoke");
        activity?.SetTag("grain.type", grainId.Type.Value);
        activity?.SetTag("grain.key", grainId.Key);
        activity?.SetTag("grain.method_id", invokable.MethodId);

        IGrainCallInvoker? remote = TryRouteRemote(grainId);
        if (remote is not null)
        {
            await remote.InvokeVoidAsync(grainId, invokable, cancellationToken).ConfigureAwait(false);
            return;
        }

        _diagnostics.OnInvocationStart(new InvocationStartEvent(grainId, invokable.MethodId, isObserver: false));
        long startedAt = Stopwatch.GetTimestamp();

        GrainActivation activation = await GetOrActivateAsync(grainId, cancellationToken).ConfigureAwait(false);
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await activation.PostAsync(async () =>
        {
            using IServiceScope scope = _services.CreateScope();
            IServiceProvider sp = scope.ServiceProvider;
            try
            {
                BindScope(sp, activation);
                IGrainBehavior behavior = sp.GetRequiredService<IBehaviorResolver>().Resolve(activation.GrainType);
                await invokable.Invoke(behavior).ConfigureAwait(false);
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }).ConfigureAwait(false);

        try
        {
            await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: false, elapsed, null));
            QuarkInstruments.GrainInvocations.Add(1, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            QuarkInstruments.GrainInvocationDuration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: false, elapsed, ex));
            QuarkInstruments.GrainInvocationErrors.Add(1, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InvokeObserverAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IObserverVoidInvokable
    {
        using Activity? activity = QuarkInstruments.ActivitySource.StartActivity("observer.invoke");
        activity?.SetTag("grain.type", grainId.Type.Value);
        activity?.SetTag("grain.key", grainId.Key);
        activity?.SetTag("grain.method_id", invokable.MethodId);

        _diagnostics.OnInvocationStart(new InvocationStartEvent(grainId, invokable.MethodId, isObserver: true));
        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            if (_observerRegistry?.TryGet(grainId, out ObserverRegistry.ObserverEntry entry) == true)
            {
                await invokable.Invoke(entry.Target).ConfigureAwait(false);
                _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: true, Stopwatch.GetElapsedTime(startedAt), null));
                return;
            }

            if (_tcpObserverTable?.TryGet(grainId, out Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task>? writeBack) == true)
            {
                var buffer = new System.Buffers.ArrayBufferWriter<byte>();
                var writer = new Quark.Serialization.Abstractions.Buffers.CodecWriter(buffer);
                invokable.Serialize(ref writer);
                await writeBack!(invokable.MethodId, buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
                _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: true, Stopwatch.GetElapsedTime(startedAt), null));
                return;
            }

            throw new InvalidOperationException(
                $"Observer '{grainId}' not found in registry. " +
                "Ensure CreateObjectReference was called before invoking observer methods.");
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: true, Stopwatch.GetElapsedTime(startedAt), ex));
            throw;
        }
    }

    // -----------------------------------------------------------------------

    private static void BindScope(IServiceProvider sp, GrainActivation activation)
    {
        ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>()).Shell = activation;
        sp.GetRequiredService<ICallContextSetter>().Set(activation.GrainId);
    }

    private IGrainCallInvoker? TryRouteRemote(GrainId grainId)
    {
        if (_siloRouter is null) return null;
        if (!_directory.TryLookup(grainId, out SiloAddress owner)) return null;
        if (owner == _siloAddress) return null;
        if (_siloRouter.TryGetInvoker(owner, out IGrainCallInvoker? remote)) return remote;
        _directory.TryUnregister(grainId, owner);
        return null;
    }

    private async Task<GrainActivation> GetOrActivateAsync(GrainId grainId, CancellationToken ct)
    {
        try
        {
            return await _activationTable.GetOrCreateAsync(grainId, () => CreateActivationAsync(grainId, ct))
                .ConfigureAwait(false);
        }
        catch
        {
            _activationTable.RemoveIfFaulted(grainId);
            throw;
        }
    }

    private async Task<GrainActivation> CreateActivationAsync(GrainId grainId, CancellationToken ct)
    {
        if (!_typeRegistry.TryGetGrainClass(grainId.Type, out Type? behaviorType) || behaviorType is null)
        {
            throw new InvalidOperationException(
                $"No behavior registered for grain type '{grainId.Type.Value}'. " +
                "Ensure the grain is registered with AddGrainBehavior<TInterface, TBehavior>().");
        }

        string behaviorTypeName = behaviorType.Name;
        _diagnostics.OnGrainActivating(new GrainActivatingEvent(grainId, behaviorTypeName));
        long activationStart = Stopwatch.GetTimestamp();

        bool isReentrant = behaviorType.IsDefined(typeof(ReentrantAttribute), inherit: true);
        var activation = new GrainActivation(grainId, grainId.Type, isReentrant, _services, _activationLogger, _diagnostics);

        // Run OnActivateAsync lifecycle hook if the behavior implements IActivationLifecycle.
        await activation.PostAsync(async () =>
        {
            try
            {
                await activation.RunLifecycleHookAsync(
                    lifecycle => lifecycle.OnActivateAsync(ct)).ConfigureAwait(false);
                activation.MarkActive();
            }
            catch
            {
                activation.MarkActive(); // still mark active so DisposeAsync can deactivate it
                throw;
            }
        }).ConfigureAwait(false);

        activation.SetOnDeactivated(() =>
        {
            _activationTable.Remove(grainId);
            return Task.CompletedTask;
        });

        _directory.TryRegister(grainId, _siloAddress, out _);

        TimeSpan activationDuration = Stopwatch.GetElapsedTime(activationStart);
        _logger.LogDebug("Activated grain {GrainId} as {BehaviorType}", grainId, behaviorTypeName);
        _diagnostics.OnGrainActivated(new GrainActivatedEvent(grainId, behaviorTypeName, activationDuration));
        QuarkInstruments.GrainActivationsCreated.Add(1,
            new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
        QuarkInstruments.ActiveGrainActivations.Add(1,
            new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
        QuarkInstruments.GrainActivationDuration.Record(activationDuration.TotalMilliseconds,
            new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));

        return activation;
    }
}
