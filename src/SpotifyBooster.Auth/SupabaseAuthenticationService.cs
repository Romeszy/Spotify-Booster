using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpotifyBooster.Contracts.Auth;
using SpotifyBooster.Core.Abstractions;

namespace SpotifyBooster.Auth;

public sealed class SupabaseAuthenticationService : IAuthenticationService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly StrictSupabaseAuthHttpClient _httpClient;
    private readonly SupabaseAuthOptions _options;
    private readonly IAuthSessionStore _store;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _refreshSync = new();
    private readonly object _lifecycleSync = new();
    private Task<AuthSessionContract>? _refreshTask;
    private bool _signingOut;
    private long _lifecycleEpoch;

    public SupabaseAuthenticationService(SupabaseAuthOptions options, IAuthSessionStore store)
        : this(StrictSupabaseAuthHttpClient.CreateProduction(options), options, store)
    {
    }

    internal SupabaseAuthenticationService(StrictSupabaseAuthHttpClient httpClient,
        SupabaseAuthOptions options, IAuthSessionStore store)
    {
        _httpClient = httpClient;
        _options = options;
        _store = store;
        options.Validate();
        if (_httpClient.DefaultRequestHeaders.Authorization is not null ||
            _httpClient.DefaultRequestHeaders.Contains("apikey"))
            throw new InvalidOperationException("Identity headers must be request-scoped.");
        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = options.BaseUri;
        else if (_httpClient.BaseAddress.GetLeftPart(UriPartial.Authority) !=
            options.BaseUri.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("Auth client origin does not match configuration.");
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
        var epoch = CaptureActiveEpoch();
        var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=password",
            new { email = request.Email, password = request.Password }, null, cancellationToken).ConfigureAwait(false);
        var session = ToContract(response);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureEpochIsCurrent(epoch);
            await _store.WriteAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task<SignOutResult> SignOutAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
        {
            if (_signingOut)
                return new(RemoteRevocation.NotAttempted);
            _signingOut = true;
            _lifecycleEpoch = checked(_lifecycleEpoch + 1);
        }
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var session = await _store.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            if (session is null)
                return new(RemoteRevocation.NotAttempted);
            await _store.WriteAsync(null, CancellationToken.None).ConfigureAwait(false);
            try
            {
                await SendWithoutBodyAsync(HttpMethod.Post, "auth/v1/logout?scope=local",
                    session.AccessToken, cancellationToken).ConfigureAwait(false);
                return new(RemoteRevocation.Succeeded);
            }
            catch (HttpRequestException) { return new(RemoteRevocation.Failed); }
            catch (AuthenticationException) { return new(RemoteRevocation.Failed); }
            catch (OperationCanceledException) { return new(RemoteRevocation.Failed); }
        }
        finally
        {
            await _store.WriteAsync(null, CancellationToken.None).ConfigureAwait(false);
            lock (_lifecycleSync) _signingOut = false;
            _lifecycleGate.Release();
        }
    }

    public async Task<AuthSessionContract> RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        Task<AuthSessionContract> shared;
        lock (_refreshSync)
            shared = _refreshTask ??= RefreshCoreAsync();
        try { return await shared.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (shared.IsCompleted)
                lock (_refreshSync)
                    if (ReferenceEquals(_refreshTask, shared)) _refreshTask = null;
        }
    }

    private async Task<AuthSessionContract> RefreshCoreAsync()
    {
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var epoch = CaptureActiveEpoch();
            var current = await _store.ReadAsync(CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No active session.");
            var response = await SendAsync(HttpMethod.Post, "auth/v1/token?grant_type=refresh_token",
                new { refresh_token = current.RefreshToken }, null, CancellationToken.None).ConfigureAwait(false);
            var updated = ToContract(response);
            EnsureEpochIsCurrent(epoch);
            await _store.WriteAsync(updated, CancellationToken.None).ConfigureAwait(false);
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
        catch (HttpRequestException) { } // Deliberately generic to prevent account enumeration.
        catch (AuthenticationException) { }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    public async Task UpdatePasswordAsync(UpdatePasswordRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePassword(request.Password);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var epoch = CaptureActiveEpoch();
            var session = await _store.ReadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No active session.");
            if (session.AccessTokenExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("The active session has expired.");
            await SendAsync(HttpMethod.Put, "auth/v1/user", new { password = request.Password },
                session.AccessToken, cancellationToken).ConfigureAwait(false);
            EnsureEpochIsCurrent(epoch);
        }
        finally { _lifecycleGate.Release(); }
    }

    public ValueTask<AuthSessionContract?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleSync)
            return _signingOut ? ValueTask.FromResult<AuthSessionContract?>(null) :
                _store.ReadAsync(cancellationToken);
    }

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
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new AuthenticationException("Authentication transport failed.");
        }
        using (response)
        {
            EnsureNoRedirect(response);
            if (!response.IsSuccessStatusCode)
                throw new AuthenticationException("Authentication request was rejected.");
            return await ReadBoundedJsonAsync(response, timeout.Token).ConfigureAwait(false);
        }
    }

    private async Task SendWithoutBodyAsync(HttpMethod method, string path, string bearer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.BaseUri, path));
        request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
        request.Headers.Authorization = new("Bearer", bearer);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new AuthenticationException("Authentication transport failed.");
        }
        using (response)
        {
            EnsureNoRedirect(response);
            if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
                throw new AuthenticationException("Authentication request was rejected.");
        }
    }

    private static void EnsureNoRedirect(HttpResponseMessage response)
    {
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new AuthenticationException("Authentication redirect rejected.");
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
        if (string.IsNullOrEmpty(password) || password.Length < 10)
            throw new ArgumentException("Password must contain at least 10 characters.", nameof(password));
    }

    public void Dispose()
    {
        _lifecycleGate.Dispose();
        _httpClient.Dispose();
    }

    private long CaptureActiveEpoch()
    {
        lock (_lifecycleSync)
        {
            if (_signingOut)
                throw new InvalidOperationException("Sign-out is in progress.");
            return _lifecycleEpoch;
        }
    }

    private void EnsureEpochIsCurrent(long epoch)
    {
        lock (_lifecycleSync)
            if (_signingOut || epoch != _lifecycleEpoch)
                throw new InvalidOperationException("Authentication lifecycle changed.");
    }

    private sealed record AuthWireResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        AuthWireUser? User);
    private sealed record AuthWireUser(Guid Id, string? Email,
        [property: JsonPropertyName("email_confirmed_at")] DateTimeOffset? EmailConfirmedAt);
}

internal sealed class StrictSupabaseAuthHttpClient : HttpClient
{
    public StrictSupabaseAuthHttpClient(HttpMessageHandler handler, SupabaseAuthOptions options)
        : base(handler, disposeHandler: true)
    {
        options.Validate();
        BaseAddress = options.BaseUri;
    }

    public static StrictSupabaseAuthHttpClient CreateProduction(SupabaseAuthOptions options) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }, options);
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
