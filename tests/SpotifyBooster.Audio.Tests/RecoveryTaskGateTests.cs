using SpotifyBooster.Audio.Engine;

namespace SpotifyBooster.Audio.Tests;

public sealed class RecoveryTaskGateTests
{
    [Fact]
    public async Task Stopping_PreventsRecoveryFromStarting()
    {
        var gate = new RecoveryTaskGate();
        bool started = false;
        gate.SetStopping(true);

        bool accepted = gate.TryStart(_ =>
        {
            started = true;
            return Task.CompletedTask;
        });

        Assert.False(accepted);
        Assert.False(started);
        await gate.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_CancelsAndAwaitsRetainedRecovery()
    {
        var gate = new RecoveryTaskGate();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(gate.TryStart(async cancellationToken =>
        {
            entered.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.SetResult();
                await release.Task;
            }
        }));
        await entered.Task;

        Task disposeTask = gate.DisposeAsync().AsTask();
        await cancellationObserved.Task;

        Assert.False(disposeTask.IsCompleted);
        Assert.False(gate.TryStart(_ => Task.CompletedTask));
        release.SetResult();
        await disposeTask;
    }
}
