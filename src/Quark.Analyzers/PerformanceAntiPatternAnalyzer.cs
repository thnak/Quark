using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for detecting performance anti-patterns in actor methods.
/// Identifies blocking calls, synchronous I/O, and other performance issues.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PerformanceAntiPatternAnalyzer : DiagnosticAnalyzer
{
    public const string BlockingCallDiagnosticId = "QUARK008";
    public const string SyncIoDiagnosticId = "QUARK009";
    
    private static readonly DiagnosticDescriptor BlockingCallRule = new DiagnosticDescriptor(
        BlockingCallDiagnosticId,
        "Blocking call detected in actor method",
        "Actor method '{0}' contains blocking call '{1}' which can cause thread starvation and deadlocks",
        "Quark.Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Avoid blocking calls like Thread.Sleep, Task.Wait, Task.Result, and .GetAwaiter().GetResult() in actor methods. " +
                     "Use 'await' with async APIs instead to maintain responsiveness and prevent thread pool starvation.");

    private static readonly DiagnosticDescriptor SyncIoRule = new DiagnosticDescriptor(
        SyncIoDiagnosticId,
        "Synchronous I/O detected in actor method",
        "Actor method '{0}' uses synchronous I/O method '{1}' which can block the thread pool",
        "Quark.Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Synchronous file I/O methods (File.ReadAllText, File.WriteAllText, etc.) block threads. " +
                     "Use async alternatives like File.ReadAllTextAsync and File.WriteAllTextAsync for better performance and scalability.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [BlockingCallRule, SyncIoRule];

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
        if (containingClass == null || !IsActorClass(containingClass))
            return;

        // Analyze method body for anti-patterns
        if (methodDeclaration.Body == null && methodDeclaration.ExpressionBody == null)
            return;

        // Check for blocking calls
        AnalyzeBlockingCalls(context, methodDeclaration, methodSymbol, semanticModel);
        
        // Check for synchronous I/O
        AnalyzeSynchronousIo(context, methodDeclaration, methodSymbol, semanticModel);
    }

    private static bool IsActorClass(INamedTypeSymbol classSymbol)
    {
        // Check for [Actor] attribute
        var hasActorAttribute = classSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "Quark.Abstractions.ActorAttribute");

        if (hasActorAttribute)
            return true;

        // Check if it inherits from ActorBase
        var baseType = classSymbol.BaseType;
        while (baseType != null)
        {
            if (baseType.Name == "ActorBase" || baseType.Name == "StatefulActorBase")
                return true;
            baseType = baseType.BaseType;
        }

        return false;
    }

    private static void AnalyzeBlockingCalls(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        var invocations = methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var targetMethod = symbolInfo.Symbol as IMethodSymbol;
            
            if (targetMethod == null)
                continue;

            var fullName = $"{targetMethod.ContainingType?.ToDisplayString()}.{targetMethod.Name}";
            
            // Check for common blocking patterns
            if (IsBlockingCall(fullName, targetMethod))
            {
                var diagnostic = Diagnostic.Create(
                    BlockingCallRule,
                    invocation.GetLocation(),
                    methodSymbol.Name,
                    targetMethod.Name);
                
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check for property access that blocks (Task.Result, Task.Wait)
        var memberAccesses = methodDeclaration.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>();

        foreach (var memberAccess in memberAccesses)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            var symbol = symbolInfo.Symbol;
            
            if (symbol == null)
                continue;

            var memberName = symbol.Name;
            var containingType = symbol.ContainingType?.ToDisplayString();

            // Check for Task.Result, Task.Wait()
            if ((containingType == "System.Threading.Tasks.Task" || 
                 containingType?.StartsWith("System.Threading.Tasks.Task<") == true) &&
                (memberName == "Result" || memberName == "Wait"))
            {
                var diagnostic = Diagnostic.Create(
                    BlockingCallRule,
                    memberAccess.GetLocation(),
                    methodSymbol.Name,
                    memberName);
                
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsBlockingCall(string fullName, IMethodSymbol method)
    {
        // Thread.Sleep
        if (fullName == "System.Threading.Thread.Sleep")
            return true;

        // Task.Wait, Task.WaitAll, Task.WaitAny
        if (fullName.StartsWith("System.Threading.Tasks.Task.Wait"))
            return true;

        // GetAwaiter().GetResult() pattern
        if (method.Name == "GetResult" && 
            method.ContainingType?.Name == "TaskAwaiter")
            return true;

        // Monitor.Enter, Monitor.Wait
        if (fullName.StartsWith("System.Threading.Monitor."))
            return true;

        // Semaphore/Mutex Wait
        if (fullName.Contains("WaitOne") && 
            (method.ContainingType?.Name == "Semaphore" || 
             method.ContainingType?.Name == "Mutex" ||
             method.ContainingType?.Name == "EventWaitHandle"))
            return true;

        return false;
    }

    private static void AnalyzeSynchronousIo(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel)
    {
        var invocations = methodDeclaration.DescendantNodes()
            .OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var targetMethod = symbolInfo.Symbol as IMethodSymbol;
            
            if (targetMethod == null)
                continue;

            var containingType = targetMethod.ContainingType?.ToDisplayString();
            var methodName = targetMethod.Name;

            // Check for synchronous File I/O methods
            if (containingType == "System.IO.File" && IsSynchronousIoMethod(methodName))
            {
                var diagnostic = Diagnostic.Create(
                    SyncIoRule,
                    invocation.GetLocation(),
                    methodSymbol.Name,
                    methodName);
                
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static bool IsSynchronousIoMethod(string methodName)
    {
        // List of common synchronous File I/O methods
        var syncMethods = new[]
        {
            "ReadAllText",
            "ReadAllLines",
            "ReadAllBytes",
            "WriteAllText",
            "WriteAllLines",
            "WriteAllBytes",
            "AppendAllText",
            "AppendAllLines",
            "Copy",
            "Move"
        };

        return syncMethods.Contains(methodName);
    }
}
