# StarXelem — Design System Reference

Charte de la refonte, validée en novembre 2026 à partir des maquettes de la direction « Inventaire expert ». Elle est la seule référence pour tout élément visuel de l'application.

- **Implémentation** : `StarXelem/Style/SxTheme.axaml` (ressources `Sx.*`, classes `sx-*`) et `StarXelem/Components/` (`NotchedBorder`, `ColumnHeader`, `LocationIcon`).
- **Valeurs** : `.claude/skills/html-to-avalonia/tokens.json`, à garder identique à `SxTheme.axaml`.
- **Maquettes** : fichiers `project/<Ecran>.dc.html` de l'artefact « StarXelem, nouvelles directions ». Les lire avant de modifier un écran.
- **Thème** : sombre uniquement. Chaque écran force le thème sombre avec `ThemeVariantScope RequestedThemeVariant="Dark"` tant que le thème clair n'est pas dessiné.

L'ancienne charte (accent violet, coins arrondis, bordures de 0,5 px, variantes claires) n'est plus utilisée par aucun écran. Elle reste dans l'historique git de ce fichier ; voir la section 12 pour les éléments qui ne sont pas encore migrés.

---

## 1. Règles générales

- Un écran applique les ressources `Sx.*` et les classes `sx-*`, **sans couleur ni taille en dur**. Une valeur commune à plusieurs écrans va dans `SxTheme.axaml`.
- Ne pas modifier une couleur, une taille, une coupe ou une règle de cette charte sans validation de l'utilisateur.
- Pas d'ombre ni de dégradé, sauf l'ombre portée du panneau flottant.
- Pas de coin arrondi, sauf les puces (rayon 17) et les barres de progression (rayon 2).
- Le mot accompagne toujours la couleur : un statut n'est jamais signalé par la couleur seule.
- Cible tactile et hauteur de contrôle : 44 px (34 px pour les contrôles compacts).

---

## 2. Couleurs

| Rôle | Ressource | Valeur |
|---|---|---|
| Fond (fenêtre, entre les blocs) | `Sx.BackgroundBrush` | `#FF0B0D12` |
| Surface (blocs, panneaux, menu) | `Sx.SurfaceBrush` | `#FF12151C` |
| Relief (survol, cartes dans un bloc, bouton secondaire) | `Sx.ReliefBrush` | `#FF1A1E27` |
| Ligne (bordures, séparateurs, piste de barre) | `Sx.LineBrush` | `#FF2A2F3A` |
| Texte | `Sx.TextBrush` | `#FFF2F4F8` |
| Secondaire (labels, en-têtes, icônes au repos) | `Sx.SecondaryBrush` | `#FFA4ACBA` |
| Discret (indications, placeholders, désactivé) | `Sx.MutedBrush` | `#FF8A93A5` |
| Accent (action, sélection) | `Sx.AccentBrush` | `#FFE8B04A` |
| Accent survol | `Sx.AccentHoverBrush` | `#D9E8B04A` |
| Accent doux (fond sélectionné) | `Sx.AccentSoftBrush` | `#24E8B04A` |
| Accent marqué (filtre actif, bascule survolée) | `Sx.AccentStrongBrush` | `#55E8B04A` |
| Sur accent (texte d'un bouton plein) | `Sx.OnAccentBrush` | `#FF0B0D12` |

**Tons** de badge et de pastille : une ressource pleine pour le texte et la pastille, une ressource `SoftBrush` à alpha `24` pour le fond.

| Ton | Ressource | Valeur | Usages |
|---|---|---|---|
| Succès | `Sx.SuccessBrush` | `#FF4FD1A5` | connecté, rangé, obtenu, Univers persistant, grade A |
| Attention | `Sx.WarningBrush` | `#FFF08A4B` | en cours, Arena Commander, grade C |
| Danger | `Sx.DangerBrush` | `#FFF2666A` | détruit, erreur, grades D et E |
| Info | `Sx.InfoBrush` | `#FF6CB6FF` | région, menu principal, grade B |
| Neutre | `Sx.NeutralBrush` | `#FFA4ACBA` | non demandé, hors ligne, non chargé, défaut |

---

## 3. Typographie

Polices : `Sx.FontSans` pour tout le texte, posée une fois sur la racine de l'écran (`TextElement.FontFamily`) ; `Sx.FontMono` (ou la classe `sx-mono`) pour les nombres, identifiants techniques et compteurs. La cible des maquettes est IBM Plex Sans / Mono ; Inter et Cascadia Mono les remplacent tant que ces polices ne sont pas embarquées.

| Taille | Poids | Usage |
|---|---|---|
| 30 | 600 | Titre d'un détail (vaisseau, blueprint, mission) et d'une page d'actions |
| 22 | 500, mono | Valeur en grand dans une carte (montant, pourcentage) |
| 20 | 600 | Titre d'écran dans un bloc (`TextBlock.sx-title`) |
| 16 | 600 | Titre de section dans un détail |
| 14 | 400–500 | Nom mis en avant (vaisseau, ami) |
| 13 | 400 | Texte courant, cellules, boutons |
| 12 | 400–600 | Légende (`TextBlock.sx-caption`), en-tête de colonne, badge, sous-titre |
| 11 | 400, mono | Compteur (`TextBlock.sx-count`) ; aussi le contractant au-dessus d'un titre de mission |

Poids utilisés : 400, 500 (`Medium`), 600 (`SemiBold`). Pas de gras 700.

---

## 4. Formes

**Angles coupés** en haut à droite et en bas à gauche, avec `comp:NotchedBorder` (`CutSize`, `Background`, `BorderBrush`, `StrokeThickness`) :

| Coupe | Usage |
|---|---|
| 7 | badge, pastille d'icône 24 px |
| 9 | ligne de liste, bouton du menu, petit champ, bouton compact |
| 11 | champ, bouton 44 px, tuile d'icône 56 px |
| 12 | panneau flottant (filtre de colonne) |
| 14 | carte dans un bloc (récompense, modificateur, composant) |
| 18 | bloc principal, menu latéral, carte de page d'actions |

- **Puces** (`Button.sx-chip`) : seul élément arrondi avec la barre de progression, rayon 17, hauteur 34.
- **Bordures** : 1 px, couleur Ligne. Les blocs et les cartes n'ont pas de contour.
- **Icônes** : tracés en contour sur une grille de 24 px (`viewBox 0 0 24 24`), trait 1,7 à 2, extrémités et jonctions arrondies, sans remplissage.

---

## 5. Mise en page

- La fenêtre a le fond `Sx.BackgroundBrush`. Chaque écran pose une marge de 12 px et sépare ses blocs de 12 px.
- Un bloc est un `NotchedBorder` Surface coupe 18.
- Les écrans suivent trois modèles :

| Modèle | Écrans | Structure |
|---|---|---|
| Tableau filtrable | Objets, Mon hangar, Amis, Armes FPS, Réputations | Un bloc : barre d'outils (titre 20, compteur, recherche, boutons ; padding 20 × 14, séparateur), rangées de puces avec un libellé de 72 px, puis le tableau ou la grille de cartes. Panneau de détail à droite (300 à 340 px) pour Objets et Armes FPS. |
| Liste et détail | Blueprints, Loadout vaisseaux, Missions | Une colonne de liste (280 à 340 px : titre, compteur, recherche, puces, liste à lignes coupées), puis le détail (padding 28 × 26, titre 30, sections espacées de 22 px). Missions ajoute une colonne de catégories de 210 px. |
| Page d'actions | Extractions, Paramètres | Pas de bloc principal : titre 30 et sous-titre directement sur le fond, sections en casse de phrase, cartes Surface coupe 18 sans contour, contenu limité à 1 120 px. Pastille d'action 44 px coupe 11 en or, boutons `sx primary`, progression `sx-quality` dans la carte. |

- Message d'attente (« Sélectionnez… dans la liste ») : `sx-caption` 13 px dans la zone vide.
- Grilles de cartes : `UniformGrid` dont le nombre de colonnes suit la largeur (`WidthToColumnsConverter`, largeur minimale en paramètre).

### Menu latéral

- Barre de 64 px, `NotchedBorder` Surface coupe 18, marge 12, padding vertical 14.
- Logo StarXelem 34 px en haut (`Assets/xelem-logo.png`), puis une icône par écran : bouton de 44 px, coupe 9, icône de 21 px, trait 1,7.
- Au repos : icône Secondaire. Survol : fond Relief, icône Texte. Écran actif : fond Accent doux, icône Accent.
- Le nom de l'écran apparaît au survol, dans une infobulle à droite.
- Chaque page fournit son tracé dans `PageViewModelBase.NavIconData`. Ordre : Objets, Mon hangar, Blueprints, Amis, Loadout vaisseaux, Armes FPS, Missions, Extractions, Réputations, Paramètres (roue crantée). Objets est l'écran d'ouverture.

---

## 6. Composants

| Composant | Classe ou contrôle | Règle |
|---|---|---|
| Bouton | `Button.sx`, `.primary`, `.small` | Hauteur 44 px (34 en `.small`), coupe 11. Un seul `.primary` par barre d'outils. |
| Bouton bascule | `Button.sx` + `.on` | Fond Accent doux quand le panneau qu'il ouvre est déplié (ex. « Plages » des Armes FPS). Le libellé compte les filtres actifs du panneau replié. |
| Champ | `NotchedBorder.sx-field` + `Path.sx-search-icon` + `TextBox.sx` | Fond Fond, contour Ligne, contour Accent au focus. |
| Filtre rapide | `Button.sx-chip`, `.on` | Compteur `sx-count` dans la puce. La puce « Tous » fait partie de la même collection que les autres. Au plus 8 puces par rangée, le reste passe par le filtre de colonne. |
| Badge de statut | `NotchedBorder.sx-badge` + `.success`, `.info`, `.warning`, `.danger` | Coupe 7, pastille de 7 px et libellé 12 px semi-gras dans le ton, fond doux du même ton. Neutre sans classe. |
| Tableau | `DataGrid.sx` | En-têtes de 44 px, lignes de 34 px minimum, séparateurs horizontaux, survol Relief, sélection Accent doux. |
| En-tête de colonne | `comp:ColumnHeader` | Tri et filtre, voir section 9. |
| Liste | `ListBox` avec modèle d'élément `NotchedBorder` coupe 9 | Survol Relief, sélection Accent doux et semi-gras. Ligne à deux niveaux possible : légende 11 px au-dessus, ou sous-titre 12 px Discret en dessous. |
| Tuile d'icône | `NotchedBorder` Neutre doux | 26 px coupe 7 dans une liste, 56 px coupe 11 en tête d'un détail. |
| Carte | `NotchedBorder` Relief coupe 14 | Padding 16 × 14. Carte de valeur : valeur 22 mono, libellé 13 Secondaire, précision 12 Discret (en Accent quand elle renvoie à une autre section). |
| Ligne cliquable | `Button` à modèle `NotchedBorder` coupe 9, fond Relief | Survol Accent doux et nom en Accent, infobulle qui dit où mène le clic (ex. blueprints possibles d'une mission). |
| Case à cocher | `CheckBox.sx-muted` | Coche en Accent, texte 12 Secondaire. |
| Barre de progression | `ProgressBar.sx-quality` | 4 px, rayon 2, piste Ligne, remplissage Accent. |
| Paire libellé / valeur | `TextBlock` 12 Secondaire au-dessus, valeur 13 (mono si technique) | Pied de détail (réputation, nom technique), séparé par une ligne. Texte copiable : `SelectableTextBlock` avec police et couleur en ligne. |
| Chargement | `ui:ProgressRing` sur un voile Surface à 0,75 d'opacité | Libellé `sx-caption` sous l'anneau (« Chargement… », « Filtrage en cours… »). |

---

## 7. Emplacement d'un objet

Une pastille d'icône (`comp:LocationIcon`, 24 px, coupe 7), le nom, puis la destination finale en texte Discret 12 px. L'infobulle et le panneau de détail donnent le chemin complet.

| Catégorie | Icône | Ton |
|---|---|---|
| Lieu | Épingle | Info |
| Hangar | Maison | Info |
| Vaisseau | Vaisseau | Accent |
| Arme | Arme | Succès |
| Armure, casque | Bouclier | Succès |
| Porté | Silhouette | Succès |
| Caisse | Cube | Attention |
| Autre | Point d'interrogation | Neutre |

---

## 8. Familles de blueprint

Icône et libellé de l'objet fabriqué : `Models/BlueprintFamily.cs` (`BlueprintFamilyInfo.GetIcon`, `GetTypeLabel`). Familles : Armures, Armes FPS, Armes vaisseau, Composants, Minage, Autres. Le type précis s'écrit « Casque · Armure lourde », « Refroidisseur · S1 · Grade C », « Arme vaisseau · Taille 3 ».

---

## 9. Tri et filtre par colonne

- Le tri et le filtrage sont faits par le ViewModel (`CanUserSortColumns="False"`), pour survivre au rechargement des lignes.
- Un clic sur le titre trie en ordre croissant, puis décroissant, puis revient à l'ordre d'origine. La flèche n'apparaît que sur la colonne triée.
- Filtre par valeurs (`ColumnFilterViewModel`) sur les colonnes à valeurs répétées seulement. **Rien de coché signifie tout afficher**, et le filtre s'applique dès le clic.
- Les valeurs sont triées par ordre alphabétique et en cascade : chacune indique son nombre de lignes selon les autres filtres, et une valeur à zéro disparaît sauf si elle est cochée.
- Filtre actif : entonnoir en Accent et nombre de valeurs cochées. Une puce et le filtre de sa colonne partagent le même état.

---

## 10. Filtrage différé

Tout écran filtrable applique le même fonctionnement, demandé par l'utilisateur après un gel à la première lettre :

- Délai de 300 ms après la dernière frappe, 120 ms après un clic sur une puce ou un tri.
- Une nouvelle demande annule la précédente (`CancellationTokenSource`).
- Les critères sont copiés sur le thread d'UI, le calcul tourne hors du thread d'UI (`Task.Run`), puis le résultat est affiché.
- Pendant le calcul, l'indicateur « Filtrage en cours… » recouvre la liste. Il n'est retiré que par la dernière demande, celle qui n'a pas été annulée.

---

## 11. Pièges Avalonia et FluentAvalonia

- **`ListBoxItem` FluentAvalonia** : largeur minimale de 88 px. Pour un élément plus étroit (menu latéral), remettre `MinWidth` et `MinHeight` à 0.
- **Modèle de `ListBoxItem`** : ne pas nommer le `ContentPresenter` `PART_ContentPresenter`, sinon les styles de sélection Fluent s'y appliquent.
- **`Run.Text`** est lié dans les deux sens par défaut : avec un `StringFormat`, il boucle jusqu'au débordement de pile. Mettre `Mode=OneWay`, ou calculer le texte dans le ViewModel.
- **`SelectableTextBlock`** n'hérite pas des styles `TextBlock.xxx` : poser police, taille et couleur en ligne.
- **Couleurs Fluent d'un contrôle** (case cochée, curseur) : redéfinir ses ressources avec les valeurs `Sx` dans `Style.Resources`, comme `CheckBox.sx-muted`.
- **Capture d'écran** : les prévisualisations Avalonia de l'IDE verrouillent `bin/Debug` ; compiler avec `-o` vers un dossier temporaire.

---

## 12. Éléments hors charte

Ces éléments utilisent encore l'ancienne charte (`Style/BlueprintStyles.axaml`, `Style/DefaultResources.axaml`) et n'ont pas de maquette :

- **Barre du haut** de `MainWindow` : sélection de la version du jeu, ouverture d'un `Data.p4k`, état du fichier, indicateur de connexion, bouton de thème. Aucune maquette ne la prévoit.
- **Fenêtres surgissantes** (`Views/Popup/`) : chargement, message, synchronisation de flotte et d'objets, comparaison d'objets, envoi Orbital Alliance.
- **`ContainerTabView`**, onglet « Conteneurs » absent du menu.

Tant qu'ils ne sont pas migrés, ne pas supprimer les styles et ressources dont ils dépendent.

---

*Dernière mise à jour : 18 novembre 2026*
