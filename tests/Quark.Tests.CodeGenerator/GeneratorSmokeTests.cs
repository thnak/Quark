using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quark.Analyzers;
using Quark.CodeGenerator;
using Quark.Core.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class SerializerGeneratorTests
{
    [Fact]
    public void Generates_Codec_And_Copier_For_Attributed_Type()
    {
        const string source = """
using Quark.Serialization.Abstractions;

namespace Demo;

[GenerateSerializer]
public sealed class Person
{
    [Id(0)] public string? Name { get; set; }
    [Id(1)] public int Age { get; set; }
}
""";

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class PersonCodec", generated);
        Assert.Contains("internal sealed class PersonCopier", generated);
        Assert.Contains("if (value is null)", generated);
        Assert.Contains("if (field.WireType == global::Quark.Serialization.Abstractions.WireType.Extended", generated);
        Assert.Contains("case 0: result.Name =", generated);
        Assert.Contains("case 1: result.Age =", generated);
        Assert.Contains("private readonly global::Quark.Serialization.Abstractions.ICopierProvider _copiers;", generated);
        Assert.Contains("_copiers.GetRequiredCopier<", generated);
        Assert.Contains("DeepCopy(input.Name, context)", generated);
    }

    [Fact]
    public void Ignores_Types_Without_Id_Members()
    {
        const string source = """
using Quark.Serialization.Abstractions;

namespace Demo;

[GenerateSerializer]
public sealed class EmptyPayload
{
    public string? Name { get; set; }
}
""";

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}

public sealed class ReflectionUsageAnalyzerTests
{
    [Fact]
    public void Reports_DynamicAssemblyLoad()
    {
        const string source = """
using System.Reflection;

namespace Demo;

public static class Loader
{
    public static Assembly LoadIt() => Assembly.Load("Demo.Plugin");
}
""";

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReflectionUsageAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0002");
    }

    [Fact]
    public void Reports_ISerializable_Implementation()
    {
        const string source = """
using System;
using System.Runtime.Serialization;

namespace Demo;

public sealed class LegacyPayload : ISerializable
{
    public void GetObjectData(SerializationInfo info, StreamingContext context) { }
}
""";

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReflectionUsageAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0003");
    }
}

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

public sealed class GrainProxyGeneratorTests
{
    [Fact]
    public void Generates_Proxy_For_Grain_Interface()
    {
        const string source = """
using System.Threading.Tasks;
using Quark.Core.Abstractions;

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
        Assert.Contains("internal sealed class CounterGrainProxy : global::Demo.ICounterGrain", generated);
        Assert.Contains("_invoker.InvokeAsync<", generated);
        Assert.Contains("_grainId, 0u, null", generated);
        Assert.Contains("return new global::System.Threading.Tasks.ValueTask(_invoker.InvokeVoidAsync(_grainId, 1u, null));", generated);
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

internal static class GeneratorTestDriver
{
    public static GeneratorTestResult Run(string source, IIncrementalGenerator generator)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [syntaxTree],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)syntaxTree.Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        ImmutableArray<string> generatedSources = runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static sourceResult => sourceResult.SourceText.ToString())
            .ToImmutableArray();

        ImmutableArray<Diagnostic> diagnostics = generatorDiagnostics
            .AddRange(outputCompilation.GetDiagnostics());

        return new GeneratorTestResult(outputCompilation, generatedSources, diagnostics);
    }

    internal static ImmutableArray<MetadataReference> GetMetadataReferences()
    {
        string[] trustedPlatformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return trustedPlatformAssemblies
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append((MetadataReference)MetadataReference.CreateFromFile(typeof(IGrain).Assembly.Location))
            .Append((MetadataReference)MetadataReference.CreateFromFile(typeof(IGrainActivator).Assembly.Location))
            .Append((MetadataReference)MetadataReference.CreateFromFile(typeof(GenerateSerializerAttribute).Assembly.Location))
            .GroupBy(static reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToImmutableArray();
    }
}

internal static class AnalyzerTestDriver
{
    public static ImmutableArray<Diagnostic> Run(string source, DiagnosticAnalyzer analyzer)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: [syntaxTree],
            references: GeneratorTestDriver.GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers([analyzer]);
        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().ToImmutableArray();
    }
}

internal readonly record struct GeneratorTestResult(
    Compilation Compilation,
    ImmutableArray<string> GeneratedSources,
    ImmutableArray<Diagnostic> Diagnostics);
