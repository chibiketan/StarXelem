---
name: starbreaker-inspect-types
description: >-
  Inspect types (classes, structs, enums) inside the StarBreaker DLLs using
  the project's PowerShell helper script. Always use this skill when you need
  to understand the shape of a StarBreaker type before writing code that
  consumes it.
---

# StarBreaker Type Inspection

Inspect the definition of types inside the StarBreaker DLLs shipped in `libs/`.

## How it works

The project ships a PowerShell script that loads every DLL in `libs/` under
**pwsh** (PowerShell 7+, which runs on .NET Core and can load .NET 10
assemblies) and returns the full reflection metadata as JSON.

```
pwsh -ExecutionPolicy Bypass -File scripts/inspect_types.ps1 `
  -ClassRegex "<regex>" `
  -TargetDll "<dll>" `
  -Json
```

### Parameters

| Param | Required | Default | Description |
|---|---|---|---|
| `-ClassRegex` | yes | — | .NET regex matched against the **short type name** |
| `-TargetDll` | no | `StarBreaker.DataCore.dll` | DLL file name inside `libs/` |
| `-Json` | no | off | Return structured JSON instead of human-readable text |

**Always pass `-Json`** so the result is machine-parseable.

## DLL reference

| DLL | Types | Contains |
|---|---|---|
| `StarBreaker.DataCore.Generated.dll` | ~7 500 | All generated game-data types (entities, contracts, blueprints, spawns…) |
| `StarBreaker.Grpc.dll` | ~6 000 | gRPC service stubs and protobuf messages |
| `StarBreaker.Common.dll` | 22 | Shared helpers: `CigGuid`, `Tag`, `RGB`, … |
| `StarBreaker.DataCore.dll` | 30 | DataCore runtime: `DataCoreDatabase`, `DataCoreRecord`, `DataCorePointer`, … |
| `StarBreaker.P4k.dll` | 19 | P4K archive reader |
| `StarBreaker.CryChunkFile.dll` | 33 | Chunk-file parser |
| `StarBreaker.Chf.dll` | 19 | CHF format |
| `StarBreaker.Dds.dll` | 16 | DDS images |
| `StarBreaker.Extraction.dll` | 15 | Extraction helpers |
| `StarBreaker.CryXmlB.dll` | 6 | Binary XML |
| `StarBreaker.Wwise.dll` | 6 | Audio |

When you do not know which DLL a type lives in, start with
`StarBreaker.DataCore.Generated.dll` — it holds the vast majority of game
data types.

## JSON schema

The `-Json` flag emits a single JSON object:

```json
{
  "dll": "StarBreaker.DataCore.Generated.dll",
  "regex": "EntityClassDefinition",
  "total": 1,
  "failed": 0,
  "types": [
    {
      "fullName":   "StarBreaker.DataCoreGenerated.EntityClassDefinition",
      "name":       "EntityClassDefinition",
      "namespace":  "StarBreaker.DataCoreGenerated",
      "baseType":   "Object",
      "interfaces": ["IDataCoreReadable`1", "IDataCoreReadable", "IEquatable`1"],
      "isClass":    true,
      "isAbstract": false,
      "isSealed":   false,
      "isEnum":     false,
      "properties": [
        {
          "name":      "selfId",
          "type":      "StarBreaker.Common.CigGuid",
          "typeShort": "CigGuid",
          "canRead":   true,
          "canWrite":  true,
          "modifiers": []
        }
      ],
      "methods": [
        {
          "name":       "Read",
          "return":     "EntityClassDefinition",
          "parameters": [
            { "name": "reader", "type": "DataCoreBinaryReader" },
            { "name": "version", "type": "Int32" }
          ],
          "modifiers": ["static"]
        }
      ],
      "fields": [
        {
          "name":      "<selfId>k__BackingField",
          "type":      "CigGuid",
          "modifiers": ["readonly"]
        }
      ]
    }
  ]
}
```

## Step-by-step workflow

1. **Decide which DLL to query.** If unsure → `StarBreaker.DataCore.Generated.dll`.

2. **Run the script with a regex that matches the target type name(s).**
   Use the Bash tool to invoke `pwsh`.

   Example — inspect `EntityClassDefinition`:
   ```
   pwsh -ExecutionPolicy Bypass -File scripts/inspect_types.ps1 `
     -ClassRegex "EntityClassDefinition" `
     -TargetDll "StarBreaker.DataCore.Generated.dll" `
     -Json
   ```

3. **Parse the JSON result.** The `types` array contains one entry per
   matching type. Each entry lists its properties (name, type, read/write,
   modifiers), methods (signature + parameters), and fields.

4. **Drill into referenced types.** If a property's type is another
   StarBreaker type you don't know, repeat step 2 for that type name.

5. **Use the information to write correct C# code** that reads from or
   writes to these types.

## Practical examples

### Find the shape of a contract handler

```
pwsh -ExecutionPolicy Bypass -File scripts/inspect_types.ps1 `
  -ClassRegex "ContractGeneratorHandler" `
  -TargetDll "StarBreaker.DataCore.Generated.dll" -Json
```

Returns every `ContractGeneratorHandler*` type with their properties —
useful to understand how mission templates are structured.

### Inspect a specific gRPC message

```
pwsh -ExecutionPolicy Bypass -File scripts/inspect_types.ps1 `
  -ClassRegex "ShipInfo" `
  -TargetDll "StarBreaker.Grpc.dll" -Json
```

### List all blueprint-related types

```
pwsh -ExecutionPolicy Bypass -File scripts/inspect_types.ps1 `
  -ClassRegex "Blueprint" `
  -TargetDll "StarBreaker.DataCore.Generated.dll" -Json
```

### Discover what `CigGuid` looks like

```
pwsh -ExecutionPolicy Bypass -File scripts/inspect_types.ps1 `
  -ClassRegex "CigGuid" `
  -TargetDll "StarBreaker.Common.dll" -Json
```

## Important notes

- **Always use `pwsh`**, not `powershell`. The script loads .NET 10
  assemblies which PowerShell 5.1 (on .NET Framework 4.x) cannot resolve.
- The `-ClassRegex` is matched against the **short type name** (no
  namespace). Use `.*` to enumerate every type in a DLL.
- Properties marked `modifiers: ["inherited"]` come from a base class —
  inspect the base type for the full picture.
- Generated types implement `IDataCoreReadable<T>` and expose a static
  `Read(reader, version)` method that deserialises the type from a
  DataCore binary stream.
- `DataCorePointer<T>` and `DataCoreReference<T>` are generic wrappers
  used for cross-record references. Their `T` tells you what type they
  point to.
- When the JSON output is very large (many matches), narrow the regex or
  query individual types to keep the response manageable.
