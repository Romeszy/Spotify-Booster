# Migration de configuration V3

## Changements

Le schéma courant est `SchemaVersion = 3`.

- plafond Expert configurable supprimé ;
- plafond RawExpert fixe `+75 dB` ;
- pas RawExpert `0,5 dB` ;
- nouveau champ `Expert75DbAcknowledged` ;
- ancien `ExpertModeAcknowledged` interdit dans un document V3.

Les modes Protégé et Direct restent limités à `+14 dB` et conservent leur
précision historique.

## V1 vers V3

V1 exprime le gain en pourcentage. La migration conserve la conversion :

```text
gainDb = 20 × log10(percent / 100)
```

V1 ne possède pas RawExpert. Ses réglages et profils sont migrés en conservant
les valeurs non demi-dB valides. Un backup byte-for-byte
`settings.schema1.backup.json` est créé avant remplacement.

## V2 vers V3

Chaque réglage principal et chaque profil est traité selon son propre mode :

- Protégé/Direct : valeur conservée sous réserve de la borne standard ;
- RawExpert : clamp `0..75`, puis arrondi au demi-dB avec
  `MidpointRounding.AwayFromZero`.

Exemples :

| Entrée V2 RawExpert | V3 |
| ---: | ---: |
| `74,24` | `74,0` |
| `74,25` | `74,5` |
| `74,6` | `74,5` |
| `74,75` | `75,0` |
| `1000` | `75,0` |

`ExpertModeAcknowledged`, vrai ou faux, est ignoré.
`Expert75DbAcknowledged` devient toujours `false` afin d’afficher le nouvel
avertissement. Le backup s’appelle `settings.schema2.backup.json`.

La preuve automatisée couvre séparément les deux valeurs de l’ancien champ,
le rejet d’un document déjà V3 qui contient encore
`ExpertModeAcknowledged`, puis un second chargement du V3 migré. Ce second
chargement ne réécrit ni `settings.json` ni le backup : la migration est
idempotente une fois le schéma V3 produit.

## Atomicité

La migration :

1. lit strictement le schéma source ;
2. crée ou vérifie le backup exact ;
3. convertit les réglages et tous les profils ;
4. écrit un fichier temporaire ;
5. recharge et valide le V3 ;
6. remplace atomiquement `settings.json`.

Une source invalide, un backup différent ou une panne d’écriture laisse la
source et le dernier fichier valide intacts.

## Validation V3

Une valeur RawExpert doit être finie, entre `0` et `75`, et exactement sur la
grille `0,5 dB`. Une valeur off-grid est rejetée, pas normalisée
silencieusement. Protégé et Direct acceptent leurs valeurs non demi-dB valides.

## Rollback

Ne remplacez pas un fichier V3 par son backup dans une version qui ne comprend
pas le schéma correspondant. Pour revenir :

1. fermer Spotify Booster ;
2. sauvegarder le V3 et les logs ;
3. installer la version compatible avec V1 ou V2 ;
4. restaurer le backup correspondant sous contrôle manuel ;
5. valider à `0 dB` avant de réactiver l’audio.

Il n’existe aucune migration automatique V3 vers V2/V1.
