# Blueprint Modifier Types (SC 4.8) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gérer deux nouveaux types de modificateurs de stats dans les blueprints SC 4.8 : plages linéaires multiples (multiplicateur flottant) et plages additives entières à valeur fixe.

**Architecture:** On distingue les deux types via deux sous-classes de `BlueprintStatModelBase`. Le ViewModel extrait le bon type selon les ranges présents dans le DataCore. La vue XAML utilise deux `DataTemplate` distincts sélectionnés automatiquement par Avalonia selon le type runtime.

**Tech Stack:** C# 13 / .NET 10, Avalonia 11, CommunityToolkit.Mvvm, `StarBreaker.DataCoreGenerated` (DLL binaire)

---

## Fichiers concernés

| Fichier | Action |
|---|---|
| `StarXelem/ViewModels/BlueprintListTabViewModel.cs` | Modifier modèles + logique d'extraction |
| `StarXelem/Views/BlueprintListTabView.axaml` | Remplacer DataTemplate stat par deux templates spécialisés |

---

## Types DataCore confirmés par réflexion DLL

**`CraftingGameplayPropertyModifierValueRange_Linear`**
- `startQuality` : `int`, `endQuality` : `int`
- `modifierAtStart` : `float`, `modifierAtEnd` : `float`

**`CraftingGameplayPropertyModifierValueRange_LinearIntegerAdditive`**
- `startQuality` : `int`, `endQuality` : `int`
- `additiveModifierAtStart` : `int`, `additiveModifierAtEnd` : `int`

---

## Tâche 1 — Refactoriser les modèles de stats dans le ViewModel

**Fichier :** `StarXelem/ViewModels/BlueprintListTabViewModel.cs` (lignes 281–287 actuellement)

- [ ] **Étape 1 : Remplacer `BlueprintStatModel` par une hiérarchie de classes**

Localiser le bloc à la fin du fichier (après `BlueprintViewModel`) et remplacer :

```csharp
// AVANT (supprimer)
public class BlueprintStatModel
{
    public required string Name { get; set; }
    public required float Min { get; set; }
    public required float Max { get; set; }
}
```

Par :

```csharp
// APRÈS
public abstract class BlueprintStatModelBase
{
    public required string Name { get; set; }
}

public class BlueprintStatLinearModel : BlueprintStatModelBase
{
    public required float Min { get; set; }
    public required float Max { get; set; }
}

public class BlueprintStatBandModel
{
    public required int StartQuality { get; set; }
    public required int EndQuality { get; set; }
    public required int Value { get; set; }
    public string QualityLabel => $"{StartQuality}-{EndQuality}";
    public string FormattedValue => Value > 0 ? $"+{Value}" : Value.ToString();
}

public class BlueprintStatAdditiveModel : BlueprintStatModelBase
{
    public required List<BlueprintStatBandModel> Bands { get; set; }
}
```

- [ ] **Étape 2 : Mettre à jour `BlueprintCategoryModel.StatModifierList`**

Dans la classe `BlueprintCategoryModel` :

```csharp
// AVANT
public required List<BlueprintStatModel> StatModifierList { get; set; }

// APRÈS
public required List<BlueprintStatModelBase> StatModifierList { get; set; }
```

- [ ] **Étape 3 : Corriger la variable locale dans `LoadItemList`**

Dans la méthode `LoadItemList`, ligne ~132 :

```csharp
// AVANT
var statModifierList = new List<BlueprintStatModel>();

// APRÈS
var statModifierList = new List<BlueprintStatModelBase>();
```

- [ ] **Étape 4 : Builder pour vérifier les erreurs de compilation**

```powershell
dotnet build D:\repos\starcitizen\StarXelem\src\StarXelem\StarXelem.csproj
```

Résultat attendu : 0 erreur (il peut y avoir des avertissements).

> Si la build échoue, chercher toute référence restante à `BlueprintStatModel` (sans suffixe `Base`, `Linear`, ou `Additive`) et corriger.

- [ ] **Étape 5 : Commit**

```powershell
git add StarXelem/ViewModels/BlueprintListTabViewModel.cs
git commit -m "refactor(blueprint): BlueprintStatModel → BlueprintStatModelBase + sous-classes Linear/Additive"
```

---

## Tâche 2 — Mettre à jour la logique d'extraction des stats

**Fichier :** `StarXelem/ViewModels/BlueprintListTabViewModel.cs` (méthode `LoadItemList`, bloc foreach stat, lignes ~133–151)

- [ ] **Étape 1 : Remplacer le bloc d'extraction actuel**

Localiser ce bloc :

```csharp
var rrrr = (tttt as CraftingGameplayPropertyModifierCommon);

var propertyName = await _p4KService.GetLocaleValue(rrrr?.gameplayPropertyRecord?.propertyName);
statModifierList.Add(new BlueprintStatModel
{
    Name = propertyName ?? "Inconnu",
    Min = rrrr?.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_Linear>().FirstOrDefault()?.modifierAtStart ?? -1.0f,
    Max = rrrr?.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_Linear>().FirstOrDefault()?.modifierAtEnd ?? -1.0f
});
```

Et le remplacer par :

```csharp
var rrrr = (tttt as CraftingGameplayPropertyModifierCommon);

var propertyName = await _p4KService.GetLocaleValue(rrrr?.gameplayPropertyRecord?.propertyName);
var name = propertyName ?? "Inconnu";

var linearRanges = rrrr?.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_Linear>().ToList();
if (linearRanges is { Count: > 0 })
{
    statModifierList.Add(new BlueprintStatLinearModel
    {
        Name = name,
        Min = linearRanges[0].modifierAtStart,
        Max = linearRanges[^1].modifierAtEnd
    });
}
else
{
    var additiveRanges = rrrr?.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_LinearIntegerAdditive>().ToList();
    if (additiveRanges is { Count: > 0 })
    {
        statModifierList.Add(new BlueprintStatAdditiveModel
        {
            Name = name,
            Bands = additiveRanges.Select(r => new BlueprintStatBandModel
            {
                StartQuality = r.startQuality,
                EndQuality = r.endQuality,
                Value = r.additiveModifierAtStart
            }).ToList()
        });
    }
    else
    {
        _logger.LogWarning("Aucun range de modificateur reconnu pour la propriété {Name}", name);
    }
}
```

> **Note :** On utilise `additiveModifierAtStart` comme valeur de la bande. Chaque bande a `additiveModifierAtStart == additiveModifierAtEnd` dans les données actuelles (valeur fixe). Si cela change, il faudra créer une plage Min/Max additive distincte.

- [ ] **Étape 2 : Builder**

```powershell
dotnet build D:\repos\starcitizen\StarXelem\src\StarXelem\StarXelem.csproj
```

Résultat attendu : 0 erreur.

- [ ] **Étape 3 : Commit**

```powershell
git add StarXelem/ViewModels/BlueprintListTabViewModel.cs
git commit -m "feat(blueprint): extraction des modificateurs Linear multi-range et LinearIntegerAdditive"
```

---

## Tâche 3 — Mettre à jour la vue XAML

**Fichier :** `StarXelem/Views/BlueprintListTabView.axaml` (lignes ~121–134)

- [ ] **Étape 1 : Remplacer le DataTemplate unique par deux templates**

Localiser le bloc actuel :

```xml
<ItemsControl Grid.Row="1" Grid.Column="2" ItemsSource="{Binding StatModifierList}">
    <ItemsControl.DataTemplates>
        <DataTemplate DataType="{x:Type vm:BlueprintStatModel}">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Classes="demi-dimming" Text="{Binding Name}" />
                <Grid Grid.Column="1" ColumnDefinitions="Auto,25,Auto">
                    <TextBlock Grid.Column="0" Classes="demi-bold" Text="{Binding Min, Converter={StaticResource PercentageConverter}, ConverterParameter=4}" />
                    <Separator Grid.Column="1" Classes="horizontal" Margin="5 0" />
                    <TextBlock Grid.Column="2" Classes="demi-bold" Text="{Binding Max, Converter={StaticResource PercentageConverter}, ConverterParameter=4}" />
                </Grid>
            </Grid>
        </DataTemplate>
    </ItemsControl.DataTemplates>
</ItemsControl>
```

Et le remplacer par :

```xml
<ItemsControl Grid.Row="1" Grid.Column="2" ItemsSource="{Binding StatModifierList}">
    <ItemsControl.DataTemplates>

        <!-- Modificateur multiplicatif linéaire (ex: 80% → 120%) -->
        <DataTemplate DataType="{x:Type vm:BlueprintStatLinearModel}">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Classes="demi-dimming" Text="{Binding Name}" />
                <Grid Grid.Column="1" ColumnDefinitions="Auto,25,Auto">
                    <TextBlock Grid.Column="0" Classes="demi-bold" Text="{Binding Min, Converter={StaticResource PercentageConverter}, ConverterParameter=4}" />
                    <Separator Grid.Column="1" Classes="horizontal" Margin="5 0" />
                    <TextBlock Grid.Column="2" Classes="demi-bold" Text="{Binding Max, Converter={StaticResource PercentageConverter}, ConverterParameter=4}" />
                </Grid>
            </Grid>
        </DataTemplate>

        <!-- Modificateur additif entier par plages de qualité (ex: -2 | -1 | 0 | +1 | +2) -->
        <DataTemplate DataType="{x:Type vm:BlueprintStatAdditiveModel}">
            <Grid RowDefinitions="Auto,Auto" Margin="0 2 0 4">
                <TextBlock Grid.Row="0" Classes="demi-dimming" Text="{Binding Name}" />
                <ItemsControl Grid.Row="1" ItemsSource="{Binding Bands}" Margin="0 2 0 0">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" Spacing="6" />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.DataTemplates>
                        <DataTemplate DataType="{x:Type vm:BlueprintStatBandModel}">
                            <StackPanel Orientation="Vertical" Spacing="1">
                                <TextBlock Classes="demi-dimming" FontSize="10" Text="{Binding QualityLabel}" HorizontalAlignment="Center" />
                                <TextBlock Classes="demi-bold" Text="{Binding FormattedValue}" HorizontalAlignment="Center" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.DataTemplates>
                </ItemsControl>
            </Grid>
        </DataTemplate>

    </ItemsControl.DataTemplates>
</ItemsControl>
```

Résultat attendu visuellement pour un modificateur additif :

```
Power generation
Q0-249   Q250-499   Q500-699   Q700-899   Q900-1000
  -2        -1          0         +1          +2
```

> **Note design :** FontSize="10" est la taille minimale autorisée par la convention StarXelem. Les classes `demi-dimming` et `demi-bold` suivent la hiérarchie typographique définie dans `CLAUDE_design_convention.md` section 12.

- [ ] **Étape 2 : Builder**

```powershell
dotnet build D:\repos\starcitizen\StarXelem\src\StarXelem\StarXelem.csproj
```

Résultat attendu : 0 erreur.

- [ ] **Étape 3 : Commit**

```powershell
git add StarXelem/Views/BlueprintListTabView.axaml
git commit -m "feat(blueprint): affichage des modificateurs additifs par plages de qualité"
```

---

## Auto-vérification du plan

**Couverture spec :**
- ✅ Plages linéaires multiples (type `CraftingGameplayPropertyModifierValueRange_Linear`, Min = premier range start, Max = dernier range end) → Tâche 2, Étape 1
- ✅ Plages additives entières fixes (`CraftingGameplayPropertyModifierValueRange_LinearIntegerAdditive`) → Tâche 2, Étape 1
- ✅ Affichage vue adapté pour les deux types → Tâche 3
- ✅ Respect du système de design StarXelem (taille minimale 10px, classes existantes) → Tâche 3, Étape 1

**Pas de placeholder :** Toutes les étapes contiennent le code complet.

**Cohérence des types :**
- `BlueprintStatModelBase` défini en Tâche 1, utilisé comme type de `StatModifierList` en Tâche 1
- `BlueprintStatLinearModel` défini en Tâche 1, référencé dans l'extraction (Tâche 2) et le XAML (Tâche 3)
- `BlueprintStatAdditiveModel` / `BlueprintStatBandModel` définis en Tâche 1, référencés en Tâches 2 et 3
- `QualityLabel` et `FormattedValue` définis dans `BlueprintStatBandModel` (Tâche 1), bindés dans le XAML (Tâche 3)
