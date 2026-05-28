using Microsoft.Extensions.Logging;
using Sc.External.Services.BlueprintLibrary.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.ViewModels;

namespace StarXelem.Services;

/// <summary>
/// Implémentation de <see cref="IBlueprintMappingService"/> qui lit les données du P4K
/// et construit les ViewModel pour chaque blueprint.
/// </summary>
internal sealed class BlueprintMappingService : IBlueprintMappingService
{
    private readonly ILogger<BlueprintMappingService> _logger;
    private readonly IP4kService _p4kService;
    private readonly IEntityClassDefinitionService _entityClassDefinitionService;

    /// <summary>Libellé de repli quand un nom localisé est introuvable dans le P4K.</summary>
    private const string UnknownLabel = "Inconnu";

    /// <summary>
    /// Initialise une nouvelle instance de <see cref="BlueprintMappingService"/>.
    /// </summary>
    /// <param name="p4kService">Service d'accès aux données du fichier Data.p4k (records, chaînes localisées).</param>
    /// <param name="entityClassDefinitionService">Service de résolution du type et sous-type d'une entité.</param>
    /// <param name="logger">Journaliseur pour les avertissements de parsing.</param>
    public BlueprintMappingService(
        IP4kService p4kService,
        IEntityClassDefinitionService entityClassDefinitionService,
        ILogger<BlueprintMappingService> logger)
    {
        _p4kService = p4kService;
        _entityClassDefinitionService = entityClassDefinitionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<BlueprintViewModel>> TransformBlueprintsAsync(IEnumerable<BlueprintEntry> entries)
    {
        var result = new List<BlueprintViewModel>();

        foreach (var entry in entries)
        {
            var vm = await TransformBlueprintAsync(entry);
            if (vm is not null)
                result.Add(vm);
        }

        return result;
    }

    /// <summary>
    /// Transforme une entrée blueprint brute en <c>BlueprintViewModel</c>.
    /// </summary>
    /// <param name="entry">L'entrée brute fournie par l'API gRPC (identifiant + utilisations restantes).</param>
    /// <returns>
    /// Un <c>BlueprintViewModel</c> complet, ou <c>null</c> si le record P4K est introuvable ou invalide.
    /// </returns>
    /// <remarks>
    /// La méthode effectue les étapes suivantes :
    /// <list type="number">
    ///   <item>Récupère le <c>CraftingBlueprintRecord</c> via <see cref="IP4kService"/> (profondeur 1).</item>
    ///   <item>Extrait la classe d'entité produite et la durée de fabrication.</item>
    ///   <item>Parcourt chaque catégorie de coût (ressources + modificateurs de stats).</item>
    ///   <item>Résout le nom localisé et le type/sous-type de l'objet fini.</item>
    /// </list>
    /// </remarks>
    private async Task<BlueprintViewModel?> TransformBlueprintAsync(BlueprintEntry entry)
    {
        var bpRecord = await _p4kService.GetRecordWithSpecificDepth(new CigGuid(entry.BlueprintId), 1);
        var b = bpRecord?.Data as CraftingBlueprintRecord;

        if (b is null || b.blueprint is not CraftingBlueprint craftingBlueprint)
        {
            _logger.LogWarning("Failed to get blueprint record for {BlueprintId}", entry.BlueprintId);
            return null;
        }

        var craftedItem = (craftingBlueprint.processSpecificData as CraftingProcess_Creation)?.entityClass;

        var craftingRecipe = craftingBlueprint.tiers.OfType<CraftingBlueprintTier>().FirstOrDefault()?.recipe as CraftingRecipe;
        var costs = craftingRecipe!.costs as CraftingRecipeCosts;
        TimeSpan duration = TimeSpan.Zero;
        switch (costs?.craftTime)
        {
            case null:
                break;
            case TimeValue_LongSeconds timeValueLongSeconds:
                duration = TimeSpan.FromSeconds(timeValueLongSeconds.seconds);
                break;
            case TimeValue_Partitioned timeValuePartitioned:
                duration = new TimeSpan(timeValuePartitioned.days, timeValuePartitioned.hours, timeValuePartitioned.minutes, (int)timeValuePartitioned.seconds);
                break;
            default:
                _logger.LogWarning("Unknown cost type : {type}", costs.craftTime.GetType().FullName);
                break;
        }

        var categoryList = new List<BlueprintCategoryModel>();
        var craftingCost = costs!.mandatoryCost as CraftingCost_Select;
        foreach (var craftingCostOption in craftingCost!.options)
        {
            switch (craftingCostOption)
            {
                case null:
                    break;
                case CraftingCost_Select craftingCostSelect:
                    var categoryName = await _p4kService.GetLocaleValue(craftingCostSelect.nameInfo.displayName);
                    var materialList = new List<BlueprintMaterialModel>();

                    // Parcours de chaque option de coût : ressources brutes (Resource) et objets finis (Item)
                    foreach (var costOption in craftingCostSelect.options)
                    {
                        switch (costOption)
                        {
                            case null:
                                break;

                            // Ressource brute (ex : Fer 0,2 SCU) — quantité exprimée en SCU
                            case CraftingCost_Resource resourceCost:
                                materialList.Add(new BlueprintResourceModel
                                {
                                    Name = await _p4kService.GetLocaleValue(resourceCost.resource?.displayName) ?? UnknownLabel,
                                    QuantityInScu = (resourceCost.quantity as SStandardCargoUnit)?.standardCargoUnits ?? -1.0f
                                });
                                break;

                            // Objet spécifique (ex : minerai Sadaryx x4) — quantité physique d'objets
                            case CraftingCost_Item itemCost:
                                materialList.Add(new BlueprintItemModel
                                {
                                    Name = await _p4kService.GetEntityClassName(itemCost.entityClass) ?? UnknownLabel,
                                    Quantity = itemCost.quantity
                                });
                                break;

                            default:
                                _logger.LogWarning("Type de coût non reconnu dans une catégorie : {type}", costOption.GetType().FullName);
                                break;
                        }
                    }

                    var statModifierList = new List<BlueprintStatModelBase>();
                    foreach (var modifierContext in craftingCostSelect.context.OfType<CraftingCostContext_ResultGameplayPropertyModifiers>())
                    {
                        statModifierList.AddRange(
                            await ExtractStatModifiersAsync(modifierContext));
                    }

                    categoryList.Add(new BlueprintCategoryModel
                    {
                        Name = categoryName!,
                        MaterialList = materialList,
                        StatModifierList = statModifierList
                    });

                    break;
                default:
                    _logger.LogWarning("Unknown cost option type : {type}", craftingCostOption.GetType().FullName);
                    break;
            }
        }

        var name = await _p4kService.GetEntityClassName(craftedItem) ?? UnknownLabel;
        var types = _entityClassDefinitionService.GetType(craftedItem);

        return new BlueprintViewModel
        {
            BlueprintId = entry.BlueprintId,
            Name = name,
            TierLevel = 1,
            CraftDuration = duration,
            RemainingUse = entry.RemainingUses,
            CategoryList = categoryList,
            Type = types.type,
            Subtype = types.subtype
        };
    }

    /// <summary>
    /// Extrait les modificateurs de stats d'un contexte de résultat de fabrication.
    /// </summary>
    /// <param name="modifierContext">
    /// Le contexte contenant les modificateurs bruts définis sur un coût de fabrication.
    /// </param>
    /// <returns>
    /// Une liste de <c>BlueprintStatLinearModel</c> ou <c>BlueprintStatAdditiveModel</c>,
    /// selon le type de données présent dans le P4K.
    /// </returns>
    /// <remarks>
    /// Deux formats sont reconnus (SC 4.8) :
    /// <list type="bullet">
    ///   <item><term>Linear</term> — plages de valeur continues. Seuls le début de la première plage
    ///   et la fin de la dernière sont conservés (Min / Max).</item>
    ///   <item><term>LinearIntegerAdditive</term> — bandes de qualité discrètes. Chaque bande
    ///   conserve son intervalle [startQuality, endQuality] et sa valeur additive.</item>
    /// </list>
    /// Les modificateurs dont le type ne correspond à aucun format sont ignorés
    /// (un avertissement est journalisé).
    /// </remarks>
    private async Task<List<BlueprintStatModelBase>> ExtractStatModifiersAsync(
        CraftingCostContext_ResultGameplayPropertyModifiers modifierContext)
    {
        var statModifiers = new List<BlueprintStatModelBase>();

        foreach (var rawModifier in (modifierContext.gameplayPropertyModifiers as CraftingGameplayPropertyModifiers_List)!.gameplayPropertyModifiers)
        {
            var modifier = rawModifier as CraftingGameplayPropertyModifierCommon;

            if (modifier is null)
            {
                _logger.LogWarning("Modificateur non castable en CraftingGameplayPropertyModifierCommon pour {Type}", rawModifier?.GetType().FullName);
                continue;
            }

            var propertyName = await _p4kService.GetLocaleValue(modifier.gameplayPropertyRecord?.propertyName);
            var statName = propertyName ?? UnknownLabel;

            var linearRanges = modifier.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_Linear>().ToList();
            if (linearRanges is { Count: > 0 })
            {
                statModifiers.Add(new BlueprintStatLinearModel
                {
                    Name = statName,
                    // En SC 4.8, les plages linéaires forment une progression continue ;
                    // on affiche uniquement le début de la première plage et la fin de la dernière.
                    Min = linearRanges[0].modifierAtStart,
                    Max = linearRanges[^1].modifierAtEnd
                });
            }
            else
            {
                var additiveRanges = modifier.valueRanges.OfType<CraftingGameplayPropertyModifierValueRange_LinearIntegerAdditive>().ToList();
                if (additiveRanges is { Count: > 0 })
                {
                    statModifiers.Add(new BlueprintStatAdditiveModel
                    {
                        Name = statName,
                        Bands = additiveRanges.Select(r => new BlueprintStatBandModel
                        {
                            StartQuality = r.startQuality,
                            EndQuality = r.endQuality,
                            // En SC 4.8, additiveModifierAtStart == additiveModifierAtEnd pour toutes les bandes ;
                            // start est utilisé par convention.
                            Value = r.additiveModifierAtStart
                        }).ToList()
                    });
                }
                else
                {
                    _logger.LogWarning("Aucun range de modificateur reconnu pour la propriété {Name}", statName);
                }
            }
        }

        return statModifiers;
    }
}
