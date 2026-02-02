using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quark.Analyzers;

/// <summary>
/// Analyzer that detects types used in actor interfaces that are missing ProtoBuf serialization attributes.
/// Actor interface parameter and return types must have [ProtoContract] and [ProtoMember] attributes
/// for proper serialization in distributed calls.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MissingProtoContractAnalyzer : DiagnosticAnalyzer
{
    public const string MissingProtoContractDiagnosticId = "QUARK014";
    public const string MissingProtoMemberDiagnosticId = "QUARK015";

    private static readonly DiagnosticDescriptor ProtoContractRule = new DiagnosticDescriptor(
        MissingProtoContractDiagnosticId,
        "Type is missing [ProtoContract] attribute",
        "Type '{0}' used in actor interface should have [ProtoContract] attribute for ProtoBuf serialization",
        "Quark.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Types used as parameters or return values in actor interfaces must have [ProtoContract] attribute for proper serialization in distributed actor calls.");

    private static readonly DiagnosticDescriptor ProtoMemberRule = new DiagnosticDescriptor(
        MissingProtoMemberDiagnosticId,
        "Property is missing [ProtoMember] attribute",
        "Property '{0}' in type '{1}' should have [ProtoMember(n)] attribute for ProtoBuf serialization",
        "Quark.Serialization",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All properties in types marked with [ProtoContract] must have [ProtoMember] attributes with sequential numbering starting from 1.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ProtoContractRule, ProtoMemberRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Analyze interface methods to find types that need ProtoBuf attributes
        context.RegisterSyntaxNodeAction(AnalyzeInterfaceMethod, SyntaxKind.MethodDeclaration);
        
        // Analyze types that have [ProtoContract] to ensure all properties have [ProtoMember]
        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration, 
            SyntaxKind.ClassDeclaration, 
            SyntaxKind.RecordDeclaration, 
            SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeInterfaceMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration);

        if (methodSymbol == null)
            return;

        // Only analyze methods in interfaces that inherit from IQuarkActor
        var containingInterface = methodSymbol.ContainingType;
        if (containingInterface?.TypeKind != TypeKind.Interface)
            return;

        if (!InheritsFromQuarkActor(containingInterface))
            return;

        // Check return type
        if (methodSymbol.ReturnType is INamedTypeSymbol returnType)
        {
            // Unwrap Task<T> and ValueTask<T>
            var actualReturnType = UnwrapTaskType(returnType);
            if (actualReturnType != null && RequiresProtoContract(actualReturnType))
            {
                CheckTypeHasProtoContract(context, actualReturnType, methodDeclaration.ReturnType.GetLocation());
            }
        }

        // Check parameters
        foreach (var parameter in methodSymbol.Parameters)
        {
            // Skip CancellationToken
            if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken")
                continue;

            if (RequiresProtoContract(parameter.Type))
            {
                var parameterSyntax = methodDeclaration.ParameterList.Parameters
                    .FirstOrDefault(p => p.Identifier.Text == parameter.Name);
                
                if (parameterSyntax != null)
                {
                    CheckTypeHasProtoContract(context, parameter.Type, parameterSyntax.Type?.GetLocation() ?? parameterSyntax.GetLocation());
                }
            }
        }
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration);

        if (typeSymbol == null)
            return;

        // Check if type has [ProtoContract] attribute
        var hasProtoContract = typeSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "ProtoBuf.ProtoContractAttribute");

        if (!hasProtoContract)
            return;

        // If it has ProtoContract, check that all properties have ProtoMember
        var properties = typeSymbol.GetMembers().OfType<IPropertySymbol>();
        
        foreach (var property in properties)
        {
            // Skip compiler-generated properties
            if (property.IsImplicitlyDeclared)
                continue;

            var hasProtoMember = property.GetAttributes()
                .Any(attr => attr.AttributeClass?.ToDisplayString() == "ProtoBuf.ProtoMemberAttribute");

            if (!hasProtoMember)
            {
                // Find the property syntax node
                var propertySyntax = typeDeclaration.Members
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.Text == property.Name);

                // Also check for record parameters (primary constructor parameters)
                var location = propertySyntax?.GetLocation();
                
                if (location == null && typeDeclaration is RecordDeclarationSyntax recordDecl && recordDecl.ParameterList != null)
                {
                    var recordParam = recordDecl.ParameterList.Parameters
                        .FirstOrDefault(p => p.Identifier.Text == property.Name);
                    location = recordParam?.GetLocation();
                }

                if (location != null)
                {
                    var diagnostic = Diagnostic.Create(
                        ProtoMemberRule,
                        location,
                        property.Name,
                        typeSymbol.Name);
                    
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool InheritsFromQuarkActor(INamedTypeSymbol interfaceType)
    {
        // Check if this interface or any of its base interfaces inherits from IQuarkActor
        if (interfaceType.ToDisplayString() == "Quark.Abstractions.IQuarkActor")
            return true;

        foreach (var baseInterface in interfaceType.AllInterfaces)
        {
            if (baseInterface.ToDisplayString() == "Quark.Abstractions.IQuarkActor")
                return true;
        }

        return false;
    }

    private static ITypeSymbol? UnwrapTaskType(INamedTypeSymbol type)
    {
        var typeName = type.ToDisplayString();
        
        if (typeName.StartsWith("System.Threading.Tasks.Task<"))
        {
            return type.TypeArguments.Length > 0 ? type.TypeArguments[0] : null;
        }
        
        if (typeName.StartsWith("System.Threading.Tasks.ValueTask<"))
        {
            return type.TypeArguments.Length > 0 ? type.TypeArguments[0] : null;
        }

        return null;
    }

    private static bool RequiresProtoContract(ITypeSymbol type)
    {
        // Primitive types don't need ProtoContract
        if (type.SpecialType != SpecialType.None)
            return false;

        var typeName = type.ToDisplayString();

        // Common framework types that don't need ProtoContract
        if (typeName == "string" ||
            typeName == "System.String" ||
            typeName == "System.DateTime" ||
            typeName == "System.DateTimeOffset" ||
            typeName == "System.TimeSpan" ||
            typeName == "System.Guid" ||
            typeName == "System.Decimal")
            return false;

        // Arrays - check element type
        if (type is IArrayTypeSymbol arrayType)
            return RequiresProtoContract(arrayType.ElementType);

        // Generic types - check if it's a collection
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var genericName = namedType.ConstructedFrom.ToDisplayString();
            
            // Collection types - check type arguments
            if (genericName.StartsWith("System.Collections.Generic.List<") ||
                genericName.StartsWith("System.Collections.Generic.Dictionary<") ||
                genericName.StartsWith("System.Collections.Generic.HashSet<") ||
                genericName.StartsWith("System.Collections.Generic.IEnumerable<") ||
                genericName.StartsWith("System.Collections.Generic.IList<") ||
                genericName.StartsWith("System.Collections.Generic.ICollection<") ||
                genericName.StartsWith("System.Collections.Generic.IReadOnlyList<") ||
                genericName.StartsWith("System.Collections.Generic.IReadOnlyCollection<"))
            {
                // Check if any type argument requires ProtoContract
                return namedType.TypeArguments.Any(RequiresProtoContract);
            }

            // Nullable<T>
            if (genericName == "System.Nullable<>")
                return RequiresProtoContract(namedType.TypeArguments[0]);
        }

        // Enums don't need ProtoContract (ProtoBuf handles them)
        if (type.TypeKind == TypeKind.Enum)
            return false;

        // Classes, structs, and records need ProtoContract
        if (type.TypeKind == TypeKind.Class || 
            type.TypeKind == TypeKind.Struct ||
            type is INamedTypeSymbol { IsRecord: true })
        {
            // Skip if it's in the System namespace (framework types)
            if (type.ContainingNamespace?.ToDisplayString().StartsWith("System") == true)
                return false;

            return true;
        }

        return false;
    }

    private static void CheckTypeHasProtoContract(
        SyntaxNodeAnalysisContext context, 
        ITypeSymbol type,
        Location location)
    {
        if (type.TypeKind != TypeKind.Class && 
            type.TypeKind != TypeKind.Struct && 
            !(type is INamedTypeSymbol { IsRecord: true }))
            return;

        var hasProtoContract = type.GetAttributes()
            .Any(attr => attr.AttributeClass?.ToDisplayString() == "ProtoBuf.ProtoContractAttribute");

        if (!hasProtoContract)
        {
            var diagnostic = Diagnostic.Create(
                ProtoContractRule,
                location,
                type.Name);
            
            context.ReportDiagnostic(diagnostic);
        }
    }
}
