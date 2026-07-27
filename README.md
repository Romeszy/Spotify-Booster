# Spotify Booster

Spotify Booster est un utilitaire audio Windows WPF. Il reçoit le signal d’un câble audio virtuel, applique un traitement DSP local en temps réel, puis l’envoie vers un casque ou des enceintes.

La V2 utilise le décibel comme autorité de gain :

- plage standard : `0 à +14 dB` ;
- mode Expert brut : plafond configurable de `+25 à +1000 dB`, avec `+1000 dB` par défaut ;
- facteur linéaire : `10^(dB/20)` ;
- `+1000 dB = ×10^50 = 10^52 %`.

Une amplitude brute multipliée par `10^50` ne tient pas dans un échantillon float32. Le DSP lisse donc le gain principal en dB avec des calculs en double précision, puis borne les magnitudes internes à `±64` avant de poursuivre le pipeline float32. Cela évite les NaN et infinis ; au-delà du seuil de saturation, le résultat est surtout de la distorsion ou de la limitation, sans relation directe avec le volume perçu ni puissance physique supplémentaire. Expert brut peut dépasser le plafond de sortie puisque le limiteur n’y est pas imposé.

> **Avertissement audio**
>
> Le limiteur protège un plafond numérique sample-peak. Il ne mesure pas le niveau acoustique, ne protège ni l’audition ni le matériel et ne rend pas un gain élevé sûr. Commencez à `0 dB`, surtout au casque.

## Fonctionnalités V2

- modes explicites **Protégé**, **Direct** et **Expert brut** ;
- pipeline modulaire : DC blocker, gain principal, Bass Boost, égaliseur trois bandes, compresseur optionnel, soft clipper, limiteur et gain de sortie ;
- Bass Boost réglable de `35 à 200 Hz` et de `0 à +18 dB` ;
- égaliseur trois bandes à `120 Hz`, `1 kHz` et `8 kHz` ;
- assistant de routage VB-CABLE en sept étapes ;
- vu-mètres, réduction dynamique, clips, underruns, overruns, charge DSP et diagnostic exportable ;
- détection de Spotify Desktop et Microsoft Store sans chemin fixe ;
- icône locale originale, zone de notification, menu rapide et comportement de fermeture configurable ;
- démarrage Windows par `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` ;
- raccourcis globaux optionnels ;
- réglages JSON locaux, sauvegarde atomique et migration du schéma V1 vers V2 ;
- profils Bass Boost persistés et chargés dynamiquement dans l’interface ;
- scripts de build/test, portable self-contained single-file et installateur NSIS utilisateur.

Le gain demandé n’est jamais diminué de façon persistante en arrière-plan. En mode Protégé, le soft clipper et le limiteur restent actifs, mais la valeur du gain ne change pas toute seule.

## Limites importantes

Spotify Booster :

- ne capture pas Spotify par PID ;
- ne modifie pas automatiquement le routage audio Windows ;
- traite tout programme envoyé vers la source virtuelle sélectionnée ;
- n’installe, ne télécharge et ne redistribue aucun câble virtuel ou pilote ;
- n’ajoute ni APO, ni service Windows, ni pilote noyau ;
- n’envoie aucune télémétrie et n’utilise aucun service réseau ;
- ne fournit aucune garantie de sécurité auditive.

VB-CABLE est un composant tiers externe et manuel. Consultez [Configuration de VB-CABLE](docs/VB_CABLE_SETUP.md).

## Prérequis

- Windows 10 version 2004 ou ultérieure, ou Windows 11 ;
- Spotify Desktop ;
- un câble audio virtuel compatible installé séparément ;
- SDK .NET 8 pour compiler ;
- Desktop Runtime .NET 8 pour exécuter une publication framework-dependent.

Le dépôt fixe le SDK `8.0.423`. Le support de .NET 8 se termine le **10 novembre 2026** ; une migration vers .NET 10 LTS doit être planifiée avant une maintenance au-delà de cette date.

## Compiler et tester

Depuis la racine du dépôt :

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln --locked-mode
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release --no-restore
& ".\.dotnet\dotnet.exe" test .\SpotifyBooster.sln --configuration Release --no-build
```

Si le SDK .NET 8 est déjà dans le `PATH`, remplacez `.\.dotnet\dotnet.exe` par `dotnet`.

## Lancer depuis les sources

```powershell
& ".\.dotnet\dotnet.exe" run --project .\src\SpotifyBooster.App\SpotifyBooster.App.csproj
```

Au premier démarrage, l’assistant s’ouvre et conserve le gain à `0 dB` en mode Protégé. N’activez le moteur qu’après avoir choisi la source virtuelle et la sortie physique.

## État de validation

- baseline MVP `ab2923c` : build propre et `71/71` tests ;
- worktree V2 vérifié le 28 juillet 2026 : build Release sans avertissement ni erreur, `162/162` tests (`71` DSP, `27` Audio, `64` Core/Infrastructure) ;
- portable `artifacts\portable\SpotifyBooster-Portable-x64.exe` et installateur `artifacts\installer\SpotifyBooster-Setup-x64.exe` générés avec NSIS 3.12 ;
- package de livraison régénéré depuis le commit propre par `scripts\package-release.ps1` ; provenance, preuves TRX et hashes consignés sous `artifacts\reports` et `artifacts\checksums` ; binaires non signés ;
- route audio ouverte puis arrêtée sans erreur, mais aucun signal n’a été injecté et le vu-mètre est resté à `−∞` ;
- transit audio de bout en bout, machine propre, cycle installation/désinstallation complet et DPI 125/150 % non validés.

Consultez [État du projet](docs/PROJECT_STATE.md) pour distinguer précisément ce qui est implémenté, automatisé, testé manuellement ou encore en attente.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Pipeline audio](docs/AUDIO_PIPELINE.md)
- [Installation et routage](docs/INSTALLATION.md)
- [Configuration de VB-CABLE](docs/VB_CABLE_SETUP.md)
- [Conception de l’interface](docs/UI_DESIGN.md)
- [Packaging](docs/PACKAGING.md)
- [Checklist de release](docs/RELEASE_CHECKLIST.md)
- [Sécurité](docs/SECURITY.md)
- [Dépannage](docs/TROUBLESHOOTING.md)
- [État du projet](docs/PROJECT_STATE.md)
- [Licences tierces](THIRD_PARTY_NOTICES.md)

## Licence du projet

Le fichier `LICENSE.txt` conserve tous les droits et n’accorde aucun droit d’utilisation ou de redistribution sans accord écrit séparé. Les dépendances restent soumises à leurs propres licences, inventoriées dans [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
