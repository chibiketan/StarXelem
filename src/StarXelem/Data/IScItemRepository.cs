using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public interface IScItemRepository
{
    Task<ScItemEntity?> GetByCrc32Async(uint crc32);

    /// <summary>
    /// Associe le nom court de chaque objet (« NomCourt » de « EntityClassDefinition.NomCourt ») au CRC32C de son enregistrement.
    /// Sert à résoudre les entrées de loadout qui désignent un objet par son nom de classe.
    /// </summary>
    Task<IReadOnlyDictionary<string, uint>> GetCrcByShortNameAsync();
}
