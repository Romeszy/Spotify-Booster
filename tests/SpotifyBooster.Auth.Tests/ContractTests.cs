using System.Text.Json;
using SpotifyBooster.Contracts.Api;
using SpotifyBooster.Contracts.Auth;
using SpotifyBooster.Contracts.Entitlements;
using SpotifyBooster.Contracts.Updates;
using SpotifyBooster.Core.Abstractions;

namespace SpotifyBooster.Auth.Tests;

public sealed class ContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SessionDiagnosticsAndJsonNeverExposeOpaqueTokens()
    {
        var session = new AuthSessionContract(new(Guid.NewGuid(), "u@example.test", true),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), "access-secret", "refresh-secret");
        var text = session.ToString();
        var json = JsonSerializer.Serialize(session);
        Assert.DoesNotContain("access-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("access-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordRequestDiagnosticsAreAlwaysRedacted()
    {
        const string password = "secret-password-value";
        object[] requests =
        [
            new SignUpRequest("u@example.test", password),
            new SignInRequest("u@example.test", password),
            new UpdatePasswordRequest(password)
        ];
        foreach (var request in requests)
        {
            Assert.DoesNotContain(password, request.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(password, JsonSerializer.Serialize(request), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trialing, true)]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.PastDue, true)]
    [InlineData(SubscriptionStatus.Unpaid, false)]
    [InlineData(SubscriptionStatus.Incomplete, false)]
    [InlineData(SubscriptionStatus.IncompleteExpired, false)]
    [InlineData(SubscriptionStatus.Paused, false)]
    [InlineData(SubscriptionStatus.Refunded, false)]
    [InlineData(SubscriptionStatus.Revoked, false)]
    public void EntitlementPolicyIsCentralized(SubscriptionStatus status, bool expected)
    {
        var entitlement = new EntitlementContract(true, CommercialPlan.Pro, status,
            DateTimeOffset.UtcNow.AddMinutes(5), false,
            new HashSet<CommercialFeature> { CommercialFeature.ExpertMode });
        Assert.Equal(expected, entitlement.IsFeatureEnabled(CommercialFeature.ExpertMode));
        Assert.False(entitlement.IsFeatureEnabled(CommercialFeature.FuturePremiumFeatures));
    }

    [Fact]
    public void CanceledEntitlementKeepsProUntilButNotAtExpiration()
    {
        DateTimeOffset expiration = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var entitlement = new EntitlementContract(
            true,
            CommercialPlan.Pro,
            SubscriptionStatus.Canceled,
            expiration,
            false,
            new HashSet<CommercialFeature> { CommercialFeature.ExpertMode });

        Assert.True(entitlement.IsFeatureEnabled(
            CommercialFeature.ExpertMode,
            expiration.AddTicks(-1)));
        Assert.False(entitlement.IsFeatureEnabled(
            CommercialFeature.ExpertMode,
            expiration));
        Assert.False(entitlement.IsFeatureEnabled(
            CommercialFeature.ExpertMode,
            expiration.AddTicks(1)));
    }

    [Fact]
    public void CanceledEntitlementWithoutPeriodEndNeverGrantsPro()
    {
        var entitlement = new EntitlementContract(
            true,
            CommercialPlan.Pro,
            SubscriptionStatus.Canceled,
            null,
            false,
            new HashSet<CommercialFeature> { CommercialFeature.ExpertMode });

        Assert.False(entitlement.IsFeatureEnabled(
            CommercialFeature.ExpertMode,
            DateTimeOffset.Parse("2026-07-28T12:00:00Z")));
    }

    [Fact]
    public void FreeAndExpiredProStayDisabled()
    {
        var features = new HashSet<CommercialFeature> { CommercialFeature.ExpertMode };
        Assert.False(new EntitlementContract(true, CommercialPlan.Free, SubscriptionStatus.Active,
            null, false, features).IsFeatureEnabled(CommercialFeature.ExpertMode));
        Assert.False(new EntitlementContract(true, CommercialPlan.Pro, SubscriptionStatus.Active,
            DateTimeOffset.UtcNow.AddMinutes(-1), false, features).IsFeatureEnabled(CommercialFeature.ExpertMode));
    }

    [Fact]
    public void UnknownSubscriptionAndFeatureValuesFailClosed()
    {
        var entitlement = new EntitlementContract(
            true,
            CommercialPlan.Pro,
            (SubscriptionStatus)int.MaxValue,
            DateTimeOffset.MaxValue,
            true,
            new HashSet<CommercialFeature> { (CommercialFeature)int.MaxValue });

        Assert.False(entitlement.IsFeatureEnabled(
            (CommercialFeature)int.MaxValue,
            DateTimeOffset.UnixEpoch));
        Assert.False(entitlement.IsFeatureEnabled(
            CommercialFeature.ExpertMode,
            DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ContractEnumMembersRemainExact()
    {
        Assert.Equal(
            [
                "Free",
                "Pro",
            ],
            Enum.GetNames<CommercialPlan>());
        Assert.Equal(
            [
                "Trialing",
                "Active",
                "PastDue",
                "Canceled",
                "Unpaid",
                "Incomplete",
                "IncompleteExpired",
                "Paused",
                "Refunded",
                "Revoked",
            ],
            Enum.GetNames<SubscriptionStatus>());
        Assert.Equal(10, Enum.GetValues<CommercialFeature>().Length);
    }

    [Fact]
    public void WirePropertyNamesAndPayloadShapeRemainStable()
    {
        Guid userId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var currentUser = new CurrentUserContract(
            userId,
            "Display",
            "pro",
            "active",
            7,
            ["expert_mode"],
            DateTimeOffset.Parse("2026-07-28T12:00:00Z"));
        using JsonDocument currentUserJson = JsonDocument.Parse(
            JsonSerializer.Serialize(currentUser, WebJson));

        Assert.Equal(
            [
                "userId",
                "displayName",
                "plan",
                "status",
                "revision",
                "features",
                "serverTime",
            ],
            currentUserJson.RootElement.EnumerateObject()
                .Select(static property => property.Name)
                .ToArray());

        var error = new ApiErrorContract("invalid_request", "Request rejected.", "trace-1");
        using JsonDocument errorJson = JsonDocument.Parse(JsonSerializer.Serialize(error, WebJson));
        Assert.Equal(
            ["code", "message", "traceId"],
            errorJson.RootElement.EnumerateObject()
                .Select(static property => property.Name)
                .ToArray());

        var update = new UpdateContract(
            new Version(3, 1, 4),
            new Uri("https://example.test/download"),
            true);
        using JsonDocument updateJson = JsonDocument.Parse(JsonSerializer.Serialize(update, WebJson));
        Assert.Equal(
            ["version", "downloadPage", "mandatory"],
            updateJson.RootElement.EnumerateObject()
                .Select(static property => property.Name)
                .ToArray());
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(currentUser, WebJson).Length < 256 * 1024);
    }

    [Fact]
    public void AuthApiEntitlementAndUpdateContractsRoundTrip()
    {
        var authUser = new AuthUserContract(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "user@example.test",
            true);
        Assert.Equal(authUser, RoundTrip(authUser));

        var error = new ApiErrorContract("invalid_request", "Request rejected.", "trace-1");
        Assert.Equal(error, RoundTrip(error));

        var entitlement = new EntitlementContract(
            true,
            CommercialPlan.Pro,
            SubscriptionStatus.Active,
            DateTimeOffset.Parse("2026-07-29T12:00:00Z"),
            false,
            new HashSet<CommercialFeature>
            {
                CommercialFeature.AdvancedGain,
                CommercialFeature.ExpertMode,
            });
        EntitlementContract entitlementCopy = RoundTrip(entitlement);
        Assert.Equal(entitlement with { Features = entitlementCopy.Features }, entitlementCopy);
        Assert.Equal(entitlement.Features, entitlementCopy.Features);

        var update = new UpdateContract(
            new Version(3, 1, 4),
            new Uri("https://example.test/download"),
            true);
        Assert.Equal(update, RoundTrip(update));
    }

    [Fact]
    public void CommercialServiceInterfacesLiveInCoreAbstractions()
    {
        Type[] interfaces =
        [
            typeof(IAuthenticationService),
            typeof(IEntitlementService),
            typeof(IUpdateService),
        ];

        Assert.All(interfaces, service =>
        {
            Assert.Equal("SpotifyBooster.Core", service.Assembly.GetName().Name);
            Assert.Equal("SpotifyBooster.Core.Abstractions", service.Namespace);
        });
        Assert.DoesNotContain(
            typeof(AuthSessionContract).Assembly.GetTypes(),
            static type => type.IsInterface
                && type.Name is nameof(IAuthenticationService)
                    or nameof(IEntitlementService)
                    or nameof(IUpdateService));
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(
            JsonSerializer.Serialize(value, WebJson),
            WebJson)
        ?? throw new InvalidOperationException("Contract round-trip returned null.");
}
