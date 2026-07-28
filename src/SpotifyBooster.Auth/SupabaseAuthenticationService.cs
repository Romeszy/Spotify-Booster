using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpotifyBooster.Contracts.Auth;

namespace SpotifyBooster.Auth;

public sealed class SupabaseAuthenticationService : IAuthenticationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly SupabaseAuthOptions _options;
    private readonly IAuthSessionStore _store;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private volatile bool _signingOut;

    public SupabaseAuthenticationService(HttpClient httpClient, SupabaseAuthOptions options, IAuthSessionStore store)
    {
        _httpClient = httpClient;
        _options = options;
        _store = store;
        options.Validate();
    }

    public async Task<AuthSessionContract?> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePassword(request.Password);
        var response = await SendAsync(HttpMethod.Post, "auth/v1/signup",
            new { email = request.Email, password = request.Password }, null, cancellationToken).ConfigureAwait(false);
        return response.AccessToken is null ? null : ToContract(response);
    }

    public async Task<AuthSessionContract> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePassword(request.Password);
        var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=password",
            new { email = request.Email, password = request.Password }, null, cancellationToken).ConfigureAwait(false);
        var session = ToContract(response);
        await _store.WriteAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task<SignOutResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _signingOut = true;
            var session = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (session is null)
                return new(RemoteRevocation.NotAttempted);
            try
            {
                await SendWithoutBodyAsync(HttpMethod.Post, "auth/v1/logout?scope=local",
                    session.AccessToken, cancellationToken).ConfigureAwait(false);
                return new(RemoteRevocation.Succeeded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return new(RemoteRevocation.Failed); }
        }
        finally
        {
            await _store.WriteAsync(null, CancellationToken.None).ConfigureAwait(false);
            _signingOut = false;
            _lifecycleGate.Release();
        }
    }

    public async Task<AuthSessionContract> RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_signingOut) throw new InvalidOperationException("Sign-out is in progress.");
            var current = await _store.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No active session.");
            var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token",
                new { refresh_token = current.RefreshToken }, null, cancellationToken).ConfigureAwait(false);
            var updated = ToContract(response);
            await _store.WriteAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally { _lifecycleGate.Release(); }
    }

    public Task ResendVerificationAsync(string email, CancellationToken cancellationToken = default) =>
        SendAndDiscardAsync("auth/v1/resend", new { type = "signup", email }, cancellationToken);

    public async Task RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        try { await SendAndDiscardAsync("auth/v1/recover", new { email = request.Email }, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { } // Deliberately generic to prevent account enumeration.
    }

    public async Task UpdatePasswordAsync(UpdatePasswordRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePassword(request.Password);
        var session = await _store.ReadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No active session.");
        await SendAsync(HttpMethod.Put, "auth/v1/user", new { password = request.Password },
            session.AccessToken, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<AuthSessionContract?> GetSessionAsync(CancellationToken cancellationToken = default) =>
        _store.ReadAsync(cancellationToken);

    private async Task SendAndDiscardAsync(string path, object body, CancellationToken cancellationToken) =>
        _ = await SendAsync(HttpMethod.Post, path, body, null, cancellationToken).ConfigureAwait(false);

    private async Task<AuthWireResponse> SendAsync(HttpMethod method, string path, object body, string? bearer,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.BaseUri, path));
        request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        request.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AuthenticationException("Authentication request was rejected.");
        return await ReadBoundedJsonAsync(response, timeout.Token).ConfigureAwait(false);
    }

    private async Task SendWithoutBodyAsync(HttpMethod method, string path, string bearer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.BaseUri, path));
        request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
        request.Headers.Authorization = new("Bearer", bearer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new AuthenticationException("Authentication request was rejected.");
    }

    private async Task<AuthWireResponse> ReadBoundedJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > _options.MaximumResponseBytes)
            throw new AuthenticationException("Authentication response is invalid.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var limited = new LimitedReadStream(stream, _options.MaximumResponseBytes);
        try
        {
            return await JsonSerializer.DeserializeAsync<AuthWireResponse>(limited, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new AuthenticationException("Authentication response is invalid.");
        }
        catch (JsonException) { throw new AuthenticationException("Authentication response is invalid."); }
    }

    private static AuthSessionContract ToContract(AuthWireResponse response)
    {
        if (response.User is null || response.AccessToken is null || response.RefreshToken is null || response.ExpiresIn <= 0)
            throw new AuthenticationException("Authentication response is invalid.");
        return new(new(response.User.Id, response.User.Email ?? string.Empty, response.User.EmailConfirmedAt is not null),
            DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn), response.AccessToken, response.RefreshToken);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 10) throw new ArgumentException("Password must contain at least 10 characters.", nameof(password));
    }

    private sealed record AuthWireResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        AuthWireUser? User);
    private sealed record AuthWireUser(Guid Id, string? Email,
        [property: JsonPropertyName("email_confirmed_at")] DateTimeOffset? EmailConfirmedAt);
}

public sealed class AuthenticationException : Exception
{
    public AuthenticationException(string message) : base(message) { }
}

internal sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
{
    private long _read;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Track(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
    private int Track(int count)
    {
        _read += count;
        if (_read > maximumBytes) throw new AuthenticationException("Authentication response is invalid.");
        return count;
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
