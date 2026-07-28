# Checklist de release

## Statut

Cette checklist décrit le gate d’intégration Phase A. Elle n’autorise pas une
release commerciale. Authenticode, .NET 10, Stripe, updater, production et
validation utilisateur complète sont différés.

## Source et dépendances

- [ ] worktree propre et commit identifié ;
- [ ] restauration NuGet verrouillée ;
- [ ] `npm ci` depuis `backend` avec Node `>=20` ;
- [ ] Supabase CLI locale exactement `2.109.1` ;
- [ ] aucune CLI globale substituée ;
- [ ] audits NuGet et npm sans vulnérabilité bloquante ;
- [ ] scan de secrets, clés privées, tokens et callbacks TLS permissifs ;
- [ ] `node_modules` et `.local` absents des artefacts.

## Build et tests .NET

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln --locked-mode
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release --no-restore
& ".\.dotnet\dotnet.exe" test .\SpotifyBooster.sln --configuration Release --no-build
& ".\.dotnet\dotnet.exe" format .\SpotifyBooster.sln --verify-no-changes --no-restore
```

- [ ] build sans erreur ni avertissement important ;
- [ ] tous les projets Contracts/Auth/API et tests intégrés à la solution ;
- [ ] aucun échec de test ;
- [ ] total final recopié dans `PROJECT_STATE.md` après exécution, jamais
  additionné depuis des voies indépendantes.

## Supabase local

Prérequis : Docker Desktop démarré, Windows PowerShell 5.1+ ou PowerShell 7,
Node `>=20`, OpenSSL et confiance explicite du certificat du compte courant.

Depuis `backend` :

```powershell
npm run db:reset
npm run db:lint
npm run test:db
```

- [ ] origine exacte `https://127.0.0.1:54321` ;
- [ ] aucune preuve obtenue par HTTP ou acceptation TLS permissive ;
- [ ] reset depuis zéro ;
- [ ] lint sans erreur bloquante ;
- [ ] pgTAP complet ;
- [ ] RPC health exacte avec seule clé publiable ;
- [ ] tests Auth 9/10 caractères ;
- [ ] tests deux utilisateurs et absence d’IDOR ;
- [ ] rotation refresh et logout ;
- [ ] panne/récupération readiness ;
- [ ] logs/TRX sans secret.

## Desktop et DSP

- [ ] RawExpert `0..+75 dB`, pas `0,5 dB` ;
- [ ] Direct/Protégé inchangés à `0..+14 dB` ;
- [ ] migrations V1/V2 vers V3 et backups vérifiés ;
- [ ] NaN/Inf, overflow, silence, lissage et sample rates couverts ;
- [ ] callback audio sans réseau, fichier, verrou long ou allocation ;
- [ ] panique, débranchement et reconstruction testés ;
- [ ] transit réel Spotify → câble → Booster → sortie testé manuellement ;
- [ ] casque testé à faible niveau uniquement.

## Documentation et livraison

- [ ] Phase B–E marquées comme différées ;
- [ ] `THIRD_PARTY_NOTICES.md`, SBOM et licences du publish final ;
- [ ] rapport de provenance et hashes ;
- [ ] installation/désinstallation sur machine propre ;
- [ ] DPI, clavier, Narrateur et accessibilité ;
- [ ] binaires Authenticode signés.

Les quatre derniers contrôles de commercialisation ne sont pas satisfaits en
Phase A. Ne pas présenter les `.exe` actuels comme une release commerciale.

## Rollback

- [ ] binaire précédent identifié et vérifié ;
- [ ] sauvegarde base testée ;
- [ ] procédure JWKS exercée ;
- [ ] backups de configuration V1/V2 conservés ;
- [ ] retrait du certificat TLS local documenté ;
- [ ] propriétaire et décision go/no-go consignés.
