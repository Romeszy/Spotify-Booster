using Microsoft.AspNetCore.Authentication;
using SpotifyBooster.Api;

var builder = WebApplication.CreateBuilder(args);
var section = builder.Configuration.GetSection("Supabase");
var localDefaultsAllowed = builder.Environment.IsDevelopment() ||
    builder.Environment.IsEnvironment("Testing");
string RequiredSetting(string name, string localDefault)
{
    string? value = section[name];
    if (!string.IsNullOrWhiteSpace(value))
        return value;
    if (localDefaultsAllowed)
        return localDefault;
    throw new InvalidOperationException("Supabase configuration is required.");
}

var origin = RequiredSetting("Origin", "https://127.0.0.1:54321/");
var options = new SupabaseApiOptions
{
    Origin = new Uri(origin, UriKind.Absolute),
    PublishableKey = RequiredSetting(
        "PublishableKey",
        "sb_publishable_test_placeholder"),
    Issuer = RequiredSetting(
        "Issuer",
        origin.TrimEnd('/') + "/auth/v1"),
    Audience = RequiredSetting("Audience", "authenticated"),
    JwksPath = RequiredSetting(
        "JwksPath",
        "/auth/v1/.well-known/jwks.json"),
};
options.Validate();

builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.None);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.AddSingleton<IngressRateLimiter>();
builder.Services.AddSingleton<FixedWindowLimiter>();
builder.Services.AddSingleton<ReadinessState>();
builder.Services.ConfigureHttpClientDefaults(http => http.RemoveAllLoggers());
builder.Services.AddHttpClient(Program.JwksHttpClientName,
        client => client.BaseAddress = options.Origin)
    .ConfigurePrimaryHttpMessageHandler(CreateStrictHandler);
builder.Services.AddSingleton(provider => new JwksCoordinator(
    provider.GetRequiredService<IHttpClientFactory>()
        .CreateClient(Program.JwksHttpClientName),
    provider.GetRequiredService<SupabaseApiOptions>(),
    provider.GetRequiredService<TimeProvider>(),
    provider.GetRequiredService<ApiMetrics>(),
    provider.GetRequiredService<ILogger<JwksCoordinator>>()));
builder.Services.AddHostedService<JwksRefreshHostedService>();
builder.Services.AddSingleton<SupabaseJwtValidator>();
builder.Services.AddAuthentication(SupabaseAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SupabaseAuthenticationHandler>(
        SupabaseAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient<DataApiClient>(client => client.BaseAddress = options.Origin)
    .ConfigurePrimaryHttpMessageHandler(CreateStrictHandler);
builder.Services.AddHttpClient<DataApiReadinessProbe>(client => client.BaseAddress = options.Origin)
    .ConfigurePrimaryHttpMessageHandler(DataApiReadinessTransportPolicy.CreateHandler)
    .SetHandlerLifetime(DataApiReadinessTransportPolicy.HandlerLifetime);
builder.Services.AddHostedService<DataApiReadinessHostedService>();

var app = builder.Build();
var unhandledLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("SpotifyBooster.Api.UnhandledException");
app.Use(async (context, next) =>
{
    try { await next(context).ConfigureAwait(false); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception)
    {
        if (context.Response.HasStarted)
            throw;
        unhandledLogger.LogError(new EventId(2103, "UnhandledRequestFailure"),
            "Unhandled API request failure.");
        await ProblemWriter.WriteAsync(context, StatusCodes.Status500InternalServerError,
            "internal_error", "Unexpected server error.").ConfigureAwait(false);
    }
});
app.UseMiddleware<IngressRateLimitingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ApiRateLimitingMiddleware>();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (JwksCoordinator jwks, ReadinessState readiness) =>
    jwks.IsReady && readiness.IsDataApiReady
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "unready" }, statusCode: StatusCodes.Status503ServiceUnavailable));
var v1 = app.MapGroup("/v1").RequireAuthorization();
v1.MapGet("/me", async (HttpContext context, DataApiClient dataApi, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var subject))
        return Results.Json(new
        {
            type = "about:blank",
            title = "Unauthorized",
            status = 401,
            code = "unauthorized",
            traceId = context.TraceIdentifier
        }, statusCode: StatusCodes.Status401Unauthorized,
            contentType: "application/problem+json");
    var header = context.Request.Headers.Authorization.ToString();
    try
    {
        return Results.Ok(await dataApi.GetMeAsync(subject, header[7..].Trim(), cancellationToken).ConfigureAwait(false));
    }
    catch (DataApiException)
    {
        return Results.Json(new
        {
            type = "about:blank",
            title = "Upstream unavailable",
            status = 503,
            code = "dependency_unavailable",
            traceId = context.TraceIdentifier
        },
            statusCode: StatusCodes.Status503ServiceUnavailable, contentType: "application/problem+json");
    }
});

app.Run();

static HttpMessageHandler CreateStrictHandler() =>
    new HttpClientHandler { AllowAutoRedirect = false };

public partial class Program
{
    public const string JwksHttpClientName = "supabase-jwks";
}
