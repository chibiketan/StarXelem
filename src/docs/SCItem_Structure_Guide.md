# Star Citizen SCItem XML — Structure et Extraction des Données

## Vue d'ensemble

Chaque objet du jeu (vaisseau, arme, bouclier, casque, module, etc.) est défini par un fichier XML dans `libs/foundry/records/entities/scitem/`. Tous partagent la même structure de racine : `<EntityClassDefinition>`, avec les données de type, taille et statistiques dans des composants imbriqués.

### Chemin racine

```
libs/foundry/records/entities/scitem/
├── ships/          — Composants de vaisseau (moteurs, boucliers, armes montées, etc.)
├── weapons/        — Armes FPS, accessoires, mines, armes montées
├── suit/           — Équipement personnage (casques, mobiglas, propulseurs)
├── cargo/          — Caisses de fret, conteneurs
├── consumables/    — Nourriture, boissons
├── vehicles/       — Véhicules terrestres
└── ...             — De nombreux autres dossiers
```

---

## Structure XML commune

```xml
<EntityClassDefinition.NOM RecordId="GUID" RecordTag="..." Type="EntityClassDefinition">
  <Category>...</Category>
  <Icon>...</Icon>
  <Invisible>...</Invisible>
  <entityDensityClass ReferencedFile="..." />
  <tags Type="Tag" Count="N">...</tags>
  <StaticEntityClassData Type="EntityClassStaticDataParams" Count="N">...</StaticEntityClassData>
  <Components Type="DataForgeComponentParams" Count="N">
    <!-- Les composants principaux -->
  </Components>
</EntityClassDefinition>
```

### Champs racine importants

| Champ | Description |
|---|---|
| `RecordId` | UUID unique de l'entité (ex: `f59a0b33-5bbe-439e-8bd5-111db00b5ff6`) |
| `RecordTag` | Tag de provenance (`SystemsDesign`, `Ship`, `Unknown`, etc.) |
| Nom du nœud racine | Contient le nom interne de l'objet (ex: `EntityClassDefinition.COOL_ACOM_S01_IcePlunge_SCItem`) |

---

## Identification du type d'objet

Le type d'un objet se trouve dans le composant `SAttachableComponentParams`, sous `AttachDef` de type `SItemDefinition`. C'est **le bloc le plus important** pour classifier un objet.

### Chemin XML

```
Components → SAttachableComponentParams → AttachDef (Type="SItemDefinition")
```

### Champs du bloc `SItemDefinition`

| Champ | Description | Exemples |
|---|---|---|
| `<Type>` | **Catégorie principale** de l'objet | `Shield`, `Cooler`, `PowerPlant`, `WeaponGun`, `WeaponPersonal`, `Suit`, `Cargo`, `Armor`, `JumpDrive`, `QuantumDrive`, `Scanner`, `Radar`, `Shield`, `MissileLauncher`, `Turret`, `MobiGlas`, `Battery`, `FuelTank`, `GravityGenerator`, `FlightController`, `LifeSupportGenerator`, etc. |
| `<SubType>` | **Sous-catégorie** (précise la catégorie) | `Helmet`, `Magazine`, `Gun`, `Power`, `Cargo`, `Small`, `Medium`, `Heavy`, `Light`, `UNDEFINED` |
| `<Size>` | **Taille** (slot de vaisseau) | `0` = taille 0 (micro), `1` = S1 (small), `2` = S2, `3` = S3, `4` = S4 (capital), `5`+, `6`, `7`, `10` |
| `<Grade>` | Grade de l'objet (rarement >1) | `1`, `2`, `3` |
| `<Manufacturer>` | Fabricant (référence externe) | `scitemmanufacturer.acom.xml`, `scitemmanufacturer.aegs.xml`, etc. |
| `<Tags>` | Tags associés à l'objet | `flightReady`, `AEGS_EMP_Device`, etc. |
| `<RequiredTags>` | Tags requis pour l'installation | Utilisé pour les contraintes de compatibilité |

### Liste complète des Types observés

**Composants de vaisseau :**
- `AIModule` — Module IA
- `Armor` — Armure de vaisseau (SubType: `Light`, `Medium`, `Heavy`)
- `Battery` — Batterie
- `Bomb` / `BombLauncher` — Bombes / Lancer de bombes
- `CapacitorAssignmentController` — Contrôleur de condensateur
- `Cooler` — Radiateur
- `CoolerController` — Contrôleur de radiateur
- `DockingCollar` — Collier d'amarrage
- `EMP` — Dispositif EMP
- `EnergyController` — Contrôleur d'énergie
- `ExternalFuelTank` — Réservoir de carburant externe
- `FlightController` — Contrôleur de vol
- `FuelController` — Contrôleur de carburant
- `FuelIntake` — Prise de carburant
- `FuelTank` — Réservoir de carburant
- `GravityGenerator` — Générateur de gravité
- `JumpDrive` — Moteur spatial (jump drive)
- `LandingSystem` — Train d'atterrissage
- `LifeSupportGenerator` — Générateur de soutien vie
- `MainThruster` — Propulseur principal
- `ManneuverThruster` — Propulseur de manœuvre
- `MiningController` — Contrôleur de minage
- `MissileController` — Contrôleur de missiles
- `MissileLauncher` — Lance-missiles
- `Module` — Module de vaisseau
- `Paints` — Peinture
- `PowerPlant` — Groupe électrogène
- `QuantumDrive` — Moteur quantique
- `QuantumFuelTank` — Réservoir de carburant quantique
- `QuantumInterdictionGenerator` — Générateur d'interdiction quantique
- `Radar` — Radar
- `RadarDisplay` — Affichage radar
- `RadarController` — Contrôleur radar
- `Relay` — Relais
- `SalvageController` — Contrôleur de dépannage
- `SalvageFieldEmitter` / `SalvageFieldSupporter` — Émetteur/soutien de champ de dépannage
- `SalvageFillerStation` — Station de remplissage de dépannage
- `SalvageHead` — Tête de dépannage
- `SalvageInternalStorage` — Stockage interne de dépannage
- `Scanner` — Scanner
- `Seat` — Siège
- `SeatAccess` — Accès au siège
- `SeatDashboard` — Tableau de bord de siège
- `SelfDestruct` — Auto-destruction
- `Shield` — Bouclier (SubType: `UNDEFINED`)
- `ShieldController` — Contrôleur de bouclier
- `TargetSelector` — Sélecteur de cible
- `TractorBeam` — Pince
- `Turret` — Tourelle
- `TurretBase` — Base de tourelle
- `UtilityTurret` — Tourelle utilitaire

**Armes :**
- `WeaponAttachment` — Accessoire d'arme (SubType: `Magazine`, etc.)
- `WeaponController` — Contrôleur d'arme
- `WeaponDefensive` — Arme défensive
- `WeaponGun` — Arme à feu montée (SubType: `Gun`)
- `WeaponMining` — Arme de minage
- `WeaponMount` — Support d'arme
- `WeaponPersonal` — Arme personnelle FPS (SubType: `Large`, `Small`, etc.)

**Équipement personnage :**
- `Char_Accessory_Eyes` / `Char_Accessory_Head` — Accessoires de personnage
- `Char_Armor_Arms` / `Char_Armor_Backpack` / `Char_Armor_Helmet` / `Char_Armor_Legs` / `Char_Armor_Torso` / `Char_Armor_Undersuit` — Armure de personnage (SubType: `Light`, `Medium`, `Heavy`)
- `Char_Body` / `Char_Head` / `Char_Hair_*` / `Char_Skin_Color` — Corps et apparence
- `Char_Clothing_*` — Vêtements
- `Char_Flair` — Personnalisation
- `Char_Lens` — Lentilles
- `Char_Visor` / `Visor` — Visière
- `FPS_Consumable` — Consommable FPS
- `FPS_Deployable` — Déployable FPS
- `FPS_Radar` — Radar FPS
- `Gadget` — Gadget
- `Grenade` — Grenade
- `MobiGlas` — MobiGlas
- `Suit` — Combinaison (SubType: `Helmet`, etc.)
- `ToolArm` — Bras d'outil

**Fret et conteneurs :**
- `Cargo` — Fret (SubType: `Small`, `Cargo`, `Mission`, `UNDEFINED`)
- `CargoGrid` — Grille de fret
- `Container` — Conteneur
- `InventoryContainer` — Conteneur d'inventaire

**Autre :**
- `AirTrafficController` — Contrôleur de trafic aérien
- `AmmoBox` — Boîte de munitions (SubType: `Magazine`)
- `AttachedPart` — Pièce attachée
- `Bottle` — Bouteille
- `Button` — Bouton
- `Cloth` — Tissu
- `CommsController` — Contrôleur de communications
- `ControlPanel` — Panneau de contrôle
- `Currency` — Monnaie
- `Debris` — Débris
- `Decal` — Décalcomanie
- `Display` — Affichage
- `Door` / `DoorController` — Porte
- `Drink` / `Food` — Boisson / Nourriture
- `Elevator` — Ascenseur
- `ExternalFuelTank` — Réservoir externe
- `Flair_*` — Personnalisation (Cockpit, Floor, Surface, Wall)
- `Light` / `LightController` — Lumière
- `Lightgroup` — Groupe lumineux
- `MiningModifier` — Modificateur de minage
- `Misc` — Divers
- `Player` — Joueur
- `RemoteConnection` — Connexion distante
- `RemovableChip` — Puce amovible
- `Room` — Pièce
- `SalvageModifier` — Modificateur de dépannage
- `Sensor` — Capteur
- `ShopDisplay` — Affichage de magasin
- `SpaceMine` — Mine spatiale
- `StatusScreen` — Écran de statut
- `TowingBeam` — Remorque
- `UNDEFINED` — Type non défini
- `Usable` — Utilisable
- `WheeledController` — Contrôleur à roues

---

## Données de taille et d'inventaire

Dans le même bloc `SItemDefinition`, plusieurs champs décrivent l'occupation inventaire :

### Taille de slot vaisseau

```xml
<Size>1</Size>  <!-- 0=micro, 1=S1, 2=S2, 3=S3, 4=S4, 5+=spécial -->
```

### Volume d'occupation inventaire (microSCU)

```xml
<inventoryOccupancyVolume Type="SMicroCargoUnit">
  <microSCU>84000</microSCU>
</inventoryOccupancyVolume>
```

- `84000` microSCU = taille S1 standard
- `2100000` microSCU = taille S4 standard
- `1` microSCU = objet virtuel (pas d'occupation réelle, ex: armure, storage)

### Dimensions d'occupation inventaire

```xml
<inventoryOccupancyDimensions Type="Vec3">
  <x>0.2233005</x>
  <y>2.211008</y>
  <z>0.801895</z>
</inventoryOccupancyDimensions>
```

Dimensions réelles en mètres pour le système de fret.

### Dimensions UI (grille inventaire)

```xml
<inventoryOccupancyFixedGridDimensionsUIV4 Type="Vec2">
  <x>1</x>  <!-- colonnes -->
  <y>1</y>  <!-- lignes -->
</inventoryOccupancyFixedGridDimensionsUIV4>
```

---

## Statistiques par type de composant

Les statistiques spécifiques à un type d'objet se trouvent dans des composants spécialisés, **après** le bloc `SAttachableComponentParams`.

### Données communes à tous les composants

#### `SEntityPhysicsControllerParams`

```xml
<SEntityPhysicsControllerParams>
  <PhysType Type="SEntityRigidPhysicsControllerParams">
    <Mass>61</Mass>  <!-- Masse en kg -->
    ...
  </PhysType>
</SEntityPhysicsControllerParams>
```

#### `SHealthComponentParams`

```xml
<SHealthComponentParams>
  <Health>69</Health>  <!-- Points de vie -->
  <DamageResistances Type="DamageResistance">
    <PhysicalResistance><Multiplier>0.9</Multiplier></PhysicalResistance>
    <EnergyResistance><Multiplier>0.9</Multiplier></EnergyResistance>
    <DistortionResistance><Multiplier>1</Multiplier></DistortionResistance>
    <ThermalResistance><Multiplier>0.1</Multiplier></ThermalResistance>
    <BiochemicalResistance><Multiplier>1</Multiplier></BiochemicalResistance>
    <StunResistance><Multiplier>1</Multiplier></StunResistance>
  </DamageResistances>
  <IsSalvagable>True</IsSalvagable>
  <IsRepairable>True</IsRepairable>
</SHealthComponentParams>
```

#### `ItemResourceComponentParams`

C'est le composant **central** pour les statistiques de ressources. Il contient les états (`states`) avec des `deltas` qui définissent la consommation et la génération.

```xml
<ItemResourceComponentParams>
  <states Type="ItemResourceState" Count="N">
    <ItemResourceState>
      <name>Online</name>
      <deltas Type="ItemResourceDeltaBase" Count="N">
        <!-- Génération -->
        <ItemResourceDeltaGeneration>
          <generation>
            <resource>Power</resource>
            <resourceAmountPerSecond Type="SPowerSegmentResourceUnit">
              <units>16</units>  <!-- Segments de puissance générés par seconde -->
            </resourceAmountPerSecond>
          </generation>
        </ItemResourceDeltaGeneration>
        <!-- Consommation -->
        <ItemResourceDeltaConsumption>
          <consumption>
            <resource>Coolant</resource>
            <resourceAmountPerSecond Type="SStandardResourceUnit">
              <standardResourceUnits>0</standardResourceUnits>  <!-- Unités de refroidissement consommées par seconde -->
            </resourceAmountPerSecond>
          </consumption>
        </ItemResourceDeltaConsumption>
        <!-- Conversion (ex: radiateur consomme Power, génère Coolant) -->
        <ItemResourceDeltaConversion>
          <consumption>
            <resource>Power</resource>
            <resourceAmountPerSecond Type="SPowerSegmentResourceUnit">
              <units>2</units>
            </resourceAmountPerSecond>
          </consumption>
          <generation>
            <resource>Coolant</resource>
            <resourceAmountPerSecond Type="SStandardResourceUnit">
              <standardResourceUnits>34</standardResourceUnits>
            </resourceAmountPerSecond>
          </generation>
        </ItemResourceDeltaConversion>
      </deltas>
    </ItemResourceState>
  </states>
  <defaultPriority>30</defaultPriority>
</ItemResourceComponentParams>
```

**Unités de ressource :**
- `SPowerSegmentResourceUnit` — Segments de puissance (1 segment ≈ 1/32 de la puissance totale du réseau)
- `SStandardResourceUnit` — Unités standard de ressource (coolant, etc.)

### Statistiques spécifiques par type

#### `SCItemShieldGeneratorParams` (Bouclier)

```xml
<SCItemShieldGeneratorParams>
  <MaxShieldHealth>194400</MaxShieldHealth>
  <MaxShieldRegen>14256</MaxShieldRegen>
  <DecayRatio>0.25</DecayRatio>
  <ReservePoolInitialHealthRatio>1</ReservePoolInitialHealthRatio>
  <ReservePoolMaxHealthRatio>1</ReservePoolMaxHealthRatio>
  <ReservePoolRegenRateRatio>1</ReservePoolRegenRateRatio>
  <ReservePoolDrainRateRatio>2.5</ReservePoolDrainRateRatio>
  <DownedRegenDelay>11.1</DownedRegenDelay>
  <DamagedRegenDelay>5.55</DamagedRegenDelay>
  <ShieldResistance Type="SShieldResistance" Count="6">
    <!-- Résistances par type de dégât -->
  </ShieldResistance>
</SCItemShieldGeneratorParams>
```

#### `SCItemJumpDriveParams` (Moteur spatial)

```xml
<SCItemJumpDriveParams>
  <alignmentRate>0.2</alignmentRate>
  <alignmentDecayRate>0.1</alignmentDecayRate>
  <tuningRate>0.26</tuningRate>
  <tuningDecayRate>0.5</tuningDecayRate>
  <fuelUsageEfficiencyMultiplier>8</fuelUsageEfficiencyMultiplier>
</SCItemJumpDriveParams>
```

#### `SCItemVehicleArmorParams` (Armure de vaisseau)

```xml
<SCItemVehicleArmorParams>
  <signalInfrared>1.1</signalInfrared>
  <signalElectromagnetic>1.1</signalElectromagnetic>
  <signalCrossSection>1.1</signalCrossSection>
  <damageMultiplier Type="DamageInfo">
    <DamagePhysical>0.8</DamagePhysical>
    <DamageEnergy>0.65</DamageEnergy>
    <DamageDistortion>1</DamageDistortion>
    <DamageThermal>1</DamageThermal>
    <DamageBiochemical>1</DamageBiochemical>
    <DamageStun>1</DamageStun>
  </damageMultiplier>
</SCItemVehicleArmorParams>
```

#### `SCItemWeaponComponentParams` (Armes)

```xml
<SCItemWeaponComponentParams>
  <ammoContainerRecord />
  <weaponAIData>
    <accuracyRange><minimum>1</minimum><maximum>100</maximum></accuracyRange>
  </weaponAIData>
  <!-- Les données de dégât sont dans les projectiles/missiles référencés -->
</SCItemWeaponComponentParams>
```

Pour les armes montées, un composant `SAmmoContainerComponentParams` définit le système de munitions.

#### `SDistortionParams` (Distorsion)

Présent sur beaucoup de composants :

```xml
<SDistortionParams>
  <DecayDelay>1.5</DecayDelay>
  <DecayRate>70</DecayRate>
  <Maximum>1050</Maximum>
  <WarningRatio>0.75</WarningRatio>
  <RecoveryRatio>0</RecoveryRatio>
  <PowerRatioAtMaxDistortion>0</PowerRatioAtMaxDistortion>
</SDistortionParams>
```

#### `SDegradationParams` (Dégradation)

```xml
<SDegradationParams>
  <accumulators Type="SAccumulatorParams" Count="1">
    <SWearAccumulatorParams>
      <DamageConversionRate>0.5</DamageConversionRate>
    </SWearAccumulatorParams>
  </accumulators>
</SDegradationParams>
```

---

## Localisation (noms et descriptions)

Les noms et descriptions sont des clés de localisation, pas du texte direct :

```xml
<Localization Type="SCItemLocalization">
  <Name>@item_NameCOOL_ACOM_S01_IcePlunge</Name>
  <ShortName>@LOC_EMPTY</ShortName>
  <Description>@item_DescCOOL_ACOM_S01_IcePlunge</Description>
</Localization>
```

Le type d'affichage en magasin est dans `SCItemPurchasableParams` :

```xml
<SCItemPurchasableParams>
  <displayName>@item_Name_COOL_ACOM_S01_IcePlunge</displayName>
  <displayType>@item_displayType_Cooler</displayType>
</SCItemPurchasableParams>
```

---

## Fabricant

```xml
<Manufacturer ReferencedFile="file://.../scitemmanufacturer/scitemmanufacturer.acom.xml" />
```

Le fabricant est une référence externe. Les fabricants connus incluent :
- `acom` — Aegis Dynamics
- `aegs` — Aegis Operations
- `anvl` — Anvil
- `argas` — Argon
- `cnou` — CRN
- `bltr` — Bistorta
- `gats` — GATS
- `apar` — Apocalypse Arms
- `tars` — Tarsus
- `acas` — Acamar
- `asas` — Asas
- `crus` — Crusader
- `lorv` — Lorville
- `slaver` — Slaver
- `cds` — CDS
- `rsi` — RSI (Roberts Space Industries)

---

## Résumé : Comment extraire les infos importantes

1. **Type d'objet** → `Components/SAttachableComponentParams/AttachDef/Type`
2. **Sous-type** → `Components/SAttachableComponentParams/AttachDef/SubType`
3. **Taille (slot)** → `Components/SAttachableComponentParams/AttachDef/Size`
4. **Grade** → `Components/SAttachableComponentParams/AttachDef/Grade`
5. **Fabricant** → `Components/SAttachableComponentParams/AttachDef/Manufacturer/@ReferencedFile`
6. **Nom localisé** → `Components/SAttachableComponentParams/AttachDef/Localization/Name`
7. **Masse** → `Components/SEntityPhysicsControllerParams/PhysType/Mass`
8. **Points de vie** → `Components/SHealthComponentParams/Health`
9. **Résistances** → `Components/SHealthComponentParams/DamageResistances/*`
10. **Volume inventaire** → `Components/SAttachableComponentParams/AttachDef/inventoryOccupancyVolume/microSCU`
11. **Dimensions inventaire** → `Components/SAttachableComponentParams/AttachDef/inventoryOccupancyDimensions/*`
12. **Ressources** → `Components/ItemResourceComponentParams/states/*/deltas/*`
13. **Stats spécifiques** → Composant `SCItem*Params` correspondant (bouclier, jump drive, armure, etc.)
14. **Distorsion** → `Components/SDistortionParams/*`
15. **Dégradation** → `Components/SDegradationParams/*`
16. **Tags** → Racine `tags/Tag/@RecordId` + `AttachDef/Tags` (texte libre)

### Mapping Type → Composant de stats spécifique

| Type | Composant de stats |
|---|---|
| `Shield` | `SCItemShieldGeneratorParams` |
| `JumpDrive` | `SCItemJumpDriveParams` |
| `Armor` | `SCItemVehicleArmorParams` |
| `PowerPlant` | `ItemResourceComponentParams` (deltas) |
| `Cooler` | `ItemResourceComponentParams` (conversion) |
| `Battery` | `ItemResourceComponentParams` (stockage) |
| `FuelTank` | `ItemResourceComponentParams` (stockage) |
| `WeaponGun` / `WeaponPersonal` | `SCItemWeaponComponentParams` + `SAmmoContainerComponentParams` |
| `Scanner` | A vérifier (probablement `SCItem*Params` spécifique) |
| `Radar` | A vérifier |
| `GravityGenerator` | `ItemResourceComponentParams` |
| `LifeSupportGenerator` | `ItemResourceComponentParams` |
| `Suit/Helmet` | `ItemResourceComponentParams` |
| `MobiGlas` | Pas de stats spécifiques |
| `Cargo` | Pas de stats spécifiques (juste dimensions/masse) |
| `Seat` | `ItemResourceComponentParams` (consommation vie) |
