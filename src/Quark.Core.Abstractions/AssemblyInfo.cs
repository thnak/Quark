using System.Runtime.CompilerServices;

// Grant the runtime project access to internals (e.g. Grain.SetContext).
[assembly: InternalsVisibleTo("Quark.Runtime")]
// Grant the test project access to internals.
[assembly: InternalsVisibleTo("Quark.Tests.Unit")]
