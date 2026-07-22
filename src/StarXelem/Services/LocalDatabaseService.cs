using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using StarXelem.Models;
using System.Runtime.InteropServices;
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
    string? ResourceName,
    string? ItemName,
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

public record RebuildProgress(int CurrentPhase, int TotalPhases, string PhaseName);

public interface ILocalDatabaseService
{
    Task RebuildDbAsync(IProgress<RebuildProgress>? progress = null);
    Task<bool> NeedsRebuildCheckAsync();
    Task EnsureDbAsync(IProgress<RebuildProgress>? progress = null);
    Task<List<MissionEntity>> GetMissionsForShipAsync(string shipGuid);
    Task<List<ShipEntity>> GetShipsForMissionAsync(string missionDebugName);
    Task<(Dictionary<string, string> TitleSuffixMap, Dictionary<string, Dictionary<string, HashSet<string>>> DescriptionAppendMap)> GetBlueprintRewardMapsAsync(HashSet<string>? obtainedBlueprintIds = null);
    Task<List<DbBlueprintRow>> GetBlueprintsAsync(HashSet<string>? obtainedBlueprintIds = null);
    IAsyncEnumerable<DbBlueprintRow> GetBlueprintsBatchedAsync(int batchSize = 200, CancellationToken cancellationToken = default);
    Task<ShipEntity?> GetShipByGuidAsync(string entityClassGuid);
    Task<List<ManufacturerEntity>> GetManufacturersAsync();
    Task<List<ShipLoadoutEntryEntity>> GetShipLoadoutAsync(string shipGuid);
    Task<List<ShipEntity>> GetShipsAsync();
    Task<Dictionary<string, List<MissionEntity>>> GetAllMissionCategoriesWithMissionsAsync();
}

public class LocalDatabaseService : ILocalDatabaseService
{
    private readonly IP4kService _p4kService;
    private readonly ILogger<LocalDatabaseService> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IDbContextFactory _factory;
    private CancellationTokenSource _rebuildCts = new();
    private Task? _rebuildTask;
    private readonly Dictionary<string, ActorEntity> _contractorCache;
    private readonly Dictionary<string, MissionCategoryEntity> _categoryCache;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public LocalDatabaseService(IP4kService p4kService, ILogger<LocalDatabaseService> logger, ISettingsService settingsService, IDbContextFactory factory, bool autoRebuild = false)
    {
        _p4kService = p4kService;
        _logger = logger;
        _settingsService = settingsService;
        _factory = factory;
        _contractorCache = new Dictionary<string, ActorEntity>(StringComparer.Ordinal);
        _categoryCache = new Dictionary<string, MissionCategoryEntity>(StringComparer.Ordinal);

        if (autoRebuild && _p4kService is P4kService p4k)
        {
            p4k.SelectedP4KFileChanged += async (s, e) => await RebuildDbAsync();
        }
    }

    public async Task<bool> NeedsRebuildCheckAsync()
    {
        string? currentP4kVersion = _p4kService.SelectedP4KFile?.Manifest?.Data?.RequestedP4ChangeNum;
        string? storedP4kVersion = await _settingsService.GetAsync("P4KVersion").ConfigureAwait(false);
        return !File.Exists(_factory.DbPath) || currentP4kVersion != storedP4kVersion;
    }

    public async Task EnsureDbAsync(IProgress<RebuildProgress>? progress = null)
    {
        if (!await NeedsRebuildCheckAsync().ConfigureAwait(false))
        {
            _logger.LogInformation("Database already exists at {Path} (P4K version: {Version})", _factory.DbPath, _p4kService.SelectedP4KFile?.Manifest?.Data?.RequestedP4ChangeNum);
            return;
        }

        string? storedP4kVersion = await _settingsService.GetAsync("P4KVersion").ConfigureAwait(false);
        _logger.LogInformation("Database rebuild needed. Stored P4K: {Stored}, Current P4K: {Current}", storedP4kVersion, _p4kService.SelectedP4KFile?.Manifest?.Data?.RequestedP4ChangeNum);
        await RebuildDbAsync(progress).ConfigureAwait(false);
        
        string? currentP4kVersion = _p4kService.SelectedP4KFile?.Manifest?.Data?.RequestedP4ChangeNum;
        if (!string.IsNullOrEmpty(currentP4kVersion))
        {
            await _settingsService.SetAsync("P4KVersion", currentP4kVersion).ConfigureAwait(false);
        }
    }

    private static readonly Regex MissionTokenRegex = new(@"~mission\(([^)]+)\)", RegexOptions.Compiled);
    private readonly Dictionary<EntityClassDefinition, string> _entityClassToGuid = new();
    private Dictionary<string, string> _itemNamesCache = new();
    private Dictionary<string, CigGuid> _componentGuidMap = new();

    /* ========================================================================
     * REBUILD ORCHESTRATION
     * ======================================================================== */

    public async Task RebuildDbAsync(IProgress<RebuildProgress>? progress = null)
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

        _rebuildTask = RebuildDbCoreAsync(cancellationToken, progress);
        try
        {
            await _rebuildTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Reconstruction de la BDD annulée");
        }
    }

    private async Task RebuildDbCoreAsync(CancellationToken cancellationToken, IProgress<RebuildProgress>? progress)
    {
        await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var total = Stopwatch.StartNew();
            const int TotalPhases = 10;
            _logger.LogInformation("Rebuilding local database at {Path}", _factory.DbPath);
            _entityClassToGuid.Clear();
            _contractorCache.Clear();
            _categoryCache.Clear();
            _componentGuidMap.Clear();

            using var db = await _factory.CreateDbContextAsync();

        await db.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // Phase 1: Locale entries
        progress?.Report(new RebuildProgress(1, TotalPhases, "Chargement des locales…"));
        var phase = Stopwatch.StartNew();
        await PopulateLocaleAsync(db, cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 1/{Total}] Locale completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: Tag hierarchy
        progress?.Report(new RebuildProgress(2, TotalPhases, "Chargement des tags…"));
        phase.Restart();
        var tagResolutionMap = new Dictionary<string, string>();
        await PopulateTagHierarchyAsync(db, tagResolutionMap).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 2/{Total}] Tags completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 3: Ships, manufacturers, and ship-tag associations
        progress?.Report(new RebuildProgress(3, TotalPhases, "Chargement des vaisseaux…"));
        phase.Restart();
        await PopulateShipsAndManufacturersAsync(db, tagResolutionMap, cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 3/{Total}] Ships & manufacturers completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 4: SCItems (must be before loadouts due to FK on ComponentRecordId)
        progress?.Report(new RebuildProgress(4, TotalPhases, "Chargement des objets (SCItems)…"));
        phase.Restart();
        await PopulateScItemsAsync(db, cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 4/{Total}] SCItems completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 5: Ship loadouts (depends on ScItems for ComponentRecordId FK)
        progress?.Report(new RebuildProgress(5, TotalPhases, "Chargement des loadouts des vaisseaux…"));
        phase.Restart();
        await PopulateShipLoadoutsAsync(db, cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 5/{Total}] Ship loadouts completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 6: Contract generators
        progress?.Report(new RebuildProgress(6, TotalPhases, "Chargement des générateurs de contrats…"));
        phase.Restart();
        await PopulateContractGeneratorsAsync(db, cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 6/{Total}] Contract generators completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Pre-populate item names cache from ScItems (populated in phase 5)
        _itemNamesCache = db.ScItems.ToDictionary(si => si.RecordId, si => si.LocalizedName);
        _logger.LogInformation("Item names cache built with {Count} entries.", _itemNamesCache.Count);

        // Phase 7: Blueprints (must be before missions due to FK)
        progress?.Report(new RebuildProgress(7, TotalPhases, "Chargement des plans de fabrication…"));
        phase.Restart();
        await ProcessAllBlueprintsAsync(db, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 7/{Total}] Blueprints completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 8: Missions and their requirements/rewards/spawn rules
        progress?.Report(new RebuildProgress(8, TotalPhases, "Chargement des missions…"));
        phase.Restart();
        await PopulateMissionsAsync(db, cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 8/{Total}] Missions completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 9: Resolve spawn rules to actual ships
        progress?.Report(new RebuildProgress(9, TotalPhases, "Résolution des règles d'apparition…"));
        phase.Restart();
        await ProcessMissionShipSpawnShipsAsync(db, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 9/{Total}] Spawn rules completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        // Phase 10: Locations (StarMapObjects)
        progress?.Report(new RebuildProgress(10, TotalPhases, "Chargement des emplacements…"));
        phase.Restart();
        await PopulateLocationsAsync(db, cancellationToken).ConfigureAwait(false);
        phase.Stop();
        _logger.LogInformation("[Phase 10/{Total}] Locations completed in {Elapsed}ms.", phase.ElapsedMilliseconds, TotalPhases);
        cancellationToken.ThrowIfCancellationRequested();

        total.Stop();
        _logger.LogInformation("Database rebuild completed in {Elapsed}ms.", total.ElapsedMilliseconds);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /* ========================================================================
     * PHASE 1: LOCALE ENTRIES
     * ======================================================================== */

    private async Task PopulateLocaleAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var localeEntries = new List<LocaleEntry>();

        try
        {
            using var stream = _p4kService.P4KFileSystem.OpenRead("Data\\Localization\\english\\global.ini");
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                var rawKey = line.Substring(0, equalsIndex);
                var value = line.Substring(equalsIndex + 1);

                // Strip trailing ,P and prefix with @
                string key;
                if (rawKey.EndsWith(",P"))
                {
                    key = "@" + rawKey.Substring(0, rawKey.Length - 2);
                }
                else
                {
                    key = "@" + rawKey;
                }

                localeEntries.Add(new LocaleEntry { Key = key, Value = value });
            }

            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.LocaleEntries.AddRange(localeEntries);
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate locale entries from P4K");
        }

        start.Stop();
        _logger.LogInformation("Locale entries populated in {Elapsed}ms. ({Count} entries)", start.ElapsedMilliseconds, localeEntries.Count);
    }

    /* ========================================================================
     * PHASE 2: TAG HIERARCHY
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
            
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Tags.AddRange(tagEntities);
            db.ChangeTracker.AutoDetectChangesEnabled = true;
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
        var componentGuidMap = new Dictionary<string, CigGuid>();
        var start = Stopwatch.StartNew();

        await foreach (var record in _p4kService.GetAllEntityClassDefinition(1).ConfigureAwait(false))
        {
            if (record.Data is not EntityClassDefinition entityClass)
                continue;

            var vehicleParams = entityClass.Components.OfType<VehicleComponentParams>().FirstOrDefault();
            if (vehicleParams != null)
            {
                var guid = record.RecordId.ToString();
                var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record.RecordId]));
                _entityClassToGuid[entityClass] = guid;

                // Extract and add ship tags
                var extractedTags = ExtractShipTags(entityClass, tagResolutionMap);
                foreach (var tagId in extractedTags)
                {
                    shipTags.Add(new ShipTagEntity { ShipGuid = guid, TagSelfId = tagId });
                }

                // Resolve manufacturer
                var manufacturerId = ResolveManufacturerId(vehicleParams.manufacturer, manufacturerCache, manufacturers);

                // Compute IsVisible from TechnicalName patterns
                var isVisible = ComputeIsVisible(record.RecordName);

                ships.Add(new ShipEntity
                {
                    EntityClassGuid = guid,
                    Crc32 = crc,
                    TechnicalName = record.RecordName,
                    LocalizedName = await _p4kService.GetEntityClassName(entityClass) ?? "Unknown",
                    ManufacturerId = manufacturerId,
                    IsVisible = isVisible
                });
            }

            // Build component GUID map for attachable components
            var attachableComponent = entityClass.Components.OfType<SAttachableComponentParams>().FirstOrDefault();
            if (attachableComponent != null)
            {
                switch (attachableComponent.AttachDef.Type)
                {
                    case EItemType.QuantumDrive:
                    case EItemType.Cooler:
                    case EItemType.Shield:
                    case EItemType.PowerPlant:
                    case EItemType.JumpDrive:
                    case EItemType.Radar:
                        var key = record.RecordName.Split(".", 2).Last();
                        componentGuidMap[key] = record.RecordId;
                        break;
                }
            }
        }

        _componentGuidMap = componentGuidMap;

        start.Stop();
        _logger.LogInformation("Ships and manufacturers processed in {Elapsed}ms.", start.ElapsedMilliseconds);

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Manufacturers.AddRange(manufacturers);
        db.Ships.AddRange(ships);
        db.ShipTags.AddRange(shipTags);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Inserted {Count} manufacturer into the database.", manufacturers.Count);
        _logger.LogInformation("Inserted {Count} ship into the database.", ships.Count);
        _logger.LogInformation("Inserted {Count} liaison ship <=> tag into the database.", shipTags.Count);
    }

    private static bool ComputeIsVisible(string technicalName)
    {
        return !technicalName.Contains("_ai_", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("salvageabledebris", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("_pu_", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("_ea_", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("_fleetweek", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("unmanned", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("mission", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("_Temp", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.Contains("bombless", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.EndsWith("_temp", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.EndsWith("_template", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.EndsWith("_tutorial", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.EndsWith("_advocacy", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.EndsWith("_indestructible", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.EndsWith("_pu", StringComparison.InvariantCultureIgnoreCase)
            && !technicalName.EndsWith("_test", StringComparison.InvariantCultureIgnoreCase);
    }

    /* ========================================================================
     * PHASE 4: SHIP LOADOUTS
     * ======================================================================== */

    private async Task PopulateShipLoadoutsAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var loadoutEntries = new List<ShipLoadoutEntryEntity>();
        var ships = await db.Ships.ToListAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Processing loadouts for {Count} ships.", ships.Count);

        foreach (var ship in ships)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var guid = new CigGuid(ship.EntityClassGuid);
                var record = await _p4kService.GetEntityType(
                    Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([guid]))).ConfigureAwait(false);

                if (record?.Data is not EntityClassDefinition )
                    continue;

                record = (await _p4kService.EnsureRecordsDepthAsync([record], 3)).First();
                var entityClass = (EntityClassDefinition)record.Data;

                var defaultLoadout = entityClass.Components.OfType<SEntityComponentDefaultLoadoutParams>().FirstOrDefault();
                if (defaultLoadout?.loadout == null)
                    continue;

                var entryIndex = 0;
                VisitLoadoutEntries(defaultLoadout.loadout, entry =>
                {
                    var loadoutEntry = ResolveLoadoutEntry(entry, ship, entryIndex++, out string? techName);
                    if (loadoutEntry != null)
                        loadoutEntries.Add(loadoutEntry);
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process loadout for ship {ShipGuid} ({TechnicalName})", ship.EntityClassGuid, ship.TechnicalName);
            }
        }

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.ShipLoadoutEntries.AddRange(loadoutEntries);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        start.Stop();
        _logger.LogInformation("Inserted {Count} ship loadout entries into the database.", loadoutEntries.Count);
    }

    private void VisitLoadoutEntries(SItemPortLoadoutBaseParams? loadout, Action<SItemPortLoadoutEntryParams> visitor)
    {
        if (loadout is SItemPortLoadoutManualParams manualLoadout)
        {
            foreach (var entry in manualLoadout.entries)
            {
                visitor(entry);
                VisitLoadoutEntries(entry.loadout, visitor);
            }
        }
    }

    private static readonly HashSet<EItemType> s_allowedLoadoutItemTypes = new()
    {
        // Attachable components
        EItemType.QuantumDrive,
        EItemType.Cooler,
        EItemType.Shield,
        EItemType.PowerPlant,
        EItemType.JumpDrive,
        EItemType.Radar,
        // Weapons
        EItemType.Bomb,
        EItemType.BombLauncher,
        EItemType.Missile,
        EItemType.MissileLauncher,
        EItemType.MissileController,
        EItemType.WeaponGun,
        EItemType.WeaponDefensive,
        EItemType.WeaponMining,
        EItemType.WeaponMount,
        EItemType.WeaponController,
        EItemType.Turret,
        EItemType.TurretBase,
        EItemType.UtilityTurret,
        // Passive systems
        EItemType.Armor,
        EItemType.Module,
        // Ajout des éléments de minage et des peintures
        EItemType.MiningModifier,
        EItemType.Paints
    };

    private ShipLoadoutEntryEntity? ResolveLoadoutEntry(SItemPortLoadoutEntryParams entry, ShipEntity ship, int index, out string? technicalNameOut)
    {
        EntityClassDefinition? entityClass = null;
        string? technicalName = entry.entityClassName;

        if (!string.IsNullOrEmpty(entry.entityClassName))
        {
            if (_componentGuidMap.TryGetValue(entry.entityClassName, out var guid))
            {
                var record = _p4kService.GetEntityType(
                    Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([guid])), 3).GetAwaiter().GetResult();
                entityClass = record?.Data as EntityClassDefinition;
                technicalName = record?.RecordName ?? entry.entityClassName;
            }
        }
        else if (entry.entityClassReference != null)
        {
            entityClass = entry.entityClassReference;
            // Get technical name from entity class selfId
            if (entityClass.selfId != default)
            {
                var crc32 = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([entityClass.selfId]));
                var record = _p4kService.GetEntityType(crc32).GetAwaiter().GetResult();
                technicalName = record?.RecordName;
            }
        }

        if (entityClass is null)
        {
            technicalNameOut = technicalName;
            return null;
        }

        var attachable = entityClass.Components.OfType<SAttachableComponentParams>().FirstOrDefault();

        // Skip entries without attachable component params (placeholders, port labels, non-component items)
        if (attachable?.AttachDef is not { } attachDef)
        {
            technicalNameOut = technicalName;
            return null;
        }

        // Filter on allowed item types
        if (!s_allowedLoadoutItemTypes.Contains(attachDef.Type))
        {
            technicalNameOut = technicalName;
            return null;
        }

        string displayName;
        string componentClass = "Unknown";
        int size = attachDef.Size;
        string grade = attachDef.Grade > 0 ? new string((char)(attachDef.Grade - 1 + 'A'), 1) : string.Empty;
        string? weaponType = null;
        string? guidanceType = null;
        float? alphaDamage = null;

        displayName = _p4kService.GetLocaleValue(attachDef.Localization.Name).GetAwaiter().GetResult() ?? entry.entityClassName ?? "Unknown";

        var desc = _p4kService.GetLocaleValue(attachDef.Localization.Description).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(desc))
        {
            var match = System.Text.RegularExpressions.Regex.Match(desc, @"Class:\s*(\w+)");
            if (match.Success && Enum.TryParse<ComponentClass>(match.Groups[1].Value, out var parsedClass))
            {
                componentClass = parsedClass.ToString();
            }
        }

        // Resolve weapon type and alpha damage from SAmmoContainerComponentParams in entity components
        // (requires load depth >= 3 for ammoParamsRecord to be populated)
        if (attachDef.Type == EItemType.WeaponGun)
        {
            var ammoContainerComponent = entityClass.Components.OfType<SAmmoContainerComponentParams>().FirstOrDefault();
            if (ammoContainerComponent?.ammoParamsRecord != null)
            {
                var ammoParams = ammoContainerComponent.ammoParamsRecord;
                var ammoCat = ammoParams.ammoCategory;

                string ammoTypeName = ammoCat switch
                {
                    AmmoCategory._5mm => "Ballistic",
                    AmmoCategory._7mm => "Ballistic",
                    AmmoCategory._10mm => "Ballistic",
                    AmmoCategory._50cal => "Ballistic",
                    AmmoCategory._50cal_pistol => "Ballistic",
                    AmmoCategory._12g => "Ballistic",
                    AmmoCategory.Coil => "Ballistic",
                    AmmoCategory.Laser => "Laser",
                    AmmoCategory.Plasma => "Plasma",
                    AmmoCategory.Electron => "Electron",
                    _ => null
                };

                // Resolve alpha damage from projectile params
                if (ammoParams.projectileParams != null)
                {
                    var damageBase = ammoParams.projectileParams switch
                    {
                        BulletProjectileParams bullet => bullet.damage,
                        TachyonProjectileParams tachyon => tachyon.damage,
                        _ => null
                    };

                    if (damageBase is DamageInfo damageInfo)
                    {
                        alphaDamage = damageInfo.DamagePhysical
                                     + damageInfo.DamageEnergy
                                     + damageInfo.DamageDistortion
                                     + damageInfo.DamageThermal
                                     + damageInfo.DamageBiochemical
                                     + damageInfo.DamageStun;
                    }
                }

                // TODO pour les sucker punch, regarder également sur la partie explosion
                // Fallback: detonation explosion damage (Jericho, Suckerpunch – explosive ordnance)
                if (alphaDamage == null || alphaDamage <= 1)
                {
                    if (ammoParams.projectileParams?.detonationParams?.explosionParams?.damage is DamageInfo detonationDmg)
                    {
                        alphaDamage = detonationDmg.DamagePhysical
                                     + detonationDmg.DamageEnergy
                                     + detonationDmg.DamageDistortion
                                     + detonationDmg.DamageThermal
                                     + detonationDmg.DamageBiochemical
                                     + detonationDmg.DamageStun;
                    }
                }

                // Fallback: beam damage per second (Supremacy-10T Laser Beam)
                if (alphaDamage == null || alphaDamage <= 0)
                {
                    var weaponComponent = entityClass.Components.OfType<SCItemWeaponComponentParams>().FirstOrDefault();
                    if (weaponComponent?.fireActions != null)
                    {
                        foreach (var action in weaponComponent.fireActions)
                        {
                            if (action is SWeaponActionFireBeamParams beamAction && beamAction.damagePerSecond is DamageInfo beamDmg)
                            {
                                alphaDamage = beamDmg.DamagePhysical
                                             + beamDmg.DamageEnergy
                                             + beamDmg.DamageDistortion
                                             + beamDmg.DamageThermal
                                             + beamDmg.DamageBiochemical
                                             + beamDmg.DamageStun;
                                break;
                            }
                        }
                    }
                }

                // Combine ammo type with weapon subtype from names
                if (ammoTypeName != null)
                {
                    var weaponSubtype = InferWeaponSubtypeFromName(technicalName, displayName);
                    weaponType = !string.IsNullOrEmpty(weaponSubtype)
                        ? $"{ammoTypeName} {weaponSubtype}"
                        : ammoTypeName;
                }
            }

            // Fallback: infer weapon type from technical name and display name
            if (weaponType == null)
            {
                var baseType = InferWeaponTypeFromName(technicalName, displayName);
                var weaponSubtype = InferWeaponSubtypeFromName(technicalName, displayName);
                weaponType = !string.IsNullOrEmpty(baseType)
                    ? (!string.IsNullOrEmpty(weaponSubtype) ? $"{baseType} {weaponSubtype}" : baseType)
                    : weaponSubtype;
            }
        }

        // Resolve missile guidance type from targeting params
        if (attachDef.Type == EItemType.Missile)
        {
            var missileParams = entityClass.Components.OfType<SCItemMissileParams>().FirstOrDefault();
            if (missileParams?.targetingParams != null)
            {
                guidanceType = missileParams.targetingParams.trackingSignalType switch
                {
                    ESignatureType.Infrared => "IR",
                    ESignatureType.Electromagnetic => "EM",
                    ESignatureType.CrossSection => "CrossSection",
                    ESignatureType.Decibel => "Decibel",
                    ESignatureType.Resource => "Resource",
                    ESignatureType.Identity => "Identity",
                    ESignatureType.CommsSignal => "CommsSignal",
                    ESignatureType.Interactable => "Interactable",
                    _ => null
                };
            }
        }

        technicalNameOut = technicalName;
        var componentRecordId = entityClass.selfId != default
            ? entityClass.selfId.ToString()
            : null;

        return new ShipLoadoutEntryEntity
        {
            ShipGuid = ship.EntityClassGuid,
            PortName = entry.itemPortName,
            ComponentType = EItemTypeToString(attachDef.Type),
            DisplayName = displayName,
            ComponentClass = componentClass,
            Size = size,
            Grade = grade,
            WeaponType = weaponType,
            GuidanceType = guidanceType,
            AlphaDamage = alphaDamage,
            ComponentRecordId = componentRecordId
        };
    }

    private static string EItemTypeToString(EItemType type)
    {
        return type switch
        {
            // Attachable components
            EItemType.PowerPlant => "PowerPlant",
            EItemType.Cooler => "Cooler",
            EItemType.Shield => "Shield",
            EItemType.Radar => "Radar",
            EItemType.QuantumDrive => "QuantumDrive",
            EItemType.JumpDrive => "JumpDrive",
            // Weapons
            EItemType.Bomb => "Bomb",
            EItemType.BombLauncher => "BombLauncher",
            EItemType.Missile => "Missile",
            EItemType.MissileLauncher => "MissileLauncher",
            EItemType.MissileController => "MissileController",
            EItemType.WeaponGun => "WeaponGun",
            EItemType.WeaponDefensive => "WeaponDefensive",
            EItemType.WeaponMining => "WeaponMining",
            EItemType.WeaponMount => "WeaponMount",
            EItemType.WeaponController => "WeaponController",
            EItemType.Turret => "Turret",
            EItemType.TurretBase => "TurretBase",
            EItemType.UtilityTurret => "UtilityTurret",
            // Passive systems
            EItemType.Armor => "Armor",
            EItemType.Module => "Module",
            _ => "Unknown"
        };
    }


    private (string? WeaponType, float? AlphaDamage) ResolveWeaponTypeAndDamage(CigGuid ammoContainerGuid, string? technicalName, string weaponName)
    {
        if (ammoContainerGuid == default)
            return (null, null);

        try
        {
            var crc32 = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([ammoContainerGuid]));
            var record = _p4kService.GetEntityType(crc32).GetAwaiter().GetResult();
            var ammoEntityClass = record?.Data as EntityClassDefinition;
            if (ammoEntityClass == null)
            {
                _logger.LogWarning("GetEntityType returned null for ammo container of {Weapon}", weaponName);
                return (null, null);
            }

            var ammoContainerParams = ammoEntityClass.Components.OfType<SAmmoContainerComponentParams>().FirstOrDefault();
            if (ammoContainerParams == null)
            {
                _logger.LogWarning("No SAmmoContainerComponentParams in ammo container entity for {Weapon}", weaponName);
                return (null, null);
            }

            if (ammoContainerParams.ammoParamsRecord == null)
            {
                _logger.LogWarning("ammoParamsRecord is null in ammo container for {Weapon}", weaponName);
                return (null, null);
            }

            var ammoParams = ammoContainerParams.ammoParamsRecord;
            var ammoCat = ammoParams.ammoCategory;

            string ammoTypeName = ammoCat switch
            {
                AmmoCategory._5mm => "Ballistic",
                AmmoCategory._7mm => "Ballistic",
                AmmoCategory._10mm => "Ballistic",
                AmmoCategory._50cal => "Ballistic",
                AmmoCategory._50cal_pistol => "Ballistic",
                AmmoCategory._12g => "Ballistic",
                AmmoCategory.Coil => "Ballistic",
                AmmoCategory.Laser => "Laser",
                AmmoCategory.Plasma => "Plasma",
                AmmoCategory.Electron => "Electron",
                _ => null
            };

            // Resolve alpha damage from projectile params
            float? alphaDamage = null;
            if (ammoParams.projectileParams != null)
            {
                var damageBase = ammoParams.projectileParams switch
                {
                    BulletProjectileParams bullet => bullet.damage,
                    TachyonProjectileParams tachyon => tachyon.damage,
                    _ => null
                };

                if (damageBase is DamageInfo damageInfo)
                {
                    alphaDamage = damageInfo.DamagePhysical
                                 + damageInfo.DamageEnergy
                                 + damageInfo.DamageDistortion
                                 + damageInfo.DamageThermal
                                 + damageInfo.DamageBiochemical
                                 + damageInfo.DamageStun;
                }
                else
                {
                    // Damage type is not DamageInfo (e.g., DamageOverTime, DamageZone)
                }
            }

            // Combine ammo type with weapon subtype from technical name
            string? weaponType;
            if (ammoTypeName != null)
            {
                var weaponSubtype = InferWeaponSubtypeFromName(technicalName, weaponName);
                weaponType = !string.IsNullOrEmpty(weaponSubtype)
                    ? $"{ammoTypeName} {weaponSubtype}"
                    : ammoTypeName;
            }
            else
            {
                weaponType = null;
            }

            return (weaponType, alphaDamage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve weapon type for {Weapon}", weaponName);
            return (null, null);
        }
    }

    private static string? InferWeaponTypeFromName(string? technicalName, string displayName)
    {
        // Priority 1: technical name contains explicit weapon type keywords
        if (!string.IsNullOrEmpty(technicalName))
        {
            var tech = technicalName.ToLowerInvariant();
            if (tech.Contains("laser")) return "Laser";
            if (tech.Contains("ballistic")) return "Ballistic";
            if (tech.Contains("plasma")) return "Plasma";
            if (tech.Contains("distortion")) return "Distortion";
            if (tech.Contains("massdriver")) return "Ballistic";
            if (tech.Contains("neutron")) return "Electron";
            if (tech.Contains("tachyon")) return "Distortion";
            if (tech.Contains("rpod")) return "Ballistic";
        }

        // Priority 2: display name heuristics
        var name = displayName.ToLowerInvariant();
        if (name.Contains("laser") || name.Contains("jericho")) return "Laser";
        if (name.Contains("plasma") || name.Contains("tigerstrike")) return "Plasma";
        if (name.Contains("electron")) return "Electron";
        if (name.Contains("distortion") || name.Contains("tachyon")) return "Distortion";
        if (name.Contains("gatling") || name.Contains("repeater") || name.Contains("cannon") || name.Contains("railgun") || name.Contains("coilgun") || name.Contains("scattergun") || name.Contains("mass driver")) return "Ballistic";
        return null;
    }

    private static string? InferWeaponSubtypeFromName(string? technicalName, string displayName)
    {
        var tech = (technicalName ?? string.Empty).ToLowerInvariant();
        var name = displayName.ToLowerInvariant();

        if (tech.Contains("repeater") || name.Contains("repeater")) return "Repeater";
        if (tech.Contains("gatling") || name.Contains("gatling")) return "Gatling";
        if (tech.Contains("cannon") || name.Contains("cannon")) return "Cannon";
        if (tech.Contains("scattergun") || name.Contains("scattergun")) return "Scattergun";
        if (tech.Contains("railgun") || name.Contains("railgun")) return "Railgun";
        if (tech.Contains("coilgun") || name.Contains("coilgun")) return "Coilgun";
        if (tech.Contains("massdriver") || name.Contains("mass driver")) return "Mass Driver";
        return null;
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

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.ContractGenerators.AddRange(contractGenerators);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
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

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        int missionCount = 0;
        foreach (var record in contracts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            missionCount += await ProcessContractForDb(record, db, record.RecordName, cancellationToken);
        }

        db.ChangeTracker.AutoDetectChangesEnabled = true;
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
                var categoryEntity = ResolveCategory(contract, handler.contractParams?.missionTypeOverride, db);
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
                await ProcessMissionPrerequisitesAsync(mission.Id, contract, db);
                await ProcessMissionObjectivesAsync(mission.Id, contract, db);
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

    private static dynamic? FindStandingPropOverride(object? overrides)
    {
        if (overrides == null)
            return null;
        foreach (dynamic p in (overrides as dynamic[] ?? Array.Empty<dynamic>()))
        {
            if (p.extendedTextToken == "StandingName")
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

    private MissionCategoryEntity? ResolveCategory(ContractBase contract, MissionType? handlerMissionTypeOverride, StarXelemDbContext db)
    {
        string? categoryKey = null;

        if (contract.paramOverrides?.missionTypeOverride != null)
        {
            categoryKey = contract.paramOverrides.missionTypeOverride.LocalisedTypeName;
        }
        else if (handlerMissionTypeOverride != null)
        {
            categoryKey = handlerMissionTypeOverride.LocalisedTypeName;
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

        // Extract standing type from handler contractParams
        string? standingType = null;
        string? standingName = null;
        int? maxStanding = null;
        string? maxStandingDisplayName = null;
        int? minStandingRaw = null;
        string? minStandingDisplayName = null;

        // Get handler reference for reputationScope access
        dynamic? handler = null;
        if (handlerIndex >= 0 && handlerIndex < generator.generators.Length)
        {
            handler = generator.generators[handlerIndex];
            var standingProp = FindStandingPropOverride(handler.contractParams?.propertyOverrides);
            if (standingProp?.value != null)
            {
                standingName = standingProp.value.ToString();
                standingType = "ReputationStanding_" + standingName;
            }
        }

        // Also check contract-level overrides
        if (string.IsNullOrEmpty(standingType))
        {
            var contractStanding = contract.paramOverrides?.propertyOverrides?.FirstOrDefault(p => p.extendedTextToken == "StandingName");
            if (contractStanding?.value != null)
            {
                standingName = contractStanding.value.ToString();
                standingType = "ReputationStanding_" + standingName;
            }
        }

        // Extract min/max standing from CareerContract
        if (contract is CareerContract careerContract)
        {
            if (careerContract.minStanding != null)
            {
                minStandingRaw = (int)careerContract.minStanding.minReputation;
                minStandingDisplayName = careerContract.minStanding.displayName;
            }

            if (careerContract.maxStanding != null)
            {
                // Default: use the standing's own minReputation
                maxStanding = (int)careerContract.maxStanding.minReputation;
                maxStandingDisplayName = careerContract.maxStanding.displayName;

                // If handler has reputationScope, use index-lookup algorithm
                if (handler is ContractGeneratorHandler_Career careerHandler
                    && careerHandler.reputationScope != null)
                {
                    var scopeParams = careerHandler.reputationScope;
                    var standings = scopeParams.standingMap.standings;
                    var size = standings.Length;
                    var foundIndex = -1;

                    for (int i = 0; i < size; i++)
                    {
                        if (standings[i]?.name == careerContract.maxStanding.name)
                        {
                            foundIndex = i;
                            break;
                        }
                    }

                    if (foundIndex > -1)
                    {
                        if (foundIndex < size - 1)
                        {
                            maxStanding = (int)(standings[foundIndex + 1].minReputation - 1);
                        }
                        else
                        {
                            maxStanding = (int)scopeParams.standingMap.reputationCeiling;
                        }
                    }
                }
            }
        }

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
            Category = categoryEntity,
            StandingType = standingType,
            StandingName = standingName,
            MaxStanding = maxStanding,
            MaxStandingDisplayName = maxStandingDisplayName,
            MinStandingRaw = minStandingRaw,
            MinStandingDisplayName = minStandingDisplayName
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

    private Task ProcessMissionRequiredTagsAsync(string missionId, ContractBase contract, StarXelemDbContext db)
    {
        try
        {
            if (contract.additionalPrerequisites == null)
                return Task.CompletedTask;

            foreach (var prerequisite in contract.additionalPrerequisites)
            {
                if (prerequisite is not ContractPrerequisite_CompletedContractTags completedTags)
                    continue;

                foreach (var tag in completedTags.requiredCompletedContractTags?.tags ?? [])
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

                foreach (var tag in completedTags.excludedCompletedContractTags?.tags ?? [])
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

        return Task.CompletedTask;
    }

    /* ---- Structured prerequisites (type-based dispatch) ---- */

    private async Task ProcessMissionPrerequisitesAsync(string missionId, ContractBase contract, StarXelemDbContext db)
    {
        try
        {
            if (contract.additionalPrerequisites == null)
                return;

            int order = 0;
            foreach (var prerequisite in contract.additionalPrerequisites)
            {
                var entity = new MissionPrerequisiteEntity { MissionId = missionId, OrderIndex = order++ };

                if (prerequisite is ContractPrerequisite_Reputation rep)
                {
                    entity.PrerequisiteType = "Reputation";
                    if (rep.minStanding != null)
                    {
                        entity.MinReputation = (int)rep.minStanding.minReputation;
                        entity.ScopeNameKey = rep.scope?.scopeName;
                        entity.FactionNameKey = rep.factionReputation?.name;
                    }
                    if (rep.maxStanding != null)
                    {
                        entity.MaxReputation = (int)rep.maxStanding.minReputation;
                    }
                    var scopeName = await _p4kService.GetLocaleValue(rep.scope?.scopeName).ConfigureAwait(false);
                    var factionName = await _p4kService.GetLocaleValue(rep.factionReputation?.name).ConfigureAwait(false);
                    entity.DisplayLabel = $"[Réputation] entre {rep.minStanding?.minReputation ?? -1} et {rep.maxStanding?.minReputation ?? -1} sur {scopeName ?? "Inconnue"} pour {factionName ?? "Inconnue"}";
                }
                else if (prerequisite is ContractPrerequisite_AreaTags areaTags)
                {
                    entity.PrerequisiteType = "AreaTags";
                    entity.RequiredTagNames = GetTagListNames(areaTags.requiredAreaTags);
                    entity.ExcludedTagNames = GetTagListNames(areaTags.excludedAreaTags);
                    var requiredLabels = areaTags.requiredAreaTags?.tags?.Select(t => t?.tagName).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
                    var excludedLabels = areaTags.excludedAreaTags?.tags?.Select(t => t?.tagName).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
                    var reqResolved = requiredLabels.Count > 0 ? string.Join(", ", await Task.WhenAll(requiredLabels.Select(n => _p4kService.GetLocaleValue(n))).ConfigureAwait(false)) : "";
                    var exclResolved = excludedLabels.Count > 0 ? string.Join(", ", await Task.WhenAll(excludedLabels.Select(n => _p4kService.GetLocaleValue(n))).ConfigureAwait(false)) : "";
                    entity.DisplayLabel = $"[Tags zone] requis: {reqResolved}; exclus: {exclResolved}";
                }
                else if (prerequisite is ContractPrerequisite_CompletedContractTags completedTags)
                {
                    entity.PrerequisiteType = "CompletedContractTags";
                    entity.RequiredTagNames = GetTagListNames(completedTags.requiredCompletedContractTags);
                    entity.ExcludedTagNames = GetTagListNames(completedTags.excludedCompletedContractTags);
                    var requiredLabels = completedTags.requiredCompletedContractTags?.tags?.Select(t => t?.tagName).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
                    var excludedLabels = completedTags.excludedCompletedContractTags?.tags?.Select(t => t?.tagName).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
                    var reqResolved = requiredLabels.Count > 0 ? string.Join(", ", await Task.WhenAll(requiredLabels.Select(n => _p4kService.GetLocaleValue(n))).ConfigureAwait(false)) : "";
                    var exclResolved = excludedLabels.Count > 0 ? string.Join(", ", await Task.WhenAll(excludedLabels.Select(n => _p4kService.GetLocaleValue(n))).ConfigureAwait(false)) : "";
                    entity.DisplayLabel = $"[Tags contrats] requis: {reqResolved}; exclus: {exclResolved}";
                }
                else if (prerequisite is ContractPrerequisite_CrimeStat crimeStat)
                {
                    entity.PrerequisiteType = "CrimeStat";
                    entity.MinCrimeStat = (int)crimeStat.minCrimeStat;
                    entity.MaxCrimeStat = (int)crimeStat.maxCrimeStat;
                    entity.JurisdictionNameKey = crimeStat.crimeStatJurisdictionOverride.ToString();
                    entity.DisplayLabel = $"[Stat criminal] entre {crimeStat.minCrimeStat} et {crimeStat.maxCrimeStat}";
                }
                else if (prerequisite is ContractPrerequisite_JournalEntries journalEntries)
                {
                    entity.PrerequisiteType = "JournalEntries";
                    if (journalEntries.requiredJournalEntries != null && journalEntries.requiredJournalEntries.Length > 0)
                    {
                        entity.RequiredJournalTitles = string.Join(",", journalEntries.requiredJournalEntries.Select(j => j.Title));
                        var titles = journalEntries.requiredJournalEntries.Select(j => j.Title).ToList();
                        var resolved = string.Join(", ", await Task.WhenAll(titles.Select(t => _p4kService.GetLocaleValue(t))).ConfigureAwait(false));
                        entity.DisplayLabel = $"[Journal] entrées requises: {resolved}";
                    }
                    else
                    {
                        entity.DisplayLabel = "[Journal] entrée requise";
                    }
                }
                else if (prerequisite is ContractPrerequisite_Locality locality)
                {
                    entity.PrerequisiteType = "Locality";
                    var localityData = locality.localityAvailable;
                    if (localityData?.availableLocations != null && localityData.availableLocations.Length > 0)
                    {
                        var names = localityData.availableLocations.Select(l => l?.name).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();
                        entity.LocationNameKey = string.Join(",", names);
                        var resolvedNames = names.Count > 0 ? string.Join(" OU ", await Task.WhenAll(names.Select(n => _p4kService.GetLocaleValue(n))).ConfigureAwait(false)) : "";
                        entity.DisplayLabel = $"[Localité] {resolvedNames}";
                    }
                    else
                    {
                        entity.DisplayLabel = "[Localité] non spécifiée";
                    }
                }
                else if (prerequisite is ContractPrerequisite_Location location)
                {
                    entity.PrerequisiteType = "Location";
                    var locationName = location.locationAvailable?.name;
                    entity.LocationNameKey = locationName;
                    var resolvedName = locationName != null ? await _p4kService.GetLocaleValue(locationName).ConfigureAwait(false) : "";
                    entity.DisplayLabel = $"[Lieu] {resolvedName ?? "Inconnue"}";
                }
                else if (prerequisite is ContractPrerequisite_LocationProperty locationProp)
                {
                    entity.PrerequisiteType = "LocationProperty";
                    entity.LocationLevelType = locationProp.locationLevelType.ToString();
                    // Resolve propertyVariableName from contract paramOverrides
                    var varName = locationProp.propertyVariableName;
                    if (!string.IsNullOrEmpty(varName) && contract.paramOverrides?.propertyOverrides != null)
                    {
                        var propOverride = contract.paramOverrides.propertyOverrides.FirstOrDefault(p => p.missionVariableName == varName);
                        if (propOverride.value != null)
                        {
                            entity.LocationNameKey = propOverride.value.ToString();
                        }
                    }
                    entity.DisplayLabel = $"[Propriété de lieu] niveau {locationProp.locationLevelType}, propriété {varName}";
                }
                else
                {
                    entity.PrerequisiteType = prerequisite.GetType().Name;
                    entity.DisplayLabel = $"[Prérequis] {prerequisite.GetType().Name}";
                }

                db.MissionPrerequisites.Add(entity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec du parsing des prérequis structurés pour la mission {MissionId}", missionId);
        }
    }

    private static string GetTagListNames(StarBreaker.DataCoreGenerated.TagList tagList)
    {
        if (tagList?.tags?.Length > 0)
            return string.Join(",", tagList.tags.Select(t => t.tagName));
        return null;
    }

    /* ---- Mission objectives ---- */

    private async Task ProcessMissionObjectivesAsync(string missionId, ContractBase contract, StarXelemDbContext db)
    {
        try
        {
            var objectiveTokens = contract.template?.objectiveTokens;
            if (objectiveTokens == null || objectiveTokens.Length == 0)
                return;

            var objectiveMap = new Dictionary<string, MissionObjectiveEntity>();

            foreach (var rootToken in objectiveTokens)
            {
                if (rootToken == null) continue;
                await ProcessObjectiveTokenAsync(missionId, rootToken, null, 0, db, objectiveMap);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec du parsing des objectifs pour la mission {MissionId}", missionId);
        }
    }

    private async Task ProcessObjectiveTokenAsync(
        string missionId,
        ObjectiveToken token,
        MissionObjectiveEntity? parentEntity,
        int order,
        StarXelemDbContext db,
        Dictionary<string, MissionObjectiveEntity> objectiveMap)
    {
        var handler = token.objectiveHandler;

        if (handler is ObjectiveHandler_Hauling haulingHandler)
        {
            // Process each hauling order, creating one objective per order (like old code)
            if (haulingHandler.haulingOrders != null)
            {
                for (int i = 0; i < haulingHandler.haulingOrders.Length; i++)
                {
                    var haulingOrder = haulingHandler.haulingOrders[i];
                    if (haulingOrder == null) continue;

                    var obj = await ProcessHaulingOrderAsync(
                        missionId, haulingHandler, haulingOrder, parentEntity, order + i, db);

                    if (obj != null)
                    {
                        objectiveMap[token.id.ToString()] = obj;

                        // Process child phases under this objective
                        if (token.childMissionPhases != null && token.childMissionPhases.Length > 0
                            && i == 0)
                        {
                            int childOrder = 0;
                            foreach (var child in token.childMissionPhases)
                            {
                                if (child == null) continue;
                                await ProcessObjectiveTokenAsync(missionId, child, obj, childOrder++, db, objectiveMap);
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // Try handler-specific display info first, then token displayInfo, then debugName
            string? textKey = null;

            // Handlers with travelObjectiveInfo (WithModule derivatives: Local, NearLocation, PlayerAttached, EntityAttached)
            if (handler is ObjectiveHandler_WithModule withModuleHandler)
            {
                var travelInfo = withModuleHandler.travelObjectiveInfo;
                if (!string.IsNullOrEmpty(travelInfo.shortDescription))
                {
                    textKey = travelInfo.shortDescription;
                }
            }

            // MeetAndTalk also has travelObjectiveInfo but inherits directly from ObjectiveHandlerBase
            if (string.IsNullOrEmpty(textKey) && handler is ObjectiveHandler_MeetAndTalk meetAndTalkHandler)
            {
                var meetInfo = meetAndTalkHandler.travelObjectiveInfo;
                if (!string.IsNullOrEmpty(meetInfo.shortDescription))
                {
                    textKey = meetInfo.shortDescription;
                }
            }

            // Fallback to token displayInfo
            if (string.IsNullOrEmpty(textKey))
            {
                textKey = token.displayInfo?.shortDescription;
            }

            // Resolve locale value
            string? resolvedText = null;
            if (!string.IsNullOrEmpty(textKey))
            {
                try { resolvedText = await _p4kService.GetLocaleValue(textKey).ConfigureAwait(false); }
                catch { }
            }

            // Final fallback chain: resolved text > debugName > raw key
            var text = !string.IsNullOrEmpty(resolvedText) ? resolvedText : (token.debugName ?? textKey ?? "");

            // Skip objectives that resolved to uninitialized placeholder
            if (text.Contains("UNINITIALIZED", StringComparison.Ordinal))
            {
                return;
            }

            var obj = new MissionObjectiveEntity
            {
                MissionId = missionId,
                Type = "Objective",
                Text = text,
                TextKey = textKey,
                Order = order,
                Parent = parentEntity
            };
            db.MissionObjectives.Add(obj);
            objectiveMap[token.id.ToString()] = obj;

            if (token.childMissionPhases != null && token.childMissionPhases.Length > 0)
            {
                int childOrder = 0;
                foreach (var child in token.childMissionPhases)
                {
                    if (child == null) continue;
                    await ProcessObjectiveTokenAsync(missionId, child, obj, childOrder++, db, objectiveMap);
                }
            }
        }
    }

    private async Task<MissionObjectiveEntity?> ProcessHaulingOrderAsync(
        string missionId,
        ObjectiveHandler_Hauling handler,
        HaulingOrderBase haulingOrder,
        MissionObjectiveEntity? parentEntity,
        int order,
        StarXelemDbContext db)
    {
        var settings = handler.objectiveSettings;
        var displayInfo = default(ObjectiveDisplayInfo);
        var hasDisplayInfo = false;
        Dictionary<string, string>? tokenMap = null;

        switch (haulingOrder)
        {
            case HaulingOrder_EntityClass hoEc:
                displayInfo = settings.itemDeliverObjective;
                hasDisplayInfo = true;
                {
                    var itemName = await _p4kService.GetEntityClassName(hoEc.entityClass).ConfigureAwait(false) ?? "Inconnu";
                    tokenMap = new Dictionary<string, string>();
                    AddTokenIfSet(tokenMap, settings.itemExtendedTextToken, itemName);
                    AddTokenIfSet(tokenMap, settings.amountExtendedTextToken, "0");
                    AddTokenIfSet(tokenMap, settings.totalExtendedTextToken, Math.Max(hoEc.minAmount, hoEc.maxAmount).ToString(System.Globalization.CultureInfo.CurrentCulture));
                    AddTokenIfSet(tokenMap, settings.dropOffLocationExtendedTextToken, "[Destination]");
                }
                break;
            case HaulingOrder_EntityClasses hoEcs:
                displayInfo = settings.itemDeliverObjective;
                hasDisplayInfo = true;
                {
                    var itemName = hoEcs.haulingEntityClasses?.orderDisplayName != null
                        ? await _p4kService.GetLocaleValue(hoEcs.haulingEntityClasses.orderDisplayName).ConfigureAwait(false) ?? "Inconnu"
                        : "Inconnu";
                    tokenMap = new Dictionary<string, string>();
                    AddTokenIfSet(tokenMap, settings.itemExtendedTextToken, itemName);
                    AddTokenIfSet(tokenMap, settings.amountExtendedTextToken, "0");
                    AddTokenIfSet(tokenMap, settings.totalExtendedTextToken, Math.Max(hoEcs.minAmount, hoEcs.maxAmount).ToString(System.Globalization.CultureInfo.CurrentCulture));
                    AddTokenIfSet(tokenMap, settings.dropOffLocationExtendedTextToken, "[Destination]");
                }
                break;
            case HaulingOrder_Resource hoRes:
                displayInfo = settings.resourceDeliverObjective;
                hasDisplayInfo = true;
                {
                    var resourceName = hoRes.resource?.displayName != null
                        ? await _p4kService.GetLocaleValue(hoRes.resource.displayName).ConfigureAwait(false) ?? "Inconnu"
                        : "Inconnu";
                    tokenMap = new Dictionary<string, string>();
                    AddTokenIfSet(tokenMap, settings.itemExtendedTextToken, resourceName);
                    AddTokenIfSet(tokenMap, settings.amountExtendedTextToken, "0");
                    AddTokenIfSet(tokenMap, settings.totalExtendedTextToken, Math.Max(hoRes.minSCU, hoRes.maxSCU).ToString(System.Globalization.CultureInfo.CurrentCulture));
                    AddTokenIfSet(tokenMap, settings.dropOffLocationExtendedTextToken, "[Destination]");
                }
                break;
            case HaulingOrder_ResourceUnlimitedDropOff hoResUnl:
                displayInfo = settings.resourceDeliverObjective;
                hasDisplayInfo = true;
                {
                    var resourceName = hoResUnl.resource?.displayName != null
                        ? await _p4kService.GetLocaleValue(hoResUnl.resource.displayName).ConfigureAwait(false) ?? "Inconnu"
                        : "Inconnu";
                    tokenMap = new Dictionary<string, string>();
                    AddTokenIfSet(tokenMap, settings.itemExtendedTextToken, resourceName);
                    AddTokenIfSet(tokenMap, settings.amountExtendedTextToken, "0");
                    AddTokenIfSet(tokenMap, settings.totalExtendedTextToken, "0");
                    AddTokenIfSet(tokenMap, settings.dropOffLocationExtendedTextToken, "[Destination]");
                }
                break;
            default:
                return null;
        }

        if (!hasDisplayInfo) return null;

        var textKey = displayInfo.shortDescription;
        string? resolvedText = null;
        if (!string.IsNullOrEmpty(textKey))
        {
            try { resolvedText = await _p4kService.GetLocaleValue(textKey).ConfigureAwait(false); }
            catch { }
        }

        var text = !string.IsNullOrEmpty(resolvedText) ? resolvedText : textKey ?? "";
        if (tokenMap != null && !string.IsNullOrEmpty(text))
        {
            text = ReplaceMissionTokens(text, tokenMap);
        }

        var obj = new MissionObjectiveEntity
        {
            MissionId = missionId,
            Type = "Hauling",
            Text = text,
            TextKey = textKey,
            Order = order,
            Parent = parentEntity
        };
        db.MissionObjectives.Add(obj);

        if (tokenMap != null)
        {
            foreach (var kvp in tokenMap)
            {
                db.MissionTokens.Add(new MissionTokenEntity
                {
                    MissionId = missionId,
                    Objective = obj,
                    TokenName = kvp.Key,
                    ValueType = InferTokenType(kvp.Key),
                    ResolvedValue = kvp.Value
                });
            }
        }

        return obj;
    }

    private static void AddTokenIfSet(Dictionary<string, string> map, string? tokenName, string value)
    {
        if (!string.IsNullOrEmpty(tokenName))
            map[tokenName] = value;
    }

    private static string ReplaceMissionTokens(string text, Dictionary<string, string> tokenMap)
    {
        var matches = MissionTokenRegex.Matches(text);
        foreach (Match match in matches.Cast<Match>().Reverse())
        {
            var tokenName = match.Groups[1].Value;
            if (tokenMap.TryGetValue(tokenName, out var value))
            {
                text = text.Substring(0, match.Index) + value + text.Substring(match.Index + match.Length);
            }
        }
        return text;
    }

    private static string InferTokenType(string tokenName)
    {
        var lower = tokenName.ToLowerInvariant();
        if (lower.Contains("faction") || lower.Contains("org")) return "Organization";
        if (lower.Contains("location")) return "Location";
        if (lower.Contains("item")) return "HaulingItem";
        if (lower.Contains("amount") || lower.Contains("count")) return "HaulingAmount";
        if (lower.Contains("destination")) return "HaulingDestination";
        if (lower.Contains("ai") || lower.Contains("npc")) return "AIName";
        return "Unknown";
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

                    db.MissionRewards.Add(new MissionRewardEntity
                    {
                        MissionId = missionId,
                        RewardType = "ContractResult_CalculatedReward",
                        DisplayValue = string.Format("{0:N0} aUEC", computed),
                        IsCalculated = true
                    });
                }
                return 1;

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
                        IsCalculated = false,
                        Count = item.amount,
                        OnlyToMissionOwner = item.awardOnlyToMissionOwner,
                        SendToHomeLocation = item.sendToPlayerHomeLocation
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
                        if (ct?.tag == null) continue;
                        tagNames.Add($"'{ct.tag.tagName}'");

                        var tagId = ct.tag.selfId.ToString();
                        if (!string.IsNullOrEmpty(tagId))
                        {
                            db.MissionCompletionTags.Add(new MissionCompletionTagEntity
                            {
                                MissionId = missionId,
                                TagSelfId = tagId
                            });
                        }
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
            Crc32 = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([new CigGuid(blueprintId)])),
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
            await IngestRecipeCostsAsync(blueprintEntity, costs, db).ConfigureAwait(false);
        }
        else
        {
            db.Blueprints.Add(blueprintEntity);
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
            await ProcessOptionalCostsAsync(blueprintEntity, costs.optionalCosts, db).ConfigureAwait(false);
        }

        foreach (var craftingCostOption in mandatoryCost.options)
        {
            if (craftingCostOption is not CraftingCost_Select costSelect)
                continue;

            var categoryName = await _p4kService.GetLocaleValue(costSelect.nameInfo.displayName) ?? "Unknown";

            var costEntities = new List<BlueprintRecipeCostEntity>();
            foreach (var costOption in costSelect.options)
            {
                var costEntity = await ProcessCostOptionAsync(blueprintEntity, costOption, categoryName, db).ConfigureAwait(false);
                if (costEntity != null)
                    costEntities.Add(costEntity);
            }

            foreach (var modifierContext in costSelect.context.OfType<CraftingCostContext_ResultGameplayPropertyModifiers>())
            {
                var modifiers = await ExtractModifierEntitiesAsync(modifierContext);
                foreach (var costEntity in costEntities)
                {
                    foreach (var modifier in modifiers)
                    {
                        modifier.Cost = costEntity;
                        db.BlueprintModifiers.Add(modifier);
                    }
                }
            }
        }
    }

    private async Task ProcessOptionalCostsAsync(BlueprintEntity blueprintEntity, dynamic[] optionalEntries, StarXelemDbContext db)
    {
        foreach (var opt in optionalEntries)
        {
            if (opt is not CraftingOptionalEntry optionalEntry)
                continue;

            var cost = optionalEntry.optionalCost;
            await ProcessCostOptionAsync(blueprintEntity, cost, "Optional", db).ConfigureAwait(false);
        }
    }

    private async Task<BlueprintRecipeCostEntity?> ProcessCostOptionAsync(BlueprintEntity blueprintEntity, dynamic? costOption, string costName, StarXelemDbContext db)
    {
        if (costOption == null)
            return null;

        switch (costOption)
        {
            case CraftingCost_Resource resourceCost:
                {
                    float rawQuantity = (resourceCost.quantity as SStandardCargoUnit)?.standardCargoUnits ?? 0f;
                    var resourceRef = resourceCost.resource?.selfId.ToString() ?? "unknown";
                    string? resourceName = null;
                    if (resourceRef != "unknown")
                    {
                        try
                        {
                            var resRecord = Task.Run(async () => await _p4kService.GetRecordWithSpecificDepth(new CigGuid(resourceRef), 0)).Result;
                            resourceName = StripRecordPrefix(resRecord?.RecordName);
                        }
                        catch
                        {
                            resourceName = null;
                        }
                    }
                    var entity = new BlueprintRecipeCostEntity
                    {
                        BlueprintId = blueprintEntity.SelfId,
                        CostType = "Resource",
                        CostName = costName,
                        ResourceRef = resourceRef,
                        ResourceAmount = (decimal)rawQuantity,
                        ResourceName = resourceName
                    };
                    db.BlueprintRecipeCosts.Add(entity);
                    return entity;
                }

            case CraftingCost_Item itemCost:
                {
                    var itemRef = itemCost.entityClass?.selfId.ToString();
                    string? itemName = null;
                    if (!string.IsNullOrEmpty(itemRef))
                    {
                        itemName = _itemNamesCache.GetValueOrDefault(itemRef);
                    }
                    var entity = new BlueprintRecipeCostEntity
                    {
                        BlueprintId = blueprintEntity.SelfId,
                        CostType = "Item",
                        CostName = costName,
                        ItemEntityClassRef = itemRef,
                        ItemCount = itemCost.quantity,
                        MinQuality = itemCost.minQuality,
                        ItemName = itemName
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
      * PHASE 8: LOCATIONS (StarMapObjects)
      * ======================================================================== */

    private async Task PopulateLocationsAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var locations = new List<LocationEntity>();
        var crcToCigGuid = new Dictionary<uint, string>();

        var starMapObjects = await _p4kService.GetAllStarMapObjects().ConfigureAwait(false);
        _logger.LogInformation("Found {Count} StarMapObject records in P4K.", starMapObjects.Count);

        foreach (var record in starMapObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.Data is not StarMapObject smo)
                continue;

            var cigGuid = record.RecordId.ToString();
            var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record.RecordId]));
            crcToCigGuid[crc] = cigGuid;

            var nameLocalized = await _p4kService.GetLocaleValue(smo.name).ConfigureAwait(false) ?? string.Empty;
            var descriptionLocalized = !string.IsNullOrEmpty(smo.description)
                ? await _p4kService.GetLocaleValue(smo.description).ConfigureAwait(false)
                : null;

            var affiliationName = smo.affiliation?.displayName;
            if (!string.IsNullOrEmpty(affiliationName))
            {
                affiliationName = await _p4kService.GetLocaleValue(affiliationName).ConfigureAwait(false) ?? affiliationName;
            }

            var jurisdictionName = smo.jurisdiction?.name;
            if (!string.IsNullOrEmpty(jurisdictionName))
            {
                jurisdictionName = await _p4kService.GetLocaleValue(jurisdictionName).ConfigureAwait(false) ?? jurisdictionName;
            }

            var callout1 = !string.IsNullOrEmpty(smo.callout1)
                ? await _p4kService.GetLocaleValue(smo.callout1).ConfigureAwait(false)
                : null;
            var callout2 = !string.IsNullOrEmpty(smo.callout2)
                ? await _p4kService.GetLocaleValue(smo.callout2).ConfigureAwait(false)
                : null;
            var callout3 = !string.IsNullOrEmpty(smo.callout3)
                ? await _p4kService.GetLocaleValue(smo.callout3).ConfigureAwait(false)
                : null;

            locations.Add(new LocationEntity
            {
                CigGuid = cigGuid,
                Crc = crc,
                SelfId = smo.selfId.ToString(),
                NameKey = smo.name ?? string.Empty,
                NameLocalized = nameLocalized,
                DescriptionKey = smo.description,
                DescriptionLocalized = descriptionLocalized,
                Type = smo.type?.name ?? string.Empty,
                Jurisdiction = jurisdictionName,
                Affiliation = affiliationName,
                Callout1 = callout1,
                Callout2 = callout2,
                Callout3 = callout3,
                RespawnLocationType = smo.respawnLocationType.ToString(),
                LocationHierarchyTag = smo.locationHierarchyTag?.selfId.ToString(),
                NavIcon = smo.navIcon.ToString(),
                IsScannable = smo.isScannable,
                Size = smo.size,
                HideInStarmap = smo.hideInStarmap,
                HideInWorld = smo.hideInWorld,
                HideWhenInAdoptionRadius = smo.hideWhenInAdoptionRadius,
                BlockTravel = smo.blockTravel,
                OnlyShowWhenParentSelected = smo.onlyShowWhenParentSelected,
                MinimumDisplaySize = smo.minimumDisplaySize,
                OverrideRotationSpeed = smo.overrideRotationSpeed,
                OverrideRotationSpeedValue = smo.overrideRotationSpeedValue,
                ShowOrbitLine = smo.showOrbitLine,
                UseHoloMaterial = smo.useHoloMaterial,
                NoAutoBodyRecovery = smo.noAutoBodyRecovery,
                StarMapGeomPath = smo.starMapGeomPath,
                StarMapMaterialPath = smo.starMapMaterialPath,
                StarMapShapePath = smo.starMapShapePath,
                LocationImagePath = smo.locationImagePath,
                LocationMedicalImagePath = smo.locationMedicalImagePath,
                ParentCigGuid = smo.parent != null ? smo.parent.selfId.ToString() : null
            });
        }

        // Fix parent references: map parent CigGuid to actual CigGuid values
        foreach (var location in locations)
        {
            if (location.ParentCigGuid != null && crcToCigGuid.TryGetValue(location.Crc, out var selfCrc))
            {
                // ParentCigGuid is the parent's CigGuid string. We need to find the location that has this CigGuid.
                // Since we stored CigGuid as PK, we can directly match.
                // The parent reference from StarMapObject.parent.selfId is a CigGuid, so it matches our PK.
            }
        }

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Locations.AddRange(locations);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        start.Stop();
        _logger.LogInformation("Inserted {Count} locations into the database.", locations.Count);
        _logger.LogInformation("Locations processing completed in {Elapsed}ms.", start.ElapsedMilliseconds);
    }

    /* ========================================================================
      * PHASE 7: SCITEMS
      * ======================================================================== */

    private async Task PopulateScItemsAsync(StarXelemDbContext db, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var items = new List<ScItemEntity>();
        var itemTags = new HashSet<(string ScItemRecordId, string TagSelfId)>();
        var itemTagEntities = new List<ScItemTagEntity>();
        var manufacturerCache = new Dictionary<string, ManufacturerEntity>();
        EnsureUnknownManufacturer(manufacturerCache);

        var existingManufacturerIds = new HashSet<string>(db.Manufacturers.Select(m => m.Id));
        int totalProcessed = 0;
        int batchesSaved = 0;

        _logger.LogInformation("Starting SCItems processing...");

        await foreach (var record in _p4kService.GetAllEntityClassDefinition(3).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.Data is not EntityClassDefinition entityClass)
                continue;

            if (entityClass.Invisible)
                continue;

            var attachable = entityClass.Components.OfType<SAttachableComponentParams>().FirstOrDefault();
            if (attachable?.AttachDef is not SItemDefinition itemDef)
                continue;

            if (itemDef.Type == EItemType.__Unknown || itemDef.Type == EItemType.UNDEFINED)
                continue;

            if (entityClass.Components.OfType<VehicleComponentParams>().Any())
                continue;

            var scItem = BuildScItemEntity(record, entityClass, itemDef, manufacturerCache);
            items.Add(scItem);
            totalProcessed++;

            if (entityClass.tags != null)
            {
                foreach (var tag in entityClass.tags)
                {
                    if (tag == null)
                        continue;
                    var tagId = tag.selfId.ToString();
                    if (!string.IsNullOrEmpty(tagId))
                    {
                        var key = (scItem.RecordId, tagId);
                        if (itemTags.Add(key))
                        {
                            itemTagEntities.Add(new ScItemTagEntity
                            {
                                ScItemRecordId = scItem.RecordId,
                                TagSelfId = tagId
                            });
                        }
                    }
                }
            }

            if (items.Count % 10_000 == 0)
            {
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var newManufacturers = manufacturerCache.Values
                    .Where(m => m.Id != "Unknown" && !existingManufacturerIds.Contains(m.Id))
                    .ToList();
                if (newManufacturers.Count > 0)
                {
                    db.Manufacturers.AddRange(newManufacturers);
                    foreach (var m in newManufacturers)
                    {
                        existingManufacturerIds.Add(m.Id);
                    }
                }
                db.ScItems.AddRange(items);
                db.ScItemTags.AddRange(itemTagEntities);
                db.ChangeTracker.AutoDetectChangesEnabled = true;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                batchesSaved++;
                _logger.LogInformation("[SCItems {BatchesSaved}/~{EstimatedBatches}] Batch saved: {Processed} total items, {InBatch} in batch",
                    batchesSaved, (totalProcessed / 10_000) + 2, totalProcessed, items.Count);
                items.Clear();
                itemTags.Clear();
                itemTagEntities.Clear();
            }
        }

        if (items.Count > 0)
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var newManufacturers = manufacturerCache.Values
                .Where(m => m.Id != "Unknown" && !existingManufacturerIds.Contains(m.Id))
                .ToList();
            if (newManufacturers.Count > 0)
            {
                db.Manufacturers.AddRange(newManufacturers);
                foreach (var m in newManufacturers)
                {
                    existingManufacturerIds.Add(m.Id);
                }
            }
            db.ScItems.AddRange(items);
            db.ScItemTags.AddRange(itemTagEntities);
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            batchesSaved++;
            _logger.LogInformation("[SCItems {BatchesSaved}/~{EstimatedBatches}] Final batch saved: {Processed} total items, {InBatch} in batch",
                batchesSaved, (totalProcessed / 10_000) + 2, totalProcessed, items.Count);
        }

        start.Stop();
        _logger.LogInformation("Inserted {Count} SCItems and {TagCount} SCItem tags into the database.", totalProcessed, itemTagEntities.Count);
        _logger.LogInformation("SCItems processing completed in {Elapsed}ms.", start.ElapsedMilliseconds);
    }

    private ScItemEntity BuildScItemEntity(
        DataCoreTypedRecord record,
        EntityClassDefinition entityClass,
        SItemDefinition itemDef,
        Dictionary<string, ManufacturerEntity> manufacturerCache)
    {
        var components = entityClass.Components;

        var healthParams = components.OfType<SHealthComponentParams>().FirstOrDefault();
        var shieldParams = components.OfType<SCItemShieldGeneratorParams>().FirstOrDefault();
        var jumpDriveParams = components.OfType<SCItemJumpDriveParams>().FirstOrDefault();
        var distortionParams = components.OfType<SDistortionParams>().FirstOrDefault();
        var armorParams = components.OfType<SCItemVehicleArmorParams>().FirstOrDefault();
        var weaponParams = components.OfType<SCItemWeaponComponentParams>().FirstOrDefault();
        var ammoContainerParams = components.OfType<SAmmoContainerComponentParams>().FirstOrDefault();
        var missileParams = components.OfType<SCItemMissileParams>().FirstOrDefault();
        var resourceParams = components.OfType<ItemResourceComponentParams>().FirstOrDefault();
        var physicsParams = components.OfType<SEntityPhysicsControllerParams>().FirstOrDefault();
        var purchasableParams = components.OfType<SCItemPurchasableParams>().FirstOrDefault();

        var manufacturerId = ResolveScItemManufacturerId(itemDef, manufacturerCache);

        var (powerGen, powerCons, coolantGen, coolantCons, fuelCap, resourceJson) =
            ExtractResourceDeltas(resourceParams);

        var shieldResistJson = shieldParams != null
            ? System.Text.Json.JsonSerializer.Serialize(shieldParams.ShieldResistance)
            : null;

        var damageResist = healthParams?.DamageResistances as DamageResistance;

        // Extract damage from weapon projectile or missile explosion
        var (dmgPhysical, dmgEnergy, dmgDistortion, dmgThermal, dmgBiochemical, dmgStun) =
            ExtractItemDamage(ammoContainerParams, missileParams, weaponParams);



        var localizedName = Task.Run(async () => await _p4kService.GetEntityClassName(entityClass)).Result
            ?? StripRecordPrefix(record.RecordName)
            ?? record.RecordName;

        return new ScItemEntity
        {
            RecordId = record.RecordId.ToString(),
            Crc32 = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record.RecordId])),
            TechnicalName = record.RecordName,
            LocalizedName = localizedName,
            TypeName = itemDef.Type.ToString(),
            SubTypeName = itemDef.SubType.ToString(),
            Size = itemDef.Size,
            Grade = itemDef.Grade,
            LocaleNameKey = itemDef.Localization.Name,
            LocaleDescKey = itemDef.Localization.Description,
            DisplayLocaleKey = purchasableParams?.displayName,
            ManufacturerId = manufacturerId,
            Mass = physicsParams?.PhysType?.Mass,
            Health = healthParams?.Health,
            IsSalvagable = healthParams?.IsSalvagable,
            IsRepairable = healthParams?.IsRepairable,
            ResistPhysical = damageResist?.PhysicalResistance.Multiplier,
            ResistEnergy = damageResist?.EnergyResistance.Multiplier,
            ResistDistortion = damageResist?.DistortionResistance.Multiplier,
            ResistThermal = damageResist?.ThermalResistance.Multiplier,
            ResistBiochemical = damageResist?.BiochemicalResistance.Multiplier,
            ResistStun = damageResist?.StunResistance.Multiplier,
            InventoryVolumeMicroSCU = itemDef.inventoryOccupancyVolume is SStandardCargoUnit scu
                ? (long?)scu.standardCargoUnits
                : null,
            InvDimX = itemDef.inventoryOccupancyDimensions.x,
            InvDimY = itemDef.inventoryOccupancyDimensions.y,
            InvDimZ = itemDef.inventoryOccupancyDimensions.z,
            TagsText = string.IsNullOrEmpty(itemDef.Tags) ? null : itemDef.Tags,
            RequiredTagsText = string.IsNullOrEmpty(itemDef.RequiredTags) ? null : itemDef.RequiredTags,
            PowerGeneration = powerGen,
            PowerConsumption = powerCons,
            CoolantGeneration = coolantGen,
            CoolantConsumption = coolantCons,
            ResourceDeltasJson = resourceJson,
            DistortionDecayDelay = distortionParams?.DecayDelay,
            DistortionDecayRate = distortionParams?.DecayRate,
            DistortionMaximum = distortionParams?.Maximum,
            ShieldHealth = shieldParams?.MaxShieldHealth,
            ShieldRegen = shieldParams?.MaxShieldRegen,
            ShieldDecayRatio = shieldParams?.DecayRatio,
            ShieldDownedRegenDelay = shieldParams?.DownedRegenDelay,
            ShieldDamagedRegenDelay = shieldParams?.DamagedRegenDelay,
            ShieldResistancesJson = shieldResistJson,
            JumpAlignmentRate = jumpDriveParams?.alignmentRate,
            JumpAlignmentDecayRate = jumpDriveParams?.alignmentDecayRate,
            JumpTuningRate = jumpDriveParams?.tuningRate,
            JumpTuningDecayRate = jumpDriveParams?.tuningDecayRate,
            JumpFuelUsageEfficiency = jumpDriveParams?.fuelUsageEfficiencyMultiplier,
            SignalInfrared = armorParams?.signalInfrared,
            SignalElectromagnetic = armorParams?.signalElectromagnetic,
            SignalCrossSection = armorParams?.signalCrossSection,
            ArmorMultPhysical = armorParams?.damageMultiplier is DamageInfo di ? di.DamagePhysical : null,
            ArmorMultEnergy = armorParams?.damageMultiplier is DamageInfo di2 ? di2.DamageEnergy : null,
            ArmorMultDistortion = armorParams?.damageMultiplier is DamageInfo di3 ? di3.DamageDistortion : null,
            ArmorMultThermal = armorParams?.damageMultiplier is DamageInfo di4 ? di4.DamageThermal : null,
            ArmorMultBiochemical = armorParams?.damageMultiplier is DamageInfo di5 ? di5.DamageBiochemical : null,
            ArmorMultStun = armorParams?.damageMultiplier is DamageInfo di6 ? di6.DamageStun : null,
            WeaponAmmoRef = weaponParams?.ammoContainerRecord?.selfId.ToString(),
            FuelCapacity = fuelCap,
            DamagePhysical = dmgPhysical,
            DamageEnergy = dmgEnergy,
            DamageDistortion = dmgDistortion,
            DamageThermal = dmgThermal,
            DamageBiochemical = dmgBiochemical,
            DamageStun = dmgStun
        };
    }

    private (int? PowerGen, int? PowerCons, float? CoolantGen, float? CoolantCons, float? FuelCap, string? Json)
        ExtractResourceDeltas(ItemResourceComponentParams? resourceParams)
    {
        int? powerGen = null;
        int? powerCons = null;
        float? coolantGen = null;
        float? coolantCons = null;
        float? fuelCap = null;
        var unmatched = new List<string>();

        if (resourceParams?.states == null)
            return (powerGen, powerCons, coolantGen, coolantCons, fuelCap, null);

        foreach (var state in resourceParams.states)
        {
            if (state.deltas == null)
                continue;

            foreach (var delta in state.deltas)
            {
                if (delta == null)
                    continue;

                var handled = false;

                if (delta is ItemResourceDeltaGeneration gen)
                {
                    var resEnum = gen.generation.resource;
                    var amount = gen.generation.resourceAmountPerSecond;
                    if (resEnum == ResourceNetworkResource.Power)
                    {
                        if (amount is SPowerSegmentResourceUnit pwr)
                        {
                            powerGen = (powerGen ?? 0) + pwr.units;
                            handled = true;
                        }
                    }
                    else if (resEnum == ResourceNetworkResource.Coolant)
                    {
                        if (amount is SStandardResourceUnit std)
                        {
                            coolantGen = (coolantGen ?? 0f) + std.standardResourceUnits;
                            handled = true;
                        }
                    }
                }
                else if (delta is ItemResourceDeltaConsumption cons)
                {
                    var resEnum = cons.consumption.resource;
                    var amount = cons.consumption.resourceAmountPerSecond;
                    if (resEnum == ResourceNetworkResource.Power)
                    {
                        if (amount is SPowerSegmentResourceUnit pwr)
                        {
                            powerCons = (powerCons ?? 0) + pwr.units;
                            handled = true;
                        }
                    }
                    else if (resEnum == ResourceNetworkResource.Coolant)
                    {
                        if (amount is SStandardResourceUnit std)
                        {
                            coolantCons = (coolantCons ?? 0f) + std.standardResourceUnits;
                            handled = true;
                        }
                    }
                }
                else if (delta is ItemResourceDeltaConversion conversion)
                {
                    handled = true;
                }
                else if (delta is ItemResourceDeltaStorage storage)
                {
                    var resEnum = storage.consumption.resource;
                    var amount = storage.consumption.resourceAmountPerSecond;
                    if (resEnum == ResourceNetworkResource.Fuel)
                    {
                        if (amount is SStandardResourceUnit std)
                        {
                            fuelCap = (fuelCap ?? 0f) + std.standardResourceUnits;
                            handled = true;
                        }
                    }
                }

                if (!handled)
                {
                    unmatched.Add(delta.GetType().Name);
                }
            }
        }

        string? json = null;
        if (unmatched.Count > 0)
        {
            json = System.Text.Json.JsonSerializer.Serialize(unmatched);
        }

        return (powerGen, powerCons, coolantGen, coolantCons, fuelCap, json);
    }

    private (float? Physical, float? Energy, float? Distortion, float? Thermal, float? Biochemical, float? Stun)
        ExtractItemDamage(SAmmoContainerComponentParams? ammoContainer, SCItemMissileParams? missileParams, SCItemWeaponComponentParams? weaponParams)
    {
        DamageInfo? damageInfo = null;
        bool hasNonZeroDamage = false;

        // Weapon: extract from ammo container projectile params (standard projectile damage)
        if (ammoContainer?.ammoParamsRecord?.projectileParams != null)
        {
            var proj = ammoContainer.ammoParamsRecord.projectileParams;
            damageInfo = proj switch
            {
                BulletProjectileParams bullet => bullet.damage as DamageInfo,
                TachyonProjectileParams tachyon => tachyon.damage as DamageInfo,
                _ => null
            };
            if (damageInfo != null)
            {
                hasNonZeroDamage = damageInfo.DamagePhysical + damageInfo.DamageEnergy
                    + damageInfo.DamageDistortion + damageInfo.DamageThermal
                    + damageInfo.DamageBiochemical + damageInfo.DamageStun > 1f;
            }
        }

        // Weapon: extract from detonation explosion (Jericho, Suckerpunch – explosive ordnance)
        if (!hasNonZeroDamage && ammoContainer?.ammoParamsRecord?.projectileParams?.detonationParams?.explosionParams?.damage is DamageInfo detonationDamage)
        {
            damageInfo = detonationDamage;
            hasNonZeroDamage = true;
        }

        // Weapon: extract from beam fire actions (Supremacy-10T Laser Beam – damagePerSecond)
        if (!hasNonZeroDamage && weaponParams?.fireActions != null)
        {
            foreach (var action in weaponParams.fireActions)
            {
                if (action is SWeaponActionFireBeamParams beamAction && beamAction.damagePerSecond is DamageInfo beamDamage)
                {
                    damageInfo = beamDamage;
                    hasNonZeroDamage = true;
                    break;
                }
            }
        }

        // Missile: extract from explosion params
        if (!hasNonZeroDamage && missileParams?.explosionParams?.damage is DamageInfo missileDamage)
        {
            damageInfo = missileDamage;
            hasNonZeroDamage = true;
        }

        if (damageInfo == null)
            return (null, null, null, null, null, null);

        return (
            damageInfo.DamagePhysical,
            damageInfo.DamageEnergy,
            damageInfo.DamageDistortion,
            damageInfo.DamageThermal,
            damageInfo.DamageBiochemical,
            damageInfo.DamageStun
        );
    }

    private string ResolveScItemManufacturerId(
        SItemDefinition itemDef,
        Dictionary<string, ManufacturerEntity> manufacturerCache)
    {
        var manufacturer = itemDef.Manufacturer;

        if (manufacturer == null)
            return GetOrCreateUnknownManufacturer(manufacturerCache, null).Id;

        var manufacturerId = !string.IsNullOrEmpty(manufacturer.Code)
            ? manufacturer.Code
            : (!string.IsNullOrEmpty(manufacturer.Localization.Name)
                ? manufacturer.Localization.Name
                : "Unknown");

        if (manufacturerId == "Unknown")
            return GetOrCreateUnknownManufacturer(manufacturerCache, null).Id;

        if (!manufacturerCache.TryGetValue(manufacturerId, out var entity))
        {
            var nameKey = !string.IsNullOrEmpty(manufacturer.Localization.Name)
                ? manufacturer.Localization.Name
                : manufacturerId;
            var descKey = !string.IsNullOrEmpty(manufacturer.Localization.Description)
                ? manufacturer.Localization.Description
                : string.Empty;

            entity = new ManufacturerEntity
            {
                Id = manufacturerId,
                Name = Task.Run(async () => await _p4kService.GetLocaleValue(nameKey)).Result ?? manufacturerId,
                NameKey = nameKey,
                Description = Task.Run(async () => await _p4kService.GetLocaleValue(descKey)).Result ?? string.Empty,
                DescriptionKey = descKey,
                Logo = manufacturer.Logo ?? string.Empty
            };
            manufacturerCache[manufacturerId] = entity;
        }

        return manufacturerId;
    }

    private void EnsureUnknownManufacturer(Dictionary<string, ManufacturerEntity> cache)
    {
        if (!cache.ContainsKey("Unknown"))
        {
            cache["Unknown"] = new ManufacturerEntity { Id = "Unknown", Name = "Unknown" };
        }
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
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Missions
                .AsNoTracking()
                .Where(m => m.ShipRequirements.Any(sr => sr.ShipGuid == shipGuid))
                .ToListAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task<List<ShipEntity>> GetShipsForMissionAsync(string missionId)
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Ships
                .AsNoTracking()
                .Where(s => s.MissionRequirements.Any(mr => mr.MissionId == missionId))
                .ToListAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task<ShipEntity?> GetShipByGuidAsync(string entityClassGuid)
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Ships
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.EntityClassGuid == entityClassGuid);
        }
        finally { _dbLock.Release(); }
    }

    public async Task<List<ManufacturerEntity>> GetManufacturersAsync()
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Manufacturers
                .AsNoTracking()
                .ToListAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task<List<ShipLoadoutEntryEntity>> GetShipLoadoutAsync(string shipGuid)
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.ShipLoadoutEntries
                .AsNoTracking()
                .Where(sle => sle.ShipGuid == shipGuid)
                .ToListAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task<List<ShipEntity>> GetShipsAsync()
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
            return await db.Ships
                .AsNoTracking()
                .Include(s => s.ShipTags)
                    .ThenInclude(st => st.Tag)
                .Include(s => s.Manufacturer)
                .ToListAsync();
        }
        finally { _dbLock.Release(); }
    }

    public async Task<List<DbBlueprintRow>> GetBlueprintsAsync(HashSet<string>? obtainedBlueprintIds = null)
    {
        var result = new List<DbBlueprintRow>();
        await foreach (var row in GetBlueprintsBatchedAsync().ConfigureAwait(false))
        {
            result.Add(row);
        }
        return result;
    }

    public async IAsyncEnumerable<DbBlueprintRow> GetBlueprintsBatchedAsync(int batchSize = 200, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _dbLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();

        var totalBlueprints = await db.Blueprints.LongCountAsync(cancellationToken).ConfigureAwait(false);
        int offset = 0;

        while (offset < totalBlueprints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await db.Blueprints
                .OrderBy(b => b.BlueprintName)
                .Skip(offset)
                .Take(batchSize)
                .Include(b => b.Costs)
                    .ThenInclude(c => c.Modifiers)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batch.Count == 0)
                break;

            var blueprintIds = batch.Select(b => b.SelfId).ToHashSet();

            var missionPoolsMap = new Dictionary<string, List<DbMissionPoolRow>>();
            if (blueprintIds.Count > 0)
            {
                var pools = await db.MissionBlueprintEntries
                    .Where(e => blueprintIds.Contains(e.BlueprintId))
                    .Include(e => e.Pool)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var missionIds = pools.Select(e => e.Pool.MissionId).ToHashSet();
                var missions = await db.Missions
                    .Where(m => missionIds.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, m => m, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var entry in pools)
                {
                    var bpId = entry.BlueprintId;
                    if (!missionPoolsMap.TryGetValue(bpId, out var list))
                    {
                        list = new List<DbMissionPoolRow>();
                        missionPoolsMap[bpId] = list;
                    }

                    if (missions.TryGetValue(entry.Pool.MissionId, out var mission))
                    {
                        list.Add(new DbMissionPoolRow(entry.Pool.PoolName, mission.Title, mission.DebugName));
                    }
                }
            }

            foreach (var bp in batch)
            {
                var costRows = bp.Costs.Select(c => new DbBlueprintCostRow(
                    c.CostType,
                    c.CostName,
                    c.ResourceRef,
                    c.ResourceAmount,
                    c.ItemEntityClassRef,
                    c.ItemCount,
                    c.MinQuality,
                    c.ResourceName,
                    c.ItemName,
                    c.Modifiers.Select(m => new DbBlueprintModifierRow(
                        m.RangeType,
                        m.PropertyName,
                        m.StartQuality,
                        m.EndQuality,
                        m.ModifierStart,
                        m.ModifierEnd
                    )).ToArray()
                )).ToArray();

                missionPoolsMap.TryGetValue(bp.SelfId, out var missionPools);

                yield return new DbBlueprintRow(
                    bp.SelfId,
                    bp.BlueprintName,
                    bp.CategoryName,
                    bp.ProcessType,
                    bp.OutputEntityClassRef,
                    bp.CraftDuration,
                    costRows,
                    missionPools?.ToArray() ?? Array.Empty<DbMissionPoolRow>()
                );
            }

             offset += batch.Count;
            await Task.CompletedTask;
        }
        }
        finally { _dbLock.Release(); }
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
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
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
        finally { _dbLock.Release(); }
    }

    /// <summary>
    /// Get all missions grouped by category with full navigation properties loaded.
    /// </summary>
    public async Task<Dictionary<string, List<MissionEntity>>> GetAllMissionCategoriesWithMissionsAsync()
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync();
        var missions = await db.Missions
            .AsNoTracking()
            .Include(m => m.Category)
            .Include(m => m.Contractor)
            .Include(m => m.Objectives)
            .Include(m => m.Prerequisites)
            .Include(m => m.Tokens)
            .Include(m => m.ShipRequirements)
                .ThenInclude(sr => sr.Ship)
            .Include(m => m.ShipSpawns)
                .ThenInclude(sp => sp.Tags)
                    .ThenInclude(st => st.Tag)
            .Include(m => m.Rewards)
            .Include(m => m.RequiredTags)
                .ThenInclude(rt => rt.Tag)
            .Include(m => m.CompletionTags)
                .ThenInclude(ct => ct.Tag)
            .Include(m => m.BlueprintPools)
            .ToListAsync();

        return missions
            .GroupBy(m => m.Category?.Id ?? "Uncategorized")
            .ToDictionary(
                g => g.Key,
                g => g.ToList()
            );
        }
        finally { _dbLock.Release(); }
    }
}
