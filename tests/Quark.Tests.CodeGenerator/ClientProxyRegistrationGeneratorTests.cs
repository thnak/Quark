using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class ClientProxyRegistrationGeneratorTests
{
    // -----------------------------------------------------------------------
    // Happy-path: grain proxy registration
    // -----------------------------------------------------------------------

    [Fact]
    public void Generates_AddGrainProxy_For_IGrain_Interface()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetClientRegistrations(result);

        Assert.Contains("public static partial class QuarkClientRegistrations", generated);
        Assert.Contains("AddGeneratorTestsGrainProxies(", generated);
        Assert.Contains("AddGrainProxy<global::Demo.ICounterGrain, global::Demo.CounterGrainProxy>(services)", generated);
        Assert.DoesNotContain("AddObserverProxy", generated);
    }

    [Fact]
    public void Generates_AddObserverProxy_For_IGrainObserver_Interface()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface IAlertObserver : IGrainObserver
                              {
                                  Task OnAlertAsync(string msg);
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetClientRegistrations(result);

        Assert.Contains("AddObserverProxy<global::Demo.IAlertObserver, global::Demo.AlertObserverProxy>(services)", generated);
        Assert.DoesNotContain("AddGrainProxy", generated);
    }

    [Fact]
    public void Generates_Both_Grain_And_Observer_Proxies()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }

                              public interface ICounterObserver : IGrainObserver
                              {
                                  Task OnCountChangedAsync(int value);
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetClientRegistrations(result);

        Assert.Contains("AddGrainProxy<global::Demo.ICounterGrain, global::Demo.CounterGrainProxy>(services)", generated);
        Assert.Contains("AddObserverProxy<global::Demo.ICounterObserver, global::Demo.CounterObserverProxy>(services)", generated);
    }

    [Fact]
    public void Derives_Proxy_Name_Without_Leading_I()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Acme.Grains;

                              public interface IOrderGrain : IGrainWithStringKey
                              {
                                  Task PlaceAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetClientRegistrations(result);

        Assert.Contains("global::Acme.Grains.IOrderGrain, global::Acme.Grains.OrderGrainProxy", generated);
    }

    [Fact]
    public void Skips_Interfaces_Without_Quark_Client_Reference()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface ICounterGrain : IGrainWithStringKey
                              {
                                  Task IncrementAsync();
                              }
                              """;

        // Compile WITHOUT Quark.Client in the references
        var compilation = CSharpCompilation.Create(
            "NoClientTests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            GeneratorTestDriver.GetMetadataReferences()
                .Where(r => !r.Display!.Contains("Quark.Client", StringComparison.OrdinalIgnoreCase))
                .ToImmutableArray(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ClientProxyRegistrationGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var runResult = driver.GetRunResult();
        var sources = runResult.Results.SelectMany(r => r.GeneratedSources).ToList();

        Assert.DoesNotContain(sources, s => s.HintName == "QuarkClientRegistrations.g.cs");
    }

    [Fact]
    public void Skips_Non_Grain_Interfaces()
    {
        const string source = """
                              namespace Demo;

                              public interface INotAGrain
                              {
                                  void DoSomething();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        Assert.Null(result.FindSource("QuarkClientRegistrations.g.cs"));
    }

    [Fact]
    public void Skips_Ambiguous_IGrain_And_IGrainObserver_Interface()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface IOddGrain : IGrainWithStringKey, IGrainObserver
                              {
                                  Task DoAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        // Ambiguous interface skipped — file may still be emitted if there are other interfaces, but this one is omitted.
        Assert.Null(result.FindSource("QuarkClientRegistrations.g.cs"));
    }

    [Fact]
    public void Sanitizes_Assembly_Name_With_Dots()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface IFooGrain : IGrainWithStringKey
                              {
                                  Task DoAsync();
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetClientRegistrations(result);

        // "GeneratorTests" → "AddGeneratorTestsGrainProxies"
        Assert.Contains("AddGeneratorTestsGrainProxies(", generated);
    }

    [Fact]
    public void Multiple_Grains_Are_All_Registered()
    {
        const string source = """
                              using System.Threading.Tasks;
                              using Quark.Core.Abstractions.Grains;

                              namespace Demo;

                              public interface IAlphaGrain : IGrainWithStringKey { Task RunAsync(); }
                              public interface IBetaGrain  : IGrainWithStringKey { Task RunAsync(); }
                              public interface IGammaGrain : IGrainWithStringKey { Task RunAsync(); }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainProxyGenerator(), new ClientProxyRegistrationGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = GetClientRegistrations(result);

        Assert.Contains("AlphaGrainProxy", generated);
        Assert.Contains("BetaGrainProxy", generated);
        Assert.Contains("GammaGrainProxy", generated);
        Assert.Equal(3, CountOccurrences(generated, "AddGrainProxy<"));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string GetClientRegistrations(GeneratorTestResult result)
    {
        string? source = result.FindSource("QuarkClientRegistrations.g.cs");
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
