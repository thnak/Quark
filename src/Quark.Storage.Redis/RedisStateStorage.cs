using System.Text.Json;
using Quark.Abstractions.Persistence;
using StackExchange.Redis;

namespace Quark.Storage.Redis;

/// <summary>
///     Redis-based implementation of state storage with optimistic concurrency control.
///     Uses Redis hashes to store state and version numbers as E-Tags.
/// </summary>
/// <typeparam name="TState">The type of state to store.</typeparam>
public sealed class RedisStateStorage<TState> : IStateStorage<TState> where TState : class
{
    private readonly IDatabase _database;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RedisStateStorage{TState}"/> class.
    /// </summary>
    /// <param name="database">The Redis database instance.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    public RedisStateStorage(IDatabase database, JsonSerializerOptions? jsonOptions = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    [Obsolete("Use LoadWithVersionAsync for optimistic concurrency support.")]
    public async Task<TState?> LoadAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        var data = await _database.HashGetAsync(key, "state");
        
        if (data.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<TState>(data.ToString(), _jsonOptions);
    }

    /// <inheritdoc />
    public async Task<StateWithVersion<TState>?> LoadWithVersionAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        var values = await _database.HashGetAsync(key, new RedisValue[] { "state", "version" });
        
        if (values[0].IsNullOrEmpty)
            return null;

        var state = JsonSerializer.Deserialize<TState>(values[0].ToString(), _jsonOptions);
        var version = values[1].IsNullOrEmpty ? 1L : (long)values[1];
        
        return state != null ? new StateWithVersion<TState>(state, version) : null;
    }

    /// <inheritdoc />
    [Obsolete("Use SaveWithVersionAsync for optimistic concurrency support.")]
    public async Task SaveAsync(string actorId, string stateName, TState state, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        var version = await _database.HashIncrementAsync(key, "version");
        
        await _database.HashSetAsync(key, new HashEntry[]
        {
            new("state", json),
            new("version", version)
        });
    }

    /// <inheritdoc />
    public async Task<long> SaveWithVersionAsync(string actorId, string stateName, TState state, long? expectedVersion, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        var json = JsonSerializer.Serialize(state, _jsonOptions);

        if (expectedVersion == null)
        {
            // First save - use HSETNX to ensure atomicity
            var newVersion = 1L;
            await _database.HashSetAsync(key, new HashEntry[]
            {
                new("state", json),
                new("version", newVersion)
            });
            return newVersion;
        }

        // Optimistic concurrency: use Lua script for atomic check-and-set
        var script = @"
            local current_version = redis.call('HGET', KEYS[1], 'version')
            if current_version == false then
                return redis.error_reply('State not found')
            end
            current_version = tonumber(current_version)
            local expected = tonumber(ARGV[1])
            if current_version ~= expected then
                return redis.error_reply('Version mismatch: expected ' .. expected .. ' but got ' .. current_version)
            end
            local new_version = current_version + 1
            redis.call('HSET', KEYS[1], 'state', ARGV[2], 'version', new_version)
            return new_version
        ";

        try
        {
            var result = await _database.ScriptEvaluateAsync(script, new RedisKey[] { key }, new RedisValue[] { expectedVersion.Value, json });
            return (long)result;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Version mismatch"))
        {
            // Parse actual version from error message
            var parts = ex.Message.Split("but got ");
            var actualVersion = parts.Length > 1 ? long.Parse(parts[1].Trim()) : 0L;
            throw new ConcurrencyException(expectedVersion.Value, actualVersion);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("State not found"))
        {
            throw new ConcurrencyException(expectedVersion.Value, 0L);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string actorId, string stateName, CancellationToken cancellationToken = default)
    {
        var key = GetKey(actorId, stateName);
        await _database.KeyDeleteAsync(key);
    }

    private static string GetKey(string actorId, string stateName)
    {
        return $"quark:state:{actorId}:{stateName}";
    }
}
