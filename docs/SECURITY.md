# Sécurité

Revue initiale effectuée le 27 juillet 2026. Elle couvre le socle local ; une nouvelle revue est requise avant distribution publique, signature ou ajout d’un installateur.

## Modèle de menace

Risques prioritaires :

1. niveau sonore excessif ou transitoire brutal ;
2. clipping numérique, NaN/infini ou filtre instable ;
3. boucle de feedback via une mauvaise route ;
4. deadlock/fuite autour des threads audio et objets COM ;
5. configuration locale malformée ou surdimensionnée ;
6. fuite d’informations personnelles dans logs/diagnostics ;
7. téléchargement, commande ou privilège non nécessaire.

## Protections audio implémentées

- premier démarrage forcé à 100 % ;
- gain clampé à 100–500 % ;
- lissage exponentiel du gain ;
- limiteur actif par défaut et forcé au-dessus de 100 % ;
- plafond sample-peak par défaut à −1 dBFS ;
- suppression de NaN/infini et bornage des valeurs extrêmes ;
- réduction automatique de 25 points après limitation répétée ;
- avertissement à partir de 300 % et texte renforcé en mode casque ;
- bouton panique appliqué au bloc audio suivant ;
- refus des sources multicanales non prises en charge ;
- refus d’une source non virtuelle et de toute sortie reconnue comme virtuelle ;
- détection de familles de câbles susceptibles de reboucler ;
- tampon borné et compteurs underrun/overrun ;
- arrêt/libération hors thread UI ;
- trois tentatives de reconstruction après perte d’un endpoint ;
- annulation et attente de toute récupération en cours avant destruction du moteur ;
- libération résiliente : chaque ressource native est tentée même si une précédente échoue, avec état terminal `Faulted` ;
- fermeture contrôlée si l’initialisation WPF échoue, sans session partiellement utilisable ;
- panique à 100 %, arrêt confirmé puis fermeture après une exception UI non gérée.

Limites :

- limiteur sample-peak sans look-ahead ni mesure true-peak ;
- pas de mesure du niveau acoustique SPL ;
- la protection numérique ne protège pas l’audition ;
- le fade-out explicite à l’arrêt/reconfiguration reste à ajouter ;
- la détection de boucle est heuristique, pas une preuve topologique ;
- un câble au nom inhabituel peut être refusé par prudence.

## Protections logicielles implémentées

- aucun trafic réseau dans le code applicatif ;
- aucune télémétrie ;
- aucun secret, token ou identifiant intégré ;
- aucune élévation de privilèges ;
- aucune installation/téléchargement de pilote ;
- aucune écriture dans `Program Files`, le registre ou une zone système ;
- réglages stockés sous `%LOCALAPPDATA%\SpotifyBooster` ;
- chemins de stockage construits en code et non lus depuis le JSON ;
- JSON limité à 1 MiB/profondeur 16, sans commentaires ni propriétés inconnues ;
- nœuds JSON `null` structuraux rejetés avec retour aux réglages sûrs ;
- plages et valeurs finies validées ;
- sauvegarde par fichier temporaire puis remplacement ;
- canal de log borné, compte des entrées abandonnées et rotation effective à 2 MiB pendant l’exécution ;
- chemins connus expurgés des erreurs en mémoire et des logs ;
- rapport de diagnostic sans IDs d’endpoints, arguments ou chemins ;
- export uniquement après choix explicite d’un fichier par l’utilisateur ;
- seule commande shell-like : ouverture de l’URI fixe et non paramétrable `ms-settings:apps-volume`.

## Audio et threading

- aucune opération fichier dans `DataAvailable` ou `ISampleProvider.Read` ;
- aucun accès WPF depuis les threads NAudio ; le Dispatcher relève l’état à 10 Hz ;
- métriques agrégées limitées à environ 10 Hz ;
- réglages DSP publiés via échange atomique ;
- start/stop sérialisés par un sémaphore ;
- opérations bloquantes NAudio déplacées dans `Task.Run` ;
- objets capture, rendu et endpoints libérés en ordre contrôlé.

Le logger crée une chaîne de message lors d’une réduction automatique rare, mais n’effectue pas l’écriture disque sur le thread audio.

Le DSP pur n’alloue pas sur des buffers réutilisés. La création des objets d’état agrégés à 10 Hz se fait encore depuis le callback de rendu ; elle est recensée comme optimisation temps réel restante.

## Données collectées

En local uniquement :

- préférences et IDs d’endpoints sélectionnés dans `settings.json` ;
- transitions/erreurs techniques dans les logs ;
- processus Spotify (nom, PID, distribution, heure si accessible) en mémoire ;
- métriques audio agrégées en mémoire.

Le rapport exporté exclut les IDs de périphériques. Aucun fichier n’est envoyé.

## Dépendances

Les versions sont épinglées et les fichiers `packages.lock.json` sont générés. Les dépendances directes sont limitées à NAudio WASAPI, Microsoft.Extensions et xUnit pour les tests. Voir [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

Audit NuGet du 27 juillet 2026 : aucun package vulnérable signalé. Trois packages transitifs réservés aux tests sont marqués legacy/dépréciés ; ils sont listés dans les notices tierces.

## Points avant distribution

- ajouter une licence projet ;
- réaliser tests auditifs et matériels documentés ;
- publier self-contained ou vérifier le runtime installé ;
- signer les binaires et l’éventuel installateur ;
- ajouter une analyse de dépendances/SBOM ;
- revoir la politique de conservation des logs ;
- vérifier les exigences juridiques liées au traitement de contenus Spotify ;
- tester veille/reprise, RDP, Bluetooth et changement de session Windows.
