using System.Net;
using System.Text;
using System.Text.Json;
using SpotifyBooster.Auth;
using SpotifyBooster.Contracts.Auth;

namespace SpotifyBooster.Auth.Tests;

public sealed class SupabaseAuthenticationServiceTests
{
    [Fact]
    public async Task SignIn_UsesPublishableHeaderAndNeverUsesItAsBearer()
    {
        HttpRequestMessage? observed = null;
        var service = Create((request, _) =>
        {
            observed = Clone(request);
            return Json(HttpStatusCode.OK, SessionJson);
        });
        var session = await service.SignInAsync(new("u@example.test", "0123456789"), TestContext.Current.CancellationToken);
        Assert.Equal(UserId, session.User.Id);
        Assert.Equal("sb_publishable_test", observed!.Headers.GetValues("apikey").Single());
        Assert.Null(observed.Headers.Authorization);
        Assert.DoesNotContain("access-token", session.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token", session.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortPasswordIsRejectedWithoutNetwork()
    {
        var calls = 0;
        var service = Create((_, _) => { calls++; return Json(HttpStatusCode.OK, SessionJson); });
        await Assert.ThrowsAsync<ArgumentException>(() => service.SignInAsync(
            new("u@example.test", "123456789"), TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InvalidAndOversizedResponsesAreExpurgated()
    {
        var invalid = Create((_, _) => Json(HttpStatusCode.OK, "{not-json"));
        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            invalid.SignInAsync(new("u@example.test", "0123456789"), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("{not-json", exception.Message, StringComparison.Ordinal);

        var oversized = Create((_, _) => Json(HttpStatusCode.OK, new string('x', 300_000)));
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            oversized.SignInAsync(new("u@example.test", "0123456789"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, RemoteRevocation.Failed)]
    [InlineData(HttpStatusCode.NoContent, RemoteRevocation.Succeeded)]
    public async Task SignOutAlwaysClearsLocalSession(HttpStatusCode status, RemoteRevocation expected)
    {
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create((_, _) => new(status), store);
        var result = await service.SignOutAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.RemoteRevocation);
        Assert.Equal(LocalSignOut.Completed, result.LocalSignOut);
        Assert.Null(await store.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RemoteRevocation.NotAttempted,
            (await service.SignOutAsync(TestContext.Current.CancellationToken)).RemoteRevocation);
    }

    [Fact]
    public async Task ConcurrentRefreshIsSerializedAndRotatesSession()
    {
        var calls = 0;
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20, cancellationToken);
            return Json(HttpStatusCode.OK, SessionJson);
        }, store);
        await Task.WhenAll(
            service.RefreshSessionAsync(TestContext.Current.CancellationToken),
            service.RefreshSessionAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
        Assert.Equal("refresh-token", (await store.ReadAsync(TestContext.Current.CancellationToken))!.RefreshToken);
    }

    [Fact]
    public async Task SignOutHidesSessionBeforeRemoteRevocationCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create((_, _) =>
        {
            entered.SetResult();
            return release.Task;
        }, store);
        var signOut = service.SignOutAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Null(await service.GetSessionAsync(TestContext.Current.CancellationToken));
        release.SetResult(new(HttpStatusCode.NoContent));
        Assert.Equal(RemoteRevocation.Succeeded, (await signOut).RemoteRevocation);
    }

    [Fact]
    public async Task RedirectIsRejectedAndLocalSessionStillCleared()
    {
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create((_, _) => new(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://evil.example/") }
        }, store);
        var result = await service.SignOutAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RemoteRevocation.Failed, result.RemoteRevocation);
        Assert.Null(await store.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledSignOutStillClearsSessionAndReportsRemoteFailure()
    {
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.NoContent);
        }, store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await service.SignOutAsync(cancellation.Token);
        Assert.Equal(RemoteRevocation.Failed, result.RemoteRevocation);
        Assert.Null(await store.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SignOutEpochPreventsLateSignInFromRestoringSession()
    {
        var signInStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSignIn = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                signInStarted.SetResult();
                return releaseSignIn.Task;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }, store);
        var signIn = service.SignInAsync(new("u@example.test", "0123456789"),
            TestContext.Current.CancellationToken);
        await signInStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RemoteRevocation.Succeeded,
            (await service.SignOutAsync(TestContext.Current.CancellationToken)).RemoteRevocation);
        releaseSignIn.SetResult(Json(HttpStatusCode.OK, SessionJson));
        await Assert.ThrowsAsync<InvalidOperationException>(() => signIn);
        Assert.Null(await service.GetSessionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SignOutEpochPreventsInFlightRefreshFromPublishing()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create((request, _) =>
        {
            if (request.RequestUri!.Query.Contains("refresh_token", StringComparison.Ordinal))
            {
                refreshStarted.SetResult();
                return releaseRefresh.Task;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }, store);
        var refresh = service.RefreshSessionAsync(TestContext.Current.CancellationToken);
        await refreshStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var signOut = service.SignOutAsync(TestContext.Current.CancellationToken);
        Assert.Null(await service.GetSessionAsync(TestContext.Current.CancellationToken));
        releaseRefresh.SetResult(Json(HttpStatusCode.OK, SessionJson));
        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh);
        Assert.Equal(RemoteRevocation.Succeeded, (await signOut).RemoteRevocation);
        Assert.Null(await service.GetSessionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SharedIdentityHeadersAreRejectedAtConstruction()
    {
        var options = ValidOptions();
        var client = new StrictSupabaseAuthHttpClient(new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))), options);
        client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", "shared-identity");
        Assert.Throws<InvalidOperationException>(() =>
            new SupabaseAuthenticationService(client, options, new InMemoryAuthSessionStore()));
        client.Dispose();
    }

    [Fact]
    public async Task LogoutAcceptsOnlyExactNoContent()
    {
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create((_, _) => new(HttpStatusCode.OK), store);
        Assert.Equal(RemoteRevocation.Failed,
            (await service.SignOutAsync(TestContext.Current.CancellationToken)).RemoteRevocation);
        Assert.Null(await store.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SignupResendResetAndUpdateUseExpectedAuthContracts()
    {
        var requests = new List<HttpRequestMessage>();
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session(), TestContext.Current.CancellationToken);
        var service = Create((request, _) =>
        {
            requests.Add(Clone(request));
            return Json(HttpStatusCode.OK, SessionJson);
        }, store);
        var signup = await service.SignUpAsync(new("u@example.test", "0123456789"),
            TestContext.Current.CancellationToken);
        Assert.NotNull(signup);
        Assert.True(signup.User.EmailVerified);
        Assert.True(signup.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
        await service.ResendVerificationAsync("u@example.test", TestContext.Current.CancellationToken);
        await service.RequestPasswordResetAsync(new("u@example.test"), TestContext.Current.CancellationToken);
        await service.UpdatePasswordAsync(new("abcdefghij"), TestContext.Current.CancellationToken);
        Assert.Contains(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/resend",
            StringComparison.Ordinal));
        Assert.Contains(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/recover",
            StringComparison.Ordinal));
        var update = Assert.Single(requests, request =>
            request.RequestUri!.AbsolutePath.EndsWith("/user", StringComparison.Ordinal));
        Assert.Equal("access-token", update.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task SignupWithoutSessionAndUnverifiedEmailAreRepresentedHonestly()
    {
        var existing = Create((_, _) => Json(HttpStatusCode.OK,
            """{"user":{"id":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","email":"u@example.test"}}"""));
        Assert.Null(await existing.SignUpAsync(new("u@example.test", "0123456789"),
            TestContext.Current.CancellationToken));

        var unverifiedJson = JsonSerializer.Serialize(new
        {
            access_token = "access-token",
            refresh_token = "refresh-token",
            expires_in = 60,
            user = new { id = UserId, email = "u@example.test", email_confirmed_at = (string?)null }
        });
        var unverified = Create((_, _) => Json(HttpStatusCode.OK, unverifiedJson));
        var session = await unverified.SignUpAsync(new("u@example.test", "0123456789"),
            TestContext.Current.CancellationToken);
        Assert.NotNull(session);
        Assert.False(session.User.EmailVerified);
    }

    [Fact]
    public async Task PasswordResetIsGenericForNetworkAndServerFailures()
    {
        var network = Create((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("private-auth-host")));
        await network.RequestPasswordResetAsync(new("u@example.test"),
            TestContext.Current.CancellationToken);
        var server = Create((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await server.RequestPasswordResetAsync(new("u@example.test"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExpiredSessionCannotUpdatePassword()
    {
        var calls = 0;
        var store = new InMemoryAuthSessionStore();
        await store.WriteAsync(Session() with
        {
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        }, TestContext.Current.CancellationToken);
        var service = Create((_, _) =>
        {
            calls++;
            return Json(HttpStatusCode.OK, SessionJson);
        }, store);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdatePasswordAsync(new("abcdefghij"), TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AuthenticationTimeoutAndNetworkErrorsAreExpurgated()
    {
        var options = ValidOptions() with { Timeout = TimeSpan.FromMilliseconds(10) };
        var timeoutClient = new StrictSupabaseAuthHttpClient(new Handler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, SessionJson);
        }), options);
        var timeout = new SupabaseAuthenticationService(timeoutClient, options,
            new InMemoryAuthSessionStore());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            timeout.SignInAsync(new("u@example.test", "0123456789"),
                TestContext.Current.CancellationToken));

        var network = Create((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("private-auth-host")));
        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            network.SignInAsync(new("u@example.test", "0123456789"),
                TestContext.Current.CancellationToken));
        Assert.DoesNotContain("private-auth-host", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("service_role_secret")]
    [InlineData("sb_secret_value")]
    [InlineData("legacy-anon-jwt")]
    [InlineData("sb_publishable_service_role")]
    [InlineData("sb_publishable_sb_secret_value")]
    [InlineData("sb_publishable_bad value")]
    [InlineData("")]
    public void PrivilegedOrLegacyKeysAreRejected(string key)
    {
        var options = new SupabaseAuthOptions
        {
            BaseUri = new("https://project.supabase.co/"),
            PublishableKey = key
        };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static SupabaseAuthenticationService Create(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler,
        IAuthSessionStore? store = null) => Create((r, c) => Task.FromResult(handler(r, c)), store);

    private static SupabaseAuthenticationService Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        IAuthSessionStore? store = null)
    {
        var options = ValidOptions();
        var client = new StrictSupabaseAuthHttpClient(new Handler(handler), options);
        return new(client, options, store ?? new InMemoryAuthSessionStore());
    }
    private static SupabaseAuthOptions ValidOptions() => new()
    {
        BaseUri = new("https://project.supabase.co/"),
        PublishableKey = "sb_publishable_test"
    };
    private static HttpResponseMessage Json(HttpStatusCode code, string value) =>
        new(code) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
    private static AuthSessionContract Session() =>
        new(new(UserId, "u@example.test", true), DateTimeOffset.UtcNow.AddMinutes(5), "access-token", "old-refresh");
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static string SessionJson => JsonSerializer.Serialize(new
    {
        access_token = "access-token",
        refresh_token = "refresh-token",
        expires_in = 3600,
        user = new { id = UserId, email = "u@example.test", email_confirmed_at = "2026-01-01T00:00:00Z" }
    });

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
