using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Quark.Tests.CodeGenerator;

internal readonly record struct GeneratorTestResult(
    Compilation Compilation,
    ImmutableArray<(string HintName, string Text)> NamedSources,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public ImmutableArray<string> GeneratedSources =>
        NamedSources.Select(static s => s.Text).ToImmutableArray();

    public string? FindSource(string hintName) =>
        NamedSources.FirstOrDefault(s => s.HintName == hintName).Text;
}
