# Configuration de VB-CABLE

VB-CABLE est un pilote tiers externe. Spotify Booster ne le télécharge, ne l’installe, ne le met à jour et ne le redistribue pas.

## Télécharger depuis l’éditeur

Utilisez la page que l’application ouvre :

<https://vb-audio.com/Cable/>

Vérifiez la provenance, la licence et les instructions actuelles de VB-Audio avant installation. Le bouton de l’assistant ouvre seulement cette URL dans le navigateur.

## Comprendre les noms

Avec VB-CABLE standard :

```text
sorties des applications choisies
  → CABLE Input (périphérique de rendu Windows)
  → câble virtuel
  → CABLE Output (périphérique de capture Windows)
  → Spotify Booster
  → casque ou enceintes
```

Le nom paraît inversé si l’on pense du point de vue de l’application :

- **CABLE Input** est l’entrée du câble, donc une sortie pour les applications ;
- **CABLE Output** est la sortie du câble, donc une source de capture pour Booster.

## Installer

1. Fermez les applications audio sensibles.
2. Téléchargez VB-CABLE depuis le site de l’éditeur.
3. Suivez son installateur et ses exigences de privilèges.
4. Redémarrez Windows si demandé.
5. Ouvrez **Paramètres → Système → Son**.
6. Vérifiez que **CABLE Input** apparaît parmi les sorties.
7. Vérifiez que **CABLE Output** apparaît parmi les entrées.

Ces opérations appartiennent au pilote tiers. Spotify Booster n’en contrôle ni le succès ni la désinstallation.

## Choisir les applications à router

1. Ouvrez l’assistant de routage de Spotify Booster.
2. Conservez Spotify, sélectionné par défaut, ou cochez aussi Discord, Google
   Chrome, Microsoft Edge, Opera GX, Mozilla Firefox ou Brave.
3. Pour une autre application, ajoutez son nom de processus, avec ou sans
   `.exe`, par exemple `vlc` ou `tidal.exe`.
4. Lancez chaque application sélectionnée et démarrez un son à faible niveau.
5. Ouvrez **Paramètres → Système → Son → Mélangeur de volume**.
6. Pour chaque application choisie, sélectionnez **CABLE Input** comme
   périphérique de sortie.
7. Laissez toutes les autres applications et les sons système sur leurs
   sorties habituelles.
8. Si une application ne change pas immédiatement de sortie, fermez-la puis
   relancez-la.

La liste dans Booster sert de guide et de détecteur de processus. Elle ne
change pas les réglages Windows. Tout programme réellement routé vers CABLE
Input sera traité, qu’il soit coché ou non dans l’assistant.

Pour Discord, changez uniquement le périphérique de **sortie** de
l’application. Ne choisissez jamais CABLE Input/CABLE Output comme microphone
ou périphérique d’entrée Discord. Le câble transporte la sortie Discord
complète : Booster ne peut pas isoler une personne, un bot ou un salon.

Pour un navigateur, le processus sélectionné transporte la sortie complète du
navigateur. Booster ne peut pas isoler un onglet.

## Configurer Spotify Booster

1. Ouvrez l’assistant de routage.
2. Cliquez sur **Actualiser**.
3. Sélectionnez **CABLE Output** comme source virtuelle.
4. Sélectionnez un casque ou des enceintes physiques comme sortie.
5. Conservez `0 dB` et le mode Protégé.
6. Activez le moteur.
7. Vérifiez **Signal reçu** et les vu-mètres.
8. Montez progressivement le volume physique, puis le gain DSP si nécessaire.

Ne choisissez pas CABLE Input comme sortie de Booster. Une sortie virtuelle est refusée pour réduire le risque de boucle.

## Format conseillé

Pour le premier diagnostic :

- stéréo ;
- 48 kHz si les deux endpoints et la sortie le permettent ;
- mode partagé ;
- améliorations audio Windows désactivées temporairement si elles compliquent le test.

44,1 et 48 kHz sont couverts par les tests hors ligne. Un pilote peut toutefois ajouter ses propres conversions ou latences.

## Test manuel sûr

1. Baissez le volume physique.
2. Dans Booster, sélectionnez Spotify et une seconde application de test, par
   exemple Discord ou un navigateur.
3. Dans le mélangeur Windows, routez seulement Spotify vers CABLE Input.
4. Démarrez Booster à `0 dB`, mode Protégé, puis confirmez que Spotify anime le
   vu-mètre et sort sur le casque ou les enceintes.
5. Lancez un son dans la seconde application : il doit rester sur sa sortie
   habituelle et ne pas entrer dans le vu-mètre de Booster.
6. Routez ensuite la sortie de cette seconde application vers CABLE Input ; son
   signal doit maintenant entrer dans Booster.
7. Décochez cette application dans Booster sans changer Windows : son signal
   doit continuer à être traité, preuve que la sélection ne contrôle pas le
   câble.
8. Pour Discord, vérifiez que le microphone reste sur son entrée habituelle.
   Pour un navigateur, vérifiez que plusieurs onglets audibles sont mélangés.
9. Confirmez l’absence d’écho ou de feedback, utilisez la panique, puis testez
   `+3 dB` et `+6 dB`.
10. Observez clips, limitation, underruns et overruns.

N’activez pas Expert brut pendant la première validation.

## État de la validation locale

La route a été ouverte puis arrêtée sans erreur, mais aucun signal n’a été injecté et le vu-mètre est resté à `−∞`. Cette observation valide seulement le cycle ouverture/arrêt ; elle ne valide ni le routage Spotify, ni le transit de bout en bout, ni la qualité audio.

## Aucun signal

- confirmez que l’application testée utilise CABLE Input ;
- confirmez que Booster capture CABLE Output ;
- lancez un son dans l’application ;
- cliquez sur Actualiser ;
- vérifiez que les endpoints ne sont pas désactivés ;
- fermez les applications qui utilisent le câble en mode exclusif ;
- vérifiez les formats dans Diagnostic.

## Écho ou feedback

Arrêtez immédiatement le moteur et baissez le volume.

- ne renvoyez pas Booster vers un endpoint virtuel ;
- désactivez **Écouter ce périphérique** sur CABLE Output ;
- vérifiez qu’aucun outil de mixage ne renvoie la sortie vers CABLE Input ;
- gardez le microphone hors de cette route.

## Désinstaller VB-CABLE

1. Arrêtez Spotify Booster.
2. Rendez une sortie physique à chaque application routée vers le câble.
3. Désactivez le démarrage automatique du traitement.
4. Suivez la procédure de désinstallation de VB-Audio.
5. Redémarrez Windows si demandé.
6. Actualisez les périphériques dans Booster et choisissez une nouvelle route valide.

Spotify Booster ne supprime aucun pilote.

## Licence et support

VB-CABLE n’est pas une dépendance NuGet et n’est pas livré avec le projet. Sa licence, ses conditions commerciales, sa compatibilité et son support relèvent de VB-Audio. Voir [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
