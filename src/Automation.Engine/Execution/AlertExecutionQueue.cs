namespace Automation.Engine.Execution;

/// <summary>FIFO admission shared by automatic and manual workflow executions.</summary>
public sealed class AlertExecutionQueue
{
    private readonly object sync = new();
    private readonly Queue<TaskCompletionSource<bool>> pending = new();
    private int running;
    private bool blocking = true;
    private bool paused;
    private TimeSpan interval;
    private DateTimeOffset nextAllowedAt;
    private bool waitingForInterval;
    public void SetInterval(TimeSpan value)
    {
        if (value < TimeSpan.Zero || value > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(value));
        lock (sync) interval = value;
    }
    public bool IsPaused { get { lock (sync) return paused; } }
    public bool IsRunning { get { lock (sync) return running > 0; } }
    public void SetBlocking(bool value)
    {
        lock (sync) { blocking = value; if (!blocking) nextAllowedAt = default; StartNext(); }
    }
    public int PendingCount { get { lock (sync) return pending.Count(item => !item.Task.IsCompleted); } }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync) { pending.Enqueue(item); StartNext(); }
        using var registration = cancellationToken.Register(() => item.TrySetCanceled(cancellationToken));
        await item.Task.ConfigureAwait(false);
    }

    public void Release()
    {
        lock (sync) { running = Math.Max(0, running - 1); if (blocking) nextAllowedAt = DateTimeOffset.UtcNow + interval; StartNext(); }
    }

    public void SetPaused(bool value)
    {
        lock (sync) { paused = value; StartNext(); }
    }

    private void StartNext()
    {
        if ((blocking && running > 0) || paused) return;
        if (blocking && pending.Count > 0 && nextAllowedAt > DateTimeOffset.UtcNow)
        {
            if (!waitingForInterval)
            {
                waitingForInterval = true;
                _ = WakeAfterIntervalAsync(nextAllowedAt - DateTimeOffset.UtcNow);
            }
            return;
        }
        while (pending.TryDequeue(out var item))
            if (item.TrySetResult(true))
            {
                running++;
                if (blocking) return;
            }
    }

    private async Task WakeAfterIntervalAsync(TimeSpan delay)
    {
        await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero).ConfigureAwait(false);
        lock (sync) { waitingForInterval = false; StartNext(); }
    }
}
