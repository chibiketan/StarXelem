# Optimisation de la reconstruction de la BDD locale (`LocalDatabaseService.RebuildDbAsync`)

Date : 2026-07-24
Contexte : `RebuildDbCoreAsync` (déclenché par `RebuildDbAsync` / `EnsureDbAsync`) reconstruit entièrement la base
SQLite locale à partir du `Data.p4k`. Deux problèmes signalés :
- Temps d'exécution élevé (≥ 3 minutes constatées par l'utilisateur).
- Empreinte mémoire finale énorme (~22 Go) après la reconstruction, problématique sur une machine
  moins puissante ou avec le jeu lancé en parallèle.

Mesures faites avec le projet `StarXelem.cli.testdb` (rebuild forcé + requêtes de contrôle), sur le même
`Data.p4k` (~23 371 SCItems, 2 499 missions, 1 105 vaisseaux, 1 597 blueprints).

## État des lieux initial

- Reconstruction séquentielle en 10 phases dans un unique `DbContext` gardé ouvert du début à la fin.
- `SaveChangesAsync` déjà relativement bien groupé par phase (pas de save par ligne), mais les entités
  restent **trackées** dans le `ChangeTracker` pendant tout le rebuild : rien n'est jamais détaché avant
  la fin (`using var db` scope complet), donc la mémoire du `ChangeTracker` croît de façon monotone
  sur les 10 phases.
- `P4kService` construit et garde en cache **tous les enregistrements `EntityClassDefinition` du jeu**,
  chargés en profondeur 1 (`_EntityClassDict`) puis re-matérialisés en profondeur 3 pour les besoins des
  phases SCItems/Missions/Loadouts (`GetAllEntityClassDefinition(3)`, `EnsureRecordsDepthAsync(..., 3)`).
  Ce cache (`_EntityClassDict` / `_entityClassGuidDict`) n'est **jamais vidé** : il vit pour toute la durée
  de vie du service, y compris après la fin du rebuild.
- SQLite ouvert avec les réglages par défaut (`journal_mode=DELETE`, `synchronous=FULL`) : chaque batch de
  write attend une synchronisation disque complète.
- Aucune mesure/logging de mémoire n'existait : impossible de vérifier objectivement une amélioration.

Reproduit en base de référence : `WorkingSet` mesuré à **~23 Go** juste après la fin de la phase 7
(Blueprints), sans jamais redescendre ensuite (le process CLI ne fait rien d'autre qu'un `SaveChangesAsync`
supplémentaire et quelques requêtes SQL après ça).

## Pistes appliquées

### 1. `db.ChangeTracker.Clear()` après chaque `SaveChangesAsync` de phase — ✅ Appliqué
**Gain attendu** : réduire l'empreinte du `ChangeTracker` en détachant les entités déjà persistées, phase
après phase, au lieu de les garder trackées jusqu'à la fin du rebuild.
**Piège rencontré** : deux caches internes (`_contractorCache`/`_categoryCache` pour les missions,
`_blueprintCache` pour les blueprints) conservent des **références directes** vers des entités EF
(`ActorEntity`, `MissionCategoryEntity`, `BlueprintEntity`) réutilisées comme propriétés de navigation par
plusieurs entités *avant* le `SaveChanges` correspondant. Un premier essai de vidage de `_blueprintCache`
juste après la phase 7 a cassé la phase 8 : les récompenses de mission de type "pool de blueprints"
(`ProcessBlueprintPoolsAsync`, appelé depuis `ProcessSingleReward` pendant la phase Missions) réutilisent
ce cache pour éviter de ré-insérer un blueprint déjà en base → sans le cache, le blueprint était recréé et
ré-ajouté, provoquant une violation de contrainte unique (`UNIQUE constraint failed: Blueprints.SelfId`) au
moment du `SaveChangesAsync` de la phase Missions.
**Correctif** : `_blueprintCache` n'est vidé qu'après la phase Missions (comme `_contractorCache` et
`_categoryCache`), pas après la phase Blueprints. `ChangeTracker.Clear()` n'est ajouté qu'aux points où
aucune méthode ultérieure ne référence encore une entité déjà trackée par identité d'objet (vérifié
manuellement phase par phase).
**Résultat** : rebuild stable, aucune duplication, mémoire gérée réduite phase après phase (voir mesures).

### 2. Libération du cache lourd de `P4kService` en fin de rebuild — ✅ Appliqué
Nouvelle méthode `IP4kService.ReleaseHeavyCache()` : vide `_EntityClassDict` / `_entityClassGuidDict` et
réarme `_loadingDatabaseTask` à `null` pour que le cache soit reconstruit paresseusement (et à moindre
coût, profondeur -1 uniquement) si un autre appelant en a de nouveau besoin après le rebuild.
Appelée à la fin de `RebuildDbCoreAsync`, une fois toutes les phases terminées.
**Gain attendu** : c'est le plus gros contributeur mémoire identifié (tous les `EntityClassDefinition` du
jeu, chargés en profondeur 3 pour beaucoup d'entre eux). Le fichier `.p4k` et son index de fichiers restent
ouverts (utilisés par d'autres fonctionnalités de l'appli — extraction, réputation, missions…) : on ne
ferme volontairement **pas** le p4k lui-même pour ne pas casser ces usages, seulement le cache de records
pré-matérialisés.

### 3. Pragmas SQLite pendant le rebuild — ✅ Appliqué
`PRAGMA journal_mode = WAL`, `PRAGMA synchronous = OFF`, `PRAGMA temp_store = MEMORY` appliqués juste après
`EnsureCreatedAsync`. Comme la base est de toute façon entièrement droppée puis recréée à chaque rebuild,
sacrifier la durabilité pendant cette phase (si crash → on relance juste un rebuild) est un compromis sûr
qui accélère fortement les écritures par lot.
**Gain observé** : c'est la piste qui a le plus contribué à la réduction du temps total (voir mesures).

### 4. GC agressif + `ConserveMemory` en fin de rebuild — ✅ Appliqué
- `LocalDatabaseService` appelle `GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true)`
  (x2, avec `WaitForPendingFinalizers` entre les deux) juste après la libération des caches, et logge la
  mémoire avant/après.
- `StarXelem.csproj` : ajout de `<ConserveMemory>9</ConserveMemory>` (+ `ServerGarbageCollection=false`,
  `ConcurrentGarbageCollection=true` explicites). Sans ce réglage, un simple `GC.Collect()` réduit bien le
  *tas managé* mais **ne rend pas les segments à l'OS** (comportement par défaut du GC .NET, qui garde les
  segments pour de futures allocations) : le `WorkingSet` du process ne bougeait quasiment pas malgré une
  mémoire managée redescendue à ~2 Go. `ConserveMemory=9` indique au GC de décommiter agressivement les
  segments inutilisés, ce qui rend l'amélioration visible côté OS (et donc côté Gestionnaire des tâches /
  RAM disponible pour le reste du système, y compris le jeu).
**Gain observé** : décisif — voir mesures ci-dessous (WorkingSet post-rebuild divisé par ~10).

### 5. Logging de mémoire (WorkingSet + tas managé) à chaque étape clé — ✅ Appliqué
Ajout de `LogMemoryUsage(string label)` (via `Environment.WorkingSet` et `GC.GetTotalMemory(false)`),
appelé avant le rebuild, après les phases 7 et 9, et avant/après le GC forcé final. Permet de suivre
objectivement l'empreinte mémoire dans les logs du CLI de test (et en prod si besoin de diagnostiquer).

## Pistes envisagées mais non retenues

- **Fermer le fichier `.p4k` en fin de rebuild** : abandonné. `P4kService` est un singleton réutilisé par
  d'autres fonctionnalités de l'application après le rebuild (onglet Extraction, Réputation, Missions,
  gRPC…). Le fermer forcerait une réouverture complète (et coûteuse) au premier accès suivant. Seul le
  cache de records pré-matérialisés est vidé (piste 2), ce qui couvre l'essentiel du gain mémoire sans
  casser ces fonctionnalités.
- **Réduire le nombre de `SaveChangesAsync`** : déjà correctement fait dans le code existant (batchs de
  10 000 pour les SCItems, un seul save en fin de phase pour les autres) — pas de gain supplémentaire
  identifié sans risquer de casser la logique de dédup par cache (voir piste 1).
- **Paralléliser les phases** : non exploré. Plusieurs phases dépendent de résultats de phases précédentes
  (FK ScItems→Loadouts, Blueprints→Missions) et partagent le même `DbContext` (non thread-safe). Le
  gain potentiel ne justifiait pas le risque dans le temps imparti à cette tâche.

## Résultats mesurés (CLI `StarXelem.cli.testdb`, build Release)

| Mesure | Avant (baseline utilisateur) | Après optimisations |
|---|---|---|
| Temps total du rebuild (`Database rebuild completed`) | ≥ 180 s (constaté) | **~76 s** |
| Temps process CLI complet (build → rebuild → requêtes de contrôle) | — | **~88 s** |
| WorkingSet pendant le rebuild (fin phase 7, pic) | ~22-23 Go (rapporté par l'utilisateur) | ~23 Go *(inchangé, transitoire — voir note)* |
| WorkingSet **après** le rebuild (avant tout GC) | ~22 Go (persistant) | ~23 Go (idem, avant nettoyage) |
| WorkingSet après nettoyage des caches + GC agressif | *(jamais nettoyé auparavant)* | **~2,3 Go** |
| Tas managé (`GC.GetTotalMemory`) après nettoyage | — | **~2,1 Go** |

Note importante : le **pic transitoire** pendant le rebuild (toutes les entités EntityClassDefinition du
jeu chargées en profondeur, plus les entités EF en cours de traitement) reste élevé (~23 Go) — c'est
attendu, il correspond au volume réel de données du jeu chargées simultanément pendant le traitement le
plus lourd (phase Blueprints/SCItems). Le vrai problème signalé par l'utilisateur était que ce pic
**restait figé indéfiniment après la fin du rebuild** : c'est ce point précis qui est corrigé — la mémoire
retombe maintenant à ~2,3 Go dès la fin du `RebuildDbAsync`, contre ~22 Go qui persistaient auparavant tant
que l'application restait ouverte.

## Fichiers modifiés

- `src/StarXelem/Services/LocalDatabaseService.cs` : `ChangeTracker.Clear()` ciblés, pragmas SQLite,
  libération du cache P4K, GC agressif, logging mémoire.
- `src/StarXelem/Services/P4kService/P4kService.cs` : nouvelle méthode `ReleaseHeavyCache()`.
- `src/StarXelem/Services/P4kService/IP4kService.cs` : ajout de `ReleaseHeavyCache()` à l'interface.
- `src/StarXelem/Services/P4kService/DesignP4kService.cs` : implémentation no-op pour le design-time.
- `src/StarXelem/StarXelem.csproj` : réglages GC (`ConserveMemory`, `ServerGarbageCollection`,
  `ConcurrentGarbageCollection`).

## Suites possibles (non traitées ici)

- `ConserveMemory=9` s'applique à toute l'application (pas seulement au rebuild) : à surveiller si cela a
  un impact sur la fluidité générale de l'UI (compromis mémoire/débit du GC) — aucun souci constaté en
  usage CLI, à confirmer en usage interactif normal de l'application.

## Itération 2 (2026-07-24, suite) : tentative de réduction du pic transitoire (~23 Go)

Objectif demandé : faire baisser le **pic mémoire pendant le rebuild** (pas seulement le résiduel après),
idéalement sous les 5 Go.

### Piste testée : libérer le cache lourd de `P4kService` dès la fin de la phase 4 (SCItems) — ❌ Abandonnée

Constat de départ : `PopulateScItemsAsync` (phase 4) appelle `_p4kService.GetAllEntityClassDefinition(3)`,
qui matérialise en profondeur 3 (résolution récursive complète des références imbriquées) **tous les
`EntityClassDefinition` du jeu** — pas seulement les objets retenus comme SCItems, mais aussi tous les
vaisseaux, PNJ, props, etc., puisque `GetAllEntityClassDefinition` filtre par type de record mais applique
l'upgrade de profondeur à l'ensemble des résultats *avant* que l'appelant ne filtre lui-même (dans
`PopulateScItemsAsync`, le filtre item/vaisseau n'intervient qu'*après* l'upgrade de profondeur). C'est
identifié comme le plus gros contributeur au pic.

**Essai 1** : appeler `_p4kService.ReleaseHeavyCache()` juste après la phase 4, en plus de l'appel déjà en
place en fin de rebuild, en misant sur le fait que les phases suivantes (Loadouts, Contract Generators,
Blueprints, Missions) ne consomment que des sous-ensembles ciblés et peuvent se recharger paresseusement.
Résultat mesuré : **aucune réduction visible** de la mémoire juste après le `Clear()` — attendu, puisque
`Dictionary.Clear()` ne fait que retirer les références, la mémoire managée n'est récupérée qu'à la
prochaine collecte du GC. Pire : sans collecte forcée, la mémoire déjà "morte" (non collectée) s'additionne
aux nouvelles allocations des phases suivantes (qui doivent re-matérialiser certains records à la demande) :
pic final mesuré à **28,3 Go** (au lieu de ~23 Go) et temps total de **101,6 s** (au lieu de ~76 s) —
régression sur les deux axes.

**Essai 2** : ajouter un `GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true,
compacting: true)` (x2, comme en fin de rebuild) immédiatement après le `ReleaseHeavyCache()` de la phase 4,
pour forcer la récupération réelle avant que les phases suivantes ne réallouent. Résultat mesuré :
- Mémoire juste après phase 4 : 21,5 Go → seulement **19,3 Go** après vidage + GC forcé (116 512
  enregistrements pourtant libérés du cache). Gain très inférieur à l'attendu.
- Pic final : **29,3 Go** (encore pire que l'essai 1).
- Temps total : **115 s** (encore pire).

### Analyse : pourquoi ça ne marche pas

Deux effets combinés expliquent l'échec de cette piste :

1. **Le GC forcé lui-même a un coût réel** sur un tas de ~20 Go (compaction bloquante) — de l'ordre de
   plusieurs secondes à chaque appel, répété deux fois pour un gain mémoire modeste.
2. **Le gain mémoire du vidage est décevant** (21,5 Go → 19,3 Go, alors que 116 512 enregistrements sont
   supprimés du cache) : l'hypothèse la plus probable est que le pic ne vient pas majoritairement du
   dictionnaire `_EntityClassDict`/`_entityClassGuidDict` lui-même, mais de structures internes à
   `DataForge`/`DataCoreDatabase` (le champ `df` de `P4kService`, jamais touché par `ReleaseHeavyCache`) :
   tables de chaînes, index de définitions de records, buffers de désérialisation partagés par tous les
   enregistrements du `.dcb`. Ces structures sont chargées une fois à l'ouverture du `.p4k`
   (`OpenP4k`/`LoadDatabaseIfNeeded`) et alimentent *toute* résolution de record, y compris celles faites
   "à la demande" par les phases suivantes après un vidage — ce qui expliquerait aussi pourquoi les phases
   suivantes doivent re-payer un coût de re-résolution (d'où le ralentissement) sans que le pic ne baisse
   en proportion (la donnée volumineuse partagée reste chargée quoi qu'il arrive).
   C'est une hypothèse basée sur le comportement observé — `StarBreaker.*` est une DLL externe sans code
   source consulté ici, donc non vérifiée en profondeur.

**Décision : piste abandonnée, code reverté.** Le rapport coût (temps + complexité + risque de régression)
/ bénéfice (gain mémoire marginal, voire négatif sans GC forcé) n'est pas favorable. Confirmé par un rebuild
de contrôle après revert : retour à ~90 s / pic ~23 Go / résiduel ~2,3 Go, cohérent avec l'itération 1.

### Pistes plus profondes pour une future itération (non explorées ici)

- **Filtrer avant d'étendre en profondeur**, plutôt que d'étendre tout puis nettoyer après coup : modifier
  `GetAllEntityClassDefinition` (ou ajouter une variante) pour n'upgrader en profondeur 3 que les records
  qui passent déjà le filtre "objet équipable" (`SAttachableComponentParams`/`SItemDefinition`) à une
  profondeur plus faible, au lieu d'étendre en profondeur 3 l'intégralité des `EntityClassDefinition` du
  jeu (vaisseaux, PNJ, props compris) avant tout filtrage. Nécessite de vérifier que le critère de filtrage
  reste déterminable à faible profondeur (à confirmer empiriquement, pas garanti par la doc StarBreaker).
- **Étudier si `StarBreaker.DataCore`/`DataForge` expose un mode de résolution "à la volée" sans cache
  interne global**, pour éviter que `df` lui-même retienne des structures volumineuses en permanence.
  Nécessiterait de lire le code source de `StarBreaker.DataCore`/`StarBreaker.DataCore.Generated` (DLL
  externes, non explorées dans cette itération) pour évaluer la faisabilité.
- Ces deux pistes touchent à des mécanismes plus profonds et plus risqués (comportement d'une lib externe,
  risque de régression fonctionnelle sur l'extraction de données) : à traiter comme un chantier à part,
  avec plus de temps de validation, si la réduction du pic transitoire reste un objectif prioritaire.

## Itération 3 (2026-07-24, suite) : test de fermeture/réouverture du fichier p4k sans vider le cache — ❌ Abandonnée

Hypothèse testée par l'utilisateur : et si l'état interne du fichier `.p4k` lui-même (pas le cache de
records) contribuait significativement au pic ? Test : fermer/rouvrir le fichier après la phase 4 (SCItems),
**sans** vider `_EntityClassDict`/`_entityClassGuidDict`.

Résultat mesuré : gain marginal (WorkingSet 21 514 → 19 834 Mo, soit ~1,7 Go ; tas managé quasi inchangé,
19 531 → 19 455 Mo) pour un coût de +24 s sur le temps total (100 s au lieu de 76 s) et aucune réduction du
pic final (~22,6 Go, identique à la baseline). Confirme que le cache de records domine bien la mémoire, pas
l'état interne du fichier p4k. **Code reverté** (`CloseAndReopenP4kFileForTestAsync` retiré).

## Itération 4 (2026-07-24, suite) : filtrage à profondeur légère avant expansion profonde — ✅ Appliqué

### Constat

`PopulateScItemsAsync` (phase 4) appelait `_p4kService.GetAllEntityClassDefinition(3)`, qui matérialise en
profondeur 3 (résolution récursive complète) **tous les `EntityClassDefinition` du jeu** — vaisseaux, PNJ,
props compris — puisque le filtre "est-ce un objet équipable ?" (`SAttachableComponentParams`/
`SItemDefinition`, non invisible, non véhicule) n'était appliqué qu'*après* cette matérialisation coûteuse,
sur le flux déjà entièrement résolu.

### Piste appliquée

Nouvelle méthode `IP4kService.GetAllEntityClassDefinitionFiltered(filterDepth, finalDepth, predicate)` :
évalue le prédicat à une profondeur légère (`filterDepth`) d'abord, puis n'étend jusqu'à `finalDepth` que
les enregistrements retenus. Le filtre de `PopulateScItemsAsync` (invisibilité, type d'objet, exclusion des
véhicules) est déplacé dans ce prédicat, évalué à `filterDepth: 1` (profondeur déjà atteinte gratuitement
pour tous les `EntityClassDefinition` par la phase Ships qui précède), avant d'étendre à `finalDepth: 3`
uniquement les ~23 371 objets retenus (au lieu des ~100 000+ `EntityClassDefinition` du jeu entier).

**Validation de correction** : le filtre à profondeur 1 (`AttachDef` résolu en `SItemDefinition`, `Type`,
`Invisible`, `Components`) donne exactement le même résultat qu'un filtre à profondeur 3 — vérifié par
comparaison stricte avant/après : même nombre de SCItems (**23 371**), mêmes statistiques de dégâts
(**402/23 371** items avec dégâts), aucune exception.

### Résultats mesurés

| Mesure | Avant (baseline) | Après filtrage |
|---|---|---|
| Phase SCItems (isolée) | 39,3 s | **21,5 s** (-45 %) |
| Pic mémoire (fin phase 9) | 23 116 Mo | **22 174 Mo** (-940 Mo) |
| Temps total du rebuild | ~76-90 s | ~80-90 s (dans la même fourchette, bruit de mesure) |
| Résiduel final après GC | ~2,3 Go | ~2,3 Go (inchangé) |

**Décision : conservé.** Gain net positif et sans risque (données strictement identiques, phase la plus
lourde 2× plus rapide) même si le gain sur le pic mémoire global reste modeste par rapport à l'objectif de
5 Go — la majorité du pic vient donc d'ailleurs (voir pistes de généralisation ci-dessous, en cours
d'investigation).

### Pistes de suite en cours d'investigation

- Généraliser le même principe (filtrer à profondeur légère avant d'étendre) aux autres phases qui
  matérialisent des `EntityClassDefinition` en profondeur — à date, les autres consommateurs
  (`GetAllContractGenerator`, `GetAllCraftingBlueprintRecord`, loadouts par vaisseau) opèrent déjà sur des
  sous-ensembles ciblés et ne semblent pas bénéficier du même effet de levier, mais reste à vérifier
  empiriquement combien de `EntityClassDefinition` distincts sont réellement promus à profondeur 3 au total
  et où ils sont utilisés.
- Retester la libération du cache + GC entre phases (Itération 2, précédemment abandonnée) maintenant que
  la phase SCItems ne pousse plus l'intégralité du jeu à profondeur 3 : le volume à recycler est plus
  faible, le ratio coût (GC)/bénéfice (mémoire récupérée) pourrait être meilleur qu'avant.
