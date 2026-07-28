using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SpotifyBooster.Api;

public sealed record JwtValidationResult(bool Succeeded, Guid Subject, ClaimsPrincipal? Principal, string Reason)
{
    public static JwtValidationResult Reject(string reason) => new(false, default, null, reason);
}

public sealed class SupabaseJwtValidator
{
    private readonly JwksCoordinator _jwks;
    private readonly SupabaseApiOptions _options;
    private readonly TimeProvider _time;
    private readonly ApiMetrics _metrics;

    public SupabaseJwtValidator(JwksCoordinator jwks, SupabaseApiOptions options, TimeProvider time, ApiMetrics metrics)
    {
        _jwks = jwks; _options = options; _time = time; _metrics = metrics;
    }

    public async Task<JwtValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var result = await ValidateCoreAsync(token, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) _metrics.JwtRejections.Add(1, new KeyValuePair<string, object?>[] { new("reason", result.Reason) });
        return result;
    }

    private async Task<JwtValidationResult> ValidateCoreAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64 * 1024)
            return JwtValidationResult.Reject("malformed");
        var parts = token.Split('.');
        if (parts.Length != 3) return JwtValidationResult.Reject("malformed");
        try
        {
            using var headerDocument = JsonDocument.Parse(Base64Url(parts[0]));
            var header = headerDocument.RootElement;
            var algorithm = String(header, "alg");
            if (algorithm is not ("RS256" or "ES256")) return JwtValidationResult.Reject("invalid_algorithm");
            var kid = String(header, "kid");
            var key = await _jwks.FindAsync(kid, cancellationToken).ConfigureAwait(false);
            if (key is null) return JwtValidationResult.Reject("unknown_kid");
            if (key.Algorithm != algorithm) return JwtValidationResult.Reject("invalid_algorithm");
            var data = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            if (!key.Verify(data, Base64Url(parts[2]))) return JwtValidationResult.Reject("invalid_signature");

            using var payloadDocument = JsonDocument.Parse(Base64Url(parts[1]));
            var payload = payloadDocument.RootElement;
            if (String(payload, "iss") != _options.Issuer) return JwtValidationResult.Reject("issuer");
            if (!HasAudience(payload, _options.Audience)) return JwtValidationResult.Reject("audience");
            var now = _time.GetUtcNow().ToUnixTimeSeconds();
            const long skew = 60;
            if (!payload.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expires) || now - skew >= expires)
                return JwtValidationResult.Reject("expired");
            if (payload.TryGetProperty("nbf", out var nbf) && nbf.TryGetInt64(out var notBefore) && now + skew < notBefore)
                return JwtValidationResult.Reject("not_yet_valid");
            if (!payload.TryGetProperty("sub", out var sub) || sub.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(sub.GetString(), out var subject))
                return JwtValidationResult.Reject("missing_subject");
            var identity = new ClaimsIdentity("SupabaseJwt", ClaimTypes.NameIdentifier, ClaimTypes.Role);
            identity.AddClaim(new("sub", subject.ToString()));
            identity.AddClaim(new(ClaimTypes.NameIdentifier, subject.ToString()));
            return new(true, subject, new(identity), string.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
        {
            return JwtValidationResult.Reject("malformed");
        }
    }

    private static bool HasAudience(JsonElement payload, string expected)
    {
        if (!payload.TryGetProperty("aud", out var aud)) return false;
        return aud.ValueKind switch
        {
            JsonValueKind.String => aud.GetString() == expected,
            JsonValueKind.Array => aud.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == expected),
            _ => false
        };
    }
    private static string String(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()! : throw new InvalidDataException();
    internal static byte[] Base64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}

public sealed class SupabaseAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SupabaseJwtValidator validator)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var result = await validator.ValidateAsync(authorization[7..].Trim(), context.RequestAborted).ConfigureAwait(false);
            if (result.Succeeded) context.User = result.Principal!;
        }
        await next(context).ConfigureAwait(false);
    }
}
