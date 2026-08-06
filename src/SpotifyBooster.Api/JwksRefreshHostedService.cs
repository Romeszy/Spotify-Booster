namespace SpotifyBooster.Api;

public sealed class JwksRefreshHostedService(
    JwksCoordinator coordinator,
    TimeProvider time) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshIgnoringShutdownAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(PollInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await RefreshIgnoringShutdownAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task RefreshIgnoringShutdownAsync(CancellationToken cancellationToken)
    {
        try { await coordinator.EnsureFreshAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
