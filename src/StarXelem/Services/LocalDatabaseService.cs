using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using System.Xml.Linq;

namespace StarXelem.Services;

public interface ILocalDatabaseService
{
    Task RebuildDbAsync();
    Task<List<MissionEntity>> GetMissionsForShipAsync(string shipGuid);
    Task<List<ShipEntity>> GetShipsForMissionAsync(string missionDebugName);
}

public class LocalDatabaseService : ILocalDatabaseService
{
    private readonly IP4kService _p4kService;
    private readonly ILogger<LocalDatabaseService> _logger;
    private readonly string _dbPath;

    public LocalDatabaseService(IP4kService p4kService, ILogger<LocalDatabaseService> logger)
    {
        _p4kService = p4kService;
        _logger = logger;
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "StarXelem");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        _dbPath = Path.Combine(folder, "database.db");

        if (_p4kService is P4kService p4k)
        {
            p4k.SelectedP4KFileChanged += async (s, e) => await RebuildDbAsync();
        }
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

    public async Task RebuildDbAsync()
    {
        _logger.LogInformation("Rebuilding local database at {Path}", _dbPath);
        _entityClassToGuid.Clear();

        using var db = new StarXelemDbContext(GetOptions());
        
        // 1. Wipe existing data
        await db.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        
        // 2. Populate Manufacturers & Ships
        var ships = new List<ShipEntity>();
        var manufacturers = new List<ManufacturerEntity>();
        var manufacturerCache = new Dictionary<string, ManufacturerEntity>();
        var shipTags = new List<ShipTagEntity>();
        var tagResolutionMap = new Dictionary<string, string>();
        
        var start = Stopwatch.StartNew();
        
        await PopulateTagHierarchyAsync(db, tagResolutionMap).ConfigureAwait(false);
        
        start.Stop();
        _logger.LogInformation("Tag hierarchy populated in {Elapsed}ms.", start.ElapsedMilliseconds);
        
        start = Stopwatch.StartNew();
        await foreach (var record in _p4kService.GetAllEntityClassDefinition(1).ConfigureAwait(false))
        {
            if (record.Data is EntityClassDefinition entityClass)
            {
                var vehicleParams = entityClass.Components.OfType<VehicleComponentParams>().FirstOrDefault();
                if (vehicleParams == null) continue;
                
                var guid = record.RecordId.ToString();
                _entityClassToGuid[entityClass] = guid;
                
                // Handle Tags
                var eaEntityDataParams = entityClass.StaticEntityClassData.OfType<EAEntityDataParams>().FirstOrDefault();
                var extractedTags = (entityClass.tags?
                    .Select(t => ResolveTag(t, tagResolutionMap))
                    .Where(name => !string.IsNullOrEmpty(name)) ?? Enumerable.Empty<string>())
                    .Concat(eaEntityDataParams?.inclusionParams.tags.tags?
                    .Select(t => ResolveTag(t, tagResolutionMap))
                    .Where(name => !string.IsNullOrEmpty(name)) ?? Enumerable.Empty<string>())
                    .Distinct();
                
                        foreach (var id in extractedTags)
                        {
                            if (tagResolutionMap.TryGetValue(id, out var name))
                            {
                                shipTags.Add(new ShipTagEntity { ShipGuid = guid, TagSelfId = id });
                            }
                        }
                
                // Handle Manufacturer
                var manufacturerId = "Unknown";
                if (vehicleParams.manufacturer != null)
                {
                    var manufacturer = vehicleParams.manufacturer;
                    // Use Code as the unique identifier if available, otherwise fallback to Localization Name
                    manufacturerId = !string.IsNullOrEmpty(manufacturer.Code) 
                                        ? manufacturer.Code 
                                        : (!string.IsNullOrEmpty(manufacturer.Localization.Name) ? manufacturer.Localization.Name : "Unknown");
        
                    if (manufacturerId != "Unknown")
                    {
                        if (!manufacturerCache.TryGetValue(manufacturerId, out var manufacturerEntity))
                        {
                            var nameKey = !string.IsNullOrEmpty(manufacturer.Localization.Name) 
                                            ? manufacturer.Localization.Name 
                                            : manufacturerId;
                            var descKey = !string.IsNullOrEmpty(manufacturer.Localization.Description) 
                                            ? manufacturer.Localization.Description 
                                            : string.Empty;
        
                            manufacturerEntity = new ManufacturerEntity
                            {
                                Id = manufacturerId,
                                Name = await _p4kService.GetLocaleValue(nameKey) ?? manufacturerId,
                                NameKey = nameKey,
                                Description = await _p4kService.GetLocaleValue(descKey) ?? string.Empty,
                                DescriptionKey = descKey,
                                Logo = manufacturer.Logo ?? string.Empty
                            };
                            manufacturerCache[manufacturerId] = manufacturerEntity;
                            manufacturers.Add(manufacturerEntity);
                        }
                    }
                    else
                    {
                        if (!manufacturerCache.TryGetValue("Unknown", out var unknownManufacturer))
                        {
                            unknownManufacturer = new ManufacturerEntity { Id = "Unknown", Name = "Unknown" };
                            manufacturerCache["Unknown"] = unknownManufacturer;
                            manufacturers.Add(unknownManufacturer);
                        }
                    }
                }
                else
                {
                    if (!manufacturerCache.TryGetValue("Unknown", out var unknownManufacturer))
                    {
                        unknownManufacturer = new ManufacturerEntity { Id = "Unknown", Name = "Unknown" };
                        manufacturerCache["Unknown"] = unknownManufacturer;
                        manufacturers.Add(unknownManufacturer);
                    }
                    manufacturerId = "Unknown";
                }
        
                ships.Add(new ShipEntity
                {
                    EntityClassGuid = guid,
                    TechnicalName = record.RecordName,
                    LocalizedName = await _p4kService.GetEntityClassName(entityClass) ?? "Unknown",
                    ManufacturerId = manufacturerId
                });
            }
        }
        start.Stop();
        _logger.LogInformation("Ships and manufacturers processed in {Elapsed}ms.", start.ElapsedMilliseconds);
        db.Manufacturers.AddRange(manufacturers);
        db.Ships.AddRange(ships);
        await db.SaveChangesAsync().ConfigureAwait(false);
        _logger.LogInformation("Inserted {Count} manufacturer into the database.", manufacturers.Count);
        _logger.LogInformation("Inserted {Count} ship into the database.", ships.Count);
        db.ShipTags.AddRange(shipTags);
        await db.SaveChangesAsync().ConfigureAwait(false);
        _logger.LogInformation("Inserted {Count} liaison ship <=> tag into the database.", shipTags.Count);
        
        // 3. Populate Contract Generators
        start = Stopwatch.StartNew();
        var contracts = await _p4kService.GetAllContractGenerator();
        _logger.LogInformation("Found {Count} contract generators. Ensuring depth 3...", contracts.Count);
        contracts = await _p4kService.EnsureRecordsDepthAsync(contracts, 3);

        var contractGenerators = new List<ContractGeneratorEntity>();
        foreach (var record in contracts)
        {
            if (record.Data is not ContractGenerator generator) continue;
            if (generator.generators == null) continue;

            int handlerIndex = 0;
            foreach (var handler in generator.generators)
            {
                if (handler == null) continue;
                var avail = handler.defaultAvailability;

                bool ToBool(dynamic? v, bool @default = false)
                {
                    if (v == null) return @default;
                    return (bool)v;
                }

                contractGenerators.Add(new ContractGeneratorEntity
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
                });
                handlerIndex++;
            }
        }

        db.ContractGenerators.AddRange(contractGenerators);
        await db.SaveChangesAsync();
        start.Stop();
        _logger.LogInformation("Inserted {Count} contract generators into the database.", contractGenerators.Count);

        // 4. Populate Missions & Requirements
        start = Stopwatch.StartNew();
        int missionCount = 0;
        foreach (var record in contracts)
        {
            missionCount += await ProcessContractForDb(record, db, record.RecordName);
        }
        start.Stop();
        _logger.LogInformation("Missions and requirements processed in {Elapsed}ms.", start.ElapsedMilliseconds);
        _logger.LogInformation("Inserted {Count} missions into the database.", missionCount);
        await db.SaveChangesAsync();

        start = Stopwatch.StartNew();
        await ProcessMissionShipSpawnShipsAsync(db);
        start.Stop();
        _logger.LogInformation("Mission spawn rules processed in {Elapsed}ms.", start.ElapsedMilliseconds);
        await db.SaveChangesAsync();
        _logger.LogInformation("Database rebuild completed.");
    }

    private async Task<int> ProcessContractForDb(DataCoreTypedRecord record, StarXelemDbContext db, string generatorName)
    {
        if (record.Data is not ContractGenerator generator || generator.generators == null) return 0;
        int missionsAdded = 0;
        int handlerIndex = 0;
        
        foreach (var handler in generator.generators)
        {
            if (handler == null) continue;

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
            else continue;

            foreach (var c in contractsToProcess)
            {
                if (c == null) continue;

                bool notForRelease = (bool)c.notForRelease;
                bool workInProgress = (bool)c.workInProgress;

                var mission = new MissionEntity
                {
                    Id = c.id.ToString(),
                    DebugName = c.debugName,
                    GeneratorName = generatorName,
                    Title = await _p4kService.GetLocaleValue(
                        c.paramOverrides?.stringParamOverrides?.FirstOrDefault(p => p.param == ContractStringParamType.Title)?.value 
                        ?? c.template?.contractDisplayInfo?.displayString[0]) ?? "Unknown",
                    NotForRelease = notForRelease,
                    WorkInProgress = workInProgress,
                    GeneratorId = $"{generator.selfId}-{handlerIndex}"
                };

                db.Missions.Add(mission);
                missionsAdded++;

                var shipDefs = ExtractShipDefs(c);
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

                await ProcessSpawnableShipsAsync(c, db, mission.Id);
            }
            
            handlerIndex++;
        }
        return missionsAdded;
    }

    private async Task ProcessSpawnableShipsAsync(ContractBase contract, StarXelemDbContext db, string missionId)
    {
        var tempRules = new List<(MissionShipSpawnEntity Rule, List<string> PosTags, List<string> NegTags)>();
        
        var allProperties = new Dictionary<string, MissionProperty>();

        // Collect properties from template
        if (contract.template?.contractProperties != null)
        {
            foreach (var prop in contract.template.contractProperties)
            {
                allProperties[prop.missionVariableName] = prop;
            }
        }

        // Overwrite with property overrides if they exist
        foreach (var overrideProp in contract.paramOverrides.propertyOverrides)
        {
            allProperties[overrideProp.missionVariableName] = overrideProp;
        }

        foreach (var prop in allProperties.Values)
        {
            var value = prop.value as MissionPropertyValue_ShipSpawnDescriptions;
            
            if (value?.spawnDescriptions != null)
            {
                foreach (var group in value.spawnDescriptions)
                {
                    if (group.ships != null)
                    {
                        foreach (var shipOption in group.ships)
                        {
                            if (shipOption.options != null)
                            {
                                foreach (dynamic ship in shipOption.options)
                                {
                                    var posIds = new List<string>();
                                    var tagsProp = ship.GetType().GetProperty("tags");
                                    if (tagsProp != null)
                                    {
                                        var tags = tagsProp.GetValue(ship);
                                        if (tags is StarBreaker.DataCoreGenerated.TagList tagList)
                                        {
                                            foreach (var t in tagList.@tags)
                                            {
                                                if (t != null) posIds.Add(t.selfId.ToString());
                                            }
                                        }
                                        else if (tags != null)
                                        {
                                            foreach (dynamic t in tags)
                                            {
                                                if (t != null) posIds.Add(t.selfId.ToString());
                                            }
                                        }
                                    }
                                    
                                    var negIds = new List<string>();
                                    var negTagsProp = ship.GetType().GetProperty("negativeTags");
                                    if (negTagsProp != null)
                                    {
                                        var negTagsObj = negTagsProp.GetValue(ship);
                                        if (negTagsObj is StarBreaker.DataCoreGenerated.TagList negTagList)
                                        {
                                            foreach (var t in negTagList.@tags)
                                            {
                                                if (t != null) negIds.Add(t.selfId.ToString());
                                            }
                                        }
                                        else if (negTagsObj != null)
                                        {
                                            foreach (dynamic t in negTagsObj)
                                            {
                                                if (t != null) negIds.Add(t.selfId.ToString());
                                            }
                                        }
                                    }
                                    
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

    private async Task ProcessMissionShipSpawnShipsAsync(StarXelemDbContext db)
    {
        long toto = 0;
        string[] excludedPath = ["/Spawning/", "/SkillDefinitions/", "/CargoManifest/", "/CrewManifest/"];
        
        var shipSpawnRules = db.MissionShipSpawns
            .Include(s => s.Tags)
            .ThenInclude(st => st.Tag)
            .ToAsyncEnumerable();

        await foreach (var spawnRule in shipSpawnRules.ConfigureAwait(false))
        {
            List<String> posTags = spawnRule.Tags.Where(t => t.IsIncluded).Select(t => t.Tag!).Where(t => !excludedPath.Any(ep => t.Path.Contains(ep))).Select(t => t.SelfId).ToList();
            List<String> negTags = spawnRule.Tags.Where(t => !t.IsIncluded).Select(t => t.Tag!.SelfId).ToList();;
            // Filtrer les tags à filtrer
            // Récupérer les vaisseaux
            // Par défaut les vaisseaux qui ne possèdent pas les tags negatifs
            var shipsQuery = db.Ships.Where(s => !negTags.Any(t => s.ShipTags.Any(st => st.Tag!.SelfId == t)));

            if (posTags.Any())
            {
                // Si au moins un tag positif, tous les tags doivent être présents
                shipsQuery = shipsQuery.Where(s => posTags.All(t => s.ShipTags.Any(st => st.Tag!.SelfId == t)));
            }

            await foreach (var ship in shipsQuery.ToAsyncEnumerable().ConfigureAwait(false))
            {
                // On a la liste des vaisseaux elligibles, on y va !
                db.MissionShipSpawnShips.Add(new MissionShipSpawnShipEntity
                {
                    SpawnRule = spawnRule,
                    Ship = ship
                });
                ++toto;
            }
        }
        
        _logger.LogInformation("Processed {Count} spawn rules.", toto);
    }
    
    private List<EntityClassDefinition> ExtractShipDefs(ContractBase contract)
    {
        var defs = new HashSet<EntityClassDefinition>();
        if (contract.template?.objectiveTokens == null) return defs.ToList();

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
                var haulingProperty = contract.template?.contractProperties.FirstOrDefault(p => p.value is MissionPropertyValue_HaulingOrders);
                if (haulingProperty == null) break;

                var propertyKey = haulingProperty.missionVariableName;
                var overrideProp = contract.paramOverrides.propertyOverrides.FirstOrDefault(p => p.missionVariableName == propertyKey);

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

    private async Task PopulateTagHierarchyAsync(StarXelemDbContext db, Dictionary<string, string> map)
    {
        var database = await _p4kService.GetTagDatabase();
        
        try
        {
            var tagEntities = new List<TagEntity>();

            // We need to process tags in a way that we can resolve parents.
            // Since the XML is nested, we can use a recursive function to build the hierarchy.
            
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
            
            _logger.LogInformation("Populated {Count} tags into database and resolution map.", tagEntities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate tag hierarchy from P4K");
        }
    }
    
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
}
