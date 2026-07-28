namespace SpotifyBooster.Auth;

public sealed record SupabaseAuthOptions
{
    public required Uri BaseUri { get; init; }
    public required string PublishableKey { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaximumResponseBytes { get; init; } = 256 * 1024;

    public void Validate()
    {
        if (!BaseUri.IsAbsoluteUri || BaseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(BaseUri.UserInfo) || !string.IsNullOrEmpty(BaseUri.Query) ||
            !string.IsNullOrEmpty(BaseUri.Fragment))
            throw new InvalidOperationException("Supabase Auth requires a clean HTTPS base URI.");
        if (string.IsNullOrWhiteSpace(PublishableKey))
            throw new InvalidOperationException("A publishable key is required.");
    }
}
