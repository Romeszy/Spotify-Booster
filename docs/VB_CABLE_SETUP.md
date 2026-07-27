# Configuration de VB-CABLE

VB-CABLE est un pilote tiers externe. Spotify Booster ne le télécharge, ne l’installe, ne le met à jour et ne le redistribue pas.

## Télécharger depuis l’éditeur

Utilisez la page que l’application ouvre :

<https://vb-audio.com/Cable/>

Vérifiez la provenance, la licence et les instructions actuelles de VB-Audio avant installation. Le bouton de l’assistant ouvre seulement cette URL dans le navigateur.

## Comprendre les noms

Avec VB-CABLE standard :

```text
Spotify
  → CABLE Input (périphérique de rendu Windows)
  → câble virtuel
  → CABLE Output (périphérique de capture Windows)
  → Spotify Booster
  → casque ou enceintes
```

Le nom paraît inversé si l’on pense du point de vue de l’application :

- **CABLE Input** est l’entrée du câble, donc une sortie pour Spotify ;
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

## Router uniquement Spotify

1. Lancez Spotify et une piste à faible niveau.
2. Ouvrez **Paramètres → Système → Son → Mélangeur de volume**.
3. Dans la ligne Spotify, choisissez **CABLE Input** comme périphérique de sortie.
4. Laissez les autres applications sur leurs sorties habituelles.
5. Si Spotify ne change pas immédiatement de sortie, redémarrez Spotify.

Tout programme routé vers CABLE Input sera traité par Booster.

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

## Test sûr

1. Baissez le volume physique.
2. Vérifiez que seul Spotify cible le câble.
3. Démarrez à `0 dB`, mode Protégé.
4. Confirmez qu’il n’y a pas d’écho.
5. Utilisez la panique.
6. Testez ensuite `+3 dB`, puis `+6 dB`.
7. Observez clips, limitation, underruns et overruns.

N’activez pas Expert brut pendant la première validation.

## État de la validation locale

La route a été ouverte puis arrêtée sans erreur, mais aucun signal n’a été injecté et le vu-mètre est resté à `−∞`. Cette observation valide seulement le cycle ouverture/arrêt ; elle ne valide ni le routage Spotify, ni le transit de bout en bout, ni la qualité audio.

## Aucun signal

- confirmez que Spotify utilise CABLE Input ;
- confirmez que Booster capture CABLE Output ;
- lancez une piste ;
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
2. Rendez une sortie physique à Spotify.
3. Désactivez le démarrage automatique du traitement.
4. Suivez la procédure de désinstallation de VB-Audio.
5. Redémarrez Windows si demandé.
6. Actualisez les périphériques dans Booster et choisissez une nouvelle route valide.

Spotify Booster ne supprime aucun pilote.

## Licence et support

VB-CABLE n’est pas une dépendance NuGet et n’est pas livré avec le projet. Sa licence, ses conditions commerciales, sa compatibilité et son support relèvent de VB-Audio. Voir [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
