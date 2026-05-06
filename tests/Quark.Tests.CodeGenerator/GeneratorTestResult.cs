using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Quark.Tests.CodeGenerator;

internal readonly record struct GeneratorTestResult(
    Compilation Compilation,
    ImmutableArray<string> GeneratedSources,
    ImmutableArray<Diagnostic> Diagnostics);