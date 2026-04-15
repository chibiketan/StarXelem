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
compatible **Avalonia UI 11.3.11 / FluentAvalonia**.

La source de vérité de tous les tokens de design (couleurs, espacements, typographie,
rayons) est le fichier `tokens.json` situé dans ce répertoire de skill.

---

## ⚠️ Règle absolue : tokens.json est en lecture seule

**`tokens.json` ne doit JAMAIS être modifié sans autorisation explicite de l'utilisateur.**

### Procédure si un token de la maquette est absent de tokens.json

1. **Signaler** le token manquant à l'utilisateur :
   ```
   ⚠️ Token manquant dans tokens.json :
   - Nom suggéré : color.background.overlay-strong
   - Valeur dark  : #66000000
   - Valeur light : #33000000
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
- Les **variables CSS** utilisées (`var(--xxx)`) → les mapper aux tokens
- Les **variables CSS inconnues** → les signaler (règle lecture seule)
- La **structure de layout** (flex, grid) → déterminer l'équivalent Avalonia
- Les **composants réutilisables** → proposer des `UserControl` ou `DataTemplate`

### Étape 3 — Générer le XAML

Produire dans cet ordre :
1. Le `UserControl` ou `Window` principal
2. Les styles locaux nécessaires (si non couverts par les styles globaux)
3. Le ViewModel associé (propriétés et commandes minimales)

### Étape 4 — Vérification

Avant de livrer le code :
- [ ] Tous les `DynamicResource` référencés existent dans `tokens.json`
- [ ] Aucun token inventé (valeur couleur codée en dur sans passer par un resource)
- [ ] Layout Avalonia valide (pas de propriétés CSS dans XAML)
- [ ] Conventions de nommage StarXelem respectées (voir skill `starxelem-csharp-conventions`)
- [ ] `DynamicResource` utilisé pour les couleurs (pas `StaticResource`)
- [ ] `StaticResource` utilisé pour les styles de texte fixes

---

## Structure des fichiers de sortie attendus

```
Views/
├── {Nom}View.axaml          ← UserControl traduit
└── {Nom}View.axaml.cs       ← Code-behind minimal

ViewModels/
└── {Nom}ViewModel.cs        ← ViewModel associé (si demandé)

Themes/
├── Tokens.axaml             ← ResourceDictionary généré depuis tokens.json
└── Styles/
    ├── Typography.axaml     ← Styles de texte
    └── Controls.axaml       ← Styles de contrôles communs
```

---

## Génération du ResourceDictionary (Tokens.axaml)

Quand l'utilisateur demande de générer ou mettre à jour `Tokens.axaml` depuis `tokens.json`,
produire un ResourceDictionary avec deux variantes par token de couleur :

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Généré automatiquement depuis tokens.json — NE PAS MODIFIER MANUELLEMENT -->
    <!-- Modifier tokens.json puis régénérer ce fichier -->

    <!-- ==== THÈME SOMBRE ==== -->
    <ResourceDictionary.ThemeDictionaries>

        <ResourceDictionary x:Key="Dark">
            <!-- Background -->
            <SolidColorBrush x:Key="BackgroundPrimary"   Color="#FF0D0D1A"/>
            <SolidColorBrush x:Key="BackgroundSecondary" Color="#FF16162A"/>
            <SolidColorBrush x:Key="BackgroundTertiary"  Color="#FF1E1E35"/>
            <!-- ... autres tokens dark ... -->
        </ResourceDictionary>

        <ResourceDictionary x:Key="Light">
            <!-- Background -->
            <SolidColorBrush x:Key="BackgroundPrimary"   Color="#FFF3F3F3"/>
            <SolidColorBrush x:Key="BackgroundSecondary" Color="#FFFFFFFF"/>
            <SolidColorBrush x:Key="BackgroundTertiary"  Color="#FFEBEBEB"/>
            <!-- ... autres tokens light ... -->
        </ResourceDictionary>

    </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

> Pour la liste complète des valeurs, lire `tokens.json`.

---

## Conventions de nommage des ressources

| Catégorie token | Pattern de clé XAML | Exemple |
|---|---|---|
| `color.background.primary` | `Background{Pascal}` | `BackgroundPrimary` |
| `color.text.primary` | `Text{Pascal}` | `TextPrimary` |
| `color.border.primary` | `Border{Pascal}` | `BorderPrimary` |
| `color.accent.primary` | `Accent{Pascal}` | `AccentPrimary` |
| `color.status.success` | `Status{Pascal}` | `StatusSuccess` |
| `color.blueprint.tier.t1` | `Tier{T1}` | `TierT1` |
| `spacing.md` | `Spacing{Pascal}` | `SpacingMd` |
| `radius.lg` | `Radius{Pascal}` | `RadiusLg` |

---

## Références

- **`tokens.json`** — Valeurs de tous les tokens (couleurs ARGB dark/light, espacements, typographie)
- **`references/css-to-xaml-mapping.md`** — Mapping détaillé CSS → XAML, patterns FluentAvalonia, exemples de composants
