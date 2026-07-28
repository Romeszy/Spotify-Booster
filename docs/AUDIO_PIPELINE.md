# Pipeline audio

## Route

```text
source virtuelle
  → capture WASAPI partagée
  → tampon borné
  → conversion stéréo / resampling
  → DSP
  → sortie WASAPI partagée
```

L’application traite tout signal présent sur la source sélectionnée. La
détection de Spotify est informative et ne prouve pas l’origine de l’audio.

## Pipeline DSP

```text
DC blocker
  → gain principal lissé
  → low-shelf Bass Boost
  → égaliseur 3 bandes
  → compresseur optionnel
  → soft clipper optionnel
  → limiteur optionnel
  → gain de sortie lissé
```

Les modules restent indépendants. Un étage désactivé n’est pas réactivé par le
DSP. La panique remet gain principal et gain de sortie à `0 dB`.

## Modes

| Mode | Gain | Pas principal | Sélection |
| --- | ---: | ---: | --- |
| Protégé | `0..+14 dB` | `0,1 dB` | active soft clipper et limiteur |
| Direct | `0..+14 dB` | `0,1 dB` | désactive les étages dynamiques |
| Expert brut | `0..+75 dB` | `0,5 dB` | acquittement, protections non imposées |

Direct et Protégé conservent leurs bornes et précision V2. La grille demi-dB
s’applique uniquement à RawExpert.

## Gain Expert

```text
gainFactor = 10^(gainDb / 20)
```

Valeurs de référence :

| dB | Facteur |
| ---: | ---: |
| `+25` | `17,7827941003892` |
| `+40` | `100` |
| `+60` | `1000` |
| `+75` | `5623,41325190349` |

Le DSP multiplie en double précision et conserve tout résultat float fini. Il
ne possède plus de clamp caché `±64`; seul un overflow numérique est saturé à
`±float.MaxValue`. NaN devient zéro, Inf devient la valeur float maximale avec
son signe, et une métrique est incrémentée. Un filtre stateful contaminé
réinitialise uniquement le canal concerné.

Ces protections assurent la stabilité numérique, pas la sécurité auditive.
`+75 dB` peut saturer immédiatement, produire une forte distorsion et endommager
l’audition ou le matériel. Le facteur numérique ne correspond ni à des dB SPL
ni à un gain de puissance acoustique garanti.

## Lissage

Le gain principal est lissé en dB. Pour une durée `ms` :

```text
N = ceil(sampleRate × ms / 1000)
```

La rampe est monotone, sans dépassement et atteint la cible au N-ième
échantillon. La panique utilise `1,5 ms` par défaut; les changements normaux
utilisent `20 ms` par défaut. Les tests couvrent 44,1, 48 et 96 kHz.

## Traitements

- Bass Boost : low-shelf, profils Balanced, Punch et Deep Bass ;
- égaliseur : 120 Hz, 1 kHz et 8 kHz ;
- compresseur : threshold, ratio, attack, release et makeup ;
- soft clipper : threshold et drive ;
- limiteur : plafond `-1 dBFS` et release `80 ms` par défaut.

Le gain de sortie vient après le limiteur; une valeur positive peut donc recréer
du clipping.

## Temps réel

Le callback audio ne fait aucune opération fichier, réseau ou WPF. Les réglages
sont préparés hors callback, puis publiés atomiquement. Le traitement du buffer
réutilisé n’alloue pas.

Les voies les plus récentes ont validé Audio `27` tests et DSP `88` tests.
Ces compteurs ne sont pas un total de release. Le transit Spotify réel de bout
en bout reste une validation manuelle séparée.
