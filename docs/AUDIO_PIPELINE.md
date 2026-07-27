# Pipeline audio

## Chemin implémenté

```text
WasapiCapture (shared/event)
  → MonitoredBufferedWaveProvider
  → normalisation WaveFormatExtensible vers PCM/IEEE standard
  → conversion float32
  → mono vers stéréo si nécessaire
  → AdaptiveResamplingSampleProvider
  → DspSampleProvider
      1. DC blocker
      2. gain principal lissé
      3. low-shelf Bass Boost
      4. égaliseur peaking trois bandes
      5. compresseur stéréo lié, optionnel
      6. soft clipper stéréo, optionnel
      7. limiteur sample-peak stéréo, optionnel
      8. gain de sortie lissé
      9. assainissement et mesures
  → SampleToWaveProvider float32
  → WasapiOut (shared/event)
```

## Formats et tampon

- traitement interne : float32 interleavé stéréo ;
- source : mono ou stéréo PCM/IEEE convertible ;
- source multicanale : refusée ;
- fréquences couvertes par les tests hors ligne : 44,1 et 48 kHz ;
- sortie : float32 stéréo, avec conversion WASAPI partagée vers le mix format ;
- capacité du tampon : 500 ms ;
- cible : 100 ms ;
- préremplissage : environ 80 ms ;
- correction de dérive : au plus ±1 500 ppm ;
- underrun : insertion de zéro et incrément du compteur ;
- overrun : rejet des données excédentaires et incrément du compteur.

Le resampler WDL reste dans le graphe même lorsque les fréquences nominales correspondent afin de corriger légèrement l’écart entre les horloges de capture et de rendu.

## Gain en décibels

Le dB est l’autorité :

```text
factor = 10^(dB / 20)
percent = factor × 100
dB = 20 × log10(percent / 100)
```

| Gain | Facteur approximatif | Affichage linéaire approximatif |
|---:|---:|---:|
| `0 dB` | `×1,0000` | `100 %` |
| `+6,0206 dB` | `×2,0000` | `200 %` |
| `+13,9794 dB` | `×5,0000` | `500 %` |
| `+14 dB` | `×5,0119` | `501 %` |
| `+20 dB` | `×10,0000` | `1 000 %` |
| `+25 dB` | `×17,7828` | `1 778 %` |
| `+1000 dB` | `×10^50` | `10^52 %` |

La plage standard est `0 à +14 dB`. Expert brut utilise un plafond configurable de `+25 à +1000 dB`, avec `+1000 dB` par défaut et comme borne architecturale.

Le gain principal utilise une rampe linéaire en dB et un facteur multiplicatif calculé une seule fois lors du changement de cible, puis appliqué sans fonction transcendante dans la boucle par échantillon. À l’ouverture ou à la reconstruction du pipeline, une cible mémorisée non nulle part de `0 dB` et la rejoint par cette rampe ; la consigne enregistrée reste inchangée. Le gain de sortie conserve son lissage exponentiel borné. Le lissage normal vaut 20 ms par défaut ; la panique utilise 1,5 ms par défaut. La panique cible `0 dB` pour les deux gains.

À `+1000 dB`, le facteur `10^50` ne peut pas être représenté comme amplitude brute float32. Le pipeline calcule le produit en double précision, puis le borne à `±64` avant de le convertir en float32. Il évite ainsi NaN et infinis, mais les valeurs au-delà du seuil de saturation ne créent pas davantage de puissance physique et ne décrivent pas directement le volume perçu : elles produisent surtout de la distorsion ou sollicitent les étages de limitation lorsqu’ils sont actifs.

Il n’existe aucune réduction automatique persistante du gain. La limitation peut réduire momentanément les échantillons dans l’étage limiteur, mais elle ne réécrit pas la consigne en dB.

## Modes de protection

| Mode | Plafond | Valeurs forcées lors de la sélection |
|---|---:|---|
| Protégé | `+14 dB` | soft clipper actif, limiteur actif |
| Direct | `+14 dB` | compresseur, soft clipper et limiteur désactivés |
| Expert brut | plafond configurable `+25 à +1000 dB`, `+1000 dB` par défaut | compresseur, soft clipper et limiteur désactivés |

Expert brut requiert un acquittement initial dans l’interface. Direct et Expert brut n’imposent pas une interdiction permanente : l’utilisateur peut réactiver ensuite un étage dynamique. Protégé empêche de désactiver le soft clipper et le limiteur.

Même en Expert brut, l’assainissement des NaN/infinis et la borne numérique interne `±64` restent des invariants de stabilité. Le DC blocker, le Bass Boost et l’égaliseur suivent leurs interrupteurs explicites.

La borne interne `±64` est une protection de stabilité, pas un plafond de sortie audio. Comme Expert brut désactive le limiteur lors de sa sélection, sa sortie peut dépasser le plafond configuré du limiteur.

Le gain de sortie est borné à `−12 à 0 dB` en mode Protégé. Direct et Expert brut autorisent `−12 à +12 dB` ; une valeur positive déclenche un avertissement explicite, car elle intervient après le limiteur.

## DC blocker

Le DC blocker stéréo est actif par défaut et désactivable. Il supprime la composante continue avant toute amplification.

## Bass Boost

Le Bass Boost est un unique low-shelf biquad stéréo :

- fréquence : `35–200 Hz` ;
- gain : `0 à +18 dB` ;
- pente : `0,25–2` ;
- défaut : `90 Hz`, `+3 dB`, pente `1`.

Profils :

| Profil | Fréquence | Gain | Pente |
|---|---:|---:|---:|
| Équilibré | 90 Hz | +3 dB | 0,90 |
| Punch | 110 Hz | +6 dB | 1,20 |
| Deep Bass | 55 Hz | +9 dB | 0,75 |

Les profils proviennent de `settings.json`, sont validés et alimentent dynamiquement la liste Bass Boost de l’interface. Modifier fréquence, gain ou pente transforme la sélection en Personnalisé.

## Égaliseur trois bandes

L’égaliseur applique trois filtres peaking en série :

- graves : 120 Hz ;
- médiums : 1 kHz ;
- aigus : 8 kHz ;
- gain de chaque bande : `−12 à +12 dB` ;
- Q partagé : `0,1–10`, défaut `0,707`.

Les fréquences sont fixes dans la V2. L’égaliseur est plat et désactivé par défaut.

## Compresseur

Le compresseur stéréo lié est désactivé par défaut :

- seuil : `−60 à 0 dBFS`, défaut `−12 dBFS` ;
- ratio : `1:1 à 20:1`, défaut `3:1` ;
- attaque : `0,1–200 ms`, défaut `10 ms` ;
- relâchement : `10–2 000 ms`, défaut `150 ms` ;
- makeup : `−12 à +24 dB`, défaut `0 dB`.

Un détecteur commun conserve l’image stéréo.

## Soft clipper

Le soft clipper est actif par défaut :

- seuil : `−18 à −0,01 dBFS`, défaut `−1 dBFS` ;
- drive : `0,5–8`, défaut `2` ;
- courbe : transition `tanh` au-dessus du seuil.

Il borne sa propre sortie à `±1`, mais ne constitue pas une mesure de sécurité auditive.

## Limiteur

Le limiteur est actif par défaut :

- plafond : `−12 à −0,01 dBFS`, défaut `−1 dBFS` ;
- relâchement : `10–1 000 ms`, défaut `80 ms` ;
- détection stéréo liée ;
- réduction immédiate du pic courant, puis relâchement ;
- clamp final au plafond configuré.

Il est sample-peak, sans look-ahead et non true-peak. Il ne garantit pas l’absence d’intersample peaks.

Le gain de sortie est appliqué **après** le limiteur. Une valeur positive de gain de sortie peut donc recréer du clipping même si le limiteur est actif.

## Assainissement

- NaN et infinis deviennent zéro ;
- les magnitudes intermédiaires sont bornées à `64` ;
- chaque étape sensible est assainie ;
- l’état des filtres et de la dynamique est remis à zéro à la reconstruction.

## Mesures

Le pipeline agrège et publie environ dix snapshots par seconde :

- pics d’entrée, après gain, après Bass Boost, après compresseur, après limiteur et en sortie ;
- gain demandé, gain appliqué et facteur linéaire ;
- réductions du compresseur et du limiteur ;
- échantillons soft-clippés, frames limitées, clips et échantillons assainis ;
- buffers, underruns, overruns, format, latence estimée, charge DSP et durée.

La latence affichée additionne les latences de capture/rendu et le remplissage du pont. Elle n’inclut pas nécessairement toute la latence Bluetooth, HDMI, codec ou pilote.

`DspSampleProvider.Read` ne réalise aucune allocation, y compris lors de l’application d’un réglage ou d’une panique. Les objets `AudioEngineMetrics` et les événements correspondants sont créés/publiés par un timer à 10 Hz, hors callback audio.

## Validation

Les tests V2 couvrent notamment :

- conversions dB/facteur/pourcentage jusqu’à `+1000 dB` ;
- gain principal lissé en dB, y compris depuis `0 dB` lors d’une initialisation non neutre, saturation numérique finie et panique ;
- limites du Bass Boost à 35/200 Hz et +18 dB ;
- égaliseur trois bandes ;
- activation indépendante de chaque module ;
- modes Direct et Expert brut sans protections cachées ;
- compresseur, soft clipper et plafond du limiteur ;
- NaN, infinis, silence et signaux extrêmes ;
- 44,1/48 kHz et buffers réutilisés sans allocation dans le DSP pur.

La vérification locale du 28 juillet 2026 a exécuté `162/162` tests : `71` DSP, `27` Audio et `64` Core/Infrastructure. Elle couvre notamment la non-allocation après mise à jour/panique, la rampe initiale d’une cible non neutre, l’acquittement obligatoire du mode Expert et les gains extrêmes finis, mais ne remplace pas un test avec signal audio réel.
