# Dépannage

## Aucun périphérique virtuel n’apparaît

- Spotify Booster n’installe aucun pilote.
- Installez séparément un câble virtuel compatible, puis redémarrez l’application si nécessaire.
- Cliquez sur **Actualiser**.
- Vérifiez dans Windows que les deux côtés du câble sont actifs.

L’application doit quand même démarrer sans câble ; la source reste vide.

Si un câble installé n’est pas reconnu comme virtuel, l’activation reste bloquée pour éviter une route microphone → enceintes. Relevez son nom exact dans le diagnostic ; cette version n’offre pas de contournement non sécurisé.

## Spotify est détecté mais aucun signal n’arrive

La détection du processus et le routage audio sont indépendants.

1. Ouvrez le mélangeur de volume Windows.
2. Vérifiez la sortie choisie pour Spotify.
3. Vérifiez que la source de capture sélectionnée dans Booster est l’autre côté du même câble.
4. Démarrez réellement une piste.
5. Désactivez tout mode exclusif sur le câble.
6. Vérifiez les vu-mètres et le diagnostic.

## Le signal contient d’autres applications

Ces applications utilisent le même endpoint virtuel. Replacez-les sur leurs sorties normales dans le mélangeur Windows. Le MVP ne peut pas distinguer plusieurs applications déjà mélangées dans le câble.

## Pas de son en sortie

- choisissez un endpoint de rendu actif ;
- une sortie reconnue comme virtuelle est refusée pour empêcher une boucle ;
- vérifiez que la sortie n’est pas utilisée en mode exclusif ;
- testez d’abord une sortie filaire ;
- consultez l’état et le format dans **Diagnostic**.

## Son haché, underruns ou overruns

- augmentez la stabilité du pilote/câble et fermez les applications audio lourdes ;
- alignez si possible le câble et la sortie sur 48 kHz ;
- évitez Bluetooth pendant le diagnostic ;
- relevez les compteurs après 30 minutes ;
- exportez le rapport.

Le socle corrige la dérive à ±1 500 ppm, mais le réglage doit encore être validé sur plusieurs pilotes.

## Distorsion ou limiteur constamment actif

- revenez à 100 % ;
- réduisez le Bass Boost ;
- laissez le compresseur et le limiteur actifs ;
- augmentez progressivement ;
- observez la réduction du limiteur.

Le moteur baisse automatiquement le gain lors d’une limitation répétée. Cela indique que les réglages sont trop agressifs pour le contenu.

## Retour à 100 % ne semble pas instantané

Le bouton panique publie la remise à zéro au prochain bloc du thread de rendu, typiquement en quelques dizaines de millisecondes. Il ne modifie pas le volume physique Windows.

## Périphérique débranché

Le moteur passe à `DeviceLost`, s’arrête proprement et tente trois reconstructions espacées. Si la récupération échoue :

1. rebranchez le périphérique ;
2. actualisez la liste ;
3. resélectionnez source/sortie ;
4. activez à nouveau à 100 %.

## Bluetooth

Bluetooth ajoute souvent 100–300 ms ou plus, change parfois de profil et peut basculer vers un mode mains libres de qualité réduite. La latence affichée par l’application n’inclut pas tout le délai radio/codec.

## Spotify Microsoft Store non détecté

Le détecteur recherche tous les processus dont le nom contient `Spotify` et ne dépend pas d’un chemin. Si aucun processus n’est visible :

- vérifiez que Spotify est réellement lancé ;
- redémarrez Spotify ;
- consultez les erreurs d’accès dans le diagnostic.

La lecture audio reste possible même si l’indicateur de processus échoue.

## Le rapport de diagnostic contient-il des données sensibles ?

Il contient noms de périphériques, PIDs Spotify, formats, métriques et erreurs expurgées. Il n’inclut pas les IDs d’endpoints, chemins, arguments de processus ni contenu audio. Relisez tout rapport avant de le partager.
