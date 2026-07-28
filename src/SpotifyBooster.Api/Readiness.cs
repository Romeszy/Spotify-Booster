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

    public ReadinessState(TimeProvider time, ApiMetrics metrics)
    {
        _time = time;
        _metrics = metrics;
        _metrics.SetDataApiReadinessObserver(() => IsDataApiReady ? 1 : 0);
    }

    public void ObserveDataApi(bool ready)
    {
        lock (_sync)
        {
            if (_dataReady is not null && _dataReady != ready)
                _metrics.ReadinessTransitions.Add(1,
                    new("dependency", "data_api"), new("to", ready ? "ready" : "unready"));
            _dataReady = ready;
            _dataObservedAt = _time.GetTimestamp();
        }
    }

    public bool IsDataApiReady
    {
        get
        {
            lock (_sync)
            {
                if (_dataReady == true &&
                    _time.GetElapsedTime(_dataObservedAt, _time.GetTimestamp()) >= TimeSpan.FromSeconds(60))
                {
                    _dataReady = false;
                    _metrics.ReadinessTransitions.Add(1,
                        new("dependency", "data_api"), new("to", "unready"));
                }
                return _dataReady == true;
            }
        }
    }
}

public sealed class DataApiReadinessProbe
{
    private readonly HttpClient _http;
    private readonly SupabaseApiOptions _options;
    private readonly ReadinessState _state;
    private readonly TimeProvider _time;

    public DataApiReadinessProbe(HttpClient http, SupabaseApiOptions options,
        ReadinessState state, TimeProvider time)
    {
        _http = http;
        _options = options;
        _state = state;
        _time = time;
        if (_http.DefaultRequestHeaders.Authorization is not null)
            throw new InvalidOperationException("Readiness probes cannot carry shared identity.");
    }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        var ready = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(_options.Origin, "/rest/v1/rpc/spotify_booster_health_v1"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.ConnectionClose = true;
            request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2), _time);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var bytes = await BoundedContent.ReadAsync(response.Content, 256, timeout.Token)
                    .ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                ready = root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 3 &&
                    root.TryGetProperty("service", out var service) &&
                    service.ValueKind == JsonValueKind.String &&
                    service.GetString() == "spotify-booster-data" &&
                    root.TryGetProperty("status", out var status) &&
                    status.ValueKind == JsonValueKind.String && status.GetString() == "ok" &&
                    root.TryGetProperty("version", out var version) &&
                    version.ValueKind == JsonValueKind.Number &&
                    version.TryGetInt32(out var value) && value == 1;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { ready = false; }
        catch (HttpRequestException) { ready = false; }
        catch (JsonException) { ready = false; }
        catch (InvalidDataException) { ready = false; }
        catch (IOException) { ready = false; }
        catch (NotSupportedException) { ready = false; }
        _state.ObserveDataApi(ready);
        return ready;
    }
}

public sealed class DataApiReadinessHostedService(
    DataApiReadinessProbe probe,
    TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProbeIgnoringShutdownAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await ProbeIgnoringShutdownAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task ProbeIgnoringShutdownAsync(CancellationToken cancellationToken)
    {
        try { await probe.ProbeAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
