# Pools de Loot (Loot Generation)

Documentation du système de génération de loot présent dans les datafiles du jeu
(`src/StarXelem/datafiles`). Basée sur les données de la version 4.10 LIVE.

---

## 1. Vue d'ensemble

Le loot n'est **pas** défini par des listes d'objets figées. Le système fonctionne par
**requêtes de tags** : chaque table de loot décrit des *pools* via des combinaisons de tags
(type d'objet, rareté, fabricant, faction, événement…), et le jeu sélectionne à l'exécution
les objets dont les tags correspondent. Un objet "fait partie d'un pool" uniquement parce
que ses tags d'entité satisfont la requête du pool.

Deux générations du système coexistent dans les données :

| Génération | Records | Usage principal |
| :--- | :--- | :--- |
| **V1 (legacy)** | `LootTable`, `LootArchetype` | Loot des PNJ (`LootGenerationComponentParams` des acteurs), anciens conteneurs |
| **V3** | `LootTableV3Record`, `LootArchetypeV3Record`, `PoolFilterRecord`, `LootV3SecondaryChoices*` | Conteneurs lootables placés dans le monde via le système *harvestable* (slot presets) |

### Chaîne de résolution complète (V3)

```
Lieu / placement (tags du slot, ex: WeaponsRack_Stocked + Common)
  └─> SubHarvestableMultiConfigRecord  (slotpreset : quel sous-config pour quels tags)
        └─> SubHarvestableConfigRecord (liste de slots ; probabilité relative par entité)
              └─> SubHarvestableSlot
                    ├─ harvestableEntityClass  (le conteneur/rack physique spawné)
                    ├─ harvestableSetup        (distance d'activation, ex: 30 m)
                    └─ lootConfig (LootConfig)
                         ├─ lootConstraints (filtre de pool, choix secondaires, remplissage, chance)
                         └─ lootTableV3  ──> LootTableV3Record
                                               └─> entrées pondérées ──> LootArchetypeV3
                                                     └─> sélecteurs de tags ──> objets du jeu
```

### Chaîne de résolution PNJ (V1)

```
Acteur (records/actor/actors/**)
  └─> LootGenerationComponentParams
        └─> lootConfig (LootConfig)
              ├─ lootConstraints (fullness, limite de résultats, chanceToGenerate)
              └─ lootTable ──> LootTable (V1)
                                └─> WeightedLootArchetype ──> LootArchetype (V1)
                                      └─> groupes primaire/secondaires de tags pondérés
```

---

## 2. Fichiers et dossiers

Racine : `src/StarXelem/datafiles/libs/foundry/records/`

### Dossier principal : `lootgeneration/`

| Dossier | Nb fichiers | Contenu |
| :--- | ---: | :--- |
| `lootgeneration/loottables/` | 220 | Tables de loot. Préfixes : `loottable_*` (V1, 89), `v3loottable_*` (V3, 59), plus des tables thématiques (`generalloot.xml`, `armor.xml`, `ammo.xml`, `drugs.xml`, `firepower.xml`, `harvestables*.xml`…). Sous-dossiers : `animals/` (kopion), `contestedzone/` (grades B/C/D + `npc/`), `derelict/`, `distributioncenters/`, `ugf/` |
| `lootgeneration/lootarchetypes/` | 267 | Archétypes = définition des pools d'objets par tags. `lootarchetype_*` (V1, 233 records), `v3lootarchetype_*` (V3, 34 records) |
| `lootgeneration/filters/` | 15 | `v3poolfilter_*.xml` (`PoolFilterRecord`) : filtres globaux appliqués au pool d'objets avant tirage |
| `lootgeneration/secondarychoices/` | 25 | `v3secondarychoices_*` (une couche), `v3multichoice_*` (multi-couches), `v3manufacturerweights_*` (pondération par fabricant) |

### Dossiers liés

| Dossier | Rôle |
| :--- | :--- |
| `tagdatabase/tagdatabase.tagdatabase.xml` | Base de tous les tags (arbre hiérarchique, ~18 800 tags). **Indispensable** pour traduire les GUID en noms lisibles (voir `Documents/Datafiles/Tags.md`) |
| `harvestable/slotpresets/` | `v3slotpreset_*.xml` (`SubHarvestableMultiConfigRecord`) : mappe des tags de placement vers des sous-configs. C'est ici que `lootTableV3`, `poolFilter` et `secondaryChoices` sont assemblés par lieu (generic, criminal, military, hightech, lowtech, contestedzones, distributioncentres, orbageddon, soo, stormbreaker…) |
| `harvestable/subharvestableconfigs/` | `v3subharvestableconfig_*.xml` (53 fichiers) : listes de slots avec `relativeProbability` par entité spawnable (racks d'armes pré-remplis, etc.) |
| `harvestable/harvestablesetups/` | Paramètres de spawn/streaming (ex : `v3harvestablesetup_lootbox_30m.xml` = activation à 30 m) |
| `harvestable/harvestablepresets/` | Presets par lieu concret (caves par planète et richesse, distribution centres par zone et niveau de sécurité, contested zones, bases astéroïdes…) |
| `entities/scitem/lootables/` | Entités des conteneurs physiques (`lootable_generated_container_{small,medium,large}_{common,uncommon,rare,epic,legendary}.xml`, coffres verrouillés, racks…). L'entité ne contient **pas** la table : elle est fournie par le placement |
| `entities/scitem/usables/` | Racks d'armes pré-remplis (`weapon_rack_1_*_{common,...}.xml`) référencés par les subharvestableconfigs |
| `inventorycontainers/lootcontainers/` | Capacité physique des conteneurs (volume utilisé par le calcul de remplissage) |
| `actor/actors/**` | PNJ ; 1 284 fichiers contiennent un `LootGenerationComponentParams` |

> Note : le rattachement final d'un `slotpreset` à un lieu précis (avant-poste, épave, zone)
> se fait dans les object containers du niveau (socpak), qui ne sont pas présents dans ce
> sous-ensemble de datafiles. Les tags portés par le point de spawn dans le niveau
> (ex : `Armour_Common`, `WeaponsRack_Stocked`) servent de clé de correspondance dans le
> `taggedConfigs` du slotpreset.

---

## 3. Structures de données

### 3.1 LootTable V1 (`LootTable.*`)

Liste de `WeightedLootArchetype` : tirage pondéré d'un archétype.

```xml
<LootTable.GeneralLoot Type="LootTable">
  <lootArchetypes Type="WeightedLootArchetype" Count="2">
    <WeightedLootArchetype>
      <archetype ReferencedFile=".../lootarchetype_randomloot_common.xml" />
      <weight>3</weight>
      <numberOfResultsConstraints>          <!-- 0/0 = pas de contrainte -->
        <minResults>0</minResults><maxResults>0</maxResults>
      </numberOfResultsConstraints>
    </WeightedLootArchetype>
    ...
```

### 3.2 LootArchetype V1 (`LootArchetype.*`)

Un archétype = un **groupe primaire** (quoi) + des **groupes secondaires** (qualificatifs) :

- `excludedTags` : objets bannis du pool (ex : tag `Legendary`).
- `primaryOrGroup/entries` (`LootArchetypeEntry_Primary`) : chaque entrée a un `tag`
  principal + `additionalTags` (`TagsDNFTerm` : `positiveTags` ET requis, `negativeTags`
  interdits) + `weight`.
- `secondaryOrGroups` (`LootArchetypeOrGroup_Secondary`) : par groupe (ex "Rarity"), tirage
  pondéré d'un tag supplémentaire qui vient s'ajouter à la requête.
- `optionalData` par entrée : `EntryOptionalData_StackSize` (quantité min/max),
  `EntryOptionalData_SpawnWith` (ex : « munitions adaptées à l'arme tirée », via `tagsToMatch`).

Exemple (`lootarchetype_container_armour_contestedzones.xml`) : primaire = 50/50 entre
`ContestedZone+Armor` et `ContestedZone+FPS` (armes, hors `Attachment` et `HeavyHip`) ;
secondaire "Rarity" = Common 100 / Uncommon 100 / Rare 100 / Epic 10 ; `Legendary` exclu.

### 3.3 LootTableV3 (`LootTableV3Record.*`)

`lootTable/lootArchetypes` = liste de `LootTableV3Entry`, chacune avec :

- `name` : libellé lisible du pool (ex : "Heavy helmet", "Mission item 10").
- `weight` : poids de tirage de l'entrée.
- `archetype` : soit inline (`LootArchetypeV3` avec ses `entries`), soit référence
  (`LootArchetypeV3_RecordRef` vers un fichier `v3lootarchetype_*.xml`).
- `optionalData` :
  | Type | Champ | Effet |
  | :--- | :--- | :--- |
  | `LootTableOptionalDataV3_ChoiceLimit` | `choiceLimit` | Nb max de fois où cette entrée peut être choisie |
  | `LootTableOptionalDataV3_DupeLimit` | `dupeLimit` | Nb max de doublons du même objet |
  | `LootTableOptionalDataV3_ChanceToExist` | `chanceToExist` (0–1) | Chance que l'entrée participe au tirage (ex : 0.75) |
  | `LootTableOptionalDataV3_PriorityChoice` | `priorityCount` | L'entrée est résolue en priorité N fois avant le tirage pondéré |

### 3.4 LootArchetypeV3 (`LootArchetypeV3Record.*` ou inline)

`entries` (`LootArchetypeV3Entry`) : `name`, `weight`, et un `selector` :

- `LootArchetypeV3Selector_Tags` (248 usages) : `TagsDNFTerm` — l'objet doit porter **tous**
  les `positiveTags` et **aucun** `negativeTags`.
- `LootArchetypeV3Selector_EntityClasses` (32 usages) : liste explicite d'entités
  (`EntityClassList_Manual`), utilisée pour les objets précis (ex : scrip du Council,
  objets de mission).
- `optionalData` : `ArchetypeOptionalDataV3_StackSize` (`QuantityRange_Linear` min/max),
  `ArchetypeOptionalDataV3_SpawnWith`.

Exemple (`v3lootarchetype_undersuit.xml`) : 50/50 entre `Undersuit` (sans `Helmet`) et
`Flightsuit` (sans `Helmet`).

### 3.5 Pool filters (`PoolFilterRecord.*`, dossier `filters/`)

Filtres appliqués **en amont** sur l'ensemble du pool d'objets candidats :

- `PoolFilter_Tags` : DNF de tags (ex : `v3poolfilter_nofactions.xml` = exclut les objets
  taggés `Faction` ou `LootFaction`).
- `PoolFilter_RecordRef` : référence un autre filtre.
- `PoolFilter_Sequence` : enchaîne des `PoolFilterInstance` (mode `Cumulative`).

`v3poolfilter_generic.xml` (le plus utilisé : 1 241 références) = "No faction" +
"No special events". Les filtres d'événements/lieux (`v3poolfilter_orbageddon`, `_soo`,
`_stormbreaker_*`, `_asddelving*`, `_rockcracker*`, `_welcometonyx`, `_tsg`, `_dcdelving`)
autorisent au contraire les objets de leur événement.

### 3.6 Secondary choices (dossier `secondarychoices/`)

Tirages **supplémentaires** appliqués après l'archétype pour affiner la requête :

- `LootV3SecondaryChoicesSingleLayerRecord` : une couche = liste pondérée de
  `LootV3SecondaryChoiceEntry` (nom, poids, sélecteur de tags).
  - `v3secondarychoices_rarity_{common,uncommon,rare,legendary}.xml` : pondération des
    tags de rareté.
  - `v3secondarychoices_faction.xml` : Faction (poids 0.1) vs Non-faction (poids 1).
  - `v3manufacturerweights_{criminal,military,hightech,lowtech,template}.xml` : pondération
    par fabricant d'armes (ex profil criminel : APAR 100, GMNI 50, KLWE 10, BEHR 1, BANU 0.1…).
- `LootV3SecondaryChoicesMultiLayerRecord` (`v3multichoice_*.xml`) : empile plusieurs
  couches. Ex `v3multichoice_generic_common.xml` = couche Faction + couche Rareté Common.
  Les 12 combinaisons `{generic,criminal,military,hightech,lowtech} × {common,uncommon,rare}`
  couvrent les profils de lieux.

### 3.7 LootConfig / LootConstraints (le point d'attache)

Présent chez les acteurs (`LootGenerationComponentParams`) et dans les slots harvestable :

```xml
<lootConfig Type="LootConfig">
  <lootConstraints Type="LootConstraints">
    <poolFilter ReferencedFile=".../v3poolfilter_generic.xml" />
    <secondaryChoices Type="LootV3SecondaryChoicesRecordRef_MultiLayer">
      <multiLayerRecord ReferencedFile=".../v3multichoice_generic_rare.xml" />
    </secondaryChoices>
    <fullnessFactorRange><min>0.8</min><max>1</max></fullnessFactorRange>
    <totalResultsLimit>15</totalResultsLimit>
    <chanceToGenerate>1</chanceToGenerate>
    <chanceToGenerateAdditionalAttachedInventories>1</chanceToGenerateAdditionalAttachedInventories>
    <advanced><pruningLevel>containerSize</pruningLevel><fullnessMode>stopAfterExceed</fullnessMode></advanced>
  </lootConstraints>
  <lootTable />                                <!-- V1, ou -->
  <lootTableV3 ReferencedFile=".../v3loottable_armour_large.xml" />
</lootConfig>
```

| Champ | Signification |
| :--- | :--- |
| `fullnessFactorRange` | Fraction du volume du conteneur à remplir (tirée entre min et max). PNJ : souvent 0.1–0.4 ; caisses : 0.8–1 |
| `totalResultsLimit` | Nb max d'objets générés (PNJ : 4 ; grande caisse : 15) |
| `chanceToGenerate` | Probabilité que du loot soit généré du tout (presque toujours 1 ; quelques cas 0.6 et 0.25) |
| `chanceToGenerateAdditionalAttachedInventories` | Chance de looter aussi les inventaires attachés (ex : chargeur dans l'arme) |
| `pruningLevel` / `fullnessMode` | Élagage des objets trop gros pour le conteneur ; arrêt du remplissage après dépassement |

---

## 4. Comment identifier les objets d'un pool

Les objets (armes, armures, consommables…) portent une liste `<tags>` au niveau racine de
leur `EntityClassDefinition` (références vers `tagdatabase`). Exemple
(`entities/scitem/weapons/fps_weapons/apar_hmg_ballistic_01.xml`) :
`FPS`-related tags + `APAR` (fabricant) + `ApocalypseArms` + `S5` + `Common` (rareté) +
**`CanGenerateAsLoot`** + `LootableFromSuit` + `Combat`, etc.

Règles pratiques :

1. **Éligibilité** : le tag `CanGenerateAsLoot` marque un objet éligible à la génération.
2. **Appartenance à un pool** : l'objet appartient à un pool si ses tags satisfont le
   `TagsDNFTerm` du sélecteur (tous les positifs, aucun négatif), après application du
   `poolFilter` et des tags des `secondaryChoices`.
3. **Rareté** : tags `Common` / `Uncommon` / `Rare` / `Epic` / `Legendary` portés par l'objet.
4. **Fabricant / faction / événement** : tags dédiés (ex `Behring`, `Gemini`, `LootFaction`,
   `SoO`, `PyroOutlaw`…).

Pour reconstituer un pool dans StarXelem : indexer les tags de toutes les entités, puis
évaluer la requête DNF de chaque entrée d'archétype contre cet index.

---

## 5. Calcul des probabilités

Tous les tirages sont des **tirages pondérés simples** : `P(entrée) = weight / Σ weights`
au sein du groupe. Les probabilités finales se composent en multipliant les étapes :

```
P(objet) = chanceToGenerate
         × P(entrée de table)          (poids dans la LootTable)
         × P(entrée d'archétype)       (poids dans le LootArchetype)
         × P(choix secondaires)        (produit des couches : faction, rareté, fabricant)
         × 1/N objets équivalents      (choix uniforme parmi les objets qui matchent la requête)
```

### Exemples chiffrés (données réelles)

**Table `generalloot.xml` (V1)** — poids 3 vs 0.25 :

| Pool | Poids | Probabilité |
| :--- | ---: | ---: |
| `lootarchetype_randomloot_common` | 3 | 92,3 % |
| `lootarchetype_harvestablecache_common` | 0.25 | 7,7 % |

**Couche rareté `v3secondarychoices_rarity_common.xml`** — Σ = 111.1 :

| Rareté | Poids | Probabilité |
| :--- | ---: | ---: |
| Common | 100 | 90,0 % |
| Uncommon | 10 | 9,0 % |
| Rare | 1 | 0,9 % |
| Epic | 0.1 | 0,09 % |

**Couche faction `v3secondarychoices_faction.xml`** : objets faction 0.1/1.1 ≈ 9,1 %,
non-faction ≈ 90,9 %.

**Table `v3loottable_armour_large.xml`** (23 entrées, Σ ≈ 486.4) — extraits :

| Pool | Poids | Probabilité |
| :--- | ---: | ---: |
| Chaque pièce Heavy (casque/bras/torse/jambes) | 100 | ≈ 20,6 % chacune |
| Mission items (cumulés 10+7.5+5+2.5+1) | 26 | ≈ 5,3 % |
| Chaque pièce Medium | 10 | ≈ 2,1 % |
| Heavy backpack / Full suit | 12.5 | ≈ 2,6 % |
| Chaque pièce Light / Undersuit | 1 | ≈ 0,21 % |
| Light backpack | 0.125 | ≈ 0,026 % |

Le tirage est répété jusqu'à atteindre `fullnessFactorRange` ou `totalResultsLimit`
(en respectant `choiceLimit` / `dupeLimit` par entrée).

**Fabricants (profil criminel, armes)** : APAR 100 ≫ GMNI 50 ≫ KLWE 10 ≫ BEHR 1 ≫ BANU/HDGW 0.1 —
un lieu "criminel" loote donc massivement de l'Apocalypse Arms.

### Probabilité du conteneur lui-même

Dans les `SubHarvestableConfigRecord`, chaque slot a `relativeProbability` (tirage pondéré
de l'entité spawnnée : quel rack, quelle caisse) et le config a `initialSlotsProbability`
(chance qu'un slot soit peuplé). Le `harvestableSetup` (ex : lootbox 30 m) contrôle le
spawn/respawn (`configRespawnTimeMultiplier`, `harvestableRespawnTimeMultiplier`).

---

## 6. Tags fréquents (GUID → nom)

Résolus via `tagdatabase.tagdatabase.xml` :

| GUID | Nom |
| :--- | :--- |
| `59ca5e36-b9a2-4459-ad0e-5745bcf5d899` | Common |
| `9a46e2f3-6651-4283-8447-c01e6dd778e8` | Uncommon |
| `dfbc6af5-6211-4d71-8d10-b64c66ea95a9` | Rare |
| `b285311b-1f68-4a69-b73f-1295e83a3909` | Epic |
| `cc8957cf-45da-4a55-84cc-af4a125eff2e` | Legendary |
| `bed8e09b-a5f2-4f4e-9be9-ab618ebfbd58` | CanGenerateAsLoot |
| `077071be-c789-4dce-a10e-f53d57c9477c` | LootableFromSuit |
| `b14fcbf7-efa4-4128-8151-5acb23974a20` | FPS (armes personnelles) |
| `7358a985-b422-4e18-a9ce-6ec8f67297fe` | Armor |
| `ea0c4066-3bbc-4b88-b56a-a1550d32d65c` | Attachment |
| `41aa0984-180e-4af0-9f40-d34a5b01c683` / `7fd80cf6…fd7e` / `5bbbbf29…0baf` | Light / Medium / Heavy |
| `1caa8797…0c31` / `0381ccf3…628b` / `89c12e9a…f9f9` / `4cd7531a…92e2` | Helmet / Arms / Core / Legs |
| `ee684f41…5365` / `297c8605…8694` | Undersuit / Flightsuit |
| `9651ca6b-8778-4fd4-90c0-fd5320a459f8` | Faction |
| `b76e5e37-58b1-4fd9-b5ce-f3a8ed11bf62` | LootFaction |
| `587a395f…e936` / `755e8d4c…0697` / `25992b0c…7475` / `b9593d0c…e646` | ApocalypseArms / Behring / Gemini / KlausWerner |
| `e2489168-748e-4b8d-bcd2-b93292b4ad3f` | ContestedZone |

---

## 7. Récapitulatif pour exploitation dans StarXelem

- **Nom des pools** : `RecordName` du fichier (`LootTableV3Record.V3LootTable_Armour_Large`)
  et champ `<name>` de chaque entrée ("Heavy helmet"…).
- **Contenu d'un pool** : évaluer le sélecteur DNF (positifs/négatifs) + `excludedTags` +
  `poolFilter` + couches `secondaryChoices` contre les tags des entités `scitem`.
- **Taux de drop** : normaliser les `weight` par groupe, composer les étapes (section 5),
  appliquer `chanceToExist`, `choiceLimit`, `dupeLimit`, `stackSize`.
- **Rattachement** :
  - PNJ → `LootGenerationComponentParams` dans `records/actor/actors/**` (1 284 fichiers) ;
  - Conteneurs du monde → `harvestable/slotpresets` + `subharvestableconfigs`
    (le lien lieu→slotpreset est dans les socpak, hors datafiles) ;
  - Racks pré-remplis → entités `usables/weapon_rack_*` listées par les subconfigs.
- **Résolution des tags** : obligatoirement via `tagdatabase.tagdatabase.xml`
  (les fichiers de loot ne contiennent que des GUID).
