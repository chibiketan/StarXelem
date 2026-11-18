# Audit des maquettes et du design system — 18 novembre 2026

Comparaison des maquettes de l'artefact « StarXelem, nouvelles directions » avec l'application compilée depuis la branche `feature/refonte_graphique` (commit `3cb373c`), puis mise à jour du design system du projet.

Méthode : capture de chaque écran avec ses données (`--screen <onglet>`, plus `--load-items`, `--load-ships`, `--load-friends`, `--load-reputations`), puis lecture de la maquette correspondante.

## 1. Écran par écran

| Écran | Maquette | Verdict | Écarts |
|---|---|---|---|
| Objets | `Inventaire.dc.html` | Conforme | Libellés de type et de sous-type non traduits dans les données (« UNDEFINED », « Char_Flair »), sans lien avec la charte. |
| Mon hangar | `Hangar.dc.html` | Conforme, enrichi | Filtres de colonne sur Statut et Emplacement, pastille d'emplacement : cohérent avec Objets. |
| Blueprints | `Blueprints.dc.html` | Conforme, enrichi | « Obtenu par mission » regroupe les missions par pool, avec le nom du pool et un compteur « ×N ». |
| Amis | `Amis.dc.html` | Conforme, enrichi | Avatar du joueur devant le pseudo. |
| Loadout vaisseaux | `Vaisseaux.dc.html` | Conforme, enrichi | Compteur dans les puces de constructeur, case « Inclure les vaisseaux masqués », tags triés en badges à angles coupés (pilules arrondies dans la maquette), tableau Missiles, points d'emport repliables. |
| Armes FPS | `ArmesFPS.dc.html` | Conforme, enrichi | Bouton bascule « Plages » et son panneau de filtres numériques. |
| Missions | `Missions.dc.html` | Conforme | Maquette mise à jour le 18 novembre (section « Blueprints possibles », réputation requise). L'application ajoute la colonne de catégories avec compteurs et la catégorie « Toutes les missions ». |
| Extractions | `Extractions.dc.html` | Conforme | Aligné le 18 novembre (commit `3e7961e`). |
| Réputations | `Reputations.dc.html` | Conforme | Grille de cartes, puces d'état, barres de palier en or, cartes « Non chargé » atténuées. |
| Paramètres | `Parametres.dc.html` | Conforme | Aligné le 18 novembre (commit `3e7961e`). |

Les planches `Main.dc.html` (direction A, « recherche d'abord ») et `Lieux.dc.html` (direction C, « cockpit par lieu ») sont les directions écartées : elles n'ont pas à correspondre à l'application.

## 2. Écarts transversaux

1. **Barre du haut.** Aucune maquette n'en a. L'application garde l'ancienne barre (sélection de la version du jeu, ouverture d'un `Data.p4k`, état du fichier, indicateur de connexion, bouton de thème), dans l'ancienne charte. Il faut décider où ces fonctions vont : bas du menu latéral, page Paramètres, ou une barre redessinée.
2. **Fenêtres surgissantes.** Les sept vues de `Views/Popup/` (chargement, message, synchronisation de flotte et d'objets, comparaison d'objets, envoi Orbital Alliance) sont dans l'ancienne charte et n'ont pas de maquette.
3. **Icône Paramètres.** L'application utilise une roue crantée, à la demande de l'utilisateur. Les dix maquettes gardent l'ancienne icône (cercle et rayons).
4. **Planche `Fondations.dc.html`.** Elle date de l'exploration : polices Inter et Space Grotesk, cartes à coins arrondis de 10 et 14 px, échelle « 34 / 20 / 14 / 12 », coupes 7 / 9 / 11 / 18. La charte retenue utilise IBM Plex (Inter en remplacement), aucun arrondi hors puces, l'échelle 30 / 22 / 20 / 16 / 13 / 12 / 11 et les coupes 7 / 9 / 11 / 12 / 14 / 18.
5. **Bloc du menu.** `Inventaire.dc.html` coupe le menu à 16 px, les autres planches et l'application à 18 px.
6. **Polices.** Les maquettes affichent IBM Plex Sans / Mono, l'application Inter / Cascadia Mono : les polices Plex ne sont pas embarquées.

## 3. Mises à jour faites dans le projet

| Fichier | Avant | Après |
|---|---|---|
| `src/CLAUDE_design_convention.md` | Sections 1 à 17 sur l'ancienne charte (violet, arrondis, 0,5 px, clair et sombre), section 18 partielle | Référence unique de la nouvelle charte : couleurs et tons, typographie réelle (30 à 11), coupes 7 à 18, trois modèles d'écran, menu latéral, composants, familles de blueprint, tri et filtre, filtrage différé, pièges Avalonia, éléments hors charte |
| `src/.claude/skills/html-to-avalonia/tokens.json` | Tokens violets clair / sombre, rayons 4 à 16, Segoe UI, tiers T1 à T3 | Tokens de la charte, identiques à `SxTheme.axaml` : couleurs, tons, polices, tailles, coupes, dimensions |
| `src/.claude/skills/html-to-avalonia/references/css-to-xaml-mapping.md` | `var(--…)`, `CornerRadius`, ressources `BackgroundPrimary` | Couleurs de maquette vers `Sx.*`, `clip-path` vers `NotchedBorder`, classes `sx-*`, pièges |
| `src/.claude/skills/html-to-avalonia/SKILL.md` | Génération d'un `Tokens.axaml` clair et sombre | Renvoi à `SxTheme.axaml`, nommage `Sx.*`, vérifications adaptées |
| `src/.claude/skills/starxelem-design/SKILL.md` | Sections et signaux d'alerte de l'ancienne charte | Sections de la nouvelle convention, lecture de la maquette, signaux d'alerte de la charte |
| `src/StarXelem/Style/SxTheme.axaml` | Commentaire renvoyant à la « section 18 » | Renvoi à la convention et à `tokens.json` |
| `src/StarXelem/Style/NavigationViewStyles.axaml` | Styles de la `NavigationView` | Supprimé : plus utilisé depuis le nouveau menu latéral (retiré de `App.axaml`) |

Maquettes mises à jour le même jour : roue crantée dans les dix planches d'écran, coupe du menu à 18 px sur `Inventaire.dc.html`, planche `Fondations.dc.html` réécrite selon la charte retenue (IBM Plex, angles coupés, échelle 30 à 11, six coupes, tons et leurs usages, Réputations parmi les tableaux filtrables). Les écarts 3, 4 et 5 de la section 2 sont donc résolus.

## 4. Reste à faire

- **Barre du haut et fenêtres surgissantes** : à maquetter puis migrer. Ensuite, `Style/BlueprintStyles.axaml` et `Style/DefaultResources.axaml` pourront être retirés.
- **Ressources de case à cocher et de curseur recopiées** dans `ItemsTabView`, `ShipTabView` et `FpsWeaponsTabView` : elles redéfinissent les mêmes couleurs que `CheckBox.sx-muted`. À remplacer par la classe, et à compléter d'une classe pour le curseur.
- **Polices IBM Plex** : à embarquer si l'on veut coller aux maquettes.
- **Données** : libellés de type non traduits dans Objets, noms « <= PLACEHOLDER => » dans Objets et Loadout vaisseaux.
