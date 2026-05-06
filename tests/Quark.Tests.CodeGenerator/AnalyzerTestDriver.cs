using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Tests.CodeGenerator;

internal static class AnalyzerTestDriver
{
    public static ImmutableArray<Diagnostic> Run(string source, DiagnosticAnalyzer analyzer)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));

        var compilation = CSharpCompilation.Create(
            "AnalyzerTests",
            [syntaxTree],
            GeneratorTestDriver.GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers([analyzer]);
        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult().ToImmutableArray();
    }
}
