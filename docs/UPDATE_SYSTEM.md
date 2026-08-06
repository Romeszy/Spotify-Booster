# Système de mise à jour

## Statut

La Phase A fournit uniquement `IUpdateService` et `UpdateContract` avec version,
page de téléchargement et indicateur `Mandatory`. Aucun updater, téléchargement
automatique, vérification de manifeste ou mise à jour obligatoire n’est
implémenté.

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

Cette procédure ne peut pas être exécutée aujourd’hui : l’updater et sa chaîne
de signature sont différés.
