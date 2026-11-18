---
name: html-to-avalonia
description: >
  Traduit les maquettes HTML/CSS StarXelem en code XAML Avalonia UI / FluentAvalonia,
  en respectant les design tokens du projet définis dans tokens.json.
  Utiliser ce skill dès qu'une tâche implique : convertir une maquette HTML en XAML,
  créer un UserControl Avalonia à partir d'un écran HTML, intégrer un composant
  visuel StarXelem, générer un ResourceDictionary de tokens, ou vérifier qu'un
  fichier XAML est cohérent avec les tokens du projet. Ce skill s'applique aussi
  pour toute question de correspondance CSS ↔ XAML dans le contexte StarXelem.
---

# html-to-avalonia — Skill de traduction maquette → XAML

## Contexte

Ce skill traduit les maquettes HTML/CSS produites pour **StarXelem** en code XAML
compatible **Avalonia UI 11.3 / FluentAvalonia**, selon la charte de la refonte (direction « Inventaire expert », novembre 2026). La référence complète de la charte est `src/CLAUDE_design_convention.md`.

Les valeurs de la charte (couleurs, typographie, coupes d'angle, tailles) sont dans `tokens.json`, situé dans ce répertoire de skill, et matérialisées dans `src/StarXelem/Style/SxTheme.axaml`.

---

## ⚠️ Règle absolue : tokens.json est en lecture seule

**`tokens.json` ne doit JAMAIS être modifié sans autorisation explicite de l'utilisateur.**

### Procédure si un token de la maquette est absent de tokens.json

1. **Signaler** le token manquant à l'utilisateur :
   ```
   ⚠️ Token manquant dans tokens.json :
   - Nom suggéré : color.background.overlay-strong
   - Valeur       : #66000000
   - Usage        : Overlay semi-opaque pour les dialogues modaux
   - Présent dans : [nom de la maquette]

   Souhaitez-vous que je l'ajoute à tokens.json ?
   ```
2. **Attendre** une confirmation explicite de l'utilisateur ("oui", "ajoute-le", etc.)
3. **Seulement après confirmation** : modifier `tokens.json` en respectant la structure existante
4. Continuer la traduction en utilisant la valeur proposée en attendant

Ne jamais modifier `tokens.json` de façon proactive, même si le token semble évident.

---

## Workflow de traduction

### Étape 1 — Lire les fichiers de référence

Avant toute traduction, charger :
- Ce fichier (`SKILL.md`) — déjà chargé
- `tokens.json` — lire pour connaître tous les tokens disponibles
- `references/css-to-xaml-mapping.md` — lire pour les règles de mapping

### Étape 2 — Analyser la maquette HTML

Identifier dans la maquette :
- Les **couleurs en dur** des styles en ligne → les mapper aux ressources `Sx.*` (voir `references/css-to-xaml-mapping.md`)
- Les **valeurs absentes de la charte** → les signaler (règle lecture seule)
- La **structure de layout** (flex, grid) → déterminer l'équivalent Avalonia
- Les **composants réutilisables** → proposer des `UserControl` ou `DataTemplate`

### Étape 3 — Générer le XAML

Produire dans cet ordre :
1. Le `UserControl` ou `Window` principal
2. Les styles locaux nécessaires (si non couverts par les styles globaux)
3. Le ViewModel associé (propriétés et commandes minimales)

### Étape 4 — Vérification

Avant de livrer le code :
- [ ] Toutes les ressources `Sx.*` référencées existent dans `SxTheme.axaml`
- [ ] Les angles coupés passent par `comp:NotchedBorder`, sans `CornerRadius` (sauf puces et barres)
- [ ] L'écran est dans un `ThemeVariantScope` sombre
- [ ] Aucun token inventé (valeur couleur codée en dur sans passer par un resource)
- [ ] Layout Avalonia valide (pas de propriétés CSS dans XAML)
- [ ] Conventions de nommage StarXelem respectées (voir skill `starxelem-csharp-conventions`)
- [ ] `DynamicResource` utilisé pour les couleurs (pas `StaticResource`)
- [ ] Classes `sx-*` utilisées plutôt que des tailles et couleurs en ligne

---

## Où vivent les éléments de la charte

```
Style/SxTheme.axaml        ← ressources Sx.* (couleurs, polices) et classes sx-* : à réutiliser, ne pas recopier
Components/NotchedBorder   ← bloc à angles coupés (CutSize)
Components/ColumnHeader    ← en-tête de colonne avec tri et filtre
Components/LocationIcon    ← pastille d'emplacement
Views/{Nom}View.axaml      ← écran traduit (styles propres à l'écran dans UserControl.Styles)
ViewModels/{Nom}ViewModel.cs
```

Il n'y a pas de `Tokens.axaml` généré : `SxTheme.axaml` matérialise `tokens.json` à la main, et les deux doivent rester identiques. Une nouvelle valeur commune à plusieurs écrans va dans `SxTheme.axaml` (et `tokens.json`), pas dans une vue.

La charte n'existe qu'en thème sombre : pas de variante claire à produire.

---

## Conventions de nommage des ressources

| Token (`tokens.json`) | Ressource XAML |
|---|---|
| `color.background`, `surface`, `relief`, `line` | `Sx.BackgroundBrush`, `Sx.SurfaceBrush`, `Sx.ReliefBrush`, `Sx.LineBrush` |
| `color.text`, `secondary`, `muted` | `Sx.TextBrush`, `Sx.SecondaryBrush`, `Sx.MutedBrush` |
| `color.accent.*` | `Sx.AccentBrush`, `Sx.AccentHoverBrush`, `Sx.AccentSoftBrush`, `Sx.AccentStrongBrush`, `Sx.OnAccentBrush` |
| `color.tone.{ton}` | `Sx.{Ton}Brush` et `Sx.{Ton}SoftBrush` (`Success`, `Warning`, `Danger`, `Info`, `Neutral`) |
| `typography.family.*` | `Sx.FontSans`, `Sx.FontMono` |
| `shape.cut.*` | valeur de `CutSize` sur `comp:NotchedBorder` |

---
## Références

- **`tokens.json`** — Valeurs de la charte (couleurs ARGB, typographie, coupes d'angle, tailles)
- **`references/css-to-xaml-mapping.md`** — Correspondance des styles de maquette vers les ressources et classes `Sx`, pièges Avalonia
- **`src/CLAUDE_design_convention.md`** — Référence complète de la charte (mise en page, composants, comportements)
