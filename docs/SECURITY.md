# Sécurité

## Garanties Phase A

- aucun secret privilégié requis dans le desktop ;
- aucun téléchargement ou installation silencieuse de pilote ;
- aucune élévation de privilège pour l’application ;
- aucun trafic réseau dans le callback audio ;
- JWT limités à `RS256`/`ES256` et validation fail-closed ;
- RLS forcée sur profils et entitlements ;
- bearer et clé publiable portés par requête, jamais par headers globaux ;
- réponses réseau bornées, timeouts finis et redirections refusées ;
- réglages locaux validés et écrits atomiquement ;
- logs et Problem Details expurgés ;
- Supabase CLI locale exacte `2.109.1`, jamais substituée par une CLI globale.

Ces contrôles sont implémentés par voie. Le gate d’intégration final et la revue
indépendante restent nécessaires avant toute release.

## Secrets et configuration

Publics et distribuables :

- `SUPABASE_URL` ;
- `SUPABASE_PUBLISHABLE_KEY` ;
- `PUBLIC_APP_URL` ;
- clés publiques de vérification.

Serveur uniquement :

- `SUPABASE_SERVICE_ROLE_KEY` ;
- `STRIPE_SECRET_KEY` ;
- `STRIPE_WEBHOOK_SECRET` ;
- Price ID configuré ;
- clés privées TLS/JWT/signature.

Les artefacts locaux vivent sous `backend\supabase\.local\`. Ils sont ignorés,
restreints au compte courant et `SYSTEM` sous Windows, et ne doivent jamais
être commis, loggés, copiés dans un rapport ou distribués.

## TLS local

L’origine normative est `https://127.0.0.1:54321`. Le certificat doit couvrir
l’IP `127.0.0.1`. Le script ne tente l’ajout au magasin
`CurrentUser\Root` qu’après l’action explicite `-TrustCertificate`.

Dans l’environnement du gate, le dialogue de confirmation Windows est resté
bloqué. Aucun certificat n’a été ajouté au magasin Windows. La validation a
donc été exécutée sous WSL avec
`SSL_CERT_FILE="$PWD/supabase/.local/tls/supabase-local.crt"` et, pour le JWKS,
`curl --cacert "$SSL_CERT_FILE"`. Cette voie conserve la validation complète de
la chaîne et du nom.

Il n’existe aucun fallback conforme vers HTTP, certificat accept-all,
`DangerousAcceptAnyServerCertificateValidator` ou désactivation du contrôle de
nom/chaîne.

Pour retirer la confiance locale, ouvrez `certmgr.msc`, trouvez le certificat
de développement Spotify Booster dans les autorités racines du compte courant,
vérifiez son empreinte contre le fichier local, puis supprimez uniquement ce
certificat. Arrêtez d’abord la stack.

## Sécurité audio

Le mode Protégé active le soft clipper et le limiteur lors de sa sélection. Le
mode Direct conserve le plafond standard `+14 dB` mais peut désactiver les
étages dynamiques. Expert brut accepte `0..+75 dB` par pas de `0,5 dB`.

`+75 dB` vaut environ `×5623,413`. Ce niveau n’est pas sûr pour l’audition et
peut produire saturation, distorsion ou dommages. Aucun algorithme numérique ne
garantit une pression acoustique sûre. Le limiteur est numérique, optionnel
hors mode Protégé et placé avant le gain de sortie.

Le bouton panique ramène les gains DSP à `0 dB` avec une rampe courte. Il ne
modifie pas le volume physique Windows.

## Base et RLS

`anon` ne possède aucun droit sur les tables. Il peut uniquement utiliser le
schéma public et exécuter la RPC constante `spotify_booster_health_v1()`.
`authenticated` lit ses propres lignes, ne modifie que son `display_name` et ne
peut jamais écrire son entitlement.

La RPC health est volontairement publique, sans donnée et sans accès table.
Elle doit être protégée contre l’abus de capacité au gateway avant production.

## Dépendances

Avant livraison :

```powershell
& ".\.dotnet\dotnet.exe" restore .\SpotifyBooster.sln --locked-mode
& ".\.dotnet\dotnet.exe" list .\SpotifyBooster.sln package --vulnerable --include-transitive
& ".\.dotnet\dotnet.exe" list .\SpotifyBooster.sln package --deprecated --include-transitive
Push-Location .\backend
npm audit --package-lock-only --audit-level=high
Pop-Location
```

Le gate actuel ne détecte aucune vulnérabilité NuGet ou npm. L’audit NuGet
`--deprecated` signale néanmoins des dépendances transitives `5.0.0` héritées
de `xunit.v3` `3.2.2` dans les projets de tests :
`Microsoft.NETCore.Platforms`, `System.Security.AccessControl` et, pour les
tests DSP/Auth/API, `System.Diagnostics.DiagnosticSource`. Il s’agit d’un
avertissement amont à surveiller, pas d’une correction locale prouvée ; ne pas
forcer une substitution transitive sans validation de la chaîne xUnit.

Ne jamais utiliser `npm audit fix` ou une mise à jour aveugle dans le gate.
Versions et licences sont inventoriées dans
[THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

## Différé avant production

- Stripe et writer privilégié ;
- écrans Auth WPF et stockage DPAPI ;
- licence hors ligne et devices ;
- updater signé et mandatory update ;
- migration .NET 10 ;
- Authenticode, SBOM et provenance commerciale ;
- hébergement, proxy, sauvegardes et restauration ;
- revue RGPD/juridique et réponse aux incidents opérationnelle.

Signaler une vulnérabilité de façon privée au mainteneur du dépôt. Ne pas
publier de token, clé privée, donnée utilisateur ou procédure d’exploitation.
