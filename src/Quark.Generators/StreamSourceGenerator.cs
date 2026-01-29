using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quark.Generators;

/// <summary>
/// Source generator for Quark streams that creates stream-to-actor mappings.
/// Detects classes with [QuarkStream] attributes and generates registration code.
/// </summary>
[Generator]
public class StreamSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all classes with the QuarkStream attribute
        var streamClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (s, _) => IsSyntaxTargetForGeneration(s),
                static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Collect all stream classes
        var compilation = context.CompilationProvider.Combine(streamClasses.Collect());

        // Generate code for all stream actors at once
        context.RegisterSourceOutput(compilation,
            static (spc, source) => Execute(source.Left, source.Right!, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration
               && classDeclaration.AttributeLists.Count > 0;
    }

    private static StreamActorInfo? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var streamNamespaces = new List<string>();

        foreach (var attributeList in classDeclaration.AttributeLists)
        foreach (var attribute in attributeList.Attributes)
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute);
            if (symbolInfo.Symbol is not IMethodSymbol attributeSymbol)
                continue;

            var attributeType = attributeSymbol.ContainingType;
            var fullName = attributeType.ToDisplayString();

            if (fullName == "Quark.Abstractions.Streaming.QuarkStreamAttribute")
            {
                // Extract the namespace parameter from the attribute
                if (attribute.ArgumentList?.Arguments.Count > 0)
                {
                    var arg = attribute.ArgumentList.Arguments[0];
                    var value = context.SemanticModel.GetConstantValue(arg.Expression);
                    if (value.HasValue && value.Value is string ns)
                    {
                        streamNamespaces.Add(ns);
                    }
                }
            }
        }

        if (streamNamespaces.Count == 0)
            return null;

        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
        if (classSymbol == null)
            return null;

        // Find the message type from IStreamConsumer<T>
        var streamConsumerInterface = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.Name == "IStreamConsumer" && i.IsGenericType);

        string? messageTypeName = null;
        if (streamConsumerInterface != null && streamConsumerInterface.TypeArguments.Length > 0)
        {
            messageTypeName = streamConsumerInterface.TypeArguments[0].ToDisplayString();
        }

        return new StreamActorInfo(
            classSymbol.Name,
            classSymbol.ToDisplayString(),
            streamNamespaces.ToArray(),
            messageTypeName);
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<StreamActorInfo?> streamActors,
        SourceProductionContext context)
    {
        if (streamActors.IsEmpty)
            return;

        var registrations = new StringBuilder();
        var dispatcherRegistrations = new StringBuilder();

        foreach (var info in streamActors)
        {
            if (info is null)
                continue;

            // Generate dispatcher for this actor type
            if (info.MessageTypeName != null)
            {
                GenerateDispatcher(context, info);
                
                // Register the dispatcher in the module initializer
                dispatcherRegistrations.AppendLine(
                    $"        StreamConsumerDispatcherRegistry.RegisterDispatcher(typeof({info.FullClassName}), new {info.ClassName}StreamDispatcher());");
            }

            foreach (var ns in info.StreamNamespaces)
            {
                if (info.MessageTypeName != null)
                {
                    registrations.AppendLine(
                        $"        StreamRegistry.RegisterImplicitSubscription(\"{ns}\", typeof({info.FullClassName}), typeof({info.MessageTypeName}));");
                }
            }
        }

        if (registrations.Length == 0)
            return;

        // Generate the module initializer for stream registrations
        var moduleInitSource = $$"""
                                 // <auto-generated/>
                                 #nullable enable
                                 using System.Runtime.CompilerServices;
                                 using Quark.Core.Streaming;
                                 using Quark.Abstractions.Streaming;

                                 namespace Quark.Generated;

                                 /// <summary>
                                 /// Auto-generated stream registration module.
                                 /// </summary>
                                 public static class StreamRegistrationModule
                                 {
                                     [ModuleInitializer]
                                     public static void Initialize()
                                     {
                                 {{dispatcherRegistrations}}
                                 {{registrations}}
                                     }
                                 }
                                 """;

        context.AddSource("StreamRegistrations.g.cs", moduleInitSource);
    }

    private static void GenerateDispatcher(
        SourceProductionContext context,
        StreamActorInfo info)
    {
        var dispatcherSource = $$"""
                                 // <auto-generated/>
                                 #nullable enable
                                 using System.Threading;
                                 using System.Threading.Tasks;
                                 using Quark.Abstractions;
                                 using Quark.Abstractions.Streaming;

                                 namespace Quark.Generated;

                                 /// <summary>
                                 /// AOT-safe dispatcher for {{info.FullClassName}}.
                                 /// Generated by StreamSourceGenerator to eliminate reflection.
                                 /// </summary>
                                 internal sealed class {{info.ClassName}}StreamDispatcher : IStreamConsumerDispatcher
                                 {
                                     public Type ActorType => typeof({{info.FullClassName}});
                                     public Type MessageType => typeof({{info.MessageTypeName}});

                                     public async Task ActivateAndNotifyAsync(
                                         IActorFactory actorFactory,
                                         string actorId,
                                         object message,
                                         StreamId streamId,
                                         CancellationToken cancellationToken)
                                     {
                                         // Type-safe actor creation - no reflection
                                         var actor = actorFactory.GetOrCreateActor<{{info.FullClassName}}>(actorId);
                                         
                                         // Type-safe message casting and method invocation - no reflection
                                         var typedMessage = ({{info.MessageTypeName}})message;
                                         await actor.OnStreamMessageAsync(typedMessage, streamId, cancellationToken);
                                     }
                                 }
                                 """;

        context.AddSource($"{info.ClassName}StreamDispatcher.g.cs", dispatcherSource);
    }

    private class StreamActorInfo
    {
        public StreamActorInfo(
            string className,
            string fullClassName,
            string[] streamNamespaces,
            string? messageTypeName)
        {
            ClassName = className;
            FullClassName = fullClassName;
            StreamNamespaces = streamNamespaces;
            MessageTypeName = messageTypeName;
        }

        public string ClassName { get; }
        public string FullClassName { get; }
        public string[] StreamNamespaces { get; }
        public string? MessageTypeName { get; }
    }
}
