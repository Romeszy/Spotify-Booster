using System.Diagnostics.Metrics;

namespace SpotifyBooster.Api;

public sealed class ApiMetrics : IDisposable
{
    private readonly Meter _meter = new("SpotifyBooster.Api", "1.0");
    private Func<double> _retryCooldown = static () => 0;
    private Func<double?> _cacheAge = static () => null;
    private Func<int> _jwksReady = static () => 0;
    private Func<int> _dataReady = static () => 0;

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
        _meter.CreateObservableGauge("jwks_retry_cooldown_remaining_seconds",
            () => Math.Clamp(_retryCooldown(), 0, 60));
        _meter.CreateObservableGauge("jwks_cache_age_seconds", ObserveCacheAge);
        _meter.CreateObservableGauge("readiness_state", ObserveReadiness);
    }

    public void SetJwksObservers(Func<double> retryCooldown, Func<double?> cacheAge, Func<int> ready)
    {
        _retryCooldown = retryCooldown;
        _cacheAge = cacheAge;
        _jwksReady = ready;
    }

    public void SetDataApiReadinessObserver(Func<int> ready) => _dataReady = ready;

    private IEnumerable<Measurement<double>> ObserveCacheAge()
    {
        var value = _cacheAge();
        if (value is not null)
            yield return new(Math.Max(0, value.Value));
    }

    private IEnumerable<Measurement<int>> ObserveReadiness()
    {
        yield return new(_jwksReady(),
            new KeyValuePair<string, object?>[] { new("dependency", "jwks") });
        yield return new(_dataReady(),
            new KeyValuePair<string, object?>[] { new("dependency", "data_api") });
    }

    public void Dispose() => _meter.Dispose();
}
