# Spotify Booster — direction produit et interface

## Source of truth

Ce document est la source de vérité de l’itération Desktop V2 pour les décisions d’interface, de contenu et d’interaction. Il complète les contraintes fonctionnelles et de sécurité décrites dans :

- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/AUDIO_PIPELINE.md`
- `docs/INSTALLATION.md`
- `docs/PROJECT_STATE.md`
- `docs/SECURITY.md`
- `docs/TROUBLESHOOTING.md`
- `src/SpotifyBooster.App/MainWindow.xaml`
- `src/SpotifyBooster.App/ViewModels/MainViewModel.cs`

Les choix ci-dessous reposent sur le cahier des charges V2 fourni le 28 juillet 2026 et sur l’audit de la V1 au commit `ab2923cf44756be7b8446d67e6cbe15123cee3d2`.

Hypothèses explicites :

- l’application cible en priorité Windows 11, tout en restant compatible Windows 10 ;
- l’usage principal se fait à la souris, avec clavier disponible pour les réglages précis ;
- le thème sombre est le thème produit de référence ; un thème clair peut être ajouté sans modifier la hiérarchie ;
- l’utilisateur peut ne rien connaître au routage audio Windows ;
- la fenêtre doit rester efficace à 100 %, 125 % et 150 % de mise à l’échelle ;
- aucune identité, icône ou couleur propriétaire de Spotify n’est réutilisée.

## Brand

Spotify Booster est un utilitaire audio Windows spécialisé, précis et rassurant. Son identité associe :

- un fond presque noir, des surfaces graphite et une lumière verte contrôlée ;
- une forme d’onde ascendante originale comme symbole d’amplification ;
- une voix directe, humaine et factuelle ;
- la transparence sur ce qui est traité, limité ou demandé manuellement.

Le nom « Spotify Booster » décrit la destination du produit, mais l’interface ne doit jamais laisser entendre une affiliation avec Spotify.

## Product goals

1. Donner un contrôle immédiat et mesurable du gain en dB.
2. Rendre visibles les protections réellement actives et leur effet instantané.
3. Permettre à un débutant de configurer VB-CABLE sans fausse automatisation.
4. Garder le traitement accessible depuis la zone de notification.
5. Mettre les détails techniques dans Diagnostic, sans encombrer l’écran principal.
6. Prévenir clairement les risques sans bloquer arbitrairement le mode Expert.

## Personas and jobs

### Utilisateur quotidien

Veut amplifier Spotify plus que le volume Windows, retrouver rapidement son profil et masquer la fenêtre pendant l’écoute.

### Utilisateur prudent au casque

Veut comprendre les limites, conserver un plafond numérique et disposer d’un retour immédiat à 0 dB.

### Utilisateur expert

Veut connaître le facteur linéaire réel, désactiver les traitements optionnels et observer chaque étage du pipeline.

### Utilisateur en dépannage

Veut savoir où le signal disparaît : routage, périphérique, format, gain, module dynamique ou sortie.

## Information architecture

La navigation principale contient quatre destinations stables :

1. **Contrôle** — source, sortie, moteur, gain, Bass Boost et niveaux.
2. **Assistant** — parcours VB-CABLE en sept étapes, relançable.
3. **Réglages** — Audio, Application, Interface et Avancé.
4. **Diagnostic** — pipeline détaillé, métriques, périphériques, erreurs et export.

Le menu de zone de notification expose uniquement les actions fréquentes : ouvrir, activer, ajuster le gain, retour à 0 dB, Bass Boost, mode, diagnostic et quitter.

## Design principles

- **Le signal d’abord** : source, sortie et niveaux sont toujours identifiables.
- **Aucune action cachée** : le gain demandé ne change pas automatiquement.
- **Précision sans surcharge** : dB principal, pourcentage linéaire secondaire.
- **Protection explicite** : les modes Protégé, Direct et Expert brut décrivent le pipeline réel.
- **Progressive disclosure** : l’écran principal reste compact ; les réglages fins vivent ailleurs.
- **Windows natif maîtrisé** : comportements attendus de fenêtre et clavier, avec tous les contrôles visuellement normalisés.
- **Danger proportionné** : avertissements visibles et sobres, jamais présentés comme une garantie matérielle.

## Visual language

### Couleurs

- fond principal : noir bleuté profond ;
- rail/navigation : graphite plus dense ;
- surfaces de travail : graphite neutre, sans multiplication de cartes ;
- texte principal : blanc cassé ;
- texte secondaire : gris froid avec contraste AA ;
- accent : vert minéral, réservé aux valeurs actives et à l’action principale ;
- information : bleu froid ;
- avertissement : ambre ;
- danger : corail rouge ;
- focus clavier : anneau clair distinct de l’accent d’état.

Les couleurs sont centralisées dans `Themes/Colors.xaml`. Aucun code couleur métier ne reste dans les vues.

### Typographie

- famille : `Segoe UI Variable`, repli `Segoe UI` ;
- valeurs de gain : chiffres tabulaires, taille dominante ;
- titres : semi-gras, courts ;
- métriques : `Cascadia Mono`, repli `Consolas`, uniquement dans Diagnostic ;
- texte utile : minimum visuel équivalent à 12 px à 100 %.

### Forme et profondeur

- rayons modérés de 6 à 12 px ;
- peu de bordures, utilisées pour la séparation fonctionnelle ;
- ombres légères uniquement pour les popups et la fenêtre ;
- aucun glow, gradient décoratif ou néon ;
- espacements fondés sur une grille de 4 px.

### Mouvement

- transitions de 100 à 180 ms pour hover, sélection et apparition ;
- aucune animation continue décorative ;
- respect du réglage de réduction des animations ;
- les vu-mètres restent fonctionnels et peuvent réduire leur fréquence visuelle.

## Components

### Gain control

Affiche une grande valeur `+x,x dB`, puis l’équivalent linéaire `y %`. Le curseur est gradué en dB, les flèches règlent finement et le bouton panique `0 dB` reste toujours visible.

### Mode selector

Sélecteur segmenté à trois états :

- Protégé ;
- Direct ;
- Expert brut.

Chaque état affiche une phrase courte décrivant les modules réellement actifs. La première activation du mode Expert ouvre une confirmation explicite.

### Signal route

Deux sélecteurs alignés « Source virtuelle » et « Sortie physique », reliés par un flux visuel discret. Un état invalide explique la cause près du contrôle concerné.

### Level meters

Deux barres horizontales avec valeur dBFS. Le rouge indique un dépassement, l’ambre une intervention dynamique ; aucune fausse courbe scientifique.

### Bass band

Une bande horizontale simple représente la zone 35–200 Hz et la fréquence sélectionnée. Elle sert de repère, pas de mesure spectrale.

### Buttons

- primaire : une seule action forte par écran ;
- secondaire : actions locales ;
- danger : quitter ou réinitialiser après confirmation ;
- icône : nom accessible et tooltip obligatoire.

### Inputs and popups

ComboBox, TextBox, CheckBox, ToggleButton, Slider, ListBox, TabControl, ToolTip, ContextMenu, MenuItem, ScrollBar et ProgressBar ont des templates complets couvrant normal, hover, pressed, focused, selected et disabled.

### Dialogs

Les confirmations sont courtes, thémées et contiennent une action principale ainsi qu’une annulation évidente. Aucun `MessageBox` natif blanc ne doit rester dans le parcours normal.

## Accessibility

- contraste cible WCAG AA pour tout texte essentiel ;
- focus clavier visible sur chaque contrôle interactif ;
- ordre de tabulation cohérent avec la lecture ;
- noms d’automatisation pour boutons icônes, vu-mètres et sélecteurs ;
- état communiqué par texte en plus de la couleur ;
- tailles de cible minimales proches de 36 px ;
- aucun avertissement uniquement rouge/vert ;
- compatibilité prévue avec les animations réduites ;
- les valeurs dB sont lisibles et modifiables au clavier.

## Responsive behavior

- taille de référence : environ 1040 × 720 ;
- taille minimale visée : 880 × 620 sans contenu essentiel coupé ;
- à 125 % et 150 %, le contenu central défile verticalement sans défilement horizontal ;
- les panneaux secondaires passent sous le contrôle principal lorsque l’espace manque ;
- la navigation reste fixe tant que la largeur le permet, puis devient compacte ;
- les popups sont bornés à la fenêtre et gardent une hauteur raisonnable.

## Interaction states

Les vues couvrent explicitement :

- Spotify fermé ou détecté ;
- source virtuelle absente, incorrecte ou sans signal ;
- sortie absente, déconnectée ou invalide ;
- moteur arrêté, démarrage, actif, arrêt, récupération et erreur ;
- changement de périphérique ;
- clipping et échantillons assainis ;
- limiteur ou compresseur en réduction ;
- mode Expert non confirmé ou actif ;
- contrôles désactivés ;
- sauvegarde, migration et export réussis ou échoués ;
- fenêtre visible, minimisée dans la barre des tâches ou masquée dans le tray.

## Content voice

- français simple, phrases courtes et verbes d’action ;
- termes audio expliqués au premier usage ;
- « 0 dB » remplace « 100 % » pour le réglage neutre principal ;
- aucun message ne promet une sécurité auditive ou une isolation que le routage ne prouve pas ;
- les erreurs indiquent une cause probable et une action suivante ;
- le mode Expert emploie un avertissement factuel, sans dramatisation répétitive.

## Implementation constraints

- WPF et MVVM sous .NET 8 ;
- aucune dépendance UI externe sans justification ;
- ressources fusionnées par domaine dans `Themes/` ;
- aucune allocation, écriture disque ou mise à jour UI dans la boucle DSP ;
- les métriques audio sont publiées vers l’UI à fréquence bornée ;
- l’icône est originale et empaquetée en ICO multi-résolutions ;
- le tray, l’autostart et les raccourcis sont encapsulés derrière des abstractions testables ;
- le câble VB-Audio reste un prérequis tiers séparé ;
- la version portable écrit dans `Data/` uniquement lorsque le mode portable est explicitement détecté.

## Open questions

- La validation matérielle du flux VB-CABLE dépend des périphériques réellement présents sur la machine de test.
- Le thème clair est secondaire pour cette itération ; s’il n’est pas livré, le thème sombre doit rester indépendant du thème Windows.
- Les raccourcis globaux par défaut doivent rester désactivés tant qu’aucune combinaison sans conflit n’est validée.
- La signature Authenticode reste impossible sans certificat fourni.
- Le compromis final de buffer/latence devra être mesuré sur une route réelle plutôt que déduit uniquement des tests hors ligne.
