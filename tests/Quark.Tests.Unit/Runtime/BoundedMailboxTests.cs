using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Resource-exhaustion coverage for the bounded mailbox (issue #55 [A2]):
///     a single grain must not accept unbounded queued work. With
///     <see cref="MailboxFullMode.RejectWhenFull"/> and a finite capacity, posting beyond the
///     capacity (while the reader is busy) throws <see cref="MailboxFullException"/> instead of
///     growing the queue without limit. The default (capacity 0) remains unbounded.
/// </summary>
public sealed class BoundedMailboxTests
{
    private static readonly GrainType Type = new("Mailbox");

    private static GrainActivation Create(int capacity, MailboxFullMode mode) =>
        new(new GrainId(Type, "g"), Type, isReentrant: false,
            new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider(),
            NullLogger<GrainActivation>.Instance,
            SimpleActivationScheduler.Instance,
            mailboxCapacity: capacity,
            mailboxFullMode: mode);

    [Fact]
    public async Task PostAsync_Rejects_When_Mailbox_Full()
    {
        await using GrainActivation grain = Create(capacity: 2, MailboxFullMode.RejectWhenFull);

        // Occupy the single reader with a work item that blocks until we release it.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = grain.PostAsync(async () =>
        {
            started.SetResult();
            await release.Task;
        });
        await started.Task; // reader is now blocked, mailbox buffer is empty

        // Fill the bounded buffer to capacity (these never run — reader is blocked).
        _ = grain.PostAsync(() => ValueTask.CompletedTask);
        _ = grain.PostAsync(() => ValueTask.CompletedTask);

        // The next post overflows the buffer and must be rejected.
        await Assert.ThrowsAsync<MailboxFullException>(
            () => grain.PostAsync(() => ValueTask.CompletedTask).AsTask());

        release.SetResult();
    }

    [Fact]
    public async Task PostAsync_Unbounded_By_Default_Accepts_Many_Queued_Items()
    {
        await using GrainActivation grain = Create(capacity: 0, MailboxFullMode.RejectWhenFull);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = grain.PostAsync(async () =>
        {
            started.SetResult();
            await release.Task;
        });
        await started.Task;

        // With capacity 0 the queue is unbounded; queueing far more than any cap must not throw.
        for (int i = 0; i < 1000; i++)
        {
            _ = grain.PostAsync(() => ValueTask.CompletedTask);
        }

        release.SetResult();
    }
}
