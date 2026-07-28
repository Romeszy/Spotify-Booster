using System.Security.Cryptography;
using System.Text.Json;

namespace SpotifyBooster.Api;

public enum JwksTrigger { ColdStart, Expiry, Retry, UnknownKid, AdminPurge }

internal sealed class JwkMaterial(string kid, string algorithm, RSA? rsa, ECDsa? ec)
{
    private int _references = 1;
    public string Kid { get; } = kid;
    public string Algorithm { get; } = algorithm;

    public JwkKey Lease()
    {
        while (true)
        {
            var current = Volatile.Read(ref _references);
            ObjectDisposedException.ThrowIf(current == 0, this);
            if (Interlocked.CompareExchange(ref _references, checked(current + 1), current) == current)
                return new(this);
        }
    }

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) =>
        Algorithm switch
        {
            "RS256" => rsa!.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "ES256" => ec!.VerifyData(data, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => false
        };

    public void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0)
        {
            rsa?.Dispose();
            ec?.Dispose();
        }
    }
}

public sealed class JwkKey : IDisposable
{
    private JwkMaterial? _material;
    internal JwkKey(JwkMaterial material) => _material = material;
    public string Kid => Material.Kid;
    public string Algorithm => Material.Algorithm;
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) =>
        Material.Verify(data, signature);
    public void Dispose() => Interlocked.Exchange(ref _material, null)?.Release();
    private JwkMaterial Material => Volatile.Read(ref _material) ??
        throw new ObjectDisposedException(nameof(JwkKey));
}

public sealed class JwksCoordinator : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Gate = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly HttpClient _http;
    private readonly SupabaseApiOptions _options;
    private readonly TimeProvider _time;
    private readonly ApiMetrics _metrics;
    private Snapshot? _snapshot;
    private Attempt? _inFlight;
    private ulong _attemptSequence;
    private ulong _generation = 1;
    private ulong _epoch;
    private bool _attemptedGeneration;
    private bool? _readiness;
    private long _retryNotBefore;
    private long _unknownNotBefore;
    private bool _disposed;

    public JwksCoordinator(HttpClient http, SupabaseApiOptions options, TimeProvider time, ApiMetrics metrics)
    {
        _http = http;
        _options = options;
        _time = time;
        _metrics = metrics;
        options.Validate();
        if (_http.DefaultRequestHeaders.Authorization is not null)
            throw new InvalidOperationException("Identity headers must not be configured globally.");
        _metrics.SetJwksObservers(GetRetryCooldownSeconds, GetCacheAgeSeconds, () => IsReady ? 1 : 0);
    }

    public bool IsReady
    {
        get
        {
            lock (_sync)
            {
                var now = _time.GetTimestamp();
                ExpireSnapshotLocked(now);
                return IsFreshLocked(now);
            }
        }
    }

    public async Task<JwkKey?> FindAsync(string kid, CancellationToken cancellationToken)
    {
        Attempt attempt;
        lock (_sync)
        {
            ThrowIfDisposed();
            var now = _time.GetTimestamp();
            var attemptCreatedByObserver = ExpireSnapshotLocked(now);
            if (_snapshot?.Keys.TryGetValue(kid, out var cached) == true)
                return cached.Lease();

            if (_inFlight is not null)
            {
                if (!ReferenceEquals(attemptCreatedByObserver, _inFlight))
                    Decision("joined_inflight");
                attempt = _inFlight;
            }
            else
            {
                var mandatory = !_attemptedGeneration;
                if (!mandatory && now < _retryNotBefore)
                {
                    Decision("retry_gate_rejected");
                    return null;
                }
                if (!mandatory && now < _unknownNotBefore)
                {
                    Decision("unknown_cooldown_rejected");
                    return null;
                }

                var trigger = mandatory
                    ? (_generation == 1 ? JwksTrigger.ColdStart : JwksTrigger.Expiry)
                    : (_snapshot is null ? JwksTrigger.Retry : JwksTrigger.UnknownKid);
                attempt = CreateAttemptLocked(trigger, now);
            }
        }

        await attempt.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (_snapshot?.Keys.TryGetValue(kid, out var key) == true &&
                IsFreshLocked(_time.GetTimestamp()))
            {
                Decision("validation_succeeded_after_refresh");
                return key.Lease();
            }
            Decision("unknown_after_refresh");
            return null;
        }
    }

    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        Attempt attempt;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_inFlight?.Trigger == JwksTrigger.AdminPurge)
            {
                Decision("joined_inflight");
                attempt = _inFlight;
            }
            else
            {
                InvalidateAttemptLocked("epoch_invalidated");
                _epoch = CheckedIncrement(_epoch);
                _generation = CheckedIncrement(_generation);
                RetireSnapshotLocked();
                SetReadinessLocked(false);
                _attemptedGeneration = true;
                attempt = CreateAttemptLocked(JwksTrigger.AdminPurge, _time.GetTimestamp());
            }
        }
        await attempt.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Attempt CreateAttemptLocked(JwksTrigger trigger, long now)
    {
        _attemptedGeneration = true;
        _unknownNotBefore = Add(now, Gate);
        var attempt = new Attempt(CheckedIncrement(ref _attemptSequence), _epoch, _generation, trigger,
            Add(now, Deadline));
        _inFlight = attempt;
        Decision("leader_created");
        _ = ExecuteAttemptAsync(attempt);
        return attempt;
    }

    private async Task ExecuteAttemptAsync(Attempt attempt)
    {
        await Task.Yield();
        var transport = FetchTransportAsync(attempt.TransportCancellation.Token);
        var deadline = Task.Delay(Deadline, _time, CancellationToken.None);
        var winner = await Task.WhenAny(transport, deadline).ConfigureAwait(false);
        if (winner == deadline)
        {
            TimeoutAttempt(attempt);
            attempt.TransportCancellation.Cancel();
            attempt.TransportCancellation.Dispose();
            _ = ObserveLateTransportAsync(attempt, transport);
            return;
        }

        FetchResult result;
        try { result = await transport.ConfigureAwait(false); }
        catch (OperationCanceledException) { result = FetchResult.Failure("timeout"); }
        catch (HttpRequestException) { result = FetchResult.Failure("http_error"); }
        CompleteAttempt(attempt, result);
        attempt.TransportCancellation.Dispose();
    }

    private void TimeoutAttempt(Attempt attempt)
    {
        lock (_sync)
        {
            if (!IsCurrentAttemptLocked(attempt))
                return;
            _inFlight = null;
            _retryNotBefore = Add(_time.GetTimestamp(), Gate);
            SetReadinessLocked(false);
            RecordRefresh(attempt.Trigger, "timeout");
            attempt.Completion.TrySetResult("timeout");
        }
    }

    private async Task ObserveLateTransportAsync(Attempt attempt, Task<FetchResult> transport)
    {
        FetchResult result;
        try { result = await transport.ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        result.Snapshot?.Dispose();
        Decision("stale_completion_discarded");
    }

    private async Task<FetchResult> FetchTransportAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(_options.Origin, _options.JwksPath));
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 || !response.IsSuccessStatusCode)
                return FetchResult.Failure("http_error");
            var bytes = await BoundedContent.ReadAsync(response.Content, 256 * 1024, cancellationToken)
                .ConfigureAwait(false);
            var keys = Parse(bytes);
            if (keys.Count == 0)
                return FetchResult.Failure("invalid_document");
            var now = _time.GetTimestamp();
            return FetchResult.Success(new(keys, now, Add(now, Lifetime)));
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException) { return FetchResult.Failure("http_error"); }
        catch (JsonException) { return FetchResult.Failure("invalid_document"); }
        catch (InvalidDataException) { return FetchResult.Failure("invalid_document"); }
        catch (IOException) { return FetchResult.Failure("invalid_document"); }
        catch (NotSupportedException) { return FetchResult.Failure("invalid_document"); }
        catch (ObjectDisposedException) { return FetchResult.Failure("invalid_document"); }
        catch (CryptographicException) { return FetchResult.Failure("invalid_document"); }
        catch (FormatException) { return FetchResult.Failure("invalid_document"); }
    }

    private void CompleteAttempt(Attempt attempt, FetchResult result)
    {
        var accepted = false;
        var timedOut = false;
        lock (_sync)
        {
            var current = IsCurrentAttemptLocked(attempt);
            if (current && _time.GetTimestamp() >= attempt.Deadline)
            {
                timedOut = true;
                _inFlight = null;
                _retryNotBefore = Add(_time.GetTimestamp(), Gate);
                SetReadinessLocked(false);
                RecordRefresh(attempt.Trigger, "timeout");
                attempt.Completion.TrySetResult("timeout");
            }
            else if (current)
            {
                accepted = true;
                _inFlight = null;
                if (result.Snapshot is not null)
                {
                    RetireSnapshotLocked();
                    _snapshot = result.Snapshot;
                    _retryNotBefore = 0;
                    SetReadinessLocked(true);
                }
                else
                {
                    _retryNotBefore = Add(_time.GetTimestamp(), Gate);
                    SetReadinessLocked(false);
                }
                RecordRefresh(attempt.Trigger, result.Result);
                attempt.Completion.TrySetResult(result.Result);
            }
        }

        if (!accepted)
        {
            result.Snapshot?.Dispose();
            if (!timedOut && !attempt.Completion.Task.IsCompleted)
                attempt.Completion.TrySetResult("invalidated");
            if (!timedOut)
                Decision("stale_completion_discarded");
        }
    }

    private Attempt? ExpireSnapshotLocked(long now)
    {
        if (_snapshot is null || now < _snapshot.ExpiresAt)
            return null;
        if (_inFlight is not null)
            InvalidateAttemptLocked("generation_invalidated");
        RetireSnapshotLocked();
        SetReadinessLocked(false);
        _generation = CheckedIncrement(_generation);
        _attemptedGeneration = false;
        return CreateAttemptLocked(JwksTrigger.Expiry, now);
    }

    private void InvalidateAttemptLocked(string decision)
    {
        if (_inFlight is null)
            return;
        var stale = _inFlight;
        _inFlight = null;
        ThreadPool.QueueUserWorkItem(static state =>
        {
            try { state.Cancel(); }
            catch (ObjectDisposedException) { }
        }, stale.TransportCancellation, preferLocal: false);
        stale.Completion.TrySetResult("invalidated");
        RecordRefresh(stale.Trigger, "invalidated");
        SetReadinessLocked(false);
        Decision(decision);
    }

    private static Dictionary<string, JwkMaterial> Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("keys", out var keys) ||
            keys.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException();
        var result = new Dictionary<string, JwkMaterial>(StringComparer.Ordinal);
        try
        {
            foreach (var item in keys.EnumerateArray())
            {
                var kid = Required(item, "kid");
                var alg = Required(item, "alg");
                if (item.TryGetProperty("d", out _))
                    throw new InvalidDataException();
                if (Required(item, "use") != "sig")
                    continue;
                if (alg == "RS256" && Required(item, "kty") == "RSA")
                {
                    var rsa = RSA.Create();
                    rsa.ImportParameters(new()
                    {
                        Modulus = Decode(Required(item, "n")),
                        Exponent = Decode(Required(item, "e"))
                    });
                    if (!result.TryAdd(kid, new(kid, alg, rsa, null)))
                    {
                        rsa.Dispose();
                        throw new InvalidDataException();
                    }
                }
                else if (alg == "ES256" && Required(item, "kty") == "EC" &&
                    Required(item, "crv") == "P-256")
                {
                    var x = Decode(Required(item, "x"));
                    var y = Decode(Required(item, "y"));
                    if (x.Length != 32 || y.Length != 32)
                        throw new InvalidDataException();
                    var ec = ECDsa.Create(new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q = new() { X = x, Y = y }
                    });
                    if (!result.TryAdd(kid, new(kid, alg, null, ec)))
                    {
                        ec.Dispose();
                        throw new InvalidDataException();
                    }
                }
            }
            return result;
        }
        catch
        {
            foreach (var key in result.Values)
                key.Release();
            throw;
        }
    }

    private bool IsFreshLocked(long now) => _snapshot is not null && now < _snapshot.ExpiresAt;
    private bool IsCurrentAttemptLocked(Attempt attempt) =>
        _inFlight is not null && _inFlight.Id == attempt.Id &&
        attempt.Epoch == _epoch && attempt.Generation == _generation;
    private double GetRetryCooldownSeconds()
    {
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            return now >= _retryNotBefore ? 0 :
                _time.GetElapsedTime(now, _retryNotBefore).TotalSeconds;
        }
    }
    private double? GetCacheAgeSeconds()
    {
        lock (_sync)
        {
            var now = _time.GetTimestamp();
            return IsFreshLocked(now) ? _time.GetElapsedTime(_snapshot!.LoadedAt, now).TotalSeconds : null;
        }
    }
    private long Add(long timestamp, TimeSpan duration) =>
        checked(timestamp + (long)(duration.TotalSeconds * _time.TimestampFrequency));
    private static ulong CheckedIncrement(ulong value) =>
        value == ulong.MaxValue ? throw new OverflowException("JWKS state exhausted.") : value + 1;
    private static ulong CheckedIncrement(ref ulong value)
    {
        value = CheckedIncrement(value);
        return value;
    }
    private static string Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new InvalidDataException();
    private static byte[] Decode(string value)
    {
        if (value.Contains('='))
            throw new InvalidDataException();
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
    private static string TriggerName(JwksTrigger trigger) => trigger switch
    {
        JwksTrigger.ColdStart => "cold_start",
        JwksTrigger.Expiry => "expiry",
        JwksTrigger.Retry => "retry",
        JwksTrigger.UnknownKid => "unknown_kid",
        JwksTrigger.AdminPurge => "admin_purge",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger))
    };
    private void Decision(string value) => _metrics.JwksDecisions.Add(1,
        new KeyValuePair<string, object?>[] { new("event", value) });
    private void RecordRefresh(JwksTrigger trigger, string result) =>
        _metrics.JwksRefresh.Add(1, new("trigger", TriggerName(trigger)), new("result", result));
    private void RetireSnapshotLocked()
    {
        _snapshot?.Dispose();
        _snapshot = null;
    }
    private void SetReadinessLocked(bool ready)
    {
        if (_readiness is not null && _readiness != ready)
            _metrics.ReadinessTransitions.Add(1,
                new("dependency", "jwks"), new("to", ready ? "ready" : "unready"));
        _readiness = ready;
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            InvalidateAttemptLocked("epoch_invalidated");
            RetireSnapshotLocked();
        }
    }

    private sealed class Snapshot(Dictionary<string, JwkMaterial> keys, long loadedAt, long expiresAt) : IDisposable
    {
        public Dictionary<string, JwkMaterial> Keys { get; } = keys;
        public long LoadedAt { get; } = loadedAt;
        public long ExpiresAt { get; } = expiresAt;
        public void Dispose()
        {
            foreach (var key in Keys.Values)
                key.Release();
            Keys.Clear();
        }
    }

    private sealed class Attempt(ulong id, ulong epoch, ulong generation, JwksTrigger trigger, long deadline)
    {
        public ulong Id { get; } = id;
        public ulong Epoch { get; } = epoch;
        public ulong Generation { get; } = generation;
        public JwksTrigger Trigger { get; } = trigger;
        public long Deadline { get; } = deadline;
        public CancellationTokenSource TransportCancellation { get; } = new();
        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record FetchResult(Snapshot? Snapshot, string Result)
    {
        public static FetchResult Success(Snapshot snapshot) => new(snapshot, "success");
        public static FetchResult Failure(string result) => new(null, result);
    }
}
