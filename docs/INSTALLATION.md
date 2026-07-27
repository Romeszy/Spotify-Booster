# Installation et routage

## Important

Spotify Booster ne fournit aucun pilote. Téléchargez et installez vous-même un câble audio virtuel compatible depuis son éditeur, après avoir vérifié sa licence, sa provenance et sa compatibilité Windows.

Le MVP traite **tout** ce qui entre dans la source virtuelle sélectionnée. Pour isoler Spotify, ne routez aucune autre application vers cette même source.

## Construire l’application

Depuis la racine :

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release
```

Le binaire framework-dependent est généré sous :

```text
src\SpotifyBooster.App\bin\Release\net8.0-windows10.0.19041.0\
```

Il n’existe pas encore d’installateur signé ni de publication self-contained. Le Desktop Runtime .NET 8 est donc requis sur une autre machine.

## Première configuration

Les câbles virtuels exposent généralement deux noms complémentaires :

- un endpoint de **rendu**, choisi comme sortie de Spotify dans Windows ;
- un endpoint de **capture**, choisi comme source dans Spotify Booster.

Avec certains pilotes, ces noms ressemblent à `CABLE Input` et `CABLE Output`. Suivez toujours la documentation du fournisseur : le mot « Input » ou « Output » peut décrire le côté du câble et non le sens affiché par l’application.

1. Installez séparément un câble audio virtuel compatible.
2. Lancez Spotify et démarrez une piste à volume faible.
3. Ouvrez **Paramètres Windows → Système → Son → Mélangeur de volume**.
4. Dans la ligne Spotify, choisissez l’endpoint de rendu du câble virtuel comme périphérique de sortie.
5. Laissez les jeux, Discord, le navigateur et les sons système sur leurs périphériques habituels.
6. Ouvrez Spotify Booster et cliquez sur **Actualiser**.
7. Dans **Source virtuelle**, choisissez l’endpoint de capture correspondant au câble.
8. Dans **Sortie physique**, choisissez le casque ou les enceintes.
9. Laissez le gain à **100 %**, puis activez le traitement.
10. Vérifiez que l’indicateur passe à **Signal reçu** et que les deux vu-mètres réagissent.
11. Augmentez lentement le gain.

Le bouton de l’assistant ouvre uniquement l’URI Windows fixe `ms-settings:apps-volume`. Il ne modifie aucun réglage à la place de l’utilisateur.

Par sécurité, ce MVP refuse une source qui n’est pas reconnue comme virtuelle et refuse toute sortie reconnue comme virtuelle. Les noms courants contenant `Virtual`, `Cable`, `VB-Audio`, `VoiceMeeter`, `Loopback` ou `Streaming` sont reconnus. Un pilote au nom inhabituel peut donc être bloqué jusqu’à l’ajout explicite de sa famille ; il n’existe pas de contournement « expert » à 500 % dans cette version.

## Test manuel minimal

1. Baissez le volume physique du casque/enceintes.
2. Vérifiez qu’aucune autre application ne cible le câble virtuel.
3. Démarrez Spotify Booster à 100 %, Bass Boost +3 dB, limiteur actif.
4. Jouez une piste connue et vérifiez l’absence de boucle/écho.
5. Comparez 100 %, 150 %, puis 200 %.
6. Activez le bouton panique et vérifiez le retour immédiat à 100 %.
7. Débranchez/rebranchez la sortie et observez `DeviceLost`, puis la récupération.
8. Testez 44,1 kHz et 48 kHz si le câble permet de choisir son format.
9. Laissez fonctionner 30 à 60 minutes et relevez underruns/overruns.

N’effectuez pas le premier test à 300–500 %.

## Compatibilité

- Windows 10/11, WASAPI partagé ;
- Spotify Desktop classique et Microsoft Store détectés par nom de processus ;
- mono/stéréo en entrée ;
- sortie configurée en stéréo ;
- PCM ou IEEE float ;
- 44,1/48 kHz testés hors ligne ;
- sorties Bluetooth possibles, avec latence supplémentaire.

Les modes exclusifs d’une autre application peuvent rendre un endpoint indisponible. Le MVP n’essaie pas de contourner ce verrouillage.
