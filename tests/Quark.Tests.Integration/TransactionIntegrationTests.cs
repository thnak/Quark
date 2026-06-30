using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Testing.Harness;
using Quark.Transactions;
using Xunit;

namespace Quark.Tests.Integration;

[Trait("category", "integration")]
public sealed class TransactionIntegrationTests
{
    private static Action<TestClusterOptions> BuildOptions() => options =>
    {
        options.ConfigureSiloServices = services =>
        {
            services.AddQuarkRuntime();
            services.AddQuarkSerialization();
            services.AddInMemoryGrainStorage();
            services.AddSingleton<IDeepCopier<Balance>, BalanceCopier>();
            services.AddTransactions();
            services.AddGrainBehavior<IAccountGrain, AccountGrainBehavior>();

            // TransactionalState<Balance> must be shared across all per-call scopes for the
            // same activation so that multiple DepositAsync calls within one transaction
            // accumulate into the same _pending buffer. Use GrainActivation.GetOrCreate so
            // the instance is constructed once per activation and reused by every scope.
            services.AddScoped<ITransactionalState<Balance>>(sp =>
            {
                var shell = sp.GetRequiredService<IActivationShellAccessor>().Shell;
                return shell.GetOrCreate(() =>
                {
                    var ctx = sp.GetRequiredService<ICallContext>();
                    var storage = sp.GetRequiredService<IGrainStorage>();
                    var coordinator = sp.GetRequiredService<TransactionCoordinator>();
                    return new TransactionalState<Balance>(
                        "balance",
                        ctx.GrainId,
                        storage,
                        coordinator,
                        src => new Balance { Value = src.Value });
                });
            });
        };
        options.ConfigureClientServices = services =>
        {
            services.AddLocalClusterClient();
            services.AddGrainProxy<IAccountGrain, AccountGrainProxy>();
        };
    };

    [Fact]
    public async Task Commit_PersistsBalanceChange()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var coordinator = cluster.PrimarySilo.Services.GetRequiredService<TransactionCoordinator>();
        IAccountGrain alice = cluster.Client.GetGrain<IAccountGrain>("alice");

        Guid txId = coordinator.BeginTransaction();
        await alice.DepositAsync(100m);
        await coordinator.CommitAsync(txId);

        Assert.Equal(100m, await alice.GetBalanceAsync());
    }

    [Fact]
    public async Task Abort_RollsBackPendingChange()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var coordinator = cluster.PrimarySilo.Services.GetRequiredService<TransactionCoordinator>();
        IAccountGrain alice = cluster.Client.GetGrain<IAccountGrain>("alice-abort");

        Guid txSetup = coordinator.BeginTransaction();
        await alice.DepositAsync(100m);
        await coordinator.CommitAsync(txSetup);

        Guid txAbort = coordinator.BeginTransaction();
        await alice.DepositAsync(400m);
        await coordinator.AbortAsync(txAbort);

        Assert.Equal(100m, await alice.GetBalanceAsync());
    }

    [Fact]
    public async Task MultipleUpdates_InOneTx_CommitAppliesAll()
    {
        await using var cluster = await TestCluster.CreateAsync(BuildOptions());

        var coordinator = cluster.PrimarySilo.Services.GetRequiredService<TransactionCoordinator>();
        IAccountGrain alice = cluster.Client.GetGrain<IAccountGrain>("alice-multi");

        Guid txId = coordinator.BeginTransaction();
        await alice.DepositAsync(50m);
        await alice.DepositAsync(50m);
        await coordinator.CommitAsync(txId);

        Assert.Equal(100m, await alice.GetBalanceAsync());
    }

    // -----------------------------------------------------------------------
    // Grain
    // -----------------------------------------------------------------------

    public interface IAccountGrain : IGrainWithStringKey
    {
        Task DepositAsync(decimal amount);
        Task<decimal> GetBalanceAsync();
    }

    private sealed class AccountGrainBehavior : IGrainBehavior, IAccountGrain, IActivationLifecycle
    {
        private readonly ITransactionalState<Balance> _balance;

        public AccountGrainBehavior(ITransactionalState<Balance> balance) => _balance = balance;

        public Task OnActivateAsync(CancellationToken cancellationToken)
            => ((TransactionalState<Balance>)_balance).LoadAsync();

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

        [Transaction(TransactionOption.CreateOrJoin)]
        public Task DepositAsync(decimal amount) => _balance.PerformUpdate(b => b.Value += amount);

        public Task<decimal> GetBalanceAsync() => _balance.PerformRead(b => b.Value);
    }

    public sealed class Balance { public decimal Value { get; set; } }

    private sealed class BalanceCopier : IDeepCopier<Balance>
    {
        public Balance DeepCopy(Balance input, CopyContext context) => new() { Value = input.Value };
    }

    // Invokables
    private readonly struct AccountGrain_DepositInvokable : IGrainVoidInvokable
    {
        private readonly decimal _amount;
        public AccountGrain_DepositInvokable(decimal amount) => _amount = amount;
        public uint MethodId => 0u;
        public ValueTask Invoke(IGrainBehavior behavior) => new(((IAccountGrain)behavior).DepositAsync(_amount));
        public void Serialize(ref CodecWriter writer) { }
    }

    private readonly struct AccountGrain_GetBalanceInvokable : IGrainInvokable<decimal>
    {
        public uint MethodId => 1u;
        public ValueTask<decimal> Invoke(IGrainBehavior behavior) => new(((IAccountGrain)behavior).GetBalanceAsync());
        public void Serialize(ref CodecWriter writer) { }
        public decimal DeserializeResult(ref CodecReader reader) => throw new NotSupportedException("Local-only invokable.");
    }

    // Proxy
    private sealed class AccountGrainProxy : IAccountGrain, IGrainProxyActivator<AccountGrainProxy>
    {
        private readonly GrainId _grainId;
        private readonly IGrainCallInvoker _invoker;

        public AccountGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            _grainId = grainId;
            _invoker = invoker;
        }

        public static AccountGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
            => new(grainId, invoker);

        public Task DepositAsync(decimal amount)
            => _invoker.InvokeVoidAsync(_grainId, new AccountGrain_DepositInvokable(amount)).AsTask();

        public Task<decimal> GetBalanceAsync()
            => _invoker.InvokeAsync<AccountGrain_GetBalanceInvokable, decimal>(
                _grainId, new AccountGrain_GetBalanceInvokable()).AsTask();
    }
}
