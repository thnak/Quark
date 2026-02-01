using System.Collections.Concurrent;
using System.Text.Json;
using Quark.Abstractions.Clustering;
using Quark.Networking.Abstractions;
using StackExchange.Redis;

namespace Quark.Client.DependencyInjection;

/// <summary>
/// Redis-based cluster membership implementation for client-only scenarios.
/// This implementation is read-only - it discovers silos but does not register itself.
/// Suitable for lightweight clients that only need to route calls to silos.
/// </summary>
public sealed class RedisClientClusterMembership : IQuarkClusterMembership
{
    private const string SiloKeyPrefix = "quark:silo:";
    private const string MembershipChannel = "quark:membership";
    private readonly IConnectionMultiplexer _redis;
    private readonly ConcurrentDictionary<string, SiloInfo> _silos;
    private ISubscriber? _subscriber;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisClientClusterMembership"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection.</param>
    /// <param name="hashRing">Optional custom hash ring implementation.</param>
    public RedisClientClusterMembership(
        IConnectionMultiplexer redis,
        IConsistentHashRing? hashRing = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        HashRing = hashRing ?? new ConsistentHashRing();
        _silos = new ConcurrentDictionary<string, SiloInfo>();
    }

    /// <inheritdoc />
    public string CurrentSiloId => string.Empty; // Clients don't have a silo ID

    /// <inheritdoc />
    public IConsistentHashRing HashRing { get; }

    /// <inheritdoc />
    public event EventHandler<SiloInfo>? SiloJoined;

    /// <inheritdoc />
    public event EventHandler<SiloInfo>? SiloLeft;

    /// <inheritdoc />
    public Task RegisterSiloAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default)
    {
        // Clients don't register themselves as silos
        throw new NotSupportedException("Client-only membership does not support silo registration.");
    }

    /// <inheritdoc />
    public Task UnregisterSiloAsync(CancellationToken cancellationToken = default)
    {
        // Clients don't unregister silos
        throw new NotSupportedException("Client-only membership does not support silo unregistration.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<SiloInfo>> GetActiveSilosAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: SiloKeyPrefix + "*");

        var silos = new List<SiloInfo>();
        foreach (var key in keys)
        {
            var data = await db.StringGetAsync(key);
            if (!data.IsNullOrEmpty)
            {
                var silo = JsonSerializer.Deserialize<SiloInfo>(data.ToString(), _jsonOptions);
                if (silo != null) silos.Add(silo);
            }
        }

        return silos.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<SiloInfo?> GetSiloAsync(string siloId, CancellationToken cancellationToken = default)
    {
        if (_silos.TryGetValue(siloId, out var cached))
            return cached;

        var db = _redis.GetDatabase();
        var key = SiloKeyPrefix + siloId;
        var data = await db.StringGetAsync(key);

        if (data.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<SiloInfo>(data.ToString(), _jsonOptions);
    }

    /// <inheritdoc />
    public Task UpdateHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        // Clients don't send heartbeats
        throw new NotSupportedException("Client-only membership does not support heartbeat updates.");
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Subscribe to membership changes
        _subscriber = _redis.GetSubscriber();
        await _subscriber.SubscribeAsync(new RedisChannel(MembershipChannel, RedisChannel.PatternMode.Auto),
            OnMembershipMessage);

        // Load existing silos
        var silos = await GetActiveSilosAsync(cancellationToken);
        foreach (var silo in silos)
        {
            _silos[silo.SiloId] = silo;
            HashRing.AddNode(new HashRingNode(silo.SiloId));
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_subscriber != null)
            await _subscriber.UnsubscribeAsync(new RedisChannel(MembershipChannel, RedisChannel.PatternMode.Auto));
    }

    /// <inheritdoc />
    public string? GetActorSilo(string actorId, string actorType)
    {
        var key = $"{actorType}:{actorId}";
        return HashRing.GetNode(key);
    }

    private void OnMembershipMessage(RedisChannel channel, RedisValue message)
    {
        var msg = message.ToString();
        var parts = msg.Split(':');

        if (parts.Length != 2)
            return;

        var action = parts[0];
        var siloId = parts[1];

        if (action == "join")
            Task.Run(async () =>
            {
                var silo = await GetSiloAsync(siloId);
                if (silo != null)
                {
                    _silos[siloId] = silo;
                    HashRing.AddNode(new HashRingNode(siloId));
                    SiloJoined?.Invoke(this, silo);
                }
            });
        else if (action == "leave")
            if (_silos.TryRemove(siloId, out var silo))
            {
                HashRing.RemoveNode(siloId);
                SiloLeft?.Invoke(this, silo);
            }
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        // No timers or other resources to dispose for client-only implementation
    }
}
