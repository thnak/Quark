using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer for detecting problematic IQuarkActor inheritance patterns.
/// Validates that IQuarkActor interfaces are inherited correctly to avoid resolution troubles.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class QuarkActorInheritanceAnalyzer : DiagnosticAnalyzer
{
    public const string MultipleImplementationsDiagnosticId = "QUARK010";
    public const string DeepInheritanceChainDiagnosticId = "QUARK011";
    
    private static readonly DiagnosticDescriptor MultipleImplementationsRule = new DiagnosticDescriptor(
        MultipleImplementationsDiagnosticId,
        "Multiple classes implement the same IQuarkActor interface",
        "Interface '{0}' is implemented by multiple classes which can cause actor resolution troubles in the cluster",
        "Quark.Actors",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each IQuarkActor interface should typically be implemented by a single concrete class to ensure proper actor resolution in the cluster. Multiple implementations can lead to ambiguity in actor routing.");

    private static readonly DiagnosticDescriptor DeepInheritanceChainRule = new DiagnosticDescriptor(
        DeepInheritanceChainDiagnosticId,
        "Deep inheritance chain detected for IQuarkActor implementation",
        "Class '{0}' has an inheritance depth of {1} which may cause performance issues and resolution troubles",
        "Quark.Actors",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Deep inheritance chains (depth > 3) can impact performance and make actor resolution more complex. Consider using composition instead of deep inheritance.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MultipleImplementationsRule, DeepInheritanceChainRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register compilation-level analysis to check for multiple implementations
        context.RegisterCompilationAction(AnalyzeCompilation);
        
        // Register class-level analysis for inheritance depth
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        // Track which IQuarkActor interfaces are implemented by which classes
        var interfaceImplementations = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);

        // Get all named types in the compilation
        var allTypes = GetAllTypes(context.Compilation.GlobalNamespace);

        foreach (var type in allTypes)
        {
            // Skip abstract classes and interfaces
            if (type.TypeKind != TypeKind.Class || type.IsAbstract)
                continue;

            // Find all IQuarkActor interfaces this class implements
            foreach (var implementedInterface in type.AllInterfaces)
            {
                if (ImplementsIQuarkActor(implementedInterface))
                {
                    if (!interfaceImplementations.ContainsKey(implementedInterface))
                    {
                        interfaceImplementations[implementedInterface] = new List<INamedTypeSymbol>();
                    }
                    interfaceImplementations[implementedInterface].Add(type);
                }
            }
        }

        // Report diagnostics for interfaces with multiple implementations
        foreach (var kvp in interfaceImplementations)
        {
            if (kvp.Value.Count > 1)
            {
                var interfaceSymbol = kvp.Key;
                foreach (var implementingClass in kvp.Value)
                {
                    // Find the syntax node for this class
                    var syntaxReferences = implementingClass.DeclaringSyntaxReferences;
                    if (syntaxReferences.Length > 0)
                    {
                        var syntax = syntaxReferences[0].GetSyntax(context.CancellationToken);
                        if (syntax is ClassDeclarationSyntax classDecl)
                        {
                            var diagnostic = Diagnostic.Create(
                                MultipleImplementationsRule,
                                classDecl.Identifier.GetLocation(),
                                interfaceSymbol.Name);
                            
                            context.ReportDiagnostic(diagnostic);
                        }
                    }
                }
            }
        }
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null || classSymbol.IsAbstract)
            return;

        // Check if this class implements IQuarkActor (directly or indirectly)
        var implementsQuarkActor = classSymbol.AllInterfaces.Any(ImplementsIQuarkActor);
        if (!implementsQuarkActor)
            return;

        // Calculate inheritance depth
        var depth = GetInheritanceDepth(classSymbol);
        
        // Warn if depth is greater than 3 (ActorBase -> CustomBase -> YourActor -> etc.)
        if (depth > 3)
        {
            var diagnostic = Diagnostic.Create(
                DeepInheritanceChainRule,
                classDeclaration.Identifier.GetLocation(),
                classSymbol.Name,
                depth);
            
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool ImplementsIQuarkActor(INamedTypeSymbol interfaceSymbol)
    {
        // Check if this interface is IQuarkActor
        if (interfaceSymbol.Name == "IQuarkActor")
            return true;

        // Check if this interface inherits from IQuarkActor
        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            if (baseInterface.Name == "IQuarkActor")
                return true;
        }

        return false;
    }

    private static int GetInheritanceDepth(INamedTypeSymbol typeSymbol)
    {
        var depth = 0;
        var current = typeSymbol.BaseType;
        
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            depth++;
            current = current.BaseType;
        }
        
        return depth;
    }

    private static List<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        var types = new List<INamedTypeSymbol>();
        
        // Add types in this namespace
        types.AddRange(namespaceSymbol.GetTypeMembers());
        
        // Recursively add types from nested namespaces
        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            types.AddRange(GetAllTypes(nestedNamespace));
        }
        
        return types;
    }
}
