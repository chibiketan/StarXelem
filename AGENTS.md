# AGENTS.md - StarXelem Developer Guide

## Commands

- **Build**: `dotnet build src\StarXelem\StarXelem.csproj`
- **CLI DB rebuild test**: `dotnet run --project src\StarXelem.cli.testdb\StarXelem.cli.testdb.csproj`
- **No tests**: project has no automated test suite. Verify by building and running.

## Architecture

- **App**: Avalonia 11 desktop app (FluentAvalonia, MVVM via CommunityToolkit.Mvvm). Entry: `src/StarXelem/Program.cs` → `App.axaml.cs`.
- **Two projects**: `StarXelem` (Avalonia app), `StarXelem.cli.testdb` (standalone CLI for DB rebuild testing).
- **Game data**: `StarBreaker.*` DLLs in `libs/` parse Star Citizen `.p4k` archives. Referenced via `<Reference>` with `HintPath`, not NuGet.
- **DB**: EF Core SQLite at `%LOCALAPPDATA%\StarXelem\database.db`. Rebuilt entirely on P4K change (not migrated — full wipe + recreate).
- **Rebuild cancellation**: `LocalDatabaseService.RebuildDbAsync()` uses `CancellationTokenSource`. If a rebuild is running, it cancels the old one, waits for completion, then starts a new one.

## Entity Model (PKs)

| Entity | PK | Notes |
|---|---|---|
| `ShipEntity` | `EntityClassGuid` | GUID from `DataCoreTypedRecord.RecordId`, NOT from `EntityClassDefinition` |
| `MissionEntity` | `contract.id` | |
| `ManufacturerEntity` | `manufacturer.Code` | |
| `ContractGeneratorEntity` | `{selfId}-{handlerIndex}` | Synthetic composite — neither `selfId` nor `debugName` is unique within a file |
| `ActorEntity` | contract param string key | Contractor/organization |
| `MissionCategoryEntity` | locale key | e.g. `ContractCategory_Eliminate` |
| `TagEntity` | `SelfId` | |
| `MissionShipSpawnTagEntity` | `IsIncluded` flag | True = required tag (AND), False = excluded tag (NOT) |

## Critical Gotchas

- **P4K GUIDs**: `EntityClassDefinition` does NOT contain `RecordId`. Extract the GUID from the `DataCoreTypedRecord` wrapper.
- **Mission extraction depth**: `ContractGenerator` records require depth ≥ 3 to reach `contracts` → `template` → `objectiveTokens`.
- **ContractGenerator is a container**: Actual mission logic is in its `generators` collection. Iterate with `handlerIndex` for deterministic FK mapping.
- **`s_maxRecursiveLoad` is `[ThreadStatic]`**: Must be set on every thread for parallel record loading. `GetRecordWithSpecificDepth` handles save/restore.
- **Tag matching is hierarchical**: A ship matches a required tag if the ship's tag `Path` (e.g. `CombatShip/SnubFighter`) starts with the required tag's `Path`.
- **aUEC on MissionEntity**: `AUECReward` (computed) and `AUECCost` (buy-in) are `decimal` columns on `MissionEntity`, NOT in `MissionRewards` table. UI should filter `> 0`.
- **Contractor fallback chain**: contract `propertyOverrides` org → contract `stringParamOverrides` → handler `propertyOverrides` org (with `stringVariants.Name` fallback when `factionReputation` is null due to cross-file P4K reference) → handler `stringParamOverrides`.
- **SQLite `decimal` = TEXT** (exact), `float` = REAL (binary approximation). Use `decimal` for exact numeric storage.
- **Blueprint `results` always empty**: `CraftingRecipe.results.results` is never populated in P4K. Output entity comes from `craftingBlueprint.processSpecificData`.

## Key Files

- `src/StarXelem/Data/Entities.cs` — EF Core entity definitions
- `src/StarXelem/Data/StarXelemDbContext.cs` — DB context and relational mappings
- `src/StarXelem/Services/LocalDatabaseService.cs` — DB ingestion logic (rebuild, contractors, categories, rewards, aUEC, blueprints, spawn rules)
- `src/StarXelem/Services/P4kService/P4kService.cs` — Low-level P4K record retrieval, depth management, locale resolution
- `src/StarXelem/datafiles/libs/foundry/records/tagdatabase/tagdatabase.tagdatabase.xml` — Tag database source of truth
