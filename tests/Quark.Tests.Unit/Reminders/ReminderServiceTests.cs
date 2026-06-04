using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;
using Quark.Reminders.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Reminders;

public sealed class ReminderServiceTests
{
    private static DefaultReminderService CreateService(
        FakeReminderStorage storage,
        FakeGrainCallInvoker invoker,
        TimeSpan? pollInterval = null)
    {
        var options = Options.Create(new ReminderOptions
        {
            PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(20)
        });
        return new DefaultReminderService(
            storage, invoker, options, NullLogger<DefaultReminderService>.Instance);
    }

    [Fact]
    public async Task Reminder_FiresAfterDueTime()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker);
        var grainId = new GrainId(new GrainType("TestGrain"), "1");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "tick", TimeSpan.FromMilliseconds(50), TimeSpan.FromHours(1));
        await Task.Delay(300);
        await svc.StopAsync(CancellationToken.None);

        Assert.True(invoker.VoidCalls.Count >= 1,
            $"Expected >=1 ReceiveReminder call, got {invoker.VoidCalls.Count}");
        var (calledGrainId, methodId, args) = invoker.VoidCalls[0];
        Assert.Equal(grainId, calledGrainId);
        Assert.Equal(ReminderMethodIds.ReceiveReminder, methodId);
        Assert.Equal("tick", (string)args![0]!);
    }

    [Fact]
    public async Task Reminder_FiresRepeatedly()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker, pollInterval: TimeSpan.FromMilliseconds(15));
        var grainId = new GrainId(new GrainType("TestGrain"), "2");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "repeat", TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(40));
        await Task.Delay(400);
        await svc.StopAsync(CancellationToken.None);

        Assert.True(invoker.VoidCalls.Count >= 3,
            $"Expected >=3 ReceiveReminder calls, got {invoker.VoidCalls.Count}");
    }

    [Fact]
    public async Task UnregisterReminder_StopsFutureFirings()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker, pollInterval: TimeSpan.FromMilliseconds(20));
        var grainId = new GrainId(new GrainType("TestGrain"), "3");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "stop", TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30));
        await Task.Delay(200);
        int countAtUnregister = invoker.VoidCalls.Count;
        await svc.UnregisterReminderAsync(grainId, "stop");
        await Task.Delay(200);
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(countAtUnregister, invoker.VoidCalls.Count);
    }

    [Fact]
    public async Task RegisterOrUpdate_UpsertsSingleEntryForSameName()
    {
        var storage = new FakeReminderStorage();
        var invoker = new FakeGrainCallInvoker();
        var svc = CreateService(storage, invoker);
        var grainId = new GrainId(new GrainType("TestGrain"), "4");

        await svc.StartAsync(CancellationToken.None);
        await svc.RegisterOrUpdateReminderAsync(grainId, "once", TimeSpan.FromHours(1), TimeSpan.FromHours(24));
        await svc.RegisterOrUpdateReminderAsync(grainId, "once", TimeSpan.FromHours(2), TimeSpan.FromHours(48));
        await svc.StopAsync(CancellationToken.None);

        var entries = await storage.ReadAllAsync();
        Assert.Single(entries);
        Assert.Equal(TimeSpan.FromHours(48), entries[0].Period);
    }

    // ---- Fakes ----

    private sealed class FakeReminderStorage : IReminderStorage
    {
        private readonly ConcurrentDictionary<string, ReminderEntry> _data = new();

        private static string Key(GrainId id, string name) =>
            $"{id.Type.Value}|{id.Key}|{name}";

        public Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReminderEntry>>(_data.Values.ToList());

        public Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default)
        {
            string prefix = $"{grainId.Type.Value}|{grainId.Key}|";
            var result = _data.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                              .Select(kv => kv.Value).ToList();
            return Task.FromResult<IReadOnlyList<ReminderEntry>>(result);
        }

        public Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default)
        {
            _data[Key(entry.GrainId, entry.ReminderName)] = entry;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default)
        {
            _data.TryRemove(Key(grainId, reminderName), out _);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGrainCallInvoker : IGrainCallInvoker
    {
        private readonly List<(GrainId, uint, object?[]?)> _voidCalls = [];
        private readonly object _lock = new();

        public IReadOnlyList<(GrainId, uint, object?[]?)> VoidCalls
        {
            get { lock (_lock) { return _voidCalls.ToList(); } }
        }

        public Task<object?> InvokeAsync(GrainId grainId, uint methodId, object?[]? arguments = null,
            CancellationToken ct = default)
        {
            lock (_lock) { _voidCalls.Add((grainId, methodId, arguments)); }
            return Task.FromResult<object?>(null);
        }

        public Task<TResult> InvokeAsync<TResult>(GrainId grainId, uint methodId, object?[]? arguments = null,
            CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task InvokeVoidAsync(GrainId grainId, uint methodId, object?[]? arguments = null,
            CancellationToken ct = default)
        {
            lock (_lock) { _voidCalls.Add((grainId, methodId, arguments)); }
            return Task.CompletedTask;
        }

        public Task<TResult> InvokeAsync<TInvokable, TResult>(GrainId grainId, TInvokable invokable, CancellationToken ct = default)
            where TInvokable : struct, IGrainInvokable<TResult>
            => throw new NotImplementedException();

        public Task InvokeVoidAsync<TInvokable>(GrainId grainId, TInvokable invokable, CancellationToken ct = default)
            where TInvokable : struct, IGrainVoidInvokable
            => throw new NotImplementedException();

        public Task InvokeObserverAsync<TInvokable>(GrainId grainId, TInvokable invokable, CancellationToken ct = default)
            where TInvokable : struct, IObserverVoidInvokable
            => throw new NotImplementedException();
    }
}
