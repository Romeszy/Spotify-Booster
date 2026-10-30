# Refonte de l’interface — agent 2

Référence : `design/reference_appli.png`. Réalisation coordonnée avec l’agent 1, propriétaire du service de mise à jour.

## Livraison

- Navigation latérale permanente, tableau de bord en cartes et thème noir/vert.
- Cadran de gain WPF en relief, gradué sur 270°, connecté au gain réel. Animation de 240 ms sans minuterie permanente. Le curseur natif conserve souris, clavier et quantification.
- Égaliseur trois bandes accessible depuis le tableau de bord, Bass Boost, source/sortie et protections conservés.
- Transitions de boutons, gradients et ombres réutilisables. Animations conditionnées par la préférence Windows.
- Disposition en une colonne sous 1180 DIPs ; minimum 880 × 620 conservé ; taille initiale plafonnée à l’espace de travail Windows.
- Bloc de mises à jour raccordé au `UpdatesViewModel` de l’agent 1.

Fichiers : `MainWindow.xaml/.cs`, `ViewModels/MainViewModel.cs`, `Controls/GainDial.cs`, dictionnaires `Themes/{Colors,Cards,Buttons,Sliders,Typography}.xaml`, `DESIGN.md`, `scripts/test-ui-contract.ps1`, `tests/fixtures/main-window-bindings.json`.

## Vérifications

- Compilation App Release finale : 0 erreur, 0 avertissement, analyseurs .NET activés.
- Suite solution : 427 tests réussis, résultat communiqué par l’agent 1 après compilation fraîche (Core 151, DSP 90, Audio 27, Auth 54, API 105).
- `scripts/test-ui-contract.ps1` : 122 bindings historiques conservés, limites et quantification du gain, taille minimale et couverture de la confirmation Expert vérifiées.
- Tests dans la véritable fenêtre Windows, avec copie portable isolée et moteur audio arrêté : navigation Contrôle/Réglages/Diagnostic/Assistant ; réglage à +3,5 dB avec affichage 150 % ; retour à 0 dB ; confirmation Expert puis annulation ; vue compacte d’environ 900 px ; vérification manuelle des mises à jour.
- La vérification GitHub affiche une erreur explicite quand aucune release stable publique n’est disponible. Aucun installateur téléchargé ou exécuté.
- Revue indépendante : taille initiale hors écran détectée puis corrigée par plafonnement avec `SystemParameters.WorkArea`.
- `git diff --check` : réussi.

Captures locales : `artifacts/ui-refresh/dashboard-final.jpg`, `compact.png`, `expert-confirmation.png`, `settings.png`, `diagnostics.jpg`. Le tableau de bord final illustre +3,5 dB, moteur arrêté. Les captures ne représentent pas un test de traitement sonore.

Reproduction : compiler avec `.\.dotnet\dotnet.exe build src/SpotifyBooster.App/SpotifyBooster.App.csproj -c Release`, puis ouvrir la sortie Release. Pour isoler les préférences, copier les fichiers de sortie dans un dossier de validation, y créer `portable.mode`, puis ouvrir l’exécutable. Le premier démarrage affiche l’assistant ; cliquer sur Vue d’ensemble. Capture de référence : 1380 × 940 DIPs, Windows à 100 %.

## Skills & Tools Report

- Ponytail : utilisé pour réutiliser WPF, les commandes existantes et les ressources, sans nouvelle dépendance.
- Design : documentation lue et contrat existant `DESIGN.md` actualisé.
- Visual Ralph : évalué pour la comparaison à une image ; boucle visuelle native adaptée à Codex App, sans lancement du runtime OMX/tmux ni revendication de correspondance pixel à pixel.
- Computer Use : utilisé pour les interactions et captures Windows via `@oai/sky`.
- Sous-agents natifs : thème/transitions, cadran, puis revue indépendante ; coordination explicite avec le chat Agent 1.
- PowerShell, .NET SDK local, analyseurs, Node et XML : compilation, contrat UI et vérifications locales.
- Figma, Sites, Web Perf, Imagegen et Hyperframes non retenus : application WPF existante et référence déjà fournie, sans site, vidéo ou nouvel asset raster à produire.
- Graphify, UI-UX-Pro-Max et Impeccable ne figurent pas dans l’inventaire de capacités disponible ; aucun outil supposé disponible n’a été invoqué. Les workflows backend, Doctor, Autoresearch, Team/tmux et Ultrawork n’apportaient pas de travail complémentaire à cette refonte bornée.

## Limites

Le relief est dessiné en WPF 2D avec gradients et ombres, sans scène 3D interactive. La référence a été adaptée aux fonctions existantes : pas de faux morceau, lecteur musical, spectre ou égaliseur dix bandes. Aucun test d’écoute prolongée, benchmark GPU, lecteur d’écran ni contrôle sur écran physique à 125/150 % n’a été réalisé. Les tests automatisés et visuels ne prouvent pas l’absence absolue de régressions matérielles. Aucun commit, push ou paquet publié par l’agent 2.
