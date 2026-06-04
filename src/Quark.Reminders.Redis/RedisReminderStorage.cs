using System.Text.Json;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Identity;
using Quark.Reminders.Abstractions;
using StackExchange.Redis;

namespace Quark.Reminders.Redis;

/// <summary>
///     Redis-backed <see cref="IReminderStorage" /> using a single Hash at a configurable key.
///     Entries are serialised as JSON via <c>System.Text.Json</c> source generation (AOT-safe).
/// </summary>
public sealed class RedisReminderStorage : IReminderStorage
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _hashKey;

    public RedisReminderStorage(IConnectionMultiplexer redis, IOptions<RedisReminderOptions> options)
    {
        _redis = redis;
        _hashKey = options.Value.HashKey;
    }

    private static string GetField(GrainId grainId, string reminderName)
        => $"{grainId.Type.Value}|{grainId.Key}|{reminderName}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IDatabase db = _redis.GetDatabase();
        HashEntry[] fields = await db.HashGetAllAsync(_hashKey).ConfigureAwait(false);
        return fields
            .Select(f => Deserialize((string?)f.Value))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(
        GrainId grainId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IDatabase db = _redis.GetDatabase();
        HashEntry[] fields = await db.HashGetAllAsync(_hashKey).ConfigureAwait(false);
        return fields
            .Select(f => Deserialize((string?)f.Value))
            .Where(e => e is not null && e.GrainId == grainId)
            .Select(e => e!)
            .ToList();
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IDatabase db = _redis.GetDatabase();
        string field = GetField(entry.GrainId, entry.ReminderName);
        string json = Serialize(entry);
        await db.HashSetAsync(_hashKey, field, json).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IDatabase db = _redis.GetDatabase();
        await db.HashDeleteAsync(_hashKey, GetField(grainId, reminderName)).ConfigureAwait(false);
    }

    // ---- Serialisation ----

    private static string Serialize(ReminderEntry entry)
    {
        var dto = new ReminderEntryDto
        {
            GrainType = entry.GrainId.Type.Value,
            GrainKey = entry.GrainId.Key,
            ReminderName = entry.ReminderName,
            StartAtUtcTicks = entry.StartAt.UtcTicks,
            PeriodTicks = entry.Period.Ticks,
            NextFireAtUtcTicks = entry.NextFireAt.UtcTicks
        };
        return JsonSerializer.Serialize(dto, ReminderEntryDtoJsonContext.Default.ReminderEntryDto);
    }

    private static ReminderEntry? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        ReminderEntryDto? dto = JsonSerializer.Deserialize(json, ReminderEntryDtoJsonContext.Default.ReminderEntryDto);
        if (dto is null) return null;
        return new ReminderEntry
        {
            GrainId = new GrainId(new GrainType(dto.GrainType), dto.GrainKey),
            ReminderName = dto.ReminderName,
            StartAt = new DateTimeOffset(dto.StartAtUtcTicks, TimeSpan.Zero),
            Period = TimeSpan.FromTicks(dto.PeriodTicks),
            NextFireAt = new DateTimeOffset(dto.NextFireAtUtcTicks, TimeSpan.Zero)
        };
    }

    internal sealed class ReminderEntryDto
    {
        public string GrainType { get; set; } = "";
        public string GrainKey { get; set; } = "";
        public string ReminderName { get; set; } = "";
        public long StartAtUtcTicks { get; set; }
        public long PeriodTicks { get; set; }
        public long NextFireAtUtcTicks { get; set; }
    }
}