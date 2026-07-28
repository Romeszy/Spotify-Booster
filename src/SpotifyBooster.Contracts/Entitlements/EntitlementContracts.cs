namespace SpotifyBooster.Contracts.Entitlements;

public enum CommercialPlan { Free, Pro }
public enum SubscriptionStatus
{
    Trialing, Active, PastDue, Canceled, Unpaid, Incomplete, IncompleteExpired,
    Paused, Refunded, Revoked
}

public enum CommercialFeature
{
    AdvancedGain, ExpertMode, AdvancedBassBoost, Equalizer, CustomProfiles,
    GlobalHotkeys, TrayAdvancedControls, AutoStart, AdvancedDiagnostics,
    FuturePremiumFeatures
}

public sealed record EntitlementContract(
    bool IsAuthenticated,
    CommercialPlan Plan,
    SubscriptionStatus SubscriptionStatus,
    DateTimeOffset? EntitlementExpiresAt,
    bool IsOfflineGraceActive,
    IReadOnlySet<CommercialFeature> Features)
{
    public bool IsFeatureEnabled(CommercialFeature feature) =>
        IsAuthenticated && Plan == CommercialPlan.Pro &&
        SubscriptionStatus is SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue &&
        (EntitlementExpiresAt is null || EntitlementExpiresAt > DateTimeOffset.UtcNow || IsOfflineGraceActive) &&
        Features.Contains(feature);
}

public interface IEntitlementService
{
    ValueTask<EntitlementContract> GetAsync(CancellationToken cancellationToken = default);
    Task<EntitlementContract> RefreshAsync(CancellationToken cancellationToken = default);
    Task RequireFeatureAsync(CommercialFeature feature, CancellationToken cancellationToken = default);
}
