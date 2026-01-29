using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for enforcing async return types on actor methods.
/// Actor methods should return Task, ValueTask, Task&lt;T&gt;, or ValueTask&lt;T&gt; to maintain AOT compatibility.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ActorMethodSignatureAnalyzer : DiagnosticAnalyzer
{
    public const string SyncMethodDiagnosticId = "QUARK004";
    
    private static readonly DiagnosticDescriptor SyncMethodRule = new DiagnosticDescriptor(
        SyncMethodDiagnosticId,
        "Actor method should be async",
        "Actor method '{0}' should return Task, ValueTask, Task<T>, or ValueTask<T> instead of '{1}'",
        "Quark.Actors",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Actor methods should be asynchronous to support distributed calls and maintain responsiveness.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(SyncMethodRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Only analyze public or internal methods in actor classes
        if (methodSymbol.DeclaredAccessibility != Accessibility.Public &&
            methodSymbol.DeclaredAccessibility != Accessibility.Internal)
            return;

        // Skip special methods
        if (methodSymbol.MethodKind != MethodKind.Ordinary)
            return;

        // Skip methods with special names (constructors, property getters/setters, etc.)
        var methodName = methodSymbol.Name;
        if (methodName.StartsWith("get_") || methodName.StartsWith("set_") || 
            methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
            return;

        // Check if the containing class has [Actor] attribute
        var containingClass = methodSymbol.ContainingType;
        if (containingClass == null)
            return;

        var hasActorAttribute = containingClass.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "Quark.Abstractions.ActorAttribute");

        if (!hasActorAttribute)
        {
            // Also check if it inherits from ActorBase
            var baseType = containingClass.BaseType;
            var isActorClass = false;
            while (baseType != null)
            {
                if (baseType.Name == "ActorBase" || baseType.Name == "StatefulActorBase")
                {
                    isActorClass = true;
                    break;
                }
                baseType = baseType.BaseType;
            }

            if (!isActorClass)
                return;
        }

        // Check return type
        var returnType = methodSymbol.ReturnType;
        var returnTypeString = returnType.ToDisplayString();

        // Allow Task, ValueTask, Task<T>, ValueTask<T>
        if (IsAsyncReturnType(returnType))
            return;

        // Report diagnostic for non-async methods
        var diagnostic = Diagnostic.Create(
            SyncMethodRule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name,
            returnTypeString);
        
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsAsyncReturnType(ITypeSymbol returnType)
    {
        var typeName = returnType.ToDisplayString();

        // Check for Task, ValueTask, Task<T>, ValueTask<T>
        if (typeName == "System.Threading.Tasks.Task")
            return true;

        if (typeName == "System.Threading.Tasks.ValueTask")
            return true;

        if (typeName.StartsWith("System.Threading.Tasks.Task<"))
            return true;

        if (typeName.StartsWith("System.Threading.Tasks.ValueTask<"))
            return true;

        return false;
    }
}
