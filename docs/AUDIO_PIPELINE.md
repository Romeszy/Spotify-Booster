# Pipeline audio

## Chemin réellement implémenté

```text
WasapiCapture (shared/event)
  → MonitoredBufferedWaveProvider (500 ms max, cible 100 ms)
  → normalisation WaveFormatExtensible → PCM/IEEE standard
  → conversion float32
  → mono vers stéréo si nécessaire
  → AdaptiveResamplingSampleProvider
  → DspSampleProvider
      1. suppression DC
      2. gain numérique lissé
      3. low-shelf Bass Boost
      4. égaliseur peaking simple
      5. compresseur stéréo lié
      6. limiteur sample-peak
      7. sanitization et mesure
  → SampleToWaveProvider float32
  → WasapiOut (shared/event, auto-conversion Windows vers le mix format)
```

## Formats

- traitement interne : float32 interleavé stéréo ;
- source acceptée : mono ou stéréo PCM/IEEE, y compris `WaveFormatExtensible` convertible ;
- fréquences testées : 44,1 kHz et 48 kHz ;
- source multicanale : refus explicite dans ce MVP ;
- sortie : flux float32 stéréo, WASAPI partagé autorise la conversion vers le mix format du périphérique.

Le resampler WDL est utilisé même lorsque les fréquences nominales correspondent. Cela permet une correction légère de la dérive entre l’horloge du câble virtuel et celle de la sortie physique.

## Tampon et dérive d’horloge

- capacité : 500 ms ;
- cible de remplissage : 100 ms ;
- démarrage de la sortie après environ 80 ms de préremplissage ;
- correction recalculée environ dix fois par seconde ;
- correction bornée à ±1 500 ppm ;
- underruns : zéro ajouté et compteur incrémenté ;
- overruns : données excédentaires rejetées et compteur incrémenté.

La correction est proportionnelle à l’écart de remplissage. Elle doit encore être mesurée sur des sessions matérielles longues avant d’être considérée comme calibrée.

## Gain

```text
gainFactor = gainPercent / 100
gainDb = 20 × log10(gainFactor)
```

| Affichage | Facteur | dB |
|---:|---:|---:|
| 100 % | 1,0 | 0,00 |
| 200 % | 2,0 | +6,02 |
| 300 % | 3,0 | +9,54 |
| 400 % | 4,0 | +12,04 |
| 500 % | 5,0 | +13,98 |

Le gain cible est clampé entre 1 et 5 et rejoint par lissage exponentiel. Le réglage applicatif par défaut est de 20 ms ; le premier démarrage force toujours 100 %.

## Bass Boost

Le Bass Boost est un low-shelf biquad stéréo :

- fréquence validée : 40–200 Hz ;
- gain : 0 à +12 dB ;
- défaut applicatif : 90 Hz / +3 dB ;
- profils UI : Balanced 90 Hz/+3 dB, Punch 105 Hz/+5 dB, Deep Bass 60 Hz/+6 dB.

Un seul shelf est appliqué. Les profils ne cumulent pas plusieurs renforcements.

## Égaliseur

Le pipeline contient une bande peaking paramétrique, plate et désactivée par défaut. Les trois valeurs low/mid/high existent dans la configuration pour préparer l’évolution, mais seul le médium à 1 kHz est actuellement mappé. L’interface indique explicitement cette limite.

## Compresseur

Compresseur stéréo lié, activé par défaut :

- threshold : −12 dBFS ;
- ratio : 3:1 ;
- attack : 10 ms ;
- release : 150 ms ;
- makeup : 0 dB.

Le détecteur commun protège l’image stéréo. Tous les paramètres sont validés avant publication.

## Limiteur

- activé par défaut ;
- plafond par défaut : −1 dBFS ;
- réduction instantanée sur le pic courant puis relâchement contrôlé ;
- clamp final garantissant qu’aucun échantillon numérique ne dépasse le plafond configuré ;
- NaN et infinis remplacés par zéro ;
- valeurs extrêmes bornées avant dynamique ;
- état remis à zéro à la reconstruction du graphe.

Le limiteur est **sample-peak, sans look-ahead et non true-peak**. Il ne garantit donc pas l’absence d’intersample peaks après conversion analogique ou codec.

Au-dessus de 100 %, le mapping audio force le limiteur même si un réglage local demande sa désactivation. Si au moins 35 % des frames sont limitées pendant une fenêtre d’environ 500 ms, le gain est réduit de 25 points, avec un cooldown de deux secondes.

## Mesures

Le moteur publie au plus environ dix snapshots par seconde :

- pic d’entrée/sortie ;
- présence du signal ;
- réduction maximale du limiteur ;
- frames limitées ;
- clips de sortie cumulés (échantillons restant au-dessus de 0 dBFS, distincts des frames sur lesquelles le limiteur a agi) ;
- nombre de buffers ;
- underruns et overruns ;
- fréquence/canaux ;
- latence estimée ;
- charge DSP estimée.

La latence affichée est une estimation : 50 ms capture + 50 ms rendu + remplissage actuel du pont. Les drivers, Bluetooth, HDMI et le câble virtuel peuvent l’augmenter.

Le pipeline DSP pur traite les buffers réutilisés sans allocation. Le wrapper moteur crée encore les objets d’état agrégés à environ 10 Hz sur le chemin de rendu ; leur déplacement complet vers une boucle de publication hors temps réel reste une optimisation avant diffusion publique.

## Tests hors ligne

`OfflineSignalGenerator` produit :

- sinus ;
- bruit rose déterministe ;
- impulsion ;
- pics au-dessus de 0 dBFS ;
- silence ;
- changement brutal de gain.

Les tests couvrent stabilité des filtres, réponse impulsionnelle, 44,1/48 kHz, silence, amplitudes maximales, NaN/infini, lissage, compresseur, plafond du limiteur et absence d’allocation dans le traitement de buffers réutilisés.
