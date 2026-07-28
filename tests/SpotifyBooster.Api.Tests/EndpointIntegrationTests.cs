using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SpotifyBooster.Api;

namespace SpotifyBooster.Api.Tests;

public sealed class EndpointIntegrationTests
{
    [Fact]
    public async Task LiveIsAnonymousAndMinimal()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(
            TestContext.Current.CancellationToken);
        var pair = Assert.Single(body!);
        Assert.Equal("status", pair.Key);
        Assert.Equal("live", pair.Value);
    }

    [Fact]
    public async Task ReadyStartsExpurgatedAndUnavailable()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("""{"status":"unready"}""", text);
    }

    [Fact]
    public async Task MeRejectsAnonymousRequestWithProblemDetails()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/v1/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"code\":\"unauthorized\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("publishable-test-placeholder", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RS256")]
    [InlineData("ES256")]
    public async Task HostedPipelineAcceptsValidAsymmetricTokens(string algorithm)
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(Bearer("/v1/me",
            factory.Token(algorithm, factory.Subject)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(factory.Subject.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token-", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostedPipelineRejectsInvalidSignatureFutureNbfAndSubjectMismatch()
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        using var otherKey = RSA.Create(2048);
        using var otherEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var invalidSignature = await client.SendAsync(Bearer("/v1/me",
            factory.Token("RS256", factory.Subject, signer: otherKey)),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidSignature.StatusCode);
        using var invalidEcSignature = await client.SendAsync(Bearer("/v1/me",
            factory.Token("ES256", factory.Subject, ecSigner: otherEc)),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidEcSignature.StatusCode);

        using var future = await client.SendAsync(Bearer("/v1/me",
            factory.Token("RS256", factory.Subject, DateTimeOffset.UtcNow.AddMinutes(2))),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, future.StatusCode);

        factory.ReturnMismatchedSubject = true;
        using var mismatch = await client.SendAsync(Bearer("/v1/me",
            factory.Token("RS256", factory.Subject)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, mismatch.StatusCode);
        var problem = await mismatch.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(factory.Subject.ToString(), problem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mismatch@example.test", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("token-", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HS256", null, null, 5, true)]
    [InlineData("RS256", "https://evil.example/auth/v1", null, 5, true)]
    [InlineData("RS256", null, "wrong", 5, true)]
    [InlineData("RS256", null, null, -2, true)]
    [InlineData("RS256", null, null, 5, false)]
    public async Task HostedPipelineRejectsEveryJwtContractViolation(string algorithm,
        string? issuer, string? audience, int expiryMinutes, bool includeSubject)
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        var token = factory.Token(algorithm, factory.Subject, issuer: issuer,
            audience: audience, expiration: DateTimeOffset.UtcNow.AddMinutes(expiryMinutes),
            includeSubject: includeSubject);
        using var response = await client.SendAsync(Bearer("/v1/me", token),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(token, problem, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostedRateLimiterReturnsExpurgated429()
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        var token = factory.Token("RS256", factory.Subject);
        for (var i = 0; i < 30; i++)
        {
            using var allowed = await client.SendAsync(Bearer("/v1/me", token),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var rejected = await client.SendAsync(Bearer("/v1/me", token),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.InRange(int.Parse(rejected.Headers.GetValues("Retry-After").Single(),
            System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        var body = await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(factory.Subject.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostedRateLimiterPartitionsSubjectsAndInvalidIdentity()
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        var subjectB = Guid.NewGuid();
        foreach (var subject in new[] { factory.Subject, subjectB })
            for (var i = 0; i < 30; i++)
            {
                using var allowed = await client.SendAsync(Bearer("/v1/me",
                    factory.Token("RS256", subject)), TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            }
        for (var i = 0; i < 10; i++)
        {
            using var unauthorized = await client.SendAsync(Bearer("/v1/me", "malformed"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }
        using var limited = await client.SendAsync(Bearer("/v1/me", "malformed"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task GlobalProblemDetailsRedactsUnexpectedDataApiFailure()
    {
        await using var factory = new AuthenticatedFactory { ThrowUnexpectedDataApi = true };
        using var client = factory.CreateClient();
        var token = factory.Token("RS256", factory.Subject);
        using var response = await client.SendAsync(Bearer("/v1/me", token),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("private-database-host", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(token, problem, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HostLogsRemainFreeOfCredentialsIdentityPrivateUrlsAndStackTraces()
    {
        await using var factory = new AuthenticatedFactory { ThrowUnexpectedDataApi = true };
        using var client = factory.CreateClient();
        var token = factory.Token("RS256", factory.Subject);
        using var response = await client.SendAsync(Bearer("/v1/me", token),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var combined = string.Join(Environment.NewLine, factory.Logs);
        foreach (var prohibited in new[]
        {
            token, "password-test-value", "person@example.test", "sb_publishable_test",
            "https://private.example/internal", "private-database-host"
        })
            Assert.True(!combined.Contains(prohibited, StringComparison.Ordinal),
                "Host logs contained a prohibited sensitive value.");
        Assert.True(!combined.Contains(" at ", StringComparison.Ordinal),
            "Host logs contained a stack trace.");
    }

    [Fact]
    public async Task HealthRoutesNeverTriggerJwtOrJwks()
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        var calls = factory.JwksCalls;
        using var request = Bearer("/health/live", "malformed.secret.value");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(calls, factory.JwksCalls);
    }

    [Fact]
    public async Task HostedReadinessRecoversAndDegradesWithoutSensitiveDetails()
    {
        await using var factory = new AuthenticatedFactory();
        using var client = factory.CreateClient();
        using (var me = await client.SendAsync(Bearer("/v1/me",
            factory.Token("RS256", factory.Subject)), TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        factory.HealthReady = true;
        var probe = factory.Services.GetRequiredService<DataApiReadinessProbe>();
        Assert.True(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        using (var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        factory.HealthReady = false;
        Assert.False(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        using var degraded = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, degraded.StatusCode);
        Assert.Equal("""{"status":"unready"}""",
            await degraded.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static HttpRequestMessage Bearer(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private sealed class AuthenticatedFactory : WebApplicationFactory<Program>
    {
        private readonly RSA _rsa = RSA.Create(2048);
        private readonly ECDsa _ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly CaptureLoggerProvider _logs = new();
        private int _jwksCalls;
        public Guid Subject { get; } = Guid.NewGuid();
        public bool ReturnMismatchedSubject { get; set; }
        public bool HealthReady { get; set; }
        public bool ThrowUnexpectedDataApi { get; set; }
        public int JwksCalls => Volatile.Read(ref _jwksCalls);
        public IReadOnlyList<string> Logs => _logs.Messages;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<JwksCoordinator>();
                services.RemoveAll<DataApiClient>();
                services.RemoveAll<DataApiReadinessProbe>();
                services.AddSingleton(provider => new JwksCoordinator(
                    Client(Handle), provider.GetRequiredService<SupabaseApiOptions>(),
                    provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ApiMetrics>()));
                services.AddSingleton(provider => new DataApiClient(
                    Client(Handle), provider.GetRequiredService<SupabaseApiOptions>(),
                    provider.GetRequiredService<ApiMetrics>()));
                services.AddSingleton(provider => new DataApiReadinessProbe(
                    Client(Handle), provider.GetRequiredService<SupabaseApiOptions>(),
                    provider.GetRequiredService<ReadinessState>(),
                    provider.GetRequiredService<TimeProvider>()));
            });
        }

        private Task<HttpResponseMessage> Handle(HttpRequestMessage request, CancellationToken _)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/jwks.json", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _jwksCalls);
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, Jwks()));
            }
            if (path.EndsWith("/spotify_booster_health_v1", StringComparison.Ordinal))
                return Task.FromResult(HealthReady
                    ? JsonResponse(HttpStatusCode.OK,
                        """{"service":"spotify-booster-data","status":"ok","version":1}""")
                    : JsonResponse(HttpStatusCode.ServiceUnavailable, "{}"));
            if (path.EndsWith("/spotify_booster_me_v1", StringComparison.Ordinal))
            {
                if (ThrowUnexpectedDataApi)
                    return Task.FromException<HttpResponseMessage>(
                        new InvalidOperationException(
                            "private-database-host https://private.example/internal " +
                            "person@example.test password-test-value sb_publishable_test"));
                var subject = ReturnMismatchedSubject ? Guid.NewGuid() :
                    SubjectFromBearer(request) ?? Subject;
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    userId = subject,
                    displayName = "mismatch@example.test",
                    plan = "free",
                    status = "active",
                    revision = 1,
                    features = Array.Empty<string>()
                })));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        public string Token(string algorithm, Guid subject, DateTimeOffset? notBefore = null,
            RSA? signer = null, ECDsa? ecSigner = null, string? issuer = null,
            string? audience = null, DateTimeOffset? expiration = null, bool includeSubject = true)
        {
            var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = algorithm,
                typ = "JWT",
                kid = algorithm == "RS256" ? "rsa" : "ec"
            }));
            var claims = new Dictionary<string, object?>
            {
                ["iss"] = issuer ?? "https://127.0.0.1:54321/auth/v1",
                ["aud"] = audience ?? "authenticated",
                ["exp"] = (expiration ?? DateTimeOffset.UtcNow.AddMinutes(5)).ToUnixTimeSeconds()
            };
            if (includeSubject) claims["sub"] = subject.ToString();
            if (notBefore is not null) claims["nbf"] = notBefore.Value.ToUnixTimeSeconds();
            var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
            var input = Encoding.ASCII.GetBytes(header + "." + payload);
            var signature = algorithm == "RS256"
                ? (signer ?? _rsa).SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                : (ecSigner ?? _ec).SignData(input, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return header + "." + payload + "." + Encode(signature);
        }

        private string Jwks()
        {
            var rsa = _rsa.ExportParameters(false);
            var ec = _ec.ExportParameters(false);
            return JsonSerializer.Serialize(new
            {
                keys = new object[]
                {
                    new { kty = "RSA", use = "sig", alg = "RS256", kid = "rsa",
                        n = Encode(rsa.Modulus!), e = Encode(rsa.Exponent!) },
                    new { kty = "EC", use = "sig", alg = "ES256", crv = "P-256", kid = "ec",
                        x = Encode(ec.Q.X!), y = Encode(ec.Q.Y!) }
                }
            });
        }

        private static Guid? SubjectFromBearer(HttpRequestMessage request)
        {
            var token = request.Headers.Authorization?.Parameter;
            var parts = token?.Split('.');
            if (parts?.Length != 3) return null;
            var value = parts[1].Replace('-', '+').Replace('_', '/');
            value += new string('=', (4 - value.Length % 4) % 4);
            using var document = JsonDocument.Parse(Convert.FromBase64String(value));
            return document.RootElement.TryGetProperty("sub", out var subject) &&
                Guid.TryParse(subject.GetString(), out var parsed) ? parsed : null;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { _rsa.Dispose(); _ec.Dispose(); }
        }
        private static HttpClient Client(Func<HttpRequestMessage, CancellationToken,
            Task<HttpResponseMessage>> send) => new(new Handler(send));
        private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
        private static HttpResponseMessage JsonResponse(HttpStatusCode status, string value) =>
            new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
        private sealed class Handler(Func<HttpRequestMessage, CancellationToken,
            Task<HttpResponseMessage>> send) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken) => send(request, cancellationToken);
        }
        private sealed class CaptureLoggerProvider : ILoggerProvider
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();
            public IReadOnlyList<string> Messages => _messages.ToArray();
            public ILogger CreateLogger(string categoryName) =>
                new CaptureLogger(categoryName, _messages);
            public void Dispose() { }
            private sealed class CaptureLogger(
                string category,
                System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                    Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    var structured = state is IEnumerable<KeyValuePair<string, object?>> values
                        ? string.Join("|", values.Select(static value =>
                            $"{value.Key}={value.Value}"))
                        : state?.ToString();
                    messages.Enqueue(
                        $"{category}|{logLevel}|{eventId.Id}|{formatter(state, exception)}|" +
                        $"{structured}|{exception}");
                }
            }
        }
    }
}
