using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Tests.Unit.Integration;

public sealed class CounterGrainMethodInvoker : IGrainMethodInvoker
{
    public const uint IncrementMethodId = 0;
    public const uint GetValueMethodId = 1;
    public const uint ResetMethodId = 2;

    public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
    {
        var counter = (CounterGrain)grain;
        return methodId switch
        {
            IncrementMethodId => await counter.IncrementAsync(),
            GetValueMethodId => await counter.GetValueAsync(),
            ResetMethodId => await counter.ResetAsync().ContinueWith(_ => (object?)null),
            _ => throw new NotSupportedException($"Unknown method id {methodId}")
        };
    }
}
