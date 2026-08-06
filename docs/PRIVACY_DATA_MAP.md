# Carte des données et confidentialité

## Principe

La Phase A minimise les données et n’ajoute aucune télémétrie applicative.
Aucun trafic réseau n’est nécessaire au moteur audio. Le backend local utilise
uniquement des données de test générées.

## Données

| Donnée | Source | Stockage Phase A | Destinataire | Sensibilité |
| --- | --- | --- | --- | --- |
| e-mail | Supabase Auth | Supabase Auth | Auth, contrat session | personnelle |
| mot de passe | utilisateur | jamais dans l’application | Supabase Auth | secret |
| access/refresh tokens | Supabase Auth | mémoire seulement | Auth/API/Data API | secret |
| UUID utilisateur | Supabase Auth | `profiles`, `entitlements` | API/Data API | pseudonyme |
| nom d’affichage | utilisateur | `profiles` | propriétaire | personnelle |
| plan/statut/features | serveur | `entitlements` | propriétaire/API | commerciale |
| réglages audio | utilisateur | JSON local | desktop | locale |
| noms de périphériques/PID | Windows | diagnostic local | utilisateur | technique |
| audio | source virtuelle | tampon mémoire borné | sortie audio | sensible, non persisté |
| logs | application | fichier local | utilisateur/support | technique expurgée |

## Flux

- Auth reçoit e-mail et mot de passe directement sur HTTPS.
- L’API reçoit un bearer et valide son JWT.
- La Data API reçoit, par requête, la clé publiable et le bearer utilisateur.
- PostgreSQL retourne uniquement les lignes autorisées par RLS.
- Le callback audio ne persiste ni n’envoie aucun échantillon.

## Exclusions Phase A

La base ne contient aucune donnée Stripe, carte, facture, device, fingerprint
matérielle, événement de sécurité utilisateur ou télémétrie. Les tables
`billing_subscriptions`, `stripe_webhook_events`, `devices` et
`security_events` n’existent pas.

## Logs et diagnostics

Ne jamais journaliser :

- mot de passe ;
- access/refresh token ou header `Authorization` ;
- clé privée, `service_role` ou secret Stripe ;
- corps webhook futur ;
- e-mail complet ;
- chaîne de connexion.

Le diagnostic desktop peut contenir noms visibles des périphériques, PID,
formats et erreurs expurgées. L’utilisateur doit le relire avant partage.

## Rétention et droits

La rétention, suppression de compte, export, base légale et sous-traitants
doivent être définis avant production. `DeleteAccountRequest` est un contrat
seulement; aucune suppression administrative n’est implémentée.

La suppression d’un utilisateur Auth local cascade vers son profil et son
entitlement. Cela ne constitue pas encore une procédure RGPD complète.
