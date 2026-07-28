using Microsoft.AspNetCore.HttpOverrides;
using SpotifyBooster.Api;

var builder = WebApplication.CreateBuilder(args);
var section = builder.Configuration.GetSection("Supabase");
var origin = section["Origin"] ?? "https://127.0.0.1:54321/";
var options = new SupabaseApiOptions
{
    Origin = new Uri(origin, UriKind.Absolute),
    PublishableKey = section["PublishableKey"] ?? "publishable-test-placeholder",
    Issuer = section["Issuer"] ?? origin.TrimEnd('/') + "/auth/v1",
    Audience = section["Audience"] ?? "authenticated",
    JwksPath = section["JwksPath"] ?? "/auth/v1/.well-known/jwks.json"
};
options.Validate();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.AddSingleton<FixedWindowLimiter>();
builder.Services.AddSingleton<ReadinessState>();
builder.Services.AddSingleton(CreateStrictHttpClient());
builder.Services.AddSingleton<JwksCoordinator>();
builder.Services.AddSingleton<SupabaseJwtValidator>();
builder.Services.AddSingleton<DataApiClient>();
builder.Services.AddSingleton<DataApiReadinessProbe>();

var app = builder.Build();
app.UseMiddleware<SupabaseAuthenticationMiddleware>();
app.UseMiddleware<ApiRateLimitingMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (JwksCoordinator jwks, ReadinessState readiness) =>
    jwks.IsReady && readiness.IsDataApiReady
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "unready" }, statusCode: StatusCodes.Status503ServiceUnavailable));
app.MapGet("/v1/me", async (HttpContext context, DataApiClient dataApi, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var subject))
        return Results.Json(new { type = "about:blank", title = "Unauthorized", status = 401, code = "unauthorized",
            traceId = context.TraceIdentifier }, statusCode: StatusCodes.Status401Unauthorized,
            contentType: "application/problem+json");
    var header = context.Request.Headers.Authorization.ToString();
    try
    {
        return Results.Ok(await dataApi.GetMeAsync(subject, header[7..].Trim(), cancellationToken).ConfigureAwait(false));
    }
    catch (DataApiException)
    {
        return Results.Json(new { type = "about:blank", title = "Upstream unavailable", status = 503,
            code = "dependency_unavailable", traceId = context.TraceIdentifier },
            statusCode: StatusCodes.Status503ServiceUnavailable, contentType: "application/problem+json");
    }
});

app.Run();

static HttpClient CreateStrictHttpClient()
{
    var handler = new HttpClientHandler { AllowAutoRedirect = false };
    return new HttpClient(handler, disposeHandler: true);
}

public partial class Program;
