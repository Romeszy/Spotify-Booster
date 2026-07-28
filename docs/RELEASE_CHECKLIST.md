# Checklist de release

## Statut

Cette checklist décrit le gate d’intégration Phase A. Elle n’autorise pas une
release commerciale. Authenticode, .NET 10, Stripe, updater, production et
validation utilisateur complète sont différés.

Le gate backend du 28 juillet 2026 a réussi les tests Node `18/18`, le lint,
Supabase `2.109.1`, reset, lint PostgreSQL, pgTAP `110/110`, atomicité des
migrations, E2E Auth/API TLS à deux utilisateurs, panne/récupération readiness
et scan de secrets. L’API ciblée réussit `94/94`. Le total global .NET réussit
`346/346`, avec build Release `0` avertissement/`0` erreur, format et diff
propres. L’audit NuGet remonte `0` vulnérabilité ; les transitifs de tests
dépréciés `5.0.0` via xUnit restent documentés. La stack locale a été arrêtée.

## Source et dépendances

- [ ] **Sign-off livraison :** worktree propre et commit identifié ;
- [x] restauration NuGet verrouillée ;
- [x] Node majeur `>=20` vérifié avant npm ;
- [x] `npm ci --offline` depuis copies exactes de `package.json` et lock ;
- [x] Supabase CLI locale exactement `2.109.1` ;
- [x] aucune CLI globale substituée ;
- [x] audits NuGet et npm sans vulnérabilité bloquante ;
- [x] scan de secrets, clés privées, tokens et callbacks TLS permissifs ;
- [ ] **Sign-off packaging :** `node_modules` et `.local` absents des
  artefacts.

## Build et tests .NET

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln --locked-mode
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release --no-restore
& ".\.dotnet\dotnet.exe" test .\SpotifyBooster.sln --configuration Release --no-build
& ".\.dotnet\dotnet.exe" format .\SpotifyBooster.sln --verify-no-changes --no-restore
```

- [x] build sans erreur ni avertissement important ;
- [x] tous les projets Contracts/Auth/API et tests intégrés à la solution ;
- [x] aucun échec de test ;
- [x] total final recopié dans `PROJECT_STATE.md` après exécution, jamais
  additionné depuis des voies indépendantes.

## Supabase local

Prérequis : Docker Desktop démarré, Windows PowerShell 5.1+ ou PowerShell 7,
WSL Ubuntu, Node `>=20` et OpenSSL.

Après génération des actifs par le bootstrap PowerShell `-SkipStart`, exécuter
depuis `backend` sous WSL :

```bash
export SSL_CERT_FILE="$PWD/supabase/.local/tls/supabase-local.crt"
npm run gate:local:auth-api
```

- [x] origine exacte `https://127.0.0.1:54321` ;
- [x] aucune preuve obtenue par HTTP ou acceptation TLS permissive ;
- [x] CA locale fournie explicitement par `SSL_CERT_FILE` ou `--cacert` ;
- [x] reset depuis zéro ;
- [x] lint sans erreur bloquante ;
- [x] pgTAP `110/110` ;
- [x] test statique : le probe crée hors transaction une séquence PostgreSQL ;
  un handler vérifie le SQLSTATE et le message exacts de `P0001`, incrémente
  la séquence, puis relance l’exception ;
- [x] **Gate final renforcé :** échec du runner, marqueur non transactionnel
  persistant, objet et version absents, puis nettoyage vérifié ;
- [x] RPC health exacte avec seule clé publiable ;
- [x] test statique : SDK `8.0.423` téléchargé depuis l’archive officielle,
  SHA-512 vérifié avant extraction et aucun `dotnet-install.sh` ;
- [x] tests Auth 9/10 caractères ;
- [x] tests deux utilisateurs et absence d’IDOR ;
- [x] rotation/rejeu famille refresh selon Auth `2.109.1` et logout ;
- [x] panne/récupération readiness `200 -> 503 -> 200` ;
- [x] test statique : sorties start/reset non persistées et tous les autres
  logs temporaires scannés sans afficher de contenu sensible ;
- [ ] **Gate final renforcé :** logs sans JWT, bearer, refresh token, mot de
  passe/connexion, e-mail jetable, clé Supabase complète ou matériau JWK privé.

Le gate Linux n’installe ni ne modifie aucun certificat dans un magasin
Windows. Le résidu précédemment présent dans `CurrentUser\Root` a été supprimé.
La preuve utilise la CA explicite sous WSL ; ni HTTP, ni `-k`, ni callback TLS
permissif ne sont autorisés.

## Desktop et DSP

- [x] RawExpert `0..+75 dB`, pas `0,5 dB` ;
- [x] Direct/Protégé inchangés à `0..+14 dB` ;
- [x] migrations V1/V2 vers V3 et backups vérifiés ;
- [x] NaN/Inf, overflow, silence, lissage et sample rates couverts ;
- [x] callback audio sans réseau, fichier, verrou long ou allocation ;
- [x] panique, débranchement et reconstruction testés ;
- [ ] **Sign-off matériel :** transit réel Spotify → câble → Booster → sortie
  testé manuellement ;
- [ ] **Sign-off sécurité auditive :** casque testé à faible niveau uniquement.

## Documentation et livraison

- [x] Phase B–E marquées comme différées ;
- [ ] `THIRD_PARTY_NOTICES.md`, SBOM et licences du publish final ;
- [ ] rapport de provenance et hashes ;
- [ ] installation/désinstallation sur machine propre ;
- [ ] DPI, clavier, Narrateur et accessibilité ;
- [ ] binaires Authenticode signés.

Les contrôles de commercialisation non cochés ci-dessus ne sont pas satisfaits
en Phase A. Ne pas présenter les `.exe` actuels comme une release commerciale.

## Rollback

- [ ] binaire précédent identifié et vérifié ;
- [ ] sauvegarde base testée ;
- [ ] procédure JWKS exercée ;
- [ ] backups de configuration V1/V2 conservés ;
- [ ] retrait du certificat TLS local documenté ;
- [ ] propriétaire et décision go/no-go consignés.
