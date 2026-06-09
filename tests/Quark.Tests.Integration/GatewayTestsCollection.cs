using Xunit;

namespace Quark.Tests.Integration;

/// <summary>Serialises all GatewayIntegrationTests to prevent port-reuse races.</summary>
[CollectionDefinition("GatewayTests", DisableParallelization = true)]
public sealed class GatewayTestsCollection { }