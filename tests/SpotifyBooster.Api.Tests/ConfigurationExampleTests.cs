using System.Text.Json;

namespace SpotifyBooster.Api.Tests;

public sealed class ConfigurationExampleTests
{
    [Fact]
    public void ApiConfigurationExamplesAreSmallParseableAndSecretFree()
    {
        var root = RepositoryRoot();
        var envPath = Path.Combine(root, ".env.example");
        var jsonPath = Path.Combine(root, "appsettings.Development.example.json");
        var env = File.ReadAllText(envPath);
        var json = File.ReadAllText(jsonPath);

        Assert.InRange(new FileInfo(envPath).Length, 1, 4096);
        Assert.InRange(new FileInfo(jsonPath).Length, 1, 4096);

        var variables = env.Split(['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        Assert.Equal(
            ["PUBLIC_APP_URL", "SUPABASE_PUBLISHABLE_KEY", "SUPABASE_URL"],
            variables.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("https://127.0.0.1:54321", variables["SUPABASE_URL"]);
        Assert.Equal("https://127.0.0.1:3000", variables["PUBLIC_APP_URL"]);
        Assert.True(SupabaseApiOptions.IsPublishableKey(
            variables["SUPABASE_PUBLISHABLE_KEY"]));

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        var supabase = document.RootElement.GetProperty("Supabase");
        Assert.Equal(
            ["Audience", "Issuer", "JwksPath", "Origin", "PublishableKey"],
            supabase.EnumerateObject().Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        var options = new SupabaseApiOptions
        {
            Origin = new Uri(supabase.GetProperty("Origin").GetString()!,
                UriKind.Absolute),
            PublishableKey = supabase.GetProperty("PublishableKey").GetString()!,
            Issuer = supabase.GetProperty("Issuer").GetString()!,
            Audience = supabase.GetProperty("Audience").GetString()!,
            JwksPath = supabase.GetProperty("JwksPath").GetString()!
        };
        options.Validate();

        var combined = env + json;
        foreach (var prohibited in new[]
        {
            "SUPABASE_SERVICE_ROLE_KEY=", "STRIPE_SECRET_KEY=",
            "STRIPE_WEBHOOK_SECRET=", "SUPABASE_ANON_KEY=",
            "\"d\":", "BEGIN PRIVATE KEY"
        })
            Assert.DoesNotContain(prohibited, combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RealLocalConfigurationAndSupabasePrivateStateAreIgnored()
    {
        var ignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));
        foreach (var pattern in new[]
        {
            ".env", ".env.local", ".env.*.local",
            "appsettings.Development.json",
            "src/SpotifyBooster.Api/appsettings.Development.json",
            "backend/supabase/.local/"
        })
            Assert.Contains(pattern, ignore, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
                   "SpotifyBooster.sln")))
            directory = directory.Parent;
        return directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root not found.");
    }
}
