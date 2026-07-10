using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Runtime.Clustering;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class CascadingTerminationTests
{
    private static GrainActivation MakeActivation(GrainId id, IServiceProvider? sp = null)
        => new(id, id.Type, isReentrant: false, sp ?? EmptySp.Instance,
            NullLogger<GrainActivation>.Instance, SimpleActivationScheduler.Instance);

    private static GrainId Id(string type, string key) => new(new GrainType(type), key);

    private static IServiceProvider BuildSp(IActivationTerminator terminator)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(terminator);
        return sc.BuildServiceProvider();
    }

    private static async Task WaitForInactive(GrainActivation activation, int timeoutMs = 5_000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (activation.ActivationStatus != GrainActivationStatus.Inactive)
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException(
                    $"Grain {activation.GrainId} did not deactivate within {timeoutMs} ms.");
            await Task.Delay(20);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 1 — CascadesToChildren boolean for each static reason
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeactivationReasons_CascadesToChildren_MatchesSpec()
    {
        Assert.False(DeactivationReason.IdleTimeout.CascadesToChildren);
        Assert.False(DeactivationReason.ShuttingDown.CascadesToChildren);
        Assert.True(DeactivationReason.ApplicationRequested.CascadesToChildren);
        Assert.True(DeactivationReason.Force.CascadesToChildren);
        Assert.True(DeactivationReason.ParentTerminated.CascadesToChildren);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 2 — Local cascade: ApplicationRequested → child is terminated
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cascade_ApplicationRequested_DeactivatesChild()
    {
        var spy = new SpyTerminator();
        var sp = BuildSp(spy);

        var parentId = Id("Parent", "p1");
        var childId  = Id("Child",  "c1");

        await using var parent = MakeActivation(parentId, sp);
        parent.MarkActive();
        parent.GetOrCreateChildRegistry().Attach(childId, ChildTerminationMode.Cascade);

        parent.Deactivate(DeactivationReason.ApplicationRequested);
        await WaitForInactive(parent);

        Assert.Contains(childId, spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 3 — Orphan mode: child is NOT terminated
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cascade_OrphanMode_ChildNotTerminated()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        var parentId = Id("Parent", "p2");
        var childId  = Id("Child",  "c2");

        await using var parent = MakeActivation(parentId, sp);
        parent.MarkActive();
        parent.GetOrCreateChildRegistry().Attach(childId, ChildTerminationMode.Orphan);

        parent.Deactivate(DeactivationReason.ApplicationRequested);
        await WaitForInactive(parent);

        Assert.DoesNotContain(childId, spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 4 — Recursive: A → B → C cascade through the spy
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cascade_Recursive_ABCChain()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        var aId = Id("G", "A");
        var bId = Id("G", "B");
        var cId = Id("G", "C");

        await using var b = MakeActivation(bId, sp);
        b.MarkActive();
        b.GetOrCreateChildRegistry().Attach(cId, ChildTerminationMode.Cascade);

        // Spy: when asked to Terminate(B), forward to the real B activation
        spy.WhenTerminate = (id, reason) =>
        {
            if (id == bId) b.Deactivate(reason);
        };

        await using var a = MakeActivation(aId, sp);
        a.MarkActive();
        a.GetOrCreateChildRegistry().Attach(bId, ChildTerminationMode.Cascade);

        a.Deactivate(DeactivationReason.ApplicationRequested);
        await WaitForInactive(a);
        await WaitForInactive(b);

        Assert.Contains(bId, spy.Terminated);
        Assert.Contains(cId, spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 5 — Cycle guard: A↔B each terminated exactly once
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cascade_CycleGuard_EachTerminatedExactlyOnce()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        var aId = Id("G", "X");
        var bId = Id("G", "Y");

        await using var a = MakeActivation(aId, sp);
        await using var b = MakeActivation(bId, sp);
        a.MarkActive();
        b.MarkActive();

        a.GetOrCreateChildRegistry().Attach(bId, ChildTerminationMode.Cascade);
        b.GetOrCreateChildRegistry().Attach(aId, ChildTerminationMode.Cascade);

        spy.WhenTerminate = (id, reason) =>
        {
            if (id == aId) a.Deactivate(reason);
            else if (id == bId) b.Deactivate(reason);
        };

        a.Deactivate(DeactivationReason.ApplicationRequested);
        await WaitForInactive(a);
        await WaitForInactive(b);

        Assert.Single(spy.Terminated, t => t == bId);
        Assert.Single(spy.Terminated, t => t == aId);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 6 — No cascade on IdleTimeout
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoCascade_IdleTimeout()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        var parentId = Id("P", "idle");
        var childId  = Id("C", "idle");

        await using var parent = MakeActivation(parentId, sp);
        parent.MarkActive();
        parent.GetOrCreateChildRegistry().Attach(childId, ChildTerminationMode.Cascade);

        parent.Deactivate(DeactivationReason.IdleTimeout);
        await WaitForInactive(parent);

        Assert.Empty(spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 7 — No cascade on ShuttingDown
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoCascade_ShuttingDown()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        var parentId = Id("P", "sd");
        var childId  = Id("C", "sd");

        await using var parent = MakeActivation(parentId, sp);
        parent.MarkActive();
        parent.GetOrCreateChildRegistry().Attach(childId, ChildTerminationMode.Cascade);

        parent.Deactivate(DeactivationReason.ShuttingDown);
        await WaitForInactive(parent);

        Assert.Empty(spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 8 — Amnesia: fresh activation has no child registry
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cascade_FreshActivation_NoChildrenToTerminate()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        await using var parent = MakeActivation(Id("P", "fresh"), sp);
        parent.MarkActive();
        parent.Deactivate(DeactivationReason.ApplicationRequested);
        await WaitForInactive(parent);

        Assert.Empty(spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 9 — Detach stops cascade
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cascade_Detach_StopsCascade()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        var parentId = Id("P", "det");
        var childId  = Id("C", "det");

        await using var parent = MakeActivation(parentId, sp);
        parent.MarkActive();

        var registry = parent.GetOrCreateChildRegistry();
        registry.Attach(childId, ChildTerminationMode.Cascade);
        bool detached = registry.Detach(childId);

        Assert.True(detached);

        parent.Deactivate(DeactivationReason.ApplicationRequested);
        await WaitForInactive(parent);

        Assert.Empty(spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 10 — Mailbox drain: in-flight call completes before cascade
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cascade_MailboxDrain_InFlightCallCompletesBeforeCascade()
    {
        var spy = new SpyTerminator();
        var sp  = BuildSp(spy);

        var parentId = Id("P", "drain");
        var childId  = Id("C", "drain");

        await using var parent = MakeActivation(parentId, sp);
        parent.MarkActive();
        parent.GetOrCreateChildRegistry().Attach(childId, ChildTerminationMode.Cascade);

        bool callCompleted = false;

        // Post a work item that must complete before deactivation.
        ValueTask callTask = parent.PostAsync(() =>
        {
            callCompleted = true;
            return ValueTask.CompletedTask;
        });

        parent.Deactivate(DeactivationReason.ApplicationRequested);

        await callTask;
        Assert.True(callCompleted, "In-flight call must complete before deactivation.");

        await WaitForInactive(parent);
        Assert.Contains(childId, spy.Terminated);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Scenario 11 — Remote leg: SiloCallInvoker receives TerminateRequest
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Terminate_RemoteLeg_SendsTerminateRequestEnvelope()
    {
        var targetId    = Id("RemoteGrain", "remote-1");
        var peerAddress = SiloAddress.Loopback(19999);

        bool requestSent = false;
        MessageEnvelope? captured = null;

        var invoker = new SiloCallInvoker(
            peerAddress,
            (env, ct) =>
            {
                captured    = env;
                requestSent = true;
                return Task.FromResult(new MessageEnvelope());
            },
            null!, null!);

        var directory = new StubDirectory(targetId, peerAddress);
        var router    = new StubRouter(peerAddress, invoker);

        await using var table = new GrainActivationTable(NullLogger<GrainActivationTable>.Instance);
        var terminator = new DefaultActivationTerminator(table, directory, router);

        terminator.Terminate(targetId, DeactivationReason.ApplicationRequested);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!requestSent && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(requestSent, "SiloCallInvoker must have received a TerminateRequest.");
        Assert.NotNull(captured);
        Assert.Equal(MessageType.TerminateRequest, captured!.MessageType);
        Assert.Equal(-1L, captured.CorrelationId);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers / stubs
    // ──────────────────────────────────────────────────────────────────────

    private sealed class SpyTerminator : IActivationTerminator
    {
        private readonly Lock _lock = new();
        private readonly List<GrainId> _terminated = [];

        public IReadOnlyList<GrainId> Terminated
        {
            get { lock (_lock) { return [.._terminated]; } }
        }

        public Action<GrainId, DeactivationReason>? WhenTerminate { get; set; }

        public void Terminate(GrainId target, DeactivationReason reason)
        {
            lock (_lock) { _terminated.Add(target); }
            WhenTerminate?.Invoke(target, reason);
        }
    }

    private sealed class EmptySp : IServiceProvider
    {
        public static readonly EmptySp Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StubDirectory(GrainId target, SiloAddress owner) : IGrainDirectory
    {
        public bool TryLookup(GrainId grainId, out SiloAddress siloAddress)
        {
            if (grainId == target) { siloAddress = owner; return true; }
            siloAddress = default;
            return false;
        }

        public bool TryRegister(GrainId grainId, SiloAddress siloAddress, out SiloAddress existing)
            => throw new NotSupportedException();

        public bool TryUnregister(GrainId grainId, SiloAddress siloAddress)
            => throw new NotSupportedException();
    }

    private sealed class StubRouter(SiloAddress peer, IGrainCallInvoker invoker) : ISiloRouter
    {
        public bool TryGetInvoker(SiloAddress address, [NotNullWhen(true)] out IGrainCallInvoker? result)
        {
            if (address == peer) { result = invoker; return true; }
            result = null;
            return false;
        }

        public void Register(SiloAddress address, IGrainCallInvoker inv) => throw new NotSupportedException();
        public void Unregister(SiloAddress address) => throw new NotSupportedException();
    }
}
