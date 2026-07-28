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

Le gate a généré les actifs avec le bootstrap PowerShell en mode `-SkipStart`,
puis a exécuté sous WSL :

```bash
# Dans le répertoire backend monté par WSL
npm ci
export SSL_CERT_FILE="$PWD/supabase/.local/tls/supabase-local.crt"
npm run supabase:version
npm exec --offline -- supabase start --workdir .
npm run db:reset
npm run db:lint
npm run test:db
npm run test:e2e:me
npm exec --offline -- supabase stop --workdir .
```

La CLI a annoncé exactement `2.109.1`. Le démarrage, le reset, le lint,
`110/110` assertions pgTAP et l’E2E TLS du RPC `/v1/me` avec deux utilisateurs
ont réussi. La stack a ensuite été arrêtée.

`SSL_CERT_FILE` désigne la CA locale explicite : cette voie valide la chaîne et
le nom TLS, sans HTTP ni acceptation permissive. Le dialogue Windows d’ajout du
certificat à `CurrentUser\Root` est resté bloqué dans l’environnement du gate ;
aucun certificat n’a été ajouté au magasin Windows. Le JWKS a été vérifié
séparément avec `curl --cacert`.

En échec :

1. arrêter les écritures ;
2. restaurer une sauvegarde compatible si nécessaire ;
3. ajouter une migration corrective ;
4. rejouer reset, lint, pgTAP et tests deux utilisateurs ;
5. ne jamais contourner RLS avec une clé privilégiée pour valider le gate.
