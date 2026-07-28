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
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);
    private readonly HttpClient _http;
    private readonly SupabaseApiOptions _options;
    private readonly ReadinessState _state;
    private readonly TimeProvider _time;
    private readonly object _publicationSync = new();
    private long _issuedGeneration;
    private long _publishedGeneration;

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
        var generation = Interlocked.Increment(ref _issuedGeneration);
        using var transportCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var deadlineCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var transport = SendAsync(transportCancellation.Token);
        var deadline = Task.Delay(Deadline, _time, deadlineCancellation.Token);
        var winner = await Task.WhenAny(transport, deadline).ConfigureAwait(false);
        if (winner != transport)
        {
            transportCancellation.Cancel();
            _ = ObserveLateTransportAsync(transport);
            cancellationToken.ThrowIfCancellationRequested();
            Publish(generation, false);
            return false;
        }

        deadlineCancellation.Cancel();
        bool ready;
        try
        {
            ready = await transport.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { ready = false; }
        catch (HttpRequestException) { ready = false; }
        catch (JsonException) { ready = false; }
        catch (InvalidDataException) { ready = false; }
        catch (IOException) { ready = false; }
        catch (NotSupportedException) { ready = false; }
        Publish(generation, ready);
        return ready;
    }

    private async Task<bool> SendAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(_options.Origin, "/rest/v1/rpc/spotify_booster_health_v1"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.ConnectionClose = true;
        request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
            return false;
        var bytes = await BoundedContent.ReadAsync(response.Content, 256, cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 3 &&
            root.TryGetProperty("service", out var service) &&
            service.ValueKind == JsonValueKind.String &&
            service.GetString() == "spotify-booster-data" &&
            root.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.String && status.GetString() == "ok" &&
            root.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.Number &&
            version.TryGetInt32(out var value) && value == 1;
    }

    private static async Task ObserveLateTransportAsync(Task<bool> transport)
    {
        try { await transport.ConfigureAwait(false); }
        catch { }
    }

    private void Publish(long generation, bool ready)
    {
        lock (_publicationSync)
        {
            if (generation <= _publishedGeneration)
                return;
            _publishedGeneration = generation;
            _state.ObserveDataApi(ready);
        }
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
