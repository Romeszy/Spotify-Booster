using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SpotifyBooster.Api;

namespace SpotifyBooster.Api.Tests;

public sealed class ApiSecurityTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly ECDsa _ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private readonly ApiMetrics _metrics = new();

    [Fact]
    public async Task ValidRs256TokenPassesAndWrongAlgorithmFailsClosed()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("key-1")));
        });
        var validator = new SupabaseJwtValidator(coordinator, Options(), _time, _metrics);
        var subject = Guid.NewGuid();
        var valid = Token("RS256", "key-1", subject, _time.GetUtcNow().AddMinutes(5));
        var first = await validator.ValidateAsync(valid, TestContext.Current.CancellationToken);
        var second = await validator.ValidateAsync(valid, TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);
        Assert.Equal(subject, first.Subject);
        Assert.True(second.Succeeded);
        Assert.Equal(1, calls);

        var unexpected = Token("HS256", "key-1", subject, _time.GetUtcNow().AddMinutes(5));
        Assert.Equal("invalid_algorithm", (await validator.ValidateAsync(unexpected, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public async Task ValidEs256TokenPasses()
    {
        using var coordinator = Coordinator((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, EcJwks("ec-key"))));
        var validator = new SupabaseJwtValidator(coordinator, Options(), _time, _metrics);
        var subject = Guid.NewGuid();
        var result = await validator.ValidateAsync(
            Token("ES256", "ec-key", subject, _time.GetUtcNow().AddMinutes(5)),
            TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal(subject, result.Subject);
    }

    [Fact]
    public async Task IssuerAudienceExpirationAndSubjectAreValidated()
    {
        using var coordinator = Coordinator((_, _) => Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("k"))));
        var validator = new SupabaseJwtValidator(coordinator, Options(), _time, _metrics);
        Assert.Equal("issuer", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(1), issuer: "https://evil.example"), TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("audience", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(1), audience: "wrong"), TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("expired", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(-2)), TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("missing_subject", (await validator.ValidateAsync(Token("RS256", "k", null,
            _time.GetUtcNow().AddMinutes(1)), TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public async Task JwksExpiresAtExactTenMinuteBoundaryAndRetryGatePreventsStorm()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? Json(HttpStatusCode.OK, RsaJwks("known"))
                : Json(HttpStatusCode.ServiceUnavailable, "{}"));
        });
        Assert.NotNull(await coordinator.FindAsync("known", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));
        Assert.True(coordinator.IsReady);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(coordinator.IsReady);
        Assert.Null(await coordinator.FindAsync("known", TestContext.Current.CancellationToken));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => coordinator.FindAsync("other", TestContext.Current.CancellationToken)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnknownKidsShareOneGlobalCooldown()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("known")));
        });
        Assert.NotNull(await coordinator.FindAsync("known", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromSeconds(60));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            coordinator.FindAsync("unknown-" + i, TestContext.Current.CancellationToken)));
        Assert.Equal(2, calls);
        Assert.All(await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            coordinator.FindAsync("again-" + i, TestContext.Current.CancellationToken))), Assert.Null);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AdministrativePurgeImmediatelyReplacesSnapshot()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            var kid = Interlocked.Increment(ref calls) == 1 ? "old" : "new";
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks(kid)));
        });
        Assert.NotNull(await coordinator.FindAsync("old", TestContext.Current.CancellationToken));
        await coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(await coordinator.FindAsync("new", TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CallerCancellationDoesNotCancelSharedJwksFetch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = Coordinator((_, _) =>
        {
            started.SetResult();
            return response.Task;
        });
        using var caller = new CancellationTokenSource();
        var cancelledWaiter = coordinator.FindAsync("shared", caller.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);

        var joiner = coordinator.FindAsync("shared", TestContext.Current.CancellationToken);
        response.SetResult(Json(HttpStatusCode.OK, RsaJwks("shared")));
        Assert.NotNull(await joiner);
    }

    [Fact]
    public void FixedWindowLimiterHasExactBoundaryAndNoQueue()
    {
        var limiter = new FixedWindowLimiter(_time);
        for (var i = 0; i < 30; i++) Assert.True(limiter.Acquire("sub:a", 30).Allowed);
        var rejected = limiter.Acquire("sub:a", 30);
        Assert.False(rejected.Allowed);
        Assert.Equal(60, rejected.RetryAfterSeconds);
        Assert.True(limiter.Acquire("sub:b", 30).Allowed);
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.True(limiter.Acquire("sub:a", 30).Allowed);
    }

    [Fact]
    public async Task DataApiKeepsConcurrentBearerHeadersRequestScoped()
    {
        var seen = new List<(string ApiKey, string Bearer)>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var subjectA = Guid.NewGuid();
        var subjectB = Guid.NewGuid();
        using var client = new HttpClient(new Handler(async (request, _) =>
        {
            lock (seen) seen.Add((request.Headers.GetValues("apikey").Single(), request.Headers.Authorization!.Parameter!));
            if (Interlocked.Increment(ref count) == 2) gate.SetResult();
            await gate.Task;
            var bearer = request.Headers.Authorization!.Parameter!;
            var subject = bearer == "token-a" ? subjectA : subjectB;
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                userId = subject, displayName = (string?)null, plan = "free", status = "active", revision = 1, features = Array.Empty<string>()
            }));
        }));
        var api = new DataApiClient(client, Options(), _metrics);
        await Task.WhenAll(api.GetMeAsync(subjectA, "token-a", TestContext.Current.CancellationToken), api.GetMeAsync(subjectB, "token-b", TestContext.Current.CancellationToken));
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Contains(("publishable-test", "token-a"), seen);
        Assert.Contains(("publishable-test", "token-b"), seen);
    }

    [Fact]
    public async Task DataApiRejectsSubjectMismatchAndOversize()
    {
        using var mismatchClient = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"userId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","plan":"free","status":"active","revision":1,"features":[]}"""))));
        var api = new DataApiClient(mismatchClient, Options(), _metrics);
        await Assert.ThrowsAsync<DataApiException>(() => api.GetMeAsync(Guid.NewGuid(), "token", TestContext.Current.CancellationToken));

        using var largeClient = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, new string('x', 300_000)))));
        var largeApi = new DataApiClient(largeClient, Options(), _metrics);
        await Assert.ThrowsAsync<InvalidDataException>(() => largeApi.GetMeAsync(Guid.NewGuid(), "token", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadinessProbeRequiresExactThreePropertyDocumentAndNoBearer()
    {
        HttpRequestMessage? observed = null;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            observed = Clone(request);
            return Task.FromResult(Json(HttpStatusCode.OK,
                """{"service":"spotify-booster-data","status":"ok","version":1}"""));
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        Assert.True(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.True(state.IsDataApiReady);
        Assert.Null(observed!.Headers.Authorization);
        Assert.Equal("publishable-test", observed.Headers.GetValues("apikey").Single());
        Assert.Equal("/rest/v1/rpc/spotify_booster_health_v1", observed.RequestUri!.AbsolutePath);
        _time.Advance(TimeSpan.FromSeconds(61));
        Assert.False(state.IsDataApiReady);
    }

    [Fact]
    public async Task ReadinessRejectsAdditionalProperties()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"service":"spotify-booster-data","status":"ok","version":1,"detail":"leak"}"""))));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        Assert.False(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.False(state.IsDataApiReady);
    }

    [Theory]
    [InlineData("http://127.0.0.1:54321/")]
    [InlineData("https://localhost:54321/")]
    [InlineData("https://127.0.0.1:54322/")]
    [InlineData("https://user@project.supabase.co/")]
    public void InvalidOriginsAreRejected(string origin)
    {
        var options = Options() with { Origin = new(origin), Issuer = origin.TrimEnd('/') + "/auth/v1" };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private JwksCoordinator Coordinator(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var client = new HttpClient(new Handler(send));
        return new(client, Options(), _time, _metrics);
    }
    private static SupabaseApiOptions Options() => new()
    {
        Origin = new("https://project.supabase.co/"),
        PublishableKey = "publishable-test",
        Issuer = "https://project.supabase.co/auth/v1",
        Audience = "authenticated"
    };
    private string RsaJwks(string kid)
    {
        var p = _rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid, n = Encode(p.Modulus!), e = Encode(p.Exponent!) } }
        });
    }
    private string EcJwks(string kid)
    {
        var p = _ec.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "EC", use = "sig", alg = "ES256", crv = "P-256",
                kid, x = Encode(p.Q.X!), y = Encode(p.Q.Y!) } }
        });
    }
    private string Token(string algorithm, string kid, Guid? subject, DateTimeOffset expiration,
        string? issuer = null, string? audience = null)
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, typ = "JWT", kid }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = issuer ?? Options().Issuer, aud = audience ?? Options().Audience,
            exp = expiration.ToUnixTimeSeconds(), sub = subject?.ToString()
        }));
        var input = Encoding.ASCII.GetBytes(header + "." + payload);
        var signature = algorithm switch
        {
            "RS256" => _rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "ES256" => _ec.SignData(input, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => new byte[32]
        };
        return header + "." + payload + "." + Encode(signature);
    }
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
    public void Dispose() { _rsa.Dispose(); _ec.Dispose(); _metrics.Dispose(); }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = utcNow;
        public override long TimestampFrequency => 1_000_000;
        public override long GetTimestamp() => Volatile.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan value)
        {
            _utcNow += value;
            Interlocked.Add(ref _timestamp, (long)(value.TotalSeconds * TimestampFrequency));
        }
    }
}
