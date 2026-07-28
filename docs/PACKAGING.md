# Packaging

## Surface implémentée

La version applicative et les scripts de release utilisent `0.2.0` pour `win-x64`.

| Fichier | Rôle |
|---|---|
| `scripts\build.ps1` | restore verrouillé puis build Release reproductible |
| `scripts\test.ps1` | restore optionnel, exécution des cinq projets de tests et sortie TRX facultative |
| `scripts\publish-portable.ps1` | publication self-contained single-file et création du mode portable |
| `scripts\build-installer.ps1` | compilation de l’installateur NSIS |
| `scripts\package-release.ps1` | nettoyage contrôlé, build, tests, portable, installateur, hashes et rapport |
| `installer\SpotifyBooster.nsi` | définition de l’installateur utilisateur |

Ces fichiers sont implémentés, mais leur présence ne prouve pas qu’une release complète a réussi.

## Prérequis

- Windows x64 ;
- SDK .NET `8.0.423` ;
- NSIS 3.12 et `makensis.exe` pour l’installateur ;
- dépendances NuGet accessibles pour la restauration RID ;
- `LICENSE.txt` et le dossier `licenses` présents.

NSIS 3.12 est installé et a produit l’installateur courant. Le script recherche `makensis.exe` dans le `PATH`, `%ProgramFiles%\NSIS`, `%ProgramFiles(x86)%\NSIS` et `%LOCALAPPDATA%\Programs\NSIS`.

.NET 8 atteint sa fin de support le 10 novembre 2026. Le portable self-contained embarque le runtime, mais il reste soumis aux correctifs et aux conditions de redistribution du runtime.

## Commande complète

Depuis la racine :

```powershell
& ".\scripts\package-release.ps1"
```

Cette commande refuse un worktree dirty. Pour un package de développement explicitement étiqueté, utilisez :

```powershell
& ".\scripts\package-release.ps1" -AllowDirty
```

Le script :

1. vérifie puis nettoie uniquement `artifacts` ;
2. restaure la solution en mode verrouillé ;
3. compile la Release avec `ContinuousIntegrationBuild=true` ;
4. exécute les tests sans rebuild et conserve cinq rapports TRX ;
5. publie le portable ;
6. construit l’installateur NSIS ;
7. génère `artifacts\checksums\SHA256SUMS.txt` en UTF-8 sans BOM ;
8. vérifie les compteurs TRX et exige que tous les tests passent ;
9. génère `artifacts\reports\release-report.txt` avec version informative, branche, commit, état du worktree et total automatisé ;
10. échoue si une étape requise échoue.

Ne distribuez rien si `artifacts\reports\release-report.txt` ne contient pas `Status: Complete`. Un statut complet sur un worktree `dirty` confirme la chaîne technique, pas un artefact final reproductible.

## Commandes par étape

```powershell
& ".\scripts\build.ps1" -Configuration Release
& ".\scripts\test.ps1" -Configuration Release -NoRestore -NoBuild
& ".\scripts\publish-portable.ps1"
& ".\scripts\build-installer.ps1"
```

`test.ps1 -NoBuild` réutilise la sortie sous `artifacts\build\bin`; exécutez d’abord `build.ps1`.

## Portable

`publish-portable.ps1` produit :

```text
artifacts\portable\
├── SpotifyBooster-Portable-x64.exe
├── appsettings.json
├── LICENSE.txt
├── THIRD_PARTY_NOTICES.md
├── licenses\
│   ├── DOTNET-LICENSE.txt
│   ├── DOTNET-THIRD-PARTY-NOTICES.txt
│   ├── NAUDIO-LICENSE.txt
│   └── NSIS-LICENSE.txt
├── portable.mode
└── Data\
```

Caractéristiques :

- self-contained ;
- single-file ;
- trimming désactivé ;
- bibliothèques natives incluses pour extraction ;
- symboles de debug exclus ;
- marqueur `portable.mode` créé automatiquement.

Le fichier `portable.mode` place les réglages dans `Data\settings.json` et les logs dans `Data\logs` à côté de l’exécutable. Le dossier doit être inscriptible par l’utilisateur.

`RuntimeIdentifiers` fixe `win-x64`. La restauration RID s’exécute en mode verrouillé contre les graphes `win-x64` consignés dans les `packages.lock.json`.

## Installateur

`build-installer.ps1` attend le portable, `appsettings.json`, l’icône locale et NSIS 3.x, puis produit :

```text
artifacts\installer\SpotifyBooster-Setup-x64.exe
```

L’installateur :

- demande le niveau `user`, sans élévation ;
- installe sous `%LOCALAPPDATA%\Programs\Spotify Booster` ;
- renomme le binaire installé en `SpotifyBooster.exe` ;
- crée le raccourci du menu Démarrer ;
- propose un raccourci Bureau ;
- propose le démarrage automatique utilisateur ;
- écrit l’entrée de désinstallation sous HKCU ;
- installe un désinstalleur ;
- embarque `LICENSE.txt`, `THIRD_PARTY_NOTICES.md` et le dossier `licenses`.

À la désinstallation, les binaires et raccourcis sont supprimés. En mode interactif, l’utilisateur choisit s’il veut aussi supprimer `%LOCALAPPDATA%\SpotifyBooster`. En mode silencieux, les réglages et logs sont conservés.

L’installateur ne contient pas VB-CABLE et n’installe aucun pilote.

## Rapport et hashes

`package-release.ps1` calcule SHA-256 pour les fichiers présents sous `artifacts`, puis écrit :

```text
artifacts\checksums\SHA256SUMS.txt
artifacts\reports\release-report.txt
artifacts\reports\tests\*.trx
```

Le manifeste est écrit en UTF-8 sans BOM. Le rapport consigne la version informative, la branche, le commit, l’état `clean` ou `dirty` du worktree et les compteurs agrégés des fichiers TRX. Le manifeste peut être produit même après un échec afin de diagnostiquer les sorties partielles. Il ne devient une preuve de release que si :

- le rapport indique `Status: Complete` ;
- tous les artefacts attendus sont présents ;
- la signature éventuelle a été appliquée avant le calcul final ;
- les fichiers n’ont pas changé ensuite.

Si la signature intervient après le script, recalculez le manifeste sur les fichiers signés.

## État du package de livraison desktop antérieur à la Phase A

La livraison desktop antérieure à la fondation commerciale, produite le
28 juillet 2026, a été régénérée par le script complet depuis son commit
propre. Son résultat historique ne valide pas les projets Phase A et ne doit
pas être présenté comme une release commerciale. Elle a :

- réussi le build Release sans avertissement ni erreur ;
- réussi `162/162` tests ;
- produit `artifacts\portable\SpotifyBooster-Portable-x64.exe` ;
- produit `artifacts\installer\SpotifyBooster-Setup-x64.exe` avec NSIS 3.12 ;
- conserve les trois preuves TRX sous `artifacts\reports\tests` ;
- produit un rapport `Status: Complete` et `Worktree: clean`.

Le cycle silencieux de l’installateur final a aussi été testé sur la machine de construction : installation et désinstallation ont retourné `0`, les dix fichiers attendus ont été contrôlés et aucun répertoire, raccourci ou clé applicative n’est resté. Ce contrôle ne remplace pas le test requis sur une machine Windows propre.

Le rapport et `SHA256SUMS.txt` sont les sources de vérité pour le commit, les tailles et les hashes de la construction courante ; ces valeurs ne sont pas dupliquées dans la documentation versionnée.

## Signature

La signature Authenticode n’est pas configurée dans le dépôt. Les deux binaires courants sont non signés. Le processus de release doit signer le portable et l’installateur, vérifier chaîne et horodatage, puis recalculer les hashes.

Ne documentez jamais un éditeur, certificat, hash ou statut signé sans preuve réelle.

## SBOM et licences

Avant distribution :

1. générez un SBOM depuis les fichiers finaux ;
2. comparez-le aux `packages.lock.json` et au runtime embarqué ;
3. incluez les textes et notices MIT/Apache-2.0/.NET/NSIS applicables ;
4. confirmez l’absence de VB-CABLE ;
5. consignez l’outil SBOM et sa version.

## Validation machine propre

Validez au minimum :

- exécution du portable ;
- stockage dans `Data` ;
- installation utilisateur et désinstallation ;
- raccourcis ;
- option de démarrage ;
- tray et raccourcis globaux ;
- assistant et diagnostic ;
- route VB-CABLE réelle ;
- panique et trois modes ;
- absence de fichiers ou entrées registre inattendus.

La livraison reste bloquée jusqu’à clôture de [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
