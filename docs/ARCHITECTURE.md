# Architecture

## Frontière du produit

Spotify Booster isole Spotify par routage Windows manuel vers un endpoint virtuel. La détection du processus Spotify renseigne l’interface ; elle ne prouve jamais l’origine du signal audio.

```text
Spotify Desktop
  → endpoint de rendu du câble virtuel
  → endpoint de capture correspondant
  → SpotifyBooster.Audio
  → SpotifyBooster.Dsp
  → endpoint de rendu physique
```

L’application ne change ni le périphérique Windows par défaut, ni le volume principal, ni le volume de session Spotify.

## Projets

| Projet | Cible | Responsabilité |
|---|---|---|
| `SpotifyBooster.App` | `net8.0-windows10.0.19041.0` | WPF, MVVM, navigation, assistant, tray, raccourcis et composition DI |
| `SpotifyBooster.Contracts` | `net8.0` | contrats Auth, API, entitlements et updates sans WPF |
| `SpotifyBooster.Core` | `net8.0` | abstractions, modèles, réglages V3, validation et état global |
| `SpotifyBooster.Audio` | `net8.0-windows10.0.19041.0` | NAudio/WASAPI, endpoints, tampon, formats, resampling et cycle du moteur |
| `SpotifyBooster.Infrastructure` | `net8.0-windows10.0.19041.0` | JSON, migration V1, logs, diagnostic, processus Spotify, registre et URI Windows |
| `SpotifyBooster.Dsp` | `net8.0` | DSP pur, sans dépendance WPF ou NAudio |
| `SpotifyBooster.Auth` | `net8.0` | adaptateur Supabase Auth, session en mémoire |
| `SpotifyBooster.Api` | `net8.0` | API ASP.NET Core, JWT/JWKS, Data API et readiness |
| `SpotifyBooster.Core.Tests` | Windows/.NET 8 | réglages, migration, persistance, diagnostic, logs et intégration Windows |
| `SpotifyBooster.Dsp.Tests` | `.NET 8` | conversions, filtres, dynamique, pipeline et signaux hors ligne |
| `SpotifyBooster.Audio.Tests` | Windows/.NET 8 | route, formats, tampon, mapping DSP, récupération et énumération |

```text
App
 ├── Core
 ├── Audio ── Core + Dsp + NAudio.Wasapi
 ├── Infrastructure ── Core
 └── Dsp
```

Les types NAudio ne traversent pas les contrats de `Core`. `IAudioEngine` et `IAudioDeviceService` définissent la frontière audio remplaçable.

## Pipeline DSP

```text
DC blocker
  → gain principal lissé
  → low-shelf Bass Boost
  → égaliseur low/mid/high
  → compresseur optionnel
  → soft clipper optionnel
  → limiteur optionnel
  → gain de sortie lissé
```

Le décibel est la valeur canonique. Le pourcentage linéaire affiché est dérivé avec `10^(dB/20) × 100`.

Les trois modes sont des choix explicites :

| Mode | Gain principal | Comportement lors de la sélection |
|---|---:|---|
| Protégé | `0 à +14 dB` | force soft clipper et limiteur actifs |
| Direct | `0 à +14 dB` | désactive compresseur, soft clipper et limiteur |
| Expert brut | `0 à +75 dB`, par pas de `0,5 dB` | exige un acquittement initial et désactive les trois étages dynamiques |

Après le changement de mode, Direct et Expert brut permettent d’activer manuellement les étages souhaités. Le plafond Expert est fixe à `+75 dB`. Les modes Protégé et Direct restent bornés à `+14 dB` et ne sont pas quantifiés au demi-dB.

Le gain principal est lissé en dB et converti en facteur linéaire en double précision. À l’ouverture ou à la reconstruction du pipeline, un gain mémorisé non nul est rampé depuis `0 dB` jusqu’à sa cible, sans modifier cette cible. Tout résultat fini représentable est conservé ; seul un overflow numérique est saturé à `±float.MaxValue`. NaN et Inf sont assainis et comptés.

`+75 dB` vaut environ `×5623,413`. Cette valeur est dangereuse, n’est pas sûre pour l’audition et peut saturer immédiatement. La stabilité numérique ne constitue pas une protection acoustique.

Il n’existe aucune boucle de réduction persistante du gain. Le lissage rejoint la valeur demandée ; seule la commande panique remet explicitement le gain principal et le gain de sortie à `0 dB`.

## Contrats principaux

- `IAudioEngine` : start, stop, mise à jour DSP, panique, état et métriques ;
- `IAudioDeviceService` : inventaire capture/rendu et résolution fraîche par ID ;
- `ISettingsService` : chargement, migration, validation et sauvegarde ;
- `IStartupService` : vérification et écriture du démarrage utilisateur ;
- `ISpotifyProcessDetector` : snapshot des processus sans chemin fixe ;
- `IErrorSink` et `IDiagnosticReportService` : erreurs bornées et export expurgé ;
- `ApplicationState` : état thread-safe indépendant de WPF.

Les frontières commerciales sont détaillées dans
[COMMERCIAL_ARCHITECTURE.md](COMMERCIAL_ARCHITECTURE.md).

## Modèle d’exécution

| Contexte | Travail |
|---|---|
| Callback de capture NAudio | copie dans le tampon borné et compte les overruns |
| Callback de rendu NAudio | lit, rééchantillonne, traite le DSP et alimente WASAPI |
| Tâches de cycle de vie | créent, arrêtent et libèrent les objets audio hors UI |
| Dispatcher WPF | commandes, propriétés liées et lecture des métriques à 10 Hz |
| Logger asynchrone | écrit transitions et erreurs via un canal borné |
| Boucle UI Spotify | actualise les processus toutes les deux secondes |

Les callbacks audio ne lisent aucun fichier et n’accèdent pas à WPF. Les réglages DSP sont préparés hors callback, publiés par échange atomique et appliqués au bloc suivant. `DspSampleProvider.Read` reste sans allocation, y compris au premier bloc après une mise à jour ou une panique. Il publie un snapshot valeur ; un timer moteur distinct construit et diffuse les objets de métriques hors callback.

## Cycle du moteur

```text
Stopped → Starting → Running → Stopping → Stopped
                      │
                      ├── DeviceLost → reconstruction, trois essais
                      └── Faulted
```

Au démarrage, le moteur :

1. valide les réglages et la route ;
2. résout à nouveau les endpoints ;
3. refuse le multicanal et les routes virtuelles susceptibles de boucler ;
4. construit tampon, conversions, resampler et DSP ;
5. démarre la capture ;
6. préremplit environ `80 ms`, avec timeout d’une seconde ;
7. démarre la sortie WASAPI partagée.

Au changement de périphérique, l’utilisateur arrête le moteur, choisit la route, puis le redémarre. Le graphe est alors reconstruit.

## Services du bureau

- `TrayIconService` utilise `System.Windows.Forms.NotifyIcon`, deux icônes locales et un menu reconstruit depuis l’état courant.
- `GlobalHotkeyService` utilise `RegisterHotKey`/`UnregisterHotKey` avec `MOD_NOREPEAT`.
- `StartupService` écrit une valeur utilisateur `Spotify Booster` sous `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- La commande enregistrée pointe vers l’exécutable courant et ajoute `--minimized`.
- Le comportement de fermeture peut masquer dans le tray, demander ou quitter.

Ces fonctions n’installent aucun service et ne demandent aucune élévation.

## Persistance

Emplacement standard :

```text
%LOCALAPPDATA%\SpotifyBooster\settings.json
%LOCALAPPDATA%\SpotifyBooster\logs\spotify-booster.log
```

Si un fichier vide nommé `portable.mode` se trouve à côté de l’exécutable, les réglages sont stockés dans `Data\settings.json` et les logs dans `Data\logs` sous le dossier de l’application.

Le schéma courant est V2. Lorsqu’un fichier V1 valide est chargé :

1. `GainPercent` est limité à `100–500` ;
2. la valeur est convertie en dB par `20 × log10(percent/100)` ;
3. le mode devient Protégé si l’ancien limiteur était actif, sinon Direct ;
4. une sauvegarde unique `settings.schema1.backup.json` est créée ;
5. le fichier principal est réécrit au format V2.

Le JSON est limité à 1 MiB et à une profondeur de 16. Les propriétés inconnues, commentaires, virgules finales, nœuds structuraux `null` et valeurs hors plage sont refusés. Une configuration invalide ramène les réglages sûrs par défaut.

## Dépendances et support

NAudio 2.3.0 fournit WASAPI et le resampler WDL. Les bibliothèques Microsoft.Extensions 10.0.10 prennent en charge `net8.0`. Les versions sont centralisées et verrouillées. `RuntimeIdentifiers` fixe `win-x64`, dont les graphes sont consignés dans les fichiers de lock.

.NET 8 atteint sa fin de support le 10 novembre 2026. L’architecture reste sur .NET 8 pour cette V2 ; la migration de maintenance doit être planifiée séparément. Voir [Licences tierces](../THIRD_PARTY_NOTICES.md).

## Hors frontière

- aucune API réseau, télémétrie ou mise à jour automatique ;
- aucun pilote, APO, service Windows ou modification automatique de route ;
- aucun traitement exclusif par PID ;
- aucun limiteur true-peak ou look-ahead ;
- aucune mesure SPL ;
- aucun artefact signé revendiqué tant que la checklist de release n’est pas clôturée.
