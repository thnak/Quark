using System.Collections.Concurrent;
using System.Text.Json;
using Quark.Abstractions.Clustering;
using Quark.Networking.Abstractions;
using StackExchange.Redis;

namespace Quark.Clustering.Redis;

/// <summary>
///     Redis-based cluster membership implementation using consistent hashing.
///     Stores silo information in Redis and uses Pub/Sub for membership changes.
/// </summary>
public sealed class RedisClusterMembership : IQuarkClusterMembership
{
    private const string SiloKeyPrefix = "quark:silo:";
    private const string MembershipChannel = "quark:membership";
    private const int HeartbeatIntervalSeconds = 10;
    private const int SiloTimeoutSeconds = 30;
    private readonly Timer? _heartbeatTimer;
    private readonly IConnectionMultiplexer _redis;
    private readonly ConcurrentDictionary<string, SiloInfo> _silos;
    private ISubscriber? _subscriber;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RedisClusterMembership" /> class.
    /// </summary>
    /// <param name="redis">The Redis connection.</param>
    /// <param name="currentSiloId">The current silo ID.</param>
    /// <param name="hashRing">Optional custom hash ring implementation.</param>
    public RedisClusterMembership(
        IConnectionMultiplexer redis,
        string currentSiloId,
        IConsistentHashRing? hashRing = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        CurrentSiloId = currentSiloId ?? throw new ArgumentNullException(nameof(currentSiloId));
        HashRing = hashRing ?? new ConsistentHashRing();
        _silos = new ConcurrentDictionary<string, SiloInfo>();
        _heartbeatTimer = new Timer(HeartbeatCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public string CurrentSiloId { get; }

    /// <inheritdoc />
    public IConsistentHashRing HashRing { get; }

    /// <inheritdoc />
    public event EventHandler<SiloInfo>? SiloJoined;

    /// <inheritdoc />
    public event EventHandler<SiloInfo>? SiloLeft;

    /// <inheritdoc />
    public async Task RegisterSiloAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SiloKeyPrefix + siloInfo.SiloId;

        // Store silo info in Redis using source-generated serialization (zero reflection)
        var data = JsonSerializer.Serialize(siloInfo, QuarkJsonSerializerContext.Default.SiloInfo);
        await db.StringSetAsync(key, data, TimeSpan.FromSeconds(SiloTimeoutSeconds));

        // Add to local cache and hash ring
        _silos[siloInfo.SiloId] = siloInfo;
        HashRing.AddNode(new HashRingNode(siloInfo.SiloId));

        // Publish membership change
        await _redis.GetSubscriber().PublishAsync(new RedisChannel(MembershipChannel, RedisChannel.PatternMode.Auto),
            $"join:{siloInfo.SiloId}");
    }

    /// <inheritdoc />
    public async Task UnregisterSiloAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SiloKeyPrefix + CurrentSiloId;

        await db.KeyDeleteAsync(key);
        await _redis.GetSubscriber().PublishAsync(new RedisChannel(MembershipChannel, RedisChannel.PatternMode.Auto),
            $"leave:{CurrentSiloId}");

        HashRing.RemoveNode(CurrentSiloId);
        _silos.TryRemove(CurrentSiloId, out _);
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
                // Use source-generated deserialization (zero reflection)
                var silo = JsonSerializer.Deserialize(data.ToString(), QuarkJsonSerializerContext.Default.SiloInfo);
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

        // Use source-generated deserialization (zero reflection)
        return JsonSerializer.Deserialize(data.ToString(), QuarkJsonSerializerContext.Default.SiloInfo);
    }

    /// <inheritdoc />
    public async Task UpdateHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SiloKeyPrefix + CurrentSiloId;

        if (_silos.TryGetValue(CurrentSiloId, out var silo))
        {
            // Create new instance with updated heartbeat
            var updated = new SiloInfo(silo.SiloId, silo.Address, silo.Port, silo.Status);
            _silos[CurrentSiloId] = updated;

            // Use source-generated serialization (zero reflection)
            var data = JsonSerializer.Serialize(updated, QuarkJsonSerializerContext.Default.SiloInfo);
            await db.StringSetAsync(key, data, TimeSpan.FromSeconds(SiloTimeoutSeconds));
        }
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

        // Start heartbeat timer
        _heartbeatTimer?.Change(
            TimeSpan.FromSeconds(HeartbeatIntervalSeconds),
            TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        if (_subscriber != null)
            await _subscriber.UnsubscribeAsync(new RedisChannel(MembershipChannel, RedisChannel.PatternMode.Auto));

        await UnregisterSiloAsync(cancellationToken);
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

    private void HeartbeatCallback(object? state)
    {
        try
        {
            UpdateHeartbeatAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Log error
        }
    }

    /// <summary>
    ///     Disposes resources.
    /// </summary>
    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
    }
}