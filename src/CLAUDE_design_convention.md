# StarXelem — Design System Reference

> Document de référence pour tous les composants graphiques de l'application StarXelem.  
> Généré à partir des maquettes et décisions prises au cours du projet.  
> **Format couleurs : ARGB 8 bits (ex: `#FF534AB7`)**  
> **Tailles : px | Poids : `normal`, `500`, `bold`**

---

## 1. Architecture générale

L'application est une fenêtre desktop Windows (.NET / Avalonia UI 11 + FluentAvalonia) composée de trois zones fixes :

```
┌─────────────────────────────────────────────────────┐
│  BARRE TITRE  (titlebar)                            │
├─────────────────────────────────────────────────────┤
│  BARRE OUTILS  (toolbar)  — présente sur certains   │
│  écrans seulement                                   │
├──────┬──────────────────────────────────────────────┤
│      │                                              │
│ NAV  │   CONTENU PRINCIPAL                          │
│ BAR  │   (liste + détail, tableau, formulaire…)     │
│      │                                              │
└──────┴──────────────────────────────────────────────┘
```

- **Sidebar de navigation** : toujours présente, icônes seules (52 px de large)
- **Barre de titre** : contient le sélecteur de version, les boutons d'action globaux, et l'indicateur de connexion gRPC
- **Contenu principal** : variable selon l'écran

---

## 2. Thèmes

L'application suit le thème Windows (clair ou sombre). **Toutes les couleurs sont définies pour les deux thèmes.**

### 2.1 Surfaces (fonds)

| Rôle | Dark | Light |
|---|---|---|
| Application (fond global) | `#FF0F1117` | `#FFF3F3F3` |
| Sidebar navigation | `#FF0B0D13` | `#FFEBEBEB` |
| Panneau liste | `#FF111318` | `#FFF8F8F8` |
| Panneau détail | `#FF0F1117` | `#FFFFFFFF` |
| En-tête de détail | `#FF0F1117` | `#FFFAFAFA` |
| Carte / bloc de contenu | `#08FFFFFF` | `#FFFAFAFA` |
| En-tête de tableau | `#FF111318` | `#FFF8F8F8` |
| Barre de titre | `#FF0F1117` | `#FFE8E8E8` |
| Barre d'outils | `#FF0F1117` | `#FFF0F0F0` |

### 2.2 Bordures

| Rôle | Dark | Light |
|---|---|---|
| Séparateur principal | `#14FFFFFF` | `#FFD8D8D8` |
| Bordure de carte | `#14FFFFFF` | `#FFE4E4E4` |
| Bordure de champ / input | `#1AFFFFFF` | `#FFD0D0D0` |
| Focus sur champ | `#80A78BFA` | `#FF7F77DD` |

**Règle générale :** bordures à `0.5px solid` sur tous les composants. Jamais de `1px` sauf cas exceptionnel justifié.

---

## 3. Couleur d'accentuation (violet)

Le violet est la couleur principale de StarXelem. Il s'applique aux éléments actifs, aux actions primaires, aux badges de sélection, aux icônes de navigation active.

| Rôle | Dark | Light |
|---|---|---|
| Accentuation primaire | `#FFA78BFA` | `#FF534AB7` |
| Accentuation atténuée | `#FF7F77DD` | `#FF7F77DD` |
| Fond teinté (actif) | `#338B5CF6` | `#FFEEEDFE` |
| Bordure teintée | `#598B5CF6` | `#FFAFA9EC` |
| Fond hover | `#408B5CF6` | `#FFCECBF6` |

---

## 4. Typographie

La taille de base est 12 px. Pas de taille inférieure à 10 px.

| Rôle | Taille | Poids | Dark | Light |
|---|---|---|---|---|
| Titre de page / item principal | 15–16 px | 500 | `#FFFFFFFF` | `#FF1A1A1A` |
| Titre de bloc (mod-name) | 13 px | 500 | `#D9FFFFFF` | `#FF2A2A2A` |
| Corps de texte / valeur | 12 px | normal | `#B3FFFFFF` | `#FF555555` |
| Label de colonne (th) | 10 px | 500 | `#4DFFFFFF` | `#FFBBBBBB` |
| Sous-titre de section (caps) | 10 px | 500 | `#40FFFFFF` | `#FFBBBBBB` |
| Texte secondaire / métadonnée | 11 px | normal | `#59FFFFFF` | `#FF999999` |
| Texte monospace (shard, code) | 11 px | normal | `#47FFFFFF` | `#FF888888` |
| Texte vide / absent | 11 px | normal | `#26FFFFFF` | `#FFCCCCCC` |
| Titre de liste (section label) | 11 px | 500 | `#66FFFFFF` | `#FF999999` |

**Règle :** toujours trois niveaux de hiérarchie visibles dans un écran — titre, label, valeur. Ne jamais mélanger plus de deux tailles de police dans un même composant.

---

## 5. Sidebar de navigation

- Largeur : **52 px**
- Icônes seules, pas de labels texte
- Taille des icônes : 15×15 px, `stroke-width: 1.5`, `fill: none`
- Icône active : fond teinté + couleur accentuation
- Séparateurs (`nav-sep`) : ligne de 24 px de large, hauteur 0.5 px

| État | Fond icône | Couleur stroke | Dark | Light |
|---|---|---|---|---|
| Inactif | transparent | Atténué | `#4DFFFFFF` | `#FF888888` |
| Actif | Teinté violet | Violet | `#338B5CF6` fond / `#FFA78BFA` icône | `#FFEEEDFE` fond / `#FF534AB7` icône |

---

## 6. Barre de titre (toolbar principal)

Contient de gauche à droite :
1. **Sélecteur de version** — dropdown avec nom de patch (ex: `HOTFIX - sc-alpha-4.7.0-hotfix`)
2. **Bouton dossier** — sélection manuelle du répertoire de données
3. **Bouton export** — action secondaire
4. **Spacer** — pousse les éléments suivants à droite
5. **Indicateur de connexion gRPC** — voir section 10

| Élément | Dark fond | Dark bordure | Light fond | Light bordure |
|---|---|---|---|---|
| Dropdown version | `#0FFFFFFF` | `#1AFFFFFF` | `#FFFFFFFF` | `#FFC8C8C8` |
| Bouton action | `#0FFFFFFF` | `#1AFFFFFF` | `#FFFFFFFF` | `#FFC8C8C8` |
| Séparateur vertical | `#1AFFFFFF` | — | `#FFD0D0D0` | — |

---

## 7. Système de badges

Tous les badges suivent le même principe : **fond teinté + bordure 0.5 px + texte de même teinte + point coloré optionnel**. C'est le langage visuel unifié de toute l'application.

### Formule générale

```
fond   : couleur_base à alpha 0.12–0.15
bordure: couleur_base à alpha 0.30
texte  : couleur_base pleine (alpha FF) en dark
         teinte foncée (stop 800) en light
```

### 7.1 Badges de statut Blueprint

| Badge | Dark fond | Dark bordure | Dark texte | Light fond | Light bordure | Light texte |
|---|---|---|---|---|---|---|
| Tier 1 | `#338B5CF6` | `#598B5CF6` | `#FFA78BFA` | `#FFEEEDFE` | `#FFAFA9EC` | `#FF3C3489` |
| ∞ Utilisations | `#261D9E75` | `#4D1D9E75` | `#FF5DCAA5` | `#FFE1F5EE` | `#FF5DCAA5` | `#FF0F6E56` |
| Timer | `#0FFFFFFF` | `#1AFFFFFF` | `#73FFFFFF` | `#FFF2F2F2` | `#FFDDDDD` | `#FF777777` |

### 7.2 Badges de statut Ami / Shard

| Statut | Dark fond | Dark bordure | Dark texte | Light fond | Light bordure | Light texte |
|---|---|---|---|---|---|---|
| Persistent Universe | `#268B5CF6` | `#598B5CF6` | `#FFA78BFA` | `#FFEEEDFE` | `#FFAFA9EC` | `#FF3C3489` |
| Menu principal | `#261D9E75` | `#4D1D9E75` | `#FF5DCAA5` | `#FFE1F5EE` | `#FF5DCAA5` | `#FF0F6E56` |
| Arena Commander | `#1EEF9F27` | `#4CEF9F27` | `#FFEF9F27` | `#FFFAEEDA` | `#FFEF9F27` | `#FF633806` |
| Hors ligne | `#0DFFFFFF` | `#1AFFFFFF` | `#40FFFFFF` | `#FFF2F2F2` | `#FFDDDDDD` | `#FFAAAAAA` |

### 7.3 Badges de statut Vaisseau (Mon Hangar)

| Statut | Dark fond | Dark bordure | Dark texte | Light fond | Light bordure | Light texte |
|---|---|---|---|---|---|---|
| Rangé | `#1F8B5CF6` | `#4D8B5CF6` | `#FFA78BFA` | `#FFEEEDFE` | `#FFAFA9EC` | `#FF3C3489` |
| Non demandé | `#1E1D9E75` | `#4D1D9E75` | `#FF5DCAA5` | `#FFE1F5EE` | `#FF5DCAA5` | `#FF0F6E56` |
| Détruit | `#1DE24B4A` | `#4DE24B4A` | `#FFF09595` | `#FFF7C1C1` | `#FFF09595` | `#FFA32D2D` |
| Dans la nature | `#1EEF9F27` | `#4CEF9F27` | `#FFEF9F27` | `#FFFAEEDA` | `#FFEF9F27` | `#FF854F0B` |
| Inconnu | `#0DFFFFFF` | `#1AFFFFFF` | `#40FFFFFF` | `#FFF2F2F2` | `#FFDDDDDD` | `#FFAAAAAA` |

### 7.4 Badges de région (Shard)

| Région | Dark fond | Dark bordure | Dark texte | Light fond | Light bordure | Light texte |
|---|---|---|---|---|---|---|
| Europe | `#1E378ADD` | `#4D378ADD` | `#FF85B7EB` | `#FFE6F1FB` | `#FF85B7EB` | `#FF0C447C` |
| USA | `#1E1D9E75` | `#4D1D9E75` | `#FF5DCAA5` | `#FFE1F5EE` | `#FF5DCAA5` | `#FF085041` |
| Asie | `#1EEF9F27` | `#4CEF9F27` | `#FFEF9F27` | `#FFFAEEDA` | `#FFEF9F27` | `#FF633806` |
| Australie | `#1ED85A30` | `#4DD85A30` | `#FFF0997B` | `#FFFAECE7` | `#FFF0997B` | `#FF712B13` |

### 7.5 Badges de type d'emplacement (Mon Hangar / Objets)

Les badges d'emplacement n'affichent que l'icône (pas de label) dans les tableaux. Le label est réservé à la légende en haut de page.

| Type | Dark fond | Dark bordure | Dark icône | Light fond | Light bordure | Light icône |
|---|---|---|---|---|---|---|
| Lieu (épingle) | `#1E378ADD` | `#4D378ADD` | `#FF85B7EB` | `#FFE6F1FB` | `#FF85B7EB` | `#FF0C447C` |
| Hangar (maison) | `#1F8B5CF6` | `#4D8B5CF6` | `#FFA78BFA` | `#FFEEEDFE` | `#FFAFA9EC` | `#FF534AB7` |
| Vaisseau (café) | `#1EEF9F27` | `#4CEF9F27` | `#FFEF9F27` | `#FFFAEEDA` | `#FFEF9F27` | `#FF633806` |
| Joueur (silhouette) | `#1E1D9E75` | `#4D1D9E75` | `#FF5DCAA5` | `#FFE1F5EE` | `#FF5DCAA5` | `#FF0F6E56` |
| Inconnu (?) | `#0DFFFFFF` | `#1AFFFFFF` | `#40FFFFFF` | `#FFF2F2F2` | `#FFDDDDDD` | `#FFAAAAAA` |

---

## 8. Bouton "Charger / Actualiser"

Bouton d'action primaire récurrent dans les écrans à liste. Toujours accompagné d'une icône de rechargement (rotation animée pendant le chargement).

| État | Dark fond | Dark bordure | Dark texte | Light fond | Light bordure | Light texte |
|---|---|---|---|---|---|---|
| Normal | `#268B5CF6` | `#598B5CF6` | `#FFA78BFA` | `#FFEEEDFE` | `#FFAFA9EC` | `#FF534AB7` |
| Hover | `#408B5CF6` | `#598B5CF6` | `#FFA78BFA` | `#FFCECBF6` | `#FFAFA9EC` | `#FF534AB7` |
| Disabled | `#0D8B5CF6` | `#1F8B5CF6` | `#59A78BFA` | `#FFF6F6FD` | `#FFD8D7F3` | `#FFB0ACDB` |

- Taille texte : 11 px, poids 500
- Icône : 11×11 px, même couleur que le texte
- Border-radius : 6–7 px
- Padding : 5 px vertical, 11 px horizontal

---

## 9. Champ de recherche

Présent dans les écrans à liste (Blueprints, etc.).

- Fond : légèrement plus clair que le panneau
- Une icône loupe à gauche (12×12 px, couleur atténuée)
- Un bouton croix à droite, **visible uniquement quand le champ est rempli**
- Focus : bordure colorée en violet

| État | Dark fond | Dark bordure | Light fond | Light bordure |
|---|---|---|---|---|
| Normal | `#0DFFFFFF` | `#1AFFFFFF` | `#FFFFFFFF` | `#FFD0D0D0` |
| Focus | `#0DFFFFFF` | `#80A78BFA` | `#FFFFFFFF` | `#FF7F77DD` |

---

## 10. Indicateur de connexion gRPC (barre de titre)

Toujours positionné à l'extrême droite de la barre de titre. Affiche l'état de la connexion au serveur du jeu.

| État | Description | Dark fond | Dark bordure | Dark texte | Light fond | Light bordure | Light texte |
|---|---|---|---|---|---|---|---|
| Jeu non détecté | Fichier de connexion absent | `#0AFFFFFF` | `#14FFFFFF` | `#4DFFFFFF` | `#0A000000` | `#17000000` | `#FFAAAAAA` |
| Connexion en cours | Point animé (pulse) | `#1EEF9F27` | `#4CEF9F27` | `#FFEF9F27` | `#19EF9F27` | `#4CEF9F27` | `#FF854F0B` |
| Connecté | Sans shard | `#1E1D9E75` | `#4D1D9E75` | `#FF5DCAA5` | `#19EF9F27` — voir note | `#4D1D9E75` | `#FF085041` |
| En jeu + shard | ID de shard affiché | `#268B5CF6` | `#598B5CF6` | `#FFA78BFA` | `#1F8B5CF6` | `#4D8B5CF6` | `#FF3C3489` |
| Erreur | Message tronqué + tooltip | `#1FE24B4A` | `#4DE24B4A` | `#FFF09595` | `#14E24B4A` | `#4DE24B4A` | `#FFA32D2D` |

**Structure de l'indicateur "En jeu + shard" :**
```
● En jeu  ·  pub_euw1b_11523279_080
```
- Point : 7 px, couleur pleine
- Label : 11 px, poids 500
- Séparateur `·` : 10 px, alpha 35%
- ID de shard : 10 px, monospace, alpha 65% (dark) / couleur foncée (light)

**Structure de l'indicateur "Erreur" :**
```
● Erreur  ·  ⓘ  StatusCode=Unauthenticated, token exp…
```
- Le message est tronqué à 160 px max avec `text-overflow: ellipsis`
- Au survol : tooltip avec le message complet en police monospace
- Fond du tooltip : toujours sombre (`#FF1C1C2E`) avec bordure rouge dans les deux thèmes

---

## 11. Tableaux de données

Utilisés dans : Amis, Mon Hangar, Objets.

### Règles générales

- `table-layout: fixed` — largeurs de colonnes fixes, jamais auto
- En-tête collant (`position: sticky`) avec fond légèrement différent
- Hauteur de ligne : 48–56 px selon la densité du contenu
- Séparateurs horizontaux uniquement (`0.5px`)
- Hover de ligne : fond très léger (`#08FFFFFF` dark / `#0A000000` light)
- Pas de bordures verticales entre colonnes

### Couleurs de tableau

| Élément | Dark | Light |
|---|---|---|
| Fond en-tête | `#FF111318` | `#FFF8F8F8` |
| Bordure en-tête | `#14FFFFFF` | `#FFE0E0E0` |
| Texte en-tête (th) | `#4DFFFFFF` | `#FFBBBBBB` |
| Fond ligne normal | transparent | transparent |
| Fond ligne hover | `#08FFFFFF` | `#0A000000` |
| Bordure ligne | `#0DFFFFFF` | `#FFE8E8E8` |
| Texte cellule | `#B3FFFFFF` | `#FF555555` |

### Colonnes indicatrices (point seul)

Pour les colonnes booléennes (Store, En ligne...), utiliser un point de 7–8 px centré plutôt qu'une case à cocher ou un texte.

| État | Dark | Light |
|---|---|---|
| Vrai / Actif | `#FF5DCAA5` (vert teal) | `#FF1D9E75` |
| Faux / Inactif | `#30FFFFFF` | `#FFD0D0D0` |

---

## 12. Cartes de modificateurs (Blueprint detail)

Chaque modificateur s'affiche dans une carte avec deux colonnes : **Ressources** (gauche) et **Propriétés** (droite), séparées par un diviseur vertical.

| Élément | Dark | Light |
|---|---|---|
| Fond de carte | `#08FFFFFF` | `#FFFAFAFA` |
| Bordure de carte | `#14FFFFFF` | `#FFE4E4E4` |
| En-tête de carte | même fond | même fond |
| Bordure bas en-tête | `#0FFFFFFF` | `#FFECECEC` |
| Titre de carte (mod-name) | `#D9FFFFFF` — 13 px / 500 | `#FF2A2A2A` — 12 px / 500 |
| Label section (RESSOURCES / PROPRIÉTÉS) | `#4DFFFFFF` — 10 px / 500 / caps | `#FFBBBBBB` — 10 px / 500 / caps |
| Point de ressource | `#FFA78BFA` | `#FF7F77DD` |
| Nom de ressource | `#99FFFFFF` — 12 px | `#FF666666` — 11 px |
| Quantité de ressource | `#D9FFFFFF` — 12 px / 500 | `#FF333333` — 11 px / 500 |
| Nom de propriété | `#80FFFFFF` — 12 px | `#FF999999` — 11 px |
| Valeur de propriété | `#D9FFFFFF` — 12 px | `#FF333333` — 11 px |
| Séparateur min–max | `#33FFFFFF` — 10 px | `#FFCCCCCC` — 10 px |
| Diviseur vertical | `#12FFFFFF` | `#FFE8E8E8` |

---

## 13. Icônes d'équipement (StreamGeometry Avalonia)

8 sous-types définis, utilisables via `{StaticResource Icon.Xxx}` dans un `Path`.

| Clé | Description |
|---|---|
| `Icon.Helmet` | Casque — calotte avec oreillettes et visière |
| `Icon.Body` | Corps — plastron avec épaulières |
| `Icon.Arms` | Bras — paire de brassards avec coudières |
| `Icon.Legs` | Jambes — paire de jambières avec genouillères |
| `Icon.HeavyWeapon` | Arme lourde — fusil épais avec crosse large |
| `Icon.LightWeapon` | Arme légère — fusil profilé avec chargeur bas |
| `Icon.Pistol` | Pistolet — compact avec crosse vers le bas |
| `Icon.Ammunition` | Munition — cartouche vue de face |

**Usage :**
```xml
<Path Data="{StaticResource Icon.Helmet}"
      Stretch="Uniform"
      Width="20" Height="20"
      Fill="{DynamicResource AccentBrush}" />
```

**Notes :** Toutes les géométries sont dessinées dans un espace 38×38. `Stretch="Uniform"` est obligatoire. Pour le rendu filaire original, utiliser `Stroke` + `Fill="Transparent"` + `StrokeThickness="1.5"`.

---

## 14. Écran "Extractions" (page d'actions)

Les fonctionnalités sont présentées en **cartes d'action** horizontales, groupées par sections.

**Structure d'une carte :**
```
[ ICÔNE ] [ TITRE          ] [ BOUTON ACTION ]
          [ description     ]
```

- Icône : 38×38 px, border-radius 9 px, couleur selon la nature de l'action
- Titre : 13 px / 500
- Description : 11 px, couleur atténuée, peut être multi-ligne
- Bouton : aligné à droite, même couleur que l'icône de la carte

**Barre de progression (pendant l'exécution) :** s'affiche sous la carte concernée, avec track de fond neutre et fill coloré animé. En dark, spinner animé. En light, barre de progression.

---

## 15. Règles de design générales

### Hiérarchie visuelle
Toujours trois niveaux dans une vue : titre, données principales, métadonnées. Jamais de texte à poids identique sur toute une page.

### Espacement
- Gap entre cards : 10–14 px
- Padding interne d'une card : 16–18 px
- Padding cellule tableau : 0 vertical (hauteur fixe), 14 px horizontal

### Border-radius
| Composant | Radius |
|---|---|
| Fenêtre principale | 12 px |
| Card / bloc | 8–10 px |
| Bouton | 6–7 px |
| Badge | 4–5 px |
| Icône d'équipement | 9–10 px |
| Icône de navigation | 8 px |
| Avatar / placeholder | 8 px |

### Transparences (dark uniquement)
Privilégier les couleurs semi-transparentes sur fond sombre plutôt que des couleurs opaques interpolées. Cela garantit la cohérence si le fond change.

### Ce qu'on ne fait pas
- Pas de bordures à 1 px (toujours 0.5 px)
- Pas de couleurs plein opaque pour les fonds de zones secondaires en dark
- Pas de labels répétés dans un tableau quand une légende globale suffit (voir badges d'emplacement)
- Pas d'ombre portée (`box-shadow` / `DropShadow`) — les bordures et les fonds semi-transparents suffisent
- Pas de gradient dans les fonds

---

## 16. Composants Avalonia — Notes d'intégration

### ListBoxItem — indicateur de sélection
FluentAvalonia ajoute un `Rectangle#SelectionIndicator` (barre violette à gauche). Pour le masquer :
```xml
<Style Selector="ListBox.my-class > ListBoxItem:selected /template/ Rectangle#SelectionIndicator">
    <Setter Property="IsVisible" Value="False"/>
</Style>
```

### DataGrid — séparateur de colonne
Agir sur la propriété source, pas le template :
```xml
<Style Selector="DataGridColumnHeader">
    <Setter Property="AreSeparatorsVisible" Value="False" />
</Style>
```

### Bouton Disabled
Ne pas utiliser `Opacity` global sur le bouton — surcharger les brushes individuellement via `:disabled` dans le `ControlTheme`.

### Tooltip d'erreur
Utiliser `ToolTip.Tip` natif d'Avalonia pour le message complet. Ne pas implémenter la visibilité manuellement.

---

## 17. Écran "Réputations"

Affiche l'ensemble des factions du jeu avec la réputation actuelle du joueur. Chargement déclenché manuellement via le bouton "Charger les données" (section 8).

### 17.1 Structure de l'écran

```
[ BARRE DE TITRE — commune ]
[ top-bar : titre "Réputations" | bouton Charger les données ]
[ barre de recherche ]
[ grille de cartes faction — 3 colonnes ]
```

- Grille : `3` colonnes fixes, gap `9 px`
- Chaque carte contient : avatar/initiales · nom de faction · badge état relationnel · un ou plusieurs scopes de réputation (barre + rang)

### 17.2 Avatar / initiales

L'encadré est **toujours neutre**, indépendant de l'état ou du palier. Si une icône PNG de faction est disponible, elle remplace les initiales sans changer le conteneur.

| Élément | Dark | Light |
|---|---|---|
| Fond avatar | `#14FFFFFF` | `#FFF0F0F0` |
| Bordure avatar | `#1AFFFFFF` | `#FFE0E0E0` |
| Texte initiales | `#66FFFFFF` | `#FFAAAAAA` |
| Taille | 30×30 px | 30×30 px |
| Border-radius | 7 px | 7 px |
| Police | 10 px / 600 / letter-spacing 0.03em | idem |

### 17.3 Code couleur — État relationnel (bordure gauche de carte)

La bordure gauche de `2 px` (seule exception à la règle des `0.5 px`) encode l'état relationnel. La bordure périphérique de la carte reste à `0.5 px`.

| État | Dark bordure gauche | Dark bordure carte | Light bordure gauche | Light bordure carte |
|---|---|---|---|---|
| Allié | `#FF1D9E75` | `#591D9E75` | `#FF1D9E75` | `#401D9E75` |
| Neutre | `#30FFFFFF` | `#17FFFFFF` | `#FFCCCCCC` | `#FFE4E4E4` |
| Hostile | `#FFE24B4A` | `#4DE24B4A` | `#FFE24B4A` | `#33E24B4A` |
| Non chargé | `#14FFFFFF` | `#0AFFFFFF` | `#FFE0E0E0` | `#FFEEEEEE` |

**Badge texte état** (sous le nom de faction, 10 px / 500 / caps) :

| État | Dark | Light |
|---|---|---|
| ALLIÉ | `#FF5DCAA5` | `#FF0F6E56` |
| NEUTRE | `#40FFFFFF` | `#FFAAAAAA` |
| HOSTILE | `#FFF09595` | `#FFA32D2D` |
| NON CHARGÉ | `#1FFFFFFF` | `#FFCCCCCC` |

**Fond de carte par état :**

| État | Dark | Light |
|---|---|---|
| Allié | `#05FFFFFF` | `#FFFAFAFA` |
| Neutre | `#05FFFFFF` | `#FFFAFAFA` |
| Hostile | `#08E24B4A` | `#FFF9F3F3` |
| Non chargé | `#05FFFFFF` (opacity 0.55) | `#FFFAFAFA` (opacity 0.55) |

### 17.4 Code couleur — Palier de réputation (fill de barre)

Le fill encode le palier atteint, **indépendamment de la faction**. La track reste toujours neutre.

| Palier | Nom générique | Dark fill | Light fill |
|---|---|---|---|
| P1 | Not Eligible | `#FF555555` | `#FF888888` |
| P2 | Applicant | `#FF85B7EB` | `#FF378ADD` |
| P3 | Trainee | `#FFEF9F27` | `#FFBA7517` |
| P4 | Jr. Rank | `#FF5DCAA5` | `#FF1D9E75` |
| P5 | Rank | `#FF1D9E75` | `#FF0F6E56` |
| P6 | Sr. Rank | `#FFA78BFA` | `#FF534AB7` |
| P7 | Master | `#FFEF9F27` + outline `1.5 px` | `#FFBA7517` + outline `1.5 px` |

> **Note P7 Master :** même couleur ambre que P3, distingué par un contour de `1.5 px` sur la barre et un texte de rang en gras (500).

**Track (fond de barre) et dimensions :**

| Élément | Dark | Light |
|---|---|---|
| Track | `#0FFFFFFF` | `#FFEBEBEB` |
| Hauteur barre | 4 px | 4 px |
| Border-radius | 2 px | 2 px |

**Label de scope** (au-dessus de chaque barre, 10 px / 500 / caps) :

| | Dark | Light |
|---|---|---|
| Couleur | `#33FFFFFF` | `#FFBBBBBB` |

**Texte de rang** (sous la barre, 10 px / normal) :

| | Dark | Light |
|---|---|---|
| Couleur | `#59FFFFFF` | `#FF999999` |
| Couleur P7 | `#B3FFFFFF` / 500 | `#FF555555` / 500 |

### 17.5 État "Non chargé"

Carte à `opacity: 0.55`, sans barres de progression. Un texte d'aide remplace les scopes.

| Élément | Dark | Light |
|---|---|---|
| Texte d'aide | `#1FFFFFFF` — 10 px | `#FFCCCCCC` — 10 px |

### 17.6 Règles de composition

- **Scopes multiples** : s'empilent verticalement dans la carte, séparés par un gap de `6 px`. Chaque scope est indépendant (label + barre + rang).
- **Pas de couleur par faction** : l'avatar/initiales est toujours gris neutre. La couleur n'encode que l'état et le palier.
- **Bordure gauche 2 px** : seule exception à la règle générale des `0.5 px`. Justifiée car elle encode une information de premier niveau (état relationnel) et doit être perçue immédiatement dans la grille.
- **Nombre de colonnes** : 3 colonnes fixes. Si le nombre de factions est important, la grille défile verticalement.

---

*Dernière mise à jour : mai 2026*