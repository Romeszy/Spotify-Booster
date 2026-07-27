# Checklist de release

Cette checklist est un gate. Une case non cochée reste un travail à faire ; elle ne constitue pas une preuve implicite.

## Identité

- [x] Version `0.2.0` du `.csproj` confirmée
- [x] Branche et commit consignés dans le rapport de provenance
- [x] package régénéré depuis un worktree propre
- [x] `LICENSE.txt` ajouté et embarqué
- [ ] Notes de version rédigées

## Build et tests

- [x] `restore --locked-mode` réussi
- [x] build Release réussi
- [x] zéro avertissement et zéro erreur
- [x] tests Core/Infrastructure `64/64`
- [x] tests DSP `71/71`
- [x] tests Audio `27/27`
- [x] total V2 `162/162`
- [x] 162 tests revalidés par le script de release après le commit
- [ ] smoke test WPF démarrage/fermeture réussi
- [ ] audit NuGet vulnérabilités refait
- [ ] audit NuGet dépréciation refait

## DSP

- [x] `0 dB = ×1 = 100 %`
- [x] plage standard bornée à `+14 dB`
- [x] plafond Expert brut configurable de `+25 à +1000 dB`, avec `+1000 dB` livré par défaut
- [x] `+1000 dB = ×10^50 = 10^52 %`
- [x] gain principal lissé en dB, calcul double et magnitudes internes bornées à `±64`
- [x] gain mémorisé non neutre rampé depuis `0 dB` à l’ouverture/reconstruction, sans modifier la cible
- [x] Expert brut peut dépasser le plafond du limiteur, qui n’est pas imposé dans ce mode
- [ ] confirmation Expert brut vérifiée
- [x] Protégé force soft clipper et limiteur
- [x] Protégé borne le gain de sortie à `0 dB` maximum
- [x] Direct/Expert n’appliquent aucune réduction cachée
- [x] Direct/Expert avertissent si le gain de sortie est positif
- [x] aucune consigne de gain modifiée automatiquement
- [x] panique remet gain principal et sortie à `0 dB`
- [x] Bass Boost 35/200 Hz et +18 dB vérifié
- [x] égaliseur trois bandes vérifié
- [x] compresseur, soft clipper et limiteur vérifiés séparément
- [x] `DspSampleProvider.Read` sans allocation après update/panique
- [x] métriques construites et publiées hors callback audio

## Audio réel

Route ouverte puis arrêtée sans erreur, mais aucun signal injecté et vu-mètre à `−∞`.

- [ ] VB-CABLE installé manuellement depuis l’éditeur
- [ ] seul Spotify routé vers CABLE Input
- [ ] CABLE Output capturé par Booster
- [ ] transit vers casque/enceintes confirmé
- [ ] aucun écho ou feedback
- [ ] test à 44,1 kHz
- [ ] test à 48 kHz
- [ ] session 30–60 minutes
- [ ] underruns/overruns consignés
- [ ] débranchement/rebranchement
- [ ] veille/reprise
- [ ] sortie filaire
- [ ] sortie USB/HDMI si supportée
- [ ] Bluetooth testé ou explicitement exclu
- [ ] comportement casque à faible volume validé

## Bureau et Windows

- [ ] assistant sept étapes terminé au clavier
- [ ] tray actif/inactif et double-clic
- [ ] menu tray complet
- [ ] MinimizeToTray
- [ ] Ask
- [ ] Exit
- [ ] démarrage HKCU Run
- [ ] `--minimized`
- [ ] suppression de l’entrée de démarrage
- [ ] traitement automatique avec route valide
- [ ] comportement avec route absente
- [ ] six raccourcis globaux
- [ ] conflit de raccourci signalé
- [ ] export diagnostic relu
- [ ] aucune donnée audio enregistrée

## Migration et stockage

- [ ] réglages neufs en schéma V2
- [ ] migration V1 `100 % → 0 dB`
- [ ] migration V1 `500 % → +13,9794 dB`
- [ ] backup `settings.schema1.backup.json`
- [ ] JSON invalide → valeurs sûres
- [ ] stockage `%LOCALAPPDATA%`
- [ ] mode portable avec `portable.mode`
- [ ] dossier portable non inscriptible testé
- [ ] rotation des logs

## Interface et accessibilité

- [ ] taille minimale `880 × 620`
- [ ] mise à l’échelle 125 %
- [ ] mise à l’échelle 150 %
- [ ] ordre de tabulation
- [ ] focus visible
- [ ] Narrateur Windows
- [ ] overlay Expert brut au clavier
- [ ] contraste des états
- [ ] textes français relus
- [ ] icônes originales correctement embarquées

## Packaging

- [x] `artifacts\portable\SpotifyBooster-Portable-x64.exe` produit
- [x] `artifacts\installer\SpotifyBooster-Setup-x64.exe` produit avec NSIS 3.12
- [x] `artifacts\reports\release-report.txt` indique `Status: Complete`
- [x] provenance branche/commit/worktree incluse
- [x] `artifacts\checksums\SHA256SUMS.txt` produit sans BOM
- [x] architecture `win-x64` confirmée et verrouillée
- [x] contenu du dossier publié inspecté
- [x] `LICENSE.txt`, `THIRD_PARTY_NOTICES.md` et `licenses\` embarqués
- [x] aucune donnée utilisateur embarquée
- [x] aucun pilote ou installateur VB-CABLE embarqué
- [ ] test sur machine propre
- [x] installation et désinstallation silencieuses testées sur cette machine, sans résidu
- [x] métadonnées de version des deux exécutables vérifiées

## Sécurité, licences et conformité

- [x] `THIRD_PARTY_NOTICES.md` à jour
- [x] textes de licences runtime applicables inclus
- [ ] SBOM généré et revu
- [x] revue de l’absence de réseau/télémétrie
- [x] revue de l’écriture HKCU et du nettoyage par le désinstallateur
- [ ] revue juridique Spotify/VB-Audio
- [ ] plan .NET 8 avant le 10 novembre 2026

## Signature et preuves

- [ ] exécutable signé — binaire courant non signé
- [ ] installateur signé — binaire courant non signé
- [ ] chaîne de signature vérifiée
- [ ] horodatage vérifié
- [ ] archive finale créée après signature
- [ ] taille de chaque artefact consignée
- [x] SHA-256 de chaque artefact consigné dans le manifeste final
- [x] trois rapports TRX attachés au package
- [ ] rapport de QA manuelle attaché

## Tableau de livraison

Les hashes et tailles ne sont pas dupliqués dans ce document : `artifacts\checksums\SHA256SUMS.txt` et le rapport de release sont les sources de vérité de la construction courante.

| Artefact | Taille | SHA-256 | Signature | QA machine propre |
|---|---:|---|---|---|
| `SpotifyBooster-Portable-x64.exe` | Voir artefact final | Voir manifeste final | Non signé | Non validée |
| `SpotifyBooster-Setup-x64.exe` | Voir artefact final | Voir manifeste final | Non signé | Non validée |

## Décision

- [ ] **GO** : tous les gates bloquants sont fermés
- [x] **NO-GO** : signal audio réel, machine propre, DPI, SBOM et signature restent ouverts

Décision, date et responsable : **à compléter après validation**.
