# Reputation Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a new Reputation page that merges exhaustive game data (P4K) with player-specific progress (gRPC) and displays them as a grid of contractor cards.

**Architecture:** 
- **Data Layer**: `IGrpcClientService` retrieves player reputations; `IP4kService` provides the full list of possible reputations.
- **Domain Layer**: `IReputationService` performs the merge and grouping logic.
- **Presentation Layer**: `ReputationTabViewModel` handles state and local search filtering; `ReputationTabView` renders the UI using a `WrapPanel` of cards.

**Tech Stack:** .NET, Avalonia UI, CommunityToolkit.Mvvm, gRPC.

---

### Task 1: Data Models

**Files:**
- Create: `StarXelem/Models/ReputationModel.cs`
- Create: `StarXelem/Models/ContractorModel.cs`

- [ ] **Step 1: Create `ReputationModel`**
```csharp
namespace StarXelem.Models;

public class ReputationModel
{
    public string Category { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public float CurrentValue { get; set; }
    public float MaxValue { get; set; }
}
```

- [ ] **Step 2: Create `ContractorModel`**
```csharp
using System.Collections.Generic;

namespace StarXelem.Models;

public class ContractorModel
{
    public string Name { get; set; } = string.Empty;
    public List<ReputationModel> Reputations { get; set; } = new();
}
```

- [ ] **Step 3: Commit**
```bash
git add StarXelem/Models/ReputationModel.cs StarXelem/Models/ContractorModel.cs
git commit -m "feat(models): add ReputationModel and ContractorModel"
```

### Task 2: gRPC Client Update

**Files:**
- Modify: `StarXelem/Services/IGrpcClientService.cs`
- Modify: `StarXelem/Services/GrpcClientService.cs`

- [ ] **Step 1: Add `QueryReputationsAsync` to `IGrpcClientService`**
Add the following method to the interface:
```csharp
Task<List<VersionedReputation>> QueryReputationsAsync();
```

- [ ] **Step 2: Implement `QueryReputationsAsync` in `GrpcClientService`**
Implement the call to `ReputationService.QueryReputations`.
```csharp
public async Task<List<VersionedReputation>> QueryReputationsAsync()
{
    var response = await _grpcClient.QueryReputationsAsync(new QueryReputationsRequest());
    return response.Reputations.ToList();
}
```

- [ ] **Step 3: Commit**
```bash
git add StarXelem/Services/IGrpcClientService.cs StarXelem/Services/GrpcClientService.cs
git commit -m "feat(grpc): add QueryReputationsAsync to GrpcClientService"
```

### Task 3: Reputation Domain Service

**Files:**
- Create: `StarXelem/Services/IReputationService.cs`
- Create: `StarXelem/Services/ReputationService.cs`

- [ ] **Step 1: Define `IReputationService`**
```csharp
using StarXelem.Models;

namespace StarXelem.Services;

public interface IReputationService
{
    Task<List<ContractorModel>> GetSynchronizedReputationsAsync();
}
```

- [ ] **Step 2: Implement `ReputationService`**
Implement the fusion logic:
1. Call `IP4kService` to get all possible reputations (assuming a method like `GetAllReputationsAsync` exists or needs to be mocked/found).
2. Call `IGrpcClientService.QueryReputationsAsync()`.
3. Merge: for each P4K reputation, check if the player has a score in gRPC data.
4. Group by Contractor name.
5. Sort by Contractor name.

```csharp
// Implementation details to be refined based on IP4kService available methods
```

- [ ] **Step 3: Commit**
```bash
git add StarXelem/Services/IReputationService.cs StarXelem/Services/ReputationService.cs
git commit -m "feat(services): implement ReputationService for data fusion"
```

### Task 4: DI Registration

**Files:**
- Modify: `StarXelem/Services/ServiceCollectionExtensions.cs` (or `App.axaml.cs` depending on registration location)

- [ ] **Step 1: Register `IReputationService`**
Add `services.AddSingleton<IReputationService, ReputationService>();` to the registration method.

- [ ] **Step 2: Commit**
```bash
git add StarXelem/Services/ServiceCollectionExtensions.cs
git commit -m "chore: register ReputationService in DI"
```

### Task 5: ReputationTabViewModel

**Files:**
- Create: `StarXelem/ViewModels/ReputationTabViewModel.cs`

- [ ] **Step 1: Implement `ReputationTabViewModel`**
- Inherit from `PageViewModelBase`.
- Add `ObservableCollection<ContractorModel> FilteredContractors`.
- Add `SearchText` property with `SetProperty` to trigger filtering.
- Implement `LoadDataCommand` calling `IReputationService.GetSynchronizedReputationsAsync()`.
- Implement filtering logic: `FilteredContractors` should only contain items where `Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)`.

- [ ] **Step 2: Commit**
```bash
git add StarXelem/ViewModels/ReputationTabViewModel.cs
git commit -m "feat(viewmodels): implement ReputationTabViewModel"
```

### Task 6: ReputationTabView UI

**Files:**
- Create: `StarXelem/Views/ReputationTabView.axaml`
- Create: `StarXelem/Views/ReputationTabView.axaml.cs`

- [ ] **Step 1: Create XAML Layout**
- Use a `DockPanel` for the root.
- Top: `StackPanel` (Orientation=Horizontal) containing a `TextBox` (Search) and `Button` (Load).
- Center: `ScrollViewer` $\rightarrow$ `WrapPanel`.
- Inside `WrapPanel`: `ItemsControl` with a `DataTemplate` for `ContractorModel`.
- The Template should be a `Border` (Card) containing:
    - `TextBlock` for Contractor Name.
    - `ItemsControl` for `Reputations` list.
    - Each reputation item: `TextBlock` (Category + Tier) + `ProgressBar` (Value/Max).

- [ ] **Step 2: Apply Design System Styles**
- Apply colors from `CLAUDE_design_convention.md`:
    - Card background: `#08FFFFFF` (Dark) / `#FFFAFAFA` (Light).
    - ProgressBar Foreground: `AccentBrush` (Violet).
    - Border: `0.5px solid`.

- [ ] **Step 3: Commit**
```bash
git add StarXelem/Views/ReputationTabView.axaml StarXelem/Views/ReputationTabView.axaml.cs
git commit -m "feat(views): implement ReputationTabView with contractor cards"
```

### Task 7: Final Integration

**Files:**
- Modify: `StarXelem/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Add ReputationTab to the main navigation**
- Instantiate `ReputationTabViewModel` and add it to the tabs collection.

- [ ] **Step 2: Commit**
```bash
git add StarXelem/ViewModels/MainWindowViewModel.cs
git commit -m "feat: integrate ReputationTab into MainWindow"
```

### Verification Plan
1. **Build**: Ensure the project compiles without errors.
2. **Visual Check**: Open the Reputation tab and verify the `WrapPanel` layout.
3. **Data Load**: Click "Charger les données" and verify that contractor cards appear.
4. **Search**: Type a contractor name and verify that other cards are hidden.
5. **UI Accuracy**: Check that ProgressBars are filled correctly and colors match the design spec.
