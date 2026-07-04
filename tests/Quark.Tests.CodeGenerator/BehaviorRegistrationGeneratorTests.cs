using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class BehaviorRegistrationGeneratorTests
{
    // -----------------------------------------------------------------------
    // Happy-path: basic behavior wiring
    // -----------------------------------------------------------------------

    [Fact]
    public void Generates_Registration_For_Simple_Behavior()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }

                              public sealed class CounterBehavior : IGrainBehavior, ICounterGrain
                              {
                                  public Task IncrementAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("public static partial class QuarkRegistrations", generated);
        // Method name derived from assembly "GeneratorTests"
        Assert.Contains("AddGeneratorTestsBehaviors(", generated);
        Assert.Contains("AddGrainBehavior<global::Demo.ICounterGrain, global::Demo.CounterBehavior>(services)", generated);
        Assert.Contains("AddGrainTransportDispatcher(services,", generated);
        Assert.Contains("GrainType(\"CounterGrain\")", generated);
        Assert.Contains("global::Demo.CounterGrainProxy_TransportDispatcher.Instance", generated);
    }

    [Fact]
    public void Generates_IActivationMemory_Scoped_Registration()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public sealed class CounterState { public int Value { get; set; } }

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }

                              public sealed class CounterBehavior : IGrainBehavior, ICounterGrain
                              {
                                  public CounterBehavior(IActivationMemory<CounterState> memory) { }
                                  public Task IncrementAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("IActivationMemory<global::Demo.CounterState>", generated);
        Assert.Contains("ActivationMemoryAccessor<global::Demo.CounterState>", generated);
        Assert.Contains("GetOrCreateHolder<global::Demo.CounterState>()", generated);
    }

    [Fact]
    public void Generates_IPersistentActivationMemory_Scoped_Registration()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;
                              using Quark.Persistence.Abstractions;

                              namespace Demo;

                              public sealed class OrderState { public decimal Total { get; set; } }

                              public interface IOrderGrain : IGrainWithStringKey
                              {
                                  Task PlaceAsync();
                              }

                              public sealed class OrderBehavior : IGrainBehavior, IOrderGrain
                              {
                                  public OrderBehavior(IPersistentActivationMemory<OrderState> state) { }
                                  public Task PlaceAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("IPersistentActivationMemory<global::Demo.OrderState>", generated);
        Assert.Contains("PersistentActivationMemoryAccessor<global::Demo.OrderState>", generated);
        Assert.Contains("IStorage<global::Demo.OrderState>", generated);
        Assert.Contains("ICallContext", generated);
        Assert.Contains("StorageOptions.DefaultStateName", generated);
    }

    [Fact]
    public void Deduplicates_State_Registrations_Across_Behaviors()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public sealed class SharedState { }

                              public interface IAlphaGrain : IGrainWithStringKey { Task RunAsync(); }
                              public interface IBetaGrain  : IGrainWithStringKey { Task RunAsync(); }

                              public sealed class AlphaBehavior : IGrainBehavior, IAlphaGrain
                              {
                                  public AlphaBehavior(IActivationMemory<SharedState> m) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }

                              public sealed class BetaBehavior : IGrainBehavior, IBetaGrain
                              {
                                  public BetaBehavior(IActivationMemory<SharedState> m) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        // Both behaviors registered
        Assert.Contains("IAlphaGrain, global::Demo.AlphaBehavior", generated);
        Assert.Contains("IBetaGrain, global::Demo.BetaBehavior", generated);

        // State accessor emitted exactly once
        int count = CountOccurrences(generated, "ActivationMemoryAccessor<global::Demo.SharedState>");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Uses_GrainBehavior_Attribute_Key()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public interface IFooGrain : IGrainWithStringKey { Task DoAsync(); }

                              [GrainBehavior("custom-type-key")]
                              public sealed class FooBehavior : IGrainBehavior, IFooGrain
                              {
                                  public Task DoAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("GrainType(\"custom-type-key\")", generated);
    }

    [Fact]
    public void Skips_Abstract_Classes()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public interface IFooGrain : IGrainWithStringKey { Task DoAsync(); }

                              public abstract class BaseBehavior : IGrainBehavior, IFooGrain
                              {
                                  public abstract Task DoAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        // No QuarkRegistrations file emitted (abstract class skipped)
        Assert.Null(result.FindSource("QuarkRegistrations.g.cs"));
    }

    [Fact]
    public void Emits_QRK0050_For_Behavior_Without_Grain_Interface()
    {
        const string source = """
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public sealed class OrphanBehavior : IGrainBehavior { }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        Assert.Contains(result.Diagnostics, d =>
            d.Id == "QRK0050" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage().Contains("OrphanBehavior"));

        Assert.Null(result.FindSource("QuarkRegistrations.g.cs"));
    }

    [Fact]
    public void Generates_IManagedActivationMemory_Scoped_Registration()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public sealed class CircularBuffer { }

                              public interface IWorkerGrain : IGrainWithStringKey
                              {
                                  Task RunAsync();
                              }

                              public sealed class WorkerBehavior : IGrainBehavior, IWorkerGrain
                              {
                                  public WorkerBehavior(IManagedActivationMemory<CircularBuffer> buffer) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("IManagedActivationMemory<global::Demo.CircularBuffer>", generated);
        Assert.Contains("ManagedActivationMemoryAccessor<global::Demo.CircularBuffer>", generated);
        Assert.Contains("GetOrCreateManagedHolder<global::Demo.CircularBuffer>()", generated);
    }

    [Fact]
    public void Deduplicates_Managed_State_Registrations_Across_Behaviors()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public sealed class SharedBuffer { }

                              public interface IAlpha2Grain : IGrainWithStringKey { Task RunAsync(); }
                              public interface IBeta2Grain  : IGrainWithStringKey { Task RunAsync(); }

                              public sealed class Alpha2Behavior : IGrainBehavior, IAlpha2Grain
                              {
                                  public Alpha2Behavior(IManagedActivationMemory<SharedBuffer> m) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }

                              public sealed class Beta2Behavior : IGrainBehavior, IBeta2Grain
                              {
                                  public Beta2Behavior(IManagedActivationMemory<SharedBuffer> m) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        int count = CountOccurrences(generated, "GetOrCreateManagedHolder<global::Demo.SharedBuffer>()");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Sanitizes_Assembly_Name_With_Dots()
    {
        // The test driver always uses "GeneratorTests" as assembly name.
        // We verify the sanitized form appears in the generated method name.
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public interface IBarGrain : IGrainWithStringKey { Task DoAsync(); }

                              public sealed class BarBehavior : IGrainBehavior, IBarGrain
                              {
                                  public Task DoAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        // "GeneratorTests" → "AddGeneratorTestsBehaviors"
        Assert.Contains("AddGeneratorTestsBehaviors(", generated);
    }

    [Fact]
    public void Emits_No_File_When_No_Behaviors_Found()
    {
        const string source = """
                              namespace Demo;

                              public sealed class NotABehavior { }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        Assert.Null(result.FindSource("QuarkRegistrations.g.cs"));
    }

    // -----------------------------------------------------------------------
    // IEagerActivationMemory<T> injection (#76)
    // -----------------------------------------------------------------------

    [Fact]
    public void Generates_IEagerActivationMemory_Registration()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public sealed class ConnectionPool { }

                              public interface IGatewayGrain : IGrainWithStringKey
                              {
                                  Task SendAsync();
                              }

                              public sealed class GatewayBehavior : IGrainBehavior, IGatewayGrain
                              {
                                  public GatewayBehavior(IEagerActivationMemory<ConnectionPool> pool) { }
                                  public Task SendAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("AddEagerActivationMemory<global::Demo.ConnectionPool>(services)", generated);
    }

    [Fact]
    public void Deduplicates_Eager_State_Registrations_Across_Behaviors()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Core.Abstractions.Hosting;

                              namespace Demo;

                              public sealed class SharedPool { }

                              public interface IAlpha3Grain : IGrainWithStringKey { Task RunAsync(); }
                              public interface IBeta3Grain  : IGrainWithStringKey { Task RunAsync(); }

                              public sealed class Alpha3Behavior : IGrainBehavior, IAlpha3Grain
                              {
                                  public Alpha3Behavior(IEagerActivationMemory<SharedPool> m) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }

                              public sealed class Beta3Behavior : IGrainBehavior, IBeta3Grain
                              {
                                  public Beta3Behavior(IEagerActivationMemory<SharedPool> m) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        int count = CountOccurrences(generated, "AddEagerActivationMemory<global::Demo.SharedPool>(services)");
        Assert.Equal(1, count);
    }

    // -----------------------------------------------------------------------
    // [ImplicitStreamSubscription] auto-registration (#75)
    // -----------------------------------------------------------------------

    [Fact]
    public void Generates_AddImplicitStreamSubscription_For_Attributed_Behavior()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Streaming.Abstractions;

                              namespace Demo;

                              public interface IRoomGrain : IGrainWithStringKey { Task PingAsync(); }

                              [ImplicitStreamSubscription("chat")]
                              public sealed class RoomBehavior : IGrainBehavior, IRoomGrain
                              {
                                  public Task PingAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("AddImplicitStreamSubscription(services,", generated);
        Assert.Contains("\"chat\", \"RoomGrain\"", generated);
    }

    [Fact]
    public void Generates_Multiple_ImplicitStreamSubscriptions_Per_Behavior()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Streaming.Abstractions;

                              namespace Demo;

                              public interface IMultiGrain : IGrainWithStringKey { Task PingAsync(); }

                              [ImplicitStreamSubscription("ns-a")]
                              [ImplicitStreamSubscription("ns-b")]
                              public sealed class MultiBehavior : IGrainBehavior, IMultiGrain
                              {
                                  public Task PingAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("\"ns-a\", \"MultiGrain\"", generated);
        Assert.Contains("\"ns-b\", \"MultiGrain\"", generated);
        Assert.Equal(2, CountOccurrences(generated, "AddImplicitStreamSubscription(services,"));
    }

    [Fact]
    public void ImplicitStreamSubscription_Respects_GrainBehavior_Attribute_Key()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Streaming.Abstractions;

                              namespace Demo;

                              public interface IRoomGrain : IGrainWithStringKey { Task PingAsync(); }

                              [GrainBehavior("room-type")]
                              [ImplicitStreamSubscription("chat")]
                              public sealed class RoomBehavior : IGrainBehavior, IRoomGrain
                              {
                                  public Task PingAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("\"chat\", \"room-type\"", generated);
    }

    // -----------------------------------------------------------------------
    // [PersistentState] IPersistentState<T> injection
    // -----------------------------------------------------------------------

    [Fact]
    public void Generates_IPersistentState_Scoped_Registration_With_Default_Provider()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Persistence.Abstractions;

                              namespace Demo;

                              public sealed class BalanceState { public decimal Amount { get; set; } }

                              public interface IWalletGrain : IGrainWithStringKey
                              {
                                  Task<decimal> GetBalanceAsync();
                              }

                              public sealed class WalletBehavior : IGrainBehavior, IWalletGrain
                              {
                                  public WalletBehavior([PersistentState("balance")] IPersistentState<BalanceState> state) { }
                                  public Task<decimal> GetBalanceAsync() => Task.FromResult(0m);
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("IPersistentState<global::Demo.BalanceState>", generated);
        Assert.Contains("PersistentState<global::Demo.BalanceState>", generated);
        Assert.Contains(".Shell.GrainId", generated);
        Assert.Contains("\"balance\"", generated);
        Assert.Contains("GetRequiredService<global::Quark.Persistence.Abstractions.IGrainStorage>()", generated);
        Assert.DoesNotContain("GetRequiredKeyedService", generated);
    }

    [Fact]
    public void Generates_IPersistentState_With_Named_Provider()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Persistence.Abstractions;

                              namespace Demo;

                              public sealed class ProfileState { public string Name { get; set; } = ""; }

                              public interface IProfileGrain : IGrainWithStringKey
                              {
                                  Task SaveAsync();
                              }

                              public sealed class ProfileBehavior : IGrainBehavior, IProfileGrain
                              {
                                  public ProfileBehavior([PersistentState("profile", "redis")] IPersistentState<ProfileState> state) { }
                                  public Task SaveAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        Assert.Contains("IPersistentState<global::Demo.ProfileState>", generated);
        Assert.Contains("GetRequiredKeyedService<global::Quark.Persistence.Abstractions.IGrainStorage>(\"redis\")", generated);
        Assert.Contains("\"profile\"", generated);
    }

    [Fact]
    public void Deduplicates_IPersistentState_Registrations_Across_Behaviors()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Persistence.Abstractions;

                              namespace Demo;

                              public sealed class CounterState { public int Value { get; set; } }

                              public interface IAlphaGrain : IGrainWithStringKey { Task RunAsync(); }
                              public interface IBetaGrain  : IGrainWithStringKey { Task RunAsync(); }

                              public sealed class AlphaBehavior : IGrainBehavior, IAlphaGrain
                              {
                                  public AlphaBehavior([PersistentState("counter")] IPersistentState<CounterState> s) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }

                              public sealed class BetaBehavior : IGrainBehavior, IBetaGrain
                              {
                                  public BetaBehavior([PersistentState("counter")] IPersistentState<CounterState> s) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetRegistrations(result);

        int count = CountOccurrences(generated, "new global::Quark.Persistence.Abstractions.PersistentState<global::Demo.CounterState>(");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Emits_QRK0052_For_Same_T_Different_State_Names()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;
                              using Quark.Persistence.Abstractions;

                              namespace Demo;

                              public sealed class SharedState { }

                              public interface IAlphaGrain : IGrainWithStringKey { Task RunAsync(); }
                              public interface IBetaGrain  : IGrainWithStringKey { Task RunAsync(); }

                              public sealed class AlphaBehavior : IGrainBehavior, IAlphaGrain
                              {
                                  public AlphaBehavior([PersistentState("slotA")] IPersistentState<SharedState> s) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }

                              public sealed class BetaBehavior : IGrainBehavior, IBetaGrain
                              {
                                  public BetaBehavior([PersistentState("slotB")] IPersistentState<SharedState> s) { }
                                  public Task RunAsync() => Task.CompletedTask;
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new BehaviorRegistrationGenerator());

        Assert.Contains(result.Diagnostics, d =>
            d.Id == "QRK0052" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage().Contains("global::Demo.SharedState"));

        // Conflicting T should be omitted from the generated registration
        string generated = GetRegistrations(result);
        Assert.DoesNotContain("SharedState", generated);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string GetRegistrations(GeneratorTestResult result)
    {
        string? source = result.FindSource("QuarkRegistrations.g.cs");
        Assert.NotNull(source);
        return source!;
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        IEnumerable<Diagnostic> errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(errors);
    }

    private static int CountOccurrences(string source, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = source.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
