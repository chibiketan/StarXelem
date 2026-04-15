---
name: starxelem-csharp-conventions
description: >
  Applies C# coding conventions to any code generation, review,
  refactoring, or explanation task. Use this skill whenever writing, reviewing, or discussing
  code — including new classes, ViewModels, services, Avalonia
  UI code-behind, XAML-related C# logic, API client code, or any other .NET/C# file in the
  project. Trigger on phrases like "écris", "modifie", "génère", "refactor", "review", "corrige",
  "ajoute une méthode", "crée une classe", "comment nommer", or any C# code snippet
  shared by the user. Even partial code snippets or architecture questions should trigger
  this skill to enforce consistent naming and structure.
user-invocable: false
---

# StarXelem — Conventions de code C#

## Contexte du projet

StarXelem est une application desktop Windows construite avec **.NET / Avalonia UI 11.3.11**
et la bibliothèque **FluentAvalonia**. Elle interroge les APIs Star Citizen pour récupérer
des données de blueprints (plans de construction). Le langage principal est **C#**.

---

## 1. Nommage

### 1.1 Règles générales

| Élément                        | Convention         | Exemple                          |
|--------------------------------|--------------------|----------------------------------|
| Classes                        | PascalCase         | `BlueprintService`               |
| Interfaces                     | IPascalCase        | `IBlueprintRepository`           |
| Méthodes                       | PascalCase         | `GetBlueprintAsync()`            |
| Propriétés publiques           | PascalCase         | `BlueprintName`                  |
| Champs privés                  | _camelCase         | `_blueprintCache`                |
| Variables locales              | camelCase          | `blueprintList`                  |
| Paramètres                     | camelCase          | `blueprintId`                    |
| Constantes                     | PascalCase         | `MaxSCUCapacity`                 |
| Enums (type)                   | PascalCase         | `EquipmentSubtype`               |
| Enums (valeurs)                | PascalCase         | `HeavyWeapon`, `Helmet`          |
| Events                         | PascalCase         | `BlueprintLoaded`                |
| Delegates                      | PascalCase + Handler | `BlueprintLoadedHandler`       |
| Types génériques               | T ou TXxx          | `TResult`, `TBlueprint`          |

### 1.2 Nommage spécifique au domaine Star Citizen

- Les abréviations du jeu sont conservées en majuscules : `SCU`, `UEC`, `ASOP`
- Les tiers de blueprint s'écrivent `T1`, `T2`, `T3` (jamais `Tier1`)

### 1.3 Nommage MVVM (Avalonia)

| Élément                        | Convention                         | Exemple                               |
|--------------------------------|------------------------------------|---------------------------------------|
| ViewModel                      | `{Nom}ViewModel`                   | `BlueprintDetailViewModel`            |
| View (code-behind)             | `{Nom}View`                        | `BlueprintDetailView`                 |
| Commandes                      | `{Verbe}{Nom}Command`              | `LoadBlueprintCommand`                |
| Observable properties          | PascalCase avec backing field      | `SelectedBlueprint` / `_selectedBlueprint` |
| Collections observables        | `{Nom}Collection` ou `{Nom}List`   | `BlueprintCollection`                 |

---

## 2. Structure des fichiers et namespaces

### 2.1 Organisation des dossiers

```
StarXelem/
├── Assets/          # Images utilisées dans l'application
├── Comparer/        # Comparateurs utilisés pour le tri
├── Components/      # Composants personnalisés pour le projet
├── Converters/      # Convertisseurs utilisés par les Bindings pour l'affichage
├── datafiles/       # Les fichiers du jeu au format XML (inutilisé par le code, uniquement pour comprendre les données du jeu)
├── Design/          # Inutilisé
├── Extensions/      # Méthodes d'extension
├── Models/          # Modèles de données (blueprints, modifiers, etc.)
├── Services/        # Logique métier, appels API
├── Style/           # Contient les ressources et les styles XAML
├── ViewModels/      # ViewModels MVVM
└── Views/           # Vues Avalonia (.axaml + .axaml.cs)
```

### 2.2 Namespaces

Le namespace suit la structure des dossiers :

```csharp
// Correct
namespace StarXelem.ViewModels;
namespace StarXelem.Services;
namespace StarXelem.Models;

// Incorrect
namespace StarXelem.ViewModels.Blueprints; // trop profond sauf si justifié
```

Utiliser les **file-scoped namespaces** (C# 10+) :

```csharp
// ✅ Correct
namespace StarXelem.Services;

public class BlueprintService { }

// ❌ Incorrect
namespace StarXelem.Services
{
    public class BlueprintService { }
}
```

---

## 3. Indentation et formatage

### 3.1 Règles de base

- **Indentation** : 4 espaces (jamais de tabulations)
- **Longueur de ligne** : max 120 caractères
- **Encoding** : UTF-8 sans BOM (standard Rider)
- **Fin de ligne** : CRLF (Windows)
- **Une classe par fichier** — toujours

### 3.2 Accolades

Toujours sur une nouvelle ligne (style Allman) :

```csharp
// ✅ Correct
public void LoadBlueprint(string id)
{
    if (string.IsNullOrEmpty(id))
    {
        return;
    }
}

// ❌ Incorrect
public void LoadBlueprint(string id) {
    if (string.IsNullOrEmpty(id)) {
        return;
    }
}
```

Exception acceptée pour les propriétés auto-implémentées et les lambdas courtes :

```csharp
public string Name { get; set; }
var names = blueprints.Select(b => b.Name).ToList();
```

### 3.3 Lignes vides

- Pas de ligne vide entre les champs d'une classe
- 1 ligne vide pour séparer les champs des méthodes d'une classe
- 1 ligne vide entre les méthodes d'une classe
- 2 lignes vides entre les classes dans un même fichier (éviter)
- Pas de ligne vide après l'accolade ouvrante ou avant la fermeture

### 3.4 Usings

- En haut du fichier, avant le namespace
- Regrouper : `System` d'abord, puis bibliothèques tierces, puis `StarXelem.*`
- Supprimer les usings inutilisés

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

using StarXelem.Models;
using StarXelem.Services;
```

---

## 4. Conventions de code

### 4.1 Modificateurs d'accès

- Toujours explicite — ne jamais omettre
- Ordre : `public / private / protected / internal / static / readonly / override / async`

```csharp
// ✅ Correct
private readonly IBlueprintService _blueprintService;
public static readonly int MaxResults = 100;

// ❌ Incorrect
readonly IBlueprintService _blueprintService;
```

### 4.2 `var` vs type explicite

- Utiliser `var` quand le type est évident à droite
- Type explicite quand le type est ambigu ou important pour la lisibilité

```csharp
// ✅ var acceptable
var blueprint = new Blueprint();
var blueprints = await _service.GetAllAsync();

// ✅ type explicite préféré
IEnumerable<Blueprint> results = _repository.Query(filter);
```

### 4.3 Async / Await

- Toute méthode asynchrone se termine par `Async`
- Toujours `await` — jamais `.Result` ou `.Wait()`
- `ConfigureAwait(false)` dans les services

```csharp
// ✅ Correct
public async Task<Blueprint> GetBlueprintAsync(string id)
{
    var result = await _repository.FindAsync(id).ConfigureAwait(false);
    return result;
}

// ❌ Incorrect
public Blueprint GetBlueprint(string id)
{
    return _repository.FindAsync(id).Result;
}
```

### 4.4 Null safety

- Activer le contexte nullable : `<Nullable>enable</Nullable>` dans le `.csproj`
- Utiliser `?` pour les types nullables, `!` uniquement quand le null est impossible
- Préférer `??` et `?.` aux vérifications explicites quand lisible

```csharp
// ✅ Correct
public string? Description { get; set; }
var name = blueprint?.Name ?? "Inconnu";

// ❌ Incorrect
public string Description { get; set; } = null!; // sauf si vraiment garanti
```

### 4.5 Pattern matching et switch expressions

Préférer les formes modernes (C# 8+) :

```csharp
// ✅ Correct
var label = subtype switch
{
    EquipmentSubtype.Helmet      => "Casque",
    EquipmentSubtype.HeavyWeapon => "Arme lourde",
    _                            => "Inconnu"
};

// ❌ Éviter
string label;
switch (subtype)
{
    case EquipmentSubtype.Helmet:
        label = "Casque";
        break;
    // ...
}
```

### 4.6 LINQ

- Préférer la syntaxe méthode à la syntaxe de requête
- Éviter les LINQ imbriqués complexes — extraire dans des méthodes nommées

```csharp
// ✅ Correct
var t1Blueprints = blueprints
    .Where(b => b.Tier == "T1")
    .OrderBy(b => b.Name)
    .ToList();

// ❌ Éviter pour la lisibilité
var t1Blueprints = (from b in blueprints where b.Tier == "T1" orderby b.Name select b).ToList();
```

---

## 5. Documentation XML

Toutes les classes publiques et méthodes publiques doivent avoir un commentaire XML :

```csharp
/// <summary>
/// Service responsable de la récupération et du cache des blueprints Star Citizen.
/// </summary>
public class BlueprintService : IBlueprintService
{
    /// <summary>
    /// Récupère un blueprint par son identifiant unique.
    /// </summary>
    /// <param name="id">Identifiant du blueprint (ex: "bpo_helmet_renter_01").</param>
    /// <returns>Le blueprint correspondant, ou <c>null</c> s'il n'existe pas.</returns>
    public async Task<Blueprint?> GetBlueprintAsync(string id)
    {
        // ...
    }
}
```

Les membres privés n'ont pas besoin de commentaire XML — un commentaire `//` suffit si nécessaire.

---

## 6. ViewModels Avalonia (CommunityToolkit.Mvvm)

Utiliser les **source generators** de `CommunityToolkit.Mvvm` :

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StarXelem.ViewModels;

/// <summary>
/// ViewModel pour la vue détail d'un blueprint.
/// </summary>
public partial class BlueprintDetailViewModel : ObservableObject
{
    // ✅ Backing field pour propriété observable
    [ObservableProperty]
    private string _blueprintName = string.Empty;

    // ✅ Commande asynchrone
    [RelayCommand]
    private async Task LoadBlueprintAsync(string id)
    {
        // ...
    }

    // ✅ Propriété calculée
    public bool HasModifiers => Modifiers.Count > 0;
}
```

---

## 7. Gestion des erreurs

- Ne jamais avaler une exception silencieusement
- Logger avant de gérer ou relancer
- Utiliser des exceptions métier personnalisées si nécessaire

```csharp
// ✅ Correct
try
{
    var data = await _apiClient.FetchBlueprintAsync(id);
}
catch (HttpRequestException ex)
{
    _logger.LogError(ex, "Erreur réseau lors de la récupération du blueprint {Id}", id);
    // Notifier l'UI ou relancer
    throw new BlueprintLoadException($"Impossible de charger le blueprint {id}.", ex);
}

// ❌ Incorrect
try { ... }
catch { } // avale l'exception
```

---

## 8. Checklist rapide

Avant de soumettre du code, vérifier :

- [ ] Nommage respecté (PascalCase, _camelCase, suffixe `Async`)
- [ ] File-scoped namespace
- [ ] Modificateurs d'accès explicites
- [ ] Accolades Allman
- [ ] Nullable activé et annoté
- [ ] Commentaires XML sur les membres publics
- [ ] Pas de `.Result` / `.Wait()` sur les Tasks
- [ ] Usings triés et nettoyés
