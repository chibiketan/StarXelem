# Weapons
Data for weapons are stored in `src/StarXelem/datafiles/libs/foundry/records/entities/scitem/weapons/`.

## Properties
- **Size**: Located in `SAttachableComponentParams` -> `AttachDef` -> `Size`.
- **Grade**: Located in `SAttachableComponentParams` -> `AttachDef` -> `Grade`.
- **Type**: Located in `SAttachableComponentParams` -> `AttachDef` -> `Type`.
- **Damage Values**: Stored in the linked `AmmoParams` file (referenced by `SAmmoContainerComponentParams` -> `ammoParamsRecord`) under `projectileParams` -> `damage` (`DamagePhysical`, `DamageEnergy`, `DamageDistortion`, `DamageThermal`, `DamageBiochemical`, `DamageStun`).
- **Multipliers**: Located in `SCItemWeaponComponentParams` -> `noPowerStats`/`underpowerStats`/`overpowerStats` (`SWeaponStats`). Includes `fireRateMultiplier`, `damageMultiplier`, `projectileSpeedMultiplier`, etc.
- **Projectile Speed**: Stored in the linked `AmmoParams` file under `speed`.

## Classification Rules (Weapon Type)
The weapon type is resolved via `LocalDatabaseService.ResolveWeaponType` and `InferWeaponTypeFromName`.

### 1. Ammo Category (Primary Source)
Resolved via `SAmmoContainerComponentParams` -> `ammoParamsRecord` -> `SAmmoContainerComponentParams` -> `ammoCategory`:
- **Ballistic**: `_5mm`, `_7mm`, `_10}\mm`, `_50cal`, `_50cal_pistol`, `_12g`, `Coil`
- **Laser**: `Laser`
- **Plasma**: `Plasma`
- **Electron**: `Electron`

### 3. Subtype Classification
A weapon is defined by the combination of its **Type** and **Subtype**, derived from the `<Tags>` element:

| Type | Subtypes | Example Tag | Example File |
|---|---|---|---|
| **Ballistic** | Cannon, Repeater, Gatling, ScatterGun, MassDriver | `BallisticGatling` | `apar_ballisticgatling_s4.xml` |
| **Laser** | Cannon, Repeater | `LaserRepeater` | `hrst_laserrepeater_s1.xml` |
| **Plasma** | Cannon | `PlasmaCannon` | `vncl_plasmac cannon_s2.xml` |
| **Electron/Neutron** | Cannon | `NeutronCannon` | `mxox_neutroncannon_s1.xml` |
| **Distortion** | Cannon, Repeater | `DistortionRepeater` | `asad_distortionrepeater_s1.xml` |

**Special Cases:**
- **PDC**: Some weapons are tagged simply as `PDC` (e.g., `behr_ballisticgatling_pdc_s1.xml`).


## Examples
### Ballistic Gatling S1
- **File**: `src/StarXelem/datafiles/libs/foundry/records/entities/scitem/weapons/weapon_mounted/gats_ballisticgatling_mounted_s1.xml`
- **Size**: 1
- **Grade**: 1
- **Type**: `WeaponGun`
- **Tags**: `GATS BallisticGatling flightReady weaponMountUsable`
- **Ammo Container**: `file://./../../../../../../../libs/foundry/records/ammoparams/vehicle/gats_ballisticgatling_s1_ammo.xml`

