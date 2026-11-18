---
name: starxelem-design
description: Use when creating, modifying, or reviewing any StarXelem UI element — XAML, Avalonia control, color, layout, badge, button, card, table, sidebar, titlebar, or design token. Triggers on "crée l'écran", "modifie le design", "change les couleurs", "fait une card", "UX", "UI", "maquette", "XAML", "Avalonia", or any visual request for StarXelem.
---

# StarXelem Design

**Respecte strictement @CLAUDE_design_convention.md.** Source unique et autoritaire pour tout aspect visuel : la charte de la refonte (novembre 2026, direction « Inventaire expert »).

## Règle absolue

**Ne modifie JAMAIS** couleur, taille, espacement, coupe d'angle, police ou règle sans validation explicite. Ce skill applique — n'ajoute pas.

## Workflow

1. **Lis** `CLAUDE_design_convention.md` avant toute action UI
2. **Lis la maquette** de l'écran (`project/<Ecran>.dc.html` de l'artefact « StarXelem, nouvelles directions ») avant de le modifier
3. **Réutilise** `Style/SxTheme.axaml` (ressources `Sx.*`, classes `sx-*`) et `Components/` (`NotchedBorder`, `ColumnHeader`, `LocationIcon`)
4. **Vérifie** par une capture d'écran (skill `starxelem-take-screenshot`)

## Sections clés

| Section | Couvre |
|---|---|
| 1 | Règles générales |
| 2 | Couleurs et tons de badge |
| 3 | Typographie (30 / 22 / 20 / 16 / 14 / 13 / 12 / 11) |
| 4 | Formes : angles coupés 7 à 18, puces, icônes |
| 5 | Mise en page, trois modèles d'écran, menu latéral |
| 6 | Composants (`sx`, `sx-chip`, `sx-badge`, `DataGrid.sx`, listes, cartes…) |
| 7-8 | Emplacement d'un objet, familles de blueprint |
| 9-10 | Tri et filtre par colonne, filtrage différé |
| 11 | Pièges Avalonia / FluentAvalonia |
| 12 | Éléments encore hors charte |

## Tokens

Valeurs ARGB de `SxTheme.axaml` et de `.claude/skills/html-to-avalonia/tokens.json` = seules autoritaires. Cas non couvert → logique de la charte (ex. ton à alpha `24` pour un fond doux).

## Rationalisations interdites

| Excuse | Réalité |
|---|---|
| "C'est cohérent avec le reste" | Le reste peut être hors charte (section 12). |
| "Petite variation" | Une coupe de 10 au lieu de 11 = violation. |
| "Je propose une alternative" | Propose → valide → change. |
| "Un coin arrondi rend mieux" | Seules les puces et les barres sont arrondies. |

## Red Flags — STOP

- Couleur hex en dur dans une vue au lieu d'une ressource `Sx.*`
- `CornerRadius` sur autre chose qu'une puce ou une barre de progression
- Accent autre que l'or `#E8B04A`, ou ancienne ressource violette
- Statut signalé par la couleur seule, sans libellé
- Deux boutons `.primary` dans une même barre d'outils
- Ombre ou dégradé ajouté
- Écran sans `ThemeVariantScope` sombre
- Filtrage calculé sur le thread d'UI, sans délai ni annulation
