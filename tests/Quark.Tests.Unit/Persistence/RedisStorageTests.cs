using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.Redis;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Unit.Persistence;

public sealed class RedisStorageTests
{
    [Fact]
    public async Task Write_And_Read_RoundTrips_State_Through_Redis_Provider()
    {
        FakeRedisStorageConnection connection = new();

        ServiceCollection services = new();
        services.AddQuarkSerialization();
        services.AddSingleton<IFieldCodec<CounterState>, CounterStateCodec>();
        services.AddSingleton<IDeepCopier<CounterState>, CounterStateCopier>();
        services.AddSingleton<IRedisStorageConnection>(connection);
        services.AddRedisGrainStorage(options => options.KeyPrefix = "quark-tests");

        using ServiceProvider provider = services.BuildServiceProvider();
        IStorage<CounterState> storage = provider.GetRequiredService<IStorage<CounterState>>();

        GrainId grainId = new(new GrainType("CounterGrain"), "redis-1");
        CounterState original = new() { Value = 42 };

        await storage.WriteAsync(grainId, original);
        original.Value = 100;

        CounterState loaded = await storage.ReadAsync(grainId);

        Assert.NotSame(original, loaded);
        Assert.Equal(42, loaded.Value);
        Assert.Contains("quark-tests", Assert.Single(connection.Keys));
    }

    [Fact]
    public async Task Clear_Removes_Previously_Written_Redis_State()
    {
        FakeRedisStorageConnection connection = new();

        ServiceCollection services = new();
        services.AddQuarkSerialization();
        services.AddSingleton<IFieldCodec<CounterState>, CounterStateCodec>();
        services.AddSingleton<IDeepCopier<CounterState>, CounterStateCopier>();
        services.AddSingleton<IRedisStorageConnection>(connection);
        services.AddRedisGrainStorage();

        using ServiceProvider provider = services.BuildServiceProvider();
        IStorage<CounterState> storage = provider.GetRequiredService<IStorage<CounterState>>();

        GrainId grainId = new(new GrainType("CounterGrain"), "redis-2");
        await storage.WriteAsync(grainId, new CounterState { Value = 7 });
        await storage.ClearAsync(grainId);

        CounterState loaded = await storage.ReadAsync(grainId);

        Assert.Equal(0, loaded.Value);
        Assert.Empty(connection.Keys);
    }

    private sealed class FakeRedisStorageConnection : IRedisStorageConnection
    {
        private readonly Dictionary<string, RedisStorageRecord> _records = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => _records.Keys.ToArray();

        public Task<RedisStorageRecord?> ReadAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_records.TryGetValue(key, out RedisStorageRecord record)
                ? (RedisStorageRecord?)record
                : null);
        }

        public Task WriteAsync(string key, RedisStorageRecord record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records[key] = new RedisStorageRecord(record.Payload.ToArray(), record.ETag);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed class CounterStateCodec : IFieldCodec<CounterState>
    {
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, CounterState value)
        {
            writer.WriteFieldHeader(fieldId, WireType.LengthPrefixed);
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value.Value);
            writer.WriteBytes(bytes);
        }

        public CounterState ReadValue(CodecReader reader, Field field)
        {
            byte[] bytes = reader.ReadBytes();
            return new CounterState { Value = BinaryPrimitives.ReadInt32LittleEndian(bytes) };
        }
    }

    private sealed class CounterStateCopier : IDeepCopier<CounterState>
    {
        public CounterState DeepCopy(CounterState original, CopyContext context) =>
            new() { Value = original.Value };
    }
}
