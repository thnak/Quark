using FastEndpoints;
using FastEndpoints.Swagger;
using Quark.Core.Actors;
using Quark.Abstractions;
using Quark.Examples.PizzaTracker.Api;

var builder = WebApplication.CreateSlimBuilder(args);

// Configure JSON serialization for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Add(PizzaTrackerJsonContext.Default);
});

// Register Quark Actor Factory as a singleton
builder.Services.AddSingleton<IActorFactory, ActorFactory>();

// Add FastEndpoints with explicit endpoint types for AOT
builder.Services
    .AddFastEndpoints(o => o.SourceGeneratorDiscoveredTypes = DiscoveredTypes.All)
    .SwaggerDocument();

var app = builder.Build();

// Configure FastEndpoints
app.UseFastEndpoints(c =>
    {
        c.Serializer.Options.TypeInfoResolverChain.Add(PizzaTrackerJsonContext.Default);
    })
    .UseSwaggerGen();

app.Run();