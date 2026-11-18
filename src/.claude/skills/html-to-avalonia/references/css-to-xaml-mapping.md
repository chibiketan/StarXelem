# Référence de mapping CSS → Avalonia XAML (charte StarXelem)

Les maquettes de la charte sont les fichiers `project/<Ecran>.dc.html` de l'artefact « StarXelem, nouvelles directions ». Elles écrivent leurs styles en ligne, avec des couleurs en dur : chaque valeur se traduit par une ressource `Sx.*` ou une classe de `Style/SxTheme.axaml`, jamais par une couleur en dur dans le XAML.

## Table des matières
1. [Couleurs](#1-couleurs)
2. [Formes : angles coupés](#2-formes--angles-coupés)
3. [Typographie](#3-typographie)
4. [Layout](#4-layout)
5. [Composants](#5-composants)
6. [Pièges Avalonia et FluentAvalonia](#6-pièges-avalonia-et-fluentavalonia)

---

## 1. Couleurs

| CSS de la maquette | Ressource Avalonia |
|---|---|
| `#0B0D12` | `Sx.BackgroundBrush` (et `Sx.OnAccentBrush` pour le texte d'un bouton plein) |
| `#12151C` | `Sx.SurfaceBrush` |
| `#1A1E27` | `Sx.ReliefBrush` |
| `#2A2F3A` | `Sx.LineBrush` |
| `#F2F4F8` | `Sx.TextBrush` |
| `#A4ACBA` | `Sx.SecondaryBrush` (`Sx.NeutralBrush` dans un badge) |
| `#8A93A5` | `Sx.MutedBrush` |
| `{{accent}}`, `#E8B04A` | `Sx.AccentBrush` |
| `{{accentSoft}}`, `accent + "24"` | `Sx.AccentSoftBrush` |
| `accent + "55"` | `Sx.AccentStrongBrush` |
| `#4FD1A5`, `#4FD1A524` | `Sx.SuccessBrush`, `Sx.SuccessSoftBrush` |
| `#F08A4B`, `#F08A4B24` | `Sx.WarningBrush`, `Sx.WarningSoftBrush` |
| `#F2666A`, `#F2666A24` | `Sx.DangerBrush`, `Sx.DangerSoftBrush` |
| `#6CB6FF`, `#6CB6FF24` | `Sx.InfoBrush`, `Sx.InfoSoftBrush` |

Toujours `{DynamicResource Sx.…}`. Un écran migré force le thème sombre :

```xml
<ThemeVariantScope RequestedThemeVariant="Dark">
    <Grid Background="{DynamicResource Sx.BackgroundBrush}" TextElement.FontFamily="{DynamicResource Sx.FontSans}">
        …
    </Grid>
</ThemeVariantScope>
```

---

## 2. Formes : angles coupés

La maquette coupe les angles avec `clip-path: polygon(0 0, calc(100% - Npx) 0, 100% Npx, 100% 100%, Npx 100%, 0 calc(100% - Npx))`. En XAML, c'est `comp:NotchedBorder` (`xmlns:comp="clr-namespace:StarXelem.Components"`) avec `CutSize="N"` :

```xml
<comp:NotchedBorder CutSize="18" Background="{DynamicResource Sx.SurfaceBrush}" Padding="28,26">
    …
</comp:NotchedBorder>
```

| N dans la maquette | Usage |
|---|---|
| 7 | badge, pastille d'icône 24 px |
| 9 | ligne de liste, bouton de menu, petit champ |
| 11 | champ, bouton 44 px, tuile d'icône 56 px |
| 12 | panneau flottant |
| 14 | carte dans un bloc |
| 18 (16 sur d'anciennes planches) | bloc principal, menu latéral |

- Contour : `BorderBrush` + `StrokeThickness` du `NotchedBorder` (pas `BorderThickness`).
- `border-radius: 19px` d'une puce → `Button.sx-chip` (rayon 17, hauteur 34). `border-radius: 6px` d'un badge → `NotchedBorder.sx-badge` (coupe 7) : la charte remplace les arrondis des badges par des angles coupés.
- `border-radius: 2px` d'une barre de 4 px → `ProgressBar.sx-quality`.
- Aucun `CornerRadius` ailleurs.

---

## 3. Typographie

| CSS | Avalonia |
|---|---|
| `font-family: 'IBM Plex Sans'` | `{DynamicResource Sx.FontSans}` (Inter tant que Plex n'est pas embarquée), posé une fois sur la racine via `TextElement.FontFamily` |
| `font-family: 'IBM Plex Mono'` | `{DynamicResource Sx.FontMono}` ou classe `sx-mono` |
| `font-size: 30px; font-weight: 600` | `FontSize="30" FontWeight="SemiBold"` (titre de détail) |
| `font-size: 22px` mono | valeur de carte |
| `font-size: 20px; font-weight: 600` | `TextBlock.sx-title` |
| `font-size: 16px; font-weight: 600` | titre de section |
| `font-size: 13px`, `14px` | texte courant |
| `font-size: 12px; color: #A4ACBA` | `TextBlock.sx-caption` |
| `font-size: 11px` mono | `TextBlock.sx-count` |

Poids : 400 → `Normal`, 500 → `Medium`, 600 → `SemiBold`. Pas de 700.

> `SelectableTextBlock` n'hérite pas des styles `TextBlock.xxx` : poser `FontFamily`, `FontSize` et `Foreground` en ligne.

---

## 4. Layout

| CSS | Avalonia |
|---|---|
| `display: flex` (ligne) | `StackPanel Orientation="Horizontal" Spacing="…"`, ou `Grid ColumnDefinitions="Auto,*,Auto"` quand un élément s'étire |
| `display: flex; flex-direction: column; gap` | `StackPanel Spacing="…"` |
| `flex-wrap: wrap` | `WrapPanel` (espacement par `Margin="0,0,6,6"` des enfants) |
| `flex: 1 1 …` | colonne `*` d'un `Grid` |
| `flex: 0 0 300px` | colonne de largeur fixe |
| `grid-template-columns: repeat(auto-fill, minmax(Mpx, 1fr))` | `UniformGrid` dont `Columns` est lié à la largeur : `Columns="{Binding $parent[ItemsControl].Bounds.Width, Converter={x:Static conv:WidthToColumnsConverter.Instance}, ConverterParameter=M}"`, marge `0,0,G,G` sur l'élément et `0,0,-G,-G` sur l'`ItemsControl` |
| `overflow-y: auto` | `ScrollViewer VerticalScrollBarVisibility="Auto"` |
| page d'un écran | `Grid ColumnDefinitions="…" ColumnSpacing="12" Margin="12"` : la fenêtre fournit le fond, chaque bloc est un `NotchedBorder` Surface coupe 18 |

`BorderThickness` et `Margin` suivent l'ordre **gauche, haut, droite, bas**.

---

## 5. Composants

| Élément de maquette | XAML |
|---|---|
| bouton plein accent | `Button Classes="sx primary"` (un seul par barre d'outils) |
| bouton fond `#1A1E27` | `Button Classes="sx"` ; `small` pour 34 px ; `on` pour une bascule ouverte |
| `input` à angles coupés + loupe | `comp:NotchedBorder Classes="sx-field"` contenant `Path Classes="sx-search-icon"` et `TextBox Classes="sx"` |
| puce de filtre avec compteur | `Button Classes="sx-chip"`, `Classes.on="{Binding IsSelected}"`, compteur en `TextBlock.sx-count` ; ViewModel `FilterChipViewModel`, la puce « Tous » dans la même collection |
| badge pastille + libellé | `comp:NotchedBorder Classes="sx-badge success"` (ou `info`, `warning`, `danger`, neutre sans classe) contenant `Ellipse` + `TextBlock` |
| tableau | `DataGrid Classes="sx"`, en-têtes `comp:ColumnHeader` pour le tri et le filtre de colonne |
| pastille d'emplacement | `comp:LocationIcon` |
| liste à lignes coupées | `ListBox` dont l'`ItemContainer` a pour modèle un `NotchedBorder CutSize="9"` ; fond `Sx.ReliefBrush` au survol, `Sx.AccentSoftBrush` sélectionné |
| case à cocher | `CheckBox Classes="sx-muted"` (coche en accent or) |
| barre de progression 4 px | `ProgressBar Classes="sx-quality"` |
| icône SVG en contour | `Path` avec `Data` = le `d` du SVG, `Stroke`, `StrokeThickness` = `stroke-width`, `StrokeLineCap="Round"`, `StrokeJoin="Round"`, `Stretch="Uniform"`, taille fixe |
| chargement | `ui:ProgressRing IsIndeterminate="True"` sur un voile Surface à 0,75 d'opacité |

---

## 6. Pièges Avalonia et FluentAvalonia

- **`ListBoxItem` FluentAvalonia** : largeur minimale de 88 px. Pour un élément plus étroit, remettre `MinWidth="0"` et `MinHeight="0"`.
- **Modèle de `ListBoxItem`** : ne pas nommer le `ContentPresenter` `PART_ContentPresenter`, sinon les styles de sélection Fluent s'y appliquent.
- **`Run.Text`** est lié dans les deux sens par défaut : avec un `StringFormat`, la valeur reformatée revient dans la source et boucle jusqu'au débordement de pile. Mettre `Mode=OneWay`, ou calculer le texte dans le ViewModel.
- **Couleurs Fluent d'un contrôle** (case cochée, curseur) : redéfinir ses ressources (`CheckBoxCheckBackgroundFillChecked`, `SliderThumbBackground`…) avec les valeurs `Sx` dans `Style.Resources`, comme `CheckBox.sx-muted`.
- **`MinWidth` d'une colonne `DataGrid`** : propriété CLR, à poser sur la colonne.
- **`DynamicResource`** pour les pinceaux, **`StaticResource`** pour les convertisseurs et les géométries fixes.
