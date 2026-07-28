using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpotifyBooster.Api;

namespace SpotifyBooster.Api.Tests;

public sealed class ApiSecurityTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly ECDsa _ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private readonly ApiMetrics _metrics = new();

    [Fact]
    public async Task ValidRs256TokenPassesAndWrongAlgorithmFailsClosed()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("key-1")));
        });
        var validator = new SupabaseJwtValidator(coordinator, Options(), _time, _metrics);
        var subject = Guid.NewGuid();
        var valid = Token("RS256", "key-1", subject, _time.GetUtcNow().AddMinutes(5));
        var first = await validator.ValidateAsync(valid, TestContext.Current.CancellationToken);
        var second = await validator.ValidateAsync(valid, TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);
        Assert.Equal(subject, first.Subject);
        Assert.True(second.Succeeded);
        Assert.Equal(1, calls);

        var unexpected = Token("HS256", "key-1", subject, _time.GetUtcNow().AddMinutes(5));
        Assert.Equal("invalid_algorithm", (await validator.ValidateAsync(unexpected, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public async Task ValidEs256TokenPasses()
    {
        using var coordinator = Coordinator((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, EcJwks("ec-key"))));
        var validator = new SupabaseJwtValidator(coordinator, Options(), _time, _metrics);
        var subject = Guid.NewGuid();
        var result = await validator.ValidateAsync(
            Token("ES256", "ec-key", subject, _time.GetUtcNow().AddMinutes(5)),
            TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal(subject, result.Subject);
    }

    [Fact]
    public async Task IssuerAudienceExpirationAndSubjectAreValidated()
    {
        using var coordinator = Coordinator((_, _) => Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("k"))));
        var validator = new SupabaseJwtValidator(coordinator, Options(), _time, _metrics);
        Assert.Equal("issuer", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(1), issuer: "https://evil.example"), TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("audience", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(1), audience: "wrong"), TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("expired", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(-2)), TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("missing_subject", (await validator.ValidateAsync(Token("RS256", "k", null,
            _time.GetUtcNow().AddMinutes(1)), TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("not_yet_valid", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(1), notBefore: _time.GetUtcNow().AddMinutes(2)),
            TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("malformed", (await validator.ValidateAsync(Token("RS256", "k", Guid.NewGuid(),
            _time.GetUtcNow().AddMinutes(1), invalidNotBefore: true),
            TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public async Task JwksExpiresAtExactTenMinuteBoundaryAndRetryGatePreventsStorm()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? Json(HttpStatusCode.OK, RsaJwks("known"))
                : Json(HttpStatusCode.ServiceUnavailable, "{}"));
        });
        Assert.NotNull(await coordinator.FindAsync("known", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));
        Assert.True(coordinator.IsReady);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(coordinator.IsReady);
        Assert.Null(await coordinator.FindAsync("known", TestContext.Current.CancellationToken));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => coordinator.FindAsync("other", TestContext.Current.CancellationToken)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task UnknownKidsShareOneGlobalCooldown()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            calls++;
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("known")));
        });
        Assert.NotNull(await coordinator.FindAsync("known", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromSeconds(60));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            coordinator.FindAsync("unknown-" + i, TestContext.Current.CancellationToken)));
        Assert.Equal(2, calls);
        Assert.All(await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            coordinator.FindAsync("again-" + i, TestContext.Current.CancellationToken))), Assert.Null);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AdministrativePurgeImmediatelyReplacesSnapshot()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            var kid = Interlocked.Increment(ref calls) == 1 ? "old" : "new";
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks(kid)));
        });
        Assert.NotNull(await coordinator.FindAsync("old", TestContext.Current.CancellationToken));
        await coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(await coordinator.FindAsync("new", TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task LeasedKeyRemainsUsableAcrossSnapshotRetirement()
    {
        using var coordinator = Coordinator((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("leased"))));
        using var lease = await coordinator.FindAsync("leased", TestContext.Current.CancellationToken);
        Assert.NotNull(lease);
        var data = Encoding.UTF8.GetBytes("lease-race-proof");
        var signature = _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        await coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        Assert.True(lease.Verify(data, signature));
    }

    [Fact]
    public async Task CallerCancellationDoesNotCancelSharedJwksFetch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = Coordinator((_, _) =>
        {
            started.SetResult();
            return response.Task;
        });
        using var caller = new CancellationTokenSource();
        var cancelledWaiter = coordinator.FindAsync("shared", caller.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);

        var joiner = coordinator.FindAsync("shared", TestContext.Current.CancellationToken);
        response.SetResult(Json(HttpStatusCode.OK, RsaJwks("shared")));
        Assert.NotNull(await joiner);
    }

    [Fact]
    public async Task LogicalDeadlineCompletesJoinersAndLateSuccessCannotPublish()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = Coordinator((_, _) =>
        {
            started.TrySetResult();
            return response.Task;
        });
        var leader = coordinator.FindAsync("late", TestContext.Current.CancellationToken);
        var joiner = coordinator.FindAsync("late", TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(await leader.WaitAsync(TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken));
        Assert.Null(await joiner.WaitAsync(TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken));
        response.SetResult(Json(HttpStatusCode.OK, RsaJwks("late")));
        await Task.Yield();
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public async Task ContentIOExceptionCompletesLeaderAndAllJoiners()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingContent(new IOException("private-content"))
            });
        });
        var requests = Enumerable.Range(0, 100)
            .Select(_ => coordinator.FindAsync("missing", TestContext.Current.CancellationToken))
            .ToArray();
        var results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.All(results, Assert.Null);
        Assert.Equal(1, calls);
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public async Task IsReadyExpirationIsPassiveAndFindStartsMandatoryExpiryLeader()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("old")));
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("new")));
        });
        using var old = await coordinator.FindAsync("old", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(coordinator.IsReady);
        Assert.Equal(1, calls);
        using var current = await coordinator.FindAsync("new", TestContext.Current.CancellationToken);
        Assert.NotNull(current);
        Assert.Equal(2, calls);
        Assert.True(coordinator.IsReady);
    }

    [Fact]
    public async Task RetryGateIsClosedUntilExactSixtySecondBoundary()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
        });
        Assert.Null(await coordinator.FindAsync("missing", TestContext.Current.CancellationToken));
        Assert.All(await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            coordinator.FindAsync($"missing-{index}", TestContext.Current.CancellationToken))), Assert.Null);
        Assert.Equal(1, calls);
        _time.Advance(TimeSpan.FromSeconds(59.999));
        Assert.Null(await coordinator.FindAsync("still-closed", TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(await coordinator.FindAsync("boundary", TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task HundredExpiryObserversShareOneMandatoryLeader()
    {
        var calls = 0;
        var expiry = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = Coordinator((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("old")));
            return expiry.Task;
        });
        using var old = await coordinator.FindAsync("old", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(coordinator.IsReady);
        var observers = Enumerable.Range(0, 100)
            .Select(_ => coordinator.FindAsync("new", TestContext.Current.CancellationToken)).ToArray();
        expiry.SetResult(Json(HttpStatusCode.OK, RsaJwks("new")));
        var keys = await Task.WhenAll(observers);
        Assert.All(keys, Assert.NotNull);
        foreach (var key in keys) key!.Dispose();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExpiryDetachesOldUnknownAttemptAndCompletesItsJoiners()
    {
        var calls = 0;
        var unknownStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = Coordinator((_, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("old")));
            if (call == 2)
            {
                unknownStarted.SetResult();
                return staleResponse.Task;
            }
            return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("new")));
        });
        using var old = await coordinator.FindAsync("old", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(599));
        var staleJoiner = coordinator.FindAsync("unknown", TestContext.Current.CancellationToken);
        await unknownStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(1));
        using var current = await coordinator.FindAsync("new", TestContext.Current.CancellationToken);
        Assert.NotNull(current);
        Assert.Null(await staleJoiner.WaitAsync(TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken));
        staleResponse.SetResult(Json(HttpStatusCode.OK, RsaJwks("compromised")));
        await Task.Yield();
        Assert.Null(await coordinator.FindAsync("compromised", TestContext.Current.CancellationToken));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void FixedWindowLimiterHasExactBoundaryAndNoQueue()
    {
        var limiter = new FixedWindowLimiter(_time);
        for (var i = 0; i < 30; i++) Assert.True(limiter.Acquire("sub:a", 30).Allowed);
        var rejected = limiter.Acquire("sub:a", 30);
        Assert.False(rejected.Allowed);
        Assert.Equal(60, rejected.RetryAfterSeconds);
        Assert.True(limiter.Acquire("sub:b", 30).Allowed);
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.True(limiter.Acquire("sub:a", 30).Allowed);
    }

    [Fact]
    public void LimiterCapacityNeverEvictsAnActiveWindow()
    {
        var limiter = new FixedWindowLimiter(_time);
        for (var i = 0; i < 4096; i++)
            Assert.True(limiter.Acquire($"partition:{i}", 30).Allowed);
        var capacity = limiter.Acquire("partition:4096", 30);
        Assert.False(capacity.Allowed);
        Assert.True(capacity.CapacityExceeded);
        for (var i = 1; i < 30; i++)
            Assert.True(limiter.Acquire("partition:0", 30).Allowed);
        Assert.False(limiter.Acquire("partition:0", 30).Allowed);
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.True(limiter.Acquire("partition:4096", 30).Allowed);
    }

    [Fact]
    public void RetryAfterTracksBeginningMiddleAndFinalWindowInstant()
    {
        var limiter = new FixedWindowLimiter(_time);
        Assert.True(limiter.Acquire("partition", 1).Allowed);
        Assert.Equal(60, limiter.Acquire("partition", 1).RetryAfterSeconds);
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(30, limiter.Acquire("partition", 1).RetryAfterSeconds);
        _time.Advance(TimeSpan.FromSeconds(29.5));
        Assert.Equal(1, limiter.Acquire("partition", 1).RetryAfterSeconds);
        _time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.True(limiter.Acquire("partition", 1).Allowed);
    }

    [Fact]
    public async Task DataApiKeepsConcurrentBearerHeadersRequestScoped()
    {
        var seen = new List<(string ApiKey, string Bearer)>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var subjectA = Guid.NewGuid();
        var subjectB = Guid.NewGuid();
        using var client = new HttpClient(new Handler(async (request, _) =>
        {
            lock (seen) seen.Add((request.Headers.GetValues("apikey").Single(), request.Headers.Authorization!.Parameter!));
            if (Interlocked.Increment(ref count) == 2) gate.SetResult();
            await gate.Task;
            var bearer = request.Headers.Authorization!.Parameter!;
            var subject = bearer == "token-a" ? subjectA : subjectB;
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                userId = subject,
                displayName = (string?)null,
                plan = "free",
                status = "active",
                revision = 1,
                features = Array.Empty<string>()
            }));
        }));
        var api = new DataApiClient(client, Options(), _metrics);
        await Task.WhenAll(api.GetMeAsync(subjectA, "token-a", TestContext.Current.CancellationToken), api.GetMeAsync(subjectB, "token-b", TestContext.Current.CancellationToken));
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Contains(("sb_publishable_test", "token-a"), seen);
        Assert.Contains(("sb_publishable_test", "token-b"), seen);
    }

    [Fact]
    public async Task DataApiRejectsSubjectMismatchAndOversize()
    {
        using var mismatchClient = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"userId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","plan":"free","status":"active","revision":1,"features":[]}"""))));
        var api = new DataApiClient(mismatchClient, Options(), _metrics);
        await Assert.ThrowsAsync<DataApiException>(() => api.GetMeAsync(Guid.NewGuid(), "token", TestContext.Current.CancellationToken));

        using var largeClient = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, new string('x', 300_000)))));
        var largeApi = new DataApiClient(largeClient, Options(), _metrics);
        await Assert.ThrowsAsync<DataApiException>(() => largeApi.GetMeAsync(Guid.NewGuid(), "token", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DataApiNormalizesRedirectTransportJsonAndTimeoutFailures()
    {
        var subject = Guid.NewGuid();
        using var redirect = new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Redirect))));
        await Assert.ThrowsAsync<DataApiException>(() => new DataApiClient(redirect, Options(), _metrics)
            .GetMeAsync(subject, "token", TestContext.Current.CancellationToken));

        using var transport = new HttpClient(new Handler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("private-host"))));
        var transportError = await Assert.ThrowsAsync<DataApiException>(() =>
            new DataApiClient(transport, Options(), _metrics)
                .GetMeAsync(subject, "token", TestContext.Current.CancellationToken));
        Assert.DoesNotContain("private-host", transportError.Message, StringComparison.Ordinal);

        using var invalidJson = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, "{invalid"))));
        await Assert.ThrowsAsync<DataApiException>(() => new DataApiClient(invalidJson, Options(), _metrics)
            .GetMeAsync(subject, "token", TestContext.Current.CancellationToken));

        using var timeout = new HttpClient(new Handler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        }))
        { Timeout = TimeSpan.FromMilliseconds(10) };
        await Assert.ThrowsAsync<DataApiException>(() => new DataApiClient(timeout, Options(), _metrics)
            .GetMeAsync(subject, "token", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JwksRejectsPrivateKeyMaterial()
    {
        var document = JsonNode.Parse(RsaJwks("private"))!.AsObject();
        document["keys"]!.AsArray()[0]!["d"] = "private-material";
        using var coordinator = Coordinator((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, document.ToJsonString())));
        Assert.Null(await coordinator.FindAsync("private", TestContext.Current.CancellationToken));
        Assert.False(coordinator.IsReady);
    }

    [Fact]
    public async Task JwksCacheAcceptsExplicitMaximumAndRejectsOneAdditionalKey()
    {
        var maximumKids = Enumerable.Range(0, JwksCoordinator.MaximumCachedKeys)
            .Select(index => $"key-{index}")
            .ToArray();
        using (var accepted = Coordinator((_, _) =>
                   Task.FromResult(Json(HttpStatusCode.OK, RsaJwks(maximumKids)))))
        {
            using var key = await accepted.FindAsync(maximumKids[^1],
                TestContext.Current.CancellationToken);
            Assert.NotNull(key);
            Assert.True(accepted.IsReady);
        }

        var oversizedKids = Enumerable.Range(0, JwksCoordinator.MaximumCachedKeys + 1)
            .Select(index => $"oversized-{index}")
            .ToArray();
        using var rejected = Coordinator((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, RsaJwks(oversizedKids))));
        Assert.Null(await rejected.FindAsync(oversizedKids[0],
            TestContext.Current.CancellationToken));
        Assert.False(rejected.IsReady);
    }

    [Fact]
    public async Task JwksHttpRedirectInvalidAndOversizedDocumentsFailClosed()
    {
        var cases = new Func<HttpResponseMessage>[]
        {
            () => new(HttpStatusCode.Redirect),
            () => Json(HttpStatusCode.InternalServerError, "{}"),
            () => Json(HttpStatusCode.OK, "{invalid"),
            () => Json(HttpStatusCode.OK, new string('x', 300_000))
        };
        foreach (var response in cases)
        {
            using var coordinator = Coordinator((_, _) => Task.FromResult(response()));
            Assert.Null(await coordinator.FindAsync("missing", TestContext.Current.CancellationToken));
            Assert.False(coordinator.IsReady);
        }
    }

    [Fact]
    public async Task RotationOverlapAcceptsBothKeysThenRejectsRetiredKey()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            Interlocked.Increment(ref calls) == 1
                ? RsaJwks("old", "new")
                : RsaJwks("new"))));
        using var old = await coordinator.FindAsync("old", TestContext.Current.CancellationToken);
        using var newDuringOverlap = await coordinator.FindAsync("new", TestContext.Current.CancellationToken);
        Assert.NotNull(old);
        Assert.NotNull(newDuringOverlap);
        await coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        using var current = await coordinator.FindAsync("new", TestContext.Current.CancellationToken);
        Assert.NotNull(current);
        Assert.Null(await coordinator.FindAsync("old", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RotationOverlapRemainsValidForTwentyMinutesThenPurgeRetiresOldKey()
    {
        var calls = 0;
        using var coordinator = Coordinator((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            Interlocked.Increment(ref calls) < 4 ? RsaJwks("old", "new") : RsaJwks("new"))));
        using (await coordinator.FindAsync("old", TestContext.Current.CancellationToken)) { }
        for (var interval = 0; interval < 2; interval++)
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            using var old = await coordinator.FindAsync("old", TestContext.Current.CancellationToken);
            using var current = await coordinator.FindAsync("new", TestContext.Current.CancellationToken);
            Assert.NotNull(old);
            Assert.NotNull(current);
        }

        await coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        using var retained = await coordinator.FindAsync("new", TestContext.Current.CancellationToken);
        Assert.NotNull(retained);
        Assert.Null(await coordinator.FindAsync("old", TestContext.Current.CancellationToken));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task ConcurrentFailedPurgesInvalidateNonCooperativeOldFetchAndRemainUnready()
    {
        var decisions = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var staleCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.JwksDecisions))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var value = tags.ToArray().Single().Value!.ToString()!;
            decisions.Enqueue(value);
            if (value == "stale_completion_discarded")
                staleCompletion.TrySetResult();
        });
        listener.Start();
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) return oldResponse.Task;
            purgeStarted.TrySetResult();
            return purgeResponse.Task;
        }));
        using var coordinator = new JwksCoordinator(client, Options(), _time, metrics);
        var old = coordinator.FindAsync("compromised", TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);
        var firstPurge = coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        await purgeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var secondPurge = coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        purgeResponse.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
        await Task.WhenAll(firstPurge, secondPurge);
        var oldResult = await old;
        oldResponse.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
        await staleCompletion.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Contains("stale_completion_discarded", decisions);
        Assert.Null(oldResult);
        Assert.False(coordinator.IsReady);
        Assert.Null(await coordinator.FindAsync("compromised", TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ReadinessProbeRequiresExactThreePropertyDocumentAndNoBearer()
    {
        HttpRequestMessage? observed = null;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            observed = Clone(request);
            return Task.FromResult(Json(HttpStatusCode.OK,
                """{"service":"spotify-booster-data","status":"ok","version":1}"""));
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        Assert.True(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.True(state.IsDataApiReady);
        Assert.Null(observed!.Headers.Authorization);
        Assert.Equal("sb_publishable_test", observed.Headers.GetValues("apikey").Single());
        Assert.Equal("/rest/v1/rpc/spotify_booster_health_v1", observed.RequestUri!.AbsolutePath);
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.False(state.IsDataApiReady);
    }

    [Fact]
    public async Task ReadinessProbeUsesFreshConnectionAndRecoversAfterTransportReplacement()
    {
        var transportGeneration = 0;
        var connectionClose = new List<bool?>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            connectionClose.Add(request.Headers.ConnectionClose);
            return transportGeneration == 0
                ? Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("Retired transport is unavailable."))
                : Task.FromResult(Json(HttpStatusCode.OK,
                    """{"service":"spotify-booster-data","status":"ok","version":1}"""));
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);

        Assert.False(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.False(state.IsDataApiReady);

        transportGeneration = 1;
        Assert.True(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.True(state.IsDataApiReady);
        Assert.Equal([true, true], connectionClose);
    }

    [Fact]
    public async Task ReadinessRejectsAdditionalProperties()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"service":"spotify-booster-data","status":"ok","version":1,"detail":"leak"}"""))));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        Assert.False(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.False(state.IsDataApiReady);
    }

    [Fact]
    public async Task ReadinessFailureMatrixAlwaysFailsClosed()
    {
        var cases = new (HttpStatusCode Status, string Body)[]
        {
            (HttpStatusCode.ServiceUnavailable, "{}"),
            (HttpStatusCode.Redirect, "{}"),
            (HttpStatusCode.OK, """{"service":"spotify-booster-data","status":"ok"}"""),
            (HttpStatusCode.OK, """{"service":"spotify-booster-data","status":"bad","version":1}"""),
            (HttpStatusCode.OK, "{invalid"),
            (HttpStatusCode.OK, new string('x', 257))
        };
        foreach (var item in cases)
        {
            using var client = new HttpClient(new Handler((_, _) =>
                Task.FromResult(Json(item.Status, item.Body))));
            var state = new ReadinessState(_time, _metrics);
            var probe = new DataApiReadinessProbe(client, Options(), state, _time);
            Assert.False(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task HostedReadinessProbeRunsImmediatelyAndEveryThirtySeconds()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Json(HttpStatusCode.OK,
                """{"service":"spotify-booster-data","status":"ok","version":1}"""));
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        using var hosted = new DataApiReadinessHostedService(probe, _time);
        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);
        _time.Advance(TimeSpan.FromSeconds(29));
        await Task.Yield();
        Assert.Equal(1, calls);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 2);
        await hosted.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SuccessfulReadinessProbeDisposesDeadlineTimerImmediately()
    {
        using var client = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK,
                """{"service":"spotify-booster-data","status":"ok","version":1}"""))));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);

        Assert.True(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _time.TimerCount);
    }

    [Fact]
    public async Task HostedReadinessShutdownDoesNotWaitForNonCooperativeTransport()
    {
        var calls = 0;
        var response = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return response.Task;
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        using var hosted = new DataApiReadinessHostedService(probe, _time);
        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);

        await hosted.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        response.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
        await Task.Yield();
        Assert.False(state.IsDataApiReady);
    }

    [Fact]
    public async Task ReadinessTimeoutAtTwoSecondsFailsClosed()
    {
        using var client = new HttpClient(new Handler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        var result = probe.ProbeAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(await result.WaitAsync(TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken));
        Assert.False(state.IsDataApiReady);
    }

    [Fact]
    public async Task HostedReadinessHardDeadlineRecoversAndDiscardsLateTransport()
    {
        var calls = 0;
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return firstResponse.Task;
            return Task.FromResult(Json(HttpStatusCode.OK,
                """{"service":"spotify-booster-data","status":"ok","version":1}"""));
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        using var hosted = new DataApiReadinessHostedService(probe, _time);
        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);

        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => _time.TimerCount == 1);
        Assert.False(state.IsDataApiReady);

        _time.Advance(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 2);
        await WaitUntilAsync(() => state.IsDataApiReady);

        firstResponse.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
        await Task.Yield();
        Assert.True(state.IsDataApiReady);
        Assert.Equal(2, calls);
        await hosted.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NewerReadinessGenerationCannotBeOverwrittenByOlderCompletion()
    {
        var calls = 0;
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return firstResponse.Task;
            return Task.FromResult(Json(HttpStatusCode.OK,
                """{"service":"spotify-booster-data","status":"ok","version":1}"""));
        }));
        var state = new ReadinessState(_time, _metrics);
        var probe = new DataApiReadinessProbe(client, Options(), state, _time);
        var older = probe.ProbeAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);

        Assert.True(await probe.ProbeAsync(TestContext.Current.CancellationToken));
        Assert.True(state.IsDataApiReady);

        firstResponse.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
        Assert.False(await older);
        Assert.True(state.IsDataApiReady);
    }

    [Fact]
    public void MetricsExposeClosedReadinessAndCooldownGauges()
    {
        var observed = new List<(string Name, double Value, string? Dependency)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument.Meter, _metrics.JwksRefresh.Meter))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            observed.Add((instrument.Name, value,
                tags.ToArray().FirstOrDefault(static tag => tag.Key == "dependency").Value?.ToString())));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            observed.Add((instrument.Name, value,
                tags.ToArray().FirstOrDefault(static tag => tag.Key == "dependency").Value?.ToString())));
        listener.Start();
        using var coordinator = Coordinator((_, _) =>
            Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}")));
        _ = new ReadinessState(_time, _metrics);
        listener.RecordObservableInstruments();
        Assert.Contains(observed, item => item.Name == "jwks_retry_cooldown_remaining_seconds" &&
            item.Value is >= 0 and <= 60 && item.Dependency is null);
        Assert.Contains(observed, item => item.Name == "readiness_state" && item.Dependency == "jwks");
        Assert.Contains(observed, item => item.Name == "readiness_state" && item.Dependency == "data_api");
    }

    [Fact]
    public async Task MeterListenerSeesClosedRefreshAndReadinessLabels()
    {
        var counters = new List<(string Name, string Labels)>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.JwksRefresh.Meter))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            counters.Add((instrument.Name, string.Join("|",
                tags.ToArray().OrderBy(static tag => tag.Key)
                    .Select(static tag => $"{tag.Key}={tag.Value}")))));
        listener.Start();
        using var client = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("metric")))));
        using var coordinator = new JwksCoordinator(client, Options(), _time, metrics);
        using var lease = await coordinator.FindAsync("metric", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(coordinator.IsReady);
        var state = new ReadinessState(_time, metrics);
        state.ObserveDataApi(true);
        state.ObserveDataApi(false);
        Assert.Contains(("jwks_refresh_total", "result=success|trigger=cold_start"), counters);
        Assert.Contains(("readiness_transitions_total", "dependency=jwks|to=unready"), counters);
        Assert.Contains(("readiness_transitions_total", "dependency=data_api|to=unready"), counters);
    }

    [Fact]
    public async Task FirstExpiryObserverIsLeaderAndOtherNinetyNineObserversJoin()
    {
        var decisions = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.JwksDecisions))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            decisions.Enqueue(tags.ToArray().Single().Value!.ToString()!));
        listener.Start();
        var release = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("old")));
            started.TrySetResult();
            return release.Task;
        }));
        using var coordinator = new JwksCoordinator(client, Options(), _time, metrics);
        using (await coordinator.FindAsync("old", TestContext.Current.CancellationToken)) { }
        decisions.Clear();
        _time.Advance(TimeSpan.FromMinutes(10));
        var observers = new List<Task<JwkKey?>>();
        observers.Add(coordinator.FindAsync("new", TestContext.Current.CancellationToken));
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        for (var index = 1; index < 100; index++)
            observers.Add(coordinator.FindAsync("new", TestContext.Current.CancellationToken));
        release.SetResult(Json(HttpStatusCode.OK, RsaJwks("new")));
        var leases = await Task.WhenAll(observers);
        foreach (var lease in leases) lease?.Dispose();
        Assert.Equal(1, decisions.Count(value => value == "leader_created"));
        Assert.Equal(99, decisions.Count(value => value == "joined_inflight"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ReadinessMetricsCaptureOnlyClosedUnknownReadyUnreadyReadyTransitions()
    {
        var transitions = new List<string>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.ReadinessTransitions))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            transitions.Add(string.Join("|", tags.ToArray().OrderBy(static tag => tag.Key)
                .Select(static tag => $"{tag.Key}={tag.Value}"))));
        listener.Start();
        var state = new ReadinessState(_time, metrics);
        state.ObserveDataApi(true);
        state.ObserveDataApi(false);
        state.ObserveDataApi(true);
        Assert.Collection(transitions,
            value => Assert.Equal("dependency=data_api|to=unready", value),
            value => Assert.Equal("dependency=data_api|to=ready", value));
        Assert.All(transitions, value => Assert.DoesNotContain("@", value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DataApiMetricsUseOnlyProfileAndEntitlementOperations()
    {
        var labels = new List<string>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.DataApiRequests))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            labels.Add(string.Join("|", tags.ToArray().OrderBy(static tag => tag.Key)
                .Select(static tag => $"{tag.Key}={tag.Value}"))));
        listener.Start();
        var subject = Guid.NewGuid();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            JsonSerializer.Serialize(new
            {
                userId = subject,
                displayName = (string?)null,
                plan = "free",
                status = "active",
                revision = 1,
                features = Array.Empty<string>()
            })))));
        await new DataApiClient(client, Options(), metrics).GetMeAsync(subject, "token",
            TestContext.Current.CancellationToken);
        Assert.Equal(2, labels.Count);
        Assert.Contains("operation=profile|result=success", labels);
        Assert.Contains("operation=entitlement|result=success", labels);
    }

    [Fact]
    public async Task MetricsCoverEveryJwksTriggerWithClosedLabels()
    {
        var labels = new List<string>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.JwksRefresh))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            labels.Add(string.Join("|", tags.ToArray().OrderBy(static tag => tag.Key)
                .Select(static tag => $"{tag.Key}={tag.Value}"))));
        listener.Start();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            return Task.FromResult(call <= 3
                ? Json(HttpStatusCode.OK, RsaJwks("known"))
                : Json(HttpStatusCode.ServiceUnavailable, "{}"));
        }));
        using var coordinator = new JwksCoordinator(client, Options(), _time, metrics);
        using (await coordinator.FindAsync("known", TestContext.Current.CancellationToken)) { }
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.Null(await coordinator.FindAsync("unknown", TestContext.Current.CancellationToken));
        await coordinator.PurgeAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(coordinator.IsReady);
        Assert.Null(await coordinator.FindAsync("expiry", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.Null(await coordinator.FindAsync("retry", TestContext.Current.CancellationToken));
        Assert.Contains("result=success|trigger=cold_start", labels);
        Assert.Contains("result=success|trigger=unknown_kid", labels);
        Assert.Contains("result=success|trigger=admin_purge", labels);
        Assert.Contains("result=http_error|trigger=expiry", labels);
        Assert.Contains("result=http_error|trigger=retry", labels);
        Assert.All(labels, label => Assert.Matches(
            "^result=(success|timeout|http_error|invalid_document|invalidated)\\|" +
            "trigger=(cold_start|expiry|retry|unknown_kid|admin_purge)$", label));
    }

    [Fact]
    public async Task MeterListenerCapturesTimeoutInvalidDocumentAndInvalidationResults()
    {
        var refreshes = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var decisions = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.JwksRefresh) ||
                ReferenceEquals(instrument, metrics.JwksDecisions))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var labels = MetricLabels(tags);
            if (ReferenceEquals(instrument, metrics.JwksRefresh))
                refreshes.Enqueue(labels);
            else
                decisions.Enqueue(labels);
        });
        listener.Start();

        var invalidTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        using (var invalidClient = new HttpClient(new Handler((_, _) =>
                   Task.FromResult(Json(HttpStatusCode.OK, "{private-invalid-document")))))
        using (var invalid = new JwksCoordinator(invalidClient, Options(), invalidTime, metrics))
            Assert.Null(await invalid.FindAsync("private-kid@example.test",
                TestContext.Current.CancellationToken));

        var timeoutTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var timeoutResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutDiscarded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (var timeoutClient = new HttpClient(new Handler((_, _) =>
               {
                   timeoutStarted.TrySetResult();
                   return timeoutResponse.Task;
               })))
        using (var timeout = new JwksCoordinator(timeoutClient, Options(), timeoutTime, metrics))
        {
            var pending = timeout.FindAsync("timeout-private-kid",
                TestContext.Current.CancellationToken);
            await timeoutStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => timeoutTime.TimerCount == 1);
            timeoutTime.Advance(TimeSpan.FromSeconds(5));
            Assert.Null(await pending.WaitAsync(TestContext.Current.CancellationToken));
            timeoutResponse.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
            await WaitUntilAsync(() =>
            {
                var observed = decisions.Any(value =>
                    value == "event=stale_completion_discarded");
                if (observed) timeoutDiscarded.TrySetResult();
                return observed;
            });
            await timeoutDiscarded.Task.WaitAsync(TestContext.Current.CancellationToken);
        }

        var oldResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var purgeResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var purgeTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        using (var purgeClient = new HttpClient(new Handler((_, _) =>
               Interlocked.Increment(ref calls) == 1 ? oldResponse.Task : purgeResponse.Task)))
        using (var purge = new JwksCoordinator(purgeClient, Options(), purgeTime, metrics))
        {
            var old = purge.FindAsync("invalidated-private-kid",
                TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);
            var administrative = purge.PurgeAsync(TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => Volatile.Read(ref calls) == 2);
            purgeResponse.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
            await administrative;
            Assert.Null(await old);
            oldResponse.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
            await WaitUntilAsync(() => decisions.Count(value =>
                value == "event=stale_completion_discarded") >= 2);
        }

        Assert.Contains(refreshes, value =>
            value == "result=invalid_document|trigger=cold_start");
        Assert.Contains(refreshes, value =>
            value == "result=timeout|trigger=cold_start");
        Assert.Contains(refreshes, value =>
            value == "result=invalidated|trigger=cold_start");
        Assert.All(refreshes, value => Assert.Matches(
            "^result=(success|timeout|http_error|invalid_document|invalidated)\\|" +
            "trigger=(cold_start|expiry|retry|unknown_kid|admin_purge)$", value));
        Assert.All(decisions, value => Assert.Matches(
            "^event=(leader_created|joined_inflight|retry_gate_rejected|" +
            "unknown_cooldown_rejected|validation_succeeded_after_refresh|" +
            "unknown_after_refresh|stale_completion_discarded|epoch_invalidated|" +
            "generation_invalidated)$", value));
        Assert.DoesNotContain(refreshes.Concat(decisions), value =>
            value.Contains("private", StringComparison.Ordinal) ||
            value.Contains('@'));
    }

    [Fact]
    public async Task MeterListenerProvokesExactClosedJwksDecisionEventSet()
    {
        var decisions = new System.Collections.Concurrent.ConcurrentQueue<string>();
        TaskCompletionSource? staleCompletionObserved = null;
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.JwksDecisions))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            Assert.Equal(1, value);
            var decision = MetricLabels(tags)["event=".Length..];
            decisions.Enqueue(decision);
            if (decision == "stale_completion_discarded")
                Volatile.Read(ref staleCompletionObserved)?.TrySetResult();
        });
        listener.Start();

        var sharedResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sharedStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (var sharedClient = new HttpClient(new Handler((_, _) =>
               {
                   sharedStarted.TrySetResult();
                   return sharedResponse.Task;
               })))
        using (var shared = new JwksCoordinator(sharedClient, Options(),
                   new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), metrics))
        {
            var leader = shared.FindAsync("shared", TestContext.Current.CancellationToken);
            await sharedStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            var joiner = shared.FindAsync("shared", TestContext.Current.CancellationToken);
            sharedResponse.SetResult(Json(HttpStatusCode.OK, RsaJwks("shared")));
            using var leaderLease = await leader;
            using var joinerLease = await joiner;
            Assert.NotNull(leaderLease);
            Assert.NotNull(joinerLease);
        }

        using (var unknownClient = new HttpClient(new Handler((_, _) =>
                   Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("known"))))))
        using (var unknown = new JwksCoordinator(unknownClient, Options(),
                   new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), metrics))
        {
            Assert.Null(await unknown.FindAsync("missing",
                TestContext.Current.CancellationToken));
            Assert.Null(await unknown.FindAsync("still-missing",
                TestContext.Current.CancellationToken));
        }

        using (var retryClient = new HttpClient(new Handler((_, _) =>
                   Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}")))))
        using (var retry = new JwksCoordinator(retryClient, Options(),
                   new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), metrics))
        {
            Assert.Null(await retry.FindAsync("retry",
                TestContext.Current.CancellationToken));
            Assert.Null(await retry.FindAsync("retry-again",
                TestContext.Current.CancellationToken));
        }

        var epochOld = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var epochPurge = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var epochOldStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var epochPurgeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var epochCalls = 0;
        using (var epochClient = new HttpClient(new Handler((_, _) =>
               {
                   if (Interlocked.Increment(ref epochCalls) == 1)
                   {
                       epochOldStarted.TrySetResult();
                       return epochOld.Task;
                   }
                   epochPurgeStarted.TrySetResult();
                   return epochPurge.Task;
               })))
        using (var epoch = new JwksCoordinator(epochClient, Options(),
                   new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), metrics))
        {
            var old = epoch.FindAsync("epoch-old", TestContext.Current.CancellationToken);
            await epochOldStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            var purge = epoch.PurgeAsync(TestContext.Current.CancellationToken);
            await epochPurgeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            epochPurge.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
            await purge;
            Assert.Null(await old);
            var epochStale = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref staleCompletionObserved, epochStale);
            epochOld.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
            await epochStale.Task.WaitAsync(TestContext.Current.CancellationToken);
            Volatile.Write(ref staleCompletionObserved, null);
        }

        var generationTime =
            new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var generationOld = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var generationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var generationCalls = 0;
        using (var generationClient = new HttpClient(new Handler((_, _) =>
               {
                   var call = Interlocked.Increment(ref generationCalls);
                   if (call == 1)
                       return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("generation-known")));
                   if (call == 2)
                   {
                       generationStarted.TrySetResult();
                       return generationOld.Task;
                   }
                   return Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("generation-new")));
               })))
        using (var generation = new JwksCoordinator(generationClient, Options(),
                   generationTime, metrics))
        {
            using var known = await generation.FindAsync("generation-known",
                TestContext.Current.CancellationToken);
            generationTime.Advance(TimeSpan.FromSeconds(60));
            var old = generation.FindAsync("generation-unknown",
                TestContext.Current.CancellationToken);
            await generationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            generationTime.AdvanceWithoutFiringTimers(TimeSpan.FromMinutes(9));
            using var current = await generation.FindAsync("generation-new",
                TestContext.Current.CancellationToken);
            Assert.NotNull(current);
            Assert.Null(await old);
            var generationStale = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref staleCompletionObserved, generationStale);
            generationOld.SetResult(Json(HttpStatusCode.ServiceUnavailable, "{}"));
            await generationStale.Task.WaitAsync(TestContext.Current.CancellationToken);
            Volatile.Write(ref staleCompletionObserved, null);
        }

        Assert.Equal(
            "epoch_invalidated,generation_invalidated,joined_inflight,leader_created," +
            "retry_gate_rejected,stale_completion_discarded,unknown_after_refresh," +
            "unknown_cooldown_rejected,validation_succeeded_after_refresh",
            string.Join(',', decisions.Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)));
        Assert.All(decisions, value => Assert.Matches(
            "^(leader_created|joined_inflight|retry_gate_rejected|" +
            "unknown_cooldown_rejected|validation_succeeded_after_refresh|" +
            "unknown_after_refresh|stale_completion_discarded|epoch_invalidated|" +
            "generation_invalidated)$", value));
    }

    [Fact]
    public async Task MetricSeriesRemainInvariantAcrossOneThousandSensitiveDimensions()
    {
        var measurements =
            new System.Collections.Concurrent.ConcurrentQueue<(string Name, string Labels)>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.JwksDecisions) ||
                ReferenceEquals(instrument, metrics.JwtRejections) ||
                ReferenceEquals(instrument, metrics.RateLimitRejections))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            Assert.Equal(1, value);
            measurements.Enqueue((instrument.Name, MetricLabels(tags)));
        });
        listener.Start();
        var sensitiveInputs = new List<string>(6_000);

        using var client = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("known")))));
        using var coordinator = new JwksCoordinator(client, Options(),
            new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), metrics);
        using var known = await coordinator.FindAsync("known",
            TestContext.Current.CancellationToken);
        Assert.NotNull(known);
        for (var index = 0; index < 1_000; index++)
        {
            var email = $"person{index:D4}@example.test";
            var kid = $"kid-{index:D4}-{email}";
            sensitiveInputs.Add(email);
            sensitiveInputs.Add(kid);
            Assert.Null(await coordinator.FindAsync(kid,
                TestContext.Current.CancellationToken));
        }

        var validator = new SupabaseJwtValidator(coordinator, Options(),
            new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), metrics);
        for (var index = 0; index < 1_000; index++)
        {
            var bytes = new byte[16];
            BitConverter.TryWriteBytes(bytes.AsSpan(0, sizeof(int)), index + 1);
            var subject = new Guid(bytes);
            var subjectValue = subject.ToString();
            var host = $"private-{index:D4}.example";
            sensitiveInputs.Add(subjectValue);
            sensitiveInputs.Add(host);
            var token = Token("RS256", "known", subject,
                DateTimeOffset.Parse("2026-01-01T00:05:00Z"),
                issuer: $"https://{host}");
            sensitiveInputs.Add(token);
            Assert.False((await validator.ValidateAsync(token,
                TestContext.Current.CancellationToken)).Succeeded);
        }

        var limiter = new FixedWindowLimiter(
            new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        var middleware = new ApiRateLimitingMiddleware(_ => Task.CompletedTask);
        for (var index = 0; index < 1_000; index++)
        {
            var ip = $"10.0.{index / 256}.{index % 256}";
            sensitiveInputs.Add(ip);
            for (var request = 0; request < 11; request++)
            {
                var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                context.Request.Path = "/v1/me";
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                using var body = new MemoryStream();
                context.Response.Body = body;
                await middleware.InvokeAsync(context, limiter, metrics);
            }
        }

        Assert.Equal(1_000, measurements.Count(value =>
            value == ("jwks_decision_events_total", "event=unknown_cooldown_rejected")));
        Assert.Equal(1_000, measurements.Count(value =>
            value == ("jwt_rejections_total", "reason=issuer")));
        Assert.Equal(1_000, measurements.Count(value =>
            value == ("rate_limit_rejections_total", "policy=authenticated_api")));
        Assert.Equal(
            "jwks_decision_events_total|event=leader_created," +
            "jwks_decision_events_total|event=unknown_cooldown_rejected," +
            "jwks_decision_events_total|event=validation_succeeded_after_refresh," +
            "jwt_rejections_total|reason=issuer," +
            "rate_limit_rejections_total|policy=authenticated_api",
            string.Join(',', measurements
                .Select(static value => $"{value.Name}|{value.Labels}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)));
        foreach (var sensitive in sensitiveInputs)
            Assert.DoesNotContain(measurements, value =>
                value.Labels.Contains(sensitive, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MeterListenerCapturesEveryClosedJwtRejectionReason()
    {
        var reasons = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.JwtRejections))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            Assert.Equal(1, value);
            reasons.Enqueue(MetricLabels(tags));
        });
        listener.Start();
        using var client = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, RsaJwks("known")))));
        using var coordinator = new JwksCoordinator(client, Options(), _time, metrics);
        var validator = new SupabaseJwtValidator(coordinator, Options(), _time, metrics);
        var subject = Guid.NewGuid();
        var expiration = _time.GetUtcNow().AddMinutes(5);

        Assert.False((await validator.ValidateAsync("person@example.test",
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("HS256", "private-kid", subject, expiration),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            CorruptSignature(Token("RS256", "known", subject, expiration)),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("RS256", "known", subject, expiration, issuer: "https://private.example"),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("RS256", "known", subject, expiration, audience: "private-audience"),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("RS256", "known", subject, _time.GetUtcNow().AddMinutes(-2)),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("RS256", "known", subject, expiration,
                notBefore: _time.GetUtcNow().AddMinutes(2)),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("RS256", "known", subject, expiration, invalidNotBefore: true),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("RS256", "known", null, expiration),
            TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await validator.ValidateAsync(
            Token("RS256", "private-kid-person@example.test", subject, expiration),
            TestContext.Current.CancellationToken)).Succeeded);

        Assert.Equal(10, reasons.Count);
        Assert.Equal(
            "audience,expired,invalid_algorithm,invalid_signature,issuer,malformed," +
            "malformed,missing_subject,not_yet_valid,unknown_kid",
            string.Join(',', reasons.Select(value => value["reason=".Length..])
                .OrderBy(static value => value, StringComparer.Ordinal)));
        Assert.All(reasons, value => Assert.Matches(
            "^reason=(unknown_kid|invalid_signature|invalid_algorithm|issuer|audience|" +
            "expired|not_yet_valid|malformed|missing_subject)$", value));
        Assert.DoesNotContain(reasons, value =>
            value.Contains(subject.ToString(), StringComparison.Ordinal) ||
            value.Contains("private", StringComparison.Ordinal) ||
            value.Contains('@'));
    }

    [Fact]
    public async Task MeterListenerCapturesEveryDataResultAndOneClosedRateLimitRejection()
    {
        var measurements =
            new System.Collections.Concurrent.ConcurrentQueue<(string Name, double Value, string Labels)>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument, metrics.DataApiRequests) ||
                ReferenceEquals(instrument, metrics.DataApiDuration) ||
                ReferenceEquals(instrument, metrics.RateLimitRejections))
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, MetricLabels(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, MetricLabels(tags))));
        listener.Start();
        var subject = Guid.NewGuid();
        const string token = "private-token-person@example.test";
        var success = JsonSerializer.Serialize(new
        {
            userId = subject,
            displayName = (string?)null,
            plan = "free",
            status = "active",
            revision = 1,
            features = Array.Empty<string>()
        });

        async Task ExpectSuccessAsync(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            using var client = new HttpClient(new Handler(send));
            _ = await new DataApiClient(client, Options(), metrics).GetMeAsync(
                subject, token, TestContext.Current.CancellationToken);
        }

        async Task ExpectDataFailureAsync(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
            TimeSpan? clientTimeout = null)
        {
            using var client = new HttpClient(new Handler(send));
            if (clientTimeout is not null) client.Timeout = clientTimeout.Value;
            await Assert.ThrowsAsync<DataApiException>(() =>
                new DataApiClient(client, Options(), metrics).GetMeAsync(
                    subject, token, TestContext.Current.CancellationToken));
        }

        await ExpectSuccessAsync((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, success)));
        await ExpectDataFailureAsync((_, _) =>
            Task.FromResult(Json(HttpStatusCode.BadRequest, "{}")));
        await ExpectDataFailureAsync((_, _) =>
            Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, "{}")));
        await ExpectDataFailureAsync((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("private-host.example")));
        await ExpectDataFailureAsync((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, "{private-invalid-json")));
        await ExpectDataFailureAsync((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, new string('x', 300_000))));
        await ExpectDataFailureAsync(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, success);
        }, TimeSpan.FromMilliseconds(10));
        using (var cancelled = new CancellationTokenSource())
        using (var cancelledClient = new HttpClient(new Handler(async (_, cancellationToken) =>
               {
                   await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                   return Json(HttpStatusCode.OK, success);
               })))
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new DataApiClient(cancelledClient, Options(), metrics).GetMeAsync(
                    subject, token, cancelled.Token));
        }
        await ExpectDataFailureAsync((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            success.Replace(subject.ToString(), Guid.NewGuid().ToString(),
                StringComparison.Ordinal))));
        await ExpectDataFailureAsync((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)));

        var limiter = new FixedWindowLimiter(_time);
        var middleware = new ApiRateLimitingMiddleware(_ => Task.CompletedTask);
        for (var request = 0; request < 31; request++)
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Request.Path = "/v1/me";
            context.Response.Body = new MemoryStream();
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("sub", subject.ToString())], "test"));
            await middleware.InvokeAsync(context, limiter, metrics);
        }

        var counters = measurements.Where(value =>
            value.Name == "data_api_requests_total").ToArray();
        var durations = measurements.Where(value =>
            value.Name == "data_api_duration_seconds").ToArray();
        var rateLimits = measurements.Where(value =>
            value.Name == "rate_limit_rejections_total").ToArray();
        Assert.Equal(20, counters.Length);
        Assert.Equal(20, durations.Length);
        Assert.All(counters, value =>
        {
            Assert.Equal(1, value.Value);
            Assert.Matches(
                "^operation=(entitlement|profile)\\|result=(success|4xx|5xx|timeout|" +
                "cancelled|subject_mismatch|redirect_rejected)$", value.Labels);
        });
        Assert.All(durations, value =>
        {
            Assert.True(value.Value >= 0);
            Assert.Matches(
                "^operation=(entitlement|profile)\\|result=(success|4xx|5xx|timeout|" +
                "cancelled|subject_mismatch|redirect_rejected)$", value.Labels);
        });
        Assert.Equal("4xx,5xx,cancelled,redirect_rejected,subject_mismatch,success,timeout",
            string.Join(',', counters.Select(value =>
                    value.Labels.Split("result=", StringSplitOptions.None)[1])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)));
        Assert.Equal(8, counters.Count(value =>
            value.Labels.EndsWith("result=5xx", StringComparison.Ordinal)));
        var rejection = Assert.Single(rateLimits);
        Assert.Equal(1, rejection.Value);
        Assert.Equal("policy=authenticated_api", rejection.Labels);
        Assert.DoesNotContain(measurements, value =>
            value.Labels.Contains(token, StringComparison.Ordinal) ||
            value.Labels.Contains(subject.ToString(), StringComparison.Ordinal) ||
            value.Labels.Contains("private", StringComparison.Ordinal) ||
            value.Labels.Contains('@', StringComparison.Ordinal) ||
            value.Labels.Contains("http", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CacheAgeAndRetryCooldownGaugesRespectExactBoundaries()
    {
        var cacheAges = new List<double>();
        var cooldowns = new List<double>();
        using var metrics = new ApiMetrics();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.JwksRefresh.Meter) &&
                instrument.Name is "jwks_cache_age_seconds" or
                    "jwks_retry_cooldown_remaining_seconds")
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "jwks_cache_age_seconds") cacheAges.Add(value);
            else cooldowns.Add(value);
        });
        listener.Start();
        var calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
            Task.FromResult(Interlocked.Increment(ref calls) == 1
                ? Json(HttpStatusCode.OK, RsaJwks("known"))
                : Json(HttpStatusCode.ServiceUnavailable, "{}"))));
        using var coordinator = new JwksCoordinator(client, Options(), _time, metrics);
        listener.RecordObservableInstruments();
        Assert.Empty(cacheAges);
        using var known = await coordinator.FindAsync("known", TestContext.Current.CancellationToken);
        listener.RecordObservableInstruments();
        _time.Advance(TimeSpan.FromSeconds(30));
        listener.RecordObservableInstruments();
        Assert.Equal(0, cacheAges[0], 3);
        Assert.Equal(30, cacheAges[1], 3);
        _time.Advance(TimeSpan.FromSeconds(570));
        Assert.False(coordinator.IsReady);
        Assert.Null(await coordinator.FindAsync("missing", TestContext.Current.CancellationToken));
        var cacheMeasurements = cacheAges.Count;
        listener.RecordObservableInstruments();
        Assert.Equal(cacheMeasurements, cacheAges.Count);
        Assert.Contains(cooldowns, value => Math.Abs(value - 60) < 0.001);
        _time.Advance(TimeSpan.FromSeconds(30));
        listener.RecordObservableInstruments();
        Assert.Contains(cooldowns, value => Math.Abs(value - 30) < 0.001);
        _time.Advance(TimeSpan.FromSeconds(30));
        listener.RecordObservableInstruments();
        Assert.Contains(cooldowns, value => Math.Abs(value) < 0.001);
    }

    [Theory]
    [InlineData("http://127.0.0.1:54321/")]
    [InlineData("https://localhost:54321/")]
    [InlineData("https://127.0.0.1:54322/")]
    [InlineData("https://user@project.supabase.co/")]
    public void InvalidOriginsAreRejected(string origin)
    {
        var options = Options() with { Origin = new(origin), Issuer = origin.TrimEnd('/') + "/auth/v1" };
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("service_role_secret")]
    [InlineData("sb_secret_secret")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.payload.signature")]
    [InlineData("anon")]
    [InlineData("sb_publishable_service_role")]
    [InlineData("sb_publishable_sb_secret_value")]
    public void NonPublishableKeysAreRejected(string key)
    {
        Assert.Throws<InvalidOperationException>(() => (Options() with { PublishableKey = key }).Validate());
    }

    [Theory]
    [InlineData("https://project.supabase.co/auth/v1?leak=1", "/auth/v1/.well-known/jwks.json")]
    [InlineData("https://project.supabase.co/auth/v1/", "/auth/v1/.well-known/jwks.json")]
    [InlineData("https://project.supabase.co/auth/v1", "/other/jwks.json")]
    public void InvalidIssuerOrJwksLocationIsRejected(string issuer, string path)
    {
        Assert.Throws<InvalidOperationException>(() =>
            (Options() with { Issuer = issuer, JwksPath = path }).Validate());
    }

    [Fact]
    public void NormativeRootRelativeJwksPathIsAccepted()
    {
        (Options() with { JwksPath = "/auth/v1/.well-known/jwks.json" }).Validate();
    }

    [Theory]
    [InlineData("file:///auth/v1/.well-known/jwks.json")]
    [InlineData("https://project.supabase.co/auth/v1/.well-known/jwks.json")]
    [InlineData("//project.supabase.co/auth/v1/.well-known/jwks.json")]
    [InlineData("/auth/v1/../v1/.well-known/jwks.json")]
    [InlineData("/auth/v1/.well-known/jwks.json?key=value")]
    [InlineData("/auth/v1/.well-known/jwks.json#fragment")]
    [InlineData(@"\auth\v1\.well-known\jwks.json")]
    public void NonNormativeJwksPathsAreRejected(string path)
    {
        Assert.Throws<InvalidOperationException>(() =>
            (Options() with { JwksPath = path }).Validate());
    }

    private JwksCoordinator Coordinator(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        var client = new HttpClient(new Handler(send));
        return new(client, Options(), _time, _metrics);
    }
    private static SupabaseApiOptions Options() => new()
    {
        Origin = new("https://project.supabase.co/"),
        PublishableKey = "sb_publishable_test",
        Issuer = "https://project.supabase.co/auth/v1",
        Audience = "authenticated"
    };
    private string RsaJwks(params string[] kids)
    {
        var p = _rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = kids.Select(kid =>
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid,
                    n = Encode(p.Modulus!),
                    e = Encode(p.Exponent!)
                }).ToArray()
        });
    }
    private string EcJwks(string kid)
    {
        var p = _ec.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "EC", use = "sig", alg = "ES256", crv = "P-256",
                kid, x = Encode(p.Q.X!), y = Encode(p.Q.Y!) } }
        });
    }
    private string Token(string algorithm, string kid, Guid? subject, DateTimeOffset expiration,
        string? issuer = null, string? audience = null, DateTimeOffset? notBefore = null,
        bool invalidNotBefore = false)
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm, typ = "JWT", kid }));
        var claims = new Dictionary<string, object?>
        {
            ["iss"] = issuer ?? Options().Issuer,
            ["aud"] = audience ?? Options().Audience,
            ["exp"] = expiration.ToUnixTimeSeconds(),
            ["sub"] = subject?.ToString()
        };
        if (invalidNotBefore) claims["nbf"] = "invalid";
        else if (notBefore is not null) claims["nbf"] = notBefore.Value.ToUnixTimeSeconds();
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var input = Encoding.ASCII.GetBytes(header + "." + payload);
        var signature = algorithm switch
        {
            "RS256" => _rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            "ES256" => _ec.SignData(input, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            _ => new byte[32]
        };
        return header + "." + payload + "." + Encode(signature);
    }
    private static string CorruptSignature(string token)
    {
        var parts = token.Split('.');
        var padded = parts[2].Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        var signature = Convert.FromBase64String(padded);
        signature[0] ^= 0x80;
        return parts[0] + "." + parts[1] + "." + Encode(signature);
    }
    private static string MetricLabels(
        ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        string.Join("|", tags.ToArray().OrderBy(static tag => tag.Key)
            .Select(static tag => $"{tag.Key}={tag.Value}"));
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(1, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
    public void Dispose() { _rsa.Dispose(); _ec.Dispose(); _metrics.Dispose(); }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
    private sealed class ThrowingContent(Exception exception) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(exception);
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<FakeTimer> _timers = [];
        private long _timestamp;
        private DateTimeOffset _utcNow = utcNow;
        public override long TimestampFrequency => 1_000_000;
        public override long GetTimestamp() => Volatile.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public int TimerCount { get { lock (_sync) return _timers.Count; } }
        public void Advance(TimeSpan value)
        {
            _utcNow += value;
            Interlocked.Add(ref _timestamp, (long)(value.TotalSeconds * TimestampFrequency));
            FakeTimer[] timers;
            lock (_sync) timers = _timers.ToArray();
            foreach (var timer in timers) timer.FireDue(_utcNow);
        }
        public void AdvanceWithoutFiringTimers(TimeSpan value)
        {
            _utcNow += value;
            Interlocked.Add(ref _timestamp, (long)(value.TotalSeconds * TimestampFrequency));
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, callback, state, _utcNow + dueTime, period);
            lock (_sync) _timers.Add(timer);
            return timer;
        }
        private void Remove(FakeTimer timer) { lock (_sync) _timers.Remove(timer); }
        private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state,
            DateTimeOffset dueAt, TimeSpan period) : ITimer
        {
            private bool _disposed;
            public void FireDue(DateTimeOffset now)
            {
                if (_disposed || now < dueAt) return;
                callback(state);
                if (period == Timeout.InfiniteTimeSpan) Dispose();
                else dueAt = now + period;
            }
            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                if (_disposed) return false;
                dueAt = owner._utcNow + dueTime;
                period = newPeriod;
                return true;
            }
            public void Dispose() { _disposed = true; owner.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
