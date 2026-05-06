using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quark.Core.Abstractions.Grains;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Attributes;

namespace Quark.Tests.CodeGenerator;

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
