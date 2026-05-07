---
name: Agentic Visual Testing — Plan d'implémentation
description: Plan concret pour rendre StarXelem testable en headless via flag isTestMode et mocks de services.
type: plan
---

# Plan : Intégration mode Test pour Tests Visuels Headless

## État initial (ce qui existe déjà)

| Élément | Statut |
|---------|--------|
| `StarXelem.Tests.Visual` | Projet créé avec `Avalonia.Headless.XUnit` + xUnit 3 |
| `Program.cs` | Parse `--test-mode` mais ne transmet pas la valeur à l'UI |
| `App.OnFrameworkInitializationCompleted()` | Appelle `RegisterServices(Design.IsDesignMode)` — pas de paramètre test mode |
| `ServiceCollectionExtensions.RegisterServices(bool isDesignMode)` | 2 branches: Design / Runtime |
| `DesignGrpcClientService` | Mock design-time existant (données fictives) |
| `FriendListTabViewModel` | Dépend de `IGrpcClientService` — **premier écran cible** |

---

## Phase 1 : Brancher le flag `isTestMode` dans le DI container

### File: `Program.cs`
**Changement :** Transmettre `isTestMode` à `AppBuilder`.

```csharp
// Avant (current)
public static AppBuilder BuildAvaloniaApp(bool isTestMode = false)
    => AppBuilder.Configure<App>()...;

// Après — rien à changer côté signature, mais il faut passer isTestMode via un moyen de configuration
```

**Approche :** Ajouter une méthode statique sur `App` pour indiquer le mode test, car `ApplicationLifetime` n'est pas disponible avant que `BuildAvaloniaApp()` ne retourne.

### File: `App.axaml.cs`
**Changements :**

1. Ajouter une propriété statique `public static bool IsTestMode { get; private set; }` initialisée avant `OnFrameworkInitializationCompleted`.
2. Modifier `OnFrameworkInitializationCompleted` pour transmettre le flag à `RegisterServices`.

```csharp
// Ajouts dans la classe App :
public static bool IsTestMode { get; private set; } = false;

public void SetTestMode(bool value) => IsTestMode = value;

// Dans OnFrameworkInitializationCompletedChanged :
collection.RegisterServices(Design.IsDesignMode || IsTestMode);
```

**Problème :** `AppBuilder.Configure<App>()` crée une instance de `App` avant qu'on puisse appeler `SetTestMode`. Solution : utiliser un registre global statique ou modifier la signature de `RegisterServices`.

### File: `ServiceCollectionExtensions.cs`
**Changement :** Ajouter le paramètre `isTestMode` à `RegisterServices`.

```csharp
// Avant :
public static void RegisterServices(this ServiceCollection services, bool isDesignMode)

// Après :
public static void RegisterServices(this ServiceCollection services, bool isDesignMode, bool isTestMode = false)
```

**Logique conditionnelle :**
- `isTestMode == true` → même registre que `isDesignMode == true` (mocks au lieu de vrais services) + override pour les ViewModels qui dépendent de données persistantes.
- `isTestMode == false && isDesignMode == true` → design-time uniquement (pour le previewer).

---

## Phase 2 : Mocks de services pour le mode test

### File: `StarXelem.Tests.Visual/TestServiceCollectionExtensions.cs` (nouveau)
Créer des mocks spécifiques au test, pas juste design-mode. Ces mocks retournent des données déterministes.

```csharp
// Enregistre dans un ServiceCollection dédié aux tests headless
public static class TestServiceCollectionExtensions
{
    public static void RegisterTestServices(this ServiceCollection services)
    {
        // IGrpcClientService mock avec données amies fictives mais structurées correctement
        services.AddSingleton<IGrpcClientService, TestGrpcClientService>();

        // LocationService mock — résolution statique
        services.AddSingleton<ILocationService, TestLocationService>();

        // P4kService mock — retourne des index vides
        services.AddSingleton<IP4kService, TestP4kService>();

        // EntityClassDefinitionService mock
        services.AddSingleton<IEntityClassDefinitionService, MockEntityClassDefinitionService>();

        // Tous les ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<FriendListTabViewModel>();
        // ... autres ViewDependencies
    }
}
```

### File: `StarXelem.Tests.Visual/TestGrpcClientService.cs` (nouveau)
Implémentation mock de `IGrpcClientService` qui retourne des données prévisibles pour les tests visuels.

**Données clés nécessaires pour FriendListTabViewModel :**
- `GetFriendList()` → liste de 3 amis avec états variés (en jeu, hors ligne, shard)
- `Status` → `GrpcConnectionStatus.Connected`
- `OnStatusChanged` → event vide mais fonctionnel

---

## Phase 3 : Premier test — FriendListTabView en headless

### File: `StarXelem.TestsVisual/FriendListHeadlessTest.cs` (nouveau)
Test qui instancie FriendListTabViewModel + View dans un contexte headless et capture le screenshot.

```csharp
public class FriendListHeadlessTest : IClassFixture<HeadlessAppFixture>
{
    private readonly HeadlessAppFixture _fixture;

    public FriendListHeadlessTest(HeadlessAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void FriendList_ShouldRender()
    {
        var viewModel = _fixture.ServiceProvider.GetRequiredService<FriendListTabViewModel>();

        // Charger la liste d'amis
        viewModel.LoadFriendListCommand?.Execute(null);

        // Capture headless du rendering
        var window = new FriendListTabView { DataContext = viewModel };
        var surface = _fixture.Render(window);

        // Screenshot comparison with reference (Phase 2+ du framework complet)
        surface.Screenshot().SaveAsPng("friend-list-actual.png");
    }
}
```

### File: `StarXelem.TestsVisual/HeadlessAppFixture.cs` (nouveau)
Fixture xUnit qui initialise le DI container en mode test et prépare le headless render.

---

## Phases futures (hors ce plan d'implémentation immédiate)

4. **Agent B — Reference Generator** : intégration Playwright pour générer les images "Golden"
5. **Agent C — Visual Judge** : intégration LLM local (Ollama) pour analyse sémantique
6. **CLI orchestrator** : outil CLI unique pour piloter les 3 agents

---

## Fichiers à créer/modifier — résumé

| Action | Fichier | Rôle |
|--------|---------|------|
| **MODIFIER** | `Program.cs` | Transmettre `isTestMode` via un registre statique |
| **MODIFIER** | `App.axaml.cs` | Ajouter `IsTestMode` getter + le passer à `RegisterServices` |
| **MODIFIER** | `ServiceCollectionExtensions.cs` | Paramètre `isTestMode` supplémentaire |
| **CRÉER** | `StarXelem.TestsVisual/TestServiceCollectionExtensions.cs` | Enregistrement DI pour tests headless |
| **CRÉER** | `StarXelem.Tests.Visual/TestGrpcClientService.cs` | Mock IGrpcClientService avec données prévisibles |
| **CRÉER** | `StarXelem.TestsVisual/HeadlessAppFixture.cs` | Fixture xUnit pour rendu headless |
| **CRÉER** | `StarXelem.TestsVisual/FriendListHeadlessTest.cs` | Premier test screenshot FriendListTabView |
