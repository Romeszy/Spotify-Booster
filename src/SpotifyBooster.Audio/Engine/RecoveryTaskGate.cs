namespace SpotifyBooster.Audio.Engine;

internal sealed class RecoveryTaskGate : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private Task recoveryTask = Task.CompletedTask;
    private bool stopping;
    private bool disposed;

    public bool TryStart(Func<CancellationToken, Task> recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        lock (sync)
        {
            if (disposed || stopping || !recoveryTask.IsCompleted)
            {
                return false;
            }

            recoveryTask = recovery(lifetimeCancellation.Token);
            return true;
        }
    }

    public void SetStopping(bool value)
    {
        lock (sync)
        {
            if (!disposed)
            {
                stopping = value;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task taskToAwait;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopping = true;
            lifetimeCancellation.Cancel();
            taskToAwait = recoveryTask;
        }

        try
        {
            await taskToAwait.ConfigureAwait(false);
        }
        finally
        {
            lifetimeCancellation.Dispose();
        }
    }
}
