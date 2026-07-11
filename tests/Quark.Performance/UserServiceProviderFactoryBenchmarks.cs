using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Performance.UserServiceProviderFactory;
using Quark.Runtime;
using Quark.Serialization;

namespace Quark.Performance;

/// <summary>
/// Quantifies the DI cost IGrainUserServiceProviderFactory (#162) is designed to remove: re-resolving
/// an expensive, effectively-stateless user dependency graph on every grain call. Compares the default
/// (not-opted-in) fresh-scope-per-call path against the opted-in cached-provider path, both isolated
/// (GrainScopeBinder.CreateCallScope + BindAndResolve) and end-to-end (full IGrainCallInvoker round
/// trip). Both variants run the real production startup path (BehaviorStartupValidator then
/// SiloHostedService) so the opted-in side exercises the actual satellite-provider wiring, not a
/// hand-rolled stand-in. See
/// docs/superpowers/specs/2026-07-10-grain-user-service-provider-factory-design.md.
/// </summary>
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
[MemoryDiagnoser]
public class UserServiceProviderFactoryBenchmarks
{
    private static readonly GrainType ExpensiveGrainType = new("ExpensiveGrain");

    private ServiceProvider _notOptedInSp = null!;
    private ServiceProvider _optedInSp = null!;

    private GrainActivationTable _notOptedInTable = null!;
    private GrainActivationTable _optedInTable = null!;

    private IGrainCallInvoker _notOptedInInvoker = null!;
    private IGrainCallInvoker _optedInInvoker = null!;

    private GrainId _notOptedInGrainId;
    private GrainId _optedInGrainId;

    private GrainActivation _notOptedInActivation = null!;
    private GrainActivation _optedInActivation = null!;

    [GlobalSetup]
    public void Setup() => SetupAsync().GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup() => CleanupAsync().GetAwaiter().GetResult();

    private async Task SetupAsync()
    {
        // --- Not opted in: ExpensiveUserRepository is Scoped on silo.Services, re-resolved from the
        // flat per-call scope on every call -- the default, unaffected path. ---
        var notOptedInServices = new ServiceCollection();
        notOptedInServices.AddLogging();
        notOptedInServices.AddQuarkSerialization();
        notOptedInServices.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "bench";
            o.ServiceId = "bench";
            o.SiloName = "silo-not-opted-in";
        });
        notOptedInServices.AddQuarkRuntime();
        notOptedInServices.AddScoped<ExpensiveUserRepository>();
        notOptedInServices.AddGrainBehavior<IExpensiveGrain, NotOptedInBehavior>();
        _notOptedInSp = notOptedInServices.BuildServiceProvider();
        await StartHostedServicesAsync(_notOptedInSp);

        _notOptedInTable = _notOptedInSp.GetRequiredService<GrainActivationTable>();
        _notOptedInInvoker = _notOptedInSp.GetRequiredService<IGrainCallInvoker>();
        _notOptedInGrainId = GrainId.Create(ExpensiveGrainType, "bench-0");
        // Pre-activate so the full-invoke benchmark hits steady state.
        await _notOptedInInvoker.InvokeAsync<ExpensiveGrain_GetConnectionCountInvokable, int>(
            _notOptedInGrainId, default);

        // Bare activation, constructed directly (bypassing GrainActivationTable) so the isolated
        // CreateCallScope+BindAndResolve benchmark measures DI resolution alone.
        _notOptedInActivation = new GrainActivation(
            GrainId.Create(ExpensiveGrainType, "bare-not-opted-in"),
            ExpensiveGrainType,
            isReentrant: false,
            _notOptedInSp,
            _notOptedInSp.GetRequiredService<ILogger<GrainActivation>>(),
            _notOptedInSp.GetRequiredService<IActivationScheduler>());

        // --- Opted in: ExpensiveUserRepository lives in a Singleton, dedicated provider built once
        // per grain type at silo startup (OptedInBehavior.CreateUserServiceProvider) and cached
        // thereafter. Never registered on silo.Services at all. ---
        var optedInServices = new ServiceCollection();
        optedInServices.AddLogging();
        optedInServices.AddQuarkSerialization();
        optedInServices.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "bench";
            o.ServiceId = "bench";
            o.SiloName = "silo-opted-in";
        });
        optedInServices.AddQuarkRuntime();
        optedInServices.AddGrainBehavior<IExpensiveGrain, OptedInBehavior>();
        optedInServices.AddGrainUserServiceProviderFactory<IExpensiveGrain, OptedInBehavior>();
        _optedInSp = optedInServices.BuildServiceProvider();
        await StartHostedServicesAsync(_optedInSp);

        _optedInTable = _optedInSp.GetRequiredService<GrainActivationTable>();
        _optedInInvoker = _optedInSp.GetRequiredService<IGrainCallInvoker>();
        _optedInGrainId = GrainId.Create(ExpensiveGrainType, "bench-0");
        await _optedInInvoker.InvokeAsync<ExpensiveGrain_GetConnectionCountInvokable, int>(
            _optedInGrainId, default);

        _optedInActivation = new GrainActivation(
            GrainId.Create(ExpensiveGrainType, "bare-opted-in"),
            ExpensiveGrainType,
            isReentrant: false,
            _optedInSp,
            _optedInSp.GetRequiredService<ILogger<GrainActivation>>(),
            _optedInSp.GetRequiredService<IActivationScheduler>());
    }

    // Runs the real production startup path (BehaviorStartupValidator, then SiloHostedService --
    // which applies AddGrainUserServiceProviderFactory registrations and builds the Quark-only
    // satellite provider) so the opted-in benchmark exercises the exact wiring a real silo does,
    // not a hand-rolled reimplementation of it.
    private static async Task StartHostedServicesAsync(IServiceProvider sp)
    {
        foreach (IHostedService hostedService in sp.GetServices<IHostedService>())
        {
            await hostedService.StartAsync(CancellationToken.None);
        }
    }

    private async Task CleanupAsync()
    {
        await _notOptedInActivation.DisposeAsync();
        await _optedInActivation.DisposeAsync();
        await _notOptedInTable.DisposeAsync();
        await _optedInTable.DisposeAsync();

        // The Quark-only satellite provider is a container the root ServiceProvider doesn't own
        // (SiloHostedService builds it separately) -- dispose it explicitly, same as
        // SiloHostedService.StopAsync does in production.
        if (_optedInSp.GetService<QuarkOnlyServiceProviderHolder>()?.Provider is IAsyncDisposable quarkOnlyProvider)
        {
            await quarkOnlyProvider.DisposeAsync();
        }

        await _notOptedInSp.DisposeAsync();
        await _optedInSp.DisposeAsync();
    }

    // Isolated: GrainScopeBinder.CreateCallScope + BindAndResolve only -- the DI resolution stage
    // IGrainUserServiceProviderFactory targets, with mailbox/dispatch overhead excluded.
    [Benchmark(Baseline = true)]
    public IGrainBehavior NotOptedIn_CreateScopeAndResolve()
    {
        (IServiceScope scope, IServiceProvider construction) =
            GrainScopeBinder.CreateCallScope(_notOptedInSp, _notOptedInActivation);
        try
        {
            return GrainScopeBinder.BindAndResolve(scope.ServiceProvider, construction, _notOptedInActivation);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Benchmark]
    public IGrainBehavior OptedIn_CreateScopeAndResolve()
    {
        (IServiceScope scope, IServiceProvider construction) =
            GrainScopeBinder.CreateCallScope(_optedInSp, _optedInActivation);
        try
        {
            return GrainScopeBinder.BindAndResolve(scope.ServiceProvider, construction, _optedInActivation);
        }
        finally
        {
            scope.Dispose();
        }
    }

    // End-to-end: full IGrainCallInvoker round trip -- mailbox, scope creation, DI resolution, and
    // the method body together. The real number a caller sees.
    [Benchmark]
    public async ValueTask<int> NotOptedIn_FullInvoke()
        => await _notOptedInInvoker.InvokeAsync<ExpensiveGrain_GetConnectionCountInvokable, int>(
            _notOptedInGrainId, default);

    [Benchmark]
    public async ValueTask<int> OptedIn_FullInvoke()
        => await _optedInInvoker.InvokeAsync<ExpensiveGrain_GetConnectionCountInvokable, int>(
            _optedInGrainId, default);
}
