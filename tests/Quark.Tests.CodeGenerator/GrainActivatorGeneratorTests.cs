using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class GrainActivatorGeneratorTests
{
    [Fact]
    public void Generates_ActivatorFactory_For_Grain_Class()
    {
        const string source = """
                              using Quark.Core.Abstractions;

                              namespace Demo;

                              public interface IClock { }

                              public sealed class CounterGrain : Grain
                              {
                                  public CounterGrain(IClock clock) { }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new GrainActivatorGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class CounterGrainActivatorFactory", generated);
        Assert.Contains(": global::Quark.Runtime.IGrainActivatorFactory", generated);
        Assert.Contains("public global::System.Type GrainClass => typeof(global::Demo.CounterGrain);", generated);
        Assert.Contains("GetRequiredService<global::Demo.IClock>(services)", generated);
        Assert.Contains("return new global::Demo.CounterGrain(", generated);
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
