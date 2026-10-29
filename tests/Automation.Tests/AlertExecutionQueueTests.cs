using Automation.Engine.Execution;
using Xunit;

namespace Automation.Tests;

public class AlertExecutionQueueTests
{
    [Fact]
    public async Task NonBlockingQueueAllowsConcurrencyButStillHonorsPause()
    {
        var queue = new AlertExecutionQueue(); queue.SetInterval(TimeSpan.FromMinutes(1)); queue.SetBlocking(false);
        await queue.WaitAsync(TestContext.Current.CancellationToken);
        await queue.WaitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        queue.SetPaused(true);
        var waiting = queue.WaitAsync(TestContext.Current.CancellationToken);
        queue.Release(); Assert.True(queue.IsRunning); Assert.False(waiting.IsCompleted);
        queue.Release(); Assert.False(queue.IsRunning); Assert.False(waiting.IsCompleted);
        queue.SetPaused(false);
        await waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        queue.Release(); Assert.False(queue.IsRunning);
    }
    [Fact]
    public async Task IntervalDelaysTheNextAlertAfterCompletion()
    {
        var queue = new AlertExecutionQueue(); queue.SetInterval(TimeSpan.FromMilliseconds(100));
        await queue.WaitAsync(TestContext.Current.CancellationToken);
        var next = queue.WaitAsync(TestContext.Current.CancellationToken);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        queue.Release();
        await next.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(90));
        queue.Release();
    }
    [Fact]
    public async Task PausedQueueDoesNotBlockAnotherQueue()
    {
        var first = new AlertExecutionQueue(); var second = new AlertExecutionQueue();
        first.SetPaused(true);
        var waiting = first.WaitAsync(TestContext.Current.CancellationToken);
        await second.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(second.IsRunning); Assert.False(waiting.IsCompleted);
        second.Release(); first.SetPaused(false);
        await waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        first.Release();
    }
    [Fact]
    public async Task ExecutesInArrivalOrderWithoutOverlap()
    {
        var queue = new AlertExecutionQueue();
        await queue.WaitAsync(TestContext.Current.CancellationToken);
        var second = queue.WaitAsync(TestContext.Current.CancellationToken); var third = queue.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, queue.PendingCount);
        Assert.False(second.IsCompleted); Assert.False(third.IsCompleted);
        queue.Release(); await second.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(third.IsCompleted);
        queue.Release(); await third.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        queue.Release(); Assert.False(queue.IsRunning);
    }

    [Fact]
    public async Task PausedQueueSkipsCancelledEntriesAndResumes()
    {
        var queue = new AlertExecutionQueue(); queue.SetPaused(true);
        using var cancellation = new CancellationTokenSource();
        var cancelled = queue.WaitAsync(cancellation.Token); var next = queue.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(next.IsCompleted); Assert.Equal(1, queue.PendingCount);
        queue.SetPaused(false); await next.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        queue.Release(); Assert.False(queue.IsRunning);
    }
}
