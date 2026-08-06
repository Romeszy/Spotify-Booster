# Base de données Phase A

## Outils locaux

Le backend utilise :

- Node.js `>=20` ;
- Supabase CLI exacte `2.109.1`, devDependency verrouillée par npm ;
- Docker Desktop et WSL Ubuntu pour la voie de gate vérifiée ;
- PostgreSQL 17 dans la stack locale ;
- origine API TLS `https://127.0.0.1:54321`.

Le bootstrap, la confiance TLS et les commandes vérifiées sont documentés dans
[`backend/docs/LOCAL_DATABASE.md`](../backend/docs/LOCAL_DATABASE.md).

## Migrations

Deux historiques ont des responsabilités différentes :

- `backend/supabase/migrations` construit la stack locale TLS/ES256 utilisée
  par le gate Auth/API ;
- `supabase/migrations` est l’historique additif du projet distant lié.

| Migration | Responsabilité |
| --- | --- |
| `20260728000100_commercial_schema.sql` | schéma privé helper, `profiles`, `entitlements`, contraintes |
| `20260728000200_commercial_triggers.sql` | création Auth et invariants d’update |
| `20260728000300_commercial_rls_and_health.sql` | RLS, grants, writer futur et RPC health |
| `20260728000400_current_user_rpc.sql` | RPC `/v1/me` sans argument, JWT-scoped et security invoker |
| `20260728000500_health_function_owner.sql` | propriétaire `postgres` explicite de la RPC health |

Le projet lié conserve
`supabase/migrations/20260728234203_remote_schema.sql` comme baseline
strictement immuable, de SHA-256
`11C3259D389CC8DEB77B9819407CBF2EF1B1788C148B548B302138D23A2C47FD`.
La nouvelle migration
`20260728235312_commercial_foundation_phase_a.sql` applique le même modèle
Phase A en une transaction, rétro-provisionne les utilisateurs Auth existants
en Free et révoque les privilèges implicites trop larges de la baseline.

Une migration appliquée est immuable. Toute correction passe par une nouvelle
migration.

## `public.profiles`

| Colonne | Règle |
| --- | --- |
| `user_id` | PK, FK `auth.users`, cascade delete |
| `display_name` | null ou texte trimé de 1 à 80 caractères, sans contrôle |
| `created_at` | serveur, immuable |
| `updated_at` | serveur, `>= created_at` |

L’utilisateur ne peut modifier que `display_name`.

## `public.entitlements`

Champs : owner, plan, statut, features, revision, dates de validité/grâce et
timestamps.

- plan : `free` ou `pro` ;
- statut : allowlist contractuelle ;
- features : uniques, au plus dix, allowlist partagée ;
- revision : positive et incrémentée exactement de 1 à chaque update serveur ;
- Free : aucune date de validité ou de grâce ;
- Pro : `valid_until` obligatoire ;
- aucune donnée Stripe.

Le trigger Auth crée une ligne Free `active`, `revision=1`. Les metadata
utilisateur ne peuvent pas créer Pro.

## RPC health

```sql
public.spotify_booster_health_v1()
```

Retour exact :

```json
{"service":"spotify-booster-data","status":"ok","version":1}
```

La fonction est sans argument, constante, `IMMUTABLE`, `PARALLEL SAFE`,
`SECURITY INVOKER`, appartient explicitement à `postgres`, avec
`search_path=pg_catalog`. Elle ne lit aucune relation.

## RPC utilisateur courant

```sql
public.spotify_booster_me_v1()
```

La fonction n’accepte aucun `user_id` client. Elle est `SECURITY INVOKER`,
fixe `search_path=pg_catalog`, appartient à `postgres`, et n’est exécutable que
par `authenticated`. Elle joint `profiles` et `entitlements` sur
`auth.uid()` ; les deux tables conservent RLS forcée. Son JSON est exactement
le modèle aval de l’API : `userId`, `displayName`, `plan`, `status`,
`revision`, `features`.

## Absences volontaires

La Phase A ne crée aucune table `devices`, `security_events`,
`billing_subscriptions` ou `stripe_webhook_events`. Le rôle writer
`spotify_booster_entitlement_writer` est `NOLOGIN`, sans endpoint ni
credential.

## Validation et rollback

Le gate a généré les actifs avec le bootstrap PowerShell en mode `-SkipStart`,
puis a exécuté sous WSL :

```bash
# Dans le répertoire backend monté par WSL
npm run gate:local:auth-api
```

La CLI a annoncé exactement `2.109.1`. Le démarrage, les resets, le lint,
`111/111` assertions pgTAP, l’atomicité du runner et l’E2E TLS du vrai
`/v1/me` ASP.NET avec deux utilisateurs ont réussi. La stack a ensuite été
arrêtée.

Le projet lié possède en plus `17` assertions ciblant les ACL par défaut
effectives, `PUBLIC`, `service_role`, le rôle writer, le backfill et l’absence
d’index secondaire. Exécutées avec les `111` assertions existantes après un
reset complet, elles réussissent `128/128`. Un scénario staged séparé applique
la baseline seule, crée un utilisateur Auth, applique la migration commerciale
et vérifie sa rétro-provision Free.

Avant tout push distant, la checklist
[`supabase/README.md`](../supabase/README.md) impose les revues SQL, sécurité,
RLS, IDOR, permissions, triggers et indexes, puis `migration list` et un
`db push --dry-run`. La baseline ne doit jamais apparaître comme migration à
rejouer.

Le déploiement distant Phase A est terminé. Les migrations
`20260728234203` et `20260728235312` sont alignées entre l’historique lié et
le projet hébergé. Le dry-run n’a listé que
`20260728235312_commercial_foundation_phase_a.sql`, puis `db push` a réussi.
Le lint de la base distante est propre et la suite pgTAP liée réussit
`128/128`. La même suite réussit `128/128` depuis la racine locale ; le gate
backend sous WSL reste vert avec `111/111` assertions et son E2E Auth/API.

La validation distante s’exécute avec le rôle CLI éphémère fourni par
Supabase. Les vérifications qui exigent les droits du propriétaire utilisent
`SET LOCAL ROLE postgres` dans une transaction systématiquement annulée par
`ROLLBACK` ; aucun changement de rôle persistant n’est créé. L’avertissement
`pg-delta` relatif au cache est non bloquant et n’a empêché ni le push, ni le
lint, ni les tests.

Ce déploiement couvre le schéma PostgreSQL hébergé Phase A. Il ne déploie pas
l’API applicative, la configuration SMTP, le domaine ni les opérations de
production.

`SSL_CERT_FILE` désigne la CA locale explicite : cette voie valide la chaîne et
le nom TLS, sans HTTP ni acceptation permissive. Le gate Linux n’installe aucun
certificat dans Windows. Un résidu précédemment identifié dans
`CurrentUser\Root` a été supprimé ; la validation finale doit conserver un
compte nul. Le JWKS a été vérifié séparément avec `curl --cacert`.

`test:db:atomicity` crée d’abord, hors de la transaction de migration, une
séquence PostgreSQL jetable. La migration versionnée de test crée ensuite une
table et lève volontairement l’exception `P0001`. Un handler vérifie alors le
SQLSTATE et le message exacts, exécute `nextval(...)` sur la séquence, puis
relance l’exception. L’incrément de séquence est non transactionnel : sa
persistance prouve que le runner a traité l’exception volontaire attendue. Le
gate exige simultanément l’échec du runner, ce marqueur
persistant, l’absence de la table via `to_regclass(...)` et l’absence de la
version dans `supabase_migrations.schema_migrations`. Une panne TLS, CLI,
parseur, SQL sans rapport ou antérieure à l’exécution ne peut donc pas servir
de preuve. Le log du runner est scanné sans afficher son contenu, puis la
migration, la table défensive, la séquence et toute version résiduelle sont
supprimées et leur absence est vérifiée par trap. Un reset propre est rejoué
avant l’E2E.

Le gate Linux télécharge directement l’archive officielle du SDK .NET
`8.0.423`, vérifie son SHA-512 fixé avant extraction et n’exécute aucun script
d’installation distant. Les sorties potentiellement sensibles de `supabase
start` et `db reset` sont rejetées. Tous les autres fichiers `*.log` du
répertoire temporaire sont scannés avant suppression pour les JWT, bearer,
refresh tokens, e-mails fixtures, credentials/connexions PostgreSQL, clés
Supabase complètes et matériau JWK privé.

En échec :

1. arrêter les écritures ;
2. restaurer une sauvegarde compatible si nécessaire ;
3. ajouter une migration corrective, sans modifier la baseline ni une
   migration déjà appliquée ;
4. rejouer reset, lint, pgTAP et tests deux utilisateurs ;
5. ne jamais contourner RLS avec une clé privilégiée pour valider le gate.

Après un push réussi, le rollback est obligatoirement une migration forward
revue. Les ACL par défaut durcies ne sont pas réouvertes pour reproduire
l’ancienne baseline.
