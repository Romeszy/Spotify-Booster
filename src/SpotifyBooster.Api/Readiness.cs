using System.Net.Http.Headers;
using System.Text.Json;

namespace SpotifyBooster.Api;

public sealed class ReadinessState
{
    private readonly object _sync = new();
    private readonly TimeProvider _time;
    private readonly ApiMetrics _metrics;
    private bool? _dataReady;
    private long _dataObservedAt;

    public ReadinessState(TimeProvider time, ApiMetrics metrics) { _time = time; _metrics = metrics; }

    public void ObserveDataApi(bool ready)
    {
        lock (_sync)
        {
            if (_dataReady is not null && _dataReady != ready)
                _metrics.ReadinessTransitions.Add(1, new("dependency", "data_api"), new("to", ready ? "ready" : "unready"));
            _dataReady = ready;
            _dataObservedAt = _time.GetTimestamp();
        }
    }

    public bool IsDataApiReady
    {
        get
        {
            lock (_sync)
                return _dataReady == true &&
                    _time.GetElapsedTime(_dataObservedAt, _time.GetTimestamp()) <= TimeSpan.FromSeconds(60);
        }
    }
}

public sealed class DataApiReadinessProbe
{
    private readonly HttpClient _http;
    private readonly SupabaseApiOptions _options;
    private readonly ReadinessState _state;
    private readonly TimeProvider _time;

    public DataApiReadinessProbe(HttpClient http, SupabaseApiOptions options, ReadinessState state, TimeProvider time)
    { _http = http; _options = options; _state = state; _time = time; }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        var ready = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(_options.Origin, "/rest/v1/rpc/spotify_booster_health_v1"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2), _time);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && (int)response.StatusCode is not (>= 300 and < 400))
            {
                var bytes = await BoundedContent.ReadAsync(response.Content, 256, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                ready = root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 3 &&
                    root.TryGetProperty("service", out var service) && service.GetString() == "spotify-booster-data" &&
                    root.TryGetProperty("status", out var status) && status.GetString() == "ok" &&
                    root.TryGetProperty("version", out var version) && version.TryGetInt32(out var value) && value == 1;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) { ready = false; }
        _state.ObserveDataApi(ready);
        return ready;
    }
}
