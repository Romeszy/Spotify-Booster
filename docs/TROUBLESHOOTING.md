# Dépannage

## Aucun périphérique virtuel n’apparaît

Spotify Booster n’installe aucun pilote.

1. Installez le câble depuis son éditeur.
2. Redémarrez Windows si l’installateur le demande.
3. Vérifiez que ses endpoints sont activés dans les paramètres Son.
4. Relancez Spotify Booster ou cliquez sur **Actualiser**.

Pour VB-CABLE, la source attendue dans Booster est généralement **CABLE Output**. Voir [VB_CABLE_SETUP.md](VB_CABLE_SETUP.md).

## Le câble est installé mais la source est refusée

La route exige une source mono/stéréo reconnue comme virtuelle. La détection s’appuie sur les métadonnées et des marqueurs tels que `Virtual`, `Cable`, `VB-Audio`, `VoiceMeeter`, `Loopback` ou `Streaming`.

Un nom inhabituel peut être refusé par prudence. Exportez le diagnostic et
relevez le nom/format exact ; la version actuelle n’expose pas de contournement
de route non sécurisé.

## L’application est détectée, mais aucun signal n’arrive

La détection du processus et le routage audio sont indépendants.

1. Ouvrez le mélangeur de volume Windows.
2. Vérifiez que l’application testée utilise **CABLE Input** comme sortie.
3. Vérifiez que Booster capture **CABLE Output**.
4. Lancez réellement un son dans l’application.
5. Vérifiez que le câble n’est pas désactivé ou monopolisé en mode exclusif.
6. Observez le vu-mètre d’entrée et la page Diagnostic.

Le moteur peut fonctionner même si la détection du processus échoue. À
l’inverse, un processus détecté ne prouve pas que sa sortie est routée vers le
câble. Spotify est seulement le choix par défaut de l’assistant.

## Une application personnalisée n’est pas détectée

- saisissez seulement le nom du processus, avec ou sans `.exe`, pas son chemin
  complet ;
- utilisez par exemple `vlc` ou `tidal.exe` ;
- ouvrez le Gestionnaire des tâches si le nom réel diffère du nom commercial ;
- retirez puis ajoutez de nouveau le nom après correction ;
- Spotify Booster refuse son propre nom de processus et limite la liste à
  seize entrées.

La détection reste informative. Si le processus est mal nommé mais que Windows
l’envoie réellement vers le câble, son signal sera quand même traité.

## Le signal contient d’autres applications

Toutes ces applications utilisent le même câble. Replacez jeux, navigateur, Discord et sons système sur leurs sorties habituelles. Booster ne sépare pas plusieurs flux déjà mélangés.

Décochez une application dans Booster uniquement pour modifier le guide et la
surveillance. Ce geste ne la retire pas du câble : rendez-lui sa sortie
habituelle dans le mélangeur Windows.

## Discord contient la mauvaise voix ou le microphone

Spotify Booster reçoit la sortie Discord complète. Il ne peut isoler ni une
personne, ni un bot, ni un salon. Dans Discord et dans Windows :

- laissez le microphone sur son entrée physique habituelle ;
- routez uniquement la sortie Discord vers CABLE Input ;
- ne choisissez jamais le câble comme entrée micro ;
- si le micro apparaît dans Booster, retirez tout retour **Écouter ce
  périphérique** ou toute boucle créée par un autre mélangeur.

## Un seul onglet du navigateur devait être amplifié

La sélection Chrome, Edge, Opera GX, Firefox ou Brave travaille au niveau du
processus détecté et le câble reçoit le mix réellement produit par Windows.
Spotify Booster ne sépare pas les onglets. Utilisez un navigateur ou un profil
séparé si Windows l’expose comme session audio distincte, puis vérifiez le
résultat dans le mélangeur ; l’application ne garantit pas cette séparation.

## Pas de son en sortie

- choisissez une sortie physique active et configurée en stéréo ;
- arrêtez le moteur avant de changer de périphérique ;
- vérifiez qu’aucune application ne monopolise la sortie en mode exclusif ;
- testez d’abord une sortie filaire ;
- vérifiez les formats source/sortie dans Diagnostic ;
- confirmez que le volume physique n’est pas coupé.

Une sortie qui semble virtuelle est refusée afin d’éviter une boucle.

## Son haché, underruns ou overruns

- alignez si possible le câble et la sortie sur 48 kHz ;
- fermez les applications audio lourdes ;
- évitez Bluetooth pendant le diagnostic ;
- désactivez temporairement les modes exclusifs ;
- laissez tourner 30 minutes et relevez les compteurs ;
- exportez le diagnostic.

Le moteur corrige la dérive à ±1 500 ppm, mais le calibrage matériel multi-pilote reste à valider.

## Distorsion ou limitation permanente

1. Utilisez la panique pour revenir à `0 dB`.
2. Repassez en mode Protégé.
3. Réduisez le Bass Boost et l’égaliseur.
4. Dans **Réglages → Audio**, remettez le gain de sortie à `0 dB`.
5. Augmentez ensuite par petits pas.

Le gain de sortie est placé après le limiteur : une valeur positive peut recréer du clipping.

Le mode Protégé refuse toute valeur positive et la ramène au plus à `0 dB`. Direct et Expert brut l’autorisent, avec un avertissement visible.

La V3 ne diminue pas automatiquement et durablement la consigne de gain. Si le gain affiché change, vérifiez un raccourci global, une action du tray, un profil ou la panique. La différence momentanée entre gain demandé et appliqué est normale pendant le lissage.

## Direct ou Expert brut sature immédiatement

Ces modes désactivent les étages dynamiques lors de leur sélection. Repassez en Protégé ou réactivez manuellement le soft clipper/limiteur. Expert brut n’a aucune protection cachée et autorise jusqu’à `+75 dB` par pas de `0,5 dB`.

À `+75 dB`, le facteur mathématique demandé vaut environ `5623,413`. Le DSP lisse le gain en dB, calcule en double précision et ne sature qu’un overflow numérique à `±float.MaxValue`. Le résultat peut être immédiatement saturé ou distordu, ne décrit pas le volume perçu et n’apporte aucune garantie de sécurité auditive. En Expert brut, la sortie peut dépasser le plafond configuré lorsque le limiteur est désactivé ou lorsqu’un gain de sortie positif est appliqué après lui.

## Le bouton panique ne semble pas instantané

La commande est consommée au bloc de rendu suivant puis applique un lissage court, 1,5 ms par défaut. Elle remet les gains DSP à `0 dB` ; elle ne change pas le volume physique Windows.

## Périphérique débranché

Le moteur passe à `DeviceLost` et tente jusqu’à trois reconstructions. Si elles échouent :

1. rebranchez le périphérique ;
2. cliquez sur **Actualiser** ;
3. resélectionnez source et sortie ;
4. redémarrez à `0 dB`.

## Bluetooth

Bluetooth peut ajouter 100–300 ms ou plus, changer de profil et basculer en mode mains libres. La latence affichée n’inclut pas nécessairement tout le délai radio/codec.

## La fenêtre a disparu

Le comportement par défaut du bouton Fermer masque la fenêtre dans le tray.

- double-cliquez sur l’icône Spotify Booster ;
- utilisez `Ctrl+Alt+S` si les raccourcis globaux sont actifs ;
- ouvrez le menu de l’icône pour restaurer ou quitter.

Si aucune icône n’est visible, vérifiez la zone des icônes masquées de Windows.

## Spotify Booster démarre avec Windows au mauvais emplacement

Le registre mémorise le chemin absolu de l’exécutable. Après déplacement :

1. lancez l’exécutable depuis son nouvel emplacement ;
2. désactivez **Démarrer avec Windows** ;
3. réactivez l’option.

L’entrée se trouve sous `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Le moteur ne démarre pas automatiquement

Le démarrage automatique du traitement exige :

- assistant terminé ;
- option activée ;
- source et sortie sauvegardées encore disponibles ;
- route valide.

Le mode **Démarrer masqué ou réduit** concerne seulement la fenêtre.

## Un raccourci global ne fonctionne pas

Une autre application ou Windows peut avoir réservé la combinaison.

- ouvrez Diagnostic et cherchez une erreur de raccourci ;
- désactivez puis réactivez les raccourcis globaux ;
- fermez l’application concurrente ;
- vérifiez les valeurs dans `settings.json` si elles ont été modifiées manuellement.

Une combinaison doit contenir au moins un modificateur (`Ctrl`, `Alt`, `Shift` ou `Win`) et exactement une touche. Deux actions ne peuvent pas partager la même combinaison.

## Les anciens réglages ont changé après mise à jour

La migration V1 convertit d’abord le pourcentage en dB, crée
`settings.schema1.backup.json`, puis amène le fichier au schéma V3. V3 ajoute
notamment `ManualRouting`; en l’absence de sélection antérieure, Spotify reste
sélectionné par défaut.

- `100 % → 0 dB` ;
- `200 % → +6,0206 dB` ;
- `500 % → +13,9794 dB`.

Si la configuration est invalide, l’application utilise les réglages sûrs par défaut sans écraser automatiquement le fichier illisible.

## Où sont les réglages et logs ?

Mode standard :

```text
%LOCALAPPDATA%\SpotifyBooster\settings.json
%LOCALAPPDATA%\SpotifyBooster\logs\spotify-booster.log
```

Mode portable :

```text
<dossier application>\portable.mode
<dossier application>\Data\settings.json
<dossier application>\Data\logs\spotify-booster.log
```

Le mode portable peut échouer si le dossier de l’application n’est pas inscriptible.

## Le rapport de diagnostic contient-il des données sensibles ?

Il contient les noms visibles des périphériques, les familles et noms des
processus audio sélectionnés détectés, leurs PID, les formats, métriques et
erreurs expurgées. Il exclut les IDs d’endpoints, chemins, arguments et contenu
audio. Relisez-le avant partage.
