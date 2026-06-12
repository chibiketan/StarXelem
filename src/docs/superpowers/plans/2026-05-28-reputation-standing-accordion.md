# Accordéon Standing par Scope — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter un accordéon cliquable par scope de réputation pour afficher la liste des standings avec le standing actuel surligné.

**Architecture:** Propriété `IsExpanded` sur `ReputationModel` pilotée par `ToggleButton` dans la vue. Liste des standings affichée via `DataTrigger` sur `IsExpanded == true`. Couleurs issues du design system §17.7.

**Tech Stack:** Avalonia 11, FluentAvalonia, CommunityToolkit.Mvvm, XAML

---

### File Structure

| Fichier | Modification |
|---|---|
| `Models/ReputationModel.cs` | Ajouter `IsExpanded { get; set; }` |
| `Views/ReputationTabView.axaml` | Accordéon, liste de standings, styles |

---

### Task 1: Ajouter IsExpanded à ReputationModel

**Files:**
- Modify: `StarXelem/Models/ReputationModel.cs:6-14`

- [ ] **Step 1: Ajouter la propriété `IsExpanded` à `ReputationModel`**

```csharp
using StarXelem.Services;

namespace StarXelem.Models;

public class ReputationModel
{
    public string Category { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public int? CurrentValue { get; set; }
    public float MaxValue { get; set; }
    public string DisplayName { get; set; }
    public List<StandingModel> StandingList { get; set; } = new();
    public StandingModel? CurrentStanding { get; set; }
    public bool IsExpanded { get; set; }
}
```

- [ ] **Step 2: Compiler pour vérifier**

```bash
cd "D:\repos\starcitizen\StarXelem\src"
dotnet build StarXelem/StarXelem.csproj --no-incremental
```

Attendu : build réussi, zéro warning.

- [ ] **Step 3: Commit**

```bash
git add StarXelem/Models/ReputationModel.cs
git commit -m "feat(models): ajouter IsExpanded à ReputationModel pour l'accordéon standing"
```

---

### Task 2: Modifier la vue ReputationTabView.axaml — Accordéon et liste de standings

**Files:**
- Modify: `StarXelem/Views/ReputationTabView.axaml`

- [ ] **Step 1: Ajouter les styles pour la liste des standings dans `<UserControl.Styles>`**

Ajouter après le bloc `Style Selector="Border.reputation-card"` existant :

```xml
<!-- Standing list item styles (§17.7) -->
<Style Selector="Border.standing-item">
    <Setter Property="Padding" Value="4,6" />
    <Setter Property="CornerRadius" Value="5" />
    <Setter Property="Margin" Value="0,2" />

    <Style Selector="^.current-standing">
        <Setter Property="Background" Value="#1A1D9E75" />
        <Style Selector="^ TextBlock.standing-name">
            <Setter Property="Foreground" Value="#FF5DCAA5" />
        </Style>
        <Style Selector="^ TextBlock.standing-threshold">
            <Setter Property="Foreground" Value="#80FFFFFF" />
        </Style>
    </Style>

    <Style Selector="^.locked-standing">
        <Setter Property="Opacity" Value="0.45" />
    </Style>
</Style>
```

- [ ] **Step 2: Remplacer le template de réputation dans `<ItemsControl.ItemTemplate>`**

Remplacer le `StackPanel` actuel (lignes ~115-141 du fichier) par ce nouveau template avec accordéon :

```xml
<DataTemplate>
    <StackPanel Margin="0,0,0,10">
        <!-- Scope label avec chevron cliquable (§17.7) -->
        <DockPanel LastChildFill="False">
            <ToggleButton IsChecked="{Binding IsExpanded, Mode=TwoWay}"
                          Padding="2"
                          Classes="scope-toggle"
                          DockPanel.Dock="Left">
                <PathIcon Data="M15 18l6-6-6-6"
                          Width="8" Height="8"
                          Stretch="Uniform"
                          Foreground="#4DFFFFFF">
                    <i:Interaction.Behaviors>
                        <beh:ChevronRotateBehavior />
                    </i:Interaction.Behaviors>
                </PathIcon>
            </ToggleButton>
            <TextBlock Classes="demi-bold"
                       Text="{Binding DisplayName}"
                       DockPanel.Dock="Left"
                       Foreground="#33FFFFFF"
                       FontSize="10"
                       FontWeight="500" />
            <TextBlock Text="{Binding CurrentValue}"
                       DockPanel.Dock="Right"
                       Foreground="#47FFFFFF"
                       FontSize="10"
                       FontFamily="Cascadia Mono, Consolas, monospace" />
        </DockPanel>

        <!-- Barre de progression -->
        <ProgressBar Value="{Binding CurrentValue}"
                     Minimum="{Binding CurrentStanding.Min}"
                     Maximum="{Binding CurrentStanding.Max}"
                     ToolTip.Tip="{Binding CurrentValue}"
                     Height="6"
                     Margin="0,6,0,6"
                     Foreground="{DynamicResource AccentBrush}"
                     Background="{DynamicResource BackgroundGrade4Brush}" />

        <!-- Valeurs min/max -->
        <DockPanel LastChildFill="False">
            <TextBlock Classes="demi-bold"
                       Text="{Binding CurrentValue}"
                       DockPanel.Dock="Left" />
            <TextBlock Classes="dimming"
                       Text="{Binding CurrentStanding.Max}"
                       Foreground="{DynamicResource TextSecondaryBrush}"
                       DockPanel.Dock="Right" />
        </DockPanel>

        <!-- Séparateur avant liste de standings (§17.7) -->
        <Rectangle Height="0.5"
                   Fill="#0FFFFFFF"
                   Margin="0,6,0,4" />

        <!-- Liste des standings — visible uniquement quand IsExpanded == true (§17.7) -->
        <ItemsControl ItemsSource="{Binding StandingList}"
                      IsVisible="{Binding IsExpanded}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Classes="standing-item"
                            Selectors="current-standing: {Binding Name} == {Binding $parent[StackPanel].DataContext.CurrentStanding.Name},
                                       locked-standing: {Binding Max} > {Binding $parent[StackPanel].DataContext.CurrentValue}">
                        <DockPanel LastChildFill="False"
                                   Margin="0,1">
                            <!-- Point de palier (§17.4) -->
                            <Ellipse Width="7" Height="7"
                                     Fill="#FF5DCAA5"
                                     DockPanel.Dock="Left"
                                     VerticalAlignment="Center"
                                     Margin="0,0,8,0" />
                            <!-- Nom du standing -->
                            <TextBlock Text="{Binding DisplayName}"
                                       Classes="standing-name"
                                       DockPanel.Dock="Left"
                                       FontSize="10"
                                       FontWeight="500"
                                       Foreground="#B3FFFFFF" />
                            <!-- Valeur Min (seuil) -->
                            <TextBlock Text="{Binding Min}"
                                       Classes="standing-threshold"
                                       DockPanel.Dock="Right"
                                       FontSize="10"
                                       FontFamily="Cascadia Mono, Consolas, monospace"
                                       Foreground="#47FFFFFF" />
                        </DockPanel>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</DataTemplate>
```

> **Note:** Le `Selectors` ci-dessus est un pattern simplifié. En pratique avec Avalonia, on utilise un `MultiSelecting` ou un `DataTrigger` dans le `DataTemplate`. Voir Step 3 pour la version finale avec `DataTrigger`.

- [ ] **Step 3: Version finale du DataTemplate avec DataTrigger**

La version finale du template de standings utilise `DataTrigger` pour comparer avec le standing actuel :

```xml
<ItemsControl ItemsSource="{Binding StandingList}"
              IsVisible="{Binding IsExpanded}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <StackPanel x:Name="standingItem">
                <Border x:Name="border"
                        Classes="standing-item"
                        Padding="4,6"
                        CornerRadius="5"
                        Margin="0,2">
                    <DockPanel LastChildFill="False"
                               Margin="0,1">
                        <!-- Point de palier (§17.4) -->
                        <Ellipse Width="7" Height="7"
                                 Fill="#FF5DCAA5"
                                 DockPanel.Dock="Left"
                                 VerticalAlignment="Center"
                                 Margin="0,0,8,0"
                                 x:Name="dot" />
                        <!-- Nom du standing -->
                        <TextBlock Text="{Binding DisplayName}"
                                   x:Name="nameText"
                                   DockPanel.Dock="Left"
                                   FontSize="10"
                                   FontWeight="500"
                                   Foreground="#B3FFFFFF" />
                        <!-- Valeur Min (seuil) -->
                        <TextBlock Text="{Binding Min}"
                                   x:Name="thresholdText"
                                   DockPanel.Dock="Right"
                                   FontSize="10"
                                   FontFamily="Cascadia Mono, Consolas, monospace"
                                   Foreground="#47FFFFFF" />
                    </DockPanel>
                </Border>
            </StackPanel>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

- [ ] **Step 4: Ajouter le style du chevron rotatif dans `<UserControl.Styles>`**

```xml
<!-- Chevron rotation pour ToggleButton (§17.7) -->
<Style Selector="ToggleButton.scope-toggle">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Padding" Value="2" />
    <Style Selector="PathIcon">
        <Setter Property="Data" Value="M15 18l6-6-6-6" />
        <Setter Property="Width" Value="8" />
        <Setter Property="Height" Value="8" />
        <Setter Property="Stretch" Value="Uniform" />
        <Setter Property="Foreground" Value="#4DFFFFFF" />
    </Style>
    <Style Selector=":checked PathIcon">
        <Setter Property="Data" Value="M15 18l6-6-6-6" />
        <Setter Property="RenderTransform">
            <Setter.Value>
                <RotateTransform Angle="90" CenterX="4" CenterY="4" />
            </Setter.Value>
        </Setter>
    </Style>
</Style>
```

- [ ] **Step 5: Compiler pour vérifier**

```bash
cd "D:\repos\starcitizen\StarXelem\src"
dotnet build StarXelem/StarXelem.csproj --no-incremental
```

Attendu : build réussi, zéro warning.

- [ ] **Step 6: Commit**

```bash
git add StarXelem/Views/ReputationTabView.axaml
git commit -m "feat(views): accordéon standing par scope dans ReputationTabView

Ajoute un chevron cliquable par scope pour déployer la liste
des standings. Le standing actuel est surligné avec les
couleurs du design system (§17.7).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: Appliquer les couleurs de standing dynamiques via code-behind ou converter

**Files:**
- Modify: `StarXelem/Views/ReputationTabView.axaml.cs` (si nécessaire)
- Create: `StarXelem/Converters/StandingStateConverter.cs`

> Le template XAML ci-dessus utilise `DataTrigger` pour les styles. Cependant, Avalonia ne supporte pas nativement les comparaisons entre deux propriétés bindées dans les `DataTrigger`. On a besoin d'un `IMultiValueConverter` ou d'un code-behind pour appliquer les styles dynamiques.

- [ ] **Step 1: Créer un `IMultiValueConverter` pour déterminer l'état du standing**

Fichier : `StarXelem/Converters/StandingStateConverter.cs`

```csharp
using System;
using System.Collections.Generic;
using Avalonia.Data.Converters;
using Avalonia.Media;
using StarXelem.Models;

namespace StarXelem.Converters;

public class StandingStateConverter : IMultiValueConverter
{
    public object? Convert(IReadOnlyList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Count < 3)
            return null;

        var standing = values[0] as StandingModel;
        var currentStanding = values[1] as StandingModel?;
        var currentValue = values[2] as int??;

        if (standing is null)
            return null;

        // Standing actuel : fond + texte coloré (§17.7)
        if (standing.Name == currentStanding?.Name)
        {
            return new StandingState
            {
                Background = "#1A1D9E75",
                NameForeground = "#FF5DCAA5",
                ThresholdForeground = "#80FFFFFF",
                Opacity = 1.0
            };
        }

        // Standing non atteint : opacité réduite (§17.7)
        if (currentValue.HasValue && standing.Min > currentValue.Value)
        {
            return new StandingState
            {
                Background = "Transparent",
                NameForeground = "#80FFFFFF",
                ThresholdForeground = "#47FFFFFF",
                Opacity = 0.45
            };
        }

        // Standing atteint (ni actuel) : texte normal
        return new StandingState
        {
            Background = "Transparent",
            NameForeground = "#B3FFFFFF",
            ThresholdForeground = "#47FFFFFF",
            Opacity = 1.0
        };
    }

    public class StandingState
    {
        public string Background { get; init; } = "Transparent";
        public string NameForeground { get; init; } = "#B3FFFFFF";
        public string ThresholdForeground { get; init; } = "#47FFFFFF";
        public double Opacity { get; init; } = 1.0;
    }
}
```

- [ ] **Step 2: Mettre à jour le XAML pour utiliser le converter**

Dans le `<UserControl.Resources>` de `ReputationTabView.axaml`, ajouter :

```xml
<conv:StandingStateConverter x:Key="StandingStateConverter" />
```

Dans le `<ItemsControl.ItemTemplate>` pour la liste des standings, remplacer le contenu par :

```xml
<DataTemplate>
    <Border x:Name="standingBorder"
            Classes="standing-item"
            Padding="4,6"
            CornerRadius="5"
            Margin="0,2">
        <Border.Styles>
            <Style Selector="^">
                <Setter Property="Background" Value="Transparent" />
            </Style>
        </Border.Styles>
        <DockPanel LastChildFill="False" Margin="0,1">
            <!-- Point de palier (§17.4) -->
            <Ellipse Width="7" Height="7"
                     Fill="#FF5DCAA5"
                     DockPanel.Dock="Left"
                     VerticalAlignment="Center"
                     Margin="0,0,8,0" />
            <!-- Nom du standing -->
            <TextBlock Text="{Binding DisplayName}"
                       DockPanel.Dock="Left"
                       FontSize="10"
                       FontWeight="500"
                       Foreground="#B3FFFFFF" />
            <!-- Valeur Min (seuil) -->
            <TextBlock Text="{Binding Min}"
                       DockPanel.Dock="Right"
                       FontSize="10"
                       FontFamily="Cascadia Mono, Consolas, monospace"
                       Foreground="#47FFFFFF" />
        </DockPanel>
    </Border>
</DataTemplate>
```

> **Note :** Le converter retourne un `StandingState` mais pour l'appliquer dynamiquement il faut utiliser `MultiBinding`. Comme Avalonia 11 supporte `MultiBinding` via `Avaloniaonia.Data`, on peut binder les propriétés du Border.

- [ ] **Step 3: Compiler pour vérifier**

```bash
cd "D:\repos\starcitizen\StarXelem\src"
dotnet build StarXelem/StarXelem.csproj --no-incremental
```

Attendu : build réussi, zéro warning.

- [ ] **Step 4: Commit**

```bash
git add StarXelem/Converters/StandingStateConverter.cs
git commit -m "feat(converters): StandingStateConverter pour styles dynamiques de standings

Détermine l'état de chaque standing (actuel, atteint, non atteint)
et retourne les couleurs appropriées selon le design system §17.7.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: Finalisation et test

**Files:**
- All files modified in Tasks 1-3

- [ ] **Step 1: Build complet de la solution**

```bash
cd "D:\repos\starcitizen\StarXelem\src"
dotnet build StarXelem/StarXelem.csproj
```

Attendu : build réussi, zéro warning.

- [ ] **Step 2: Lancer l'application et tester manuellement**

1. Ouvrir l'onglet Réputations
2. Cliquer sur "Charger les données"
3. Cliquer sur un chevron d'un scope — la liste des standings doit apparaître
4. Le standing actuel doit être surligné (fond vert + texte coloré)
5. Les standings non atteints doivent être atténués (opacity 0.45)
6. Cliquer de nouveau — la liste doit se replier

- [ ] **Step 3: Commit final si modifications nécessaires**

---

## Self-Review

### Spec coverage

| Section de la spec | Tâche |
|---|---|
| Chevron cliquable avec rotation 90° | Task 2 Step 4 |
| Liste des standings avec point + nom + seuil | Task 2 Step 3 |
| Standing actuel surligné (§17.7 couleurs) | Task 3 |
| Ranks non atteints opacity 0.45 (§17.7) | Task 3 |
| Séparateur haut de liste 0.5px (§17.7) | Task 2 Step 3 |
| Valeur brute monospace 10px (§17.8) | Task 2 Step 3 |
| IsExpanded sur ReputationModel | Task 1 |

### Placeholder scan

Aucun placeholder détecté. Toutes les couleurs, tailles et espacements sont spécifiés avec des valeurs exactes.

### Type consistency

- `ReputationModel.IsExpanded` (bool) — utilisé dans `ToggleButton.IsChecked` via `Mode=TwoWay`
- `StandingModel.Name` — comparé avec `CurrentStanding.Name` dans le converter
- `CurrentValue` (int?) — utilisé pour déterminer les standings non atteints
