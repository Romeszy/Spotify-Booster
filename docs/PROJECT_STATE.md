# État du projet

Mise à jour : 27 juillet 2026.

## Environnement vérifié

| Élément | Résultat |
|---|---|
| Racine | `F:\Spotify Booster` |
| Système | Windows 11 Famille 64 bits, build 26200 |
| CPU | AMD Ryzen 7 7800X3D |
| Git | 2.54.0.windows.1 |
| SDK initial | absent du `PATH` et des emplacements standards |
| SDK de build | .NET SDK 8.0.423 installé sans admin sous `.dotnet` |
| Runtime Desktop | 8.0.29 |
| Endpoints Windows actifs observés | 9 |
| Endpoint ressemblant à un câble virtuel | 0 |
| Processus Spotify observés | 7 |

Le SDK local n’est pas versionné. L’installation a utilisé le script officiel Microsoft `dotnet-install.ps1`.

## Phase 0

| Élément | État |
|---|---|
| Solution et huit projets | Terminé |
| DI et logging asynchrone | Terminé |
| Configuration JSON locale | Terminé et testée |
| Contrats et état global | Terminé |
| Gestion globale des erreurs | Terminé |
| WPF sombre/compacte | Terminé, ouverture/fermeture smoke-testée |
| Navigation simple | Terminé |
| Assistant première configuration | Terminé |
| Diagnostic/export texte | Terminé et testé hors dialogue |
| Documentation initiale | Terminé |

## Socle Phase 1

| Élément | État réel |
|---|---|
| Énumération capture/rendu WASAPI | Implémentée, appel NAudio smoke-testé |
| Sélection source/sortie | Implémentée |
| Capture source virtuelle | Implémentée dans le graphe, non testée avec un câble réel ici |
| Retransmission vers sortie | Implémentée dans le graphe, non testée de bout en bout ici |
| Conversion PCM/float, entrée mono/stéréo, sortie stéréo | Implémentée et testée |
| Resampling 44,1/48 kHz | Implémenté et testé hors ligne |
| Correction de dérive | Implémentée, calibration matérielle requise |
| Gain 100–500 % | Implémenté et testé |
| Bass Boost low-shelf | Implémenté et testé |
| Compresseur | Implémenté et testé |
| Limiteur −1 dBFS | Implémenté et testé |
| Vu-mètres/clipping/limitation | Reliés aux métriques réelles |
| Panique 100 % | Implémentée au bloc suivant |
| Réduction automatique après saturation | Implémentée |
| Détection Spotify | Implémentée et test smoke |
| Sauvegarde | Implémentée et testée |
| Reprise après déconnexion | Trois essais implémentés ; annulation et teardown résilient testés, matériel requis |

## Validation automatisée

Résultats frais du 27 juillet 2026 :

- restauration verrouillée : réussie ;
- build Release des huit projets : réussi, zéro avertissement et zéro erreur ;
- tests DSP : 35/35 ;
- tests Audio : 19/19 ;
- tests Core/Infrastructure : 17/17 ;
- total : 71/71 ;
- smoke test WPF : fenêtre `Spotify Booster` visible et réactive, fermeture par `WM_CLOSE`, code de sortie 0 ;
- audit NuGet vulnérabilités : aucune vulnérabilité signalée ;
- audit NuGet dépréciation : trois dépendances transitives legacy, uniquement dans les projets de test.

Le hash du commit et l’état du push sont fournis dans le rapport de livraison final.

## Fonctionnalités volontairement non revendiquées

- aucune preuve de transit Spotify → câble → DSP → casque sur cette machine ;
- aucun test ne démarre réellement `WasapiCapture`/`WasapiOut` faute de route virtuelle disponible ;
- aucun test auditif ou de qualité sonore ;
- aucune exclusivité par application sans routage manuel ;
- aucune capture par PID ;
- aucun pilote/APO propriétaire ;
- aucune installation de câble ;
- aucun installateur ou binaire signé ;
- aucun démarrage Windows ;
- aucune zone de notification ;
- aucune gestion UI complète de profils personnalisés ;
- égaliseur UI multi-bande non livré ;
- limiteur true-peak/look-ahead non livré ;
- aucune mesure SPL ou garantie de sécurité à 500 %.

Le générateur de signaux est un outil de test hors ligne. Il ne remplace pas le signal réel dans l’application.

## Prochaines étapes vers un MVP audio validé

1. Installer manuellement un câble virtuel sur une machine de test dédiée.
2. Router uniquement Spotify vers son endpoint de rendu.
3. Vérifier source → sortie à 100 %, puis 150/200 %.
4. Exécuter 30–60 minutes à 44,1 et 48 kHz.
5. Mesurer latence, charge CPU, dérive, underruns/overruns et fuites.
6. Tester débranchement/rebranchement, veille/reprise et changement de sortie.
7. Tester casque filaire, USB, HDMI et Bluetooth.
8. Ajouter fade-in/fade-out explicite lors des reconstructions.
9. Calibrer la boucle de correction de dérive avec les mesures.
10. Exposer l’égaliseur et la gestion de profils seulement après stabilité du routage.
11. Ajouter tray/autostart, packaging et signature dans une phase séparée.

## Risques ouverts

- `WasapiOut` est stable dans NAudio 2.3 mais considéré legacy dans la future branche NAudio 3 ;
- deux endpoints ont des horloges indépendantes, d’où le besoin de tests longue durée ;
- un pilote qui fournit un format inhabituel peut être refusé ;
- Bluetooth et modes exclusifs peuvent invalider les hypothèses de latence ;
- les fréquences de sortie au-delà de 48 kHz peuvent être ouvertes par WASAPI mais ne sont pas qualifiées par les tests hors ligne actuels ;
- un câble non reconnu par son nom est bloqué dans ce MVP, même s’il est techniquement compatible ;
- les objets de métriques agrégées sont encore créés à environ 10 Hz sur le callback de rendu ;
- les paquets transitifs legacy détectés ne concernent que l’infrastructure de test ;
- la fin de support .NET 8 est prévue le 10 novembre 2026 : migration vers .NET 10 LTS à planifier.
