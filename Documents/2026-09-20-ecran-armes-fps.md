# Plan d'action — Écran « Armes FPS »

Date : 2026-09-20 · Branche : `feature/ajout_armement_fps`

## 1. Objectif

Ajouter un onglet affichant les armes FPS du jeu dans un tableau, avec :

- recherche par nom ;
- filtre à double curseur sur les dégâts (min–max) ;
- filtre à double curseur sur la portée d'efficacité.

## 2. Cartographie des données (vérifiée sur l'Arlington)

Fichier de référence :
`src/StarXelem/datafiles/libs/foundry/records/entities/scitem/weapons/fps_weapons/hdgw_rifle_ballistic_01_tint02.xml`

### 2.1 Chaîne de résolution

L'arme ne contient pas ses propres statistiques de tir : il faut traverser trois
enregistrements.

```
EntityClassDefinition (l'arme)
├── SAttachableComponentParams.AttachDef          → identité (nom, type, taille, fabricant)
└── SCItemWeaponComponentParams
    ├── fireActions[]                             → modes de tir (cadence, plombs, multiplicateur)
    ├── ammoRepoolParams                          → extraction des balles du sac à dos
    ├── weaponDegradationModifier.weaponStats     → modificateurs
    └── ammoContainerRecord  ─► EntityClassDefinition (le CHARGEUR)
        └── SAmmoContainerComponentParams
            ├── maxAmmoCount                      → balles par chargeur
            └── ammoParamsRecord ─► AmmoParams (la MUNITION)
                ├── speed, lifetime               → portée maximale
                └── projectileParams (BulletProjectileParams)
                    ├── damage.DamagePhysical     → dégâts
                    └── damageDropParams          → portée d'efficacité
```

### 2.2 Emplacement exact de chaque donnée demandée

| Donnée | Chemin | Valeur Arlington |
|---|---|---|
| **Nom** | `SAttachableComponentParams.AttachDef.Localization.Name` (clé locale, résolue via `IP4kService.GetEntityClassName`) | `@item_Namehdgw_rifle_ballistic_01_tint02` |
| **Dégâts** | `AmmoParams.projectileParams.damage.DamagePhysical` | 80 |
| **Portée maximale** | `AmmoParams.speed × AmmoParams.lifetime` (calcul) | 550 × 2 = **1100 m** |
| **Portée d'efficacité max.** | `damageDropParams.damageDropMinDistance.DamagePhysical` | **50 m** |
| **Distance de plancher** (info) | `minDistance + (dégâts − damageDropMinDamage) / damageDropPerMeter` | 50 + (80−35)/0,1 = **500 m** |
| **Balles par chargeur** | chargeur → `SAmmoContainerComponentParams.maxAmmoCount` | 20 |
| **Modificateurs** | `SCItemWeaponComponentParams.weaponDegradationModifier.weaponStats` (`SWeaponStats`) | cadence, dégâts, vitesse, plombs, dispersion, recul… |
| **Extraction sac à dos** | `SCItemWeaponComponentParams.ammoRepoolParams` | `unstowMagDuration` 1 s · `bulletsPerSecond` 10 · `fullMagMergeDuration` 0,01 s |
| **Type de munition** | 3ᵉ segment du nom technique (`ammoCategory` vaut `None` sur 62 des 64 munitions FPS : inexploitable) | `ballistic` → Balistique |
| **Type de dégâts** | canal dominant de `projectileParams.damage` | Physique (primaire), Énergie (secondaire) |
| **Durée de recharge** | ❌ **absente de DataCore** — voir §2.4 | — |

Types confirmés par réflexion sur `StarBreaker.DataCore.Generated.dll` :
`SCItemWeaponComponentParams.ammoContainerRecord` est bien un `EntityClassDefinition`
déréférencé, `SWeaponAmmoRepoolParams` expose `bulletsPerSecond` / `unstowMagDuration` /
`fullMagMergeDuration`, et `BulletDamageDropParams` expose les trois `DamageBase`
(à caster en `DamageInfo` pour lire `DamagePhysical`).

### 2.3 Dégâts et capacités : six pièges

1. **Multi-modes.** L'Arlington a deux `fireActions` (`Slug` et `Single`) et
   **deux munitions** (`ammoParamsRecord` 44 mm + `secondaryAmmoParamsRecord`
   shotgun). Les dégâts sont une propriété du **mode de tir**, pas de l'arme.
2. **Plombs.** Le 12g donne `DamagePhysical = 2` — c'est le dégât **par plomb**.
   Dégâts réels par tir = `damage × launchParams.pelletCount × launchParams.damageMultiplier`.
3. **Modes de tir imbriqués.** Certaines armes n'exposent pas leurs modes au premier niveau
   de `fireActions` : le Killshot Rifle (`none_rifle_multi_01`) les enferme dans un
   `SWeaponActionSequenceParams`, le Prism Laser Shotgun (`volt_shotgun_energy_01`) dans un
   `SWeaponActionDynamicConditionParams`. Un parcours plat de `fireActions` fait remonter ces
   armes **sans aucune statistique de tir**. Il faut descendre récursivement dans les
   conteneurs (`sequenceEntries`, `conditionalWeaponActions` + `defaultWeaponAction`,
   `weaponActions`, `weaponAction`).
4. **Munitions sans chute.** 53 des 64 fichiers de munitions FPS ont
   `damageDropParams` ; les 11 autres (armes à énergie) n'en ont pas → portée
   d'efficacité à `null`, à gérer dans l'UI comme « — » et non comme 0.
5. **Capacité hors chargeur.** Un lance-roquettes ne compte pas des balles : l'Animus a
   `maxAmmoCount = 0` et porte ses trois roquettes sur des ports d'objets
   (`missile_01..03` de `SItemPortContainerComponentParams`). Quand `maxAmmoCount` vaut 0,
   il faut prendre le nombre de ports. Un seul chargeur sur 66 est concerné.
6. **Ordonnance explosive : deux dégâts distincts, pas un repli.** Une munition explosive
   inflige des dégâts d'impact **et** des dégâts d'explosion — ce ne sont pas deux sources
   alternatives. Le Boomtube touche à 20 à l'impact et explose à 41 000 ; l'Animus ne fait
   aucun dégât direct et 150 à l'explosion ; le GP-33 fait 5 et 75.

   Piège de lecture du XML : dans `projectileParams`, le bloc `detonationParams` →
   `explosionParams` → `damage` **précède** le `damage` direct. Un `find('<damage')` naïf lit
   donc l'explosion en croyant lire l'impact. C'est ce qui a d'abord fait croire que la roquette
   de l'Animus faisait 150 de dégâts directs.

   Les deux valeurs sont donc stockées séparément (`PrimaryExplosiveDamage`,
   `SecondaryExplosiveDamage`) et affichées « direct (explosif) ».

**Décision retenue** : le tableau affiche **le tir principal ET le tir secondaire**.
Le lien entre un mode de tir et sa munition est explicite dans les données :
`launchParams.projectileType` vaut `Primary` (→ `ammoParamsRecord`) ou `Secondary`
(→ `secondaryAmmoParamsRecord`). Chaque jeu de statistiques est stocké dans son propre
groupe de colonnes (`Primary*` / `Secondary*`), sans table enfant.

Valeurs relevées sur l'Arlington par la sonde :

| | Mode | Munition | Dégâts unitaires | Plombs | Par tir | Vitesse | Portée max |
|---|---|---|---|---|---|---|---|
| Principal | `Slug` | 44 mm | 80 physique | ×1 | **80** | 550 m/s | 1100 m |
| Secondaire | `Single` | shotgun | 12,5 énergie | ×8 | **100** | 300 m/s | 600 m |

Le mode secondaire n'a aucune chute de dégâts (valeurs 0/0/0) : sa portée de dégâts
pleins est `null`, affichée « — ».

### 2.4 Durée de recharge — donnée inexistante

Vérification exhaustive sur les 405 fichiers d'armes FPS : le seul champ contenant
« reload » est `hasReloadModesOnUI` (booléen). La recharge est déclenchée par
l'interaction `SwapAmmoMag` (libellé `@ui_CIFPSReload`) dont la durée est portée
par les animations mannequin (`.dba`/`.adb`), hors DataCore.

**Décision retenue** : pas de colonne « durée de recharge ». On expose à la place
les trois valeurs réelles de `ammoRepoolParams`, correctement libellées
(« Extraction chargeur », « Balles/s », « Fusion chargeur »).

## 3. Périmètre et classification

### 3.1 Le filtrage par tags est piégeux — ne pas l'utiliser

Les tags `<Tags>` sont incomplets : `gmni_sniper_ballistic_01` n'a **pas** le tag
`sniper`, `behr_glauncher_ballistic_01` n'a pas de tag de classe, et
`behr_rifle_ballistic_02_civilian` non plus. Un filtre par tags perdrait
silencieusement de vraies armes.

### 3.2 Classification par nom technique (fiable, couverture 100 %)

Les noms suivent le motif `<fabricant>_<classe>_<munition>_<index>[_<variante>]`.
Le 2ᵉ segment donne la classe sans exception :

| Retenu (armes à feu) | Compte | Exclu (outils/gadgets) | Compte |
|---|---|---|---|
| rifle | 84 | multitool | 23 |
| pistol | 66 | medgun | 9 |
| smg | 60 | binoculars | 5 |
| shotgun | 43 | tractor | 4 |
| sniper | 40 | salvage | 2 |
| lmg | 33 | fire | 2 |
| special (railgun) | 11 | cutter | 1 |
| hmg | 9 | yormandi_weapon | 1 |
| glauncher | 6 | | |
| crossbow | 6 | | |

Filtres cumulés : `EItemType.WeaponPersonal` + classe dans la liste blanche
+ exclusion des 13 fichiers `*_prop.xml` (accessoires décoratifs non fonctionnels).

### 3.3 Regroupement des livrées

405 fichiers pour une cinquantaine d'armes réelles : l'essentiel sont des variantes de couleur
(l'Arlington existe en `tint01/02/03` + le modèle de base). Clé de regroupement principale :
**`AttachDef.mannequinTags.mannequinClassTag`** — vaut `hdgw_rifle_ballistic_01` pour les
quatre variantes de l'Arlington.

⚠️ Une première mesure avait conclu à une couverture de 100 %, mais elle portait sur un
échantillon filtré par les tags de classe — lequel excluait précisément les armes au tag
manquant. La couverture réelle est de 46 armes sur 52 ; voir le repli ci-dessous.

**`mannequinClassTag` n'est pas toujours renseigné.** Les six livrées du P8-AR
(`behr_rifle_ballistic_02_civilian*`) l'ont vide et formaient donc six lignes distinctes au
lieu d'une. Repli retenu : **le plus long jeton de `<Tags>` qui préfixe le nom technique** —
toutes les livrées du P8-AR portent `behr_rifle_ballistic_02_civilian`, l'identité exacte du
modèle. Vérifié sur le corpus : ce repli couvre les 6 cas sans exception, et aucun
`mannequinClassTag` ne fusionne deux familles de nom technique distinctes (les deux cas
`lbco_pistol_energy_cen01` et `lbco_sniper_energy_imp01` sont de vraies livrées).

Résultat mesuré au rebuild : **348 variantes → 47 modèles**. Le compte dépasse l'estimation
initiale de 40 parce que la classification par nom rattrape les armes que les tags manquaient
(lance-grenades, railguns `special`, arbalètes, fusils civils, sniper Gemini).

Représentant du groupe : la variante au nom technique le plus court — le modèle de base, sans
suffixe de livrée — à égalité le premier par ordre alphabétique. Le nombre et les noms des
variantes sont conservés pour l'affichage.

## 4. Profondeur de résolution — risque levé

**Vérifié par la sonde (`--probe-fps` du CLI) : toute la chaîne se résout à `finalDepth: 3`.**
`ammoContainerRecord`, son `SAmmoContainerComponentParams`, `ammoParamsRecord` et
`projectileParams` sont tous non-null. Aucune seconde passe ni résolution à la demande
n'est nécessaire : les armes FPS sont collectées directement dans la boucle existante de
`PopulateScItemsAsync`, sans coût d'énumération supplémentaire.

Ce qui explique que `DamagePhysical` reste vide pour les armes FPS dans `ScItems` :
`BuildScItemEntity` lit `components.OfType<SAmmoContainerComponentParams>()` sur l'arme
elle-même, or pour une arme FPS ce composant est porté par le **chargeur**. L'écran dédié
suit la référence, la table `ScItems` non — comportement inchangé, hors périmètre.

## 5. Découpage de l'implémentation

### Étape 1 — Sonde de faisabilité ✅

`src/StarXelem.Explo` ne contient plus ni projet ni source et n'est plus dans la solution :
la sonde est donc intégrée au CLI existant sous le drapeau `--probe-fps`
(`src/StarXelem.cli.testdb/FpsWeaponProbe.cs`). Elle ouvre le P4K, résout une arme par son
`mannequinClassTag` et imprime toute la chaîne — statut de résolution de chaque maillon,
munitions primaire et secondaire, modes de tir, repool, modificateurs.

```powershell
dotnet run --project src\StarXelem.cli.testdb\StarXelem.cli.testdb.csproj -- --probe-fps
```

Le drapeau court-circuite la reconstruction : la sonde s'exécute en quelques secondes.
Le projet CLI a dû recevoir les références d'assembly `StarBreaker.*` (elles ne transitaient
pas via la référence de projet).

### Étape 2 — Schéma de données ✅

`src/StarXelem/Data/FpsWeaponEntities.cs` — `FpsWeaponEntity`, une ligne par modèle d'arme :

- **Identité** : `Id` (= `mannequinClassTag`), `RecordId`, `TechnicalName`, `LocalizedName`,
  `LocaleNameKey`, `WeaponClass`, `AmmoFamily`, `SubTypeName`, `Size`, `Grade`, `ManufacturerId`
- **Variantes** : `VariantCount`, `VariantNames` (noms séparés par des retours à la ligne,
  destinés à l'infobulle de la ligne)
- **Chargeur** : `MagazineSize`, `RepoolUnstowDuration`, `RepoolBulletsPerSecond`,
  `RepoolFullMagMergeDuration`
- **Tir principal** : `PrimaryModeName`, `PrimaryDamageType`, `PrimaryDamagePerProjectile`,
  `PrimaryDamagePerShot`, `PrimaryExplosiveDamage`, `PrimaryPelletCount`, `PrimaryFireRate`,
  `PrimaryProjectileSpeed`, `PrimaryProjectileLifetime`, `PrimaryMaxRange`,
  `PrimaryEffectiveRange`, `PrimaryDamageFloorRange`
- **Tir secondaire** : `HasSecondaryFire` + le même jeu préfixé `Secondary*`
- **Détail** : `ModifiersJson` (uniquement les modificateurs non neutres ; null sinon)

Tous les champs numériques sont `float?`/`int?` : l'absence de donnée reste distinguable de zéro.

`StarXelemDbContext` : `DbSet<FpsWeaponEntity> FpsWeapons`, index sur `WeaponClass`, relation
`Restrict` vers `ManufacturerEntity`.

**`DatabaseConstants.DatabaseVersion` : 1 → 4** (2 schéma initial, 3 correction du
regroupement et de la capacité, 4 dégâts explosifs) — la base utilise `EnsureCreatedAsync`,
aucune migration EF à générer.

### Étape 3 — Peuplement ✅

Dans `LocalDatabaseService` :

- `TryBuildFpsWeaponVariant` — appelée dans la boucle existante de `PopulateScItemsAsync`,
  retourne null pour tout ce qui n'est pas une arme à feu FPS. Réutilise le nom localisé et
  l'identifiant de fabricant déjà calculés par `BuildScItemEntity` : aucun travail dupliqué.
- `ApplyFpsFireMode` — renseigne le jeu primaire ou secondaire en appariant le mode de tir
  (`projectileType`) à sa munition.
- `ExtractFpsDamage` — somme les six canaux et retient le canal dominant comme type de dégâts.
- `ExtractFpsRanges` — portée de dégâts pleins et distance de plancher, avec `null` (et non 0)
  quand la munition n'a pas de chute de dégâts.
- `ExtractFpsModifiers` — sérialise les seuls modificateurs non neutres.
- `SaveFpsWeaponsAsync` — regroupe par `mannequinClassTag` après le dernier lot d'objets
  (les fabricants référencés sont alors tous insérés), agrège les noms de variantes et
  journalise le décompte.

### Étape 4 — Accès aux données ✅

- `IScItemRepository` ne contient qu'un `GetByCrc32Async` : créer un
  `IFpsWeaponRepository` / `FpsWeaponRepository` dédié, avec
  `Task<List<FpsWeaponEntity>> GetAllAsync()`, et l'enregistrer dans
  `ServiceCollectionExtensions.RegisterServices()`.

### Étape 5 — ViewModel ✅

`src/StarXelem/ViewModels/FpsWeaponsTabViewModel.cs`, calqué sur
`P4kShipTabViewModel` (le modèle le plus proche : lecture base locale + cache
complet + filtres en mémoire) :

- `PageViewModelBase`, chargement dans `OnFirstShowAsync`, `Name` / `Icon` ;
- cache `_allWeapons` (~40 éléments, filtrage en mémoire, aucune requête au filtre) ;
- `[ObservableProperty]` : `NameFilter`, `DamageMin`/`DamageMax`,
  `EffectiveRangeMin`/`EffectiveRangeMax`, `SelectedWeaponClass`, `Weapons`,
  `SelectedWeapon`, `IsLoading` ;
- bornes des curseurs calculées depuis les données chargées
  (`DamageLowerBound`/`UpperBound`, idem portée) et non codées en dur ;
- `ApplyFilters()` déclenché par les `OnXxxChanged` générés, comme
  `ItemsTabViewModel.OnNameFilterChanged` ;
- les armes sans portée d'efficacité (`null`) ne doivent **pas** être éliminées par
  le curseur de portée — prévoir une case « inclure les armes sans portée connue ».

Enregistrements : `services.AddTransient<FpsWeaponsTabViewModel>()` et ajout à la
liste `_pages` de `MainWindowViewModel` (+ clé de navigation `"fpsweapons"` dans le
`switch` ligne ~674). `ViewLocator` fonctionne par réflexion : aucune inscription
manuelle de vue nécessaire.

### Étape 6 — Vue ✅

`src/StarXelem/Views/FpsWeaponsTabView.axaml` (+ code-behind).

**Colonnes empilées plutôt que dédoublées.** Dédoubler chaque statistique en deux colonnes
(principal / secondaire) aurait donné un tableau illisible. Chaque cellule porte donc les deux
valeurs l'une au-dessus de l'autre : le tir principal en 12 px, le tir secondaire en 11 px
atténué, masqué quand l'arme n'a qu'un mode. Hauteur de ligne 52 px (convention : 48–56 px).

**Colonnes retenues** (12) : Nom (+ badge de livrées), Classe, Fabricant, Chargeur, Mode,
Dégâts / tir, Type de dégâts, Cadence, Vitesse projectile, Portée dégâts pleins, Portée max,
Extraction sac à dos.

La colonne « Dégâts / tir » affiche les dégâts directs, suivis entre parenthèses des dégâts
d'explosion quand la munition en a : `80` pour un fusil, `0 (150)` pour l'Animus,
`20 (41000)` pour le Boomtube.

Deux colonnes ont été retirées après revue : **Munition** (famille balistique/énergie de
l'arme — c'est le type de dégâts de la munition qui compte, déjà présent) et **Taille**
(S/G, sans portée pratique sur une arme FPS).

**Indicateur de variantes.** Un badge compact affiche le nombre de livrées regroupées sur la
ligne ; la liste complète des noms apparaît au survol via `ToolTip.Tip` natif d'Avalonia
(section 16 de la convention). Le badge n'apparaît que lorsqu'il y a plus d'une variante.

**Double curseur** : deux `Slider` liés par paire (min / max) avec contrainte croisée dans le
ViewModel et lecture numérique à gauche de chaque curseur. Option *(a)* du plan initial :
aucun `RangeSlider` n'existe dans Avalonia 11 / FluentAvalonia. Un filtre laissé à pleine
amplitude n'écarte personne, y compris les armes dont la donnée est inconnue.

**Piège du `ComboBox` de classe.** Remplacer l'instance de la collection source vide sa
sélection : le champ s'affiche alors vide. La liste est donc une `ObservableCollection`
construite une fois, contenant déjà « Toutes les classes », et complétée en place au
chargement — l'élément sélectionné n'est jamais retiré.

**Couleurs** : uniquement des brosses de thème existantes (`CardBackgroundBrush`,
`CardBorderBrush`, `DimmingBrush`, `DemiDimmingBrush`, brosses de badge) — aucune couleur
nouvelle, parité clair/sombre automatique. Bordures à 0,5 px, rayon de carte 8 px.

### Étape 7 — Vérification

```powershell
Remove-Item "$env:LOCALAPPDATA\StarXelem\database.db" -ErrorAction SilentlyContinue
dotnet run --project src\StarXelem.cli.testdb\StarXelem.cli.testdb.csproj
```

Puis capture d'écran de l'onglet (`--screen fpsweapons`) via le skill
`starxelem-take-screenshot`. La sonde seule, sans reconstruction :

```powershell
dotnet run --project src\StarXelem.cli.testdb\StarXelem.cli.testdb.csproj -- --probe-fps
dotnet run --project src\StarXelem.cli.testdb\StarXelem.cli.testdb.csproj -- "<chemin Data.p4k>" <mannequinClassTag> --probe-fps
```

Le CLI imprime en fin de rebuild le décompte des modèles, la répartition par classe et la
fiche complète de l'Arlington.

Jeu de validation — l'Arlington doit afficher exactement :

| Champ | Attendu |
|---|---|
| Classe / munition | rifle / Ballistic |
| Variantes regroupées | 4 |
| Chargeur | 20 |
| Tir principal | 80 dégâts physiques ×1, cadence 85 |
| Portées principal | pleins 50 m · plancher 500 m · max 1100 m |
| Tir secondaire | 100 dégâts d'énergie (12,5 ×8), max 600 m, portée de dégâts pleins « — » |
| Extraction sac à dos | 1 s · 10 balles/s |

Contrôles globaux attendus : **47 modèles** issus de **348 variantes**, dont **3 à double tir**
(Arlington Rifle 80/100, Killshot Rifle 22/19, Prism Laser Shotgun 46/50). Une seule arme doit
rester sans dégâts : le pistolet-jouet WowBlast, dont les dégâts sont réellement nuls.

## 6. Limites connues et points ouverts

### Écarts entre les fichiers extraits et le P4K courant

`src/StarXelem/datafiles/` est une extraction figée qui ne correspond pas toujours au P4K
installé. Exemple rencontré : la roquette de l'Animus affiche 150 dégâts physiques dans le
fichier extrait, mais 0 dans le P4K courant — ses dégâts réels sont dans l'explosion. **Toute
valeur lue dans `datafiles/` doit être confirmée par la sonde `--probe-fps` avant d'être
tenue pour vraie.**

### Données volontairement absentes

- **Durée de recharge** : inexistante dans DataCore (§2.4). Remplacée par les temps
  d'extraction du sac à dos.
- **Pistolets-jouets** : dégâts réellement nuls dans les données ; la colonne affiche « 0 ».
  Ce n'est pas un défaut d'extraction. Un bloc de dégâts présent mais nul donne « 0 », son
  absence totale donne « — » : la distinction est conservée jusqu'à l'affichage.
- **Armes sans chute de dégâts** : portée de dégâts pleins à « — ». La case « inclure les
  armes sans portée connue » évite qu'elles disparaissent du filtre de portée.

### Points restant à arbitrer

- Les modificateurs (`SWeaponStats`, 23 champs) sont extraits et stockés dans `ModifiersJson`
  mais **ne sont pas encore affichés** : ils attendent un panneau de détail. Sur les armes
  vérifiées ils sont tous neutres, donc la colonne est null presque partout.
- Les noms de modes de tir sont les libellés internes (`Slug`, `Single`, `RapidBeam`) : la clé
  localisée `localisedName` vaut `@FireMode_Single` pour les deux modes de l'Arlington et ne
  permet donc pas de les distinguer.
- `overrideDisplayStats` (8 `Single`, une seule valeur renseignée à 59 sur l'Arlington) reste
  non interprété.
- Le double curseur utilise deux `Slider` liés. Un vrai `RangeSlider` dans `Components/`
  reste possible si l'ergonomie ne convient pas.

---

## 7. État de livraison

Toutes les étapes sont réalisées et vérifiées sur la base reconstruite depuis le P4K
`sc-alpha-4.10.0` : 47 modèles chargés dans l'écran, valeurs de l'Arlington conformes au jeu
de validation, capture d'écran contrôlée.

Fichiers ajoutés :

- `src/StarXelem/Data/FpsWeaponEntities.cs`, `IFpsWeaponRepository.cs`, `FpsWeaponRepository.cs`
- `src/StarXelem/Models/FpsWeaponModel.cs`
- `src/StarXelem/ViewModels/FpsWeaponsTabViewModel.cs`
- `src/StarXelem/Views/FpsWeaponsTabView.axaml` (+ code-behind)
- `src/StarXelem.cli.testdb/FpsWeaponProbe.cs`

Fichiers modifiés : `StarXelemDbContext.cs`, `DatabaseConstants.cs`, `LocalDatabaseService.cs`,
`ServiceCollectionExtensions.cs`, `MainWindowViewModel.cs`, `DesignData.cs`,
`StarXelem.cli.testdb/Program.cs` et son `.csproj`.
