using System.Security.Cryptography;
using System.Text.Json;

namespace SpotifyBooster.Api;

public enum JwksTrigger { ColdStart, Expiry, Retry, UnknownKid, AdminPurge }

public sealed record JwkKey(string Kid, string Algorithm, RSA? Rsa, ECDsa? Ec) : IDisposable
{
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) =>
        Algorithm switch
        {
            "RS256" => Rsa!.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "ES256" => Ec!.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => false
        };
    public void Dispose() { Rsa?.Dispose(); Ec?.Dispose(); }
}

public sealed class JwksCoordinator : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Gate = TimeSpan.FromMinutes(1);
    private readonly object _sync = new();
    private readonly HttpClient _http;
    private readonly SupabaseApiOptions _options;
    private readonly TimeProvider _time;
    private readonly ApiMetrics _metrics;
    private Snapshot? _snapshot;
    private Task<Snapshot?>? _inFlight;
    private long _generation = 1;
    private long _epoch;
    private bool _attemptedGeneration;
    private long _retryNotBefore;
    private long _unknownNotBefore;

    public JwksCoordinator(HttpClient http, SupabaseApiOptions options, TimeProvider time, ApiMetrics metrics)
    {
        _http = http; _options = options; _time = time; _metrics = metrics;
        options.Validate();
        if (_http.DefaultRequestHeaders.Authorization is not null)
            throw new InvalidOperationException("Identity headers must not be configured globally.");
    }

    public bool IsReady
    {
        get { lock (_sync) return IsFreshLocked(); }
    }

    public async Task<JwkKey?> FindAsync(string kid, CancellationToken cancellationToken)
    {
        Task<Snapshot?> fetch;
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            if (_snapshot is not null && !IsFreshLocked())
            {
                DisposeSnapshot(_snapshot);
                _snapshot = null;
                _generation = checked(_generation + 1);
                _attemptedGeneration = false;
            }

            if (_snapshot?.Keys.TryGetValue(kid, out var cached) == true)
                return cached;

            if (_inFlight is not null)
            {
                _metrics.JwksDecisions.Add(1, new("event", "joined_inflight"));
                fetch = _inFlight;
            }
            else
            {
                var mandatory = !_attemptedGeneration;
                if (!mandatory && now < _retryNotBefore)
                {
                    _metrics.JwksDecisions.Add(1, new("event", "retry_gate_rejected"));
                    return null;
                }
                if (!mandatory && now < _unknownNotBefore)
                {
                    _metrics.JwksDecisions.Add(1, new("event", "unknown_cooldown_rejected"));
                    return null;
                }
                var trigger = mandatory
                    ? (_snapshot is null && _generation == 1 ? JwksTrigger.ColdStart : JwksTrigger.Expiry)
                    : (_snapshot is null ? JwksTrigger.Retry : JwksTrigger.UnknownKid);
                _attemptedGeneration = true;
                _unknownNotBefore = Add(now, Gate);
                fetch = _inFlight = FetchAsync(_epoch, _generation, trigger);
                _metrics.JwksDecisions.Add(1, new("event", "leader_created"));
            }
        }

        await fetch.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (_snapshot?.Keys.TryGetValue(kid, out var key) == true)
            {
                _metrics.JwksDecisions.Add(1, new("event", "validation_succeeded_after_refresh"));
                return key;
            }
            _metrics.JwksDecisions.Add(1, new("event", "unknown_after_refresh"));
            return null;
        }
    }

    public Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        Task<Snapshot?> task;
        lock (_sync)
        {
            if (_inFlight is not null) return _inFlight.WaitAsync(cancellationToken);
            _epoch = checked(_epoch + 1);
            _generation = checked(_generation + 1);
            if (_snapshot is not null) DisposeSnapshot(_snapshot);
            _snapshot = null;
            _attemptedGeneration = true;
            _unknownNotBefore = Add(_time.GetTimestamp(), Gate);
            task = _inFlight = FetchAsync(_epoch, _generation, JwksTrigger.AdminPurge);
        }
        return task.WaitAsync(cancellationToken);
    }

    private async Task<Snapshot?> FetchAsync(long epoch, long generation, JwksTrigger trigger)
    {
        var result = "invalid_document";
        Snapshot? created = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.Origin, _options.JwksPath));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), _time);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) { result = "http_error"; return null; }
            if (!response.IsSuccessStatusCode) { result = "http_error"; return null; }
            var bytes = await BoundedContent.ReadAsync(response.Content, 256 * 1024, timeout.Token).ConfigureAwait(false);
            var keys = Parse(bytes);
            if (keys.Count == 0) return null;
            var now = _time.GetTimestamp();
            created = new(epoch, generation, keys, now, Add(now, Lifetime));
            result = "success";
            return created;
        }
        catch (OperationCanceledException) { result = "timeout"; return null; }
        catch (HttpRequestException) { result = "http_error"; return null; }
        catch (JsonException) { return null; }
        catch (InvalidDataException) { return null; }
        finally
        {
            lock (_sync)
            {
                if (epoch == _epoch && generation == _generation && ReferenceEquals(_inFlight, Task.CurrentId is null ? _inFlight : _inFlight))
                {
                    if (created is not null)
                    {
                        if (_snapshot is not null) DisposeSnapshot(_snapshot);
                        _snapshot = created;
                        _retryNotBefore = 0;
                    }
                    else _retryNotBefore = Add(_time.GetTimestamp(), Gate);
                    _inFlight = null;
                }
                else if (created is not null)
                {
                    DisposeSnapshot(created);
                    created = null;
                    result = "invalidated";
                    _metrics.JwksDecisions.Add(1, new("event", "stale_completion_discarded"));
                }
            }
            _metrics.JwksRefresh.Add(1,
                new("trigger", TriggerName(trigger)), new("result", result));
        }
    }

    private Dictionary<string, JwkKey> Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException();
        var result = new Dictionary<string, JwkKey>(StringComparer.Ordinal);
        foreach (var item in keys.EnumerateArray())
        {
            var kid = Required(item, "kid");
            var alg = Required(item, "alg");
            if (alg == "RS256" && Required(item, "kty") == "RSA")
            {
                var rsa = RSA.Create();
                rsa.ImportParameters(new() { Modulus = Decode(Required(item, "n")), Exponent = Decode(Required(item, "e")) });
                if (!result.TryAdd(kid, new(kid, alg, rsa, null))) rsa.Dispose();
            }
            else if (alg == "ES256" && Required(item, "kty") == "EC" && Required(item, "crv") == "P-256")
            {
                var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256,
                    Q = new() { X = Decode(Required(item, "x")), Y = Decode(Required(item, "y")) } });
                if (!result.TryAdd(kid, new(kid, alg, null, ec))) ec.Dispose();
            }
        }
        return result;
    }

    private bool IsFreshLocked() => _snapshot is not null && _time.GetTimestamp() < _snapshot.ExpiresAt;
    private long Add(long timestamp, TimeSpan duration) => timestamp + (long)(duration.TotalSeconds * _time.TimestampFrequency);
    private static string Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new InvalidDataException();
    private static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
    private static string TriggerName(JwksTrigger trigger) => trigger switch
    {
        JwksTrigger.ColdStart => "cold_start", JwksTrigger.Expiry => "expiry",
        JwksTrigger.Retry => "retry", JwksTrigger.UnknownKid => "unknown_kid",
        _ => "admin_purge"
    };
    private static void DisposeSnapshot(Snapshot snapshot) { foreach (var key in snapshot.Keys.Values) key.Dispose(); }
    public void Dispose() { lock (_sync) { if (_snapshot is not null) DisposeSnapshot(_snapshot); _snapshot = null; } }
    private sealed record Snapshot(long Epoch, long Generation, Dictionary<string, JwkKey> Keys, long LoadedAt, long ExpiresAt);
}
