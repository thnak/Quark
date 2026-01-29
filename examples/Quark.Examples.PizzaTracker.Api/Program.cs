using FastEndpoints;
using Quark.Core.Actors;
using Quark.Abstractions;
using Quark.Examples.PizzaTracker.Api;
using System.Text.Json.Serialization;
using Quark.Examples.PizzaTracker.Api.Endpoints;

var builder = WebApplication.CreateSlimBuilder(args);

// Configure JSON serialization for AOT
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Add(PizzaTrackerJsonContext.Default);
});

// Register Quark Actor Factory as a singleton
builder.Services.AddSingleton<IActorFactory, ActorFactory>();

// Add FastEndpoints with explicit endpoint types for AOT
builder.Services.AddFastEndpoints(o =>
{
    o.Assemblies = new[] { typeof(Program).Assembly };
});

var app = builder.Build();

// Configure FastEndpoints
app.UseFastEndpoints(c =>
{
    c.Serializer.Options.TypeInfoResolverChain.Add(PizzaTrackerJsonContext.Default);
});

app.Run();
