using StarXelem.Models;

namespace StarXelem.Services.ShipLoadout;

/// <summary>Loadout fictif pour l'aperçu des vues en mode design.</summary>
public sealed class DesignShipLoadoutService : IShipLoadoutService
{
    public Task<OwnedShipLoadoutModel> GetLoadoutAsync(SpaceshipModel ship, CancellationToken token = default) => Task.FromResult(Sample);

    /// <summary>Loadout réel d'exemple, avec deux écarts par rapport à l'origine.</summary>
    public static OwnedShipLoadoutModel Sample { get; } = new()
    {
        Source = OwnedShipLoadoutSource.Real,
        ComponentSections =
        [
            new OwnedShipComponentSection("Générateur", [Component("hardpoint_powerplant", "JS-300", ComponentClass.Civilian, 1, "C")]),
            new OwnedShipComponentSection("Refroidisseurs",
            [
                Component("hardpoint_cooler_left", "Bracer", ComponentClass.Military, 1, "A", "Arctic"),
                Component("hardpoint_cooler_right", "Arctic", ComponentClass.Industrial, 1, "C")
            ]),
            new OwnedShipComponentSection("Bouclier", [Component("hardpoint_shield_generator", "FR-66", ComponentClass.Military, 1, "A")]),
            new OwnedShipComponentSection("Réacteur quantique", [Component("hardpoint_quantum_drive", "Atlas", ComponentClass.Civilian, 1, "C")])
        ],
        Weapons =
        [
            new OwnedShipWeaponRow
            {
                HardpointName = "Weapon wing left", PortName = "hardpoint_weapon_wing_left", EquipmentName = "CF-337 Panther",
                MountLabel = "VariPuck S3", Size = 3, TypeLabel = "Laser Repeater", AlphaDamage = 66.2f, IsModified = true, OriginName = "M5A Cannon"
            },
            new OwnedShipWeaponRow
            {
                HardpointName = "Weapon wing right", PortName = "hardpoint_weapon_wing_right", EquipmentName = "M5A Cannon", Size = 3,
                TypeLabel = "Laser Cannon", AlphaDamage = 230.4f
            },
            new OwnedShipWeaponRow
            {
                HardpointName = "Missile rack left", PortName = "hardpoint_missile_rack_left", EquipmentName = "MSD-442 Missile Rack", Size = 4,
                TypeLabel = "4 × Arrester III · IR", IsMissile = true
            }
        ]
    };

    private static OwnedShipComponentModel Component(string port, string name, ComponentClass componentClass, int size, string grade, string? originName = null) => new()
    {
        PortName = port,
        DisplayName = name,
        Class = componentClass,
        Size = size,
        Grade = grade,
        IsModified = originName != null,
        OriginName = originName
    };
}
