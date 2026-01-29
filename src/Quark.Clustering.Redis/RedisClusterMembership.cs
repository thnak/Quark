using System.Collections.Concurrent;
using Quark.Abstractions.Clustering;
using Quark.Networking.Abstractions;
using StackExchange.Redis;

namespace Quark.Clustering.Redis;

/// <summary>
/// Redis-based cluster membership implementation using consistent hashing.
/// Stores silo information in Redis and uses Pub/Sub for membership changes.
/// </summary>
public sealed class RedisClusterMembership : IQuarkClusterMembership
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IConsistentHashRing _hashRing;
    private readonly string _currentSiloId;
    private readonly ConcurrentDictionary<string, SiloInfo> _silos;
    private readonly Timer? _heartbeatTimer;
    private ISubscriber? _subscriber;
    private const string SiloKeyPrefix = "quark:silo:";
    private const string MembershipChannel = "quark:membership";
    private const int HeartbeatIntervalSeconds = 10;
    private const int SiloTimeoutSeconds = 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisClusterMembership"/> class.
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
        _currentSiloId = currentSiloId ?? throw new ArgumentNullException(nameof(currentSiloId));
        _hashRing = hashRing ?? new ConsistentHashRing();
        _silos = new ConcurrentDictionary<string, SiloInfo>();
        _heartbeatTimer = new Timer(HeartbeatCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public string CurrentSiloId => _currentSiloId;

    /// <inheritdoc />
    public IConsistentHashRing HashRing => _hashRing;

    /// <inheritdoc />
    public event EventHandler<SiloInfo>? SiloJoined;

    /// <inheritdoc />
    public event EventHandler<SiloInfo>? SiloLeft;

    /// <inheritdoc />
    public async Task RegisterSiloAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SiloKeyPrefix + siloInfo.SiloId;
        
        // Store silo info in Redis
        var data = System.Text.Json.JsonSerializer.Serialize(siloInfo);
        await db.StringSetAsync(key, data, TimeSpan.FromSeconds(SiloTimeoutSeconds));

        // Add to local cache and hash ring
        _silos[siloInfo.SiloId] = siloInfo;
        _hashRing.AddNode(new HashRingNode(siloInfo.SiloId));

        // Publish membership change
        await _redis.GetSubscriber().PublishAsync(MembershipChannel, $"join:{siloInfo.SiloId}");
    }

    /// <inheritdoc />
    public async Task UnregisterSiloAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SiloKeyPrefix + _currentSiloId;
        
        await db.KeyDeleteAsync(key);
        await _redis.GetSubscriber().PublishAsync(MembershipChannel, $"leave:{_currentSiloId}");

        _hashRing.RemoveNode(_currentSiloId);
        _silos.TryRemove(_currentSiloId, out _);
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
                var silo = System.Text.Json.JsonSerializer.Deserialize<SiloInfo>(data.ToString());
                if (silo != null)
                {
                    silos.Add(silo);
                }
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

        return System.Text.Json.JsonSerializer.Deserialize<SiloInfo>(data.ToString());
    }

    /// <inheritdoc />
    public async Task UpdateHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SiloKeyPrefix + _currentSiloId;
        
        if (_silos.TryGetValue(_currentSiloId, out var silo))
        {
            // Create new instance with updated heartbeat
            var updated = new SiloInfo(silo.SiloId, silo.Address, silo.Port, silo.Status);
            _silos[_currentSiloId] = updated;
            
            var data = System.Text.Json.JsonSerializer.Serialize(updated);
            await db.StringSetAsync(key, data, TimeSpan.FromSeconds(SiloTimeoutSeconds));
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Subscribe to membership changes
        _subscriber = _redis.GetSubscriber();
        await _subscriber.SubscribeAsync(MembershipChannel, OnMembershipMessage);

        // Load existing silos
        var silos = await GetActiveSilosAsync(cancellationToken);
        foreach (var silo in silos)
        {
            _silos[silo.SiloId] = silo;
            _hashRing.AddNode(new HashRingNode(silo.SiloId));
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
        {
            await _subscriber.UnsubscribeAsync(MembershipChannel);
        }

        await UnregisterSiloAsync(cancellationToken);
    }

    /// <inheritdoc />
    public string? GetActorSilo(string actorId, string actorType)
    {
        var key = $"{actorType}:{actorId}";
        return _hashRing.GetNode(key);
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
        {
            Task.Run(async () =>
            {
                var silo = await GetSiloAsync(siloId);
                if (silo != null)
                {
                    _silos[siloId] = silo;
                    _hashRing.AddNode(new HashRingNode(siloId));
                    SiloJoined?.Invoke(this, silo);
                }
            });
        }
        else if (action == "leave")
        {
            if (_silos.TryRemove(siloId, out var silo))
            {
                _hashRing.RemoveNode(siloId);
                SiloLeft?.Invoke(this, silo);
            }
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
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
    }
}
