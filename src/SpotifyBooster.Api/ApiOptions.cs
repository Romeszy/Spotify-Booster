using System.Buffers;

namespace SpotifyBooster.Api;

public sealed record SupabaseApiOptions
{
    private static readonly SearchValues<char> PublishableKeyCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");
    public required Uri Origin { get; init; }
    public required string PublishableKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public string JwksPath { get; init; } = "/auth/v1/.well-known/jwks.json";

    public void Validate()
    {
        ValidateOrigin(Origin);
        if (!IsPublishableKey(PublishableKey))
            throw new InvalidOperationException("A publishable key is required.");
        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttps ||
            issuer.GetLeftPart(UriPartial.Authority) != Origin.GetLeftPart(UriPartial.Authority) ||
            !string.IsNullOrEmpty(issuer.UserInfo) || !string.IsNullOrEmpty(issuer.Query) ||
            !string.IsNullOrEmpty(issuer.Fragment) || issuer.AbsolutePath != "/auth/v1")
            throw new InvalidOperationException("Issuer must use the configured HTTPS origin.");
        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Audience is required.");
        if (JwksPath != "/auth/v1/.well-known/jwks.json" ||
            Uri.TryCreate(JwksPath, UriKind.Absolute, out _) || JwksPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("JWKS path must be a fixed relative path.");
    }

    public static void ValidateOrigin(Uri origin)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/")
            throw new InvalidOperationException("Supabase requires a clean HTTPS origin.");

        if (origin.IsLoopback && !(origin.Host == "127.0.0.1" && origin.Port == 54321))
            throw new InvalidOperationException("Only the normative TLS loopback origin is accepted.");
        if (!origin.IsLoopback && origin.Port != 443)
            throw new InvalidOperationException("Hosted Supabase origins must use the default HTTPS port.");
    }

    public static bool IsPublishableKey(string? value)
    {
        const string prefix = "sb_publishable_";
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            value.Length <= prefix.Length || value.Length > 512 ||
            value.Contains("service_role", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("sb_secret", StringComparison.OrdinalIgnoreCase))
            return false;
        return value.AsSpan(prefix.Length).IndexOfAnyExcept(PublishableKeyCharacters) < 0;
    }
}
