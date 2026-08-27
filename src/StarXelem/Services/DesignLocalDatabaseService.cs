using System.Runtime.CompilerServices;
using StarXelem.Data;

namespace StarXelem.Services;

/// <summary>
/// Implémentation inerte de <see cref="ILocalDatabaseService"/> utilisée uniquement
/// en mode design (Avalonia <c>Design.IsDesignMode</c>).
/// Ne touche jamais au fichier SQLite : les rebuilds sont des no-op et les getters
/// renvoient des collections vides. Les aperçus design sont peuplés par <c>DesignData</c>.
/// </summary>
public class DesignLocalDatabaseService : ILocalDatabaseService
{
    public Task RebuildDbAsync(IProgress<RebuildProgress>? progress = null) => Task.CompletedTask;

    public Task<bool> NeedsRebuildCheckAsync() => Task.FromResult(false);

    public Task EnsureDbAsync(IProgress<RebuildProgress>? progress = null) => Task.CompletedTask;

    public Task<List<MissionEntity>> GetMissionsForShipAsync(string shipGuid)
        => Task.FromResult(new List<MissionEntity>());

    public Task<List<ShipEntity>> GetShipsForMissionAsync(string missionDebugName)
        => Task.FromResult(new List<ShipEntity>());

    public Task<(Dictionary<string, string> TitleSuffixMap, Dictionary<string, Dictionary<string, HashSet<string>>> DescriptionAppendMap)> GetBlueprintRewardMapsAsync(HashSet<string>? obtainedBlueprintIds = null)
        => Task.FromResult((
            new Dictionary<string, string>(),
            new Dictionary<string, Dictionary<string, HashSet<string>>>()));

    public Task<List<DbBlueprintRow>> GetBlueprintsAsync(HashSet<string>? obtainedBlueprintIds = null)
        => Task.FromResult(new List<DbBlueprintRow>());

    public async IAsyncEnumerable<DbBlueprintRow> GetBlueprintsBatchedAsync(int batchSize = 200, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<ShipEntity?> GetShipByGuidAsync(string entityClassGuid)
        => Task.FromResult<ShipEntity?>(null);

    public Task<List<ManufacturerEntity>> GetManufacturersAsync()
        => Task.FromResult(new List<ManufacturerEntity>());

    public Task<List<ShipLoadoutEntryEntity>> GetShipLoadoutAsync(string shipGuid)
        => Task.FromResult(new List<ShipLoadoutEntryEntity>());

    public Task<List<ShipEntity>> GetShipsAsync()
        => Task.FromResult(new List<ShipEntity>());

    public Task<Dictionary<string, List<MissionEntity>>> GetAllMissionCategoriesWithMissionsAsync()
        => Task.FromResult(new Dictionary<string, List<MissionEntity>>());
}
