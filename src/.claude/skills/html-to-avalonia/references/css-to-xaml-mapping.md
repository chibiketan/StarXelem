# Référence de mapping CSS → Avalonia XAML

## Table des matières
1. [Layout](#1-layout)
2. [Couleurs et tokens](#2-couleurs-et-tokens)
3. [Typographie](#3-typographie)
4. [Bordures et coins arrondis](#4-bordures-et-coins-arrondis)
5. [Espacements](#5-espacements)
6. [Contrôles courants](#6-contrôles-courants)
7. [Patterns FluentAvalonia spécifiques](#7-patterns-fluentavalonia-spécifiques)

---

## 1. Layout

### Flexbox → Grid / StackPanel

| CSS | Avalonia XAML |
|-----|---------------|
| `display: flex; flex-direction: row` | `<StackPanel Orientation="Horizontal">` |
| `display: flex; flex-direction: column` | `<StackPanel Orientation="Vertical">` |
| `display: flex; gap: 8px` (row) | `<StackPanel Orientation="Horizontal" Spacing="8">` |
| `display: flex; justify-content: space-between` | `<Grid>` avec colonnes `* Auto` ou `*` + `HorizontalAlignment` |
| `display: flex; align-items: center` | `VerticalAlignment="Center"` sur les enfants ou `<StackPanel>` avec `VerticalAlignment` |
| `display: flex; flex-wrap: wrap` | `<WrapPanel>` |
| `flex: 1` | `HorizontalAlignment="Stretch"` ou colonne `*` dans un Grid |
| `display: grid; grid-template-columns: 1fr 2fr` | `<Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="2*"/></Grid.ColumnDefinitions>` |

### Cas typique : header de carte (titre à gauche + badge à droite)

**HTML:**
```html
<div style="display:flex; justify-content:space-between; align-items:center">
  <span class="title">Nom</span>
  <span class="badge">T1</span>
</div>
```

**XAML:**
```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="*"/>
    <ColumnDefinition Width="Auto"/>
  </Grid.ColumnDefinitions>
  <TextBlock Grid.Column="0" Text="Nom" Style="{StaticResource TextTitlePrimary}"/>
  <Border Grid.Column="1" Style="{StaticResource BadgeTier}" Tag="T1">
    <TextBlock Text="T1" Style="{StaticResource TextBadge}"/>
  </Border>
</Grid>
```

---

## 2. Couleurs et tokens

### Correspondance CSS var → StaticResource Avalonia

Les tokens sont définis dans `tokens.json` et matérialisés dans `Themes/Tokens.axaml`.

| CSS variable | StaticResource (dark) | StaticResource (light) | StaticResource thémé |
|---|---|---|---|
| `var(--bg-primary)` | `BackgroundPrimaryDark` | `BackgroundPrimaryLight` | `BackgroundPrimary` |
| `var(--bg-secondary)` | `BackgroundSecondaryDark` | `BackgroundSecondaryLight` | `BackgroundSecondary` |
| `var(--bg-tertiary)` | `BackgroundTertiaryDark` | `BackgroundTertiaryLight` | `BackgroundTertiary` |
| `var(--text-primary)` | `TextPrimaryDark` | `TextPrimaryLight` | `TextPrimary` |
| `var(--text-secondary)` | `TextSecondaryDark` | `TextSecondaryLight` | `TextSecondary` |
| `var(--text-tertiary)` | `TextTertiaryDark` | `TextTertiaryLight` | `TextTertiary` |
| `var(--text-accent)` | `TextAccentDark` | `TextAccentLight` | `TextAccent` |
| `var(--border-primary)` | `BorderPrimaryDark` | `BorderPrimaryLight` | `BorderPrimary` |
| `var(--border-secondary)` | `BorderSecondaryDark` | `BorderSecondaryLight` | `BorderSecondary` |
| `var(--accent-primary)` | `AccentPrimaryDark` | `AccentPrimaryLight` | `AccentPrimary` |
| `var(--accent-subtle)` | `AccentSubtleDark` | `AccentSubtleLight` | `AccentSubtle` |
| `var(--tier-t1)` | `TierT1Dark` | `TierT1Light` | `TierT1` |
| `var(--tier-t2)` | `TierT2Dark` | `TierT2Light` | `TierT2` |
| `var(--tier-t3)` | `TierT3Dark` | `TierT3Light` | `TierT3` |

### Utilisation dans XAML

```xml
<!-- Fond d'une carte -->
<Border Background="{DynamicResource BackgroundSecondary}">

<!-- Texte titre -->
<TextBlock Foreground="{DynamicResource TextPrimary}">

<!-- Bordure -->
<Border BorderBrush="{DynamicResource BorderPrimary}" BorderThickness="1">
```

> **Important** : utiliser `DynamicResource` (pas `StaticResource`) pour les couleurs thémées, afin que le changement de thème à chaud fonctionne.

---

## 3. Typographie

### Mapping tailles

| CSS (`font-size`) | Token | Avalonia (`FontSize`) |
|---|---|---|
| `10px` | `typography.size.xs` | `10` |
| `11px` | `typography.size.sm` | `11` |
| `13px` | `typography.size.md` | `13` |
| `15px` | `typography.size.lg` | `15` |
| `18px` | `typography.size.xl` | `18` |
| `22px` | `typography.size.xxl` | `22` |

### Mapping poids

| CSS (`font-weight`) | Token | Avalonia (`FontWeight`) |
|---|---|---|
| `400` | `regular` | `Regular` |
| `500` | `medium` | `Medium` |
| `600` | `semibold` | `SemiBold` |
| `700` | `bold` | `Bold` |

### Styles de texte prédéfinis (dans `Styles/Typography.axaml`)

```xml
<Style x:Key="TextTitlePrimary" TargetType="TextBlock">
    <Setter Property="FontSize" Value="18"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="{DynamicResource TextPrimary}"/>
</Style>

<Style x:Key="TextLabel" TargetType="TextBlock">
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="FontWeight" Value="Medium"/>
    <Setter Property="Foreground" Value="{DynamicResource TextSecondary}"/>
</Style>

<Style x:Key="TextCaption" TargetType="TextBlock">
    <Setter Property="FontSize" Value="10"/>
    <Setter Property="FontWeight" Value="Regular"/>
    <Setter Property="Foreground" Value="{DynamicResource TextTertiary}"/>
</Style>

<Style x:Key="TextBadge" TargetType="TextBlock">
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
</Style>
```

---

## 4. Bordures et coins arrondis

| CSS | Token | Avalonia |
|-----|-------|---------|
| `border-radius: 4px` | `radius.sm` | `CornerRadius="4"` |
| `border-radius: 8px` | `radius.md` | `CornerRadius="8"` |
| `border-radius: 12px` | `radius.lg` | `CornerRadius="12"` |
| `border-radius: 16px` | `radius.xl` | `CornerRadius="16"` |
| `border-radius: 9999px` | `radius.full` | `CornerRadius="9999"` |
| `border: 1px solid` | — | `BorderThickness="1"` |
| `border-bottom: 1px solid` | — | `BorderThickness="0,0,0,1"` (Top, Left, Bottom, Right... non, **L,T,R,B**) |

> **Rappel Avalonia** : `BorderThickness` suit l'ordre **Left, Top, Right, Bottom**.

---

## 5. Espacements

| CSS (`gap` / `padding` / `margin`) | Token | Avalonia (`Spacing` / `Padding` / `Margin`) |
|---|---|---|
| `4px` | `spacing.xs` | `4` |
| `8px` | `spacing.sm` | `8` |
| `12px` | `spacing.md` | `12` |
| `16px` | `spacing.lg` | `16` |
| `24px` | `spacing.xl` | `24` |
| `32px` | `spacing.xxl` | `32` |

**Syntaxe Avalonia pour padding/margin** :
- `Padding="12"` → tous côtés
- `Padding="16,8"` → gauche/droite=16, haut/bas=8
- `Padding="8,4,8,4"` → gauche, haut, droite, bas

---

## 6. Contrôles courants

### Carte (card)

**HTML:**
```html
<div class="card" style="background:var(--bg-secondary); border-radius:12px; padding:16px; border:1px solid var(--border-primary)">
```

**XAML:**
```xml
<Border Background="{DynamicResource BackgroundSecondary}"
        CornerRadius="12"
        Padding="16"
        BorderBrush="{DynamicResource BorderPrimary}"
        BorderThickness="1">
```

### Badge tier

**HTML:**
```html
<span class="badge tier-t1" style="background:var(--tier-t1); border-radius:4px; padding:2px 8px">T1</span>
```

**XAML:**
```xml
<Border Background="{DynamicResource TierT1}"
        CornerRadius="4"
        Padding="8,2">
    <TextBlock Text="T1"
               Style="{StaticResource TextBadge}"
               Foreground="{DynamicResource TextInverse}"/>
</Border>
```

### Séparateur horizontal

**HTML:**
```html
<hr style="border-color: var(--border-secondary)"/>
```

**XAML:**
```xml
<Rectangle Height="1"
           Fill="{DynamicResource BorderSecondary}"
           HorizontalAlignment="Stretch"/>
```

### Label + valeur (paire de données)

**HTML:**
```html
<div class="field">
  <span class="label">SCU</span>
  <span class="value">24</span>
</div>
```

**XAML:**
```xml
<StackPanel Orientation="Vertical" Spacing="2">
    <TextBlock Text="SCU" Style="{StaticResource TextLabel}"/>
    <TextBlock Text="24" Style="{StaticResource TextTitlePrimary}"/>
</StackPanel>
```

---

## 7. Patterns FluentAvalonia spécifiques

### Cacher l'indicateur de sélection d'un ListBoxItem

```xml
<Style Selector="ListBox.my-class > ListBoxItem:selected /template/ Rectangle#SelectionIndicator">
    <Setter Property="IsVisible" Value="False"/>
</Style>
```

### Cacher les séparateurs de colonnes DataGrid

```xml
<Style Selector="DataGridColumnHeader">
    <Setter Property="AreSeparatorsVisible" Value="False"/>
</Style>
```

### MinWidth sur une colonne DataGrid

`MinWidth` est une propriété CLR, pas une AvaloniaProperty — la définir directement sur la colonne :
```xml
<DataGridTextColumn MinWidth="0" Width="*" Header="Nom" Binding="{Binding Name}"/>
```

### DynamicResource vs StaticResource pour les thèmes

- `DynamicResource` → couleurs, brushes (changement de thème à chaud)
- `StaticResource` → styles de texte, templates, valeurs fixes qui ne changent pas avec le thème
