using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Placement;

namespace Quark.Runtime.StatelessWorker;

/// <summary>
///     Silo-wide singleton that manages stateless-worker pool lifecycle and routes calls
///     to synthetic worker activation identities.
///     <para>
///         A logical grain <c>L = (Type, key)</c> with <c>[StatelessWorker]</c> gets a pool of
///         up to <c>MaxLocalActivations</c> worker activations whose ids are
///         <c>W_i = (Type, key + SENTINEL + i)</c>.  The pool is created on first acquire and
///         removed when all slots are free after a worker deactivates.
///     </para>
/// </summary>
internal sealed class StatelessWorkerRouter
{
    private readonly ConcurrentDictionary<GrainId, StatelessWorkerPool> _pools = new();
    private readonly ConcurrentDictionary<GrainType, StatelessWorkerPoolPolicy?> _policyCache = new();
    private readonly IGrainTypeRegistry _typeRegistry;
    private readonly SiloRuntimeOptions _options;

    public StatelessWorkerRouter(IGrainTypeRegistry typeRegistry, IOptions<SiloRuntimeOptions> options)
    {
        _typeRegistry = typeRegistry;
        _options = options.Value;
    }

    /// <summary>
    ///     Looks up and caches the pool policy for <paramref name="grainType"/>.
    ///     Returns <c>false</c> when the type is not a stateless-worker grain.
    /// </summary>
    public bool TryGetPolicy(GrainType grainType, out StatelessWorkerPoolPolicy policy)
    {
        StatelessWorkerPoolPolicy? cached = _policyCache.GetOrAdd(grainType, ResolvePolicy);
        if (cached is null)
        {
            policy = default;
            return false;
        }
        policy = cached.Value;
        return true;
    }

    /// <summary>
    ///     Acquires a worker slot for <paramref name="logicalId"/> from its pool, creating
    ///     the pool on first access.  Returns a lease whose <see cref="StatelessWorkerLease.WorkerId"/>
    ///     is the synthetic activation identity to pass to <c>GetOrActivateAsync</c>.
    /// </summary>
    public async ValueTask<StatelessWorkerLease> AcquireAsync(
        GrainId logicalId,
        StatelessWorkerPoolPolicy policy,
        CancellationToken ct)
    {
        StatelessWorkerPool pool = _pools.GetOrAdd(logicalId, ValueFactory, policy);
        (int slotIndex, GrainId workerId) = await pool.AcquireAsync(logicalId, ct).ConfigureAwait(false);
        return new StatelessWorkerLease(workerId, slotIndex, pool);
    }

    private static StatelessWorkerPool ValueFactory(GrainId _, StatelessWorkerPoolPolicy p)
    {
        return new StatelessWorkerPool(p);
    }

    /// <summary>
    ///     Called from the <c>SetOnDeactivated</c> callback when a synthetic worker activation
    ///     deactivates (idle-collection or explicit).  Defensively marks the slot free and
    ///     removes the pool from the dictionary if all slots are idle.
    /// </summary>
    public void OnWorkerDeactivated(GrainId workerId)
    {
        if (!TryDecode(workerId, out GrainId logicalId, out int ordinal))
            return;

        if (!_pools.TryGetValue(logicalId, out StatelessWorkerPool? pool))
            return;

        pool.MarkWorkerDeactivated(ordinal);

        if (pool.IsEmpty)
        {
            _pools.TryRemove(new KeyValuePair<GrainId, StatelessWorkerPool>(logicalId, pool));
        }
    }

    /// <summary>
    ///     Fast check: returns <c>true</c> when <paramref name="id"/> is a synthetic worker id
    ///     (contains the SENTINEL character AND the type is a registered stateless-worker type).
    /// </summary>
    public bool IsWorkerId(GrainId id)
    {
        if (!id.Key.Contains(StatelessWorkerIdentity.Sentinel))
            return false;

        return TryGetPolicy(id.Type, out _);
    }

    /// <summary>
    ///     Decodes a synthetic worker id.  Also validates that the grain type is a registered
    ///     stateless-worker type, so a user key that happens to contain the sentinel is not
    ///     misidentified (OQ5 resolution).
    /// </summary>
    public bool TryDecode(GrainId workerId, out GrainId logicalId, out int ordinal)
    {
        if (!StatelessWorkerIdentity.TryDecode(workerId, out logicalId, out ordinal))
            return false;

        if (!TryGetPolicy(workerId.Type, out _))
        {
            logicalId = default;
            ordinal = 0;
            return false;
        }

        return true;
    }

    private StatelessWorkerPoolPolicy? ResolvePolicy(GrainType grainType)
    {
        if (!_typeRegistry.TryGetGrainClass(grainType, out Type? behaviorType) || behaviorType is null)
            return null;

        object[] attrs = behaviorType.GetCustomAttributes(typeof(StatelessWorkerAttribute), inherit: true);
        if (attrs.Length == 0 || attrs[0] is not StatelessWorkerAttribute attr)
            return null;

        int maxLocalActivations = attr.MaxLocalWorkers >= 1
            ? attr.MaxLocalWorkers
            : Math.Max(1, _options.StatelessWorkerDefaultMaxLocalActivations);

        return new StatelessWorkerPoolPolicy(
            MaxLocalActivations: maxLocalActivations,
            MaxConcurrentExecutions: maxLocalActivations,
            QueueCapacity: _options.StatelessWorkerQueueCapacity,
            OverloadMode: _options.StatelessWorkerOverloadMode);
    }
}
