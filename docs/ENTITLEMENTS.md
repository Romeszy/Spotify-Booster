# Entitlements

## Contrat Phase A

`EntitlementContract` centralise :

- authentification ;
- plan `Free` ou `Pro` ;
- statut d’abonnement ;
- expiration ;
- grâce hors ligne ;
- ensemble de fonctionnalités.

`IsFeatureEnabled` exige un utilisateur authentifié, le plan Pro, un statut
autorisé, une échéance effective non nulle et strictement future, et la présence
de la fonctionnalité. `EntitlementExpiresAt` représente cette échéance
effective, y compris la fin de grâce lorsqu’une grâce bornée a été calculée par
une source autoritaire. `IsOfflineGraceActive` est informatif et ne peut jamais,
à lui seul, prolonger un droit expiré ou dépourvu d’échéance.

Fonctionnalités connues :

`AdvancedGain`, `ExpertMode`, `AdvancedBassBoost`, `Equalizer`,
`CustomProfiles`, `GlobalHotkeys`, `TrayAdvancedControls`, `AutoStart`,
`AdvancedDiagnostics`, `FuturePremiumFeatures`.

## Autorité de la base

`public.entitlements` est créé automatiquement en `free`, `active`,
`revision=1`, sans durée ni grâce. L’utilisateur authentifié peut lire
uniquement sa ligne. Il ne peut pas l’insérer, la modifier ou la supprimer.

Le rôle `spotify_booster_entitlement_writer` est `NOLOGIN` et `NOINHERIT`. Il
modélise un writer serveur futur; aucun endpoint ni credential ne l’expose en
Phase A.

Toute mise à jour serveur future doit :

- conserver `user_id`, `issued_at` et `created_at` ;
- incrémenter `revision` exactement de 1 ;
- utiliser uniquement l’allowlist de dix fonctionnalités ;
- respecter les contraintes de durée et de grâce.

## Ce qui n’est pas opérationnel

- aucune facturation Stripe ;
- aucun service concret `IEntitlementService` branché à WPF ;
- aucune licence JWS signée ;
- aucun cache Pro DPAPI ;
- aucune période hors ligne de 72 heures ;
- aucune table `devices` ;
- aucun feature gating commercial dans l’application desktop.

La matrice Free/Pro reste provisoire jusqu’à la Phase C. Le mode Expert actuel
n’est donc pas une preuve d’un droit Pro et n’est pas protégé commercialement.
