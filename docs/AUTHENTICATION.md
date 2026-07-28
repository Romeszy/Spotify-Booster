# Authentification

## Portée Phase A

`SpotifyBooster.Auth` implémente un adaptateur HTTP Supabase Auth derrière
`IAuthenticationService`. Il couvre inscription, connexion, refresh,
déconnexion locale, renvoi de vérification, demande de reset et changement de
mot de passe.

Il n’est pas branché à WPF. La session est conservée en mémoire uniquement :
aucun refresh token n’est écrit dans `settings.json`, les logs ou un stockage
DPAPI.

## Configuration publique

L’adaptateur accepte :

- une origine HTTPS propre ;
- une clé `sb_publishable_...` ;
- un timeout fini ;
- une taille maximale de réponse.

La clé publiable est envoyée uniquement dans `apikey`. `Authorization` contient
uniquement un bearer utilisateur lorsque l’opération l’exige. Les redirections
HTTP sont refusées et chaque appel utilise un nouvel `HttpRequestMessage`.

Les secrets `service_role`, `sb_secret`, Stripe et clés privées sont interdits
dans le client.

## Contrat de session

`AuthSessionContract` contient l’utilisateur, l’expiration de l’access token et
deux jetons opaques. Les jetons sont exclus de la sérialisation JSON et de
`ToString()`.

- mot de passe : minimum local de 10 caractères ;
- serveur local : `minimum_password_length = 10` ;
- refresh : single-flight et remplacement atomique en mémoire ;
- reset : réponse générique pour limiter l’énumération de comptes ;
- logout : session locale effacée même si la révocation distante échoue.

Le résultat de logout distingue `Succeeded`, `Failed` et `NotAttempted`. Un JWT
d’accès déjà émis peut rester valide jusqu’à son expiration; le client local
cesse néanmoins de l’utiliser immédiatement.

## Validation JWT de l’API

`SpotifyBooster.Api` accepte exclusivement :

- `RS256` avec clé RSA publique ;
- `ES256` avec courbe P-256 ;
- issuer et audience configurés ;
- `exp`, `nbf` et `sub` UUID valides ;
- tolérance d’horloge de 60 secondes.

Le document JWKS est limité à 256 Kio et le fetch à 5 secondes. Le cache est
frais sur l’intervalle `[loadedAt, loadedAt + 10 min)`. À expiration, en cas de
purge ou d’échec de refresh, la validation échoue fermée.

Le coordinateur applique :

- un single-flight par processus ;
- un cooldown global de 60 secondes après échec ;
- un cooldown global de 60 secondes pour les `kid` inconnus ;
- une invalidation par génération/epoch ;
- le rejet des complétions tardives.

La rotation doit publier ancienne et nouvelle clés pendant au moins 20 minutes,
prouver les deux signatures, puis retirer l’ancienne et purger/refetch.

## API publique Phase A

| Route | Résultat |
| --- | --- |
| `GET /health/live` | live, sans validation JWT |
| `GET /health/ready` | ready seulement si JWKS et Data API sont frais |
| `GET /v1/me` | profil et entitlement du subject validé |

La réponse `/v1/me` contient uniquement `userId`, `displayName`, `plan`,
`status`, `revision`, `features` et `serverTime`. Elle ne contient ni token,
e-mail, donnée Stripe ou configuration. Une absence d’identité retourne `401`;
une dépendance Data API invalide ou indisponible retourne `503`. Les erreurs
utilisent un Problem Details expurgé avec `code` et `traceId`.

`/v1/me` limite à 30 requêtes par minute et subject authentifié. Une requête
sans subject valide est limitée à 10 requêtes par minute et adresse distante.
Un dépassement retourne `429` avec `Retry-After` et Problem Details expurgé.

## Différé

La Phase C doit encore fournir les vues WPF, le callback Windows, le stockage
DPAPI `CurrentUser`, les ACL, la révocation locale complète et les tests de
cycle utilisateur. Aucun de ces éléments n’est opérationnel en Phase A.
