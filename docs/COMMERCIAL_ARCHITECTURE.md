# Architecture commerciale Phase A

## Statut

La Phase A construit des frontières locales vérifiables. Elle ne constitue ni
un backend de production ni une offre commerciale opérationnelle.

```mermaid
flowchart LR
    WPF["SpotifyBooster.App<br/>client public non fiable"]
    Auth["SpotifyBooster.Auth"]
    API["SpotifyBooster.Api<br/>HTTPS"]
    SA["Supabase Auth"]
    DB["PostgreSQL + RLS"]
    Stripe["Stripe<br/>Phase B"]
    License["Licence hors ligne<br/>Phase C"]
    Updater["Updater<br/>Phase D"]

    WPF -. "intégration différée" .-> Auth
    Auth --> SA
    WPF -. "intégration différée" .-> API
    API --> DB
    API -.-> Stripe
    API -.-> License
    WPF -.-> Updater
```

Le desktop est un client public non fiable. Il ne doit jamais contenir de
`service_role`, secret Stripe, clé privée de signature ou identifiant
administrateur. L’API valide l’identité et PostgreSQL applique RLS.

## Projets et dépendances autorisées

```text
SpotifyBooster.Contracts      → aucune dépendance projet
SpotifyBooster.Core           → Contracts
SpotifyBooster.Dsp            → aucune dépendance projet
SpotifyBooster.Audio          → Core, Dsp
SpotifyBooster.Infrastructure → Core
SpotifyBooster.Auth           → Core, Contracts
SpotifyBooster.Api            → Contracts
SpotifyBooster.App            → Core, Audio, Infrastructure, Dsp
```

`SpotifyBooster.App` ne référence pas Auth en Phase A. Cette séparation évite
de présenter des écrans ou un stockage de session non livrés comme
opérationnels. `SpotifyBooster.Api` ne référence pas Auth : il valide le bearer
entrant et utilise un appel Data API limité à `/v1/me`.

## Frontières

| Frontière | Règle |
| --- | --- |
| Audio | aucune requête réseau, persistance ou décision commerciale dans le callback |
| Auth | clé publiable dans `apikey`; bearer utilisateur par requête |
| API | JWT asymétrique validé; aucun fallback vers une clé privilégiée |
| Data API | bearer de l’utilisateur transmis par `HttpRequestMessage` neuf |
| Base | RLS forcée; profils owner-write limité, entitlements read-only |
| Secrets | développement, staging et production séparés; aucun secret dans Git |

## Phases

| Phase | Portée |
| --- | --- |
| A — actuelle | contrats, Auth testable, API/JWKS, schéma/RLS local, V3, `+75 dB` |
| B — différée | Stripe Checkout/Portal/webhooks et writer d’entitlements |
| C — différée | écrans WPF, DPAPI, licence hors ligne signée et devices |
| D — différée | updater signé et politique de mise à jour obligatoire |
| E — différée | .NET 10, production, Authenticode, SBOM et release commerciale |

Les contrats des phases suivantes stabilisent les interfaces. Ils n’accordent
aucun droit Pro et ne déclenchent aucun paiement ou téléchargement.

## Rollback

- API : revenir au dernier artefact vérifié; la base reste source
  d’autorisation.
- JWKS : republier la clé précédente pendant le chevauchement, purger le cache
  par l’opération interne, puis vérifier les signatures.
- Base : restaurer une sauvegarde compatible et revenir aux migrations
  versionnées; ne jamais modifier une migration déjà appliquée.
- Desktop V3 : conserver le backup `settings.schema1.backup.json` ou
  `settings.schema2.backup.json`; ne pas rétrograder en écrasant `settings.json`.

Les procédures détaillées sont dans
[INCIDENT_RESPONSE.md](INCIDENT_RESPONSE.md) et
[CONFIGURATION_MIGRATION_V3.md](CONFIGURATION_MIGRATION_V3.md).
