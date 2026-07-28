# Threat model Phase A

## Périmètre

Le modèle couvre le desktop, l’adaptateur Auth, l’API ASP.NET Core, Supabase
Auth/Data API, PostgreSQL/RLS et les artefacts locaux de développement. Stripe,
licence hors ligne, updater et production sont hors périmètre car non
implémentés.

## Actifs

- audition et matériel audio de l’utilisateur ;
- access/refresh tokens ;
- identité et profil ;
- état d’entitlement ;
- clés privées TLS/JWT locales ;
- intégrité des migrations, packages et binaires ;
- disponibilité JWKS, Data API et audio.

## Frontières de confiance

| Composant | Confiance |
| --- | --- |
| WPF et machine utilisateur | non fiable pour autoriser Pro |
| bearer utilisateur | secret temporaire, jamais loggable |
| clé Supabase publiable | publique, sans privilège administratif |
| API | valide l’identité mais délègue l’accès aux lignes à RLS |
| PostgreSQL/RLS | frontière d’autorisation des données |
| secrets serveur futurs | absents du desktop et du dépôt |

## Menaces et contrôles

| Menace | Contrôle Phase A | Risque restant |
| --- | --- | --- |
| IDOR entre A et B | bearer par requête, subject comparé, RLS owner-only, E2E RPC TLS A/B réussi lors du gate actuel | preuve à rejouer après toute modification des policies ou RPC |
| JWT forgé | `RS256`/`ES256`, issuer/audience/temps/sub validés | custody production différée |
| tempête de `kid` | cache 10 min, single-flight, cooldown 60 s | cooldown par processus |
| complétion JWKS tardive | epoch/generation et résultat stale ignoré | supervision production absente |
| fuite de bearer | pas de default headers, réponses/logs expurgés | scan final requis |
| privilège Data API excessif | grants minimaux et RLS forcée | audit staging/production différé |
| secret dans Git | exemples factices, `.local` ignoré, scan requis | erreur humaine |
| abus du mode `+75 dB` | acquittement, panique, lissage, modes explicites | aucune sécurité auditive garantie |
| boucle audio | source virtuelle/sortie physique validées | matériel et routage tiers |
| supply chain | versions exactes, locks, audits | signature/SBOM commerciale différés |

## RPC health publique

`spotify_booster_health_v1()` est une capacité publique volontaire. Elle est
sans argument, constante, `SECURITY INVOKER`, `IMMUTABLE`, `PARALLEL SAFE` et
ne lit aucune table, claim, horloge ou configuration.

Elle prouve uniquement que le gateway/Data API peut exécuter le contrat
versionné. Elle ne prouve pas Auth, une ligne utilisateur, les écritures ou la
santé complète de PostgreSQL.

Charge attendue : deux appels par minute et par instance API. Un attaquant peut
consommer le quota upstream même sans accéder aux données. La production devra
surveiller le débit, appliquer quotas/jitter au gateway et ne jamais ajouter
table, compteur ou donnée utilisateur à cette fonction.

## Hypothèses à invalider avant production

- hébergement, proxy et origine HTTPS définitifs ;
- gestion et rotation des clés ;
- quotas distribués multi-instance ;
- sauvegardes et restauration ;
- conformité RGPD et rétention ;
- Authenticode et chaîne de mise à jour ;
- Stripe et writer privilégié.
