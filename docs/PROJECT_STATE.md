# État du projet

Date de mise à jour : 28 juillet 2026.

## Synthèse

Spotify Booster possède un desktop audio WPF testable et une fondation
commerciale Phase A intégrée dans la solution. Les implémentations API, Auth,
base/RLS et DSP/V3 existent. Aucun environnement commercial ou paiement réel
n’est livré.

## Opérationnel et vérifié par voie

| Voie | Fonctionnalité | Preuve la plus récente |
| --- | --- | --- |
| Desktop/Audio | énumération, route, moteur, métriques, récupération | Audio `27` tests |
| DSP | pipeline modulaire, stabilité numérique, `+75 dB` | DSP `90` tests |
| Core/Infrastructure | validation V3, migration V1/V2, persistance | Core `88` tests |
| API | JWT/JWKS borné à 32 clés, `/health/*`, `/v1/me`, rate limiting, métriques | API `76` tests |
| Auth | contrats, appels Supabase, refresh/logout, redaction | Auth `45` tests |
| Tooling Node | bootstrap/assets locaux, ACL après remplacement | Node `12` tests, lint réussi, audit `0` |
| PostgreSQL/RLS | schéma, grants, policies, triggers, RPC health/me | pgTAP `110/110`, E2E RPC TLS A/B réussi |

La restauration verrouillée et le build Release des 13 projets réussissent
avec `0` avertissement et `0` erreur. Le test complet de la solution réussit
`326/326` lors du gate d’intégration actuel. Pour la voie API/backend actuelle,
la CLI Supabase exacte `2.109.1`, le démarrage, le reset depuis zéro, le lint
PostgreSQL, pgTAP `110/110`, l’E2E TLS réel à deux utilisateurs et la lecture
JWKS avec `curl --cacert` ont réussi sous WSL avec CA explicite. La stack a
ensuite été arrêtée.

Les audits NuGet et npm ne détectent aucune vulnérabilité. L’audit NuGet des
packages dépréciés remonte toutefois des transitifs `5.0.0` hérités de
`xunit.v3` `3.2.2` dans les projets de tests
(`Microsoft.NETCore.Platforms`, `System.Security.AccessControl` et, selon le
projet, `System.Diagnostics.DiagnosticSource`). Cet avertissement amont reste
à surveiller ; aucune substitution transitive non validée n’est revendiquée.

## Implémenté Phase A

- `SpotifyBooster.Contracts`, `SpotifyBooster.Auth` et `SpotifyBooster.Api` ;
- JWT `RS256`/`ES256`, cache JWKS 10 minutes borné à 32 clés, gates 60 secondes
  et fail-closed ;
- endpoints `GET /health/live`, `GET /health/ready`, `GET /v1/me` ;
- Data API avec bearer par requête et vérification du subject ;
- migrations `profiles`, `entitlements`, triggers, RLS, RPC health et RPC
  `/v1/me` JWT-scoped sans argument d’identité ;
- Supabase CLI locale exacte `2.109.1`, TLS loopback et clés ES256 locales ;
- schéma de réglages V3 ;
- RawExpert fixe `0..+75 dB` par pas de `0,5 dB` ;
- suppression du clamp DSP caché `±64` et assainissement NaN/Inf.
- abstractions Auth/entitlement/update placées dans `Core/Abstractions`, avec
  modèles wire indépendants dans Contracts ;
- entitlement `canceled` maintenu jusqu’à la fin de période exacte, puis
  désactivé ;
- preuve V3 du rejet de `ExpertModeAcknowledged` obsolète et du rechargement
  idempotent après migration ;
- récupération finie ciblée des états compresseur et limiteur après NaN/Inf.

## Fonctionne seulement localement ou comme contrat

- Supabase est une stack de développement Docker, pas un déploiement ;
- l’adaptateur Auth n’est pas branché aux vues WPF ;
- les sessions ne sont pas persistées ;
- les contrats entitlement/update n’ont pas de service desktop concret ;
- le rôle writer d’entitlements est `NOLOGIN`, sans endpoint ;
- la RPC health teste seulement un chemin Data API constant ;
- l’E2E backend prouve le RPC Data API réel ; le test hosted de l’API reste un
  test contractuel à transport contrôlé, pas une preuve d’hébergement externe.

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
- E2E Auth complet de rotation refresh/logout et scans de release.

## Risques

- `+75 dB` est dangereux et non sûr pour l’audition ;
- tout programme routé vers le câble est traité ;
- Bluetooth et pilotes tiers ajoutent latence et conversions ;
- la disponibilité JWKS/Data API conditionne l’API ;
- le certificat et les clés locales deviennent sensibles s’ils quittent
  `.local` ;
- le dialogue Windows d’ajout à `CurrentUser\Root` est resté bloqué pendant le
  gate ; aucun certificat n’a été ajouté, la preuve TLS stricte a utilisé la CA
  explicite sous WSL ;
- .NET 8 sort du support le 10 novembre 2026.

## Prochaine étape

Compléter l’E2E Auth de rotation refresh/logout, puis exécuter la checklist
manuelle Phase A. La Phase B commencera par une ADR
Stripe et le writer privilégié ; elle ne doit pas être amorcée dans ce gate.
