using SpotifyBooster.Contracts.Entitlements;

namespace SpotifyBooster.Core.Abstractions;

public interface IEntitlementService
{
    bool IsAuthenticated { get; }

    CommercialPlan Plan { get; }

    SubscriptionStatus SubscriptionStatus { get; }

    DateTimeOffset? EntitlementExpiresAt { get; }

    bool IsOfflineGraceActive { get; }

    bool IsFeatureEnabled(CommercialFeature feature);

    Task<EntitlementContract> RefreshAsync(CancellationToken cancellationToken = default);

    Task RequireFeatureAsync(
        CommercialFeature feature,
        CancellationToken cancellationToken = default);
}
