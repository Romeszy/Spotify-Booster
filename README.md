# Spotify Booster

Spotify Booster est un utilitaire Windows WPF qui reçoit le signal d’un périphérique audio virtuel, applique un traitement DSP réel, puis l’envoie vers un casque ou des enceintes. Le gain numérique va de 100 % à 500 %, avec Bass Boost, compresseur et limiteur.

> **Avertissement audio**
>
> Le limiteur protège le signal numérique contre le dépassement de son plafond, pas l’audition ni le matériel. Un réglage à 500 % n’est jamais présenté comme sans danger. Commencez à 100 %, surtout au casque.

## État de cette livraison

Cette livraison contient la Phase 0 et le premier socle de Phase 1 :

- solution .NET 8 à huit projets, WPF/MVVM et injection de dépendances ;
- énumération réelle des endpoints WASAPI de capture et de rendu ;
- chemin réel `WasapiCapture → tampon → float/stéréo → resampling → DSP → WasapiOut` ;
- gain lissé, DC blocker, low-shelf, égaliseur interne simple, compresseur et limiteur ;
- vu-mètres, compteurs de clipping/limitation, underruns et overruns ;
- détection des processus Spotify Desktop/Microsoft Store sans chemin fixe ;
- configuration JSON locale validée et sauvegarde atomique ;
- assistant de routage manuel, diagnostic et export texte ;
- tests hors ligne DSP et tests du pont audio (71 tests automatisés).

L’application WPF compile, ouvre sa fenêtre principale et se ferme proprement sur la machine de livraison. Le code du chemin audio est opérationnel, mais le transit sonore de bout en bout n’a pas pu être validé ici : aucun câble audio virtuel n’y est installé. Cette limite est volontairement visible dans [PROJECT_STATE.md](docs/PROJECT_STATE.md).

## Prérequis

- Windows 10 version 2004 ou ultérieure, ou Windows 11 ;
- SDK .NET 8 pour compiler, ou Desktop Runtime .NET 8 pour exécuter un build framework-dependent ;
- Spotify Desktop ;
- un périphérique audio virtuel compatible installé séparément par l’utilisateur.

Spotify Booster ne télécharge, n’installe et ne redistribue aucun pilote.

## Compiler et tester

Le dépôt utilise `global.json` et cible le SDK `8.0.423`. Dans cet environnement, un SDK non administrateur a été installé sous `.dotnet` et est exclu de Git :

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release
& ".\.dotnet\dotnet.exe" test .\SpotifyBooster.sln --configuration Release --no-build
```

Avec un SDK .NET 8 déjà présent dans le `PATH`, remplacez `.\.dotnet\dotnet.exe` par `dotnet`.

## Lancer

```powershell
& ".\.dotnet\dotnet.exe" run --project .\src\SpotifyBooster.App\SpotifyBooster.App.csproj
```

Le premier démarrage reste à 100 % et ouvre l’assistant. Consultez [INSTALLATION.md](docs/INSTALLATION.md) avant d’activer le moteur.

## Ce que l’application ne fait pas

- elle ne dépasse pas 100 % en modifiant le volume de session Windows ;
- elle ne capture pas Spotify par PID ;
- elle ne modifie pas automatiquement le routage par application ;
- elle n’installe pas de câble virtuel, de pilote, d’APO ou de service ;
- elle ne garantit pas que la source contient uniquement Spotify ;
- elle ne nécessite aucun trafic réseau et n’émet aucune télémétrie.

Si une autre application utilise le même câble virtuel, elle sera traitée elle aussi.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Pipeline audio](docs/AUDIO_PIPELINE.md)
- [Installation et routage](docs/INSTALLATION.md)
- [Sécurité](docs/SECURITY.md)
- [Dépannage](docs/TROUBLESHOOTING.md)
- [État précis du projet](docs/PROJECT_STATE.md)
- [Licences tierces](THIRD_PARTY_NOTICES.md)

## Licence du projet

Aucune licence propre au projet n’a encore été choisie. Les dépendances et leurs licences sont documentées séparément ; ne supposez pas que le code du projet est open source ou redistribuable tant qu’un fichier `LICENSE` n’a pas été ajouté.
