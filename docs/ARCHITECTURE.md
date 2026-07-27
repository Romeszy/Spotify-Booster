# Architecture

## But et frontière du MVP

Le MVP isole Spotify par **routage Windows explicite** vers un endpoint virtuel. La détection du processus Spotify n’est qu’un signal d’interface. Elle ne constitue jamais une preuve sur l’origine du son.

```text
Spotify Desktop
  → endpoint de rendu du câble virtuel (choisi manuellement dans Windows)
  → endpoint de capture correspondant
  → SpotifyBooster.Audio
  → SpotifyBooster.Dsp
  → endpoint de rendu physique
```

L’application ne change ni le volume principal Windows, ni le volume de session Spotify, ni le périphérique par défaut.

## Projets

| Projet | Cible | Responsabilité |
|---|---|---|
| `SpotifyBooster.App` | `net8.0-windows10.0.19041.0` | WPF, MVVM, navigation, composition DI et passage vers le Dispatcher |
| `SpotifyBooster.Core` | `net8.0` | contrats, modèles, réglages versionnés, validation et état global |
| `SpotifyBooster.Audio` | `net8.0-windows10.0.19041.0` | adaptateurs NAudio/WASAPI, formats, tampon, resampling et cycle moteur |
| `SpotifyBooster.Infrastructure` | `net8.0-windows10.0.19041.0` | persistance JSON, processus Spotify, logs, diagnostic et intégration Windows |
| `SpotifyBooster.Dsp` | `net8.0` | algorithmes DSP purs, sans WPF ni NAudio |
| `SpotifyBooster.Core.Tests` | Windows/.NET 8 | validation, persistance, diagnostic et détection |
| `SpotifyBooster.Dsp.Tests` | `.NET 8` | DSP, signaux hors ligne et non-allocation |
| `SpotifyBooster.Audio.Tests` | Windows/.NET 8 | garde-fous de route, tampon, formats, resampling et énumération |

## Dépendances

```text
App
 ├── Core
 ├── Audio ── Core + Dsp + NAudio.Wasapi
 ├── Infrastructure ── Core
 └── Dsp
```

Les types NAudio ne traversent pas les contrats de `Core`. `IAudioEngine` et `IAudioDeviceService` sont les frontières remplaçables. Une implémentation APO ou propriétaire ultérieure pourra donc remplacer le moteur WASAPI sans déplacer la logique WPF, les réglages ou le DSP.

## Contrats principaux

- `IAudioEngine` : start/stop, mise à jour DSP, panique, état et métriques ;
- `IAudioDeviceService` : endpoints de capture et de rendu ;
- `ISettingsService` : lecture/écriture des réglages validés ;
- `ISpotifyProcessDetector` : snapshot sans chemin fixe ;
- `IErrorSink` et `IDiagnosticReportService` : erreurs bornées et rapport expurgé ;
- `ApplicationState` : état global thread-safe indépendant de WPF.

## Modèle d’exécution

| Contexte | Travail |
|---|---|
| Thread de capture NAudio | copie le bloc dans le tampon borné et incrémente les compteurs |
| Thread de rendu NAudio | tire les échantillons, rééchantillonne, exécute le DSP et alimente WASAPI |
| Tâches de cycle de vie | créent, arrêtent et libèrent les objets COM/NAudio hors du thread UI |
| Dispatcher WPF | propriétés liées, commandes et vues |
| Logger asynchrone | écrit les transitions et erreurs hors callback audio |

Les callbacks audio ne lisent aucun fichier, ne modifient pas directement WPF et n’attendent aucun verrou long. Le Dispatcher relève les métriques depuis l’état du moteur à 10 Hz ; il ne reçoit pas chaque bloc audio.

## États du moteur

```text
Stopped → Starting → Running → Stopping → Stopped
                      │
                      ├── DeviceLost → tentative de reconstruction (3 essais)
                      └── Faulted
```

Une modification DSP est publiée au `DspSampleProvider` et appliquée au prochain bloc. Les sélecteurs de périphériques sont verrouillés pendant que le moteur fonctionne ; l’utilisateur doit d’abord l’arrêter, choisir la nouvelle route, puis le redémarrer, ce qui reconstruit entièrement le graphe.

## Démarrage et arrêt

### Démarrage

1. validation stricte de la route et des paramètres ;
2. résolution fraîche des IDs d’endpoints ;
3. refus des sources multicanales et des routes connues comme rebouclées ;
4. construction du tampon, des convertisseurs, du resampler et du DSP ;
5. démarrage de la capture ;
6. préremplissage jusqu’à environ 80 ms, borné par un timeout d’une seconde ;
7. démarrage de la sortie partagée.

### Arrêt

1. état `Stopping` et blocage des reprises concurrentes ;
2. désabonnement des événements ;
3. arrêt capture puis rendu hors UI ;
4. remise à zéro DSP/tampon ;
5. libération des clients, endpoints et objets COM ;
6. état `Stopped`.

Le socle ne réalise pas encore de fade-out de sortie à l’arrêt ; le gain panique est toutefois ramené à 1,0 avant la prochaine réutilisation du pipeline.

## Persistance et logs

- réglages : `%LOCALAPPDATA%\SpotifyBooster\settings.json` ;
- logs : `%LOCALAPPDATA%\SpotifyBooster\logs\spotify-booster.log` ;
- taille JSON maximale : 1 MiB ;
- profondeur JSON maximale : 16 ;
- membres JSON inconnus refusés ;
- fichier temporaire puis remplacement ;
- log borné à 2 MiB avec une génération précédente ;
- écritures de log via canal borné, jamais directement depuis le callback audio.

## Évolutions prévues

- tests matériels longue durée et réglage de la correction de dérive ;
- surveillance native des notifications de périphériques ;
- égaliseur multi-bande exposé ;
- profils nommés éditables ;
- zone de notification et démarrage automatique ;
- véritable limiteur look-ahead/true-peak si les mesures CPU le permettent ;
- nouvel adaptateur audio remplaçant le câble virtuel, sans casser `Core`.
