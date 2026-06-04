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
        Assert.Contains("_invoker.InvokeAsync<", generated);
        Assert.Contains("_grainId, 0u, null", generated);
        Assert.Contains(
            "return new global::System.Threading.Tasks.ValueTask(_invoker.InvokeVoidAsync(_grainId, 1u, null));",
            generated);
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
        Assert.Contains("InvokeVoidAsync(_grainId, 0u,", generated);
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
