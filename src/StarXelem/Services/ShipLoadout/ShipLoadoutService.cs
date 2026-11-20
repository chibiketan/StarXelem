using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Sc.External.Services.Entitygraph.V1;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarXelem.Data;
using StarXelem.Models;

namespace StarXelem.Services.ShipLoadout;

/// <summary>
/// Construit le loadout d'un vaisseau possédé sous forme d'arbre de ports, pour le loadout réel (arêtes ATTACHED_TO renvoyées par le jeu)
/// comme pour celui d'origine (SEntityComponentDefaultLoadoutParams du Data.p4k), puis l'aplatit en cartes de composants et en lignes
/// d'armes. Les deux arbres sont comparés port par port : un port est identifié par le chemin des CRC32C de ses noms depuis le vaisseau.
/// </summary>
public sealed class ShipLoadoutService : IShipLoadoutService
{
    // Garde-fou contre un loadout qui se référencerait lui-même.
    private const int MaxDepth = 8;
    private const int DescriptionDepth = 3;

    private static readonly (EItemType Type, string Singular, string Plural)[] ComponentSections =
    [
        (EItemType.PowerPlant, "Générateur", "Générateurs"),
        (EItemType.Cooler, "Refroidisseur", "Refroidisseurs"),
        (EItemType.Shield, "Bouclier", "Boucliers"),
        (EItemType.Radar, "Radar", "Radars"),
        (EItemType.QuantumDrive, "Réacteur quantique", "Réacteurs quantiques"),
        (EItemType.JumpDrive, "Moteur de saut", "Moteurs de saut")
    ];

    private static readonly HashSet<EItemType> ComponentTypes = ComponentSections.Select(section => section.Type).ToHashSet();
    private static readonly HashSet<EItemType> GunTypes = [EItemType.WeaponGun, EItemType.WeaponMining];
    private static readonly HashSet<EItemType> LauncherTypes = [EItemType.MissileLauncher, EItemType.BombLauncher];
    private static readonly HashSet<EItemType> MissileTypes = [EItemType.Missile, EItemType.Bomb];
    private static readonly HashSet<EItemType> MountTypes = [EItemType.Turret, EItemType.TurretBase, EItemType.WeaponMount, EItemType.UtilityTurret];

    /// <summary>Types dont on lit la description : les autres objets (portes, sièges…) ne servent qu'à parcourir l'arbre.</summary>
    private static readonly HashSet<EItemType> DescribedTypes = [.. ComponentTypes, .. GunTypes, .. LauncherTypes, .. MissileTypes, .. MountTypes];

    private readonly IP4kService _p4kService;
    private readonly IScItemRepository _scItemRepository;
    private readonly ILogger<ShipLoadoutService> _logger;
    private readonly ShipComponentDescriber _describer;
    private IReadOnlyDictionary<string, uint>? _crcByShortName;

    public ShipLoadoutService(IP4kService p4kService, IScItemRepository scItemRepository, ILogger<ShipLoadoutService> logger)
    {
        _p4kService = p4kService;
        _scItemRepository = scItemRepository;
        _logger = logger;
        _describer = new ShipComponentDescriber(p4kService);
    }

    public Task<OwnedShipLoadoutModel> GetLoadoutAsync(SpaceshipModel ship, CancellationToken token = default) =>
        Task.Run(() => BuildLoadoutAsync(ship, token), token);

    private async Task<OwnedShipLoadoutModel> BuildLoadoutAsync(SpaceshipModel ship, CancellationToken token)
    {
        var source = ship.HasRealLoadout ? OwnedShipLoadoutSource.Real : OwnedShipLoadoutSource.Origin;
        var shipCrc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([new CigGuid(ship.Entitlement.EntityClassGuid)]));
        var shipDefinition = (await _p4kService.GetEntityType(shipCrc, DescriptionDepth).ConfigureAwait(false))?.Data as EntityClassDefinition;

        var origin = shipDefinition == null ? [] : await BuildOriginNodesAsync(DefaultLoadout(shipDefinition), 1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var originEntries = Flatten(origin);

        if (source == OwnedShipLoadoutSource.Origin)
        {
            return CreateModel(source, originEntries, null);
        }

        var originPortNames = new Dictionary<uint, string>();
        CollectPortNames(origin, originPortNames);
        var real = await BuildRealNodesAsync(ship, shipDefinition, originPortNames, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        return CreateModel(source, Flatten(real), originEntries);
    }

    /* ========================================================================
     * Arbre d'origine (Data.p4k)
     * ======================================================================== */

    private static SItemPortLoadoutBaseParams? DefaultLoadout(EntityClassDefinition definition) =>
        definition.Components.OfType<SEntityComponentDefaultLoadoutParams>().FirstOrDefault()?.loadout;

    private async Task<List<LoadoutNode>> BuildOriginNodesAsync(SItemPortLoadoutBaseParams? loadout, int depth, CancellationToken token)
    {
        var nodes = new List<LoadoutNode>();
        if (loadout is not SItemPortLoadoutManualParams manual || depth > MaxDepth)
        {
            return nodes;
        }

        foreach (var entry in manual.entries)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.itemPortName) || await ResolveEntryCrcAsync(entry).ConfigureAwait(false) is not { } classCrc)
            {
                continue;
            }

            var record = await _p4kService.GetEntityType(classCrc, DescriptionDepth).ConfigureAwait(false);
            // Un véhicule embarqué (snub, moto) a son propre loadout : côté réel, c'est une entité à part.
            if (record?.Data is not EntityClassDefinition definition || definition.Components.OfType<VehicleComponentParams>().Any())
            {
                continue;
            }

            var attachType = definition.Components.OfType<SAttachableComponentParams>().FirstOrDefault()?.AttachDef?.Type;
            var node = new LoadoutNode(entry.itemPortName, PortCrc(entry.itemPortName), classCrc)
            {
                Description = attachType is { } type && DescribedTypes.Contains(type) ? await DescribeAsync(definition, record.RecordName).ConfigureAwait(false) : null
            };

            // Sans sous-entrées dans le loadout du vaisseau, l'objet garde son propre loadout par défaut (tourelle et ses canons…).
            var childLoadout = entry.loadout is SItemPortLoadoutManualParams nested && nested.entries.Any() ? entry.loadout : DefaultLoadout(definition);
            node.Children.AddRange(await BuildOriginNodesAsync(childLoadout, depth + 1, token).ConfigureAwait(false));
            nodes.Add(node);
        }

        return nodes;
    }

    private async Task<uint?> ResolveEntryCrcAsync(SItemPortLoadoutEntryParams entry)
    {
        if (entry.entityClassReference is { } reference && reference.selfId != default)
        {
            return Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([reference.selfId]));
        }

        if (string.IsNullOrEmpty(entry.entityClassName))
        {
            return null;
        }

        _crcByShortName ??= await _scItemRepository.GetCrcByShortNameAsync().ConfigureAwait(false);
        if (_crcByShortName.TryGetValue(entry.entityClassName, out var crc))
        {
            return crc;
        }

        _logger.LogDebug("Objet de loadout introuvable dans la base locale : {ClassName}", entry.entityClassName);
        return null;
    }

    private static void CollectPortNames(IEnumerable<LoadoutNode> nodes, Dictionary<uint, string> names)
    {
        foreach (var node in nodes)
        {
            names.TryAdd(node.PortCrc, node.PortName);
            CollectPortNames(node.Children, names);
        }
    }

    /* ========================================================================
     * Arbre réel (EntityGraph)
     * ======================================================================== */

    private async Task<List<LoadoutNode>> BuildRealNodesAsync(SpaceshipModel ship, EntityClassDefinition? shipDefinition,
        IReadOnlyDictionary<uint, string> originPortNames, CancellationToken token)
    {
        var byParent = ship.AttachedEntities
            .Where(entity => entity.EntityEdge is { Type: EntityEdgeType.AttachedTo, End.HasEntityId: true })
            .GroupBy(entity => entity.EntityEdge!.End.EntityId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return await BuildRealChildrenAsync(ship.EntityProperties!.EntityNodeProperties!.Geid, shipDefinition, byParent, originPortNames, 1, token)
            .ConfigureAwait(false);
    }

    private async Task<List<LoadoutNode>> BuildRealChildrenAsync(ulong parentGeid, EntityClassDefinition? parentDefinition,
        Dictionary<ulong, List<EntityItemQueryResult>> byParent, IReadOnlyDictionary<uint, string> originPortNames, int depth, CancellationToken token)
    {
        var nodes = new List<LoadoutNode>();
        if (depth > MaxDepth || !byParent.TryGetValue(parentGeid, out var children))
        {
            return nodes;
        }

        var parentPorts = PortNamesByCrc(parentDefinition);

        foreach (var child in children)
        {
            token.ThrowIfCancellationRequested();
            var properties = child.EntityNodeProperties!;
            var portCrc = child.EntityEdge!.Properties?.AttachmentPortCrc ?? 0;
            var portName = parentPorts.GetValueOrDefault(portCrc) ?? originPortNames.GetValueOrDefault(portCrc) ?? $"port {portCrc:x8}";
            var hasChildren = byParent.ContainsKey(properties.Geid);
            var isDescribed = DescribedTypes.Contains((EItemType)properties.ItemTypeEnum);

            // La définition ne sert qu'à décrire l'objet ou à nommer les ports de ses enfants : inutile de la charger sinon.
            var record = isDescribed || hasChildren
                ? await _p4kService.GetEntityType(properties.ClassGuidCrc, DescriptionDepth).ConfigureAwait(false)
                : null;
            var definition = record?.Data as EntityClassDefinition;

            var node = new LoadoutNode(portName, portCrc, properties.ClassGuidCrc)
            {
                Description = isDescribed && definition != null ? await DescribeAsync(definition, record!.RecordName).ConfigureAwait(false) : null
            };
            node.Children.AddRange(await BuildRealChildrenAsync(properties.Geid, definition, byParent, originPortNames, depth + 1, token).ConfigureAwait(false));
            nodes.Add(node);
        }

        return nodes;
    }

    private static Dictionary<uint, string> PortNamesByCrc(EntityClassDefinition? definition)
    {
        var names = new Dictionary<uint, string>();
        var ports = definition?.Components.OfType<SItemPortContainerComponentParams>().FirstOrDefault()?.Ports ?? [];
        foreach (var port in ports.Where(port => !string.IsNullOrEmpty(port.Name)))
        {
            names.TryAdd(PortCrc(port.Name), port.Name);
        }

        return names;
    }

    /* ========================================================================
     * Aplatissement et comparaison
     * ======================================================================== */

    private Task<ShipComponentDescription?> DescribeAsync(EntityClassDefinition definition, string? recordName) =>
        _describer.DescribeAsync(definition, recordName, ShipComponentDescriber.ShortName(recordName));

    /// <summary>CRC32C du nom de port tel quel : c'est la valeur portée par l'arête ATTACHED_TO (attachmentPortCrc).</summary>
    private static uint PortCrc(string portName) => Crc32c.FromSpan(Encoding.UTF8.GetBytes(portName));

    private static FlatLoadout Flatten(IEnumerable<LoadoutNode> roots)
    {
        var flat = new FlatLoadout();
        Visit(roots, string.Empty, null, []);
        return flat;

        void Visit(IEnumerable<LoadoutNode> nodes, string parentKey, LoadoutNode? hardpoint, IReadOnlyList<LoadoutNode> mounts)
        {
            foreach (var node in nodes)
            {
                var key = $"{parentKey}/{node.PortCrc:x8}";
                var top = hardpoint ?? node;
                var type = node.Description?.Type;

                if (type is { } componentType && ComponentTypes.Contains(componentType))
                {
                    flat.Components[key] = new FlatEntry(node, top, mounts, [], node.ClassCrc.ToString());
                }
                else if (type is { } gunType && GunTypes.Contains(gunType))
                {
                    flat.Weapons[key] = new FlatEntry(node, top, mounts, [], node.ClassCrc.ToString());
                }
                else if (type is { } launcherType && LauncherTypes.Contains(launcherType))
                {
                    var missiles = Descendants(node).Where(child => child.Description?.Type is { } t && MissileTypes.Contains(t)).ToList();
                    var signature = string.Join(",", missiles.Select(missile => missile.ClassCrc).Order().Prepend(node.ClassCrc));
                    flat.Weapons[key] = new FlatEntry(node, top, mounts, missiles, signature);
                }
                else if (type is { } missileType && MissileTypes.Contains(missileType))
                {
                    flat.Weapons[key] = new FlatEntry(node, top, mounts, [node], node.ClassCrc.ToString());
                }
                else
                {
                    var childMounts = type is { } mountType && MountTypes.Contains(mountType) ? [.. mounts, node] : mounts;
                    Visit(node.Children, key, top, childMounts);
                }
            }
        }
    }

    private static IEnumerable<LoadoutNode> Descendants(LoadoutNode node) =>
        node.Children.SelectMany(child => Descendants(child).Prepend(child));

    private static OwnedShipLoadoutModel CreateModel(OwnedShipLoadoutSource source, FlatLoadout shown, FlatLoadout? origin)
    {
        var components = shown.Components.Select(pair =>
        {
            var reference = origin?.Components.GetValueOrDefault(pair.Key);
            return CreateComponent(pair.Value, origin != null && reference?.Signature != pair.Value.Signature, reference, false);
        }).ToList();
        var weapons = shown.Weapons.Select(pair =>
        {
            var reference = origin?.Weapons.GetValueOrDefault(pair.Key);
            return CreateWeaponRow(pair.Value, origin != null && reference?.Signature != pair.Value.Signature, reference, false);
        }).ToList();

        // Ports équipés d'origine et vides aujourd'hui.
        if (origin != null)
        {
            components.AddRange(origin.Components.Where(pair => !shown.Components.ContainsKey(pair.Key))
                .Select(pair => CreateComponent(pair.Value, true, pair.Value, true)));
            weapons.AddRange(origin.Weapons.Where(pair => !shown.Weapons.ContainsKey(pair.Key))
                .Select(pair => CreateWeaponRow(pair.Value, true, pair.Value, true)));
        }

        var sections = ComponentSections
            .Select(section => (section, list: components.Where(component => component.Type == section.Type).Select(component => component.Model).ToList()))
            .Where(group => group.list.Count > 0)
            .Select(group => new OwnedShipComponentSection(group.list.Count > 1 ? group.section.Plural : group.section.Singular, group.list))
            .ToList();

        return new OwnedShipLoadoutModel
        {
            Source = source,
            ComponentSections = sections,
            Weapons = weapons
                .OrderBy(row => row.IsMissile)
                .ThenBy(row => row.HardpointName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.PortName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static (EItemType Type, OwnedShipComponentModel Model) CreateComponent(FlatEntry entry, bool isModified, FlatEntry? reference, bool isRemoved)
    {
        var description = entry.Node.Description!;
        var model = new OwnedShipComponentModel
        {
            PortName = entry.Node.PortName,
            DisplayName = isRemoved ? "Emplacement vide" : description.DisplayName,
            Class = isRemoved ? ComponentClass.Unknown : description.Class,
            Size = description.Size,
            Grade = isRemoved ? string.Empty : description.Grade,
            IsModified = isModified,
            IsRemoved = isRemoved,
            OriginName = reference?.Node.Description?.DisplayName
        };

        return (description.Type, model);
    }

    private static OwnedShipWeaponRow CreateWeaponRow(FlatEntry entry, bool isModified, FlatEntry? reference, bool isRemoved)
    {
        var description = entry.Node.Description!;
        var isMissile = entry.Missiles.Count > 0 || LauncherTypes.Contains(description.Type);
        string? typeLabel;
        if (isMissile)
        {
            typeLabel = string.Join(", ", entry.Missiles
                .GroupBy(missile => missile.Description?.DisplayName ?? "?")
                .Select(group => group.Count() > 1 ? $"{group.Count()} × {group.Key}" : group.Key));
            var guidance = entry.Missiles.Select(missile => missile.Description?.GuidanceType).FirstOrDefault(value => value != null);
            if (guidance != null)
            {
                typeLabel = $"{typeLabel} · {guidance}";
            }
        }
        else
        {
            typeLabel = description.WeaponType;
        }

        return new OwnedShipWeaponRow
        {
            HardpointName = HumanizePort(entry.Hardpoint.PortName),
            PortName = entry.Hardpoint.PortName,
            EquipmentName = isRemoved ? "Emplacement vide" : description.DisplayName,
            MountLabel = entry.Mounts.Count > 0 ? string.Join(" › ", entry.Mounts.Select(mount => mount.Description!.DisplayName)) : null,
            Size = description.Size,
            TypeLabel = isRemoved ? null : typeLabel,
            AlphaDamage = isRemoved || isMissile ? null : description.AlphaDamage,
            IsMissile = isMissile,
            IsModified = isModified,
            IsRemoved = isRemoved,
            OriginName = reference?.Node.Description?.DisplayName
        };
    }

    /// <summary>« hardpoint_weapon_wing_left » → « Weapon wing left » : les ports d'un vaisseau n'ont pas de nom localisé.</summary>
    private static string HumanizePort(string portName)
    {
        var name = portName.StartsWith("hardpoint_", StringComparison.OrdinalIgnoreCase) ? portName["hardpoint_".Length..] : portName;
        name = name.Replace('_', ' ').Trim();
        return name.Length == 0 ? portName : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>Objet monté sur un port, avec ses propres objets montés.</summary>
    private sealed class LoadoutNode(string portName, uint portCrc, uint classCrc)
    {
        public string PortName { get; } = portName;
        public uint PortCrc { get; } = portCrc;
        public uint ClassCrc { get; } = classCrc;
        public ShipComponentDescription? Description { get; init; }
        public List<LoadoutNode> Children { get; } = [];
    }

    /// <summary>
    /// Composant ou arme retenu pour l'affichage. <see cref="Signature"/> résume ce qui est monté (classe de l'objet et, pour un lanceur,
    /// de ses missiles) : deux entrées de même chemin et de signatures différentes forment un écart.
    /// </summary>
    private sealed record FlatEntry(LoadoutNode Node, LoadoutNode Hardpoint, IReadOnlyList<LoadoutNode> Mounts, IReadOnlyList<LoadoutNode> Missiles,
        string Signature);

    /// <summary>Composants et armes indexés par chemin de port.</summary>
    private sealed class FlatLoadout
    {
        public Dictionary<string, FlatEntry> Components { get; } = new();
        public Dictionary<string, FlatEntry> Weapons { get; } = new();
    }
}
