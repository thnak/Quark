using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Quark.Tests.CodeGenerator;

internal static class CodeFixTestDriver
{
    public static async Task<string> ApplyFixAsync(
        string source,
        DiagnosticAnalyzer analyzer,
        CodeFixProvider fixer,
        string diagnosticId,
        int fixIndex = 0)
    {
        using var workspace = new AdhocWorkspace();

        Project project = workspace.AddProject("FixTests", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
            .AddMetadataReferences(GeneratorTestDriver.GetMetadataReferences());

        Document document = project.AddDocument("Test.cs", SourceText.From(source));

        Compilation? compilation = await document.Project.GetCompilationAsync(CancellationToken.None).ConfigureAwait(false);
        if (compilation == null)
            throw new InvalidOperationException("Compilation failed.");

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).ConfigureAwait(false);

        Diagnostic? diagnostic = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
        if (diagnostic == null)
            throw new InvalidOperationException($"No diagnostic '{diagnosticId}' was reported.");

        var actions = new List<CodeAction>();
        var ctx = new CodeFixContext(document, diagnostic, (a, _) => actions.Add(a), CancellationToken.None);
        await fixer.RegisterCodeFixesAsync(ctx).ConfigureAwait(false);

        if (actions.Count <= fixIndex)
            throw new InvalidOperationException($"No code fix action at index {fixIndex}. Found {actions.Count} action(s).");

        ImmutableArray<CodeActionOperation> ops = await actions[fixIndex]
            .GetOperationsAsync(CancellationToken.None)
            .ConfigureAwait(false);

        ApplyChangesOperation? applyOp = ops.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOp == null)
            throw new InvalidOperationException("Code fix produced no ApplyChangesOperation.");

        Solution changedSolution = applyOp.ChangedSolution;
        Document? changedDoc = changedSolution.GetDocument(document.Id);
        if (changedDoc == null)
            throw new InvalidOperationException("Document not found in changed solution.");

        SourceText text = await changedDoc.GetTextAsync(CancellationToken.None).ConfigureAwait(false);
        return text.ToString();
    }
}
