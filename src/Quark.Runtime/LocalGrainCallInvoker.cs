using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Placement;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime.Clustering;
using Quark.Runtime.StatelessWorker;
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
    private readonly ILogger<LocalGrainCallInvoker> _logger;
    private readonly ILogger<GrainActivation> _activationLogger;
    private readonly ObserverRegistry? _observerRegistry;
    private readonly TcpClientObserverTable? _tcpObserverTable;
    private readonly IServiceProvider _services;
    private readonly SiloAddress _siloAddress;
    private readonly ISiloRouter? _siloRouter;
    private readonly IGrainTypeRegistry _typeRegistry;
    private readonly IPlacementDirector? _placementDirector;
    private readonly IClusterMembershipSnapshot? _membershipSnapshot;
    private readonly int _mailboxCapacity;
    private readonly MailboxFullMode _mailboxFullMode;
    private readonly IRequestDedupStore? _dedupStore;
    private readonly StatelessWorkerRouter? _statelessWorkerRouter;

    internal LocalGrainCallInvoker(
        GrainActivationTable activationTable,
        IGrainTypeRegistry typeRegistry,
        IGrainDirectory directory,
        IServiceProvider services,
        IOptions<SiloRuntimeOptions> options,
        ILogger<LocalGrainCallInvoker> logger,
        ILogger<GrainActivation> activationLogger,
        ObserverRegistry? observerRegistry = null,
        ICopierProvider? copierProvider = null,
        ISiloRouter? siloRouter = null,
        TcpClientObserverTable? tcpObserverTable = null,
        IQuarkDiagnosticListener? diagnostics = null,
        IPlacementDirector? placementDirector = null,
        IClusterMembershipSnapshot? membershipSnapshot = null,
        IRequestDedupStore? dedupStore = null,
        StatelessWorkerRouter? statelessWorkerRouter = null)
    {
        _activationTable = activationTable;
        _typeRegistry = typeRegistry;
        _directory = directory;
        _services = services;
        _siloAddress = options.Value.SiloAddress;
        _mailboxCapacity = options.Value.MailboxCapacity;
        _mailboxFullMode = options.Value.MailboxFullMode;
        _logger = logger;
        _activationLogger = activationLogger;
        _observerRegistry = observerRegistry;
        _copierProvider = copierProvider;
        _siloRouter = siloRouter;
        _tcpObserverTable = tcpObserverTable;
        _diagnostics = diagnostics ?? NullDiagnosticListener.Instance;
        _placementDirector = placementDirector;
        _membershipSnapshot = membershipSnapshot;
        _dedupStore = dedupStore;
        _statelessWorkerRouter = statelessWorkerRouter;
    }

    /// <inheritdoc />
    public async ValueTask<TResult> InvokeAsync<TInvokable, TResult>(
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
        {
            return await remote.InvokeAsync<TInvokable, TResult>(grainId, invokable, cancellationToken)
                .ConfigureAwait(false);
        }

        // G2: consult PlacementDirector on a directory miss when silo-to-silo transport is active.
        if (_siloRouter is not null && _placementDirector is not null && _membershipSnapshot is not null
            && !_directory.TryLookup(grainId, out _))
        {
            IGrainCallInvoker? placementRemote = TryPlaceRemote(grainId);
            if (placementRemote is not null)
            {
                return await placementRemote.InvokeAsync<TInvokable, TResult>(grainId, invokable, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        _diagnostics.OnInvocationStart(new InvocationStartEvent(grainId, invokable.MethodId, isObserver: false));
        long startedAt = Stopwatch.GetTimestamp();

        GrainId target = grainId;
        StatelessWorkerLease lease = default;
        bool pooled = false;
        if (_statelessWorkerRouter is not null
            && _statelessWorkerRouter.TryGetPolicy(grainId.Type, out StatelessWorkerPoolPolicy policy))
        {
            pooled = true;
            lease = await _statelessWorkerRouter.AcquireAsync(grainId, policy, cancellationToken)
                .ConfigureAwait(false);
            target = lease.WorkerId;
        }

        TResult result = default!;
        try
        {
            GrainActivation activation = await GetOrActivateAsync(target, cancellationToken).ConfigureAwait(false);

            await activation.PostAsync(async () =>
            {
                using IServiceScope scope = _services.CreateScope();
                IServiceProvider sp = scope.ServiceProvider;
                IGrainBehavior behavior = await GrainScopeBinder.BindAndResolveAsync(sp, activation, cancellationToken).ConfigureAwait(false);
                TResult r = await invokable.Invoke(behavior).ConfigureAwait(false);
                if (_copierProvider?.TryGetCopier<TResult>() is { } copier)
                {
                    r = copier.DeepCopy(r, new CopyContext());
                }
                result = r;
            }).ConfigureAwait(false);

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: false, elapsed, null));
            QuarkInstruments.GrainInvocations.Add(1, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            QuarkInstruments.GrainInvocationDuration.Record(elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: false, elapsed, ex));
            QuarkInstruments.GrainInvocationErrors.Add(1, new KeyValuePair<string, object?>("grain_type", grainId.Type.Value));
            throw;
        }
        finally
        {
            if (pooled) lease.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask InvokeVoidAsync<TInvokable>(
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

        // G2: consult PlacementDirector on a directory miss when silo-to-silo transport is active.
        if (_siloRouter is not null && _placementDirector is not null && _membershipSnapshot is not null
            && !_directory.TryLookup(grainId, out _))
        {
            IGrainCallInvoker? placementRemote = TryPlaceRemote(grainId);
            if (placementRemote is not null)
            {
                await placementRemote.InvokeVoidAsync(grainId, invokable, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
        }

        _diagnostics.OnInvocationStart(new InvocationStartEvent(grainId, invokable.MethodId, isObserver: false));
        long startedAt = Stopwatch.GetTimestamp();

        GrainId target = grainId;
        StatelessWorkerLease lease = default;
        bool pooled = false;
        if (_statelessWorkerRouter is not null
            && _statelessWorkerRouter.TryGetPolicy(grainId.Type, out StatelessWorkerPoolPolicy policy))
        {
            pooled = true;
            lease = await _statelessWorkerRouter.AcquireAsync(grainId, policy, cancellationToken)
                .ConfigureAwait(false);
            target = lease.WorkerId;
        }

        try
        {
            GrainActivation activation = await GetOrActivateAsync(target, cancellationToken).ConfigureAwait(false);

            await activation.PostAsync(async () =>
            {
                using IServiceScope scope = _services.CreateScope();
                IServiceProvider sp = scope.ServiceProvider;
                IGrainBehavior behavior = await GrainScopeBinder.BindAndResolveAsync(sp, activation, cancellationToken).ConfigureAwait(false);
                await invokable.Invoke(behavior).ConfigureAwait(false);
            }).ConfigureAwait(false);

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
        finally
        {
            if (pooled) lease.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask InvokeObserverAsync<TInvokable>(
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
        bool dispatched = false;

        try
        {
            if (_observerRegistry?.TryGet(grainId, out ObserverRegistry.ObserverEntry entry) == true)
            {
                dispatched = true;
                await invokable.Invoke(entry.Target).ConfigureAwait(false);
                _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: true, Stopwatch.GetElapsedTime(startedAt), null));
                _diagnostics.OnObserverInvoked(new ObserverInvokedEvent(grainId, invokable.MethodId, true, null));
                return;
            }

            if (_tcpObserverTable?.TryGet(grainId, out Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task>? writeBack) == true)
            {
                dispatched = true;
                var buffer = new System.Buffers.ArrayBufferWriter<byte>();
                var writer = new Quark.Serialization.Abstractions.Buffers.CodecWriter(buffer);
                invokable.Serialize(ref writer);
                await writeBack!(invokable.MethodId, buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
                _diagnostics.OnInvocationEnd(new InvocationEndEvent(grainId, invokable.MethodId, isObserver: true, Stopwatch.GetElapsedTime(startedAt), null));
                _diagnostics.OnObserverInvoked(new ObserverInvokedEvent(grainId, invokable.MethodId, true, null));
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
            if (dispatched)
            {
                _diagnostics.OnObserverInvoked(new ObserverInvokedEvent(grainId, invokable.MethodId, false, ex));
            }
            throw;
        }
    }

    // -----------------------------------------------------------------------

    internal Task EnsureActivatedAsync(GrainId grainId, CancellationToken cancellationToken = default)
        => GetOrActivateAsync(grainId, cancellationToken);

    private IGrainCallInvoker? TryPlaceRemote(GrainId grainId)
    {
        if (!_typeRegistry.TryGetGrainClass(grainId.Type, out Type? behaviorClass) || behaviorClass is null)
            return null;

        SiloAddress target = _placementDirector!.SelectActivationSilo(
            grainId, behaviorClass, _siloAddress, _membershipSnapshot!.ActiveSilos);

        if (target == _siloAddress)
            return null;

        if (!_siloRouter!.TryGetInvoker(target, out IGrainCallInvoker? invoker))
            return null;

        // Cache the placement decision locally so subsequent calls hit the fast TryRouteRemote path.
        _directory.TryRegister(grainId, target, out _);
        return invoker;
    }

    private IGrainCallInvoker? TryRouteRemote(GrainId grainId)
    {
        if (_siloRouter is null)
        {
            return null;
        }

        if (!_directory.TryLookup(grainId, out SiloAddress owner))
        {
            return null;
        }

        if (owner == _siloAddress)
        {
            return null;
        }

        if (_siloRouter.TryGetInvoker(owner, out IGrainCallInvoker? remote))
        {
            return remote;
        }

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
        var activation = new GrainActivation(grainId, grainId.Type, isReentrant, _services, _activationLogger,
            _diagnostics, _mailboxCapacity, _mailboxFullMode);

        // Resolve behavior (ctor registers eager factories), init eager holders with the activation
        // scope's SP, then call OnActivateAsync if IActivationLifecycle. Single scope — no double construction.
        await activation.PostAsync(async () =>
        {
            try
            {
                await activation.RunActivationAsync(ct).ConfigureAwait(false);
                activation.MarkActive();
            }
            catch
            {
                activation.MarkActive(); // still mark active so DisposeAsync can deactivate it
                throw;
            }
        }).ConfigureAwait(false);

        IRequestDedupStore? dedupStore = _dedupStore;
        StatelessWorkerRouter? router = _statelessWorkerRouter;
        bool isWorker = router?.IsWorkerId(grainId) == true;

        activation.SetOnDeactivated(() =>
        {
            _activationTable.Remove(grainId);
            dedupStore?.EvictGrain(grainId);
            if (isWorker) router!.OnWorkerDeactivated(grainId);
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
