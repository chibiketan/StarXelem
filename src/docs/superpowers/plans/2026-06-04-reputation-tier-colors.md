# Reputation — Couleurs tier pour ProgressBar et pastilles

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Appliquer les couleurs tier (P1-P7) du design system §17.4 à la ProgressBar principale et aux pastilles (Ellipse) de chaque standing dans l'accordéon Reputation.

**Architecture:** Ajouter la propriété `Tier` à `StandingModel`, la calculer dans `ReputationService` selon l'index du standing dans StandingList. Créer un converter `TierToFillBrushConverter` qui mappe le tier vers `RepTier{N}FillBrush`. Utiliser ces liaisons dans le XAML pour remplacer les couleurs en dur.

**Tech Stack:** Avalonia 11, FluentAvalonia, CommunityToolkit.Mvvm, XAML

---

### Contexte technique

**Fichier de ressources :** `Style/DefaultResources.axaml` — contient déjà `RepTier1FillBrush` à `RepTier7FillBrush` définis pour les deux thèmes (Light lignes 147-153, Dark lignes 321-327).

**Mapping index → tier (design system §17.4) :**
- Index 0 → P1 (Not Eligible)
- Index 1 → P2 (Applicant)
- Index 2 → P3 (Trainee)
- Index 3 → P4 (Jr. Rank)
- Index 4 → P5 (Rank)
- Index 5 → P6 (Sr. Rank)
- Index 6+ → P7 (Master)

**StandingModel actuel** (dans `Services/ReputationService.cs` lignes 152-158) :
```csharp
public class StandingModel
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public long Min { get; set; }
    public long Max { get; set; }
}
```

**Code actuel de la ProgressBar** (lignes 235-241 de `ReputationTabView.axaml`) :
```xml
<ProgressBar ... Foreground="{DynamicResource AccentBrush}" ... />
```

**Code actuel de la pastille** (lignes 274-278 de `ReputationTabView.axaml`) :
```xml
<Ellipse Width="7" Height="7" Fill="#FF5DCAA5" ... />
```

---

### Task 1: Ajouter la propriété Tier à StandingModel

**Files:**
- Modify: `Services/ReputationService.cs:152-158`

- [ ] **Step 1: Ajouter la propriété Tier à StandingModel**

```csharp
public class StandingModel
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public long Min { get; set; }
    public long Max { get; set; }
    /// <summary>
    /// Palier (1-7) correspondant au design system §17.4. Index 0 → P1, ..., 6+ → P7.
    /// </summary>
    public int Tier { get; set; }
}
```

- [ ] **Step 2: Calculer le tier lors du peuplement de StandingList dans ReputationService**

Dans `ReputationService.cs`, trouver la boucle qui peuplre StandingList (autour de la ligne 76-90). Ajouter le calcul du tier en utilisant l'index dans la boucle `foreach`. Il faut convertir le foreach en une boucle indexée pour avoir accès au numéro d'index.

Actuellement le code fait :
```csharp
foreach (var standing in scopeContext.scope.standingMap.standings)
{
    if (standing is null)
    {
        _logger.LogWarning("Null standing found for faction reputation {RecordId}", dataCoreTypedRecord.RecordId);
        continue;
    }

    scope.StandingList.Add(new StandingModel
    {
        Name = standing.name,
        DisplayName = await _p4kService.GetLocaleValue(standing.displayName),
        Min = standing.minReputation
    });
}
```

Le remplacer par :
```csharp
int standingIndex = 0;
foreach (var standing in scopeContext.scope.standingMap.standings)
{
    if (standing is null)
    {
        _logger.LogWarning("Null standing found for faction reputation {RecordId}", dataCoreTypedRecord.RecordId);
        continue;
    }

    scope.StandingList.Add(new StandingModel
    {
        Name = standing.name,
        DisplayName = await _p4kService.GetLocaleValue(standing.displayName),
        Min = standing.minReputation,
        Tier = Math.Min(standingIndex + 1, 7)
    });

    standingIndex++;
}
```

Le `Math.Min(standingIndex + 1, 7)` garantit que les standings au-delà du 7e palier (P7 Master) se voient attribuer tier 7.

- [ ] **Step 3: Vérifier le build**

Build de `StarXelem` — s'assurer qu'il n'y a pas d'erreurs.

```bash
dotnet build StarXelem
```

- [ ] **Step 4: Commit**

```bash
git add StarXelem/Services/ReputationService.cs
git commit -m "feat(models): ajouter la propriété Tier à StandingModel et la calculer"
```

---

### Task 2: Créer le converter TierToFillBrushConverter

**Files:**
- Create: `Converters/TierToFillBrushConverter.cs`

- [ ] **Step 1: Créer le converter**

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace StarXelem.Converters;

/// <summary>
/// Convertit un numéro de tier (1-7) en le brush ResourceKey correspondant (§17.4).
/// Retourne le brush RepTier{N}FillBrush depuis les ressources actuelles.
/// Valeur d'entrée attendue : int (tier 1-7)
/// </summary>
public class TierToFillBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int tier)
            return null;

        tier = Math.Max(1, Math.Min(7, tier));

        string resourceName = $"RepTier{tier}FillBrush";

        return Application.Current?.FindResource(new ResourceKey(resourceName));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
```

- [ ] **Step 2: Vérifier le build**

```bash
dotnet build StarXelem
```

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Converters/TierToFillBrushConverter.cs
git commit -m "feat(converters): ajouter TierToFillBrushConverter pour mapper tier → RepTier{N}FillBrush"
```

---

### Task 3: Appliquer le converter à la ProgressBar

**Files:**
- Modify: `Views/ReputationTabView.axaml`

- [ ] **Step 1: Enregistrer le converter dans les ressources du UserControl**

Dans `ReputationTabView.axaml`, ajouter le converter au `ResourceDictionary` :

```xml
<conv:TierToFillBrushConverter x:Key="TierToFillBrushConverter" />
```

- [ ] **Step 2: Remplacer AccentBrush par la liaison dynamique dans la ProgressBar**

Dans l'Expander Header, remplacer :

```xml
<ProgressBar Value="{Binding CurrentValue}"
             Minimum="{Binding CurrentStanding.Min}"
             Maximum="{Binding CurrentStanding.Max}"
             ToolTip.Tip="{Binding CurrentValue}"
             Height="6"
             Foreground="{Binding CurrentStanding.Tier, Converter={StaticResource TierToFillBrushConverter}}"
             Background="{DynamicResource BackgroundGrade4Brush}" />
```

Le `Foreground` utilise maintenant le tier du standing courant (`CurrentStanding.Tier`) converti en brush via `TierToFillBrushConverter`.

- [ ] **Step 3: Vérifier le build**

```bash
dotnet build StarXelem
```

- [ ] **Step 4: Commit**

```bash
git add StarXelem/Views/ReputationTabView.axaml
git commit -m "fix(views): ProgressBar Reputation utilise les couleurs tier via TierToFillBrushConverter"
```

---

### Task 4: Appliquer le converter aux pastilles (Ellipse) des standings

**Files:**
- Modify: `Views/ReputationTabView.axaml`

- [ ] **Step 1: Remplacer la couleur en dur par la liaison dynamique dans chaque Ellipse de StandingList**

Dans l'ItemsControl de StandingList, remplacer :

```xml
<Ellipse Width="7" Height="7"
         Fill="#FF5DCAA5"
         DockPanel.Dock="Left"
         VerticalAlignment="Center"
         Margin="0,0,8,0" />
```

Par :

```xml
<Ellipse Width="7" Height="7"
         Fill="{Binding Tier, Converter={StaticResource TierToFillBrushConverter}}"
         DockPanel.Dock="Left"
         VerticalAlignment="Center"
         Margin="0,0,8,0" />
```

Chaque Ellipse se lie maintenant à `Tier` du StandingModel correspondant (le DataContext dans le DataTemplate est le StandingModel).

- [ ] **Step 2: Vérifier le build**

```bash
dotnet build StarXelem
```

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Views/ReputationTabView.axaml
git commit -m "fix(views): pastilles StandingList utilisent les couleurs tier selon design system §17.4"
```

---

### Task 5: Mettre à jour les données Design (optionnel)

**Files:**
- Modify: `DesignData.cs`

- [ ] **Step 1: Vérifier que les StandingList dans DesignData ont les bons tiers**

Les StandingList créées manuellement dans `DesignData.cs` n'ont pas de propriété Tier définie (valeur par défaut = 0). Pour que le design time affiche correctement les couleurs, il faut les mettre à jour.

Dans chaque `StandingList` de `DesignData.cs`, ajouter la valeur `Tier` correspondant à la position :
- Standing à l'index 0 → `Tier = 1`
- Standing à l'index 1 → `Tier = 2`
- etc.

Exemple pour Covalex ShipCombat :

```csharp
StandingList = new List<StandingModel>
{
    new() { Name = "ShipCombat_NotEligible",    DisplayName = "Not Eligible",      Min = -1000, Max = -1,   Tier = 1 },
    new() { Name = "ShipCombat_Rank0",          DisplayName = "Recruit",           Min = 0,     Max = 99,   Tier = 2 },
    new() { Name = "ShipCombat_Rank1",          DisplayName = "Novice",            Min = 100,   Max = 499,  Tier = 3 },
    new() { Name = "ShipCombat_Rank2",          DisplayName = "Apprentice",        Min = 500,   Max = 999,  Tier = 4 },
    new() { Name = "ShipCombat_Rank3",          DisplayName = "Adept",             Min = 1000,  Max = 4999,   Tier = 5 },
    new() { Name = "ShipCombat_Rank4",          DisplayName = "Proficient",        Min = 5000,  Max = 119999, Tier = 6 },
    new() { Name = "ShipCombat_Rank5",          DisplayName = "Veteran",           Min = 120000, Max = 479999, Tier = 7 },
    new() { Name = "ShipCombat_Rank6",          DisplayName = "Master",            Min = 480000, Max = 1000, Tier = 7 },
}
```

Appliquer la même logique à **toutes** les StandingList dans DesignData (il y en a une par scope de réputation).

- [ ] **Step 2: Vérifier le build**

```bash
dotnet build StarXelem
```

- [ ] **Step 3: Commit**

```bash
git add StarXelem/DesignData.cs
git commit -m "fix(data): ajouter les valeurs Tier aux StandingList de DesignData pour le design time"
```

---

## Self-Review

**1. Couverture du spec :**
- ✅ ProgressBar colore selon tier du CurrentStanding → Task 3
- ✅ Pastilles coloreées selon tier de chaque standing → Task 4
- ✅ Ressources RepTier1-7FillBrush déjà définies, utilisées via converter
- ✅ Les deux thèmes (Light/Dark) sont couverts car les StaticResource sont définis pour chacun

**2. Placeholder scan :**
- ✅ Pas de TBD, pas de TODO, pas de "ajouter un traitement d'erreur approprié"
- ✅ Tout le code est complet

**3. Cohérence des types :**
- ✅ `Tier` est un `int` dans StandingModel, le converter attend un `int`
- ✅ Le converter retourne le résultat de `FindResource` qui est un `SolidColorBrush` — compatible avec `Fill` (Ellipse) et `Foreground` (ProgressBar)

---
