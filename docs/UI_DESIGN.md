# Conception de l’interface V2

Ce document décrit l’interface WPF implémentée. `DESIGN.md` conserve la décision produit détaillée ; ce fichier sert de guide de maintenance pour le code actuel.

## Principes

- le gain principal est exprimé en dB ;
- l’état audio, le mode et la route restent visibles ;
- les actions dangereuses sont explicites ;
- aucune modification silencieuse des périphériques Windows ;
- les contrôles fréquents restent accessibles depuis la fenêtre, le tray et les raccourcis ;
- la densité est compacte sans sacrifier les libellés ou le focus clavier.

## Fenêtre

| Propriété | Valeur |
|---|---|
| Taille initiale | `1040 × 720` |
| Taille minimale | `880 × 620` |
| Style | fenêtre sans bordure avec barre de titre personnalisée |
| Pages principales | Contrôle, Réglages, Diagnostic |
| Page contextuelle | Assistant de routage |
| Barre basse | état de route, panique et activation du moteur |

La navigation haute reste stable. Les notifications globales apparaissent sous la navigation avec quatre niveaux visuels : information, succès, avertissement et erreur.

## Page Contrôle

Le banc audio rassemble :

- sélection source virtuelle et sortie physique ;
- rafraîchissement des endpoints ;
- mode Protégé, Direct ou Expert brut ;
- valeur de gain en dB et équivalent linéaire ;
- slider borné par le mode ;
- avertissement contextuel ;
- vu-mètres entrée/sortie ;
- état du signal, clipping et limiteur ;
- Bass Boost, profil, fréquence, gain et pente.

La liste de profils Bass Boost est alimentée par les profils validés et persistés dans les réglages, puis complétée par Personnalisé.

Le pourcentage est secondaire et dérivé. Il ne doit jamais redevenir l’autorité de sauvegarde ou de validation.

## Page Réglages

Sections :

- **Audio** : DC blocker, Bass Boost, égaliseur trois bandes, paramètres complets du compresseur, soft clipper et limiteur, gain de sortie, lissage, plafond Expert et avertissements casque ;
- **Application** : démarrage Windows, démarrage masqué, traitement automatique, notifications et action du bouton Fermer ;
- **Interface** : activation et édition des six raccourcis globaux, affichage du pourcentage linéaire et diagnostics détaillés ;
- **Avancé** : relance de l’assistant et ouverture des pages Son/Mélangeur Windows.

Les changements audio sont publiés immédiatement. Les préférences sont sauvegardées localement avec temporisation.

Les champs de schéma `Theme`, `Density` et `ReduceAnimations` existent pour l’évolution, mais la V2 visible utilise le thème sombre et la densité compacte définis par les ressources XAML ; elle n’expose pas encore de sélecteur complet pour ces trois valeurs.

## Page Diagnostic

La page présente :

- état du moteur et formats ;
- processus Spotify détectés ;
- modules DSP et gains demandé/appliqué ;
- niveaux aux étapes principales ;
- charge, buffers, underruns, overruns, clips et durée ;
- erreurs récentes ;
- export texte explicite.

Le mode **diagnostics détaillés** est sauvegardé, mais l’interface V2 affiche déjà les blocs principaux ; toute différence future doit être documentée et testée.

## Assistant

L’assistant en sept étapes explique avant d’agir :

1. topologie Spotify → câble → Booster → sortie ;
2. installation externe de VB-CABLE ;
3. présence de CABLE Input/CABLE Output ;
4. routage manuel de Spotify ;
5. sélection de la route ;
6. test à `0 dB` et panique ;
7. dépannage et fin.

L’assistant utilise une progression visible, Précédent/Suivant, et avertit lorsque la sélection de route est incomplète. Terminer l’assistant restaure le mode Protégé à `0 dB`.

## Expert brut

Le premier choix d’Expert brut ouvre un overlay modal :

- titre et risque explicites ;
- focus clavier cyclé dans le dialogue ;
- Annuler ne change pas le mode ;
- Confirmer persiste l’acquittement ;
- plafond fixe `+75 dB`, avec pas `0,5 dB` ;
- explication que `+75 dB` vaut environ `×5623,413`, peut saturer immédiatement, n’est pas sûr pour l’audition et peut dépasser le plafond du limiteur ;
- le mode désactive les protections dynamiques sans correction cachée.

Ne contournez pas cet acquittement par une transition implicite. L’avertissement contextuel reste visible en Expert brut. La stabilité numérique ne constitue jamais une garantie acoustique.

## Tray

L’icône active reflète un moteur en marche ; la variante inactive reflète l’arrêt. Le menu offre :

- ouvrir/restaurer ;
- démarrer/arrêter ;
- presets de gain compatibles avec le mode ;
- ±1 dB et panique ;
- Bass Boost et profils ;
- mode de protection ;
- sortie physique ;
- Diagnostic ;
- Quitter.

Le double-clic restaure la fenêtre. Le bouton Fermer masque dans le tray par défaut ; **Quitter** termine réellement le processus.

## Raccourcis globaux

Les raccourcis utilisent `MOD_NOREPEAT`. Ils sont désactivés par défaut et suivent la configuration sauvegardée. Une erreur d’enregistrement produit une notification et une entrée diagnostic sans empêcher l’application de démarrer.

## Tokens visuels

Palette principale :

| Token | Valeur | Usage |
|---|---|---|
| Canvas | `#0A0D10` | fond principal |
| Rail | `#0D1115` | navigation/barres |
| Surface | `#11161B` | cartes |
| SurfaceRaised | `#182027` | contrôles et panneaux |
| TextPrimary | `#F3F6F8` | texte principal |
| TextSecondary | `#BAC4CD` | texte secondaire |
| TextMuted | `#96A3AE` | légendes |
| Accent | `#55E38C` | action/état positif |
| Info | `#84D7FF` | focus et information |
| Warning | `#FFCC66` | risque |
| Danger | `#FF6B7A` | erreur/panique |

Typographie :

- UI : Segoe UI Variable, repli Segoe UI ;
- valeurs : Bahnschrift SemiCondensed ;
- métriques : Cascadia Mono, repli Consolas ;
- tailles de tokens : 11, 12, 13, 14, 20 et 48 px.

Les nombres utilisent autant que possible des chiffres tabulaires.

## Ressources et composants

Les dictionnaires sous `Themes` séparent :

- couleurs ;
- typographie ;
- boutons ;
- cartes/panneaux ;
- champs et sélecteurs ;
- menus ;
- sliders ;
- scrollbars et vu-mètres ;
- chrome de fenêtre.

Préférez un token existant à une couleur ou taille codée localement. Toute nouvelle interaction doit conserver un état focus visible, un libellé d’automatisation et un contraste cohérent avec le token sémantique.

## Accessibilité

L’implémentation fournit :

- `AutomationProperties.Name` sur les contrôles critiques ;
- focus visuel cyan de 2 px ;
- navigation clavier des boutons, sliders, combos et overlay ;
- notifications avec live region polie ;
- texte en plus de la couleur pour les états ;
- zones scrollables pour les petites fenêtres.

À vérifier manuellement avant release :

- ordre de tabulation complet ;
- Narrateur Windows ;
- 125 % et 150 % de mise à l’échelle, non validés à ce stade ;
- contraste de chaque état désactivé/hover/focus ;
- clavier seul pour l’assistant et Expert brut ;
- comportement à la taille minimale.

## Icônes

Les fichiers sous `Assets` sont des ressources locales originales du projet :

- `SpotifyBooster.ico` ;
- `SpotifyBooster-inactive.ico` ;
- déclinaisons PNG de 16 à 1024 px ;
- source chromatique locale.

Ils ne proviennent pas de Spotify, VB-Audio ou d’une bibliothèque d’icônes tierce. Leur utilisation et leur redistribution restent soumises à `LICENSE.txt`.
