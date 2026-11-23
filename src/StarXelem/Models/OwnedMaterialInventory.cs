namespace StarXelem.Models;

/// <summary>
/// Instantané des matériaux possédés, publié par l'écran Objets après chaque chargement. Immuable : il peut être lu
/// hors du thread d'UI.
/// </summary>
public sealed class OwnedMaterialInventory
{
    private readonly ILookup<string, OwnedMaterialLot> _byResource;
    private readonly ILookup<string, OwnedMaterialLot> _byClass;

    /// <summary>
    /// Construit l'instantané et ses index de recherche.
    /// </summary>
    /// <param name="lots">Objets candidats (conteneurs de ressources, objets à l'unité).</param>
    /// <param name="loadedAt">Heure du relevé.</param>
    /// <param name="itemCount">Nombre total d'objets chargés, matériaux ou non.</param>
    /// <param name="areLocationsResolved">Vrai une fois tous les emplacements résolus.</param>
    public OwnedMaterialInventory(IReadOnlyList<OwnedMaterialLot> lots, DateTime loadedAt, int itemCount, bool areLocationsResolved)
    {
        Lots = lots;
        LoadedAt = loadedAt;
        ItemCount = itemCount;
        AreLocationsResolved = areLocationsResolved;
        _byResource = lots.Where(lot => lot.ResourceRef != null).ToLookup(lot => lot.ResourceRef!, StringComparer.OrdinalIgnoreCase);
        _byClass = lots.Where(lot => lot.ClassRecordId != null).ToLookup(lot => lot.ClassRecordId!, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Objets candidats.</summary>
    public IReadOnlyList<OwnedMaterialLot> Lots { get; }

    /// <summary>Heure du relevé.</summary>
    public DateTime LoadedAt { get; }

    /// <summary>Nombre total d'objets chargés.</summary>
    public int ItemCount { get; }

    /// <summary>Vrai une fois tous les emplacements résolus.</summary>
    public bool AreLocationsResolved { get; }

    /// <summary>
    /// Lots contenant une ressource, d'après la ressource déclarée par leur classe. Les ressources raffinées sont rangées
    /// dans des boîtes propres à leur type ; seuls les sacs de minage sont mixtes, et leur minerai brut n'est pas utilisable.
    /// </summary>
    /// <param name="resourceRef">selfId du <c>ResourceType</c> demandé par le blueprint.</param>
    public IEnumerable<OwnedMaterialLot> FindResourceLots(string resourceRef) => _byResource[resourceRef];

    /// <summary>Lots d'une classe d'objet précise (coût « Item » : gemmes, minerais à l'unité…).</summary>
    /// <param name="classRecordId">RecordId de la classe d'objet demandée par le blueprint.</param>
    public IEnumerable<OwnedMaterialLot> FindItemLots(string classRecordId) => _byClass[classRecordId];
}
