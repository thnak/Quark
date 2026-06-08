namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Marker interface for a grain behavior component.
///     Implement on a POCO class — no base class required.
///     One instance is constructed per grain method call inside a short-lived DI scope.
///     Constructor parameters are resolved from that scope.
/// </summary>
public interface IGrainBehavior { }
