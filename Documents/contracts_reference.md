# Reference — Contrats Star Citizen

> Fichier de référence pour retrouver rapidement les informations sur les missions de contrats.
> Données extraites des fichiers XML dans `StarXelem/datafiles/libs/foundry/records/contracts/`

---

## 1. Structure des fichiers

### Chemin de base des générateurs de contrats

```
datafiles/libs/foundry/records/contracts/contractgenerator/
```

### Générateurs par guilde / faction

| Guilde / Faction | Dossier | Fichiers principaux |
|---|---|---|
| **FTL Courier** | `interstellartransport_guild/ftl/` | `ftl_courier.xml`, `ftl_recoveritem.xml`, `ftl.xml` |
| **Red Wind** | `interstellartransport_guild/redwind/` | `redwind_hauling.xml`, `redwind_recoveritem.xml`, `redwind_recovercargo.xml` |
| **Covalex Industries** | `interstellartransport_guild/covalex industries/` | `covalex_deliverypilot.xml`, `covalex_hauling.xml`, `covalex_recovercargo.xml` |
| **Ling Family** | `interstellartransport_guild/lingfamilyhauling/` | `lingfamilyhauling_recovercargo.xml`, `lingfamilyhauling_hauling.xml` |
| **Headhunters** | `thecouncil_guild/headhunters/` | `headhunters_mercenary_fps.xml`, `hh_courier.xml`, `headhunters_investigation.xml`, `headhunters_patrol.xml`, `headhunters_recovercargo.xml`, `headhunters_destroyitems.xml` |
| **Vaughn** | `thecouncil_guild/vaughn/` | `vaughn_missingperson.xml`, `vaughn_assassination.xml`, `vaughn_generator.xml` |
| **Bitzeros** | `thecouncil_guild/bitzeros/` | `bitzeros_recoveritem.xml` |
| **Dead Saints** | `thecouncil_guild/deadsaints/` | `deadsaints_recovercargo.xml`, `deadsaints_hauling.xml`, `deadsaints_recoveritem.xml` |
| **Citizens for Prosperity** | `mercenary_guild/citizensforprosperity/` | `citizensforprosperity_defendship.xml`, `cfp_courier.xml`, `citizensforprosperity_investigation.xml`, `citizensforprosperity_hauling.xml`, `citizensforprosperity_missingpersons.xml` |
| **InterSec Defense Solutions** | `mercenary_guild/intersecdefensesolutions/` | `intersec_recoverdata.xml`, `intersec_stationassault.xml` |
| **Eckhart Security** | `mercenary_guild/eckhartsecurity/` | `eckhartsecurity_recovercargo.xml`, `eckhartsecurity_killship.xml`, `eckhartsecurity_escortships.xml` |
| **Foxwell Enforcement** | `mercenary_guild/foxwellenforcement/` | `foxwellenforcement_generator.xml`, `foxwellenforcement_defenddestructibleentities.xml` |
| **Bounty Hunter Guild** | `mercenary_guild/bountyhunterguild/` | `bountyhuntersguild_killship.xml`, `bountyhuntersguild_fps.xml` |
| **Hockrow Agency** | `mercenary_guild/hockrowagency/` | `hockrowagency_missingperson.xml`, `hockrowagency_recoveritem.xml` |
| **Rayari Inc** | `academyofsciences_guild/rayari inc/` | `rayari_recoveritem.xml`, `rayari_research.xml` |
| **Highpoint Wilderness Specialists** | `academyofsciences_guild/highpoint wilderness specialists/` | `highpointwildernessspecialists_killanimals.xml` |
| **United Resource Workers** | `unitedresourceworkers_guild/` | `united wayfarers club/unitedwayfarersclub.xml`, `shubin interstellar/shubin_resourcegathering*.xml`, `adagio holdings/adagio_resourcegathering_fpssalvage.xml` |

### Autres générateurs notables

| Fichier | Description |
|---|---|
| `roughandready_generator.xml` | Missions brutes/générales |
| `tarpits_generator.xml` | Missions du Tarpits |
| `klescher_generator.xml` | Missions de Klescher |
| `gobling_generator.xml` | Missions Gobling |
| `unaffiliated_generator.xml` | Missions non affiliées |
| `event_luminalia.xml` | Événement Luminalia |
| `yearspecificcontent.xml` | Contenu annuel |

---

## 2. Profils de difficulté

Tous les profils sont dans :

```
datafiles/libs/foundry/records/contracts/contractdifficultyprofiles/
```

### Tableau des profils

| Profil | mechWeight | mentalWeight | riskWeight | knowledgeWeight | Usage typique |
|---|---|---|---|---|---|
| **general.xml** | 0.34 | 0.33 | 0.22 | 0.11 | FPS, combat, mercenaire |
| **general_15pctboost.xml** | 0.39 | 0.36 | 0.25 | 0.13 | general + boost 15% |
| **logistics.xml** | 0.172 | 0.702 | 0.515 | 0.172 | Cargo, transport, FTL |
| **logisitics_15pctboost.xml** | 0.14 | 0.49 | 0.36 | 0.14 | logistics + boost 15% |
| **discovery.xml** | 0.153 | 0.459 | 0.306 | 0.473 | Exploration, Rayari |
| **event_difficultyprofile.xml** | 0.47 | 0.463 | 0.295 | 0.18 | Événements |
| **event_logisticsprofile.xml** | 0.129 | 0.532 | 0.39 | 0.12 | Événements cargo |

### Comment trouver les poids d'une mission

1. Lire le bloc `<difficulty>` dans le contrat
2. Repérer `<difficultyProfile ReferencedFile=".../nom_du_profil.xml" />`
3. Chercher `nom_du_profil.xml` dans le dossier `contractdifficultyprofiles/`
4. Les 4 valeurs `<xxxWeight>` sont les poids demandés

---

## 3. Templates de contrats

Tous les templates sont dans :

```
datafiles/libs/foundry/records/contracts/contracttemplates/
```

### Templates par type de mission

| Template | Type de mission | Utilisé par |
|---|---|---|
| `courier.xml` | Livraison rapide (FTL) | FTL Courier |
| `courier_1box.xml` ... `courier_5boxes.xml` | Livraison boîtes multiples | FTL Courier |
| `courier_multitosingle.xml` | Multi-versu-single | Red Wind, FTL |
| `courier_singletomulti.xml` | Single-versu-multi | Red Wind |
| `haulcargo.xml` | Transport cargo général | Tous guildes transport |
| `haulcargo_atob.xml` | A versu B (taille par suffixe) | Covalex, Red Wind |
| `haulcargo_atob_small.xml` | Cargo small | Nombreux |
| `haulcargo_atob_bulk.xml` | Cargo bulk | Nombreux |
| `haulcargo_atob_latebulk.xml` | Late bulk resources | Stanton system |
| `haulcargo_atob_small_processedfood.xml` | Nourriture transformée | Rayari, Ling Family |
| `haulcargo_multi2tosingle.xml` | Multi-versu-single | Red Wind |
| `haulcargo_singletomulti.xml` | Single-versu-multi | Red Wind |
| `eliminateall.xml` | Éliminer tous les cibles | Headhunters, CFP |
| `eliminateall_courier.xml` | Élimination + courier | Headhunters |
| `missingperson.xml` | Personne disparue | Vaughn |
| `missingperson_space.xml` | Disparue dans l'espace | Vaughn |
| `missingperson_courier.xml` | Disparue + courier | Vaughn |
| `defendship_destroyitems.xml` | Défendre navire | CFP |
| `defenddestructibleentities.xml` | Défendre entités | CFP, Headhunters |
| `recovercargo.xml` | Récupérer cargo | Headhunters, Dead Saints |
| `recoverencrypteddata.xml` | Données chiffrées | InterSec |
| `tacticalstrikegroup/tsg_fullstrikeonstation.xml` | Frappe station TSG | InterSec |
| `destroyitems.xml` | Détruire objets | CFP, Dead Saints |
| `investigation.xml` | Enquête | Headhunters, CFP |
| `patrol.xml` | Patrouille | Headhunters, CFP |
| `fps.xml` | Mission FPS | Headhunters, Eckhart |

---

## 4. Nommage des missions

### Convention de nommage

```
<Faction>_<System/Region>_<Type>_<Rank/Grade>_<Variant>
```

### Exemples par faction

| Faction | Préfixe | Exemple |
|---|---|---|
| FTL Courier | `FTL_Courier` | `FTL_Courier_Stanton_Food_Rank0` |
| Headhunters | `HH` | `HH_Pyro_RegionB_M_CFPOutposts_EliminateAll` |
| Vaughn | `Vaughn` | `Vaughn_Stanton_MissingPersonSpace_VeryHard` |
| Rayari | `Rayari` | `Rayari_Rank1_Harvestable_Single_JuviTeeth` |
| CFP | `CFP` | `CFP_Pyro_RegionA_M_2AsteroidBase_Criminals_DefendShip_BombingRun` |
| InterSec | `InterSec` / `IDS` | `InterSec_Nyx_TSG_FullStrikeOnStation`, `IDS_RecoverEncryptedData_Intro` |
| Red Wind | `RedWind` | `RedWind_Pyro_BulkGrade_Solar_CFP_StationToStation_Waste_CargoHauling_Multi2ToSingle` |
| Covalex | `Covalex` | `Covalex_Stanton_M_...` |
| Ling Family | `LingFamily` | `LingFamily_Stanton_...` |
| Dead Saints | `DeadSaints` | `DeadSaints_Pyro_...` |
| Bitzeros | `Bitzeros` | `Bitzeros_Stanton_...` |
| Eckhart | `Eckhart` | `Eckhart_Pyro_...` |
| Foxwell | `Foxwell` | `Foxwell_Pyro_...` |
| Hockrow | `Hockrow` | `Hockrow_Pyro_...` |
| Bounty Hunter | `BHG` | `BHG_Pyro_...` |

### Indicateurs de grade/rang

| Suffixe | Signification |
|---|---|
| `Rank0` | Rang 0 (débutant) |
| `Rank1` ... `Rank6` | Rangs progressifs |
| `Intro` | Mission d'introduction |
| `VeryHard` | Très difficile |
| `M` | Medium |
| `L` | Large / Long |
| `S` | Small / Short |
| `RegionA`, `RegionB`, `RegionC` | Région du système |
| `BulkGrade` | Cargo en vrac |
| `SmallGrade` | Petit cargo |

---

## 5. Recherche rapide d'une mission

### Méthode en 3 étapes

**1. Trouver le fichier contenant la mission :**

```bash
grep -rl "NOM_DE_LA_MISSION" datafiles/libs/foundry/records/contracts/contractgenerator/
```

**2. Extraire le bloc de difficulté :**

Trouver la ligne du `<debugName>`, puis chercher le bloc `<difficulty>` dans les ~500 lignes suivantes :

```bash
grep -n "debugName>NOM_DE_LA_MISSION<" fichier.xml
# Ensuite lire ~500 lignes à partir de cette ligne
```

Le bloc recherché ressemble à :
```xml
<contractResults>
    ...
    <timeToComplete>XX</timeToComplete>
    <difficulty Type="ContractDifficulty">
        <difficultyProfile ReferencedFile=".../nom_profil.xml" />
        <mechanicalSkill>...</mechanicalSkill>
        <mentalLoad>...</mentalLoad>
        <riskOfLoss>...</riskOfLoss>
        <gameKnowledge>...</gameKnowledge>
    </difficulty>
</contractResults>
```

**3. Lire le profil de difficulté pour les poids :**

```bash
cat datafiles/libs/foundry/records/contracts/contractdifficultyprofiles/nom_profil.xml
```

---

## 6. Structure XML d'un contrat

### Contrat de type CareerContract (le plus courant)

```xml
<CareerContract Type="CareerContract">
    <debugName>NOM_DE_LA_MISSION</debugName>
    <template ReferencedFile=".../contracttemplates/nom_template.xml" />
    <paramOverrides>
        <stringParamOverrides>...</stringParamOverrides>
        <propertyOverrides>...</propertyOverrides>
        <modifierOverrides>...</modifierOverrides>
    </paramOverrides>
    <subContracts Type="SubContract" Count="X">
        <SubContract>...</SubContract>
    </subContracts>
    <additionalPrerequisites>...</additionalPrerequisites>
    <generationParams>...</generationParams>
    <contractLifeTime>...</contractLifeTime>
    <contractResults>
        <contractResults Type="ContractResultBase" Count="X">
            ...
            <timeToComplete>XX</timeToComplete>
            <difficulty Type="ContractDifficulty">
                <difficultyProfile ReferencedFile=".../nom_profil.xml" />
                <mechanicalSkill>...</mechanicalSkill>
                <mentalLoad>...</mentalLoad>
                <riskOfLoss>...</riskOfLoss>
                <gameKnowledge>...</gameKnowledge>
            </difficulty>
        </contractResults>
    </contractResults>
    <minStanding ReferencedFile=".../reputation/standings/..." />
    <maxStanding ReferencedFile=".../reputation/standings/..." />
</CareerContract>
```

### Contrat de type Contract (InterSec, etc.)

Même structure mais utilise `<Contract>` au lieu de `<CareerContract>`.

---

## 7. Valeurs de difficulté courantes

### mechanicalSkill (du plus simple au plus difficile)

| Valeur | Niveau |
|---|---|
| `Easy_PvE_only_action_3` | Facile PvE |
| `Zero_risk_of_action_2` | Très facile |
| `Normal_PvE_only_action_4` | Moyen |
| `Hard_PvE_or_Easy_PvP_action_5` | Difficile |
| `Multiplayer_PvE_or_Expert_PvP_action_6` | Expert |
| `PvE_PvP_large_group_action_eg_warzone_7` | Extrême (zone de guerre) |

### mentalLoad

| Valeur | Niveau |
|---|---|
| `Requires_minimal_thought_2` | Minimal |
| `Routine_light_work_3` | Routine léger |
| `Moments_of_concentration_required_4` | Concentration requise |
| `Like_spinning_10_plates_at_once_5` | Multi-tâche intense |
| `Extremely_hard_to_manage_alone_6` | Très difficile seul |
| `Insane_complexity_NOT_soloable_7` | Extrême (non soloable) |

### riskOfLoss

| Valeur | Niveau |
|---|---|
| `Barely_even_breaking_a_sweat_2` | Peu de risque |
| `Ship_could_get_damaged_Could_lose_cargo_4` | Risque moyen |
| `Player_might_die_Ship_could_explode_5` | Risque élevé |
| `Player_likely_to_die_Ship_too_6` | Risque très élevé |
| `Without_help_Player_and_Ship_die_7` | Mortel sans aide |

### gameKnowledge

| Valeur | Niveau |
|---|---|
| `Flight_mechanics_fly_dock_quantum_3` | Mécaniques de vol |
| `Standard_understanding_FPS_flight_professions_4` | Connaissance standard FPS |
| `Expert_understanding_FPS_flight_professions_5` | Connaissance experte FPS |
| `Pro_understanding_of_optimal_tactics_6` | Tactics pro |
| `Pro_understanding_of_optimal_tactics_6` | Tactics pro |
