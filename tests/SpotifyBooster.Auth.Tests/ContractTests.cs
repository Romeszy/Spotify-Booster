using System.Text.Json;
using SpotifyBooster.Contracts.Auth;
using SpotifyBooster.Contracts.Entitlements;

namespace SpotifyBooster.Auth.Tests;

public sealed class ContractTests
{
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
    [InlineData(SubscriptionStatus.Canceled, false)]
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
    public void FreeAndExpiredProStayDisabled()
    {
        var features = new HashSet<CommercialFeature> { CommercialFeature.ExpertMode };
        Assert.False(new EntitlementContract(true, CommercialPlan.Free, SubscriptionStatus.Active,
            null, false, features).IsFeatureEnabled(CommercialFeature.ExpertMode));
        Assert.False(new EntitlementContract(true, CommercialPlan.Pro, SubscriptionStatus.Active,
            DateTimeOffset.UtcNow.AddMinutes(-1), false, features).IsFeatureEnabled(CommercialFeature.ExpertMode));
    }
}
