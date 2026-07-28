# Spotify Booster

Spotify Booster est un utilitaire audio WPF pour Windows 10 et 11. Il capture
une source audio virtuelle, applique un traitement DSP local en temps réel,
puis envoie le résultat vers un casque ou des enceintes.

```text
Spotify → câble audio virtuel → Spotify Booster → sortie physique
```

L’isolation de Spotify dépend du routage manuel dans le mélangeur Windows.
L’application ne capture pas un flux par PID et traite toute application
envoyée vers la même source virtuelle.

> **Avertissement audio**
>
> Le mode Expert brut accepte jusqu’à `+75 dB` (`×5623,413...`) par pas de
> `0,5 dB`. Ce niveau est dangereux, n’est pas sûr pour l’audition et peut
> provoquer une saturation immédiate. Le limiteur protège seulement un plafond
> numérique lorsqu’il est actif ; il ne mesure pas le niveau acoustique.
> Commencez à `0 dB`, baissez le volume physique et utilisez le bouton panique.

## État actuel

La fondation commerciale Phase A ajoute :

- les contrats Auth, API, entitlements et mise à jour, indépendants de WPF ;
- un adaptateur Supabase Auth testable, avec session en mémoire uniquement ;
- une API ASP.NET Core avec JWT asymétriques `RS256`/`ES256`, cache JWKS,
  rate limiting, readiness et endpoint `/v1/me` ;
- PostgreSQL/Supabase local avec migrations, RLS, profils et entitlements ;
- le schéma de configuration V3 et le plafond Expert fixe à `+75 dB`.

La Phase A ne fournit pas Stripe, abonnement Pro réel, écrans de compte WPF,
stockage DPAPI, licence hors ligne, updater, mise à jour obligatoire,
Authenticode, environnement de production ou release commerciale.

La restauration verrouillée et le build Release des 13 projets réussissent
avec `0` avertissement et `0` erreur. Le test complet de la solution réussit
`344/344` :

- desktop : Audio `27`, DSP `90` et Core `90` tests ;
- fondation réseau : API `92/92` et Auth `45` tests ;
- tooling backend : Node `18/18` tests, lint réussi et audit npm à
  `0` vulnérabilité.

La voie Supabase locale a également réussi le démarrage, le reset, le lint,
`110/110` assertions pgTAP, la preuve d’atomicité du runner et l’E2E Auth/API
TLS complet. Le gate démarre le vrai processus ASP.NET, traverse `/v1/me` avec
deux utilisateurs, rotation/rejeu refresh, logout, panne puis récupération
readiness. Le JWKS a été lu avec `curl --cacert`. Ces preuves ont été obtenues
sous WSL avec CA explicite, puis stack, API et données jetables ont été
supprimées.

L’audit NuGet ne trouve aucune vulnérabilité. L’audit des packages dépréciés
signale cependant `Microsoft.NETCore.Platforms` et
`System.Security.AccessControl` `5.0.0`, ainsi que
`System.Diagnostics.DiagnosticSource` `5.0.0` sur les tests DSP/Auth/API, tous
transitifs via la chaîne xUnit. Cet avertissement amont est détaillé dans
[Sécurité](docs/SECURITY.md).

## Fonctionnalités desktop

- modes Protégé, Direct et Expert brut ;
- gain principal lissé, Bass Boost, égaliseur, compresseur, soft clipper et
  limiteur modulaires ;
- vu-mètres, clipping, réduction dynamique et diagnostic exportable ;
- sélection de la source virtuelle et de la sortie physique ;
- détection informative des processus Spotify ;
- assistant de routage, tray, raccourcis globaux et démarrage utilisateur ;
- réglages JSON locaux, sauvegarde atomique et migrations V1/V2 vers V3.

Les modes Protégé et Direct conservent leur plafond `+14 dB` et leur précision
historique. Seul Expert brut utilise la grille `0,5 dB` et le plafond `+75 dB`.

## Prérequis desktop

- Windows 10 version 2004 ou ultérieure, ou Windows 11 ;
- Spotify Desktop ;
- un câble audio virtuel compatible installé séparément ;
- SDK .NET `8.0.423` pour compiler.

Spotify Booster ne télécharge, n’installe et ne redistribue aucun pilote.

## Compiler et tester

Depuis la racine :

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln --locked-mode
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release --no-restore
& ".\.dotnet\dotnet.exe" test .\SpotifyBooster.sln --configuration Release --no-build
```

Le SDK local `.\.dotnet\dotnet.exe` est celui vérifié dans ce dépôt. Le support
de .NET 8 se termine le 10 novembre 2026 ; la migration vers .NET 10 est un
prérequis de commercialisation.

## Lancer le desktop

```powershell
& ".\.dotnet\dotnet.exe" run --project .\src\SpotifyBooster.App\SpotifyBooster.App.csproj
```

Au premier démarrage, conservez `0 dB` en mode Protégé. Configurez le câble
virtuel avant d’activer le moteur.

## Backend local Phase A

Le backend local exige Node.js `>=20`, Windows PowerShell 5.1+ ou PowerShell 7,
WSL Ubuntu, OpenSSL et Docker Desktop avec le moteur démarré. La CLI Supabase
est la dépendance de développement exacte `2.109.1`, installée par `npm ci`;
aucune CLI globale n’est utilisée.

Générez d’abord les actifs locaux depuis `backend` dans PowerShell, sans
modifier le magasin de certificats :

```powershell
powershell.exe -NoProfile -File .\scripts\bootstrap-supabase-local.ps1 -SkipStart
```

Le gate vérifié utilise ensuite WSL et désigne explicitement la CA pour chaque
client TLS :

```bash
# Dans le répertoire backend monté par WSL
npm test
npm run lint
npm run gate:local:auth-api
```

L’origine conforme est `https://127.0.0.1:54321`. Dans l’environnement du gate,
le dialogue Windows d’ajout à `CurrentUser\Root` n’a pas pu être validé ; le
certificat n’a donc pas été ajouté au magasin Windows. La voie WSL ci-dessus
conserve une validation TLS stricte : elle fournit la CA avec `SSL_CERT_FILE`
ou `--cacert` et ne désactive jamais la vérification. Les certificats et clés
sont créés sous `backend\supabase\.local\`, qui doit rester ignoré et ne jamais
être commité.

Consultez
[la procédure locale détaillée](backend/docs/LOCAL_DATABASE.md) avant usage.

## API Phase A

| Route | Authentification | Contrat |
| --- | --- | --- |
| `GET /health/live` | aucune | `200 {"status":"live"}` |
| `GET /health/ready` | aucune | `200 ready` ou `503 unready` |
| `GET /v1/me` | bearer Supabase | profil expurgé et entitlement effectif |

La readiness exige un cache JWKS frais et une réponse exacte de la RPC
publique `spotify_booster_health_v1()`. Elle ne prouve pas à elle seule la
santé complète d’Auth, de PostgreSQL ou d’un abonnement.

## Limites importantes

- aucun trafic réseau n’est nécessaire au moteur audio actuel ;
- l’adaptateur Auth et l’API ne sont pas branchés à l’interface WPF en Phase A ;
- aucun refresh token n’est persisté ;
- aucune écriture d’entitlement n’est exposée aux utilisateurs ;
- un câble virtuel, Bluetooth et les modes exclusifs peuvent ajouter latence
  ou incompatibilités ;
- `+75 dB` ne signifie pas 75 dB SPL ni un volume perçu multiplié de manière
  équivalente.

## Documentation

- [Architecture desktop](docs/ARCHITECTURE.md)
- [Architecture commerciale](docs/COMMERCIAL_ARCHITECTURE.md)
- [Authentification](docs/AUTHENTICATION.md)
- [Base de données](docs/DATABASE.md)
- [RLS](docs/RLS.md)
- [Entitlements](docs/ENTITLEMENTS.md)
- [Billing](docs/BILLING.md)
- [Système de mise à jour](docs/UPDATE_SYSTEM.md)
- [Pipeline audio](docs/AUDIO_PIPELINE.md)
- [Migration de configuration V3](docs/CONFIGURATION_MIGRATION_V3.md)
- [Sécurité](docs/SECURITY.md)
- [Threat model](docs/THREAT_MODEL.md)
- [Carte des données](docs/PRIVACY_DATA_MAP.md)
- [Réponse aux incidents](docs/INCIDENT_RESPONSE.md)
- [Checklist de release](docs/RELEASE_CHECKLIST.md)
- [État du projet](docs/PROJECT_STATE.md)
- [Licences tierces](THIRD_PARTY_NOTICES.md)

## Licence

`LICENSE.txt` conserve tous les droits et n’accorde aucun droit d’utilisation
ou de redistribution sans accord écrit séparé. Les dépendances restent soumises
à leurs licences, recensées dans
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
