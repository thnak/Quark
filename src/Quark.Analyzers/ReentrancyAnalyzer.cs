using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for detecting potential reentrancy issues in actor methods.
/// Warns when actor methods might cause circular calls that could lead to deadlocks or stack overflows.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReentrancyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "QUARK007";
    
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "Potential reentrancy issue detected",
        "Method '{0}' may cause reentrancy: calling '{1}' on same actor could lead to circular calls",
        "Quark.Actors",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Actor methods calling other methods on the same actor instance (e.g., 'this.MethodAsync()') can cause reentrancy issues. " +
                     "Consider using non-reentrant actors or restructuring the logic to avoid self-calls. " +
                     "This warning is only raised if the actor is marked with [Actor(Reentrant = false)].");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

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

        // Only analyze public or internal methods
        if (methodSymbol.DeclaredAccessibility != Accessibility.Public &&
            methodSymbol.DeclaredAccessibility != Accessibility.Internal)
            return;

        // Check if the containing class is an actor
        var containingClass = methodSymbol.ContainingType;
        if (containingClass == null)
            return;

        // Check for [Actor] attribute
        var actorAttribute = containingClass.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == "Quark.Abstractions.ActorAttribute");

        // If no [Actor] attribute, check if it inherits from ActorBase
        var isActorClass = actorAttribute != null;
        if (!isActorClass)
        {
            var baseType = containingClass.BaseType;
            while (baseType != null)
            {
                if (baseType.Name == "ActorBase" || baseType.Name == "StatefulActorBase")
                {
                    isActorClass = true;
                    break;
                }
                baseType = baseType.BaseType;
            }
        }

        if (!isActorClass)
            return;

        // Check if actor is marked as non-reentrant (Reentrant = false)
        // Default is non-reentrant, so we warn unless explicitly marked Reentrant = true
        var isReentrant = false;
        if (actorAttribute != null)
        {
            var reentrantArg = actorAttribute.NamedArguments
                .FirstOrDefault(arg => arg.Key == "Reentrant");
            
            if (reentrantArg.Key == "Reentrant")
            {
                if (reentrantArg.Value.Value is bool boolValue)
                {
                    isReentrant = boolValue;
                }
            }
        }

        // If actor is explicitly marked as reentrant, don't warn
        if (isReentrant)
            return;

        // Analyze method body for self-calls
        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
            return;

        var selfCalls = FindSelfCalls(methodDeclaration, semanticModel, containingClass);
        
        foreach (var (invocation, targetMethod) in selfCalls)
        {
            var diagnostic = Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                methodSymbol.Name,
                targetMethod);
            
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static IEnumerable<(InvocationExpressionSyntax, string)> FindSelfCalls(
        MethodDeclarationSyntax methodDeclaration,
        SemanticModel semanticModel,
        INamedTypeSymbol containingClass)
    {
        var result = new List<(InvocationExpressionSyntax, string)>();

        // Get all invocations in the method
        var invocations = methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            // Check if this is a member access expression (e.g., this.MethodAsync())
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                // Check if it's on 'this'
                if (memberAccess.Expression is ThisExpressionSyntax)
                {
                    var targetSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    if (targetSymbol != null && 
                        SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, containingClass))
                    {
                        result.Add((invocation, targetSymbol.Name));
                    }
                }
            }
            // Check for simple invocation (e.g., MethodAsync() without explicit this)
            else if (invocation.Expression is IdentifierNameSyntax)
            {
                var targetSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (targetSymbol != null && 
                    SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, containingClass))
                {
                    result.Add((invocation, targetSymbol.Name));
                }
            }
        }

        return result;
    }
}
