# État du projet

Date de mise à jour : 28 juillet 2026.

## Synthèse

Spotify Booster possède un desktop audio WPF testable et une fondation
commerciale Phase A en cours d’intégration finale. Les implémentations API,
Auth, base/RLS et DSP/V3 existent. Aucun environnement commercial ou paiement
réel n’est livré.

## Opérationnel et vérifié par voie

| Voie | Fonctionnalité | Preuve la plus récente |
| --- | --- | --- |
| Desktop/Audio | énumération, route, moteur, métriques, récupération | Audio `27` tests |
| DSP | pipeline modulaire, stabilité numérique, `+75 dB` | DSP `88` tests |
| Core/Infrastructure | validation V3, migration V1/V2, persistance | Core `86` tests |
| API | JWT/JWKS, `/health/*`, `/v1/me`, rate limiting, métriques | API `35` tests |
| Auth | contrats, appels Supabase, refresh/logout, redaction | Auth `27` tests |
| Tooling Node | bootstrap/assets locaux | Node `10` tests |
| PostgreSQL/RLS | schéma, grants, policies, triggers, RPC | pgTAP `85` assertions |

Ces nombres proviennent de voies séparées et ne doivent pas être additionnés.
Le total final, le build global et les audits seront écrits après le gate
d’intégration sur la branche de livraison.

## Implémenté Phase A

- `SpotifyBooster.Contracts`, `SpotifyBooster.Auth` et `SpotifyBooster.Api` ;
- JWT `RS256`/`ES256`, cache JWKS 10 minutes, gates 60 secondes et fail-closed ;
- endpoints `GET /health/live`, `GET /health/ready`, `GET /v1/me` ;
- Data API avec bearer par requête et vérification du subject ;
- migrations `profiles`, `entitlements`, triggers, RLS et RPC health ;
- Supabase CLI locale exacte `2.109.1`, TLS loopback et clés ES256 locales ;
- schéma de réglages V3 ;
- RawExpert fixe `0..+75 dB` par pas de `0,5 dB` ;
- suppression du clamp DSP caché `±64` et assainissement NaN/Inf.

## Fonctionne seulement localement ou comme contrat

- Supabase est une stack de développement Docker, pas un déploiement ;
- l’adaptateur Auth n’est pas branché aux vues WPF ;
- les sessions ne sont pas persistées ;
- les contrats entitlement/update n’ont pas de service desktop concret ;
- le rôle writer d’entitlements est `NOLOGIN`, sans endpoint ;
- la RPC health teste seulement un chemin Data API constant.

## Non implémenté

- Stripe, Checkout, Portal, webhooks et abonnement Pro réel ;
- feature gating Free/Pro ;
- vues WPF Welcome/SignIn/Account ;
- stockage DPAPI et licence hors ligne signée ;
- table devices et révocation de device ;
- updater, mandatory update et métadonnées signées ;
- Authenticode, .NET 10 et release commerciale ;
- production Supabase/API, SMTP, domaine, sauvegardes opérationnelles ;
- télémétrie ou trafic réseau du moteur audio.

## Validation manuelle encore requise

- transit réel Spotify de bout en bout avec signal ;
- test casque à faible niveau et panique ;
- endurance audio et débranchement/reconnexion ;
- machine propre, installation/désinstallation ;
- DPI 125/150 %, clavier et Narrateur ;
- gate intégré .NET, Node, reset/lint/pgTAP, E2E Auth/RLS et scans.

## Risques

- `+75 dB` est dangereux et non sûr pour l’audition ;
- tout programme routé vers le câble est traité ;
- Bluetooth et pilotes tiers ajoutent latence et conversions ;
- la disponibilité JWKS/Data API conditionne l’API ;
- le certificat et les clés locales deviennent sensibles s’ils quittent
  `.local` ;
- .NET 8 sort du support le 10 novembre 2026.

## Prochaine étape

Intégrer toutes les voies dans la solution, exécuter la checklist Phase A,
corriger les findings indépendants, mettre à jour les totaux réels, puis
seulement créer et pousser le commit final. La Phase B commencera par une ADR
Stripe et le writer privilégié; elle ne doit pas être amorcée dans ce gate.
