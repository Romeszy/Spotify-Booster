using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SpotifyBooster.Contracts.Api;

namespace SpotifyBooster.Api;

public sealed class DataApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly SupabaseApiOptions _options;
    private readonly ApiMetrics _metrics;

    public DataApiClient(HttpClient http, SupabaseApiOptions options, ApiMetrics metrics)
    {
        _http = http; _options = options; _metrics = metrics;
        options.Validate();
        if (_http.DefaultRequestHeaders.Authorization is not null)
            throw new InvalidOperationException("Authorization must be request-scoped.");
    }

    public async Task<CurrentUserContract> GetMeAsync(Guid subject, string accessToken, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = "5xx";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(_options.Origin, "/rest/v1/rpc/spotify_booster_me_v1"));
            request.Headers.TryAddWithoutValidation("apikey", _options.PublishableKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new("application/json"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400) { result = "redirect_rejected"; throw new DataApiException("Data API redirect rejected."); }
            if (!response.IsSuccessStatusCode)
            {
                result = (int)response.StatusCode < 500 ? "4xx" : "5xx";
                throw new DataApiException("Data API request failed.");
            }
            var bytes = await BoundedContent.ReadAsync(response.Content, 256 * 1024, timeout.Token).ConfigureAwait(false);
            var wire = JsonSerializer.Deserialize<MeWire>(bytes, JsonOptions)
                ?? throw new DataApiException("Data API response invalid.");
            if (wire.UserId != subject)
            {
                result = "subject_mismatch";
                throw new DataApiException("Data API subject mismatch.");
            }
            result = "success";
            return new(wire.UserId, wire.DisplayName, wire.Plan, wire.Status, wire.Revision,
                wire.Features ?? Array.Empty<string>(), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { result = "cancelled"; throw; }
        catch (OperationCanceledException) { result = "timeout"; throw new DataApiException("Data API timeout."); }
        finally
        {
            _metrics.DataApiRequests.Add(1, new("operation", "profile"), new("result", result));
            _metrics.DataApiDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new("operation", "profile"), new("result", result));
        }
    }

    private sealed record MeWire(Guid UserId, string? DisplayName, string Plan, string Status, long Revision, string[]? Features);
}

public sealed class DataApiException(string message) : Exception(message);
