# Système de mise à jour

## Statut

Le canal stable GitHub est désormais implémenté via `GitHubUpdateService` et
`UpdatesViewModel`. Dans Paramètres, la vérification est déclenchée par l’utilisateur.
Elle consulte la dernière release stable du dépôt `Romeszy/Spotify-Booster`.
Les brouillons, préversions et versions inférieures ou identiques sont exclus.

La release doit contenir `SpotifyBooster-Setup-x64.exe`, avec une taille valide
et un digest SHA-256 fourni par l’API GitHub. Un asset sans digest est refusé.
Le téléchargement est borné et son hash et sa taille sont vérifiés avant lancement.
GitHub et le compte qui publie les releases constituent la racine de confiance :
ce hash ne remplace pas une signature indépendante de l’éditeur.

Après clic sur Installer, l’installateur utilisateur s’ouvre puis Booster se
ferme par son chemin normal (sauvegarde/libération audio). Les nouveaux installateurs
acceptent `/UPDATEPID` et attendent jusqu’à 30 secondes la fermeture du processus
avant de remplacer les fichiers. L’assistant NSIS reste visible ; il permet de
relancer l’application à la fin. Aucun droit administrateur n’est demandé.
Une ancienne release NSIS sans ce protocole exige de vérifier que Booster est
fermé avant de poursuivre son assistant.

Le portable peut vérifier la version, mais son remplacement reste manuel via
l’archive GitHub en conservant `Data`. Installer un EXE ne convertirait pas
automatiquement ses réglages portables vers le dossier utilisateur.

Publication : augmenter la version de l’application et du packaging, produire
l’installateur, créer une release stable avec un tag `vX.Y.Z`, puis joindre
`SpotifyBooster-Setup-x64.exe`. Vérifier que GitHub expose `digest: sha256:…`
pour cet asset. Une préversion seule ne sera pas proposée par ce canal.
Le premier passage vers une version équipée de l’updater se fait manuellement.

## Validation avant diffusion

1. Installer manuellement une version équipée de ce canal sur un compte Windows de test.
2. Publier une version supérieure stable avec l’asset attendu, sans modifier une release existante.
3. Dans Réglages, vérifier les mises à jour : la version supérieure doit être proposée.
4. Cliquer Installer : vérifier le téléchargement, la fermeture du moteur, puis l’assistant NSIS.
5. Relancer et vérifier version, réglages conservés et fonctionnement audio.
6. Vérifier aussi le mode hors ligne et la copie portable (installation désactivée).

Ces étapes réelles ne sont pas validées par les tests HTTP synthétiques.
Les téléchargements vérifiés restent dans `%LOCALAPPDATA%\SpotifyBooster\Updates` ;
ils peuvent être supprimés après installation, lorsque NSIS est fermé.

Les binaires actuels sont non signés. Ils ne constituent pas une release
commerciale.

## Contrat Phase D

Une ADR devra choisir entre MSIX/App Installer et une solution maintenue
compatible TUF. Le système retenu devra vérifier avant toute exécution :

- métadonnées signées, expirantes et versionnées ;
- hash et longueur ;
- RID et canal ;
- version minimale ;
- politique de rollback et de downgrade ;
- staging dans un emplacement sûr.

Le moteur audio devra être arrêté et ses ressources libérées avant le handoff.
Un simple HTTP `200` ne devra jamais autoriser l’exécution d’un fichier.

Une mise à jour obligatoire pourra bloquer l’usage normal, mais devra laisser
accessibles le support, les informations de sécurité et la désinstallation.

## Rollback futur

Le mécanisme devra conserver une version précédente vérifiée, refuser toute
downgrade non autorisée par des métadonnées signées et restaurer la dernière
version saine en cas d’échec avant validation post-installation.

Le rollback automatique et la chaîne de métadonnées signées restent différés.
Le canal GitHub manuel ci-dessus n’implémente pas le contrat commercial Phase D.
