# Sécurité

Dernière mise à jour documentaire : 28 juillet 2026. Cette revue couvre le code V2 local. Elle ne vaut pas approbation d’un paquet, d’un installateur ou d’une distribution publique.

## Modèle de menace

Risques prioritaires :

1. niveau sonore excessif ou transitoire brutal ;
2. clipping, NaN/infini ou filtre instable ;
3. boucle de feedback par mauvaise route ;
4. deadlock ou fuite autour de WASAPI/COM ;
5. configuration locale malformée ou surdimensionnée ;
6. fuite d’informations dans les logs/diagnostics ;
7. persistance Windows non désirée ;
8. pilote, téléchargement, commande ou privilège non nécessaire.

## Modes audio explicites

| Mode | Plafond principal | Protections |
|---|---:|---|
| Protégé | `+14 dB` | soft clipper et limiteur imposés actifs |
| Direct | `+14 dB` | aucune dynamique imposée |
| Expert brut | plafond configurable `+25 à +1000 dB`, `+1000 dB` par défaut | acquittement initial, aucune protection cachée |

Le réglage avancé permet de choisir le plafond Expert de `+25 à +1000 dB`. `+1000 dB` correspond à un facteur mathématique `10^50`, soit `10^52 %`, et ne constitue pas une recommandation d’usage. Au-delà de la saturation numérique, le résultat est surtout de la distorsion ou de la limitation ; cette valeur ne représente pas directement le volume perçu et ne fournit aucune puissance physique supplémentaire.

Le gain demandé ne fait l’objet d’aucune baisse persistante automatique. Les étages dynamiques modifient le signal au fil des échantillons ; ils ne réécrivent pas la consigne de gain. La panique est une action explicite qui ramène le gain principal et le gain de sortie à `0 dB`.

Expert brut supprime les protections dynamiques imposées, mais ne désactive pas les invariants de stabilité numérique : le gain principal est lissé en dB et calculé en double précision, NaN/infinis restent assainis et les magnitudes intermédiaires sont bornées à `±64` avant le retour en float32.

La borne `±64` empêche les valeurs non finies ; elle ne garantit pas le respect du plafond de sortie. Expert brut désactive le limiteur lors de sa sélection et peut donc dépasser son plafond configuré.

## Protections audio

- premier démarrage et fin d’assistant à `0 dB` en mode Protégé ;
- valeurs dB bornées et finies ;
- lissage du gain principal et du gain de sortie ;
- rampe du gain mémorisé depuis `0 dB` à l’ouverture ou à la reconstruction du pipeline, sans altérer la cible ;
- DC blocker actif par défaut ;
- soft clipper et limiteur actifs par défaut ;
- impossibilité de désactiver ces deux étages en mode Protégé ;
- gain de sortie positif refusé en mode Protégé ;
- avertissement explicite pour un gain de sortie positif en Direct ou Expert brut ;
- plafond sample-peak par défaut à `−1 dBFS` ;
- NaN/infinis remplacés par zéro ;
- magnitudes intermédiaires bornées ;
- avertissement renforcé au casque dès un affichage linéaire d’environ 300 % ;
- avertissement renforcé en Expert brut à partir de `+100 dB`, lorsque la saturation interne domine ;
- confirmation avant le premier passage en Expert brut ;
- rejet d’une configuration Expert brut persistée sans acquittement préalable ;
- panique appliquée au bloc audio suivant avec lissage court ;
- tampon borné et compteurs d’incidents ;
- refus du multicanal non pris en charge ;
- refus des routes non virtuelles/virtuelles susceptibles de boucler ;
- arrêt et libération hors thread UI ;
- trois tentatives de reconstruction après perte d’un endpoint ;
- arrêt audio tenté avant fermeture sur exception UI non gérée.

Limites :

- limiteur sample-peak, sans look-ahead et non true-peak ;
- gain de sortie appliqué après le limiteur ;
- aucune mesure SPL ;
- aucune garantie de sécurité auditive ou matérielle ;
- détection de boucle fondée sur les métadonnées/noms, pas sur une topologie prouvée ;
- aucun fade-out explicite garanti à chaque arrêt/reconfiguration ;
- tests matériels longue durée encore requis.

## Route et isolation

Spotify Booster traite tout ce qui atteint l’endpoint de capture choisi. L’indicateur de processus Spotify n’autorise pas le moteur et ne filtre aucun flux.

La source doit sembler virtuelle et la sortie physique. Cette politique réduit les erreurs microphone → enceintes et les boucles évidentes, mais peut refuser un pilote au nom inhabituel.

## Données et réseau

Le code applicatif :

- ne contient aucun client réseau, service cloud, compte ou télémétrie ;
- ne capture ni n’enregistre le contenu audio ;
- ne contient aucun secret ou token ;
- n’installe ni pilote, ni service, ni mise à jour automatique.

Un clic explicite sur **Ouvrir le site officiel VB-Audio** délègue au navigateur système l’URL fixe `https://vb-audio.com/Cable/`. Les boutons Windows ouvrent les URI fixes `ms-settings:apps-volume` et `ms-settings:sound`.

Données locales :

- préférences et IDs d’endpoints dans `settings.json` ;
- transitions et erreurs techniques dans le log ;
- noms/PID/distribution des processus Spotify en mémoire ;
- métriques audio agrégées en mémoire ;
- rapport diagnostic uniquement après choix explicite d’un fichier.

Le rapport exclut les IDs d’endpoints, chemins, arguments de processus et contenu audio. Relisez néanmoins le rapport avant partage, car il contient les noms visibles des périphériques et les PID.

## Persistance et fichiers

- emplacement standard sous `%LOCALAPPDATA%\SpotifyBooster` ;
- mode portable sous `Data` uniquement si `portable.mode` existe près de l’exécutable, avec réglages dans `Data\settings.json` et logs dans `Data\logs` ;
- JSON limité à 1 MiB et profondeur 16 ;
- propriétés inconnues, commentaires, virgules finales et nœuds structuraux `null` refusés ;
- validation des enums, longueurs, doublons et plages numériques ;
- sauvegarde par fichier temporaire puis remplacement ;
- backup unique avant migration V1 → V2 ;
- log asynchrone borné, rotation à 2 MiB et une génération précédente ;
- chemins utilisateur connus expurgés des erreurs et logs.

## Registre et privilèges

L’option de démarrage écrit uniquement :

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

La valeur contient le chemin absolu de l’exécutable courant, entre guillemets, suivi de `--minimized`. Le code refuse un chemin non absolu, non `.exe` ou contenant guillemet/retour de ligne. L’écriture est vérifiée après modification.

Aucune élévation, aucune écriture dans `HKLM` et aucune écriture dans `Program Files` ne sont effectuées.

L’installateur généré avec NSIS 3.12 utilise lui aussi un niveau utilisateur. Il installe sous `%LOCALAPPDATA%\Programs\Spotify Booster`, écrit ses informations de désinstallation sous HKCU et propose une entrée HKCU Run. Il n’embarque aucun pilote. Son cycle complet reste à tester sur une machine propre.

## Threading audio

- aucune I/O fichier dans `DataAvailable` ou `ISampleProvider.Read` ;
- aucun accès WPF depuis les threads NAudio ;
- publication des réglages par échange atomique ;
- start/stop sérialisés ;
- opérations NAudio potentiellement bloquantes exécutées hors UI ;
- récupération concurrente gardée et attendue avant destruction ;
- libération résiliente de chaque ressource native.

`DspSampleProvider.Read` traite les buffers sans allocation, y compris après une mise à jour de réglages ou une panique. Les snapshots valeur sont consommés par un timer moteur à environ 10 Hz ; la construction et la diffusion des objets de métriques ont lieu hors callback audio.

## Dépendances

Les versions NuGet sont centralisées et verrouillées. NAudio et Microsoft.Extensions utilisent la licence MIT ; xUnit utilise Apache-2.0 ; les composants de test Microsoft listés utilisent la licence MIT. Les obligations de notice et de redistribution sont détaillées dans [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

.NET 8 atteint sa fin de support le 10 novembre 2026. Une release maintenue au-delà de cette date doit migrer vers un runtime pris en charge et refaire les audits.

## Gates avant distribution

- conserver le build, les tests TRX et les audits de dépendances pour chaque paquet issu d’un commit propre ;
- effectuer les tests audio réels documentés ;
- vérifier le paquet sur une machine propre ;
- générer SBOM, notices et textes de licences ;
- signer le portable et l’installateur générés ;
- consigner les hashes des artefacts finaux ;
- obtenir la revue juridique requise pour les marques et le traitement de contenus Spotify ;
- clôturer [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
