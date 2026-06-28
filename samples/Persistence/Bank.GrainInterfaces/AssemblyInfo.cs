using System.Runtime.CompilerServices;

// The code generator emits internal proxy/invokable types into this assembly.
// Make them visible to the behaviors and host projects that consume them.
[assembly: InternalsVisibleTo("Bank.Grains")]
[assembly: InternalsVisibleTo("Bank.Server")]
[assembly: InternalsVisibleTo("Bank.Client")]
