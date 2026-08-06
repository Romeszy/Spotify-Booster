# Installation et routage

## Prérequis

- Windows 10 version 2004 ou ultérieure, ou Windows 11 ;
- au moins une application audio à router ; Spotify Desktop reste le choix par
  défaut ;
- un câble audio virtuel compatible installé séparément ;
- Desktop Runtime .NET 8 pour un paquet framework-dependent.

Spotify Booster ne fournit aucun pilote, ne demande aucun privilège
administrateur et ne configure pas le routage à votre place. La version
applicative courante est `0.2.1`.

.NET 8 atteint sa fin de support le 10 novembre 2026. Un paquet public maintenu après cette date devra migrer vers une version .NET prise en charge ou documenter explicitement son exception.

## Construire depuis les sources

Depuis la racine :

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln --locked-mode
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release --no-restore
```

Le build de développement se trouve sous :

```text
src\SpotifyBooster.App\bin\Release\net8.0-windows10.0.19041.0\
```

Pour le portable self-contained et l’installateur NSIS utilisateur, consultez [PACKAGING.md](PACKAGING.md). NSIS 3.12 produit les deux artefacts depuis un commit propre. Ils restent non signés ; le rapport de provenance, les résultats TRX et les hashes de la construction courante se trouvent sous `artifacts`.

## Installer le câble virtuel

Utilisez uniquement le site de l’éditeur du câble. L’assistant ouvre la page officielle VB-Audio dans le navigateur, mais ne lance aucun téléchargement.

Pour VB-CABLE :

- les applications choisies envoient leur sortie vers le périphérique de rendu
  **CABLE Input** ;
- Spotify Booster capture le périphérique **CABLE Output** ;
- Spotify Booster renvoie ensuite le son vers la sortie physique.

Voir [VB_CABLE_SETUP.md](VB_CABLE_SETUP.md) pour la procédure détaillée.

## Assistant de première configuration

L’assistant comporte sept étapes :

1. comprendre le chemin audio ;
2. installer VB-CABLE séparément ;
3. vérifier les deux côtés du câble ;
4. choisir les applications et les router dans le mélangeur Windows ;
5. sélectionner la source virtuelle et la sortie physique ;
6. tester à `0 dB` en mode Protégé, y compris la panique ;
7. consulter le dépannage et terminer.

Le bouton du mélangeur ouvre `ms-settings:apps-volume`. Le bouton Son ouvre `ms-settings:sound`. L’application ne change aucun réglage Windows silencieusement.

À la fin de l’assistant, les réglages audio reviennent à :

- mode Protégé ;
- gain principal `0 dB` ;
- gain de sortie `0 dB` ;
- compresseur désactivé ;
- soft clipper actif ;
- limiteur actif.

## Route manuelle multi-application

1. Baissez le volume physique du casque ou des enceintes.
2. Ouvrez l’assistant et choisissez les applications à amplifier. Spotify est
   coché par défaut ; les autres choix intégrés sont Discord, Google Chrome,
   Microsoft Edge, Opera GX, Mozilla Firefox et Brave.
3. Pour une application non intégrée, ajoutez son nom de processus, avec ou
   sans `.exe`, par exemple `vlc` ou `tidal.exe`. Le chemin complet n’est pas
   accepté ; Spotify Booster lui-même est refusé.
4. Lancez chaque application sélectionnée et démarrez un son à faible niveau.
5. Ouvrez **Paramètres Windows → Système → Son → Mélangeur de volume**.
6. Pour chaque application choisie, sélectionnez **CABLE Input** comme sortie.
7. Gardez toutes les autres applications et les sons système sur leurs sorties
   habituelles.
8. Pour Discord, changez uniquement sa **sortie**. Gardez le microphone sur son
   entrée physique habituelle.
9. Dans Spotify Booster, cliquez sur **Actualiser**.
10. Choisissez **CABLE Output** comme source virtuelle.
11. Choisissez votre casque ou vos enceintes comme sortie physique.
12. Activez le moteur à `0 dB` en mode Protégé.
13. Vérifiez **Signal reçu**, les vu-mètres et l’absence de boucle.
14. Augmentez lentement, par petits pas.

La sélection dans l’assistant ne pilote pas Windows. Elle surveille les
processus et génère les instructions. Le moteur traite tout signal réellement
présent sur CABLE Output, même celui d’une application non sélectionnée. Il ne
peut isoler ni une personne ou un bot Discord, ni un onglet de navigateur.

La route est refusée si :

- la source n’est pas un endpoint de capture mono/stéréo reconnu comme virtuel ;
- la sortie n’est pas un endpoint de rendu stéréo ;
- source et sortie sont identiques ;
- la sortie semble virtuelle ;
- les noms indiquent une famille susceptible de créer une boucle.

Cette détection est heuristique. Un câble compatible au nom inhabituel peut être refusé.

## Tray, fermeture et démarrage

Le comportement par défaut du bouton Fermer est **MinimizeToTray**. La fenêtre disparaît, mais l’application et le moteur peuvent rester actifs. Double-cliquez sur l’icône pour restaurer la fenêtre, ou choisissez **Quitter** dans son menu.

L’option **Démarrer avec Windows** ajoute une valeur pour l’utilisateur courant :

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

La commande enregistrée utilise l’exécutable courant avec `--minimized`. Déplacer ou renommer l’application peut invalider cette entrée ; désactivez puis réactivez l’option depuis le nouvel emplacement.

**Activer automatiquement le traitement** ne démarre le moteur que si :

- l’assistant a été terminé ;
- les endpoints sauvegardés sont retrouvés ;
- la route passe la validation.

## Raccourcis globaux

Ils sont désactivés par défaut. Les valeurs actuelles par défaut sont :

| Action | Raccourci |
|---|---|
| Gain +1 dB | `Ctrl+Alt+Up` |
| Gain −1 dB | `Ctrl+Alt+Down` |
| Activer/arrêter le moteur | `Ctrl+Alt+B` |
| Panique à 0 dB | `Ctrl+Alt+0` |
| Activer/désactiver Bass Boost | `Ctrl+Alt+G` |
| Masquer/restaurer la fenêtre | `Ctrl+Alt+S` |

Si une combinaison est déjà réservée, les autres raccourcis restent utilisables et l’échec apparaît dans les notifications/diagnostics.

## Test manuel multi-application

1. Sélectionnez Spotify et une seconde application, par exemple Discord ou un
   navigateur.
2. Routez uniquement Spotify vers CABLE Input, puis démarrez à `0 dB`, mode
   Protégé, soft clipper et limiteur actifs.
3. Vérifiez que Spotify anime le vu-mètre d’entrée et sort sur le périphérique
   physique choisi.
4. Lancez un son dans la seconde application sans la router vers le câble : il
   ne doit pas apparaître dans le vu-mètre de Booster.
5. Routez sa sortie vers CABLE Input : elle doit maintenant être traitée.
6. Décochez cette application dans Booster sans modifier Windows : son signal
   doit rester traité. Ce test confirme que la liste ne modifie pas le câble.
7. Pour Discord, confirmez que le microphone reste sur son entrée habituelle et
   que toutes les voix/bots audibles sont mélangés. Pour un navigateur,
   confirmez que tous les onglets audibles sont mélangés.
8. Routez temporairement une application non sélectionnée vers CABLE Input et
   confirmez qu’elle est elle aussi traitée, puis rendez-lui sa sortie normale.
9. Confirmez l’absence d’écho ou de feedback.
10. Testez `+3 dB`, puis `+6 dB`.
11. Utilisez la panique et vérifiez le retour rapide à `0 dB`.
12. Arrêtez le moteur avant de changer de périphérique.
13. Débranchez/rebranchez la sortie et observez la récupération.
14. Testez 44,1 et 48 kHz si le pilote le permet.
15. Exécutez 30 à 60 minutes et relevez underruns, overruns et charge DSP.

N’utilisez pas Expert brut pour le premier test.

## Mettre à jour l’installation d’un ami vers 0.2.1

Il n’existe pas encore d’updater intégré. Pour une installation NSIS déjà en
place :

1. Côté constructeur, produisez l’installateur depuis un worktree propre et ne
   le transmettez que si `artifacts\reports\release-report.txt` indique
   `Status: Complete` et `Worktree: clean`.
2. Transmettez `SpotifyBooster-Setup-x64.exe` et son SHA-256 issu du manifeste
   de cette même construction. Les anciens hashes du dépôt ne valident pas un
   nouvel installateur.
3. Sur le PC de l’ami, quittez réellement Spotify Booster depuis le tray. Ne
   laissez pas le moteur ou l’exécutable actif pendant le remplacement.
4. N’exécutez pas l’ancien désinstalleur : lancez l’installateur `0.2.1` sous le
   même compte Windows afin qu’il remplace les binaires dans
   `%LOCALAPPDATA%\Programs\Spotify Booster`.
5. Vérifiez les composants facultatifs. Si le démarrage automatique installé
   était déjà actif, l’installateur présélectionne cette option ; décochez-la
   seulement si vous voulez le désactiver.
6. Relancez l’application. Les réglages sous
   `%LOCALAPPDATA%\SpotifyBooster` doivent être conservés ; vérifiez la source,
   la sortie, la sélection d’applications et les processus personnalisés.
7. Ouvrez **Applications installées** dans Windows et vérifiez que Spotify
   Booster affiche la version `0.2.1`, puis rejouez au minimum les étapes 1 à 11
   du test manuel ci-dessus.

Les binaires actuels ne sont pas signés. Windows peut donc afficher un
avertissement ; ne contournez pas un hash différent de celui transmis par le
constructeur.

Pour une copie portable, quittez l’ancienne application, extrayez l’archive
`0.2.1` dans un nouveau dossier, puis copiez uniquement l’ancien dossier
`Data` dans ce nouveau dossier. Conservez le `portable.mode` fourni avec la
nouvelle version et ne recopiez pas l’ancien exécutable. Lancez et testez le
nouveau dossier avant de supprimer l’ancien.

## Désinstallation

Spotify Booster n’installe aucun service ou pilote. Pour retirer une copie portable :

1. quittez réellement l’application depuis le tray ;
2. désactivez **Démarrer avec Windows** ;
3. supprimez le dossier de l’application ;
4. supprimez facultativement `%LOCALAPPDATA%\SpotifyBooster`.

Le câble virtuel est indépendant. Désinstallez-le selon les instructions de son éditeur.

L’installateur NSIS généré installe par utilisateur sous `%LOCALAPPDATA%\Programs\Spotify Booster` et fournit `Uninstall.exe`. Il propose de conserver ou supprimer les réglages/logs lors d’une désinstallation interactive. Le cycle complet installation/désinstallation reste à valider sur une machine propre avant diffusion.
