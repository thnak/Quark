using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Quark.Analyzers;

/// <summary>
///     Diagnostic analyzer that detects reflection-heavy APIs that are incompatible with
///     Native AOT in Quark assemblies and reports actionable errors.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReflectionUsageAnalyzer : DiagnosticAnalyzer
{
    // QRK0001 — Type.GetType() with a non-literal string
    public static readonly DiagnosticDescriptor DynamicTypeResolution = new(
        "QRK0001",
        "Avoid dynamic type resolution",
        "'{0}' uses dynamic type resolution which is incompatible with Native AOT. " +
        "Use typeof(T) or a source-generated factory instead.",
        "Quark.AOT",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/thnak/Quark/docs/aot.md");

    // QRK0002 — Assembly.Load / Assembly.LoadFrom
    public static readonly DiagnosticDescriptor DynamicAssemblyLoad = new(
        "QRK0002",
        "Avoid dynamic assembly loading",
        "'{0}' loads assemblies dynamically which is not supported in Native AOT. " +
        "Register providers explicitly via AddXxx() extension methods.",
        "Quark.AOT",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/thnak/Quark/docs/aot.md");

    // QRK0003 — ISerializable usage
    public static readonly DiagnosticDescriptor ISerializableUsage = new(
        "QRK0003",
        "Do not use ISerializable",
        "Type '{0}' implements ISerializable which requires DynamicMethod and is " +
        "incompatible with Native AOT. Use [GenerateSerializer] with [Id] instead.",
        "Quark.AOT",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: "https://github.com/thnak/Quark/docs/aot.md");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DynamicTypeResolution, DynamicAssemblyLoad, ISerializableUsage);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeType(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not INamedTypeSymbol type)
        {
            return;
        }

        // Check for ISerializable implementation
        foreach (INamedTypeSymbol iface in type.Interfaces)
        {
            if (iface.ToDisplayString() == "System.Runtime.Serialization.ISerializable")
            {
                ctx.ReportDiagnostic(
                    Diagnostic.Create(ISerializableUsage, type.Locations[0], type.Name));
                break;
            }
        }
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IInvocationOperation invocation)
        {
            return;
        }

        IMethodSymbol method = invocation.TargetMethod;
        string containingTypeName = method.ContainingType?.ToDisplayString() ?? string.Empty;
        string methodName = method.Name;

        // Detect Type.GetType(string) with dynamic input
        if (containingTypeName == "System.Type" && methodName == "GetType")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                DynamicTypeResolution,
                invocation.Syntax.GetLocation(),
                $"Type.{methodName}"));
        }

        // Detect Assembly.Load / Assembly.LoadFrom / Assembly.LoadFile
        if (containingTypeName == "System.Reflection.Assembly" &&
            methodName is "Load" or "LoadFrom" or "LoadFile")
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                DynamicAssemblyLoad,
                invocation.Syntax.GetLocation(),
                $"Assembly.{methodName}"));
        }
    }
}
