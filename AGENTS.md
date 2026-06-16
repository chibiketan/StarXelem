# AGENTS.md - StarXelem Developer Guide

## 🛠️ Commands
- **Build Project**: `dotnet build src\StarXelem\StarXelem.csproj`
- **Verify Build**: No errors after adding new dependencies or changing entity models — the project rebuilds the DB schema on launch via EF migrations.

## 🏗️ Architecture & Data Flow
- **Tech Stack**: Avalonia 11 (FluentAvalonia), MVVM (CommunityToolkit.Mvvm), EF Core SQLite.
- **P4K Integration**: Uses `StarBreaker.*` libraries to parse game archives.
- **Local DB**: SQLite database stored in `APP_DATA`, rebuilt entirely when P4K files change.
    - **Ships**: PK is `EntityClassGuid`.
    - **Missions**: PK is `contract.id`.
    - **Manufacturers**: PK is `manufacturer.Code`.
    - **ContractGeneratorEntity**: PK is synthetic `{selfId}-{handlerIndex}` because neither `selfId` nor `debugName` is unique within a file. `MissionEntity.GeneratorId` FK points to this composite key.

## ⚠️ Critical Gotchas (Hard-Earned Context)
- **P4K GUIDs**: `EntityClassDefinition` does NOT contain the `RecordId`. The GUID must be extracted from the `DataCoreTypedRecord` wrapper.
- **Mission Extraction**: `ContractGenerator` records MUST be loaded at depth ≥ 3 to access `contracts` → `template` → `objectiveTokens`.
- **Contract Generators**: `ContractGenerator` is a container; actual mission logic resides in its `generators` collection (e.g., `ContractGeneratorHandler_Career`). Iterating with `handlerIndex` ensures deterministic FK mapping.
- **Thread Safety**: `P4kService.s_maxRecursiveLoad` is `[ThreadStatic]`. Parallel record loading requires setting this value on every thread.
- **Tag Resolution**: Tags are resolved via `tagdatabase.tagdatabase.xml`. Matching is hierarchical: a ship matches a required tag if the ship's tag `Path` (e.g., `CombatShip/SnubFighter`) starts with the required tag's `Path`.
- **ContractGeneratorEntity PK collision**: Within a single XML file, multiple handlers can share the same `selfId` AND the same `debugName` (e.g., `foxwellenforcement_defenddestructibleentities.xml` has two `Foxwell_DefendDestructibleEntities_Stanton` handlers). Always use the `{selfId}-{handlerIndex}` composite key to keep everything linkable.
- **No automated tests**: The project has no test suite. Verification requires manual run + build.

## 📂 Key Files
- `src/StarXelem/Services/P4kService/P4kService.cs`: Low-level P4K record retrieval and depth management.
- `src/StarXelem/Services/LocalDatabaseService.cs`: DB schema population and bidirectional query API.
- `src/StarXelem/Data/Entities.cs`: EF Core entity definitions.
- `src/StarXelem/Data/StarXelemDbContext.cs`: DB context and relational mappings.
- `src/StarXelem/datafiles/`: Data files from the game.
- `src/StarXelem/datafiles/libs/foundry/records/tagdatabase/tagdatabase.tagdatabase.xml`: Source of truth for tag names.
- `src/StarXelem/datafiles/libs/foundry/records/contracts/contractgenerator/mercenary_guild/foxwellenforcement/shipbattles/foxwellenforcement_ambush.xml`: Reference for spawn logic.
