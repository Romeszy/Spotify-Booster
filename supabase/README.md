# Migrations du projet Supabase lié

Ce dossier porte l’historique du projet Supabase distant **Spotify Booster**.
Il est distinct de `backend/supabase`, qui reste la stack locale TLS/ES256
normative du gate Auth/API Phase A.

## Baseline immuable

`migrations/20260728234203_remote_schema.sql` représente l’état distant
synchronisé avant la fondation commerciale.

- SHA-256 attendu :
  `11C3259D389CC8DEB77B9819407CBF2EF1B1788C148B548B302138D23A2C47FD`
- ne jamais modifier, reformater, renommer ou regénérer ce fichier ;
- toute correction ou évolution passe par une nouvelle migration ;
- une divergence de hash bloque le push.

La fondation commerciale est ajoutée par
`20260728235312_commercial_foundation_phase_a.sql`. Elle neutralise les
privilèges par défaut hérités de la baseline, puis crée uniquement les tables,
fonctions, triggers, politiques et grants de la Phase A.

## Création d’une évolution

Depuis la racine du dépôt, utiliser la CLI épinglée `2.109.1` :

```powershell
.\backend\node_modules\.bin\supabase.cmd migration new <nom>
```

Ne jamais réécrire une migration déjà appliquée. Les collisions d’objet ou de
rôle doivent échouer afin de révéler un drift au lieu de l’adopter.

`config.toml` et `.temp/` sont propres au poste et à sa liaison distante ; ils
ne sont pas versionnés. La configuration locale revue et reproductible reste
`backend/supabase/config.toml`.

## Gate local obligatoire

Docker doit être démarré. Depuis la racine :

```powershell
$cli = '.\backend\node_modules\.bin\supabase.cmd'
& $cli start
& $cli db reset --local --no-seed
& $cli db lint --local --level warning
& $cli test db `
  'backend/supabase/tests/001_schema_and_rls.test.sql' `
  'supabase/tests/001_commercial_foundation_security.test.sql' `
  --local
```

La preuve Phase A courante est de `128/128` assertions : `111` assertions
fonctionnelles/RLS/IDOR et `17` assertions de durcissement du projet lié. Le
backfill est aussi testé en appliquant d’abord la baseline, en créant un
utilisateur Auth jetable, puis en appliquant la migration commerciale : le
résultat doit être un profil vide et un entitlement `free/active/revision=1`.

## Checklist avant `db push`

Un push est interdit tant que chacune de ces revues n’est pas propre :

1. SQL et compatibilité PostgreSQL/Supabase ;
2. sécurité générale ;
3. RLS et IDOR ;
4. permissions `PUBLIC`, `anon`, `authenticated`, `service_role` et writer ;
5. fonctions `SECURITY DEFINER`/`INVOKER`, owners et `search_path` ;
6. triggers, backfill et invariants de révision ;
7. contraintes et indexes ;
8. transaction, drift et rollback ;
9. hash de la baseline ;
10. lint, reset et pgTAP locaux.

Ensuite seulement :

```powershell
& $cli migration list
& $cli db push --dry-run
& $cli db push
& $cli migration list
```

Le dry-run doit annoncer uniquement les migrations nouvelles. Si la baseline
`20260728234203` apparaît comme non appliquée, arrêter : elle contient une
opération distante historique et ne doit pas être rejouée.

## Rollback

Un échec de migration est annulé atomiquement par le runner Supabase. Après un
push réussi, il n’existe pas de down migration automatique : sauvegarder les
données concernées et livrer une nouvelle migration corrective revue. Les
privilèges par défaut durcis ne doivent pas être réouverts pour reproduire
l’ancienne baseline.
