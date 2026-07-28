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
    HashSet<CommercialFeature> Features)
{
    public bool IsFeatureEnabled(CommercialFeature feature) =>
        IsFeatureEnabled(feature, DateTimeOffset.UtcNow);

    public bool IsFeatureEnabled(CommercialFeature feature, DateTimeOffset evaluatedAt) =>
        Enum.IsDefined(feature) &&
        IsAuthenticated && Plan == CommercialPlan.Pro &&
        IsStatusEligible(evaluatedAt) &&
        Features.Contains(feature);

    private bool IsStatusEligible(DateTimeOffset evaluatedAt) =>
        SubscriptionStatus switch
        {
            SubscriptionStatus.Trialing
                or SubscriptionStatus.Active
                or SubscriptionStatus.PastDue =>
                EntitlementExpiresAt is not null
                && EntitlementExpiresAt > evaluatedAt,
            SubscriptionStatus.Canceled =>
                EntitlementExpiresAt is not null
                && EntitlementExpiresAt > evaluatedAt,
            _ => false,
        };
}
