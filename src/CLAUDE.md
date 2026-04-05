# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                      # Build the solution
dotnet run --project StarXelem    # Run in debug mode
dotnet publish -c Release         # Publish release build
```

There are no automated tests in this project.

To update game data files from a Star Citizen installation, use the PowerShell scripts in `../scripts/`:
- `update_live.ps1` — extracts from LIVE channel
- `update_ptu.ps1` — extracts from PTU channel

## Architecture

**StarXelem** is a Windows desktop application (Avalonia 11 + FluentAvalonia) for browsing Star Citizen game data. It reads game archives (`.p4k` files) and communicates with the game's gRPC backend.

### Technology Stack

- **UI:** Avalonia 11 with Fluent theme (FluentAvalonia) — XAML-based cross-platform UI
- **Pattern:** MVVM via `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]` source generators)
- **DI:** `Microsoft.Extensions.DependencyInjection`
- **Game data:** `StarBreaker.*` DLLs in `libs/` — P4K archive parsing, DataCore (DCB files), gRPC definitions
- **Backend:** gRPC/Protobuf communication with the Star Citizen game client

### Project Layout

```
StarXelem/
├── App.axaml(.cs)         # App root; DI registration, view locator setup
├── Program.cs             # Entry point (STAThread → Avalonia startup)
├── ViewLocator.cs         # Convention-based ViewModel→View mapping
├── ViewModels/            # MVVM ViewModels (extend ViewModelBase or PageViewModelBase)
├── Views/                 # Avalonia XAML views
├── Services/              # Business logic: P4kService, GrpcClientService, LocationService
├── Models/                # Data models (SpaceshipModel, ItemViewModel, P4kFileModel…)
├── Components/            # Custom Avalonia controls
├── Converters/            # XAML value converters
├── Style/                 # XAML resource dictionaries / styling
└── datafiles/             # Extracted game data bundled with the app
```

### Key Abstractions

**Services (interfaces in `Services/`):**
- `IP4kService` — opens and indexes the `Data.p4k` archive; exposes game entity definitions and locale strings
- `IGrpcClientService` — wraps all gRPC calls to the Star Citizen game backend (ships, inventory, blueprints, friends, missions)
- `ILocationService` — resolves raw container IDs to human-readable location names

Each service has a `Design*` implementation (e.g. `DesignP4kService`) registered when `Design.IsDesignMode` is true, enabling Avalonia design-time previews.

**View resolution:** `ViewLocator` maps `StarXelem.ViewModels.XyzViewModel` → `StarXelem.Views.XyzView` by name convention. Views are registered once at startup via reflection.

**Popup system:** Decoupled via `WeakReferenceMessenger`. Any code can send `ShowPopupMessage` / `ClosePopupMessage`; `PopupViewModel` listens and manages the overlay.

**Tab pages:** Each tab extends `PageViewModelBase` which adds `OnFirstShowAsync()` / `OnShowAsync()` lifecycle hooks. Tabs are lazy-loaded on first activation.

### Application Startup Flow

1. `Program.Main` → `BuildAvaloniaApp().StartWithClassicDesktopLifetime()`
2. `App.OnFrameworkInitializationCompleted()` → registers all DI services → builds `ServiceProvider` → creates `MainWindow`
3. `MainWindowViewModel` loads known P4K installation paths (from Windows Registry), user selects one
4. On selection: P4K opens → data cache fills → gRPC client initializes
5. Tabs query services independently as the user navigates

### Conventions

- Use `[ObservableProperty]` for bindable properties and `[RelayCommand]` / `[AsyncRelayCommand]` for commands — let the source generator emit the boilerplate
- Add new services to `ServiceCollectionExtensions.RegisterServices()` and provide a `Design*` counterpart
- New tab pages: create `XyzTabViewModel : PageViewModelBase` + `XyzTabView.axaml`, then register the view in `ViewLocator` and add the tab to `MainWindowViewModel`
- Line length limit: 200 characters (see `.editorconfig`)