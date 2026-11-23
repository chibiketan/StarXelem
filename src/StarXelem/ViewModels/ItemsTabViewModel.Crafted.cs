using Microsoft.Extensions.Logging;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;
using StarXelem.Services.ShipLoadout;

namespace StarXelem.ViewModels;

public partial class ItemsTabViewModel
{
    // Profondeur suffisante pour les valeurs de base des facteurs de craft (paramètres du moteur quantique, santé).
    private const int CraftedDefinitionDepth = 3;

    /// <summary>
    /// Décode les données de craft d'un objet (valeurs améliorées, matériaux). Appelé une seule fois par objet, à l'affichage de sa marque ou de
    /// son détail : la résolution passe par DataCore.
    /// </summary>
    internal async Task<CraftedItemInfo?> ReadCraftedInfoAsync(EntityItemQueryResult item)
    {
        try
        {
            var properties = item.EntityNodeProperties;
            var record = await _p4kService.GetEntityType(properties.ClassGuidCrc, CraftedDefinitionDepth).ConfigureAwait(false);
            return await _craftedReader.ReadAsync(item.EntitySnapshot?.Variables, (EItemType)properties.ItemTypeEnum, record?.Data as EntityClassDefinition)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lecture des données de craft impossible pour l'objet {Id}", item.EntityNodeProperties?.Geid);
            return null;
        }
    }
}
