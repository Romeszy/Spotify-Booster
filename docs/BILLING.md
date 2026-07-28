# Billing

## Statut

Le billing n’est pas implémenté en Phase A. Le dépôt ne contient ni SDK Stripe,
ni Checkout, ni Portal, ni webhook, ni Price ID réel, ni endpoint d’écriture
d’entitlement.

La présence des contrats `CommercialPlan`, `SubscriptionStatus` et
`EntitlementContract` ne crée aucun abonnement Pro.

## Contrat Phase B

Le client demandera uniquement le plan symbolique
`spotify_booster_pro_monthly`. Le serveur le mappera vers
`STRIPE_PRO_MONTHLY_PRICE_ID`; le client ne pourra jamais fournir un montant ou
un Price ID arbitraire.

La Phase B devra ajouter :

- Checkout et Portal authentifiés ;
- vérification que le customer appartient à l’utilisateur ;
- webhooks validés sur le corps brut avec la signature officielle ;
- journal d’événements idempotent et transactionnel ;
- tolérance aux retries et à l’ordre des événements ;
- writer serveur privilégié, jamais exposé au desktop.

## Source de vérité

Stripe sera la source de vérité commerciale. Le cache local et l’interface ne
pourront pas accorder Pro.

| État Stripe futur | Entitlement effectif prévu |
| --- | --- |
| `active`, `trialing` | Pro |
| `past_due` | Pro pendant une grâce configurée |
| `canceled` | Pro jusqu’à la fin de période si applicable |
| `unpaid`, `incomplete`, `incomplete_expired`, `paused`, `refunded`, `revoked` | Free |

Cette matrice reste un contrat de conception jusqu’à son implémentation et ses
tests Phase B.

## Secrets futurs

`STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` et le Price ID configuré restent
serveur uniquement. Ils sont interdits dans WPF, Git, les exemples de
configuration et les réponses health.

## Rollback futur

Un rollback Billing devra arrêter les nouveaux Checkout, continuer à accepter
et journaliser les webhooks, réconcilier Stripe vers PostgreSQL, et ne jamais
restaurer Pro depuis un cache client. La procédure exacte dépendra de
l’architecture Phase B.
