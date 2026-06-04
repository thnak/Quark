using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class GrainProxyGeneratorTests
{
    [Fact]
    public void Generates_Proxy_For_Grain_Interface()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task<long> IncrementAsync();
                                  ValueTask ResetAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class CounterGrainProxy", generated);
        Assert.Contains(": global::Demo.ICounterGrain", generated);
        Assert.Contains(
            ", global::Quark.Core.Abstractions.Hosting.IGrainProxyActivator<CounterGrainProxy>",
            generated);
        Assert.Contains("public static CounterGrainProxy Create(", generated);
        Assert.Contains("=> new CounterGrainProxy(grainId, invoker);", generated);
        Assert.Contains("internal readonly struct CounterGrainProxy_IncrementAsyncInvokable", generated);
        Assert.Contains("internal readonly struct CounterGrainProxy_ResetAsyncInvokable", generated);
        Assert.Contains("_invoker.InvokeAsync<CounterGrainProxy_IncrementAsyncInvokable, long>(_grainId, new CounterGrainProxy_IncrementAsyncInvokable())", generated);
        Assert.Contains(
            "return new global::System.Threading.Tasks.ValueTask(_invoker.InvokeVoidAsync(_grainId, new CounterGrainProxy_ResetAsyncInvokable()));",
            generated);
        Assert.Contains("internal sealed class CounterGrainProxy_TransportDispatcher", generated);
        Assert.Contains(": global::Quark.Core.Abstractions.Hosting.ITransportGrainDispatcher", generated);
        Assert.Contains("DispatchAsync(", generated);
        Assert.Contains("invoker.InvokeAsync<CounterGrainProxy_IncrementAsyncInvokable, long>(", generated);
        Assert.Contains("invoker.InvokeVoidAsync<CounterGrainProxy_ResetAsyncInvokable>(", generated);
    }

    [Fact]
    public void Generates_Proxy_For_Observer_Interface()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface IMyObserver : IGrainObserver
                              {
                                  Task OnEventAsync(string message);
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class MyObserverProxy", generated);
        Assert.Contains(": global::Demo.IMyObserver", generated);
        Assert.Contains(
            ", global::Quark.Core.Abstractions.Hosting.IGrainObserverProxyActivator<MyObserverProxy>",
            generated);
        Assert.Contains("public static MyObserverProxy Create(", generated);
        Assert.Contains("=> new MyObserverProxy(grainId, invoker);", generated);
        Assert.Contains("internal readonly struct MyObserverProxy_OnEventAsyncInvokable", generated);
        Assert.Contains(": global::Quark.Core.Abstractions.Hosting.IObserverVoidInvokable", generated);
        Assert.Contains("InvokeObserverAsync(_grainId, new MyObserverProxy_OnEventAsyncInvokable(", generated);
        Assert.Contains("internal sealed class MyObserverProxy_TransportDispatcher", generated);
        Assert.Contains("invoker.InvokeObserverAsync<MyObserverProxy_OnEventAsyncInvokable>(", generated);
    }

    [Fact]
    public void Ignores_Non_Grain_Interfaces()
    {
        const string source = """
                              namespace Demo;

                              public interface IUtilityContract
                              {
                                  void Ping();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator());

        AssertNoErrors(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
