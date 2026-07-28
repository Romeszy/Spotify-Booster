using System.Net;

namespace SpotifyBooster.Api;

public sealed class FixedWindowLimiter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public FixedWindowLimiter(TimeProvider time) => _time = time;

    public RateLimitDecision Acquire(string partition, int permitLimit)
    {
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            if (!_entries.TryGetValue(partition, out var entry) ||
                _time.GetElapsedTime(entry.StartedAt, now) >= TimeSpan.FromSeconds(60))
                entry = new(now, 0);
            if (entry.Count >= permitLimit)
            {
                var remaining = TimeSpan.FromSeconds(60) - _time.GetElapsedTime(entry.StartedAt, now);
                var retry = Math.Clamp((int)Math.Ceiling(remaining.TotalSeconds), 1, 60);
                return new(false, retry);
            }
            _entries[partition] = entry with { Count = entry.Count + 1 };
            return new(true, 0);
        }
    }

    private sealed record Entry(long StartedAt, int Count);
}

public sealed record RateLimitDecision(bool Allowed, int RetryAfterSeconds);

public sealed class ApiRateLimitingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, FixedWindowLimiter limiter, ApiMetrics metrics)
    {
        if (!context.Request.Path.StartsWithSegments("/v1"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        var subject = context.User.FindFirst("sub")?.Value;
        var authenticated = Guid.TryParse(subject, out var id);
        var partition = authenticated
            ? "sub:" + id
            : "ip:" + Normalize(context.Connection.RemoteIpAddress);
        var decision = limiter.Acquire(partition, authenticated ? 30 : 10);
        if (decision.Allowed)
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        metrics.RateLimitRejections.Add(1, new KeyValuePair<string, object?>[] { new("policy", "authenticated_api") });
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ProblemWriter.WriteAsync(context, StatusCodes.Status429TooManyRequests, "rate_limit", "Request limit exceeded.")
            .ConfigureAwait(false);
    }

    private static string Normalize(IPAddress? address) =>
        address is null ? "unknown" :
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
}

internal static class ProblemWriter
{
    public static Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title,
            status,
            code,
            traceId = context.TraceIdentifier
        }, context.RequestAborted);
    }
}
