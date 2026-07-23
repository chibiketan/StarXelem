# Mineable Rocks - Format et Extraction des Signatures

## Localisation des fichiers

```
src/StarXelem/datafiles/libs/foundry/records/entities/mineable/
```

Chaque rocher minable est décrit dans un fichier XML nommé `mineablerock_{type}.xml`.

## Structure XML

Chaque fichier contient un élément racine `<EntityClassDefinition>` avec les composants suivants :

### Composants clés

| Composant | Description |
|---|---|
| `MineableParams` | Paramètres globaux du minage (composition, audio, effets visuels) |
| `SMineableHealthComponentParams` | Santé et résistance du rocher (zones de dégâts, rayons) |
| `SSCSignatureSystemParams` | **Signatures radar** - identification du rocher |
| `BreakableComponentParams` | Comportement de destruction (pièces, particules) |
| `HarvestableParams` | Paramètres de récolte (respawn, sous-configuration) |

## Extraction de la signature radar

La signature se trouve dans le chemin XML suivant :

```
SSCSignatureSystemParams → radarProperties → baseSignatureParams → signatures → Single[5]
```

Il y a 8 canaux de signature (`<Single>`), la signature du minéral est au **5ème index** (indice 4 en programmation) :

```xml
<SSCSignatureSystemParams Type="SSCSignatureSystemParams">
  <radarProperties Type="SSCRadarContactProperites">
    <baseSignatureParams Type="SSCSignatureSystemBaseSignatureParams">
      <signatures Type="ActivityBehaviorRequestCondition" Count="8">
        <Single>0</Single>     <!-- Canal 0 -->
        <Single>0</Single>     <!-- Canal 1 -->
        <Single>0</Single>     <!-- Canal 2 -->
        <Single>0</Single>     <!-- Canal 3 -->
        <Single>3200</Single>  <!-- Canal 4 ← SIGNATURE DU MINERAL -->
        <Single>0</Single>     <!-- Canal 5 -->
        <Single>0</Single>     <!-- Canal 6 -->
        <Single>0</Single>     <!-- Canal 7 -->
      </signatures>
    </baseSignatureParams>
  </radarProperties>
</SSCSignatureSystemParams>
```

## Classification des rochers minables

### 1. Mining spatial (Vaisseau)

**Nomenclature :** `mineablerock_asteroid{rarete}_{mineral}.xml`

```
mineablerock_asteroidlegendary_savrilium.xml
mineablerock_asteroidepic_lindinium.xml
mineablerock_asteroidrare_bexalite.xml
mineablerock_asteroiduncommon_tungsten.xml
mineablerock_asteroidcommon_iron.xml
```

| Indice | Mot-clé | Type |
|---|---|---|
| 1 | `asteroidlegendary` | Mining spatial - Rarete Legendary |
| 2 | `asteroidepic` | Mining spatial - Rarete Epic |
| 3 | `asteroidrare` | Mining spatial - Rarete Rare |
| 4 | `asteroiduncommon` | Mining spatial - Rarete Uncommon |
| 5 | `asteroidcommon` | Mining spatial - Rarete Common |

**Signatures :** Chaque minéral a une signature unique (ex: Savrilium=3200, Lindinium=3400).

### 2. Mining en surface (Première personne / Main)

**Nomenclature :** `mineablerock_fps_{mineral}.xml`

```
mineablerock_fps_aphorite.xml
mineablerock_fps_carinite.xml
mineablerock_fps_carinite_large.xml
mineablerock_fps_carinite_small.xml
mineablerock_fps_dolivine.xml
mineablerock_fps_hadanite.xml
mineablerock_fps_jaclium.xml
mineablerock_fps_jaclium_large.xml
mineablerock_fps_jaclium_small.xml
mineablerock_fps_janalite.xml
mineablerock_fps_sadaryx.xml
mineablerock_fps_saldynium.xml
mineablerock_fps_saldynium_large.xml
mineablerock_fps_saldynium_small.xml
```

**Signatures :** Signature générique **3000** pour tous les minéraux FPS.

### 3. Mining en véhicule terrestre

**Nomenclature :** `mineablerock_groundvehicle_{mineral}.xml`

```
mineablerock_groundvehicle_beradom.xml
mineablerock_groundvehicle_carinite.xml
mineablerock_groundvehicle_carinite_large.xml
mineablerock_groundvehicle_carinite_small.xml
mineablerock_groundvehicle_feynmaline.xml
mineablerock_groundvehicle_glacosite.xml
```

**Signatures :** Signature générique **4000** pour tous les minéraux ground vehicle.

### 4. Mining surface (équivalent asteroid)

**Nomenclature :** `mineablerock_surface{rarete}_{mineral}.xml`

```
mineablerock_surfacelegendary_savrilium.xml
mineablerock_surfaceepic_lindinium.xml
mineablerock_surfacerare_bexalite.xml
mineablerock_surfaceuncommon_tungsten.xml
mineablerock_surfacecommon_iron.xml
```

**Signatures :** Identiques aux rochers asteroid correspondants.

## Règles de reconnaissance

| Préfixe dans le nom | Type de minage | Signature |
|---|---|---|
| `asteroidlegendary_` | Vaisseau - Legendary | Unique par minéral |
| `asteroidepic_` | Vaisseau - Epic | Unique par minéral |
| `asteroidrare_` | Vaisseau - Rare | Unique par minéral |
| `asteroiduncommon_` | Vaisseau - Uncommon | Unique par minéral |
| `asteroidcommon_` | Vaisseau - Common | Unique par minéral |
| `surfacelegendary_` | Surface - Legendary | Unique par minéral |
| `surfaceepic_` | Surface - Epic | Unique par minéral |
| `surfacerare_` | Surface - Rare | Unique par minéral |
| `surfaceuncommon_` | Surface - Uncommon | Unique par minéral |
| `surfacecommon_` | Surface - Common | Unique par minéral |
| `fps_` | Main (FPS) | **3000** (générique) |
| `groundvehicle_` | Véhicule terrestre | **4000** (générique) |

## Composition du rocher

La composition (minéraux extraits) est définie dans un fichier externe référencé par :

```xml
<MineableParams Type="MineableParams">
  <composition ReferencedFile="file://./../../../../../libs/foundry/records/mining/rockcompositionpresets/asteroidshipmining/legendaryshipmineablesasteroid_savrilium.xml" />
</MineableParams>
```

Le chemin de la composition suit le pattern :
```
libs/foundry/records/mining/rockcompositionpresets/{type}shipmining/{rarete}shipmineables{type}_{mineral}.xml
```

## ⚠️ Piège : entités polluantes à exclure

En plus des familles `mineablerock_*` documentées ci-dessus, le dossier `entities/mineable/`
contient deux autres familles d'entités qu'il **ne faut jamais utiliser** pour extraire une
signature radar par minéral :

### 1. Rochers génériques par classe spectrale d'astéroïde

**Nomenclature :** `asteroid{c|e|i|m|p|q|s}typemineablerock[_mineral].xml`
(ex: `asteroidstypemineablerock.xml`, `asteroidstypemineablerock_iron.xml`,
`asteroidctypemineablerock_copper.xml`, ...)

Ces entités représentent la classification spectrale visuelle d'un astéroïde (types C/E/I/M/P/Q/S),
pas un minerai précis. Elles partagent **une seule signature générique par type d'astéroïde**
(ex: `4720` pour le S-type) qui n'a **rien à voir** avec la signature radar propre à chaque minéral,
et leur `composition` référence souvent plusieurs minéraux différents (cf. `asteroid_stype.xml` qui
mélange aluminium, beryl, laranite, gold, taranite, bexalite, quantainium avec la signature générique
`4720`).

Si ces entités sont traitées avant les entités canoniques `mineablerock_asteroid{rarete}_{mineral}`,
elles "gagnent la course" (le code ne garde que la première valeur trouvée par minéral) et polluent
la map avec une signature fausse — c'est la cause du bug observé sur le Bexalite (4720 au lieu de 3600).

### 2. Entités de test

**Nomenclature :** `*_test.xml` / `mineablerock_test_{mineral}.xml`

Ex: `mineablerock_test_bexalite.xml`, `asteroidstypemineablerock_test.xml`. Ces entités ont un
`displayName` non résolu (`@LOC_UNINITIALIZED`) et des valeurs de signature arbitraires/de debug
(ex: `4000` dans `mineablerock_test_bexalite.xml`, généralement déjà filtré par l'exclusion 3000/4000
mais pas toujours).

### Règle de filtrage retenue

Seules les entités dont le `RecordName` (nom de classe DataForge, ex:
`MineableRock_AsteroidRare_Bexalite`) commence par `MineableRock_` (insensible à la casse) **et**
ne contient pas `test` doivent être prises en compte pour extraire une signature radar de minéral.
Cela exclut naturellement toute la famille `AsteroidXTypeMineableRock*` (dont le `RecordName` ne
commence pas par `MineableRock_`) ainsi que les entités de test.

Voir `ExtractionTabViewModel.UpdateLocalisationAsync` pour l'implémentation de ce filtre.

### 3. Minéraux secondaires/traces dans `compositionArray`

Même en ne gardant que les entités canoniques `MineableRock_*`, leur `composition.compositionArray`
peut contenir **plusieurs minéraux différents**, pas seulement celui désigné par le nom du rocher :
les 2 premiers éléments correspondent au minéral principal (répété pour 2 paliers de qualité/quantité),
mais des éléments suivants peuvent lister des minéraux secondaires/traces qui possèdent leur propre
rocher dédié ailleurs avec leur propre signature.

Exemple : `mineablerock_asteroidrare_bexalite.xml` référence `rareshipmineablesasteroid_bexalite`
dont la composition est `[bexalite_raw, bexalite_raw, borase_ore, gold_ore]` — seul `bexalite_raw`
(1er élément) est le minéral principal du rocher "Bexalite" (signature 3600) ; `borase_ore` et
`gold_ore` sont des traces et **ne doivent pas** hériter de cette signature (Borase a sa propre
signature 3570, Gold 3585).

**Règle retenue :** ne prendre en compte que le **premier** élément de `compositionArray`
(`FirstOrDefault()`) comme minéral associé à la signature du rocher.

## Résumé des signatures par minéral

| Minéral | Rareté | Signature |
|---|---|---|
| Savrilium | Legendary | 3200 |
| Quantainium | Legendary | 3170 |
| Stileron | Legendary | 3185 |
| Lindinium | Epic | 3400 |
| Ouratite | Epic | 3370 |
| Riccite | Epic | 3385 |
| Bexalite | Rare | 3600 |
| Gold | Rare | 3585 |
| Borase | Rare | 3570 |
| Taranite | Rare | 3555 |
| Beryl | Rare | 3540 |
| Tungsten | Uncommon | 3870 |
| Torite | Uncommon | 3900 |
| Agricium | Uncommon | 3885 |
| Titanium | Uncommon | 3855 |
| Aslarite | Uncommon | 3840 |
| Laranite | Uncommon | 3825 |
| Iron | Common | 4270 |
| Aluminum | Common | 4285 |
| Silicon | Common | 4255 |
| Copper | Common | 4240 |
| Corundum | Common | 4225 |
| Quartz | Common | 4210 |
| Hephaestanite | Common | 4180 |
| Tin | Common | 4195 |
| Ice | Common | 4300 |
