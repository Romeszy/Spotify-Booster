# État du projet

Date de mise à jour : 6 août 2026.

## Synthèse

Spotify Booster possède un desktop audio WPF testable et une fondation
commerciale Phase A intégrée dans la solution. Les implémentations API, Auth,
base/RLS et DSP/V3 existent. Le schéma PostgreSQL Phase A est déployé sur le
projet Supabase hébergé ; l’API applicative, SMTP, le domaine, les opérations
de production et le paiement réel ne sont pas déployés.

La version desktop `0.2.1` ajoute le guidage de routage manuel
multi-application : Spotify reste sélectionné par défaut, avec Discord, Chrome,
Edge, Opera GX, Firefox, Brave et des processus personnalisés disponibles.
Cette sélection ne modifie pas Windows et ne filtre pas le contenu du câble.

## Opérationnel et vérifié par voie

| Voie | Fonctionnalité | Preuve la plus récente |
| --- | --- | --- |
| Desktop/Audio | énumération, route, moteur, métriques, récupération | Audio `27` tests |
| DSP | pipeline modulaire, stabilité numérique, `+75 dB` | DSP `90` tests |
| Core/Infrastructure | validation V3, migration V1/V2, persistance, sélection et détection multi-application | Core `139` tests |
| API | JWT/JWKS borné à 32 clés, `/health/*`, `/v1/me`, rate limiting pré/post-auth, métriques | API `105/105` tests |
| Auth | contrats, appels Supabase, refresh/logout, redaction | Auth `54/54` tests |
| Tooling Node | bootstrap/assets locaux, gate E2E et atomicité | `23/24` tests, 1 skip de plateforme, lint réussi, audit npm production propre |
| PostgreSQL/RLS | schéma, grants, policies, triggers, RPC health/me | preuve historique du 29 juillet 2026 ; non rejouée pour `0.2.1` |

Le 6 août 2026, la restauration verrouillée et le build Release des 13 projets
ont réussi avec `0` avertissement et `0` erreur. Le test .NET complet a réussi
`415/415` (Audio `27`, Core `139`, DSP `90`, Auth `54`, API `105`). Les tests
Node réussissent `23/24` avec un skip TLS de plateforme, le lint réussit et les
audits NuGet/npm production sont propres. Les gates Supabase n’ont pas été
rejoués pour `0.2.1`.

Pour la preuve API/backend antérieure du 29 juillet 2026, la CLI Supabase exacte `2.109.1`,
Node contrôlé avant `npm ci --offline`, le démarrage, deux resets depuis zéro,
le lint PostgreSQL, pgTAP `111/111`, l’atomicité du runner, l’E2E Auth/API réel
à deux utilisateurs, la panne/récupération readiness et la lecture JWKS avec
`curl --cacert` ont réussi sous WSL avec CA explicite. Le gate renforcé fixe
l’archive officielle du SDK Linux `8.0.423` et son SHA-512 avant extraction,
prouve l’exécution de la migration fautive par un handler qui vérifie le
SQLSTATE et le message exacts de l’exception `P0001`, incrémente une séquence
PostgreSQL non transactionnelle, puis relance l’exception. Il vérifie ensuite
le rollback de l’objet et de la version de migration, ainsi que le nettoyage
des trois artefacts. Il rejette les sorties start/reset et scanne tous les
autres logs temporaires. Stack, API,
SDK Linux temporaire, utilisateurs et logs temporaires ont ensuite été
supprimés.

Cette preuve historique comprenait aussi le déploiement Supabase distant Phase
A : les migrations
`20260728234203` et `20260728235312` sont alignées, avec la baseline immuable
au SHA-256
`11C3259D389CC8DEB77B9819407CBF2EF1B1788C148B548B302138D23A2C47FD`.
Le dry-run n’a annoncé que la nouvelle migration, puis le push a réussi. Le
lint distant est propre, pgTAP réussit `128/128` sur le projet lié et
`128/128` depuis la racine locale, tandis que le gate backend WSL reste vert à
`111/111` avec son E2E. Le contrôle privilégié utilise
`SET LOCAL ROLE postgres` avec le rôle CLI éphémère dans une transaction
annulée par rollback. L’avertissement de cache `pg-delta` est non bloquant.

Les audits NuGet et npm de cette preuve ne détectaient aucune vulnérabilité.
L’audit NuGet des packages dépréciés remontait toutefois des transitifs `5.0.0` hérités de
`xunit.v3` `3.2.2` dans les projets de tests
(`Microsoft.NETCore.Platforms`, `System.Security.AccessControl` et, selon le
projet, `System.Diagnostics.DiagnosticSource`). Cet avertissement amont reste
à surveiller ; aucune substitution transitive non validée n’est revendiquée.
L’audit `--outdated` a aussi été analysé : les upgrades directs proposés vers
ASP.NET Core `10.0.10` nécessitent la migration .NET 10 explicitement différée,
et les mises à jour transitives ne sont pas forcées. Le lock npm audité a le
SHA-256
`3ba35759b1d1b8adfe828a00d751f5a4ced1c562c86a3159f470df27b5739e46`.

## Implémenté Phase A

- `SpotifyBooster.Contracts`, `SpotifyBooster.Auth` et `SpotifyBooster.Api` ;
- JWT `RS256`/`ES256`, cache JWKS 10 minutes borné à 32 clés, gates 60 secondes
  et fail-closed, y compris exception transport inattendue, timeout tardif,
  wraparound fatal et sauts d’horloge murale ;
- plafond d’entrée `/v1` distinct de `120` requêtes/minute/adresse avant
  validation JWT, puis quotas `30`/subject et `10`/adresse après validation ;
- endpoints `GET /health/live`, `GET /health/ready`, `GET /v1/me`, avec
  bootstrap JWKS à froid et groupe `/v1` protégé par défaut ;
- Data API avec bearer par requête et vérification du subject ;
- migrations `profiles`, `entitlements`, triggers, RLS, RPC health et RPC
  `/v1/me` JWT-scoped sans argument d’identité ;
- Supabase CLI locale exacte `2.109.1`, TLS loopback et clés ES256 locales ;
- schéma de réglages V3 ;
- routage manuel guidé pour Spotify, Discord, Chrome, Edge, Opera GX, Firefox,
  Brave et jusqu’à seize noms de processus personnalisés ;
- Spotify sélectionné par défaut, détection informative des processus choisis
  et persistance dans `ManualRouting` ;
- RawExpert fixe `0..+75 dB` par pas de `0,5 dB` ;
- suppression du clamp DSP caché `±64` et assainissement NaN/Inf.
- abstractions Auth/entitlement/update placées dans `Core/Abstractions`, avec
  modèles wire indépendants dans Contracts ;
- entitlement Pro toujours borné par une échéance effective ; `canceled` est
  maintenu jusqu’à cette échéance exacte, puis désactivé, et le drapeau de
  grâce seul n’accorde aucun droit ;
- preuve V3 du rejet de `ExpertModeAcknowledged` obsolète et du rechargement
  idempotent après migration ;
- récupération finie ciblée des états compresseur et limiteur après NaN/Inf.
- sessions de récupération Auth distinguées, options réseau bornées et logout
  concurrent sérialisé ; la révocation distante est tentée même si
  l’effacement local échoue, puis l’échec persistant est rapporté explicitement
  au lieu d’être annoncé comme réussi ;
- artifact WPF portable republié puis contrôlé par allowlist et scan de secrets.
- contrat Auth `2.109.1` prouvé séparément pour récupération du parent direct
  et révocation de famille au rejeu du grand-parent ;
- `/v1/me` documenté comme projection d’entitlement Phase A, sans revendiquer
  l’évaluation Stripe, validité/grâce ou feature gating différée.

## Fonctionne seulement localement ou comme contrat

- le schéma Supabase Phase A est hébergé, mais l’API applicative reste locale ;
- l’adaptateur Auth n’est pas branché aux vues WPF ;
- les sessions ne sont pas persistées ;
- les contrats entitlement/update n’ont pas de service desktop concret ;
- le rôle writer d’entitlements est `NOLOGIN`, sans endpoint ;
- la RPC health teste seulement un chemin Data API constant ;
- l’E2E backend prouve le vrai processus API local, Auth, JWKS, Data API et
  RLS ; ce n’est pas une preuve de déploiement de l’API applicative ni
  d’exploitation en production.

## Non implémenté

- Stripe, Checkout, Portal, webhooks et abonnement Pro réel ;
- feature gating Free/Pro ;
- vues WPF Welcome/SignIn/Account ;
- stockage DPAPI et licence hors ligne signée ;
- table devices et révocation de device ;
- updater, mandatory update et métadonnées signées ;
- Authenticode, .NET 10 et release commerciale ;
- API applicative hébergée, SMTP, domaine, sauvegardes et opérations de
  production ;
- télémétrie ou trafic réseau du moteur audio.

## Validation manuelle encore requise

- transit réel de bout en bout avec Spotify puis une seconde application ;
- preuve qu’une application cochée mais non routée n’entre pas dans Booster ;
- preuve qu’une application non cochée mais réellement routée est traitée ;
- Discord : sortie complète seulement, microphone inchangé, absence
  d’isolation de personne/bot ;
- navigateur : mix de plusieurs onglets, absence d’isolation par onglet ;
- test casque à faible niveau et panique ;
- endurance audio et débranchement/reconnexion ;
- machine propre, installation/désinstallation ;
- DPI 125/150 %, clavier et Narrateur ;

## Risques

- `+75 dB` est dangereux et non sûr pour l’audition ;
- tout programme routé vers le câble est traité ;
- la sélection d’applications ne change aucun réglage Windows ;
- Discord et les navigateurs sont traités comme des sorties complètes, sans
  isolation de personne, bot ou onglet ;
- Bluetooth et pilotes tiers ajoutent latence et conversions ;
- la disponibilité JWKS/Data API conditionne l’API ;
- le certificat et les clés locales deviennent sensibles s’ils quittent
  `.local` ;
- le gate Linux n’installe aucun certificat Windows ; le résidu précédemment
  présent dans `CurrentUser\Root` a été supprimé et la preuve TLS stricte
  utilise la CA explicite sous WSL ;
- .NET 8 sort du support le 10 novembre 2026.

## Prochaine étape

Préparer une préversion technique limitée : exécuter la checklist manuelle
Phase A, déployer l’API applicative dans un environnement de préversion et
valider son intégration avec le schéma Supabase hébergé. La Phase B commencera
par une ADR Stripe et le writer privilégié ; elle ne doit pas être amorcée
pendant cette préversion.
