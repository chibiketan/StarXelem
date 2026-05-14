---
name: starxelem-design
description: Use when creating, modifying, or reviewing any StarXelem UI element — XAML, Avalonia control, color, layout, badge, button, card, table, sidebar, titlebar, or design token. Triggers on "crée l'écran", "modifie le design", "change les couleurs", "fait une card", "UX", "UI", "maquette", "XAML", "Avalonia", or any visual request for StarXelem.
---

# StarXelem Design

**Respecte strictement @CLAUDE_design_convention.md.** Source unique et autoritaire pour tout aspect visuel.

## Règle absolue

**Ne modifie JAMAIS** couleur, taille, espacement, bordure, police, radius ou règle sans validation explicite. Ce skill applique — n'ajoute pas.

## Workflow

1. **Lis** `CLAUDE_design_convention.md` avant toute action UI
2. **Trouve** la section pertinente
3. **Applique** les valeurs exactes

## Sections clés

| Section | Couvre |
|---|---|
| 1-2 | Architecture, surfaces dark/light |
| 3 | Accentuation violet |
| 4 | Typographie (10-16px) |
| 5 | Sidebar (52px) |
| 6-10 | Titre, badges, boutons, recherche, gRPC |
| 11-13 | Tableaux, cartes, icônes |
| 14-17 | Extractions, règles générales, Avalonia, Réputations |

## Tokens

Couleurs ARGB du fichier = seules autoritaires. Cas non couvert → logique du fichier (ex: `couleur_base` à alpha X).

## Rationalisations interdites

| Excuse | Réalité |
|---|---|
| "C'est cohérent avec le reste" | Le reste peut être faux. |
| "Petite variation" | 0.5px → 1px = violation. |
| "Je propose une alternative" | Propose → valide → change. |
| "C'est plus moderne" | Moderne ≠ conventions. |

## Red Flags — STOP

- Couleur hex non listée
- Bordure 1px au lieu de 0.5px
- +2 tailles de police dans un composant
- Ombre/gradient ajouté
- Light omis quand Dark défini
- Badge modifié sans re-vérifier section 7
