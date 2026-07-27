# Installation et routage

## Prérequis

- Windows 10 version 2004 ou ultérieure, ou Windows 11 ;
- Spotify Desktop ;
- un câble audio virtuel compatible installé séparément ;
- Desktop Runtime .NET 8 pour un paquet framework-dependent.

Spotify Booster ne fournit aucun pilote, ne demande aucun privilège administrateur et ne configure pas le routage à votre place.

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

- Spotify envoie son son vers le périphérique de rendu **CABLE Input** ;
- Spotify Booster capture le périphérique **CABLE Output** ;
- Spotify Booster renvoie ensuite le son vers la sortie physique.

Voir [VB_CABLE_SETUP.md](VB_CABLE_SETUP.md) pour la procédure détaillée.

## Assistant de première configuration

L’assistant comporte sept étapes :

1. comprendre le chemin audio ;
2. installer VB-CABLE séparément ;
3. vérifier les deux côtés du câble ;
4. router uniquement Spotify dans le mélangeur Windows ;
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

## Route manuelle

1. Baissez le volume physique du casque ou des enceintes.
2. Lancez Spotify et une piste à faible niveau.
3. Ouvrez **Paramètres Windows → Système → Son → Mélangeur de volume**.
4. Choisissez **CABLE Input** comme sortie de Spotify.
5. Gardez jeux, navigateur, Discord et sons système sur leurs sorties habituelles.
6. Dans Spotify Booster, cliquez sur **Actualiser**.
7. Choisissez **CABLE Output** comme source virtuelle.
8. Choisissez votre casque ou vos enceintes comme sortie physique.
9. Activez le moteur à `0 dB` en mode Protégé.
10. Vérifiez **Signal reçu**, les vu-mètres et l’absence de boucle.
11. Augmentez lentement, par petits pas.

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

Ils sont désactivés par défaut. Les valeurs V2 par défaut sont :

| Action | Raccourci |
|---|---|
| Gain +1 dB | `Ctrl+Alt+Up` |
| Gain −1 dB | `Ctrl+Alt+Down` |
| Activer/arrêter le moteur | `Ctrl+Alt+B` |
| Panique à 0 dB | `Ctrl+Alt+0` |
| Activer/désactiver Bass Boost | `Ctrl+Alt+G` |
| Masquer/restaurer la fenêtre | `Ctrl+Alt+S` |

Si une combinaison est déjà réservée, les autres raccourcis restent utilisables et l’échec apparaît dans les notifications/diagnostics.

## Test manuel minimal

1. Vérifiez que seul Spotify cible le câble.
2. Démarrez à `0 dB`, mode Protégé, soft clipper et limiteur actifs.
3. Confirmez l’absence d’écho ou de feedback.
4. Testez `+3 dB`, puis `+6 dB`.
5. Utilisez la panique et vérifiez le retour rapide à `0 dB`.
6. Arrêtez le moteur avant de changer de périphérique.
7. Débranchez/rebranchez la sortie et observez la récupération.
8. Testez 44,1 et 48 kHz si le pilote le permet.
9. Exécutez 30 à 60 minutes et relevez underruns, overruns et charge DSP.

N’utilisez pas Expert brut pour le premier test.

## Désinstallation

Spotify Booster n’installe aucun service ou pilote. Pour retirer une copie portable :

1. quittez réellement l’application depuis le tray ;
2. désactivez **Démarrer avec Windows** ;
3. supprimez le dossier de l’application ;
4. supprimez facultativement `%LOCALAPPDATA%\SpotifyBooster`.

Le câble virtuel est indépendant. Désinstallez-le selon les instructions de son éditeur.

L’installateur NSIS généré installe par utilisateur sous `%LOCALAPPDATA%\Programs\Spotify Booster` et fournit `Uninstall.exe`. Il propose de conserver ou supprimer les réglages/logs lors d’une désinstallation interactive. Le cycle complet installation/désinstallation reste à valider sur une machine propre avant diffusion.
