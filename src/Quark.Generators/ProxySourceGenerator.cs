using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Quark.Generators;

/// <summary>
/// Source generator for creating AOT-compatible actor proxies with System.Text.Json serialization.
/// Generates type-safe client proxies for interfaces inheriting from IQuarkActor or registered via QuarkActorContext.
/// </summary>
[Generator]
public class ProxySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all interfaces inheriting from IQuarkActor
        var actorInterfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (s, _) => IsSyntaxTargetForGeneration(s),
                static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Find all classes with QuarkActorContext attribute
        var contextClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (s, _) => IsContextSyntaxTarget(s),
                static (ctx, _) => GetContextSemanticTarget(ctx))
            .Where(static m => m is not null);

        // Collect both sources
        var compilation = context.CompilationProvider
            .Combine(actorInterfaces.Collect())
            .Combine(contextClasses.Collect());

        // Generate code for all actor interfaces at once
        context.RegisterSourceOutput(compilation,
            static (spc, source) => Execute(source.Left.Left, source.Left.Right!, source.Right!, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        // Look for interface declarations
        return node is InterfaceDeclarationSyntax interfaceDeclaration
               && interfaceDeclaration.BaseList is not null
               && interfaceDeclaration.BaseList.Types.Count > 0;
    }

    private static bool IsContextSyntaxTarget(SyntaxNode node)
    {
        // Look for class declarations with attributes
        return node is ClassDeclarationSyntax classDeclaration
               && classDeclaration.AttributeLists.Count > 0;
    }

    private static InterfaceDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
        var interfaceSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;

        if (interfaceSymbol == null)
            return null;

        // Check if interface inherits from IQuarkActor
        if (InheritsFromIQuarkActor(interfaceSymbol))
        {
            return interfaceDeclaration;
        }

        return null;
    }

    private static ClassDeclarationSyntax? GetContextSemanticTarget(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;

        if (classSymbol == null)
            return null;

        // Check if class has QuarkActorContext attribute
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "Quark.Abstractions.QuarkActorContextAttribute")
            {
                return classDeclaration;
            }
        }

        return null;
    }

    private static bool InheritsFromIQuarkActor(INamedTypeSymbol interfaceSymbol)
    {
        // Check if this interface directly inherits from IQuarkActor
        foreach (var iface in interfaceSymbol.Interfaces)
        {
            var displayString = iface.ToDisplayString();
            if (displayString == "Quark.Abstractions.IQuarkActor")
                return true;
        }

        // Check all interfaces (including transitive)
        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            if (baseInterface.ToDisplayString() == "Quark.Abstractions.IQuarkActor")
                return true;
        }

        return false;
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<InterfaceDeclarationSyntax?> actorInterfaces,
        ImmutableArray<ClassDeclarationSyntax?> contextClasses,
        SourceProductionContext context)
    {
        var allInterfaceSymbols = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var proxyRegistrations = new StringBuilder();

        // Process interfaces inheriting from IQuarkActor
        foreach (var interfaceDeclaration in actorInterfaces)
        {
            if (interfaceDeclaration is null)
                continue;

            var semanticModel = compilation.GetSemanticModel(interfaceDeclaration.SyntaxTree);
            var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;

            if (interfaceSymbol is null)
                continue;

            allInterfaceSymbols.Add(interfaceSymbol);
        }

        // Process context classes and extract actor interfaces from QuarkActor attributes
        foreach (var contextClass in contextClasses)
        {
            if (contextClass is null)
                continue;

            var semanticModel = compilation.GetSemanticModel(contextClass.SyntaxTree);
            var classSymbol = semanticModel.GetDeclaredSymbol(contextClass) as INamedTypeSymbol;

            if (classSymbol is null)
                continue;

            // Find all QuarkActor attributes on the context class
            foreach (var attribute in classSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != "Quark.Abstractions.QuarkActorAttribute")
                    continue;

                // Get the actor type from the attribute constructor argument
                if (attribute.ConstructorArguments.Length > 0)
                {
                    var actorTypeArg = attribute.ConstructorArguments[0];
                    if (actorTypeArg.Value is INamedTypeSymbol actorTypeSymbol)
                    {
                        // Validate that it's an interface
                        if (actorTypeSymbol.TypeKind == TypeKind.Interface)
                        {
                            allInterfaceSymbols.Add(actorTypeSymbol);
                        }
                    }
                }
            }
        }

        // If no interfaces found, exit early
        if (allInterfaceSymbols.Count == 0)
            return;

        // Generate proxies for all collected interfaces
        foreach (var interfaceSymbol in allInterfaceSymbols)
        {
            var interfaceName = interfaceSymbol.Name;
            var fullInterfaceName = interfaceSymbol.ToDisplayString();
            var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
            
            // Remove 'I' prefix from interface name for proxy class name
            var proxyClassName = interfaceName.StartsWith("I") && interfaceName.Length > 1
                ? interfaceName.Substring(1) + "Proxy"
                : interfaceName + "Proxy";

            // Check if the interface inherits from IQuarkActor
            bool implementsIQuarkActor = InheritsFromIQuarkActor(interfaceSymbol);

            // Get all methods in the interface (excluding IActor base methods)
            var methods = GetActorMethods(interfaceSymbol);

            if (methods.Count == 0)
                continue;

            // No longer generate message contracts - using binary converters instead

            // Generate client-side proxy class
            GenerateProxyClass(context, interfaceName, proxyClassName, fullInterfaceName, namespaceName, methods, implementsIQuarkActor);

            // Add to proxy registration
            proxyRegistrations.AppendLine(
                $"            ActorProxyFactory.RegisterProxyFactory<{fullInterfaceName}>((client, actorId) => new {namespaceName}.Generated.{proxyClassName}(client, actorId));");
        }

        // Generate the registration class
        var assemblyName = compilation.AssemblyName?.Replace(".", "") ?? "Assembly";
        GenerateRegistrationClass(context, assemblyName, proxyRegistrations.ToString());
    }

    private static List<IMethodSymbol> GetActorMethods(INamedTypeSymbol interfaceSymbol)
    {
        var methods = new List<IMethodSymbol>();

        // Get methods directly defined in this interface
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && !method.IsStatic)
            {
                // Skip IActor base methods (OnActivateAsync, OnDeactivateAsync)
                var methodName = method.Name;
                if (methodName == "OnActivateAsync" || methodName == "OnDeactivateAsync")
                    continue;

                // Skip property accessors
                if (method.MethodKind == MethodKind.PropertyGet || method.MethodKind == MethodKind.PropertySet)
                    continue;

                // Include all methods (async and sync) - we'll handle them differently in proxy generation
                methods.Add(method);
            }
        }

        return methods;
    }

    private static bool IsAsyncReturnType(string returnTypeString)
    {
        return returnTypeString == "System.Threading.Tasks.Task" ||
               returnTypeString == "System.Threading.Tasks.ValueTask" ||
               returnTypeString.StartsWith("System.Threading.Tasks.Task<") ||
               returnTypeString.StartsWith("System.Threading.Tasks.ValueTask<");
    }

    // GenerateJsonSerializerContext method removed - using binary converters instead

    private static void GenerateProxyClass(
        SourceProductionContext context,
        string interfaceName,
        string proxyClassName,
        string fullInterfaceName,
        string namespaceName,
        List<IMethodSymbol> methods,
        bool implementsIQuarkActor)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine("using System;");
        source.AppendLine("using System.IO;");
        source.AppendLine("using System.Linq;");
        source.AppendLine("using System.Threading;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine("using Quark.Abstractions;");
        source.AppendLine("using Quark.Abstractions.Converters;");
        source.AppendLine("using Quark.Client;");
        source.AppendLine("using Quark.Networking.Abstractions;");
        source.AppendLine();
        source.AppendLine($"namespace {namespaceName}.Generated");
        source.AppendLine("{");
        source.AppendLine($"    /// <summary>");
        source.AppendLine($"    /// Type-safe client proxy for {fullInterfaceName}.");
        source.AppendLine($"    /// </summary>");
        
        // If the interface doesn't inherit from IQuarkActor, explicitly implement it in the proxy
        if (implementsIQuarkActor)
        {
            source.AppendLine($"    public sealed class {proxyClassName} : {fullInterfaceName}");
        }
        else
        {
            source.AppendLine($"    public sealed class {proxyClassName} : {fullInterfaceName}, IQuarkActor");
        }
        
        source.AppendLine($"    {{");
        source.AppendLine($"        private readonly IClusterClient _client;");
        source.AppendLine($"        private readonly string _actorId;");
        source.AppendLine();
        source.AppendLine($"        public {proxyClassName}(IClusterClient client, string actorId)");
        source.AppendLine($"        {{");
        source.AppendLine($"            _client = client ?? throw new ArgumentNullException(nameof(client));");
        source.AppendLine($"            _actorId = actorId ?? throw new ArgumentNullException(nameof(actorId));");
        source.AppendLine($"        }}");
        source.AppendLine();
        source.AppendLine($"        public string ActorId => _actorId;");
        source.AppendLine();

        // Generate IActor base methods (OnActivateAsync, OnDeactivateAsync)
        source.AppendLine($"        public Task OnActivateAsync(CancellationToken cancellationToken = default)");
        source.AppendLine($"        {{");
        source.AppendLine($"            // Client proxies don't implement activation lifecycle");
        source.AppendLine($"            return Task.CompletedTask;");
        source.AppendLine($"        }}");
        source.AppendLine();
        source.AppendLine($"        public Task OnDeactivateAsync(CancellationToken cancellationToken = default)");
        source.AppendLine($"        {{");
        source.AppendLine($"            // Client proxies don't implement deactivation lifecycle");
        source.AppendLine($"            return Task.CompletedTask;");
        source.AppendLine($"        }}");
        source.AppendLine();

        // Generate proxy methods
        foreach (var method in methods)
        {
            GenerateProxyMethod(source, interfaceName, fullInterfaceName, method);
        }

        source.AppendLine($"    }}");
        source.AppendLine("}");

        context.AddSource($"{proxyClassName}.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void GenerateProxyMethod(StringBuilder source, string interfaceName, string fullInterfaceName, IMethodSymbol method)
    {
        var methodName = method.Name;
        var returnType = method.ReturnType.ToDisplayString();
        var parameters = string.Join(", ", method.Parameters.Select(p =>
            $"{p.Type.ToDisplayString()} {p.Name}"));

        source.AppendLine($"        public {returnType} {methodName}({parameters})");
        source.AppendLine($"        {{");

        // Check if this is an async method
        if (!IsAsyncReturnType(returnType))
        {
            // For non-async methods (like Subscribe/Unsubscribe), throw NotSupportedException
            source.AppendLine($"            throw new NotSupportedException(");
            source.AppendLine($"                \"Method '{methodName}' cannot be called remotely through a proxy. \" +");
            source.AppendLine($"                \"Only async methods (returning Task or Task<T>) are supported for remote actor calls.\");");
            source.AppendLine($"        }}");
            source.AppendLine();
            return;
        }

        // Get serializable parameters (excluding CancellationToken)
        var serializableParams = method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .ToList();

        // Get BinaryConverter attributes for this method
        var converterAttributes = method.GetAttributes()
            .Where(attr => attr.AttributeClass?.Name == "BinaryConverterAttribute")
            .OrderBy(attr => {
                // Get Order property value
                var orderArg = attr.NamedArguments.FirstOrDefault(arg => arg.Key == "Order");
                return orderArg.Value.Value as int? ?? 0;
            })
            .ToList();

        // Serialize request parameters using binary converters with length-prefixing
        if (serializableParams.Count > 0)
        {
            source.AppendLine($"            byte[] payload;");
            source.AppendLine($"            using (var ms = new System.IO.MemoryStream())");
            source.AppendLine($"            {{");
            source.AppendLine($"                using (var writer = new System.IO.BinaryWriter(ms))");
            source.AppendLine($"                {{");
            
            // Write each parameter using its converter with length-prefixing
            foreach (var param in serializableParams)
            {
                var paramName = param.Name;
                var paramType = param.Type.ToDisplayString();
                
                // Find converter for this parameter
                var converterAttr = converterAttributes.FirstOrDefault(attr => 
                {
                    var paramNameArg = attr.NamedArguments.FirstOrDefault(arg => arg.Key == "ParameterName");
                    var specifiedParamName = paramNameArg.Value.Value as string;
                    return specifiedParamName == paramName;
                });

                if (converterAttr != null && converterAttr.ConstructorArguments.Length > 0)
                {
                    var converterType = converterAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
                    var converterTypeName = converterType?.ToDisplayString();
                    
                    source.AppendLine($"                    // Serialize {paramName} with length-prefixing using {converterTypeName}");
                    source.AppendLine($"                    BinaryConverterHelper.WriteWithLength(writer, new {converterTypeName}(), {paramName});");
                }
                else
                {
                    // Use default converter based on type
                    var defaultConverter = GetDefaultConverterForType(paramType);
                    source.AppendLine($"                    // Serialize {paramName} with length-prefixing using default converter");
                    source.AppendLine($"                    BinaryConverterHelper.WriteWithLength(writer, new {defaultConverter}(), {paramName});");
                }
            }
            
            source.AppendLine($"                }}");
            source.AppendLine($"                payload = ms.ToArray();");
            source.AppendLine($"            }}");
        }
        else
        {
            source.AppendLine($"            byte[] payload = Array.Empty<byte>();");
        }

        source.AppendLine();

        // Use fully qualified interface name as actor type for uniqueness across namespaces
        var actorTypeName = fullInterfaceName;

        // Create envelope
        source.AppendLine($"            var envelope = new QuarkEnvelope(");
        source.AppendLine($"                messageId: Guid.NewGuid().ToString(),");
        source.AppendLine($"                actorId: _actorId,");
        source.AppendLine($"                actorType: \"{actorTypeName}\",");
        source.AppendLine($"                methodName: \"{methodName}\",");
        source.AppendLine($"                payload: payload);");
        source.AppendLine();

        // Send envelope and handle response
        var returnTypeSymbol = method.ReturnType as INamedTypeSymbol;
        string resultType = string.Empty;
        var hasResult = returnTypeSymbol != null && IsTaskWithResult(returnTypeSymbol, out resultType);

        if (hasResult)
        {
            source.AppendLine($"            return SendWithResultAsync(envelope);");
            source.AppendLine();
            source.AppendLine($"            async Task<{resultType}> SendWithResultAsync(QuarkEnvelope env)");
            source.AppendLine($"            {{");
            source.AppendLine($"                var response = await _client.SendAsync(env, default);");
            source.AppendLine();
            source.AppendLine($"                if (response.IsError)");
            source.AppendLine($"                {{");
            source.AppendLine($"                    throw new InvalidOperationException($\"Actor call failed: {{response.ErrorMessage}}\");");
            source.AppendLine($"                }}");
            source.AppendLine();
            source.AppendLine($"                if (response.ResponsePayload == null || response.ResponsePayload.Length == 0)");
            source.AppendLine($"                {{");
            source.AppendLine($"                    throw new InvalidOperationException(\"Expected response payload but received none.\");");
            source.AppendLine($"                }}");
            source.AppendLine();
            
            // Deserialize response using binary converter
            var returnConverterAttr = converterAttributes.FirstOrDefault(attr =>
            {
                var paramNameArg = attr.NamedArguments.FirstOrDefault(arg => arg.Key == "ParameterName");
                return paramNameArg.Value.Value == null; // null ParameterName means return value
            });

            source.AppendLine($"                using (var ms = new System.IO.MemoryStream(response.ResponsePayload))");
            source.AppendLine($"                {{");
            source.AppendLine($"                    using (var reader = new System.IO.BinaryReader(ms))");
            source.AppendLine($"                    {{");
            
            if (returnConverterAttr != null && returnConverterAttr.ConstructorArguments.Length > 0)
            {
                var converterType = returnConverterAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
                var converterTypeName = converterType?.ToDisplayString();
                source.AppendLine($"                        return BinaryConverterHelper.ReadWithLength(reader, new {converterTypeName}());");
            }
            else
            {
                var defaultConverter = GetDefaultConverterForType(resultType);
                source.AppendLine($"                        return BinaryConverterHelper.ReadWithLength(reader, new {defaultConverter}());");
            }
            
            source.AppendLine($"                    }}");
            source.AppendLine($"                }}");
            source.AppendLine($"            }}");
        }
        else
        {
            // Return Task or ValueTask without result
            source.AppendLine($"            return SendWithoutResultAsync(envelope);");
            source.AppendLine();
            source.AppendLine($"            async Task SendWithoutResultAsync(QuarkEnvelope env)");
            source.AppendLine($"            {{");
            source.AppendLine($"                var response = await _client.SendAsync(env, default);");
            source.AppendLine();
            source.AppendLine($"                if (response.IsError)");
            source.AppendLine($"                {{");
            source.AppendLine($"                    throw new InvalidOperationException($\"Actor call failed: {{response.ErrorMessage}}\");");
            source.AppendLine($"                }}");
            source.AppendLine($"            }}");
        }

        source.AppendLine($"        }}");
        source.AppendLine();
    }

    private static string GetDefaultConverterForType(string typeName)
    {
        return typeName switch
        {
            "string" => "Quark.Abstractions.Converters.StringConverter",
            "int" or "System.Int32" => "Quark.Abstractions.Converters.Int32Converter",
            "long" or "System.Int64" => "Quark.Abstractions.Converters.Int64Converter",
            "bool" or "System.Boolean" => "Quark.Abstractions.Converters.BooleanConverter",
            "double" or "System.Double" => "Quark.Abstractions.Converters.DoubleConverter",
            "System.Guid" => "Quark.Abstractions.Converters.GuidConverter",
            "System.DateTime" => "Quark.Abstractions.Converters.DateTimeConverter",
            "byte[]" => "Quark.Abstractions.Converters.ByteArrayConverter",
            _ => throw new InvalidOperationException($"No default converter for type {typeName}. Please specify a [BinaryConverter] attribute.")
        };
    }

    private static void GenerateRegistrationClass(SourceProductionContext context, string assemblyName, string registrations)
    {
        var source = $$"""
                       // <auto-generated/>
                       #nullable enable
                       using Quark.Abstractions;
                       using Quark.Client;
                       
                       namespace Quark.Generated
                       {
                           /// <summary>
                           /// Auto-generated registration class for actor proxies.
                           /// Call RegisterAll() to register all actor proxy factories for this assembly.
                           /// </summary>
                           public static class {{assemblyName}}ActorProxyFactoryRegistration
                           {
                               private static bool _registered = false;
                               
                               /// <summary>
                               /// Registers all actor proxy factories for this assembly.
                               /// This method is idempotent and can be called multiple times safely.
                               /// </summary>
                               public static void RegisterAll()
                               {
                                   if (_registered)
                                       return;
                                   
                                   _registered = true;
                                   
                       {{registrations}}
                               }
                           }
                       }
                       """;

        context.AddSource($"{assemblyName}ActorProxyFactoryRegistration.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static bool IsTaskWithResult(INamedTypeSymbol taskType, out string resultType)
    {
        resultType = string.Empty;

        // Check for Task<T> or ValueTask<T>
        if (taskType.IsGenericType)
        {
            var typeDefinition = taskType.ConstructedFrom.ToDisplayString();
            if (typeDefinition == "System.Threading.Tasks.Task<TResult>" ||
                typeDefinition == "System.Threading.Tasks.ValueTask<TResult>")
            {
                resultType = taskType.TypeArguments[0].ToDisplayString();
                return true;
            }
        }

        return false;
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return char.ToUpper(name[0]) + name.Substring(1);
    }

    private static string GetNamespace(InterfaceDeclarationSyntax interfaceDeclaration)
    {
        var namespaceDeclaration = interfaceDeclaration.Ancestors()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        if (namespaceDeclaration != null)
            return namespaceDeclaration.Name.ToString();

        var fileScopedNamespace = interfaceDeclaration.Ancestors()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();

        return fileScopedNamespace?.Name.ToString() ?? "Global";
    }
}
