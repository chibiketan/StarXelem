using System.Collections;
using System.ComponentModel;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarBreaker.Extraction;
using StarBreaker.FileSystem;
using StarBreaker.P4k;
using StarXelem.Models;

namespace StarXelem.Services;

public interface IP4kService : INotifyPropertyChanged
{
    P4kDirectoryNode P4KFileSystem { get; }
    
    P4kFileModel? SelectedP4KFile { get; set; }
    
    event EventHandler<P4kFileModel?>? SelectedP4KFileChanged;

    // Nouveaux champs exposés
    P4kService.P4kFileLoadState FileLoadState { get; }
    string? GetLastErrorMessage();
    
    Task OpenP4k(string path, IProgress<double> p4kProgress, IProgress<double> fileSystemProgress);
    Task<IList<P4kFileModel>> FindInstalledFiles();
    
    /// <summary>
    /// Récupère les informations sur une installation de SC via le chemin de son fichier data.p4k
    /// </summary>
    /// <param name="p4kPath">Chemin vers le fichier</param>
    /// <returns>les informations ou null si ce n'est pas une installation valide</returns>
    Task<P4kFileModel?> GetInstallationInfo(string p4kPath);

    /// <summary>
    /// Retourne la valeur texte qui correspond à la clé passée en paramètre
    /// </summary>
    /// <param name="key">La clé à chercher</param>
    /// <returns>La valeur trouvée qui correspond à la clé, sinon null</returns>
    Task<string?> GetLocaleValue(string? key);
    Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc);
    Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc, int depth);

    /// <summary>
    /// Retourne l'ensemble des enregistrements de type EntityClassDefinition
    /// </summary>
    /// <returns>UUne énumération asynchronie des enregistrements</returns>
    IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinition(int depth);

    /// <summary>
    /// Comme <see cref="GetAllEntityClassDefinition"/>, mais évalue d'abord <paramref name="predicate"/> à
    /// une profondeur légère (<paramref name="filterDepth"/>) et n'étend jusqu'à <paramref name="finalDepth"/>
    /// que les enregistrements retenus, pour éviter de matérialiser en profondeur des entités non pertinentes.
    /// </summary>
    IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinitionFiltered(
        int filterDepth, int finalDepth, Func<EntityClassDefinition, bool> predicate);

    /// <summary>
    /// Provoque le lancement de l'alimentation des différents cache ce qui permet d'avoir des données prêtes et une requête rapide
    /// </summary>
    /// <returns></returns>
    Task FillDataCache();

    Task<List<DataCoreTypedRecord>> GetAllContractGenerator();
    Task<string?> GetEntityClassName(EntityClassDefinition? entityClass);
    Task<DataCoreTypedRecord> GetRecordWithFullHistory(CigGuid recordId);
    
    /// <summary>
    /// Retourne la base de données des tags
    /// </summary>
    /// <returns></returns>
    Task<TagDatabase> GetTagDatabase();
    
    /// <summary>
    /// Récupère un enregistrement avec un minimum une profondeur de données spécifique
    /// </summary>
    /// <param name="recordId">ID de l'enregistrement à récupérer</param>
    /// <param name="depth">Profondeur d'historique de données minium</param>
    /// <returns>L'enregistrement demandé</returns>
    Task<DataCoreTypedRecord?> GetRecordWithSpecificDepth(CigGuid recordId, int depth);

    Task<List<DataCoreTypedRecord>> GetAllFactionReputations();
    Task<List<DataCoreTypedRecord>> GetAllCraftingBlueprintRecord();
    Task<List<DataCoreTypedRecord>> GetAllStarMapObjects();
    Task<List<DataCoreTypedRecord>> EnsureRecordsDepthAsync(IEnumerable<DataCoreTypedRecord> records, int depth);

    /// <summary>
    /// Récupère un enregistrement arbitraire par son CigGuid (utile pour ResourceType, MineableComposition, etc.)
    /// </summary>
    Task<object?> GetRecordById(CigGuid recordId);

    /// <summary>
    /// Vide le cache lourd des enregistrements EntityClassDefinition (chargés en profondeur pour toutes les
    /// entités du jeu). Ce cache sera reconstruit paresseusement (profondeur minimale) au prochain accès.
    /// À utiliser après une opération massive (ex: reconstruction de la BDD locale) pour libérer la mémoire.
    /// </summary>
    void ReleaseHeavyCache();
}

