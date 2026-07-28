# Base de données Phase A

## Outils locaux

Le backend utilise :

- Node.js `>=20` ;
- Supabase CLI exacte `2.109.1`, devDependency verrouillée par npm ;
- Docker Desktop ;
- PostgreSQL 17 dans la stack locale ;
- origine API TLS `https://127.0.0.1:54321`.

Le bootstrap, la confiance TLS et les commandes vérifiées sont documentés dans
[`backend/docs/LOCAL_DATABASE.md`](../backend/docs/LOCAL_DATABASE.md).

## Migrations

| Migration | Responsabilité |
| --- | --- |
| `20260728000100_commercial_schema.sql` | schéma privé helper, `profiles`, `entitlements`, contraintes |
| `20260728000200_commercial_triggers.sql` | création Auth et invariants d’update |
| `20260728000300_commercial_rls_and_health.sql` | RLS, grants, writer futur et RPC health |
| `20260728000400_current_user_rpc.sql` | RPC `/v1/me` sans argument, JWT-scoped et security invoker |

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
`SECURITY INVOKER`, avec `search_path=pg_catalog`. Elle ne lit aucune relation.

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

Depuis `backend` :

```powershell
npm run db:reset
npm run db:lint
npm run test:db
npm run test:e2e:me
```

La voie base a exécuté reset, lint et pgTAP avec succès. Le gate intégré doit
rafraîchir cette preuve.

En échec :

1. arrêter les écritures ;
2. restaurer une sauvegarde compatible si nécessaire ;
3. ajouter une migration corrective ;
4. rejouer reset, lint, pgTAP et tests deux utilisateurs ;
5. ne jamais contourner RLS avec une clé privilégiée pour valider le gate.
