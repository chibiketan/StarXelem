using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using System.Xml.Linq;

namespace StarXelem.Services;

public record DbBlueprintRow(
    string SelfId,
    string BlueprintName,
    string CategoryName,
    string ProcessType,
    string? OutputEntityClassRef,
    TimeSpan? CraftDuration,
    DbBlueprintCostRow[] Costs,
    DbMissionPoolRow[] MissionPools);

public record DbBlueprintCostRow(
    string CostType,
    string CostName,
    string? ResourceRef,
    decimal? ResourceAmount,
    string? ItemEntityClassRef,
    int? ItemCount,
    int? MinQuality,
    DbBlueprintModifierRow[] Modifiers);

public record DbBlueprintModifierRow(
    string RangeType,
    string PropertyName,
    int StartQuality,
    int EndQuality,
    decimal ModifierStart,
    decimal ModifierEnd);

public record DbMissionPoolRow(
    string PoolName,
    string MissionTitle,
    string MissionDebugName);

public interface ILocalDatabaseService
{
    Task RebuildDbAsync();
    Task EnsureDbAsync();
    Task<List<MissionEntity>> GetMissionsForShipAsync(string shipGuid);
    Task<List<ShipEntity>> GetShipsForMissionAsync(string missionDebugName);
    Task<(Dictionary<string, string> TitleSuffixMap, Dictionary<string, Dictionary<string, HashSet<string>>> DescriptionAppendMap)> GetBlueprintRewardMapsAsync(HashSet<string>? obtainedBlueprintIds = null);
    Task<List<DbBlueprintRow>> GetBlueprintsAsync(HashSet<string>? obtainedBlueprintIds = null);
}

public class LocalDatabaseService : ILocalDatabaseService
{
    private readonly IP4kService _p4kService;
    private readonly ILogger<LocalDatabaseService> _logger;
    private readonly string _dbPath;
    private CancellationTokenSource _rebuildCts = new();
    private Task? _rebuildTask;
    private readonly Dictionary<string, ActorEntity> _contractorCache;
    private readonly Dictionary<string, MissionCategoryEntity> _categoryCache;

    public LocalDatabaseService(IP4kService p4kService, ILogger<LocalDatabaseService> logger, bool autoRebuild = false)
    {
        _p4kService = p4kService;
        _logger = logger;
        _contractorCache = new Dictionary<string, ActorEntity>(StringComparer.Ordinal);
        _categoryCache = new Dictionary<string, MissionCategoryEntity>(StringComparer.Ordinal);
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "StarXelem");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "database.db");

        if (autoRebuild && _p4kService is P4kService p4k)
        {
            p4k.SelectedP4KFileChanged += async (s, e) => await RebuildDbAsync();
        }
    }

    public async Task EnsureDbAsync()
    {
        if (File.Exists(_dbPath))
        {
            _logger.LogInformation("Database already exists at {Path}", _dbPath);
            return;
        }

        _logger.LogInformation("Database not found, rebuilding at {Path}", _dbPath);
        await RebuildDbAsync();
    }

    private DbContextOptions<StarXelemDbContext> GetOptions()
    {
        return new DbContextOptionsBuilder<StarXelemDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
#if DEBUG
            .EnableSensitiveDataLogging()
            .EnableDetailedErrors()
#endif
            .Options;
    }

    private readonly Dictionary<EntityClassDefinition, string> _entityClassToGuid = new();

    /* ========================================================================
     * REBUILD ORCHESTRATION
     * ======================================================================== */

    public async Task RebuildDbAsync()
    {
        if (_rebuildTask != null && !_rebuildTask.IsCompleted)
        {
            _logger.LogWarning("Reconstruction déjà en cours, annulation...");
            _rebuildCts.Cancel();
            try
            {
                await _rebuildTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Reconstruction précédente annulée");
            }
        }

        _rebuildCts = new CancellationTokenSource();
        var cancellationToken = _rebuildCts.Token;

        _rebuildTask = RebuildDbCoreAsync(cancellationToken);
        try
        {
            await _rebuildTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Reconstruction de la BDD annulée");
        }
    }

    private async Task RebuildDbCoreAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Rebuilding local database at {Path}", _dbPath);
        _entityClassToGuid.Clear();
        _contractorCache.Clear();
        _categoryCache.Clear();

        using var db = new StarXelemDbContext(GetOptions());
        
        await db.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // Phase 1: Tag hierarchy
        var tagResolutionMap = new Dictionary<string, string>();
        await PopulateTagHierarchyAsync(db, tagResolutionMap).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: Ships, manufacturers, and ship-tag associations
        await PopulateShipsAndManufacturersAsync(db, tagResolutionMap, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 3: Contract generators
        await PopulateContractGeneratorsAsync(db, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 4: Missions and their requirements/rewards/spawn rules
        await PopulateMissionsAsync(db, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 5: Resolve spawn rules to actual ships
        await ProcessMissionShipSpawnShipsAsync(db, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 6: Blueprints
        await ProcessAllBlueprintsAsync(db, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Database rebuild completed.");
    }

    /* ========================================================================
     * PHASE 1: TAG HIERARCHY
     * ======================================================================== */

    private async Task PopulateTagHierarchyAsync(StarXelemDbContext db, Dictionary<string, string> map)
    {
        var database = await _p4kService.GetTagDatabase();
        
        try
        {
            var tagEntities = new List<TagEntity>();
            var start = Stopwatch.StartNew();

            void ParseTagsRecursive(Tag tagElement, string? parentName, string currentPath)
            {
                var id = tagElement.selfId.ToString();
                var name = tagElement.tagName;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                {
                    var newPath = string.IsNullOrEmpty(currentPath) ? name : $"{currentPath}/{name}";

                    tagEntities.Add(new TagEntity
                    {
                        Name = name,
                        SelfId = id,
                        ParentName = parentName,
                        Path = newPath
                    });

                    map[id] = name;

                    if (tagElement.children != null)
                    {
                        foreach (var child in tagElement.children)
                        {
                            ParseTagsRecursive(child, name, newPath);
                        }
                    }
                }
            }

            if (database.tags != null)
            {
                foreach (var tag in database.tags)
                {
                    ParseTagsRecursive(tag, null, "");
                }
            }
            
            db.Tags.AddRange(tagEntities);
            await db.SaveChangesAsync();
            start.Stop();
            _logger.LogInformation("Tag hierarchy populated in {Elapsed}ms.", start.ElapsedMilliseconds);
            _logger.LogInformation("Populated {Count} tags into database and resolution map.", tagEntities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate tag hierarchy from P4K");
        }
    }

    /* ========================================================================
     * PHASE 2: SHIPS, MANUFACTURERS, SHIP-TAGS
     * ======================================================================== */

    private async Task PopulateShipsAndManufacturersAsync(
        StarXelemDbContext db, 
        Dictionary<string, string> tagResolutionMap, 
        CancellationToken cancellationToken)
    {
        var ships = new List<ShipEntity>();
        var manufacturers = new List<ManufacturerEntity>();
        var manufacturerCache = new Dictionary<string, ManufacturerEntity>();
        var shipTags = new List<ShipTagEntity>();
        var start = Stopwatch.StartNew();

        await foreach (var record in _p4kService.GetAllEntityClassDefinition(1).ConfigureAwait(false))
        {
            if (record.Data is not EntityClassDefinition entityClass)
                continue;

            var vehicleParams = entityClass.Components.OfType<VehicleComponentParams>().FirstOrDefault();
            if (vehicleParams == null)
                continue;

            var guid = record.RecordId.ToString();
            _entityClassToGuid[entityClass] = guid;

            // Extract and add ship tags
            var extractedTags = ExtractShipTags(entityClass, tagResolutionMap);
            foreach (var tagId in extractedTags)
            {
                shipTags.Add(new ShipTagEntity { ShipGuid = guid, TagSelfId = tagId });
            }

            // Resolve manufacturer
            var manufacturerId = ResolveManufacturerId(vehicleParams.manufacturer, manufacturerCache, manufacturers);

            ships.Add(new ShipEntity
            {
                EntityClassGuid = guid,
                TechnicalName = record.RecordName,
                LocalizedName = await _p4kService.GetEntityClassName(entityClass) ?? "Unknown",
                ManufacturerId = manufacturerId
            });
        }

        start.Stop();
        _logger.LogInformation("Ships and manufacturers processed in {Elapsed}ms.", start.ElapsedMilliseconds);

        db.Manufacturers.AddRange(manufacturers);
        db.Ships.AddRange(ships);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Inserted {Count} manufacturer into the database.", manufacturers.Count);
        _logger.LogInformation("Inserted {Count} ship into the database.", ships.Count);

        db.ShipTags.AddRange(shipTags);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Inserted {Count} liaison ship <=> tag into the database.", shipTags.Count);
    }

    private IEnumerable<string> ExtractShipTags(EntityClassDefinition entityClass, Dictionary<string, string> tagResolutionMap)
    {
        var tagSources = new List<IEnumerable<object?>>();

        if (entityClass.tags != null)
            tagSources.Add(entityClass.tags);

        var eaEntityDataParams = entityClass.StaticEntityClassData.OfType<EAEntityDataParams>().FirstOrDefault();
        if (eaEntityDataParams?.inclusionParams?.tags?.tags != null)
            tagSources.Add(eaEntityDataParams.inclusionParams.tags.tags);

        var extractedTags = tagSources
            .SelectMany(source => source)
            .Select(t => ResolveTag(t, tagResolutionMap))
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct();

        foreach (var id in extractedTags)
        {
            if (tagResolutionMap.TryGetValue(id, out _))
            {
                yield return id;
            }
        }
    }

    private string ResolveManufacturerId(
        dynamic? manufacturer, 
        Dictionary<string, ManufacturerEntity> manufacturerCache, 
        List<ManufacturerEntity> manufacturers)
    {
        if (manufacturer == null)
            return GetOrCreateUnknownManufacturer(manufacturerCache, manufacturers).Id;

        var manufacturerId = !string.IsNullOrEmpty(manufacturer.Code) 
            ? manufacturer.Code 
            : (!string.IsNullOrEmpty(manufacturer.Localization.Name) ? manufacturer.Localization.Name : "Unknown");

        if (manufacturerId == "Unknown")
            return GetOrCreateUnknownManufacturer(manufacturerCache, manufacturers).Id;

        ManufacturerEntity? existingEntity;
        if (!manufacturerCache.TryGetValue(manufacturerId, out existingEntity))
        {
            var nameKey = !string.IsNullOrEmpty(manufacturer.Localization.Name) 
                ? manufacturer.Localization.Name 
                : manufacturerId;
            var descKey = !string.IsNullOrEmpty(manufacturer.Localization.Description) 
                ? manufacturer.Localization.Description 
                : string.Empty;

            var entity = new ManufacturerEntity
            {
                Id = manufacturerId,
                Name = _p4kService.GetLocaleValue(nameKey).GetAwaiter().GetResult() ?? manufacturerId,
                NameKey = nameKey,
                Description = _p4kService.GetLocaleValue(descKey).GetAwaiter().GetResult() ?? string.Empty,
                DescriptionKey = descKey,
                Logo = manufacturer.Logo ?? string.Empty
            };
            manufacturerCache[manufacturerId] = entity;
            manufacturers.Add(entity);
        }

        return manufacturerId;
    }

    private ManufacturerEntity GetOrCreateUnknownManufacturer(
        Dictionary<string, ManufacturerEntity> manufacturerCache, 
        List<ManufacturerEntity> manufacturers)
    {
        if (!manufacturerCache.TryGetValue("Unknown", out var entity))
        {
            entity = new ManufacturerEntity { Id = "Unknown", Name = "Unknown" };
            manufacturerCache["Unknown"] = entity;
            manufacturers.Add(entity);
        }
        return entity;
    }

    /* ========================================================================
     * PHASE 3: CONTRACT GENERATORS
     * ======================================================================== */

    private async Task PopulateContractGeneratorsAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var contracts = await _p4kService.GetAllContractGenerator();
        _logger.LogInformation("Found {Count} contract generators. Ensuring depth 5 (factionReputation in org at depth 4-5)...", contracts.Count);
        contracts = await _p4kService.EnsureRecordsDepthAsync(contracts, 3);

        var contractGenerators = ExtractContractGeneratorEntities(contracts);

        db.ContractGenerators.AddRange(contractGenerators);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        start.Stop();
        _logger.LogInformation("Inserted {Count} contract generators into the database.", contractGenerators.Count);
    }

    private List<ContractGeneratorEntity> ExtractContractGeneratorEntities(IEnumerable<DataCoreTypedRecord> contracts)
    {
        var result = new List<ContractGeneratorEntity>();

        foreach (var record in contracts)
        {
            if (record.Data is not ContractGenerator generator)
                continue;
            if (generator.generators == null)
                continue;

            int handlerIndex = 0;
            foreach (var handler in generator.generators)
            {
                if (handler == null)
                    continue;

                var avail = handler.defaultAvailability;
                result.Add(CreateContractGeneratorEntity(generator, handler, handlerIndex, avail));
                handlerIndex++;
            }
        }

        return result;
    }

    private ContractGeneratorEntity CreateContractGeneratorEntity(
        ContractGenerator generator, 
        dynamic handler, 
        int handlerIndex, 
        dynamic? avail)
    {
        return new ContractGeneratorEntity
        {
            Id = $"{generator.selfId}-{handlerIndex}",
            DebugName = handler.debugName,
            NotForRelease = ToBool(handler.notForRelease),
            WorkInProgress = ToBool(handler.workInProgress),
            MaxPlayersPerInstance = avail?.maxPlayersPerInstance ?? 1,
            OnceOnly = ToBool(avail?.onceOnly),
            AvailableInPrison = ToBool(avail?.availableInPrison),
            HideInMobiGlas = ToBool(avail?.hideInMobiGlas),
            CanReacceptAfterAbandoning = ToBool(avail?.canReacceptAfterAbandoning),
            AbandonedCooldownTime = avail?.abandonedCooldownTime ?? 1f,
            AbandonedCooldownTimeVariation = avail?.abandonedCooldownTimeVariation ?? 1f,
            CanReacceptAfterFailing = ToBool(avail?.canReacceptAfterFailing),
            HasPersonalCooldown = ToBool(avail?.hasPersonalCooldown),
            PersonalCooldownTime = avail?.personalCooldownTime ?? 1f,
            PersonalCooldownTimeVariation = avail?.personalCooldownTimeVariation ?? 1f,
            NotifyOnAvailable = ToBool(avail?.notifyOnAvailable),
        };
    }

    private static bool ToBool(dynamic? v, bool @default = false)
    {
        if (v == null) return @default;
        return (bool)v;
    }

    /* ========================================================================
     * PHASE 4: MISSIONS
     * ======================================================================== */

    private async Task PopulateMissionsAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var contracts = await _p4kService.GetAllContractGenerator();
        contracts = await _p4kService.EnsureRecordsDepthAsync(contracts, 3);

        int missionCount = 0;
        foreach (var record in contracts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            missionCount += await ProcessContractForDb(record, db, record.RecordName, cancellationToken);
        }

        start.Stop();
        _logger.LogInformation("Missions and requirements processed in {Elapsed}ms.", start.ElapsedMilliseconds);
        _logger.LogInformation("Inserted {Count} missions into the database.", missionCount);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ProcessContractForDb(DataCoreTypedRecord record, StarXelemDbContext db, string generatorName, CancellationToken cancellationToken)
    {
        if (record.Data is not ContractGenerator generator || generator.generators == null)
            return 0;

        int missionsAdded = 0;
        int handlerIndex = 0;

        foreach (var handler in generator.generators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (handler == null)
                continue;

            var contractsToProcess = ExtractContractsFromHandler(handler);
            if (contractsToProcess == null)
            {
                handlerIndex++;
                continue;
            }

            // Resolve contractor from handler level
            var handlerContractorKey = ResolveHandlerContractorKey(handler);

            foreach (var contract in contractsToProcess)
            {
                if (contract == null)
                    continue;

                var contractorEntity = ResolveContractor(contract, handlerContractorKey, db);
                var categoryEntity = ResolveCategory(contract, db);
                var mission = CreateMissionEntity(contract, generator, handlerIndex, generatorName, contractorEntity, categoryEntity);

                db.Missions.Add(mission);
                missionsAdded++;

                var shipDefs = ExtractShipDefs(contract);
                foreach (var def in shipDefs)
                {
                    if (_entityClassToGuid.TryGetValue(def, out var guid))
                    {
                        db.MissionShipRequirements.Add(new MissionShipRequirementEntity
                        {
                            MissionId = mission.Id,
                            ShipGuid = guid
                        });
                    }
                }

                await ProcessSpawnableShipsAsync(contract, db, mission.Id);
                await ProcessMissionRewards(mission.Id, contract, db);
                await ProcessMissionRequiredTagsAsync(mission.Id, contract, db);
            }

            handlerIndex++;
        }

        return missionsAdded;
    }

    /* ---- Handler and contract extraction ---- */

    private List<ContractBase>? ExtractContractsFromHandler(dynamic handler)
    {
        var contractsToProcess = new List<ContractBase>();

        if (handler is ContractGeneratorHandler_Career career)
        {
            contractsToProcess.AddRange(career.introContracts.Cast<ContractBase>());
            contractsToProcess.AddRange(career.contracts.Cast<ContractBase>());
        }
        else if (handler is ContractGeneratorHandler_List list)
        {
            contractsToProcess.AddRange(list.contracts.Cast<ContractBase>());
        }
        else
        {
            return null;
        }

        return contractsToProcess;
    }

    /* ---- Contractor resolution ---- */

    private string? ResolveHandlerContractorKey(dynamic handler)
    {
        var stringParamKey = FindContractorFromParams(handler.contractParams?.stringParamOverrides);
        var propOverride = FindContractorPropOverride(handler.contractParams?.propertyOverrides);

        string? orgKey = null;
        if (propOverride?.value is MissionPropertyValue_Organization orgValue)
        {
            foreach (var mc in orgValue.matchConditions)
            {
                if (mc is DataSetMatchCondition_SpecificOrganizationsDef specOrg)
                {
                    orgKey = specOrg.organizations.FirstOrDefault()?
                        .factionReputation?.name;

                    if (string.IsNullOrEmpty(stringParamKey))
                    {
                        stringParamKey = specOrg.organizations.FirstOrDefault()?
                            .stringVariants?.variants?.FirstOrDefault(s => s.tag?.tagName == "Name")?.@string;
                    }
                    break;
                }
            }
        }

        return stringParamKey ?? orgKey;
    }

    private static string? FindContractorFromParams(object? overrides)
    {
        if (overrides == null)
            return null;
        foreach (dynamic p in (overrides as dynamic[] ?? Array.Empty<dynamic>()))
        {
            if (p.param == ContractStringParamType.Contractor)
                return p.value;
        }
        return null;
    }

    private static dynamic? FindContractorPropOverride(object? overrides)
    {
        if (overrides == null)
            return null;
        foreach (dynamic p in (overrides as dynamic[] ?? Array.Empty<dynamic>()))
        {
            if (p.extendedTextToken == "Contractor")
                return p;
        }
        return null;
    }

    private ActorEntity? ResolveContractor(ContractBase contract, string? handlerContractorKey, StarXelemDbContext db)
    {
        string? contractorKey = null;

        // 1st: contract propertyOverrides "Contractor" MissionProperty
        var contractContractorProp = contract.paramOverrides?.propertyOverrides
            ?.FirstOrDefault(p => p.extendedTextToken == "Contractor");
        if (contractContractorProp?.value is MissionPropertyValue_Organization cOrgValue)
        {
            foreach (var mc in cOrgValue.matchConditions)
            {
                if (mc is DataSetMatchCondition_SpecificOrganizationsDef cSpecOrg)
                {
                    contractorKey = cSpecOrg.organizations.FirstOrDefault()?
                        .factionReputation?.name;
                    break;
                }
            }
        }

        // 2nd: contract stringParamOverrides
        if (string.IsNullOrEmpty(contractorKey))
        {
            contractorKey = contract.paramOverrides?.stringParamOverrides
                ?.FirstOrDefault(p => p.param == ContractStringParamType.Contractor)?.value;
        }

        // 3rd: handler resolved key
        if (string.IsNullOrEmpty(contractorKey))
        {
            contractorKey = handlerContractorKey;
        }

        if (string.IsNullOrEmpty(contractorKey))
            return null;

        if (!_contractorCache.TryGetValue(contractorKey, out var entity))
        {
            var name = Task.Run(async () => await _p4kService.GetLocaleValue(contractorKey)).Result ?? "Inconnu";
            entity = new ActorEntity
            {
                Id = contractorKey,
                NameKey = contractorKey,
                Name = name
            };
            db.Actors.Add(entity);
            _contractorCache[contractorKey] = entity;
        }

        return entity;
    }

    /* ---- Category resolution ---- */

    private MissionCategoryEntity? ResolveCategory(ContractBase contract, StarXelemDbContext db)
    {
        string? categoryKey = null;

        if (contract.paramOverrides?.missionTypeOverride != null)
        {
            categoryKey = contract.paramOverrides.missionTypeOverride.LocalisedTypeName;
        }
        else
        {
            categoryKey = contract.template?.contractDisplayInfo?.type?.LocalisedTypeName;
        }

        if (string.IsNullOrEmpty(categoryKey))
            return null;

        if (!_categoryCache.TryGetValue(categoryKey, out var entity))
        {
            var categoryName = Task.Run(async () => await _p4kService.GetLocaleValue(categoryKey)).Result ?? "Inconnue";
            entity = new MissionCategoryEntity
            {
                Id = categoryKey,
                Name = categoryName
            };
            db.MissionCategories.Add(entity);
            _categoryCache[categoryKey] = entity;
        }

        return entity;
    }

    /* ---- Mission entity construction ---- */

    private MissionEntity CreateMissionEntity(
        ContractBase contract,
        ContractGenerator generator,
        int handlerIndex,
        string generatorName,
        ActorEntity? contractorEntity,
        MissionCategoryEntity? categoryEntity)
    {
        var titleKey = contract.paramOverrides?.stringParamOverrides
            ?.FirstOrDefault(p => p.param == ContractStringParamType.Title)?.value 
            ?? contract.template?.contractDisplayInfo?.displayString[0];

        var descKey = contract.paramOverrides?.stringParamOverrides
            ?.FirstOrDefault(p => p.param == ContractStringParamType.Description)?.value 
            ?? contract.template?.contractDisplayInfo?.displayString[2];

        return new MissionEntity
        {
            Id = contract.id.ToString(),
            DebugName = contract.debugName,
            GeneratorName = generatorName,
            TitleKey = titleKey,
            Title = Task.Run(async () => await _p4kService.GetLocaleValue(titleKey)).Result ?? "Unknown",
            DescriptionKey = descKey,
            Description = Task.Run(async () => await _p4kService.GetLocaleValue(descKey)).Result ?? "",
            NotForRelease = (bool)contract.notForRelease,
            WorkInProgress = (bool)contract.workInProgress,
            GeneratorId = $"{generator.selfId}-{handlerIndex}",
            Contractor = contractorEntity,
            Category = categoryEntity
        };
    }

    /* ========================================================================
     * PHASE 5: SPAWN RULES -> SHIPS RESOLUTION
     * ======================================================================== */

    private async Task ProcessMissionShipSpawnShipsAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        long toto = 0;
        string[] excludedPath = ["/Spawning/", "/SkillDefinitions/", "/CargoManifest/", "/CrewManifest/"];
        
        var shipSpawnRules = db.MissionShipSpawns
            .Include(s => s.Tags)
            .ThenInclude(st => st.Tag)
            .ToAsyncEnumerable();

        await foreach (var spawnRule in shipSpawnRules.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var posTags = spawnRule.Tags
                .Where(t => t.IsIncluded)
                .Select(t => t.Tag!)
                .Where(t => !excludedPath.Any(ep => t.Path.Contains(ep)))
                .Select(t => t.SelfId)
                .ToList();

            var negTags = spawnRule.Tags
                .Where(t => !t.IsIncluded)
                .Select(t => t.Tag!.SelfId)
                .ToList();

            var shipsQuery = db.Ships
                .Where(s => !negTags.Any(t => s.ShipTags.Any(st => st.Tag!.SelfId == t)));

            if (posTags.Any())
            {
                shipsQuery = shipsQuery.Where(s => posTags.All(t => s.ShipTags.Any(st => st.Tag!.SelfId == t)));
            }

            await foreach (var ship in shipsQuery.ToAsyncEnumerable().ConfigureAwait(false))
            {
                db.MissionShipSpawnShips.Add(new MissionShipSpawnShipEntity
                {
                    SpawnRule = spawnRule,
                    Ship = ship
                });
                ++toto;
            }
        }
        
        start.Stop();
        _logger.LogInformation("Processed {Count} spawn rules.", toto);
        _logger.LogInformation("Mission spawn rules processed in {Elapsed}ms.", start.ElapsedMilliseconds);
    }

    /* ========================================================================
     * SPAWNABLE SHIPS (per mission)
     * ======================================================================== */

    private async Task ProcessSpawnableShipsAsync(ContractBase contract, StarXelemDbContext db, string missionId)
    {
        var tempRules = new List<(MissionShipSpawnEntity Rule, List<string> PosTags, List<string> NegTags)>();
        var allProperties = new Dictionary<string, MissionProperty>();

        if (contract.template?.contractProperties != null)
        {
            foreach (var prop in contract.template.contractProperties)
            {
                allProperties[prop.missionVariableName] = prop;
            }
        }

        foreach (var overrideProp in contract.paramOverrides.propertyOverrides)
        {
            allProperties[overrideProp.missionVariableName] = overrideProp;
        }

        foreach (var prop in allProperties.Values)
        {
            var value = prop.value as MissionPropertyValue_ShipSpawnDescriptions;
            if (value?.spawnDescriptions == null)
                continue;

            foreach (var group in value.spawnDescriptions)
            {
                if (group.ships == null)
                    continue;

                foreach (var shipOption in group.ships)
                {
                    if (shipOption.options == null)
                        continue;

                    foreach (dynamic ship in shipOption.options)
                    {
                        var posIds = ExtractTagIds(ship, "tags");
                        var negIds = ExtractTagIds(ship, "negativeTags");
                        var groupName = !string.IsNullOrWhiteSpace(group?.Name) ? group.Name : prop.missionVariableName;

                        var spawnRule = new MissionShipSpawnEntity
                        {
                            MissionId = missionId,
                            GroupName = groupName,
                            Weight = (int)ship.weight
                        };

                        db.MissionShipSpawns.Add(spawnRule);
                        tempRules.Add((spawnRule, posIds, negIds));
                    }
                }
            }
        }
        
        foreach (var item in tempRules)
        {
            foreach (var tagStr in item.PosTags)
            {
                if (string.IsNullOrEmpty(tagStr)) continue;
                db.MissionShipSpawnTags.Add(new MissionShipSpawnTagEntity
                {
                    SpawnRule = item.Rule,
                    TagSelfId = tagStr,
                    IsIncluded = true
                });
            }
            
            foreach (var tagStr in item.NegTags)
            {
                if (string.IsNullOrEmpty(tagStr)) continue;
                db.MissionShipSpawnTags.Add(new MissionShipSpawnTagEntity
                {
                    SpawnRule = item.Rule,
                    TagSelfId = tagStr,
                    IsIncluded = false
                });
            }
        }
    }

    private List<string> ExtractTagIds(dynamic ship, string propertyName)
    {
        var ids = new List<string>();
        var prop = ship.GetType().GetProperty(propertyName);
        if (prop == null)
            return ids;

        var tagsObj = prop.GetValue(ship);
        if (tagsObj is StarBreaker.DataCoreGenerated.TagList tagList)
        {
            foreach (var t in tagList.@tags)
            {
                if (t != null) ids.Add(t.selfId.ToString());
            }
        }
        else if (tagsObj != null)
        {
            foreach (dynamic t in tagsObj)
            {
                if (t != null) ids.Add(t.selfId.ToString());
            }
        }

        return ids;
    }

    /* ========================================================================
     * SHIP DEFINITIONS EXTRACTION
     * ======================================================================== */

    private List<EntityClassDefinition> ExtractShipDefs(ContractBase contract)
    {
        var defs = new HashSet<EntityClassDefinition>();
        if (contract.template?.objectiveTokens == null)
            return defs.ToList();

        foreach (var token in contract.template.objectiveTokens)
        {
            if (token.objectiveHandler is ObjectiveHandler_Hauling hauling)
            {
                foreach (var order in hauling.haulingOrders)
                {
                    ProcessHaulingOrder(contract, order, defs);
                }
            }
        }
        return defs.ToList();
    }

    private void ProcessHaulingOrder(ContractBase contract, object? order, HashSet<EntityClassDefinition> defs)
    {
        switch (order)
        {
            case HaulingOrder_EntityClass ec:
                if (ec.entityClass != null) defs.Add(ec.entityClass);
                break;
            case HaulingOrder_EntityClasses ecs:
                if (ecs.haulingEntityClasses != null)
                {
                    foreach (var ec in GetEntityClasses(ecs.haulingEntityClasses))
                    {
                        if (ec != null) defs.Add(ec);
                    }
                }
                break;
            case HaulingOrder_Property prop:
                var haulingProperty = contract.template?.contractProperties
                    .FirstOrDefault(p => p.value is MissionPropertyValue_HaulingOrders);
                if (haulingProperty == null) return;

                var propertyKey = haulingProperty.missionVariableName;
                var overrideProp = contract.paramOverrides.propertyOverrides
                    .FirstOrDefault(p => p.missionVariableName == propertyKey);

                var haulingOrders = (overrideProp?.value as MissionPropertyValue_HaulingOrders) 
                                   ?? (haulingProperty.value as MissionPropertyValue_HaulingOrders);

                if (haulingOrders?.haulingOrderContent != null)
                {
                    foreach (var content in haulingOrders.haulingOrderContent)
                    {
                        ProcessHaulingOrderContent(content, defs);
                    }
                }
                break;
        }
    }

    private void ProcessHaulingOrderContent(object? content, HashSet<EntityClassDefinition> defs)
    {
        switch (content)
        {
            case HaulingOrderContent_EntityClass ec:
                if (ec.entityClass != null) defs.Add(ec.entityClass);
                break;
            case HaulingOrderContent_EntityClasses ecs:
                if (ecs.haulingEntityClasses != null)
                {
                    foreach (var ec in GetEntityClasses(ecs.haulingEntityClasses))
                    {
                        if (ec != null) defs.Add(ec);
                    }
                }
                break;
        }
    }

    private IEnumerable<EntityClassDefinition> GetEntityClasses(object listBase)
    {
        var prop = listBase.GetType().GetProperties()
            .FirstOrDefault(p => typeof(IEnumerable<EntityClassDefinition>).IsAssignableFrom(p.PropertyType));
        
        if (prop != null)
        {
            return (IEnumerable<EntityClassDefinition>)prop.GetValue(listBase)!;
        }
        
        return Enumerable.Empty<EntityClassDefinition>();
    }

    /* ========================================================================
     * MISSION REWARDS
     * ======================================================================== */

    private async Task<int> ProcessMissionRewards(string missionId, dynamic contract, StarXelemDbContext db)
    {
        var count = 0;

        if (contract.contractResults?.contractResults == null)
            return 0;

        if (contract.contractResults.contractBuyInAmount > 0)
        {
            var mission = db.Missions.Find(missionId);
            if (mission != null)
                mission.AUECCost = (decimal)contract.contractResults.contractBuyInAmount;
        }

        var resultArray = contract.contractResults.contractResults as ContractResultBase[];
        foreach (var resultBase in resultArray ?? Array.Empty<ContractResultBase>())
        {
            if (resultBase == null)
                continue;

            count += await ProcessSingleReward(missionId, resultBase, contract, db);
        }

        return count;
    }

    private async Task ProcessMissionRequiredTagsAsync(string missionId, ContractBase contract, StarXelemDbContext db)
    {
        try
        {
            if (contract.additionalPrerequisites == null)
                return;

            foreach (var prerequisite in contract.additionalPrerequisites)
            {
                if (prerequisite is not ContractPrerequisite_CompletedContractTags completedTags)
                    continue;

                foreach (var tag in completedTags.requiredCompletedContractTags?.tags ?? Enumerable.Empty<object>())
                {
                    if (tag == null) continue;
                    dynamic t = tag;
                    var tagId = t.selfId.ToString();
                    if (string.IsNullOrEmpty(tagId)) continue;

                    db.MissionRequiredTags.Add(new MissionRequiredTagEntity
                    {
                        MissionId = missionId,
                        TagSelfId = tagId,
                        IsRequired = true,
                        RequiredCount = completedTags.requiredCountValue
                    });
                }

                foreach (var tag in completedTags.excludedCompletedContractTags?.tags ?? Enumerable.Empty<object>())
                {
                    if (tag == null) continue;
                    dynamic t = tag;
                    var tagId = t.selfId.ToString();
                    if (string.IsNullOrEmpty(tagId)) continue;

                    db.MissionRequiredTags.Add(new MissionRequiredTagEntity
                    {
                        MissionId = missionId,
                        TagSelfId = tagId,
                        IsRequired = false,
                        RequiredCount = completedTags.excludedCountValue
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec du parsing des prérequis de tags pour la mission {MissionId}", missionId);
        }
    }

    private async Task<int> ProcessSingleReward(
        string missionId, 
        ContractResultBase resultBase, 
        dynamic contract, 
        StarXelemDbContext db)
    {
        switch (resultBase)
        {
            case ContractResult_CalculatedReward calculatedReward:
                {
                    var computed = await ComputeAUECReward(contract, calculatedReward);
                    var mission = db.Missions.Find(missionId);
                    if (mission != null)
                        mission.AUECReward = (decimal)computed;
                }
                return 0;

            case BlueprintRewards blueprintRewards:
                {
                    var poolRef = blueprintRewards.blueprintPool?.selfId.ToString();
                    if (!string.IsNullOrEmpty(poolRef))
                    {
                        var poolRecord = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(poolRef), 0);
                        var poolEntity = new MissionBlueprintPoolEntity
                        {
                            MissionId = missionId,
                            BlueprintPoolRef = poolRef,
                             PoolName = StripRecordPrefix(poolRecord?.RecordName) ?? "Unknown"
                        };
                        db.MissionBlueprintPools.Add(poolEntity);

                        db.MissionRewards.Add(new MissionRewardEntity
                        {
                            MissionId = missionId,
                            RewardType = "BlueprintRewards",
                            DisplayValue = $"Blueprint pool (chance: {blueprintRewards.chance * 100}%)",
                            IsCalculated = false
                        });

                        if (blueprintRewards.blueprintPool?.blueprintRewards != null)
                        {
                            await ProcessBlueprintPoolsAsync(blueprintRewards.blueprintPool, db, poolEntity);
                        }
                    }
                }
                return 1;

            case ContractResult_Reward reward:
                {
                    var contractReward = reward.contractReward;
                    if (contractReward != null)
                    {
                        var currencyName = Enum.GetName(typeof(CurrencyType), contractReward.currencyType);
                        db.MissionRewards.Add(new MissionRewardEntity
                        {
                            MissionId = missionId,
                            RewardType = "ContractResult_Reward",
                            DisplayValue = string.Format("{0:N0} {1}", contractReward.reward, currencyName),
                            IsCalculated = false
                        });
                        return 1;
                    }
                }
                return 0;

            case ContractResult_Item item:
                {
                    var entityName = await _p4kService.GetEntityClassName(item.entityClass) ?? "Inconnu";
                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = "ContractResult_Item",
                        DisplayValue = string.Format("{0} x {1}", entityName, item.amount),
                        IsCalculated = false
                    });
                }
                return 1;

            case ContractResult_BadgeAward badgeAward:
                {
                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = "ContractResult_BadgeAward",
                        DisplayValue = badgeAward.badgeToAward.ToString(),
                        IsCalculated = false
                    });
                }
                return 1;

            case ContractResult_ScenarioProgress scenarioProgress:
                {
                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = "ContractResult_ScenarioProgress",
                        DisplayValue = string.Format("{0:N0} points", scenarioProgress.PointsToAward),
                        IsCalculated = false
                    });
                }
                return 1;

            case ContractResult_LegacyReputation legacyRep:
                {
                    var amounts = legacyRep.contractResultReputationAmounts;
                    var rewardValue = amounts?.reward?.reputationAmount ?? 0;
                    var scopeName = await _p4kService.GetLocaleValue(amounts?.reputationScope?.displayName);
                    var factionName = await _p4kService.GetLocaleValue(amounts?.factionReputation?.displayName);

                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = "ContractResult_LegacyReputation",
                        DisplayValue = string.Format("{0:N0} points de réputation {1} pour {2}", rewardValue, scopeName, factionName),
                        IsCalculated = false
                    });
                }
                return 1;

            case ContractResult_CalculatedReputation reputation:
                {
                    var scopeName = await _p4kService.GetLocaleValue(reputation.reputationScope?.displayName);
                    var factionName = await _p4kService.GetLocaleValue(reputation.factionReputation?.displayName);
                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = "ContractResult_CalculatedReputation",
                        DisplayValue = $"Réputation: {scopeName} → {factionName}",
                        IsCalculated = false
                    });
                }
                return 1;

            case ContractResult_CompletionTags completionTags:
                {
                    var tagNames = new List<string>();
                    foreach (var ct in completionTags.completionTags)
                    {
                        tagNames.Add($"'{ct.tag?.tagName}'");
                    }
                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = "ContractResult_CompletionTags",
                        DisplayValue = $"Tags: {string.Join(", ", tagNames)}",
                        IsCalculated = false
                    });
                }
                return 1;

            default:
                {
                    var type = resultBase.GetType().Name;
                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = type,
                        DisplayValue = type,
                        IsCalculated = false
                    });
                }
                return 1;
        }
    }

    /* ========================================================================
     * AUEC REWARD COMPUTATION
     * ======================================================================== */

    private async Task<float> ComputeAUECReward(ContractBase contract, ContractResult_CalculatedReward _cr)
    {
        try
        {
            if (contract.contractResults.difficulty is null)
                return 0f;

            var scDefaultRecord = await _p4kService.GetRecordWithSpecificDepth(
                new CigGuid("330ce5d3-fb01-4f82-8708-3154a3a4b78a"), 1).ConfigureAwait(false);
            var uecCurve = ((scDefaultRecord?.Data as GameMode)?.subsumptionMissionModule as SSubsumptionMission)?.uecCurve;
            if (uecCurve == null)
                return 0f;

            double i = uecCurve.i;
            double steepness = uecCurve.k;
            double midpoint = uecCurve.m;

            int mechanicalSkill = (int)contract.contractResults.difficulty.mechanicalSkill;
            int mentalLoad = (int)contract.contractResults.difficulty.mentalLoad;
            int riskOfLoss = (int)contract.contractResults.difficulty.riskOfLoss;
            int gameKnowledge = (int)contract.contractResults.difficulty.gameKnowledge;

            double mechWeight = contract.contractResults.difficulty.difficultyProfile?.mechanicalSkillWeight ?? 1.0;
            double mentalWeight = contract.contractResults.difficulty.difficultyProfile?.mentalLoadWeight ?? 1.0;
            double riskWeight = contract.contractResults.difficulty.difficultyProfile?.riskOfLossWeight ?? 1.0;
            double gameWeight = contract.contractResults.difficulty.difficultyProfile?.gameKnowledgeWeight ?? 1.0;

            double difficultyScore =
                (mechanicalSkill + 1.0) * mechWeight +
                (mentalLoad + 1.0) * mentalWeight +
                (riskOfLoss + 1.0) * riskWeight +
                (gameKnowledge + 1.0) * gameWeight;

            var timeToComplete = contract.contractResults.timeToComplete;
            double rewardRaw = Math.Exp((difficultyScore - midpoint) * steepness) * i * (timeToComplete / 60.0);
            double aUEC = Math.Round((rewardRaw / 250.0)) * 250;
            int rounded = Math.Max(0, (int)Math.Round(aUEC));
            return rounded;
        }
        catch
        {
            return 0f;
        }
    }

    /* ========================================================================
     * BLUEPRINTS
     * ======================================================================== */

    private readonly Dictionary<string, BlueprintEntity> _blueprintCache = new();

    private async Task ProcessAllBlueprintsAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var allBlueprints = await _p4kService.GetAllCraftingBlueprintRecord();
        _logger.LogInformation("Found {Count} blueprint records in P4K.", allBlueprints.Count);

        var start = Stopwatch.StartNew();
        int count = 0;

        foreach (var record in allBlueprints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.Data is not CraftingBlueprintRecord)
                continue;

            var bpId = record.RecordId.ToString();
            if (_blueprintCache.ContainsKey(bpId))
                continue;

            var blueprintEntity = await IngestBlueprintAsync(bpId, db);
            if (blueprintEntity != null)
            {
                _blueprintCache[bpId] = blueprintEntity;
                count++;
            }

            if (count % 50 == 0)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        start.Stop();
        _logger.LogInformation("Ingested {Count} new blueprints (total in cache: {CacheCount}).", count, _blueprintCache.Count);
        _logger.LogInformation("All blueprints processed in {Elapsed}ms.", start.ElapsedMilliseconds);
    }

    private async Task ProcessBlueprintPoolsAsync(dynamic blueprintPool, StarXelemDbContext db, MissionBlueprintPoolEntity poolEntity)
    {
        if (blueprintPool?.blueprintRewards == null)
            return;

        foreach (var blueprintReward in (blueprintPool.blueprintRewards as dynamic[]) ?? Array.Empty<dynamic>())
        {
            if (blueprintReward == null)
                continue;

            var bpRecord = blueprintReward.blueprintRecord;
            if (bpRecord?.selfId == null)
                continue;

            var bpId = bpRecord.selfId.ToString();
            var entry = new MissionBlueprintEntryEntity
            {
                Pool = poolEntity,
                BlueprintId = bpId,
                Weight = blueprintReward.weight
            };
            db.MissionBlueprintEntries.Add(entry);

            if (_blueprintCache.ContainsKey(bpId))
                continue;

            var blueprintEntity = await IngestBlueprintAsync(bpId, db);
            if (blueprintEntity != null)
            {
                _blueprintCache[bpId] = blueprintEntity;
            }
        }
    }

    private async Task<BlueprintEntity?> IngestBlueprintAsync(string blueprintId, StarXelemDbContext db)
    {
        var bpRecord = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(blueprintId), 4);
        var b = bpRecord?.Data as CraftingBlueprintRecord;

        if (b?.blueprint is not CraftingBlueprint craftingBlueprint)
        {
            _logger.LogWarning("Failed to get blueprint record for {BlueprintId}", blueprintId);
            return null;
        }

        var (processType, outputEntityClassRef) = ResolveProcessType(craftingBlueprint.processSpecificData);
        
        var blueprintName = await ResolveBlueprintName(craftingBlueprint, craftingBlueprint.processSpecificData);

        var blueprintEntity = new BlueprintEntity
        {
            SelfId = blueprintId,
            BlueprintName = blueprintName,
            CategoryRef = craftingBlueprint.category?.selfId.ToString() ?? "",
            CategoryName = "Unknown",
            ProcessType = processType,
            OutputEntityClassRef = outputEntityClassRef
        };

        var craftingRecipe = craftingBlueprint.tiers.OfType<CraftingBlueprintTier>().FirstOrDefault()?.recipe as CraftingRecipe;
        var costs = craftingRecipe?.costs as CraftingRecipeCosts;

        if (costs != null)
        {
            blueprintEntity.CraftDuration = ResolveCraftDuration(costs.craftTime);
            db.Blueprints.Add(blueprintEntity);
            await db.SaveChangesAsync();
            await IngestRecipeCostsAsync(blueprintEntity, costs, db);
        }
        else
        {
            db.Blueprints.Add(blueprintEntity);
            await db.SaveChangesAsync();
        }

        return blueprintEntity;
    }

    private (string ProcessType, string? OutputEntityClassRef) ResolveProcessType(object? processSpecificData)
    {
        return processSpecificData switch
        {
            CraftingProcess_Creation creation => ("Creation", creation.entityClass?.selfId.ToString()),
            CraftingProcess_Dismantle dismantle => ("Dismantle", dismantle.entityClass?.selfId.ToString()),
            CraftingProcess_Upgrade upgrade => ("Upgrade", upgrade.entityClass?.selfId.ToString()),
            CraftingProcess_Refining _ => ("Refining", null),
            CraftingProcess_Repair repair => ("Repair", repair.entityClass?.selfId.ToString()),
            _ => (string.Empty, null)
        };
    }

    private async Task<string> ResolveBlueprintName(CraftingBlueprint blueprint, object? processSpecificData)
    {
        EntityClassDefinition? entityClass = null;

        if (processSpecificData is CraftingProcess_Creation creation)
            entityClass = creation.entityClass;
        else if (processSpecificData is CraftingProcess_Dismantle dismantle)
            entityClass = dismantle.entityClass;
        else if (processSpecificData is CraftingProcess_Upgrade upgrade)
            entityClass = upgrade.entityClass;
        else if (processSpecificData is CraftingProcess_Repair repair)
            entityClass = repair.entityClass;

        if (entityClass != null)
        {
            var name = await _p4kService.GetEntityClassName(entityClass);
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        if (!string.IsNullOrEmpty(blueprint.blueprintName))
        {
            var resolved = await _p4kService.GetLocaleValue(blueprint.blueprintName);
            if (!string.IsNullOrEmpty(resolved))
                return resolved;
        }

        return "Unknown";
    }

    private static TimeSpan? ResolveCraftDuration(object? craftTime)
    {
        return craftTime switch
        {
            null => null,
            TimeValue_LongSeconds t => TimeSpan.FromSeconds(t.seconds),
            TimeValue_Partitioned t => new TimeSpan(t.days, t.hours, t.minutes, (int)t.seconds),
            _ => null
        };
    }

    private async Task IngestRecipeCostsAsync(BlueprintEntity blueprintEntity, CraftingRecipeCosts costs, StarXelemDbContext db)
    {
        var mandatoryCost = costs.mandatoryCost as CraftingCost_Select;
        if (mandatoryCost == null)
        {
            _logger.LogWarning("Mandatory cost is not CraftingCost_Select for blueprint {BlueprintId}", blueprintEntity.SelfId);
            return;
        }

        if (costs.optionalCosts != null)
        {
            ProcessOptionalCosts(blueprintEntity, costs.optionalCosts, db);
        }

        foreach (var craftingCostOption in mandatoryCost.options)
        {
            if (craftingCostOption is not CraftingCost_Select costSelect)
                continue;

            var categoryName = await _p4kService.GetLocaleValue(costSelect.nameInfo.displayName) ?? "Unknown";

            var costEntities = new List<BlueprintRecipeCostEntity>();
            foreach (var costOption in costSelect.options)
            {
                var costEntity = ProcessCostOption(blueprintEntity, costOption, categoryName, db);
                if (costEntity != null)
                    costEntities.Add(costEntity);
            }

            await db.SaveChangesAsync();

            foreach (var modifierContext in costSelect.context.OfType<CraftingCostContext_ResultGameplayPropertyModifiers>())
            {
                var modifiers = await ExtractModifierEntitiesAsync(modifierContext);
                foreach (var costEntity in costEntities)
                {
                    foreach (var modifier in modifiers)
                    {
                        modifier.CostId = costEntity.Id;
                        db.BlueprintModifiers.Add(modifier);
                    }
                }
            }
        }
    }

    private void ProcessOptionalCosts(BlueprintEntity blueprintEntity, dynamic[] optionalEntries, StarXelemDbContext db)
    {
        foreach (var opt in optionalEntries)
        {
            if (opt is not CraftingOptionalEntry optionalEntry)
                continue;

            var cost = optionalEntry.optionalCost;
            ProcessCostOption(blueprintEntity, cost, "Optional", db);
        }
    }

    private BlueprintRecipeCostEntity? ProcessCostOption(BlueprintEntity blueprintEntity, dynamic? costOption, string costName, StarXelemDbContext db)
    {
        if (costOption == null)
            return null;

        switch (costOption)
        {
            case CraftingCost_Resource resourceCost:
                {
                    float rawQuantity = (resourceCost.quantity as SStandardCargoUnit)?.standardCargoUnits ?? 0f;
                    var entity = new BlueprintRecipeCostEntity
                    {
                        BlueprintId = blueprintEntity.SelfId,
                        CostType = "Resource",
                        CostName = costName,
                        ResourceRef = resourceCost.resource?.selfId.ToString() ?? "unknown",
                        ResourceAmount = (decimal)rawQuantity
                    };
                    db.BlueprintRecipeCosts.Add(entity);
                    return entity;
                }

            case CraftingCost_Item itemCost:
                {
                    var entity = new BlueprintRecipeCostEntity
                    {
                        BlueprintId = blueprintEntity.SelfId,
                        CostType = "Item",
                        CostName = costName,
                        ItemEntityClassRef = itemCost.entityClass?.selfId.ToString() ?? "unknown",
                        ItemCount = itemCost.quantity,
                        MinQuality = itemCost.minQuality
                    };
                    db.BlueprintRecipeCosts.Add(entity);
                    return entity;
                }

            default:
                _logger.Log(LogLevel.Warning, 0, "Unknown cost option type for blueprint {BlueprintId}: {Type}", 
                    new object?[] { blueprintEntity.SelfId, costOption.GetType().FullName }, null, default);
                return null;
        }
    }

    private async Task<List<BlueprintModifierEntity>> ExtractModifierEntitiesAsync(
        CraftingCostContext_ResultGameplayPropertyModifiers modifierContext)
    {
        var modifiers = new List<BlueprintModifierEntity>();

        var modifierList = modifierContext.gameplayPropertyModifiers as CraftingGameplayPropertyModifiers_List;
        if (modifierList?.gameplayPropertyModifiers == null)
            return modifiers;

        foreach (var rawModifier in modifierList.gameplayPropertyModifiers)
        {
            var modifier = rawModifier as CraftingGameplayPropertyModifierCommon;
            if (modifier == null)
            {
                _logger.LogWarning("Modifier not castable: {Type}", rawModifier?.GetType().FullName);
                continue;
            }

            var propertyName = await _p4kService.GetLocaleValue(modifier.gameplayPropertyRecord?.propertyName);

            var linearRanges = modifier.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_Linear>().ToList();
            if (linearRanges is { Count: > 0 })
            {
                foreach (var range in linearRanges)
                {
                    modifiers.Add(new BlueprintModifierEntity
                    {
                        RangeType = "Linear",
                        PropertyName = propertyName ?? string.Empty,
                        StartQuality = range.startQuality,
                        EndQuality = range.endQuality,
                        ModifierStart = (decimal)range.modifierAtStart,
                        ModifierEnd = (decimal)range.modifierAtEnd
                    });
                }
            }
            else
            {
                var additiveRanges = modifier.valueRanges
                    .OfType<CraftingGameplayPropertyModifierValueRange_LinearIntegerAdditive>().ToList();
                if (additiveRanges is { Count: > 0 })
                {
                    foreach (var range in additiveRanges)
                    {
                        modifiers.Add(new BlueprintModifierEntity
                        {
                            RangeType = "Additive",
                            PropertyName = propertyName ?? string.Empty,
                            StartQuality = range.startQuality,
                            EndQuality = range.endQuality,
                            ModifierStart = (decimal)range.additiveModifierAtStart,
                            ModifierEnd = (decimal)range.additiveModifierAtEnd
                        });
                    }
                }
                else
                {
                    _logger.LogWarning("No recognized modifier range for property {Name}", propertyName);
                    continue;
                }
            }
        }

        return modifiers;
    }

    /* ========================================================================
     * TAG RESOLUTION
     * ======================================================================== */

    private string? ResolveTag(object? tag, Dictionary<string, string> map)
    {
        if (tag == null) return null;
        
        var type = tag.GetType();
        
        var idProp = type.GetProperty("RecordId");
        if (idProp != null)
        {
            var id = idProp.GetValue(tag)?.ToString();
            if (id != null && map.ContainsKey(id))
            {
                return id;
            }
        }

        var selfIdProp = type.GetProperty("selfId");
        if (selfIdProp != null)
        {
            var id = selfIdProp.GetValue(tag)?.ToString();
            if (id != null && map.ContainsKey(id))
            {
                return id;
            }
        }
        
        return null;
    }

    /* ========================================================================
     * QUERY METHODS
     * ======================================================================== */

    public async Task<List<MissionEntity>> GetMissionsForShipAsync(string shipGuid)
    {
        using var db = new StarXelemDbContext(GetOptions());
        return await db.Missions
            .Where(m => m.ShipRequirements.Any(sr => sr.ShipGuid == shipGuid))
            .ToListAsync();
    }

    public async Task<List<ShipEntity>> GetShipsForMissionAsync(string missionId)
    {
        using var db = new StarXelemDbContext(GetOptions());
        return await db.Ships
            .Where(s => s.MissionRequirements.Any(mr => mr.MissionId == missionId))
            .ToListAsync();
    }

    public async Task<List<DbBlueprintRow>> GetBlueprintsAsync(HashSet<string>? obtainedBlueprintIds = null)
    {
        await using var db = new StarXelemDbContext(GetOptions());

        var blueprints = await db.Blueprints
            .OrderBy(b => b.BlueprintName) 
            .ToListAsync()
            .ConfigureAwait(false);

        var result = new List<DbBlueprintRow>();

        foreach (var bp in blueprints)
        {
            var costs = await db.BlueprintRecipeCosts
                .Where(c => c.BlueprintId == bp.SelfId)
                .ToListAsync()
                .ConfigureAwait(false);

            var costRows = new List<DbBlueprintCostRow>();
            foreach (var cost in costs)
            {
                var modifiers = await db.BlueprintModifiers
                    .Where(m => m.CostId == cost.Id)
                    .ToListAsync()
                    .ConfigureAwait(false);

                costRows.Add(new DbBlueprintCostRow(
                    cost.CostType,
                    cost.CostName,
                    cost.ResourceRef,
                    cost.ResourceAmount,
                    cost.ItemEntityClassRef,
                    cost.ItemCount,
                    cost.MinQuality,
                    modifiers.Select(m => new DbBlueprintModifierRow(
                        m.RangeType,
                        m.PropertyName,
                        m.StartQuality,
                        m.EndQuality,
                        m.ModifierStart,
                        m.ModifierEnd
                    )).ToArray()
                ));
            }

            var missionPools = await db.MissionBlueprintPools
                .Where(mp => mp.Entries.Any(e => e.BlueprintId == bp.SelfId))
                .Join(db.Missions,
                    mp => mp.MissionId,
                    m => m.Id,
                    (mp, m) => new DbMissionPoolRow(mp.PoolName, m.Title, m.DebugName))
                .ToListAsync()
                .ConfigureAwait(false);

            result.Add(new DbBlueprintRow(
                bp.SelfId,
                bp.BlueprintName,
                bp.CategoryName,
                bp.ProcessType,
                bp.OutputEntityClassRef,
                bp.CraftDuration,
                costRows.ToArray(),
                missionPools.ToArray()
            ));
        }

        return result;
    }

    private static string? StripRecordPrefix(string? recordName)
    {
        if (string.IsNullOrEmpty(recordName)) return null;
        var dotIndex = recordName.IndexOf('.');
        return dotIndex >= 0 ? recordName.Substring(dotIndex + 1) : recordName;
    }

    /// <summary>
    /// Construit les cartes de suffixe de titre et d'appendice de description pour les missions comportant des récompenses Blueprint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La méthode interroge la base de données pour récupérer l'ensemble des missions possédant au moins un pool Blueprint,
    /// puis génère deux dictionnaires destinés à l'export du fichier de localisation <c>global.ini</c>.
    /// </para>
    /// <para>
    /// <b>TitleSuffixMap</b> — clé INI du titre (sans préfixe <c>@</c>) → suffixe à apposer au titre.
    /// <list type="bullet">
    /// <item><term>Sans suivi d'obtention :</term><description><c>[N BP]</c> (N = nombre total d'entrées BP du contrat)</description></item>
    /// <item><term>Avec suivi d'obtention :</term><description><c>[non_obtenus/total BP]</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>DescriptionAppendMap</b> — clé INI de description (sans préfixe <c>@</c>) → dictionnaire pool → ensemble de noms BP.
    /// Chaque pool produit un bloc <c>\n\n&lt;EM3&gt;**PoolName**&lt;/EM3&gt;</c> suivi d'une liste <c>\n- BPName</c> pour chaque Blueprint.
    /// Lorsqu'un BP n'a pas encore été obtenu par le joueur, son nom est entouré de <c>&lt;EM4&gt;...&lt;/EM4&gt;</c> pour le mettre en évidence visuellement.
    /// </para>
    /// <para>
    /// Le paramètre <c>obtainedBlueprintIds</c> permet de fournir l'ensemble des identifiants Blueprint déjà détenus par le joueur
    /// (issus de l'API gRPC <c>BlueprintLibrary</c>). Chaque identifiant correspond au <c>SelfId</c> d'un enregistrement
    /// <c>CraftingBlueprintRecord</c> dans le P4K. Lorsqu'il est <c>null</c>, le mode de suivi d'obtention est désactivé :
    /// les titres affichent simplement le total et aucun BP n'est entouré de balises <c>&lt;EM4&gt;</c>.
    /// </para>
    /// <para>
    /// <b>Exemple de sortie dans le fichier INI :</b>
    /// </para>
    /// <code>
    /// Mission_Title_X = Assaut sur la base pirate [2/5 BP]
    /// Mission_Desc_X = Éliminez les cibles...\n\n&lt;EM3&gt;**Récompenses**&lt;/EM3&gt;\n- &lt;EM4&gt;Plasma Rifle&lt;/EM4&gt;\n- Fusioncutter MK2
    /// </code>
    /// <para>
    /// Dans cet exemple, le joueur possède déjà 3 des 5 Blueprint récompensés par ce contrat.
    /// Le Plasma Rifle n'a pas encore été obtenu et apparaît entouré de <c>&lt;EM4&gt;</c>.
    /// </para>
    /// </remarks>
    /// <param name="obtainedBlueprintIds">
    /// Ensemble des identifiants Blueprint déjà obtenus par le joueur (gRPC <c>BlueprintEntry.BlueprintId</c>).
    /// <c>null</c> pour désactiver le suivi d'obtention.
    /// </param>
    /// <returns>
    /// Un tuple contenant :
    /// <list type="table">
    /// <item><term><c>TitleSuffixMap</c></term><description>Mappage clé titre → suffixe <c>[x/y BP]</c> ou <c>[y BP]</c></description></item>
    /// <item><term><c>DescriptionAppendMap</c></term><description>Mappage clé description → pool → ensemble de noms BP (avec ou sans balises <c>&lt;EM4&gt;</c>)</description></item>
    /// </list>
    /// </returns>
    public async Task<(Dictionary<string, string> TitleSuffixMap, Dictionary<string, Dictionary<string, HashSet<string>>> DescriptionAppendMap)> GetBlueprintRewardMapsAsync(HashSet<string>? obtainedBlueprintIds = null)
    {
        await using var db = new StarXelemDbContext(GetOptions());
        var titleSuffixMap = new Dictionary<string, string>();
        var descAppendMap = new Dictionary<string, Dictionary<string, HashSet<string>>>();

        var missions = await db.Missions
            .Where(m => m.NotForRelease == false && m.WorkInProgress == false && m.Generator!.NotForRelease == false && m.Generator.WorkInProgress == false)
            .Include(m => m.BlueprintPools)
            .ThenInclude(bp => bp.Entries)
            .ThenInclude(bp => bp.Blueprint)
            .Where(m => m.BlueprintPools.Any())
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var missionEntity in missions)
        {
            var titleKey = missionEntity.TitleKey!.Substring(1);
            var descKey = missionEntity.DescriptionKey!.Substring(1);

            if (titleSuffixMap.ContainsKey(titleKey))
            {
                _logger.LogWarning("Duplicate title key for BP: {Key}", titleKey);
            }
            else
            {
                var totalBp = missionEntity.BlueprintPools.Sum(bp => bp.Entries.Count);
                if (obtainedBlueprintIds != null)
                {
                    var obtainedCount = 0;
                    foreach (var bpPool in missionEntity.BlueprintPools)
                    {
                        foreach (var bpEntry in bpPool.Entries)
                        {
                            var bpId = bpEntry.Blueprint?.SelfId;
                            if (bpId != null && obtainedBlueprintIds.Contains(bpId))
                            {
                                obtainedCount++;
                            }
                        }
                    }
                    var unobtained = totalBp - obtainedCount;
                    titleSuffixMap[titleKey] = $"[{unobtained}/{totalBp} BP]";
                }
                else
                {
                    titleSuffixMap[titleKey] = $"[{totalBp} BP]";
                }
            }

            if (descAppendMap.ContainsKey(descKey))
            {
                _logger.LogWarning("Duplicate description key for BP: {Key}", descKey);
            }
            else
            {
                var poolDic = new Dictionary<string, HashSet<string>>();
                descAppendMap.Add(descKey, poolDic);
                foreach (var bpPool in missionEntity.BlueprintPools)
                {
                    var pool = new HashSet<string>();
                    foreach (var bpEntry in bpPool.Entries)
                    {
                        var bpName = bpEntry.Blueprint!.BlueprintName;
                        var bpId = bpEntry.Blueprint.SelfId;
                        if (obtainedBlueprintIds != null && !obtainedBlueprintIds.Contains(bpId))
                        {
                            bpName = $"<EM4>{bpName}</EM4>";
                        }
                        pool.Add(bpName);
                    }
                    poolDic.Add(bpPool.PoolName, pool);
                }
            }
        }

        return (titleSuffixMap, descAppendMap);
    }
}
