using SpotifyBooster.Contracts.Auth;

namespace SpotifyBooster.Auth;

public interface IAuthSessionStore
{
    ValueTask<AuthSessionContract?> ReadAsync(CancellationToken cancellationToken);
    ValueTask WriteAsync(AuthSessionContract? session, CancellationToken cancellationToken);
}

public sealed class InMemoryAuthSessionStore : IAuthSessionStore
{
    private AuthSessionContract? _session;
    public ValueTask<AuthSessionContract?> ReadAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _session));
    public ValueTask WriteAsync(AuthSessionContract? session, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _session, session);
        return ValueTask.CompletedTask;
    }
}
