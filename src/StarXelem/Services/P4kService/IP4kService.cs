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
    /// Retourne la valeur texte qui correspond à la clé passée en paramètre
    /// </summary>
    /// <param name="key">La clé à chercher</param>
    /// <returns>La valeur trouvée qui correspond à la clé, sinon null</returns>
    Task<string?> GetLocaleValue(string? key);
    Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc);
}