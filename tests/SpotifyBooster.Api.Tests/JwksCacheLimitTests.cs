using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpotifyBooster.Api.Tests;

public sealed class JwksCacheLimitTests
{
    [Fact]
    public async Task OversizedRotationCannotEvictFreshValidatedKeys()
    {
        using var rsa = RSA.Create(2048);
        var calls = 0;
        var oversizedKids = Enumerable.Range(0, JwksCoordinator.MaximumCachedKeys + 1)
            .Select(index => $"rotated-{index}")
            .ToArray();
        using var client = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json(Interlocked.Increment(ref calls) == 1
                ? Jwks(rsa, ["current"])
                : Jwks(rsa, oversizedKids)))));
        using var metrics = new ApiMetrics();
        var time = new AdjustableTimeProvider();
        using var coordinator = new JwksCoordinator(client, Options(),
            time, metrics);

        using var current = await coordinator.FindAsync("current",
            TestContext.Current.CancellationToken);
        Assert.NotNull(current);
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(await coordinator.FindAsync(oversizedKids[0],
            TestContext.Current.CancellationToken));
        using var retained = await coordinator.FindAsync("current",
            TestContext.Current.CancellationToken);
        Assert.NotNull(retained);
        Assert.True(coordinator.IsReady);
        Assert.Equal(2, calls);
    }

    private static SupabaseApiOptions Options() => new()
    {
        Origin = new("https://127.0.0.1:54321/"),
        PublishableKey = "sb_publishable_test_placeholder",
        Issuer = "https://127.0.0.1:54321/auth/v1",
        Audience = "authenticated"
    };

    private static string Jwks(RSA rsa, IEnumerable<string> kids)
    {
        var parameters = rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = kids.Select(kid => new
            {
                kty = "RSA",
                use = "sig",
                alg = "RS256",
                kid,
                n = Encode(parameters.Modulus!),
                e = Encode(parameters.Exponent!)
            })
        });
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Volatile.Read(ref _timestamp);
        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _timestamp, duration.Ticks);
    }
}
