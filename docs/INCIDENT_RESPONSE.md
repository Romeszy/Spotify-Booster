# Réponse aux incidents

## Portée

Ce runbook couvre la fondation Phase A. Il n’existe pas encore d’astreinte,
d’environnement de production, de Stripe ou d’updater.

## Priorités

1. protéger les utilisateurs : arrêter le moteur en cas de risque audio ;
2. empêcher une nouvelle autorisation incorrecte ;
3. préserver des preuves expurgées ;
4. restaurer le dernier état vérifié ;
5. documenter cause et actions sans recopier de secret.

## Incident JWKS

Exemples : clé compromise, retrait inattendu, hausse de `unknown_kid`, panne
JWKS.

Objectifs de runbook : accusé sous 15 minutes, purge/refetch ou rollback
commencé sous 30 minutes, statut toutes les 30 minutes.

1. rendre readiness unready et conserver le fail-closed ;
2. ne pas activer de fallback HTTP ou de secret partagé ;
3. republier la dernière clé sûre si nécessaire ;
4. publier ancienne et nouvelle clés pendant le chevauchement ;
5. déclencher la purge interne, jamais un endpoint admin public ;
6. vérifier acceptation/rejet des signatures attendues ;
7. rechercher uniquement catégories, compteurs et horaires expurgés.

Un échec de purge ne restaure jamais un ancien snapshot.

## Secret local exposé

1. arrêter Supabase local ;
2. supprimer les artefacts sous `backend\supabase\.local\` de façon locale ;
3. retirer le certificat du magasin utilisateur s’il n’est plus requis ;
4. vérifier historique Git, logs, TRX, pièces jointes et terminal ;
5. si un secret a été commité, considérer l’historique compromis et faire
   coordonner purge et rotation avant tout push ;
6. relancer le bootstrap pour générer de nouveaux artefacts.

Ne jamais joindre les fichiers `.local` au rapport.

## Incident RLS ou entitlement

1. couper `/v1/me` ou l’API si une lecture croisée est possible ;
2. conserver la base pour investigation en lecture seule ;
3. identifier migration, rôle, policy et grants concernés ;
4. restaurer la dernière sauvegarde compatible si l’intégrité est perdue ;
5. appliquer une nouvelle migration corrective; ne pas éditer une migration
   déjà appliquée ;
6. rejouer reset, lint, pgTAP et tests deux utilisateurs avant réouverture.

Un cache client ne doit jamais restaurer Pro.

## Incident audio

1. utiliser la panique et arrêter le moteur ;
2. remettre le gain physique à un niveau bas ;
3. revenir au mode Protégé et `0 dB` ;
4. déconnecter la route virtuelle si une boucle est suspectée ;
5. exporter le diagnostic sans audio ;
6. ne pas relancer Expert brut avant identification de la cause.

## Rollback desktop/configuration

- revenir au dernier binaire vérifié ;
- conserver `settings.schema1.backup.json` ou
  `settings.schema2.backup.json` ;
- ne pas faire lire directement un fichier V3 par une ancienne version ;
- restaurer manuellement l’ancien backup seulement avec une version qui
  comprend son schéma.

## Clôture

Consigner impact, fenêtre, données concernées, décisions, tests de récupération
et actions préventives. La communication externe, les obligations de
notification et le contact DPO/juridique restent à définir avant production.
