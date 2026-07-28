using System.Diagnostics.Metrics;

namespace SpotifyBooster.Api;

public sealed class ApiMetrics : IDisposable
{
    private readonly Meter _meter = new("SpotifyBooster.Api", "1.0");
    public Counter<long> JwksRefresh { get; }
    public Counter<long> JwksDecisions { get; }
    public Counter<long> JwtRejections { get; }
    public Counter<long> DataApiRequests { get; }
    public Histogram<double> DataApiDuration { get; }
    public Counter<long> RateLimitRejections { get; }
    public Counter<long> ReadinessTransitions { get; }

    public ApiMetrics()
    {
        JwksRefresh = _meter.CreateCounter<long>("jwks_refresh_total");
        JwksDecisions = _meter.CreateCounter<long>("jwks_decision_events_total");
        JwtRejections = _meter.CreateCounter<long>("jwt_rejections_total");
        DataApiRequests = _meter.CreateCounter<long>("data_api_requests_total");
        DataApiDuration = _meter.CreateHistogram<double>("data_api_duration_seconds");
        RateLimitRejections = _meter.CreateCounter<long>("rate_limit_rejections_total");
        ReadinessTransitions = _meter.CreateCounter<long>("readiness_transitions_total");
    }

    public void Dispose() => _meter.Dispose();
}
