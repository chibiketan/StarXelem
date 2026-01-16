using StarBreaker.DataCoreGenerated;
using StarBreaker.FileSystem;
using StarBreaker.P4k;
using StarXelem.Models;

namespace StarXelem.Services;

public interface IP4kService
{
    P4kDirectoryNode P4KFileSystem { get; }
    
    P4kFileModel? SelectedP4KFile { get; set; }
    
    event EventHandler<P4kFileModel?>? SelectedP4KFileChanged;
    
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

    /// <summary>
    /// Retourne l'ensemble des enregistrements de type EntityClassDefinition
    /// </summary>
    /// <returns>UUne énumération asynchronie des enregistrements</returns>
    IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinition();
}