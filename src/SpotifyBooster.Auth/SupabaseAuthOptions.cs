using System.Buffers;

namespace SpotifyBooster.Auth;

public sealed record SupabaseAuthOptions
{
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(30);
    public const int MaximumAllowedResponseBytes = 256 * 1024;

    public required Uri BaseUri { get; init; }
    public required string PublishableKey { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaximumResponseBytes { get; init; } = MaximumAllowedResponseBytes;

    public void Validate()
    {
        if (BaseUri is null || !BaseUri.IsAbsoluteUri || BaseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(BaseUri.UserInfo) || !string.IsNullOrEmpty(BaseUri.Query) ||
            !string.IsNullOrEmpty(BaseUri.Fragment) || BaseUri.AbsolutePath != "/" ||
            (BaseUri.IsLoopback
                ? BaseUri.Host != "127.0.0.1" || BaseUri.Port != 54321
                : BaseUri.Port != 443))
            throw new InvalidOperationException("Supabase Auth requires a clean HTTPS base URI.");
        if (!PublishableKeyPolicy.IsPublishable(PublishableKey))
            throw new InvalidOperationException("A publishable key is required.");
        if (Timeout <= TimeSpan.Zero || Timeout > MaximumTimeout)
            throw new InvalidOperationException("Supabase Auth requires a finite bounded timeout.");
        if (MaximumResponseBytes is <= 0 or > MaximumAllowedResponseBytes)
            throw new InvalidOperationException("Supabase Auth requires a bounded response limit.");
    }
}

internal static class PublishableKeyPolicy
{
    private const string Prefix = "sb_publishable_";
    private static readonly SearchValues<char> AllowedCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    public static bool IsPublishable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal) ||
            value.Length <= Prefix.Length || value.Length > 512 ||
            value.Contains("service_role", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("sb_secret", StringComparison.OrdinalIgnoreCase))
            return false;
        return value.AsSpan(Prefix.Length).IndexOfAnyExcept(AllowedCharacters) < 0;
    }
}
