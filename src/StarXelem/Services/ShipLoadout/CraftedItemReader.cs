using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using Google.Protobuf.Collections;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Models;

namespace StarXelem.Services.ShipLoadout;

/// <summary>
/// Lit la variable persistante « Crafting.PersistentItemData » d'un objet crafté (structure C++ Crafting::SCraftedItemPersistentData) et la
/// rend affichable : modificateurs résolus en CraftingGameplayPropertyDef (nom localisé, transformation d'affichage, unité) et matériaux utilisés.
/// Format et vérification en LIVE : Documents/2026-11-23-analyse-objets-craftes.md.
/// </summary>
public sealed partial class CraftedItemReader
{
    /// <summary>CRC32 (zlib) de « crafting.persistentitemdata » : nom de la variable en minuscules.</summary>
    public const uint CraftedVariableNameCrc = 2174272695;

    private const int RecordDepth = 2;

    private readonly IP4kService _p4kService;
    private readonly ConcurrentDictionary<CigGuid, Task<DataCoreTypedRecord?>> _records = new();

    public CraftedItemReader(IP4kService p4kService)
    {
        _p4kService = p4kService;
    }

    /// <summary>Lit les données de craft parmi les variables d'une entité, ou renvoie <c>null</c> si l'objet n'est pas crafté.</summary>
    /// <param name="variables">Variables persistantes (EntitySnapshot.Variables ou PersistentEntityProperties.Variables).</param>
    /// <param name="itemType">Type de l'objet, pour les noms de propriété propres à un type (dégâts d'un laser de minage…).</param>
    /// <param name="definition">Définition de l'objet, pour les valeurs de base auxquelles s'appliquent les facteurs.</param>
    public async Task<CraftedItemInfo?> ReadAsync(RepeatedField<EntityVariable>? variables, EItemType? itemType, EntityClassDefinition? definition)
    {
        var variable = variables?.FirstOrDefault(candidate => candidate.NameCrc == CraftedVariableNameCrc);
        if (variable == null || Decode(variable.Snapshot.Span) is not { } data)
        {
            return null;
        }

        var modifiers = new List<CraftedModifier>(data.Modifiers.Count);
        foreach (var (propertyId, value) in data.Modifiers)
        {
            modifiers.Add(await DescribeModifierAsync(propertyId, value, itemType, definition).ConfigureAwait(false));
        }

        var materials = new List<CraftedMaterial>(data.Materials.Count);
        foreach (var (materialId, quality) in data.Materials)
        {
            materials.Add(new CraftedMaterial(await MaterialNameAsync(materialId).ConfigureAwait(false), quality));
        }

        return new CraftedItemInfo(data.Tier, modifiers, materials);
    }

    /* ========================================================================
     * Décodage binaire
     * ======================================================================== */

    private sealed record CraftedItemData(int Tier, List<(CigGuid? Id, int Quality)> Materials, List<(CigGuid Id, float Value)> Modifiers);

    /// <summary>
    /// Décode la variable : entiers little-endian, tailles de liste en varint LEB128, lectures alignées sur l'octet. Renvoie <c>null</c> si les
    /// données sont tronquées (format changé par un patch).
    /// </summary>
    private static CraftedItemData? Decode(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data.ToArray());
        try
        {
            // m_composition : variante (0 ou 1 = GUID présent), qualité, quantité accumulée.
            var materials = new List<(CigGuid?, int)>();
            for (var count = reader.VarInt(); count > 0; count--)
            {
                var variant = reader.U8();
                CigGuid? material = variant <= 1 ? reader.Guid() : null;
                var quality = reader.U16();
                reader.Skip(8);
                materials.Add((material, quality));
            }

            var tier = reader.I8();

            // m_gameplayPropertyModifiers : GUID d'un CraftingGameplayPropertyDef → valeur.
            var modifiers = new List<(CigGuid, float)>();
            for (var count = reader.VarInt(); count > 0; count--)
            {
                modifiers.Add((reader.Guid(), reader.F32()));
            }

            // m_classId suit : inutile ici, la classe est déjà connue.
            return new CraftedItemData(tier, materials, modifiers);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private sealed class Reader(byte[] data)
    {
        private int _position;

        private ReadOnlySpan<byte> Take(int count)
        {
            var span = data.AsSpan(_position, count);
            _position += count;
            return span;
        }

        public void Skip(int count) => Take(count);
        public byte U8() => Take(1)[0];
        public sbyte I8() => (sbyte)Take(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

        /// <summary>Deux uint64 little-endian, écrits l'un après l'autre en hexadécimal : c'est la forme textuelle de CigGuid.</summary>
        public CigGuid Guid()
        {
            var hex = $"{BinaryPrimitives.ReadUInt64LittleEndian(Take(8)):x16}{BinaryPrimitives.ReadUInt64LittleEndian(Take(8)):x16}";
            return new CigGuid($"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}");
        }

        public ulong VarInt()
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                var b = U8();
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return value;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(data), "Varint trop long");
        }
    }

    /* ========================================================================
     * Résolution DataCore
     * ======================================================================== */

    private Task<DataCoreTypedRecord?> GetRecordAsync(CigGuid id) => _records.GetOrAdd(id, key => _p4kService.GetRecordWithSpecificDepth(key, RecordDepth));

    private async Task<CraftedModifier> DescribeModifierAsync(CigGuid propertyId, float factor, EItemType? itemType, EntityClassDefinition? definition)
    {
        var record = await GetRecordAsync(propertyId).ConfigureAwait(false);
        if (record?.Data is not CraftingGameplayPropertyDef property)
        {
            return new CraftedModifier(ShortName(record?.RecordName) ?? propertyId.ToString(), PercentChange(factor));
        }

        // Nom propre au type d'objet (« Puissance du laser » pour un laser de minage), sinon nom général de la propriété.
        var nameKey = property.nameOverrides?
            .FirstOrDefault(nameOverride => itemType is { } type
                                            && nameOverride.condition is CraftingPropertyNameOverrideCondition_ItemType condition
                                            && condition.matchItemTypes?.Contains(type) == true)?.propertyName
                      ?? property.propertyName;
        var name = await LocalizeAsync(nameKey).ConfigureAwait(false) ?? ShortName(record.RecordName) ?? propertyId.ToString();
        var unit = await LocalizeAsync(property.unitFormat).ConfigureAwait(false);

        var baseValue = BaseValue(ShortName(record.RecordName), definition);
        return new CraftedModifier(name, FormatValue(factor, baseValue, property.displayTransformation, unit));
    }

    /// <summary>
    /// La valeur stockée est toujours un facteur appliqué à la statistique de base de l'objet (1,3292 = +32,92 %). La transformation d'affichage
    /// du jeu part, elle, de la valeur absolue :
    /// <list type="bullet">
    /// <item>« valeur → facteur de la base » puis « facteur → variation en % » (dégâts…) : la variation se calcule directement sur le facteur ;</item>
    /// <item>sinon (échelle seule, ou aucune) : valeur absolue = base × facteur, mise à l'échelle et suivie de son unité (vitesse quantique en Mm/s),
    /// avec la variation entre parenthèses. Si la base n'est pas connue, seule la variation est affichée.</item>
    /// </list>
    /// </summary>
    private static string FormatValue(float factor, double? baseValue, CraftingDisplayTransformation_Base? transformation, string? unit)
    {
        var steps = Flatten(transformation).ToList();
        var convertsToFactor = steps.Any(step => step is CraftingDisplayTransformation_ConvertValueToFactorOfBaseValue);
        if (!convertsToFactor && baseValue == null)
        {
            return PercentChange(factor);
        }

        // Valeur absolue, ou facteur si la transformation ramène de toute façon la valeur à un facteur de la base.
        var result = convertsToFactor ? factor : baseValue!.Value * factor;
        var signed = false;
        foreach (var step in steps)
        {
            switch (step)
            {
                case CraftingDisplayTransformation_ConvertFactorToPercentChange:
                    result = (result - 1) * 100;
                    signed = true;
                    break;
                case CraftingDisplayTransformation_ConvertFactorToNegatedPercentChange:
                    result = (1 - result) * 100;
                    signed = true;
                    break;
                case CraftingDisplayTransformation_Scale scale:
                    result *= scale.scale;
                    break;
            }
        }

        var label = ApplyUnit(result, unit, signed);
        return convertsToFactor ? label : $"{label} ({PercentChange(factor)})";
    }

    /// <summary>Variation correspondant au facteur : 1,081 → « +8,1 % », 0,901 → « -9,86 % ».</summary>
    private static string PercentChange(float factor) => ((factor - 1d) * 100).ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture) + " %";

    /// <summary>
    /// Statistique de base de l'objet à laquelle s'applique le facteur, pour les propriétés affichées en valeur absolue. <c>null</c> si la
    /// propriété n'est pas prise en charge ou si la définition ne la contient pas.
    /// </summary>
    private static double? BaseValue(string? propertyName, EntityClassDefinition? definition)
    {
        if (definition == null)
        {
            return null;
        }

        double? value = propertyName switch
        {
            "GPP_Quantum_Speed" => definition.Components.OfType<SCItemQuantumDriveParams>().FirstOrDefault()?.@params?.driveSpeed,
            "GPP_Quantum_FuelRequirement" => definition.Components.OfType<SCItemQuantumDriveParams>().FirstOrDefault()?.quantumFuelRequirement,
            "GPP_Health_MaxHealth" => definition.Components.OfType<SHealthComponentParams>().FirstOrDefault()?.Health,
            _ => null
        };

        return value is > 0 ? value : null;
    }

    /// <summary>
    /// L'unité localisée est souvent un format printf (« %+.2f %% » pour un pourcentage) : la valeur y est insérée avec sa précision et son
    /// signe. Sinon, l'unité est simplement accolée à la valeur.
    /// </summary>
    private static string ApplyUnit(double value, string? unit, bool signed)
    {
        var defaultNumber = value.ToString(signed ? "+0.##;-0.##;0" : "0.##", CultureInfo.CurrentCulture);
        if (string.IsNullOrEmpty(unit))
        {
            return defaultNumber;
        }

        var match = PrintfNumberRegex().Match(unit);
        if (!match.Success)
        {
            return $"{defaultNumber} {unit}";
        }

        var precision = match.Groups["precision"].Success ? int.Parse(match.Groups["precision"].Value, CultureInfo.InvariantCulture) : 2;
        var number = value.ToString($"F{precision}", CultureInfo.CurrentCulture);
        if (match.Groups["plus"].Success && value >= 0)
        {
            number = "+" + number;
        }

        return (unit[..match.Index] + number + unit[(match.Index + match.Length)..]).Replace("%%", "%").Trim();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"%(?<plus>\+)?(?:\.(?<precision>\d+))?f")]
    private static partial System.Text.RegularExpressions.Regex PrintfNumberRegex();

    private static IEnumerable<CraftingDisplayTransformation_Base> Flatten(CraftingDisplayTransformation_Base? transformation) => transformation switch
    {
        null => [],
        CraftingDisplayTransformation_Sequence sequence => (sequence.transformations ?? []).SelectMany(Flatten),
        _ => [transformation]
    };

    private async Task<string> MaterialNameAsync(CigGuid? materialId)
    {
        if (materialId is not { } id)
        {
            return "Matériau inconnu";
        }

        var record = await GetRecordAsync(id).ConfigureAwait(false);
        var localized = record?.Data switch
        {
            ResourceType resource => await LocalizeAsync(resource.displayName).ConfigureAwait(false),
            EntityClassDefinition entity => await LocalizeAsync(entity.Components.OfType<SAttachableComponentParams>().FirstOrDefault()?.AttachDef?.Localization?.Name)
                .ConfigureAwait(false),
            _ => null
        };

        return localized ?? ShortName(record?.RecordName) ?? id.ToString();
    }

    /// <summary>Texte localisé d'une clé « @… », ou <c>null</c> si la clé est vide ou introuvable.</summary>
    private async Task<string?> LocalizeAsync(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Equals("@LOC_EMPTY", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = await _p4kService.GetLocaleValue(key).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) || value.StartsWith('@') ? null : value.Trim();
    }

    private static string? ShortName(string? recordName) =>
        string.IsNullOrEmpty(recordName) ? null : recordName[(recordName.IndexOf('.') + 1)..];
}
