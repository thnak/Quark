using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Reminders;
using Quark.Reminders.Abstractions;
using Quark.Reminders.InMemory;
using Quark.Runtime;
using Quark.Testing.Harness;
using Xunit;

namespace Quark.Tests.Integration;

public sealed class ReminderIntegrationTests
{
    private static Action<TestClusterOptions> BuildOptions(
        Action<IServiceCollection>? extraSilo = null,
        TimeSpan? pollInterval = null)
    {
        return options =>
        {
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddInMemoryReminders(o =>
                    o.PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50));
                services.AddGrain<ReminderTestGrain>();
                services.AddGrainActivatorFactory<ReminderTestGrainActivatorFactory>();
                extraSilo?.Invoke(services);
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IReminderTestGrain, ReminderTestGrainProxy>();
            };
        };
    }

    [Fact]
    public async Task Reminder_FiresOnIRemindableGrain()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync(BuildOptions());

        IReminderTestGrain grain = cluster.Client.GetGrain<IReminderTestGrain>("fire-test");
        await grain.RegisterReminderAsync("daily", TimeSpan.FromMilliseconds(100), TimeSpan.FromHours(24));

        await Task.Delay(500);

        int count = await grain.GetReceiveCountAsync();
        Assert.True(count >= 1, $"Expected >=1 reminder fires, got {count}");
    }

    [Fact]
    public async Task UnregisterReminder_StopsFutureFirings()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync(
            BuildOptions(pollInterval: TimeSpan.FromMilliseconds(30)));

        IReminderTestGrain grain = cluster.Client.GetGrain<IReminderTestGrain>("unregister-test");
        await grain.RegisterReminderAsync("tick", TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        await Task.Delay(300);
        int countBeforeUnregister = await grain.GetReceiveCountAsync();
        await grain.UnregisterReminderAsync("tick");
        await Task.Delay(300); // drain any in-flight reminder dispatch already queued to the grain channel
        int countAfterUnregister = await grain.GetReceiveCountAsync();

        Assert.Equal(countBeforeUnregister, countAfterUnregister);
    }

    [Fact]
    public async Task Reminder_SurvivesSimulatedRestart()
    {
        // Use a shared storage instance across both clusters to simulate persistence.
        var storage = new InMemoryReminderStorage();

        Action<IServiceCollection> sharedStorage = services =>
        {
            services.RemoveAll<IReminderStorage>();
            services.AddSingleton<IReminderStorage>(storage);
        };

        await using (TestCluster cluster1 = await TestCluster.CreateAsync(
            BuildOptions(sharedStorage, pollInterval: TimeSpan.FromMilliseconds(50))))
        {
            IReminderTestGrain grain = cluster1.Client.GetGrain<IReminderTestGrain>("restart-test");
            // dueTime = 2s so cluster1 shuts down before the first tick, leaving NextFireAt in the future.
            await grain.RegisterReminderAsync("persist", TimeSpan.FromSeconds(2), TimeSpan.FromHours(24));
        }
        // cluster1 disposed — grain activations gone, storage still has entry with NextFireAt ~now+2s.

        await using TestCluster cluster2 = await TestCluster.CreateAsync(
            BuildOptions(sharedStorage, pollInterval: TimeSpan.FromMilliseconds(50)));

        // Wait for the 2s due time to expire and the reminder to fire.
        await Task.Delay(2500);

        IReminderTestGrain grain2 = cluster2.Client.GetGrain<IReminderTestGrain>("restart-test");
        int count = await grain2.GetReceiveCountAsync();
        Assert.True(count >= 1, $"Expected reminder to fire after restart, got {count}");
    }

    [Fact]
    public async Task GetReminders_ReturnsRegisteredReminders()
    {
        await using TestCluster cluster = await TestCluster.CreateAsync(BuildOptions());

        IReminderTestGrain grain = cluster.Client.GetGrain<IReminderTestGrain>("list-test");
        await grain.RegisterReminderAsync("r1", TimeSpan.FromHours(1), TimeSpan.FromHours(24));
        await grain.RegisterReminderAsync("r2", TimeSpan.FromHours(2), TimeSpan.FromHours(12));

        IReadOnlyList<IGrainReminder> reminders = await grain.GetReminderListAsync();

        Assert.Equal(2, reminders.Count);
        Assert.Contains(reminders, r => r.ReminderName == "r1");
        Assert.Contains(reminders, r => r.ReminderName == "r2");
    }

    // ---- Test grain interface ----

    private interface IReminderTestGrain : IGrainWithStringKey
    {
        Task RegisterReminderAsync(string name, TimeSpan dueTime, TimeSpan period);
        Task UnregisterReminderAsync(string name);
        Task<int> GetReceiveCountAsync();
        Task<IReadOnlyList<IGrainReminder>> GetReminderListAsync();
    }

    // ---- Test grain implementation ----

    private sealed class ReminderTestGrain : Grain, IRemindable, IReminderTestGrain
    {
        private int _receiveCount;
        private readonly Dictionary<string, IGrainReminder> _handles = new();

        public async Task RegisterReminderAsync(string name, TimeSpan dueTime, TimeSpan period)
        {
            IGrainReminder handle = await RegisterOrUpdateReminderAsync(name, dueTime, period);
            _handles[name] = handle;
        }

        public Task UnregisterReminderAsync(string name)
        {
            if (_handles.Remove(name, out IGrainReminder? handle))
            {
                return UnregisterReminderAsync(handle);
            }
            return Task.CompletedTask;
        }

        public Task<int> GetReceiveCountAsync() => Task.FromResult(_receiveCount);

        public async Task<IReadOnlyList<IGrainReminder>> GetReminderListAsync()
            => await GetRemindersAsync();

        public Task ReceiveReminder(string reminderName, TickStatus status)
        {
            Interlocked.Increment(ref _receiveCount);
            return Task.CompletedTask;
        }
    }

    // ---- Hand-written invokables ----

    private readonly struct ReminderTestGrainProxy_RegisterReminderAsyncInvokable : IGrainVoidInvokable
    {
        private readonly string _name;
        private readonly TimeSpan _dueTime;
        private readonly TimeSpan _period;
        public ReminderTestGrainProxy_RegisterReminderAsyncInvokable(string name, TimeSpan dueTime, TimeSpan period)
        {
            _name = name;
            _dueTime = dueTime;
            _period = period;
        }
        public uint MethodId => 0u;
        public ValueTask Invoke(Grain grain) => new(((IReminderTestGrain)grain).RegisterReminderAsync(_name, _dueTime, _period));
    }

    private readonly struct ReminderTestGrainProxy_UnregisterReminderAsyncInvokable : IGrainVoidInvokable
    {
        private readonly string _name;
        public ReminderTestGrainProxy_UnregisterReminderAsyncInvokable(string name) => _name = name;
        public uint MethodId => 1u;
        public ValueTask Invoke(Grain grain) => new(((IReminderTestGrain)grain).UnregisterReminderAsync(_name));
    }

    private readonly struct ReminderTestGrainProxy_GetReceiveCountAsyncInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 2u;
        public ValueTask<int> Invoke(Grain grain) => new(((IReminderTestGrain)grain).GetReceiveCountAsync());
    }

    private readonly struct ReminderTestGrainProxy_GetReminderListAsyncInvokable : IGrainInvokable<IReadOnlyList<IGrainReminder>>
    {
        public uint MethodId => 3u;
        public ValueTask<IReadOnlyList<IGrainReminder>> Invoke(Grain grain)
            => new(((IReminderTestGrain)grain).GetReminderListAsync());
    }

    // ---- Hand-written proxy (client side) ----

    private sealed class ReminderTestGrainProxy(
        GrainId grainId,
        IGrainCallInvoker invoker)
        : IReminderTestGrain,
          IGrainProxyActivator<ReminderTestGrainProxy>
    {
        public static ReminderTestGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task RegisterReminderAsync(string name, TimeSpan dueTime, TimeSpan period)
            => invoker.InvokeVoidAsync(grainId, new ReminderTestGrainProxy_RegisterReminderAsyncInvokable(name, dueTime, period));

        public Task UnregisterReminderAsync(string name)
            => invoker.InvokeVoidAsync(grainId, new ReminderTestGrainProxy_UnregisterReminderAsyncInvokable(name));

        public Task<int> GetReceiveCountAsync()
            => invoker.InvokeAsync<ReminderTestGrainProxy_GetReceiveCountAsyncInvokable, int>(
                grainId, new ReminderTestGrainProxy_GetReceiveCountAsyncInvokable());

        public Task<IReadOnlyList<IGrainReminder>> GetReminderListAsync()
            => invoker.InvokeAsync<ReminderTestGrainProxy_GetReminderListAsyncInvokable, IReadOnlyList<IGrainReminder>>(
                grainId, new ReminderTestGrainProxy_GetReminderListAsyncInvokable());
    }

    // ---- Hand-written activator factory ----

    private sealed class ReminderTestGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(ReminderTestGrain);
        public Grain Create(GrainId grainId, IServiceProvider services) => new ReminderTestGrain();
    }
}
