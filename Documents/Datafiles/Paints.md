# Paints
Data for ship liveries/paints are stored in `src/StarXelem/datafiles/libs/foundry/records/entities/scitem/ships/paints/`.

## Properties
- **Type**: Located in `SAttachableComponentParams` -> `AttachDef` -> `Type` (typically `Paints`).
- **Color Palette**: The actual color definitions are stored in separate palette files. The reference is found in `SGeometryResourceParams` -> `Geometry` -> `SubGeometry` -> `Palette` -> `RootRecord`.
