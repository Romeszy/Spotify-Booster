using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
}
