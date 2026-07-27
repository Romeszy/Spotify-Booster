# État du projet

Mise à jour : 28 juillet 2026.

## Références

| Élément | Valeur |
|---|---|
| Racine de travail | `F:\Spotify Booster` |
| Branche V2 | `codex/spotify-booster-desktop-v2` |
| Baseline MVP | `ab2923cf44756be7b8446d67e6cbe15123cee3d2` |
| Version applicative déclarée | `0.2.0` |
| SDK fixé | .NET SDK `8.0.423` |
| Cible bureau | `net8.0-windows10.0.19041.0` |

La branche V2 est fondée sur le commit MVP `ab2923c`. La livraison est intégrée dans un commit cohérent, puis le package est régénéré depuis ce worktree propre. Le rapport sous `artifacts\reports` constitue la source de vérité pour le commit, la version informative et les compteurs de tests ; le manifeste sous `artifacts\checksums` porte les hashes.

## Baseline vérifiée

Le commit `ab2923c` sur `codex/spotify-booster-mvp` avait :

- restauration réussie ;
- build Release propre ;
- `71/71` tests automatisés réussis ;
- smoke test de la fenêtre WPF réussi ;
- aucun câble virtuel disponible sur la machine de référence.

Cette preuve concerne le MVP baseline, pas automatiquement l’ensemble des modifications V2 non commitées.

## Fonctionnalités V2 implémentées

| Domaine | État du code |
|---|---|
| Gain canonique en dB | Implémenté |
| Plage standard `0 à +14 dB` | Implémentée |
| Expert brut avec plafond configurable `+25 à +1000 dB` | Implémenté, `+1000 dB` par défaut |
| Borne architecturale `+1000 dB` | Validée par le modèle |
| Lissage du gain principal en dB, calcul double et borne interne `±64` | Implémentés |
| Modes Protégé, Direct, Expert brut | Implémentés et explicites |
| Absence de réduction persistante automatique | Implémentée |
| DC blocker modulaire | Implémenté |
| Bass Boost `35–200 Hz`, jusqu’à `+18 dB` | Implémenté |
| Égaliseur trois bandes | Implémenté |
| Compresseur optionnel | Implémenté |
| Soft clipper optionnel | Implémenté |
| Limiteur sample-peak optionnel | Implémenté |
| Gain de sortie | Implémenté |
| Gain de sortie Protégé borné à `0 dB` max | Implémenté et testé |
| Avertissement gain de sortie positif Direct/Expert | Implémenté |
| Migration JSON V1 → V2 avec backup | Implémentée |
| Profils persistés alimentant l’UI Bass Boost | Implémentés |
| Assistant de routage en sept étapes | Implémenté |
| Zone de notification et menu rapide | Implémentés |
| Comportement de fermeture configurable | Implémenté |
| Démarrage utilisateur via HKCU Run | Implémenté |
| Démarrage masqué `--minimized` | Implémenté |
| Raccourcis globaux | Implémentés |
| Diagnostic V2 et export texte | Implémentés |
| Icône locale originale et variantes | Intégrées |
| VB-CABLE | Externe, manuel, non distribué |
| Réseau/télémétrie | Aucun dans le code applicatif |
| Pilote/APO/service | Aucun |
| Scripts build/test/release | Implémentés |
| Lecture DSP sans allocation après update/panique | Implémentée et testée |
| Publication des métriques hors callback | Implémentée |
| Portable self-contained single-file | Généré |
| Installateur NSIS 3.12 utilisateur | Généré |

## Validation automatisée V2

Vérification locale fraîche du 28 juillet 2026 :

- restauration verrouillée : réussie ;
- build Release : réussi ;
- avertissements : 0 ;
- erreurs : 0 ;
- DSP : `71/71` ;
- Audio : `27/27` ;
- Core/Infrastructure : `64/64` ;
- total : `162/162`.

Les commandes exécutées sont :

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln --locked-mode
& ".\.dotnet\dotnet.exe" build .\SpotifyBooster.sln --configuration Release --no-restore
& ".\.dotnet\dotnet.exe" test .\SpotifyBooster.sln --configuration Release --no-build
```

Le rapport de release doit réexécuter la suite sur le commit final et consigner tout écart par rapport à ces 162 tests.

## Validation matérielle

La route audio a été ouverte puis arrêtée sans erreur, mais aucun signal n’a été injecté : le vu-mètre est resté à `−∞`. Aucun test audio réel ne peut donc être revendiqué.

Restent non réalisés :

- transit Spotify → VB-CABLE → DSP → casque/enceintes ;
- qualité sonore et comparaison des trois modes ;
- session longue 30–60 minutes ;
- dérive réelle entre horloges ;
- récupération après débranchement, veille/reprise et changement de session ;
- sorties filaire, USB, HDMI et Bluetooth ;
- comportement auditif du soft clipper/limiteur ;
- niveau acoustique.

Le transit audio de bout en bout reste non validé.

## Packaging et release

Package de livraison du 28 juillet 2026 :

- `artifacts\portable\SpotifyBooster-Portable-x64.exe` généré ;
- `artifacts\installer\SpotifyBooster-Setup-x64.exe` généré avec NSIS 3.12 ;
- `artifacts\checksums\SHA256SUMS.txt` généré en UTF-8 sans BOM ;
- `artifacts\reports\release-report.txt` généré avec branche, commit et état du worktree ;
- trois rapports TRX conservés sous `artifacts\reports\tests` ;
- rapport `Status: Complete` et provenance `Worktree: clean` ;
- `LICENSE.txt`, `THIRD_PARTY_NOTICES.md` et `licenses\` inclus ;
- `13/13` entrées du manifeste SHA-256 recalculées et vérifiées ;
- métadonnées de version des deux exécutables vérifiées ;
- installation puis désinstallation silencieuses testées sur cette machine, avec codes `0/0` et aucun résidu ;
- binaires non signés.

Ces fichiers forment un candidat de livraison reproductible, mais les gates suivants restent ouverts :

- test sur machine propre ;
- signature Authenticode ;
- SBOM ;
- smoke test portable isolé du package final ;
- QA manuelle signée.

Ne pas ajouter de valeurs fictives. Utiliser [PACKAGING.md](PACKAGING.md) et [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

## Limites non revendiquées

- aucune capture par PID ;
- aucune exclusivité par application sans routage Windows manuel ;
- aucune installation ou redistribution de VB-CABLE ;
- aucun changement silencieux de route Windows ;
- aucun limiteur true-peak/look-ahead ;
- aucune mesure SPL ou garantie auditive ;
- aucune mise à jour automatique ;
- aucune télémétrie ;
- aucun binaire signé à ce stade ;
- aucun test machine propre ;
- aucune validation installation/désinstallation sur machine propre ;
- aucune validation DPI 125 % ou 150 %.

## Risques ouverts

- calibration matérielle de la correction de dérive ;
- formats/pilotes inhabituels ;
- latence Bluetooth/HDMI ;
- sortie positive après le limiteur pouvant recréer du clipping ;
- heuristique de détection des périphériques virtuels ;
- compatibilité et durée de vie du moteur `WasapiOut` ;
- fin de support .NET 8 le 10 novembre 2026 ;
- audit de dépendances et validation de distribution à refaire sur le paquet final.

## Stop condition V2

La V2 ne doit être déclarée prête à distribuer que lorsque :

1. build et suite complète sont revalidés sur le commit final ;
2. transit audio réel et tests de récupération sont consignés ;
3. le paquet final passe sur une machine propre ;
4. SBOM, signature et hashes finaux sont présents ;
5. la checklist de release ne contient plus de gate bloquant.
