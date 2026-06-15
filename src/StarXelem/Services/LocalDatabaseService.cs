using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;

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
            .Options;
    }

    private readonly Dictionary<EntityClassDefinition, string> _entityClassToGuid = new();

    public async Task RebuildDbAsync()
    {
        _logger.LogInformation("Rebuilding local database at {Path}", _dbPath);
        _entityClassToGuid.Clear();

        using var db = new StarXelemDbContext(GetOptions());
        
        // 1. Wipe existing data
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // 2. Populate Manufacturers & Ships
        var ships = new List<ShipEntity>();
        var manufacturers = new List<ManufacturerEntity>();
        var manufacturerCache = new Dictionary<string, ManufacturerEntity>();
        var tagCache = new Dictionary<string, TagEntity>();
        var shipTags = new List<ShipTagEntity>();

        await foreach (var record in _p4kService.GetAllEntityClassDefinition(1))
        {
            if (record.Data is EntityClassDefinition entityClass)
            {
                var vehicleParams = entityClass.Components.OfType<VehicleComponentParams>().FirstOrDefault();
                if (vehicleParams == null) continue;

                var guid = record.RecordId.ToString();
                _entityClassToGuid[entityClass] = guid;

                // Handle Tags
                var eaEntityDataParams = entityClass.StaticEntityClassData.OfType<EAEntityDataParams>().FirstOrDefault();
                var extractedTags = (entityClass.tags?.Where(t => null != t?.tagName).Select(t => t.tagName) ?? Enumerable.Empty<string>())
                    .Concat(eaEntityDataParams?.inclusionParams.tags.tags.Where(t => null != t).Select(t => t.tagName) ?? Enumerable.Empty<string>())
                    .Distinct();

                foreach (var tagName in extractedTags)
                {
                    if (!tagCache.TryGetValue(tagName, out var tagEntity))
                    {
                        tagEntity = new TagEntity { Name = tagName };
                        tagCache[tagName] = tagEntity;
                    }
                    shipTags.Add(new ShipTagEntity { ShipGuid = guid, TagName = tagName });
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
        db.Manufacturers.AddRange(manufacturers);
        db.Ships.AddRange(ships);
        db.Tags.AddRange(tagCache.Values);
        db.ShipTags.AddRange(shipTags);
        await db.SaveChangesAsync();


        // 3. Populate Missions & Requirements
        var contracts = await _p4kService.GetAllContractGenerator();
        foreach (var record in contracts)
        {
            await ProcessContractForDb(record, db);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Database rebuild completed.");
    }

    private async Task ProcessContractForDb(DataCoreTypedRecord record, StarXelemDbContext db)
    {
        if (record.Data is not ContractGeneratorHandlerBase contract) return;

        var contractsToProcess = new List<ContractBase>();
        
        if (contract is ContractGeneratorHandler_Career career)
        {
            contractsToProcess.AddRange(career.introContracts.Cast<ContractBase>());
            contractsToProcess.AddRange(career.contracts.Cast<ContractBase>());
        }
        else if (contract is ContractGeneratorHandler_List list)
        {
            contractsToProcess.AddRange(list.contracts.Cast<ContractBase>());
        }
        else return;

        foreach (var c in contractsToProcess)
        {
            if (c == null) continue;

            var mission = new MissionEntity
            {
                DebugName = c.debugName,
                Title = await _p4kService.GetLocaleValue(
                    c.paramOverrides?.stringParamOverrides?.FirstOrDefault(p => p.param == ContractStringParamType.Title)?.value 
                    ?? c.template?.contractDisplayInfo?.displayString[0]) ?? "Unknown"
            };

            db.Missions.Add(mission);

            var shipDefs = ExtractShipDefs(c);
            foreach (var def in shipDefs)
            {
                if (_entityClassToGuid.TryGetValue(def, out var guid))
                {
                    db.MissionShipRequirements.Add(new MissionShipRequirementEntity
                    {
                        MissionDebugName = mission.DebugName,
                        ShipGuid = guid
                    });
                }
            }
        }
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

    public async Task<List<ShipEntity>> GetShipsForMissionAsync(string missionDebugName)
    {
        using var db = new StarXelemDbContext(GetOptions());
        return await db.Ships
            .Where(s => s.MissionRequirements.Any(mr => mr.MissionDebugName == missionDebugName))
            .ToListAsync();
    }
}
