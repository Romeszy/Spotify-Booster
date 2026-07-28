namespace SpotifyBooster.Api;

public sealed record SupabaseApiOptions
{
    public required Uri Origin { get; init; }
    public required string PublishableKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public string JwksPath { get; init; } = "/auth/v1/.well-known/jwks.json";

    public void Validate()
    {
        ValidateOrigin(Origin);
        if (string.IsNullOrWhiteSpace(PublishableKey) ||
            PublishableKey.StartsWith("service_role", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A publishable key is required.");
        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttps || issuer.GetLeftPart(UriPartial.Authority) != Origin.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("Issuer must use the configured HTTPS origin.");
        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Audience is required.");
        if (!JwksPath.StartsWith('/', StringComparison.Ordinal) ||
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
    }
}
