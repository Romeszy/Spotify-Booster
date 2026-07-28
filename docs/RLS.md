# Row Level Security

## Principe

RLS est activée et forcée sur `public.profiles` et
`public.entitlements`. Les privilèges sont révoqués avant les grants minimaux.

## Matrice d’accès

| Rôle | `profiles` | `entitlements` | RPC health |
| --- | --- | --- | --- |
| `PUBLIC` | aucun | aucun | aucun |
| `anon` | aucun | aucun | exécution |
| `authenticated` | select propre, update propre de `display_name` | select propre | exécution |
| writer futur | aucun droit login; update serveur contrôlé | select/update limité | non requis |

Policies :

- `profiles_select_own` ;
- `profiles_update_own` avec `USING` et `WITH CHECK` sur `auth.uid()` ;
- `entitlements_select_own` ;
- `entitlements_writer_select` et `entitlements_writer_update` pour le rôle
  `NOLOGIN` futur.

Les grants de colonnes empêchent l’utilisateur de modifier owner, timestamps
ou champs d’entitlement même si une policy était mal utilisée.

## Schéma privé

`spotify_booster_private` contient uniquement fonctions de validation et
triggers. `public`, `anon` et `authenticated` n’ont ni `USAGE` ni `EXECUTE`.
Les fonctions `SECURITY DEFINER` fixent `search_path=pg_catalog`.

## Preuves pgTAP

La suite `backend/supabase/tests/001_schema_and_rls.test.sql` couvre notamment :

- isolement de deux utilisateurs ;
- absence de droits anon sur tables, vues et séquences ;
- update limité de `display_name` ;
- entitlements read-only pour l’utilisateur ;
- contraintes plan/statut/features/dates/revision ;
- invariants des triggers ;
- forme, privilèges et retour exact de la RPC ;
- absence des tables différées.

La voie base a obtenu un résultat pgTAP positif. Ce résultat doit être rejoué
depuis un reset propre dans le gate d’intégration final.

## Modification sûre

Toute nouvelle table exposée doit :

1. révoquer les privilèges par défaut ;
2. activer et forcer RLS ;
3. définir les grants de colonnes ;
4. fournir des tests anon, owner, autre owner et writer ;
5. prouver les fonctions `SECURITY DEFINER` et leur `search_path` ;
6. échouer fermée sans `service_role`.
