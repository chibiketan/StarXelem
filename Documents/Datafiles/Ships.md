# Ships
Ship definitions are stored in `src/StarXelem/datafiles/libs/foundry/records/entities/spaceships/`.

## Structure
Ships use the `EntityClassDefinition` root element.

### Key Sections
- **Basic Info**: 
    - `StaticEntityClassData` -> `EntityUIDisplayParams`: Contains `displayName`, `displayDescription`, and `displayIcon`.
- **Technical Specs**:
    - `Components` -> `VehicleComponentParams`:
        - `manufacturer`: Reference to the manufacturer.
        - `vehicleDefinition`: Path to the implementation XML.
        - `movementClass`: Movement category (e.g., "Spaceship").
        - `crewSize`: Number of crew.
        - `vehicleCareer` & `vehicleRole`: The intended use and role of the ship.
        - `vehicleHullDamageNormalizationValue`: Base health/durability value.
        - `physicsGrid`: References to voxel data and physics proxies.
- **Loadout (Default Parts)**:
    - `Components` -> `SEntityComponentDefaultLoadoutParams`: Maps `itemPortName` (hardpoints) to specific `entityClassName` (e.g., power plants, coolers, shields, weapons).
- **Classification**:
    - `Components` -> `SAttachableComponentParams` -> `AttachDef`:
        - `Size`, `Grade`: Classification of the ship's size and grade.
        - `Manufacturer`: Redundant manufacturer reference.
        - `Localization`: Human-readable names and descriptions.
- **Tags**:
    - `tags`: A list of references to the tag database (`tagdatabase.tagdatabase.xml`).
